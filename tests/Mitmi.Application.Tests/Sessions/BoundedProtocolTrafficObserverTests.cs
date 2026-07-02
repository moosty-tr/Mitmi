using Mitmi.Application.Sessions;
using Mitmi.Domain;

namespace Mitmi.Application.Tests.Sessions;

public sealed class BoundedProtocolTrafficObserverTests
{
    [Fact]
    public async Task DisposeAsync_flushes_queued_observations_to_inner_observer()
    {
        var innerObserver = new RecordingProtocolTrafficObserver();
        var eventSink = new RecordingSessionEventSink();

        await using (var observer = new BoundedProtocolTrafficObserver(
            innerObserver,
            eventSink,
            new SessionId("test"),
            new ConnectionId(1),
            capacity: 4))
        {
            await observer.ObserveAsync(CreateObservation(TrafficDirection.ClientToServer, [0x01]), CancellationToken.None);
            await observer.ObserveAsync(CreateObservation(TrafficDirection.ServerToClient, [0x02]), CancellationToken.None);
        }

        Assert.Contains(innerObserver.Observations, observation => observation.Direction == TrafficDirection.ClientToServer);
        Assert.Contains(innerObserver.Observations, observation => observation.Direction == TrafficDirection.ServerToClient);
        Assert.Empty(eventSink.Events);
    }

    [Fact]
    public async Task ObserveAsync_drops_newest_observation_when_queue_is_full_and_reports_loss()
    {
        var innerObserver = new BlockingRecordingProtocolTrafficObserver();
        var eventSink = new RecordingSessionEventSink();

        await using (var observer = new BoundedProtocolTrafficObserver(
            innerObserver,
            eventSink,
            new SessionId("test"),
            new ConnectionId(7),
            capacity: 1))
        {
            await observer.ObserveAsync(CreateObservation(TrafficDirection.ClientToServer, [0x01]), CancellationToken.None);
            await innerObserver.WaitForFirstObservationAsync();

            await observer.ObserveAsync(CreateObservation(TrafficDirection.ServerToClient, [0x02]), CancellationToken.None);
            await observer.ObserveAsync(CreateObservation(TrafficDirection.ClientToServer, [0x03]), CancellationToken.None);

            Assert.Equal(1, observer.DroppedObservations);

            innerObserver.ReleaseFirstObservation();
        }

        Assert.Equal(2, innerObserver.Observations.Count);
        Assert.DoesNotContain(innerObserver.Observations, observation => observation.Payload.Span[0] == 0x03);
        Assert.Contains(eventSink.Events, sessionEvent => sessionEvent.Name == SessionEventNames.ProtocolObservationDropped);

        var summary = Assert.Single(
            eventSink.Events,
            sessionEvent => sessionEvent.Name == SessionEventNames.ProtocolObservationLossSummary);
        Assert.Equal(new ConnectionId(7), summary.ConnectionId);
        Assert.Contains("Dropped 1 protocol traffic observations", summary.Message);
    }

    [Fact]
    public async Task ObserveAsync_reports_inner_observer_failure_without_throwing_to_caller()
    {
        var innerObserver = new FailingProtocolTrafficObserver();
        var eventSink = new RecordingSessionEventSink();

        await using (var observer = new BoundedProtocolTrafficObserver(
            innerObserver,
            eventSink,
            new SessionId("test"),
            new ConnectionId(3),
            capacity: 4))
        {
            await observer.ObserveAsync(CreateObservation(TrafficDirection.ClientToServer, [0x01]), CancellationToken.None);
        }

        var failure = Assert.Single(
            eventSink.Events,
            sessionEvent => sessionEvent.Name == SessionEventNames.ProtocolObserverFailed);
        Assert.Equal(SessionEventLevel.Warning, failure.Level);
        Assert.Equal(new ConnectionId(1), failure.ConnectionId);
        Assert.Contains("Protocol diagnostics failed", failure.Message);
    }

    private static ProtocolTrafficObservation CreateObservation(
        TrafficDirection direction,
        byte[] payload)
    {
        return new ProtocolTrafficObservation(
            new SessionId("test"),
            new ConnectionId(1),
            direction,
            payload);
    }

    private class RecordingProtocolTrafficObserver : IProtocolTrafficObserver
    {
        private readonly object gate = new();
        private readonly List<ProtocolTrafficObservation> observations = [];

        public IReadOnlyList<ProtocolTrafficObservation> Observations
        {
            get
            {
                lock (gate)
                {
                    return observations.ToArray();
                }
            }
        }

        public virtual ValueTask ObserveAsync(
            ProtocolTrafficObservation observation,
            CancellationToken cancellationToken)
        {
            lock (gate)
            {
                observations.Add(observation);
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingRecordingProtocolTrafficObserver : RecordingProtocolTrafficObserver
    {
        private readonly TaskCompletionSource firstObservationStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releaseFirstObservation = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int shouldBlockFirstObservation = 1;

        public override async ValueTask ObserveAsync(
            ProtocolTrafficObservation observation,
            CancellationToken cancellationToken)
        {
            await base.ObserveAsync(observation, cancellationToken);

            if (Interlocked.Exchange(ref shouldBlockFirstObservation, 0) == 1)
            {
                firstObservationStarted.TrySetResult();
                await releaseFirstObservation.Task.WaitAsync(cancellationToken);
            }
        }

        public Task WaitForFirstObservationAsync()
        {
            return firstObservationStarted.Task;
        }

        public void ReleaseFirstObservation()
        {
            releaseFirstObservation.TrySetResult();
        }
    }

    private sealed class FailingProtocolTrafficObserver : IProtocolTrafficObserver
    {
        public ValueTask ObserveAsync(
            ProtocolTrafficObservation observation,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Decoder failed.");
        }
    }

    private sealed class RecordingSessionEventSink : ISessionEventSink
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

        public ValueTask EmitAsync(SessionEvent sessionEvent, CancellationToken cancellationToken)
        {
            lock (gate)
            {
                events.Add(sessionEvent);
            }

            return ValueTask.CompletedTask;
        }
    }
}
