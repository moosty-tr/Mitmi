using Mitmi.Application.Sessions;
using Mitmi.Domain;
using Mitmi.Host.Console;

namespace Mitmi.IntegrationTests;

public sealed class SessionEventMetricsSinkTests
{
    [Fact]
    public async Task EmitConnectionSummaryAsync_writes_connection_metrics_event()
    {
        var eventSink = new RecordingSessionEventSink();
        var metricsSink = new SessionEventMetricsSink(eventSink);

        await metricsSink.EmitConnectionSummaryAsync(
            new ConnectionMetricsSummary(
                new DateTimeOffset(2026, 7, 2, 12, 0, 0, TimeSpan.Zero),
                new SessionId("integration"),
                new ConnectionId(5),
                TimeSpan.FromMilliseconds(12.4),
                new TrafficDirectionMetrics(Bytes: 10, Chunks: 2),
                new TrafficDirectionMetrics(Bytes: 20, Chunks: 3)),
            CancellationToken.None);

        var sessionEvent = Assert.Single(eventSink.Events);
        Assert.Equal(SessionEventNames.MetricsConnectionSummary, sessionEvent.Name);
        Assert.Equal(new SessionId("integration"), sessionEvent.SessionId);
        Assert.Equal(new ConnectionId(5), sessionEvent.ConnectionId);
        Assert.Contains("duration_ms=12", sessionEvent.Message);
        Assert.Contains("client_to_server_bytes=10", sessionEvent.Message);
        Assert.Contains("server_to_client_chunks=3", sessionEvent.Message);
    }

    [Fact]
    public async Task EmitSessionSummaryAsync_writes_session_metrics_event()
    {
        var eventSink = new RecordingSessionEventSink();
        var metricsSink = new SessionEventMetricsSink(eventSink);

        await metricsSink.EmitSessionSummaryAsync(
            new SessionMetricsSummary(
                new DateTimeOffset(2026, 7, 2, 12, 0, 0, TimeSpan.Zero),
                new SessionId("integration"),
                TimeSpan.FromMilliseconds(25.1),
                ConnectionsAccepted: 2,
                ConnectionsClosed: 1,
                UpstreamConnectionFailures: 1,
                new TrafficDirectionMetrics(Bytes: 10, Chunks: 2),
                new TrafficDirectionMetrics(Bytes: 20, Chunks: 3)),
            CancellationToken.None);

        var sessionEvent = Assert.Single(eventSink.Events);
        Assert.Equal(SessionEventNames.MetricsSessionSummary, sessionEvent.Name);
        Assert.Equal(new SessionId("integration"), sessionEvent.SessionId);
        Assert.Null(sessionEvent.ConnectionId);
        Assert.Contains("connections_accepted=2", sessionEvent.Message);
        Assert.Contains("upstream_connection_failures=1", sessionEvent.Message);
        Assert.Contains("server_to_client_bytes=20", sessionEvent.Message);
    }

    private sealed class RecordingSessionEventSink : ISessionEventSink
    {
        private readonly List<SessionEvent> events = [];

        public IReadOnlyList<SessionEvent> Events => events;

        public ValueTask EmitAsync(SessionEvent sessionEvent, CancellationToken cancellationToken)
        {
            events.Add(sessionEvent);
            return ValueTask.CompletedTask;
        }
    }
}
