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

    public ModbusTcpTrafficObserver(ISessionEventSink eventSink)
    {
        this.eventSink = eventSink;
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
            }
        }
        finally
        {
            observationLock.Release();
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
}
