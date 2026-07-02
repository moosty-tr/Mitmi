namespace Mitmi.Protocols.Modbus.Framing;

public sealed record ModbusTcpTransactionEvent(
    ModbusTcpTransactionEventKind Kind,
    ModbusTcpFrame Frame,
    ModbusTcpFrame? RequestFrame,
    string CorrelationId,
    IReadOnlyList<ModbusTcpDecodeWarning> Warnings);
