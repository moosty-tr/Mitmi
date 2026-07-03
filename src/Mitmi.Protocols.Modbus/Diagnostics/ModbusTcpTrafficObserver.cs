using System.Globalization;
using Mitmi.Application.Sessions;
using Mitmi.Domain;
using Mitmi.Protocols.Modbus.Framing;

namespace Mitmi.Protocols.Modbus.Diagnostics;

public sealed class ModbusTcpTrafficObserver : IProtocolTrafficObserver
{
    private readonly ModbusTcpFrameDecoder clientToServerDecoder = new();
    private readonly ISessionEventSink eventSink;
    private readonly ModbusTcpFrameDecoder serverToClientDecoder = new();
    private readonly SemaphoreSlim observationLock = new(1, 1);
    private readonly ModbusTcpTransactionCorrelator transactionCorrelator = new();
    private readonly bool captureRawPayloads;
    private readonly ITrafficCaptureSink? trafficCaptureSink;

    public ModbusTcpTrafficObserver(
        ISessionEventSink eventSink,
        ITrafficCaptureSink? trafficCaptureSink = null,
        bool captureRawPayloads = false)
    {
        this.eventSink = eventSink;
        this.trafficCaptureSink = trafficCaptureSink;
        this.captureRawPayloads = captureRawPayloads;
    }

    public async ValueTask ObserveAsync(
        ProtocolTrafficObservation observation,
        CancellationToken cancellationToken)
    {
        await observationLock.WaitAsync(cancellationToken);
        try
        {
            var decoder = observation.Direction == TrafficDirection.ClientToServer
                ? clientToServerDecoder
                : serverToClientDecoder;

            var frameDirection = observation.Direction == TrafficDirection.ClientToServer
                ? ModbusTcpFrameDirection.ClientToServer
                : ModbusTcpFrameDirection.ServerToClient;

            var decodeResult = decoder.Append(observation.Payload.Span, frameDirection);
            foreach (var warning in decodeResult.Warnings)
            {
                await EmitWarningAsync(
                    observation,
                    warning,
                    SessionEventNames.ProtocolDecodeWarning,
                    cancellationToken);
            }

            foreach (var frame in decodeResult.Frames)
            {
                await EmitFrameDecodedAsync(observation, frame, cancellationToken);

                foreach (var warning in frame.DecodeWarnings)
                {
                    await EmitWarningAsync(
                        observation,
                        warning,
                        SessionEventNames.ProtocolDecodeWarning,
                        cancellationToken);
                }

                var transactionEvent = transactionCorrelator.Observe(frame);
                await EmitTransactionEventAsync(observation, transactionEvent, cancellationToken);
                await CaptureFrameAsync(observation, frame, transactionEvent, cancellationToken);
            }
        }
        finally
        {
            observationLock.Release();
        }
    }

