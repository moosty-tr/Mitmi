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
    private readonly ModbusTcpAnalyzerSessionSummary? analyzerSessionSummary;
    private readonly NetworkEndpoint? upstreamEndpoint;
    private readonly ModbusObservedValueState? observedValueState;
    private readonly IModbusObservedValueUpdateSink? observedValueSink;

    public ModbusTcpTrafficObserver(
        ISessionEventSink eventSink,
        ITrafficCaptureSink? trafficCaptureSink = null,
        bool captureRawPayloads = false,
        ModbusTcpAnalyzerSessionSummary? analyzerSessionSummary = null,
        NetworkEndpoint? upstreamEndpoint = null,
        ModbusObservedValueState? observedValueState = null,
        IModbusObservedValueUpdateSink? observedValueSink = null)
    {
        this.eventSink = eventSink;
        this.trafficCaptureSink = trafficCaptureSink;
        this.captureRawPayloads = captureRawPayloads;
        this.analyzerSessionSummary = analyzerSessionSummary;
        this.upstreamEndpoint = upstreamEndpoint;
        this.observedValueState = observedValueState;
        this.observedValueSink = observedValueSink;
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
                var frameAnalysis = ModbusTcpPduAnalyzer.Analyze(frame, transactionEvent.RequestFrame);
                var transactionAnalysis = ModbusTcpPduAnalyzer.Analyze(
                    transactionEvent.Frame,
                    transactionEvent.RequestFrame);
                analyzerSessionSummary?.Observe(frame, frameAnalysis);

                await EmitTransactionEventAsync(
                    observation,
                    transactionEvent,
                    transactionAnalysis,
                    cancellationToken);
                await EmitObservedValueUpdateAsync(
                    observation,
                    transactionEvent,
                    cancellationToken);
                await CaptureFrameAsync(
                    observation,
                    frame,
                    transactionEvent,
                    frameAnalysis,
                    cancellationToken);
            }
        }
        finally
        {
            observationLock.Release();
        }
    }

    private async ValueTask EmitObservedValueUpdateAsync(
        ProtocolTrafficObservation observation,
        ModbusTcpTransactionEvent transactionEvent,
        CancellationToken cancellationToken)
    {
        if (observedValueState is null ||
            observedValueSink is null ||
            upstreamEndpoint is null)
        {
            return;
        }

        var updateGroup = observedValueState.ObserveMatchedTransaction(
            observation.SessionId,
            upstreamEndpoint,
            transactionEvent,
            DateTimeOffset.UtcNow);

        if (updateGroup is null)
        {
            return;
        }

        try
        {
            await observedValueSink.EmitAsync(updateGroup, cancellationToken);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException ||
            !cancellationToken.IsCancellationRequested)
        {
            await eventSink.EmitAsync(
                new SessionEvent(
                    DateTimeOffset.UtcNow,
                    SessionEventLevel.Warning,
                    SessionEventNames.ProtocolObserverFailed,
                    observation.SessionId,
                    observation.ConnectionId,
                    $"Observed-value update delivery failed while processing Modbus diagnostics: {exception.Message}",
                    exception),
                cancellationToken);
        }
    }

    private async ValueTask CaptureFrameAsync(
        ProtocolTrafficObservation observation,
        ModbusTcpFrame frame,
        ModbusTcpTransactionEvent transactionEvent,
        ModbusTcpPduAnalysis analysis,
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
                    BuildProtocolMetadata(frame, transactionEvent, analysis),
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
        ModbusTcpPduAnalysis analysis,
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
                RenderTransactionMessage(transactionEvent, analysis)),
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

    private static string RenderTransactionMessage(
        ModbusTcpTransactionEvent transactionEvent,
        ModbusTcpPduAnalysis analysis)
    {
        return transactionEvent.Kind switch
        {
            ModbusTcpTransactionEventKind.RequestObserved =>
                $"Observed Modbus request correlation={transactionEvent.CorrelationId} function={FormatFunction(transactionEvent.Frame)} {FormatAnalysis(analysis)}.",
            ModbusTcpTransactionEventKind.ResponseAwaitingRequest =>
                $"Observed Modbus response before matching request diagnostics correlation={transactionEvent.CorrelationId} function={FormatFunction(transactionEvent.Frame)} {FormatAnalysis(analysis)}.",
            ModbusTcpTransactionEventKind.ResponseMatched =>
                $"Matched Modbus response correlation={transactionEvent.CorrelationId} function={FormatFunction(transactionEvent.Frame)} exception={transactionEvent.Frame.IsExceptionResponse} {FormatAnalysis(analysis)}.",
            ModbusTcpTransactionEventKind.ResponseWithoutRequest =>
                $"Observed Modbus response without pending request correlation={transactionEvent.CorrelationId} function={FormatFunction(transactionEvent.Frame)} {FormatAnalysis(analysis)}.",
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
        ModbusTcpTransactionEvent transactionEvent,
        ModbusTcpPduAnalysis analysis)
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
            ["transactionEventKind"] = FormatTransactionEventKind(transactionEvent.Kind),
            ["operation"] = analysis.Operation,
            ["addressBase"] = "zeroBasedPdu"
        };

        if (frame.ExceptionCode is { } exceptionCode)
        {
            metadata["exceptionCode"] = exceptionCode.ToString(CultureInfo.InvariantCulture);
        }

        if (analysis.Address is { } address)
        {
            metadata["address"] = address.ToString(CultureInfo.InvariantCulture);
        }

        if (analysis.Quantity is { } quantity)
        {
            metadata["quantity"] = quantity.ToString(CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(analysis.AddressRange))
        {
            metadata["addressRange"] = analysis.AddressRange;
        }

        if (!string.IsNullOrWhiteSpace(analysis.ValuesHex))
        {
            metadata["valuesHex"] = analysis.ValuesHex;
        }

        if (analysis.ByteCount is { } byteCount)
        {
            metadata["byteCount"] = byteCount.ToString(CultureInfo.InvariantCulture);
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

    private static string FormatAnalysis(ModbusTcpPduAnalysis analysis)
    {
        var parts = new List<string>
        {
            $"operation={analysis.Operation}"
        };

        if (analysis.Address is { } address)
        {
            parts.Add($"address={address.ToString(CultureInfo.InvariantCulture)}");
            parts.Add("address_base=zeroBasedPdu");
        }

        if (analysis.Quantity is { } quantity)
        {
            parts.Add($"quantity={quantity.ToString(CultureInfo.InvariantCulture)}");
        }

        if (!string.IsNullOrWhiteSpace(analysis.AddressRange))
        {
            parts.Add($"address_range={analysis.AddressRange}");
        }

        if (!string.IsNullOrWhiteSpace(analysis.ValuesHex))
        {
            parts.Add($"values={analysis.ValuesHex}");
        }

        if (analysis.ByteCount is { } byteCount)
        {
            parts.Add($"byte_count={byteCount.ToString(CultureInfo.InvariantCulture)}");
        }

        if (analysis.ExceptionCode is { } exceptionCode)
        {
            parts.Add($"exception_code={exceptionCode.ToString(CultureInfo.InvariantCulture)}");
        }

        return string.Join(" ", parts);
    }
}
