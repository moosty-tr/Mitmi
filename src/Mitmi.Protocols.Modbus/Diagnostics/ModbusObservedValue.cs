using System.Globalization;

namespace Mitmi.Protocols.Modbus.Diagnostics;

public readonly record struct ModbusObservedValue
{
    private readonly bool booleanValue;
    private readonly ushort registerValue;

    private ModbusObservedValue(
        ModbusObservedValueKind kind,
        bool booleanValue,
        ushort registerValue)
    {
        Kind = kind;
        this.booleanValue = booleanValue;
        this.registerValue = registerValue;
    }

    public ModbusObservedValueKind Kind { get; }

    public bool BooleanValue => Kind == ModbusObservedValueKind.Boolean
        ? booleanValue
        : throw new InvalidOperationException("Observed value is not a boolean value.");

    public ushort RegisterValue => Kind == ModbusObservedValueKind.Register
        ? registerValue
        : throw new InvalidOperationException("Observed value is not a register value.");

    public static ModbusObservedValue Boolean(bool value) =>
        new(ModbusObservedValueKind.Boolean, value, registerValue: 0);

    public static ModbusObservedValue Register(ushort value) =>
        new(ModbusObservedValueKind.Register, booleanValue: false, value);

    public override string ToString()
    {
        return Kind switch
        {
            ModbusObservedValueKind.Boolean => booleanValue ? "true" : "false",
            ModbusObservedValueKind.Register => registerValue.ToString("x4", CultureInfo.InvariantCulture),
            _ => string.Empty
        };
    }
}
