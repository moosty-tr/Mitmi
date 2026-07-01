using Mitmi.Domain;

namespace Mitmi.Application.Configuration;

public enum ProductLogLevel
{
    Debug,
    Info,
    Warning,
    Error
}

public enum CaptureRetentionMode
{
    Manual
}

public sealed record LoggingRuntimeOptions(
    LogSinkRuntimeOptions Console,
    LogSinkRuntimeOptions File);

public sealed record LogSinkRuntimeOptions(
    bool Enabled,
    ProductLogLevel MinimumLevel,
    string? Path);

public sealed record CaptureRuntimeOptions(
    bool Enabled,
    string OutputPath,
    CaptureRetentionMode RetentionMode);

public sealed record MetricsRuntimeOptions(
    bool Enabled,
    string Sink);

public sealed record SessionRuntimeOptions(
    SessionId Id,
    ProtocolId Protocol,
    NetworkEndpoint ListenEndpoint,
    NetworkEndpoint UpstreamEndpoint,
    SessionDiagnosticsRuntimeOptions Diagnostics);

public sealed record SessionDiagnosticsRuntimeOptions(
    bool DecodeProtocol,
    bool CaptureRawPayloads);
