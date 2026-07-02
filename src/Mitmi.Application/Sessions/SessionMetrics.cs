using Mitmi.Domain;

namespace Mitmi.Application.Sessions;

public sealed record TrafficDirectionMetrics(
    long Bytes,
    long Chunks);

public sealed record ConnectionMetricsSummary(
    DateTimeOffset Timestamp,
    SessionId SessionId,
    ConnectionId ConnectionId,
    TimeSpan Duration,
    TrafficDirectionMetrics ClientToServer,
    TrafficDirectionMetrics ServerToClient);

public sealed record SessionMetricsSummary(
    DateTimeOffset Timestamp,
    SessionId SessionId,
    TimeSpan Duration,
    long ConnectionsAccepted,
    long ConnectionsClosed,
    long UpstreamConnectionFailures,
    TrafficDirectionMetrics ClientToServer,
    TrafficDirectionMetrics ServerToClient);
