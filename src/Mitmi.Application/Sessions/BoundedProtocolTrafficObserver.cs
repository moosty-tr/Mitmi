using System.Threading.Channels;
using Mitmi.Domain;

namespace Mitmi.Application.Sessions;

public sealed class BoundedProtocolTrafficObserver : IProtocolTrafficObserver, IAsyncDisposable
{
    private const int DefaultCapacity = 4096;

    private readonly Channel<ProtocolTrafficObservation> channel;
    private readonly IProtocolTrafficObserver innerObserver;
    private readonly ISessionEventSink eventSink;
    private readonly SessionId sessionId;
    private readonly ConnectionId connectionId;
    private readonly Task writerTask;
    private int disposed;
    private int lossWarningEmitted;
    private long droppedObservations;

    public BoundedProtocolTrafficObserver(
        IProtocolTrafficObserver innerObserver,
        ISessionEventSink eventSink,
        SessionId sessionId,
        ConnectionId connectionId,
        int capacity = DefaultCapacity)
    {
        ArgumentNullException.ThrowIfNull(innerObserver);
        ArgumentNullException.ThrowIfNull(eventSink);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        this.innerObserver = innerObserver;
        this.eventSink = eventSink;
        this.sessionId = sessionId;
        this.connectionId = connectionId;
        channel = Channel.CreateBounded<ProtocolTrafficObservation>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
        writerTask = Task.Run(ObserveTrafficAsync);
    }

    public long DroppedObservations => Interlocked.Read(ref droppedObservations);

    public ValueTask ObserveAsync(
        ProtocolTrafficObservation observation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Volatile.Read(ref disposed) != 0 || !channel.Writer.TryWrite(observation))
        {
            Interlocked.Increment(ref droppedObservations);
        }

        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        channel.Writer.TryComplete();
        await writerTask;

        if (innerObserver is IAsyncDisposable asyncDisposableObserver)
        {
            await asyncDisposableObserver.DisposeAsync();
        }

        var dropped = DroppedObservations;
        if (dropped > 0)
        {
            await eventSink.EmitAsync(
                new SessionEvent(
                    DateTimeOffset.UtcNow,
                    SessionEventLevel.Warning,
                    SessionEventNames.ProtocolObservationLossSummary,
                    sessionId,
                    connectionId,
                    $"Dropped {dropped} protocol traffic observations because the diagnostics queue was full."),
                CancellationToken.None);
        }
    }

    private async Task ObserveTrafficAsync()
    {
        await foreach (var observation in channel.Reader.ReadAllAsync())
        {
            try
            {
                await innerObserver.ObserveAsync(observation, CancellationToken.None);
            }
            catch (Exception exception)
            {
                await eventSink.EmitAsync(
                    new SessionEvent(
                        DateTimeOffset.UtcNow,
                        SessionEventLevel.Warning,
                        SessionEventNames.ProtocolObserverFailed,
                        observation.SessionId,
                        observation.ConnectionId,
                        $"Protocol diagnostics failed while observing {observation.Direction} traffic: {exception.Message}",
                        exception),
                    CancellationToken.None);
            }

            await EmitLossWarningIfNeededAsync();
        }

        await EmitLossWarningIfNeededAsync();
    }

    private async ValueTask EmitLossWarningIfNeededAsync()
    {
        if (DroppedObservations == 0)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref lossWarningEmitted, 1, 0) != 0)
        {
            return;
        }

        await eventSink.EmitAsync(
            new SessionEvent(
                DateTimeOffset.UtcNow,
                SessionEventLevel.Warning,
                SessionEventNames.ProtocolObservationDropped,
                sessionId,
                connectionId,
                "Protocol diagnostics queue is full; dropping newest traffic observations until the decoder catches up."),
            CancellationToken.None);
    }
}
