namespace Mitmi.Protocols.Modbus.Diagnostics;

public sealed record ModbusObservedValueCellUpdate(
    ushort Address,
    ModbusObservedValue? PreviousValue,
    ModbusObservedValue CurrentValue,
    bool Changed,
    DateTimeOffset FirstObservedAt,
    DateTimeOffset LastObservedAt,
    DateTimeOffset? LastChangedAt);