    private async ValueTask CaptureFrameAsync(
        ProtocolTrafficObservation observation,
        ModbusTcpFrame frame,
        ModbusTcpTransactionEvent transactionEvent,
        CancellationToken cancellationToken)
    {
        if (trafficCaptureSink is null)
        {
            return;
        }

        try
        {
            await trafficCaptureSink.CaptureAsync(
                new TrafficCaptureRecord(
                    DateTimeOffset.UtcNow,
                    observation.SessionId,
                    observation.ConnectionId,
                    new ProtocolId(ModbusTcpProtocolPlugin.ProtocolId),
                    observation.Direction,
                    frame.RawFrame.Length,
                    captureRawPayloads ? frame.RawFrame : null,
                    transactionEvent.CorrelationId,
                    BuildProtocolMetadata(frame, transactionEvent),
                    BuildDecodeWarnings(frame, transactionEvent),
                    TrafficCaptureRecordKind.ProtocolFrame),
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException ||
            !cancellationToken.IsCancellationRequested)
        {
            await eventSink.EmitAsync(
                new SessionEvent(
                    DateTimeOffset.UtcNow,
                    SessionEventLevel.Warning,
                    SessionEventNames.CaptureSinkFailed,
                    observation.SessionId,
                    observation.ConnectionId,
                    $"Capture failed while recording decoded Modbus frame: {exception.Message}",
                    exception),
                cancellationToken);
        }
    }

    private ValueTask EmitFrameDecodedAsync(
        ProtocolTrafficObservation observation,
        ModbusTcpFrame frame,
        CancellationToken cancellationToken)
    {
        return eventSink.EmitAsync(
            new SessionEvent(
                DateTimeOffset.UtcNow,
                SessionEventLevel.Info,
                SessionEventNames.ProtocolFrameDecoded,
                observation.SessionId,
                observation.ConnectionId,
                $"Decoded Modbus TCP {frame.Direction} frame tx={frame.TransactionId} unit={frame.UnitId} function={FormatFunction(frame)} length={frame.RawFrame.Length}."),
            cancellationToken);
    }

    private async ValueTask EmitTransactionEventAsync(
        ProtocolTrafficObservation observation,
        ModbusTcpTransactionEvent transactionEvent,
        CancellationToken cancellationToken)
    {
        var eventName = transactionEvent.Kind switch
        {
            ModbusTcpTransactionEventKind.RequestObserved => SessionEventNames.ProtocolTransactionObserved,
            ModbusTcpTransactionEventKind.ResponseAwaitingRequest => SessionEventNames.ProtocolTransactionObserved,
            ModbusTcpTransactionEventKind.ResponseMatched => SessionEventNames.ProtocolTransactionMatched,
            ModbusTcpTransactionEventKind.ResponseWithoutRequest => SessionEventNames.ProtocolTransactionWarning,
            _ => SessionEventNames.ProtocolTransactionWarning
        };

        var level = transactionEvent.Kind == ModbusTcpTransactionEventKind.ResponseWithoutRequest
            ? SessionEventLevel.Warning
            : SessionEventLevel.Info;

        await eventSink.EmitAsync(
            new SessionEvent(
                DateTimeOffset.UtcNow,
                level,
                eventName,
                observation.SessionId,
                observation.ConnectionId,
                RenderTransactionMessage(transactionEvent)),
            cancellationToken);

        foreach (var warning in transactionEvent.Warnings)
        {
            await EmitWarningAsync(
                observation,
                warning,
                SessionEventNames.ProtocolTransactionWarning,
                cancellationToken);
        }
    }

    private static string RenderTransactionMessage(ModbusTcpTransactionEvent transactionEvent)
    {
        return transactionEvent.Kind switch
        {
            ModbusTcpTransactionEventKind.RequestObserved =>
                $"Observed Modbus request correlation={transactionEvent.CorrelationId} function={FormatFunction(transactionEvent.Frame)}.",
            ModbusTcpTransactionEventKind.ResponseAwaitingRequest =>
                $"Observed Modbus response before matching request diagnostics correlation={transactionEvent.CorrelationId} function={FormatFunction(transactionEvent.Frame)}.",
            ModbusTcpTransactionEventKind.ResponseMatched =>
                $"Matched Modbus response correlation={transactionEvent.CorrelationId} function={FormatFunction(transactionEvent.Frame)} exception={transactionEvent.Frame.IsExceptionResponse}.",
            ModbusTcpTransactionEventKind.ResponseWithoutRequest =>
                $"Observed Modbus response without pending request correlation={transactionEvent.CorrelationId} function={FormatFunction(transactionEvent.Frame)}.",
            _ =>
                $"Observed Modbus transaction event correlation={transactionEvent.CorrelationId}."
        };
    }

    private ValueTask EmitWarningAsync(
        ProtocolTrafficObservation observation,
        ModbusTcpDecodeWarning warning,
        string eventName,
        CancellationToken cancellationToken)
    {
        return eventSink.EmitAsync(
            new SessionEvent(
                DateTimeOffset.UtcNow,
                SessionEventLevel.Warning,
                eventName,
                observation.SessionId,
                observation.ConnectionId,
                $"{warning.Code}: {warning.Message}"),
            cancellationToken);
    }

    private static string FormatFunction(ModbusTcpFrame frame)
    {
        return frame.IsExceptionResponse
            ? $"{frame.OperationFunctionCode} exceptionCode={frame.ExceptionCode}"
            : frame.FunctionCode.ToString();
    }

    private static IReadOnlyDictionary<string, string> BuildProtocolMetadata(
        ModbusTcpFrame frame,
        ModbusTcpTransactionEvent transactionEvent)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["transactionId"] = frame.TransactionId.ToString(CultureInfo.InvariantCulture),
            ["mbapProtocolId"] = frame.ProtocolId.ToString(CultureInfo.InvariantCulture),
            ["length"] = frame.Length.ToString(CultureInfo.InvariantCulture),
            ["unitId"] = frame.UnitId.ToString(CultureInfo.InvariantCulture),
            ["functionCode"] = frame.FunctionCode.ToString(CultureInfo.InvariantCulture),
            ["operationFunctionCode"] = frame.OperationFunctionCode.ToString(CultureInfo.InvariantCulture),
            ["isExceptionResponse"] = frame.IsExceptionResponse ? "true" : "false",
            ["transactionEventKind"] = FormatTransactionEventKind(transactionEvent.Kind)
        };

        if (frame.ExceptionCode is { } exceptionCode)
        {
            metadata["exceptionCode"] = exceptionCode.ToString(CultureInfo.InvariantCulture);
        }

        return metadata;
    }

    private static IReadOnlyList<TrafficCaptureWarning>? BuildDecodeWarnings(
        ModbusTcpFrame frame,
        ModbusTcpTransactionEvent transactionEvent)
    {
        var warnings = frame.DecodeWarnings
            .Concat(transactionEvent.Warnings)
            .Select(warning => new TrafficCaptureWarning(warning.Code, warning.Message))
            .ToArray();

        return warnings.Length == 0 ? null : warnings;
    }

    private static string FormatTransactionEventKind(ModbusTcpTransactionEventKind kind)
    {
        return kind switch
        {
            ModbusTcpTransactionEventKind.RequestObserved => "requestObserved",
            ModbusTcpTransactionEventKind.ResponseAwaitingRequest => "responseAwaitingRequest",
            ModbusTcpTransactionEventKind.ResponseMatched => "responseMatched",
            ModbusTcpTransactionEventKind.ResponseWithoutRequest => "responseWithoutRequest",
            _ => kind.ToString()
        };
    }
}
