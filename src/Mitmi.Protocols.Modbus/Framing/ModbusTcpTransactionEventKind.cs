namespace Mitmi.Protocols.Modbus.Framing;

public enum ModbusTcpTransactionEventKind
{
    RequestObserved,
    ResponseAwaitingRequest,
    ResponseMatched,
    ResponseWithoutRequest
}
