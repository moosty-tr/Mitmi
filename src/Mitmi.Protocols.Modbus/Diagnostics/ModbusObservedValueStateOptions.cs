namespace Mitmi.Protocols.Modbus.Diagnostics;

public sealed record ModbusObservedValueStateOptions
{
    public int MaxObservedCells { get; init; } = 4096;

    public int MaxCellsPerUpdateGroup { get; init; } = 256;
}
