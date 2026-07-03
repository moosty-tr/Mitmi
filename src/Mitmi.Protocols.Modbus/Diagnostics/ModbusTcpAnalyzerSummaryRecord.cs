using Mitmi.Domain;

namespace Mitmi.Protocols.Modbus.Diagnostics;

public sealed record ModbusTcpAnalyzerSummaryRecord(
    DateTimeOffset Timestamp,
    SessionId SessionId,
    byte UnitId,
    byte FunctionCode,
    string Operation,
    ushort? Address,
    ushort? Quantity,
    string? AddressRange,
    string AddressBase,
    int Reads,
    int Writes,
    int Requests,
    int Responses,
    int Exceptions);
