using Mitmi.Application.Protocols;

namespace Mitmi.Application.Configuration;

public sealed class ConfigurationValidationContext
{
    private ConfigurationValidationContext(
        string configurationFilePath,
        string configurationDirectory,
        IReadOnlySet<string> availableProtocolIds,
        bool warnOnPrivilegedPorts)
    {
        ConfigurationFilePath = configurationFilePath;
        ConfigurationDirectory = configurationDirectory;
        AvailableProtocolIds = availableProtocolIds;
        WarnOnPrivilegedPorts = warnOnPrivilegedPorts;
    }

    public string ConfigurationFilePath { get; }

    public string ConfigurationDirectory { get; }

    public IReadOnlySet<string> AvailableProtocolIds { get; }

    public bool WarnOnPrivilegedPorts { get; }

    public static ConfigurationValidationContext ForFile(
        string configurationFilePath,
        ProtocolRegistry protocolRegistry,
        bool warnOnPrivilegedPorts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationFilePath);
        ArgumentNullException.ThrowIfNull(protocolRegistry);

        var fullPath = Path.GetFullPath(configurationFilePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = Directory.GetCurrentDirectory();
        }

        return new ConfigurationValidationContext(
            fullPath,
            directory,
            protocolRegistry.ProtocolIds.ToHashSet(StringComparer.OrdinalIgnoreCase),
            warnOnPrivilegedPorts);
    }
}
