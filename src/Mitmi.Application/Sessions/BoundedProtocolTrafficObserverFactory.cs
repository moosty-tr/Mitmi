using Mitmi.Domain;

namespace Mitmi.Application.Sessions;

public sealed class BoundedProtocolTrafficObserverFactory : IProtocolTrafficObserverFactory, IAsyncDisposable
{
    private readonly IProtocolTrafficObserverFactory innerFactory;
    private readonly ISessionEventSink eventSink;
    private readonly int capacity;

    public BoundedProtocolTrafficObserverFactory(
        IProtocolTrafficObserverFactory innerFactory,
        ISessionEventSink eventSink,
        int capacity = 4096)
    {
        ArgumentNullException.ThrowIfNull(innerFactory);
        ArgumentNullException.ThrowIfNull(eventSink);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        this.innerFactory = innerFactory;
        this.eventSink = eventSink;
        this.capacity = capacity;
    }

    public IProtocolTrafficObserver Create(SessionId sessionId, ConnectionId connectionId)
    {
        return new BoundedProtocolTrafficObserver(
            innerFactory.Create(sessionId, connectionId),
            eventSink,
            sessionId,
            connectionId,
            capacity);
    }

    public async ValueTask DisposeAsync()
    {
        if (innerFactory is IAsyncDisposable asyncDisposableFactory)
        {
            await asyncDisposableFactory.DisposeAsync();
        }
    }
}
