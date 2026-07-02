using System.Diagnostics;
using Mitmi.Domain;

namespace Mitmi.Application.Sessions;

internal sealed class SessionMetricsCollector
{
    private readonly SessionId sessionId;
    private readonly long startTimestamp;
    private long clientToServerBytes;
    private long clientToServerChunks;
    private long serverToClientBytes;
    private long serverToClientChunks;
    private long connectionsAccepted;
    private long connectionsClosed;
    private long upstreamConnectionFailures;

    public SessionMetricsCollector(SessionId sessionId)
    {
        this.sessionId = sessionId;
        startTimestamp = Stopwatch.GetTimestamp();
    }

    public ConnectionMetricsCollector BeginConnection(ConnectionId connectionId)
    {
        Interlocked.Increment(ref connectionsAccepted);
        return new ConnectionMetricsCollector(sessionId, connectionId);
    }

    public void RecordUpstreamConnectionFailure()
    {
        Interlocked.Increment(ref upstreamConnectionFailures);
    }

    public ConnectionMetricsSummary CloseConnection(ConnectionMetricsCollector connection)
    {
        var summary = connection.CreateSummary();
        Interlocked.Increment(ref connectionsClosed);
        Interlocked.Add(ref clientToServerBytes, summary.ClientToServer.Bytes);
        Interlocked.Add(ref clientToServerChunks, summary.ClientToServer.Chunks);
        Interlocked.Add(ref serverToClientBytes, summary.ServerToClient.Bytes);
        Interlocked.Add(ref serverToClientChunks, summary.ServerToClient.Chunks);
        return summary;
    }

    public SessionMetricsSummary CreateSummary()
    {
        return new SessionMetricsSummary(
            DateTimeOffset.UtcNow,
            sessionId,
            Stopwatch.GetElapsedTime(startTimestamp),
            Interlocked.Read(ref connectionsAccepted),
            Interlocked.Read(ref connectionsClosed),
            Interlocked.Read(ref upstreamConnectionFailures),
            new TrafficDirectionMetrics(
                Interlocked.Read(ref clientToServerBytes),
                Interlocked.Read(ref clientToServerChunks)),
            new TrafficDirectionMetrics(
                Interlocked.Read(ref serverToClientBytes),
                Interlocked.Read(ref serverToClientChunks)));
    }

    internal sealed class ConnectionMetricsCollector
    {
        private readonly SessionId sessionId;
        private readonly ConnectionId connectionId;
        private readonly long startTimestamp;
        private long clientToServerBytes;
        private long clientToServerChunks;
        private long serverToClientBytes;
        private long serverToClientChunks;

        public ConnectionMetricsCollector(SessionId sessionId, ConnectionId connectionId)
        {
            this.sessionId = sessionId;
            this.connectionId = connectionId;
            startTimestamp = Stopwatch.GetTimestamp();
        }

        public void RecordForwarded(TrafficDirection direction, int bytes)
        {
            if (direction == TrafficDirection.ClientToServer)
            {
                Interlocked.Add(ref clientToServerBytes, bytes);
                Interlocked.Increment(ref clientToServerChunks);
                return;
            }

            Interlocked.Add(ref serverToClientBytes, bytes);
            Interlocked.Increment(ref serverToClientChunks);
        }

        public ConnectionMetricsSummary CreateSummary()
        {
            return new ConnectionMetricsSummary(
                DateTimeOffset.UtcNow,
                sessionId,
                connectionId,
                Stopwatch.GetElapsedTime(startTimestamp),
                new TrafficDirectionMetrics(
                    Interlocked.Read(ref clientToServerBytes),
                    Interlocked.Read(ref clientToServerChunks)),
                new TrafficDirectionMetrics(
                    Interlocked.Read(ref serverToClientBytes),
                    Interlocked.Read(ref serverToClientChunks)));
        }
    }
}
