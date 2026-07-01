namespace Mitmi.Application.Configuration;

public sealed class LoggingConfigurationDocument
{
    public LogSinkConfigurationDocument? Console { get; init; }

    public LogSinkConfigurationDocument? File { get; init; }
}
