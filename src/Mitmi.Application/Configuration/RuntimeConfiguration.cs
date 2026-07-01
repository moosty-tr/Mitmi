namespace Mitmi.Application.Configuration;

public sealed record RuntimeConfiguration(
    int ConfigurationVersion,
    string ConfigurationFilePath,
    LoggingRuntimeOptions Logging,
    CaptureRuntimeOptions Capture,
    MetricsRuntimeOptions Metrics,
    SessionRuntimeOptions Session);
