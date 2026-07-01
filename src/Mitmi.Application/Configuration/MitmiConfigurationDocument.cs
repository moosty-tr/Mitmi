namespace Mitmi.Application.Configuration;

public sealed class MitmiConfigurationDocument
{
    public int? ConfigurationVersion { get; init; }

    public LoggingConfigurationDocument? Logging { get; init; }

    public CaptureConfigurationDocument? Capture { get; init; }

    public MetricsConfigurationDocument? Metrics { get; init; }

    public SessionConfigurationDocument? Session { get; init; }
}
