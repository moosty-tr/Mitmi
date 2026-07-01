namespace Mitmi.Application.Configuration;

public sealed class SessionConfigurationDocument
{
    public string? Id { get; init; }

    public string? Protocol { get; init; }

    public EndpointConfigurationDocument? ListenEndpoint { get; init; }

    public EndpointConfigurationDocument? UpstreamEndpoint { get; init; }

    public SessionDiagnosticsConfigurationDocument? Diagnostics { get; init; }
}
