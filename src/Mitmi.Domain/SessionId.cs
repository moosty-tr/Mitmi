namespace Mitmi.Domain;

public readonly record struct SessionId(string Value)
{
    public override string ToString() => Value;
}
