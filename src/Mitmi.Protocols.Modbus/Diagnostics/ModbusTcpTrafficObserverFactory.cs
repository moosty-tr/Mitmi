using Mitmi.Application.Sessions;
using Mitmi.Domain;

namespace Mitmi.Protocols.Modbus.Diagnostics;

public sealed class ModbusTcpTrafficObserverFactory : IProtocolTrafficObserverFactory, IAsyncDisposable
{
    private readonly ISessionEventSink eventSink;
    private readonly bool captureRawPayloads;
    private readonly ITrafficCaptureSink? trafficCaptureSink;
    private readonly ModbusTcpAnalyzerSessionSummary analyzerSessionSummary = new();
    private SessionId? sessionId;
    private int disposed;

    public ModbusTcpTrafficObserverFactory(
        ISessionEventSink eventSink,
        ITrafficCaptureSink? trafficCaptureSink = null,
        bool captureRawPayloads = false)
    {
        this.eventSink = eventSink;
        this.trafficCaptureSink = trafficCaptureSink;
        this.captureRawPayloads = captureRawPayloads;
    }

    public IProtocolTrafficObserver Create(SessionId sessionId, ConnectionId connectionId)
    {
        this.sessionId = sessionId;
        return new ModbusTcpTrafficObserver(
            eventSink,
            trafficCaptureSink,
            captureRawPayloads,
            analyzerSessionSummary);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0 || sessionId is null)
        {
            return;
        }

        foreach (var summaryEvent in analyzerSessionSummary.CreateEvents(sessionId.Value))
        {
            await eventSink.EmitAsync(summaryEvent, CancellationToken.None);
        }
    }
}
