namespace Mitmi.Application.Sessions;

public interface ISessionMetricsSink
{
    ValueTask EmitConnectionSummaryAsync(
        ConnectionMetricsSummary summary,
        CancellationToken cancellationToken);

    ValueTask EmitSessionSummaryAsync(
        SessionMetricsSummary summary,
        CancellationToken cancellationToken);
}
