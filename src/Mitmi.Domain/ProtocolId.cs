namespace Mitmi.Domain;

public readonly record struct ProtocolId(string Value)
{
    public override string ToString() => Value;
}
