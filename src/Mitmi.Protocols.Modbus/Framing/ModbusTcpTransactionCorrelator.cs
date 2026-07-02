namespace Mitmi.Protocols.Modbus.Framing;

public sealed class ModbusTcpTransactionCorrelator
{
    private readonly Dictionary<ModbusTcpCorrelationKey, ModbusTcpFrame> pendingRequests = [];
    private readonly Dictionary<ModbusTcpCorrelationKey, ModbusTcpFrame> pendingResponses = [];

    public ModbusTcpTransactionEvent Observe(ModbusTcpFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        return frame.Direction switch
        {
            ModbusTcpFrameDirection.ClientToServer => ObserveRequest(frame),
            ModbusTcpFrameDirection.ServerToClient => ObserveResponse(frame),
            _ => throw new InvalidOperationException($"Unsupported Modbus frame direction '{frame.Direction}'.")
        };
    }

    private ModbusTcpTransactionEvent ObserveRequest(ModbusTcpFrame frame)
    {
        if (pendingResponses.Remove(frame.CorrelationKey, out var pendingResponse))
        {
            return MatchResponseToRequest(pendingResponse, frame);
        }

        var warnings = new List<ModbusTcpDecodeWarning>();
        if (pendingRequests.ContainsKey(frame.CorrelationKey))
        {
            warnings.Add(new ModbusTcpDecodeWarning(
                ModbusTcpWarningCodes.DuplicatePendingRequest,
                $"A pending Modbus request already exists for correlation key '{frame.CorrelationKey}'."));
        }

        pendingRequests[frame.CorrelationKey] = frame;

        return new ModbusTcpTransactionEvent(
            ModbusTcpTransactionEventKind.RequestObserved,
            frame,
            RequestFrame: null,
            frame.CorrelationKey.ToString(),
            warnings);
    }

    private ModbusTcpTransactionEvent ObserveResponse(ModbusTcpFrame frame)
    {
        if (!pendingRequests.Remove(frame.CorrelationKey, out var requestFrame))
        {
            pendingResponses[frame.CorrelationKey] = frame;
            return new ModbusTcpTransactionEvent(
                ModbusTcpTransactionEventKind.ResponseAwaitingRequest,
                frame,
                RequestFrame: null,
                frame.CorrelationKey.ToString(),
                []);
        }

        return MatchResponseToRequest(frame, requestFrame);
    }

    private static ModbusTcpTransactionEvent MatchResponseToRequest(
        ModbusTcpFrame responseFrame,
        ModbusTcpFrame requestFrame)
    {
        var warnings = new List<ModbusTcpDecodeWarning>();
        if (requestFrame.FunctionCode != responseFrame.OperationFunctionCode)
        {
            warnings.Add(new ModbusTcpDecodeWarning(
                ModbusTcpWarningCodes.ResponseFunctionMismatch,
                $"Response function code '{responseFrame.OperationFunctionCode}' does not match request function code '{requestFrame.FunctionCode}'."));
        }

        return new ModbusTcpTransactionEvent(
            ModbusTcpTransactionEventKind.ResponseMatched,
            responseFrame,
            requestFrame,
            responseFrame.CorrelationKey.ToString(),
            warnings);
    }
}
