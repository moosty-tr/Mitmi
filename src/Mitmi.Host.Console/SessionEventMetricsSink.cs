using System.Globalization;
using Mitmi.Application.Sessions;

namespace Mitmi.Host.Console;

internal sealed class SessionEventMetricsSink : ISessionMetricsSink
{
    private readonly ISessionEventSink eventSink;

    public SessionEventMetricsSink(ISessionEventSink eventSink)
    {
        this.eventSink = eventSink;
    }

    public ValueTask EmitConnectionSummaryAsync(
        ConnectionMetricsSummary summary,
        CancellationToken cancellationToken)
    {
        return eventSink.EmitAsync(
            new SessionEvent(
                summary.Timestamp,
                SessionEventLevel.Info,
                SessionEventNames.MetricsConnectionSummary,
                summary.SessionId,
                summary.ConnectionId,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Connection metrics duration_ms={summary.Duration.TotalMilliseconds:F0} client_to_server_bytes={summary.ClientToServer.Bytes} client_to_server_chunks={summary.ClientToServer.Chunks} server_to_client_bytes={summary.ServerToClient.Bytes} server_to_client_chunks={summary.ServerToClient.Chunks}.")),
            cancellationToken);
    }

    public ValueTask EmitSessionSummaryAsync(
        SessionMetricsSummary summary,
        CancellationToken cancellationToken)
    {
        return eventSink.EmitAsync(
            new SessionEvent(
                summary.Timestamp,
                SessionEventLevel.Info,
                SessionEventNames.MetricsSessionSummary,
                summary.SessionId,
                ConnectionId: null,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Session metrics duration_ms={summary.Duration.TotalMilliseconds:F0} connections_accepted={summary.ConnectionsAccepted} connections_closed={summary.ConnectionsClosed} upstream_connection_failures={summary.UpstreamConnectionFailures} client_to_server_bytes={summary.ClientToServer.Bytes} client_to_server_chunks={summary.ClientToServer.Chunks} server_to_client_bytes={summary.ServerToClient.Bytes} server_to_client_chunks={summary.ServerToClient.Chunks}.")),
            cancellationToken);
    }
}
