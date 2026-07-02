namespace Mitmi.Protocols.Modbus.Framing;

public sealed record ModbusTcpDecodeWarning(
    string Code,
    string Message);
