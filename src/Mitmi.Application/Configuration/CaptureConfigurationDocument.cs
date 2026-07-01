namespace Mitmi.Application.Configuration;

public sealed class CaptureConfigurationDocument
{
    public bool? Enabled { get; init; }

    public string? OutputPath { get; init; }

    public CaptureRetentionConfigurationDocument? Retention { get; init; }
}
