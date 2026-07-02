namespace Mitmi.Protocols.Modbus.Framing;

public readonly record struct ModbusTcpCorrelationKey(
    ushort TransactionId,
    byte UnitId)
{
    public override string ToString() => $"{TransactionId:X4}:{UnitId:X2}";
}
