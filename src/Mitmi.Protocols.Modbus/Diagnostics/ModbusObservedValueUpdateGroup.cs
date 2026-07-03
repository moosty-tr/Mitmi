using Mitmi.Domain;

namespace Mitmi.Protocols.Modbus.Diagnostics;

public sealed record ModbusObservedValueUpdateGroup(
    SessionId SessionId,
    NetworkEndpoint UpstreamEndpoint,
    byte UnitId,
    byte FunctionCode,
    string Operation,
    ModbusObservedTable Table,
    ushort Address,
    ushort Quantity,
    string AddressBase,
    string AddressRange,
    string CorrelationId,
    DateTimeOffset ObservedAt,
    IReadOnlyList<ModbusObservedValueCellUpdate> ObservedCells,
    IReadOnlyList<ModbusObservedValueCellUpdate> ChangedCells);
