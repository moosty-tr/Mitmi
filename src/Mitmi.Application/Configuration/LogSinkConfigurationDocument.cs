namespace Mitmi.Application.Configuration;

public sealed class LogSinkConfigurationDocument
{
    public bool? Enabled { get; init; }

    public string? MinimumLevel { get; init; }

    public string? Path { get; init; }
}
