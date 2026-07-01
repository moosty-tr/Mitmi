namespace Mitmi.Domain;

public sealed record NetworkEndpoint(string Address, int Port)
{
    public override string ToString() => $"{Address}:{Port}";
}
