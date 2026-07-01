namespace Mitmi.Application.Configuration;

public sealed class SessionDiagnosticsConfigurationDocument
{
    public bool? DecodeProtocol { get; init; }

    public bool? CaptureRawPayloads { get; init; }
}
