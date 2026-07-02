namespace Mitmi.Protocols.Modbus.Framing;

public sealed record ModbusTcpDecodeResult(
    IReadOnlyList<ModbusTcpFrame> Frames,
    IReadOnlyList<ModbusTcpDecodeWarning> Warnings);
