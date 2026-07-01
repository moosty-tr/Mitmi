namespace Mitmi.Application.Configuration;

public sealed class MetricsConfigurationDocument
{
    public bool? Enabled { get; init; }

    public string? Sink { get; init; }
}
