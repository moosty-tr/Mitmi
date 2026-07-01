namespace Mitmi.Application.Protocols;

public sealed class ProtocolRegistry
{
    private readonly Dictionary<string, IProtocolPlugin> plugins = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<string> ProtocolIds => plugins.Keys.ToArray();

    public IReadOnlyCollection<IProtocolPlugin> Plugins => plugins.Values.ToArray();

    public void Register(IProtocolPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        ArgumentException.ThrowIfNullOrWhiteSpace(plugin.Id);

        if (!plugins.TryAdd(plugin.Id, plugin))
        {
            throw new InvalidOperationException($"Protocol '{plugin.Id}' is already registered.");
        }
    }

    public bool Contains(string protocolId) => plugins.ContainsKey(protocolId);
}
