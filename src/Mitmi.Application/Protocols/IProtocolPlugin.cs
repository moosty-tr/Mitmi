namespace Mitmi.Application.Protocols;

public interface IProtocolPlugin
{
    string Id { get; }

    string DisplayName { get; }
}
