namespace Mitmi.Domain;

public readonly record struct ConnectionId(long Value)
{
    public override string ToString() => Value.ToString();
}
