namespace Mitmi.Protocols.Modbus.Diagnostics;

public sealed record ModbusTcpPduAnalysis(
    byte UnitId,
    byte FunctionCode,
    byte OperationFunctionCode,
    string Operation,
    ushort? Address,
    ushort? Quantity,
    string? AddressRange,
    string? ValuesHex,
    int? ByteCount,
    byte? ExceptionCode);
