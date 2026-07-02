using Mitmi.Application.Sessions;
using Mitmi.Domain;

namespace Mitmi.Application.Tests.Sessions;

public sealed class BoundedSessionEventSinkTests
{
    [Fact]
    public async Task DisposeAsync_flushes_queued_events_to_inner_sink()
    {
        var innerSink = new RecordingSessionEventSink();

        await using (var sink = new BoundedSessionEventSink(innerSink, new SessionId("test"), capacity: 4))
        {
            await sink.EmitAsync(CreateEvent("event.one"), CancellationToken.None);
            await sink.EmitAsync(CreateEvent("event.two"), CancellationToken.None);
        }

        Assert.Contains(innerSink.Events, sessionEvent => sessionEvent.Name == "event.one");
        Assert.Contains(innerSink.Events, sessionEvent => sessionEvent.Name == "event.two");
        Assert.DoesNotContain(innerSink.Events, sessionEvent => sessionEvent.Name == SessionEventNames.DiagnosticsEventLossSummary);
    }

    [Fact]
    public async Task EmitAsync_drops_newest_event_when_queue_is_full_and_reports_loss()
    {
        var innerSink = new BlockingRecordingSessionEventSink();

        await using (var sink = new BoundedSessionEventSink(innerSink, new SessionId("test"), capacity: 1))
        {
            await sink.EmitAsync(CreateEvent("event.one"), CancellationToken.None);
            await innerSink.WaitForFirstEventAsync();

            await sink.EmitAsync(CreateEvent("event.two"), CancellationToken.None);
            await sink.EmitAsync(CreateEvent("event.three"), CancellationToken.None);

            Assert.Equal(1, sink.DroppedEvents);

            innerSink.ReleaseFirstEvent();
        }

        Assert.Contains(innerSink.Events, sessionEvent => sessionEvent.Name == "event.one");
        Assert.Contains(innerSink.Events, sessionEvent => sessionEvent.Name == "event.two");
        Assert.DoesNotContain(innerSink.Events, sessionEvent => sessionEvent.Name == "event.three");
        Assert.Contains(innerSink.Events, sessionEvent => sessionEvent.Name == SessionEventNames.DiagnosticsEventDropped);

        var summary = Assert.Single(
            innerSink.Events,
            sessionEvent => sessionEvent.Name == SessionEventNames.DiagnosticsEventLossSummary);
        Assert.Contains("Dropped 1 session events", summary.Message);
    }

    private static SessionEvent CreateEvent(string name)
    {
        return new SessionEvent(
            DateTimeOffset.Parse("2026-07-02T09:00:00+00:00"),
            SessionEventLevel.Info,
            name,
            new SessionId("test"),
            ConnectionId: null,
            $"Message for {name}.");
    }

    private class RecordingSessionEventSink : ISessionEventSink
    {
        private readonly object gate = new();
        private readonly List<SessionEvent> events = [];

        public IReadOnlyList<SessionEvent> Events
        {
            get
            {
                lock (gate)
                {
                    return events.ToArray();
                }
            }
        }

        public virtual ValueTask EmitAsync(SessionEvent sessionEvent, CancellationToken cancellationToken)
        {
            lock (gate)
            {
                events.Add(sessionEvent);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingRecordingSessionEventSink : RecordingSessionEventSink
    {
        private readonly TaskCompletionSource firstEventStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releaseFirstEvent = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int shouldBlockFirstEvent = 1;

        public override async ValueTask EmitAsync(SessionEvent sessionEvent, CancellationToken cancellationToken)
        {
            await base.EmitAsync(sessionEvent, cancellationToken);

            if (Interlocked.Exchange(ref shouldBlockFirstEvent, 0) == 1)
            {
                firstEventStarted.TrySetResult();
                await releaseFirstEvent.Task.WaitAsync(cancellationToken);
            }
        }

        public Task WaitForFirstEventAsync()
        {
            return firstEventStarted.Task;
        }

        public void ReleaseFirstEvent()
        {
            releaseFirstEvent.TrySetResult();
        }
    }
}
