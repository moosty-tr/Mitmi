using System.Threading.Channels;
using Mitmi.Domain;

namespace Mitmi.Application.Sessions;

public sealed class BoundedSessionEventSink : ISessionEventSink, IAsyncDisposable
{
    private const int DefaultCapacity = 4096;

    private readonly Channel<SessionEvent> channel;
    private readonly ISessionEventSink innerSink;
    private readonly SessionId sessionId;
    private readonly Task writerTask;
    private int disposed;
    private int lossWarningEmitted;
    private long droppedEvents;

    public BoundedSessionEventSink(
        ISessionEventSink innerSink,
        SessionId sessionId,
        int capacity = DefaultCapacity)
    {
        ArgumentNullException.ThrowIfNull(innerSink);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        this.innerSink = innerSink;
        this.sessionId = sessionId;
        channel = Channel.CreateBounded<SessionEvent>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
        writerTask = Task.Run(WriteEventsAsync);
    }

    public long DroppedEvents => Interlocked.Read(ref droppedEvents);

    public ValueTask EmitAsync(SessionEvent sessionEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionEvent);
        cancellationToken.ThrowIfCancellationRequested();

        if (Volatile.Read(ref disposed) != 0 || !channel.Writer.TryWrite(sessionEvent))
        {
            Interlocked.Increment(ref droppedEvents);
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

        var dropped = DroppedEvents;
        if (dropped > 0)
        {
            await innerSink.EmitAsync(
                new SessionEvent(
                    DateTimeOffset.UtcNow,
                    SessionEventLevel.Warning,
                    SessionEventNames.DiagnosticsEventLossSummary,
                    sessionId,
                    ConnectionId: null,
                    $"Dropped {dropped} session events because the diagnostic event queue was full."),
                CancellationToken.None);
        }
    }

    private async Task WriteEventsAsync()
    {
        await foreach (var sessionEvent in channel.Reader.ReadAllAsync())
        {
            await innerSink.EmitAsync(sessionEvent, CancellationToken.None);
            await EmitLossWarningIfNeededAsync();
        }

        await EmitLossWarningIfNeededAsync();
    }

    private async ValueTask EmitLossWarningIfNeededAsync()
    {
        if (DroppedEvents == 0)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref lossWarningEmitted, 1, 0) != 0)
        {
            return;
        }

        await innerSink.EmitAsync(
            new SessionEvent(
                DateTimeOffset.UtcNow,
                SessionEventLevel.Warning,
                SessionEventNames.DiagnosticsEventDropped,
                sessionId,
                ConnectionId: null,
                "Diagnostic event queue is full; dropping newest session events until the writer catches up."),
            CancellationToken.None);
    }
}
