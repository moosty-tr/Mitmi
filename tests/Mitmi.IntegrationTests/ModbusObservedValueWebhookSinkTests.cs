using System.Net;
using System.Text.Json.Nodes;
using Mitmi.Application.Sessions;
using Mitmi.Domain;
using Mitmi.Host.Console;
using Mitmi.Protocols.Modbus.Diagnostics;

namespace Mitmi.IntegrationTests;

public sealed class ModbusObservedValueWebhookSinkTests
{
    [Fact]
    public async Task EmitAsync_posts_filtered_changed_cell_payload()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var eventSink = new RecordingSessionEventSink();
        var handler = new RecordingHttpMessageHandler(HttpStatusCode.Accepted);
        await using var sink = new ModbusObservedValueWebhookSink(
            CreateOptions(
                new ObservedValueWebhookRangeFilter(
                    UnitId: 1,
                    Table: ModbusObservedTable.HoldingRegisters,
                    StartAddress: 1,
                    EndAddress: 1)),
            eventSink,
            new HttpClient(handler));

        await sink.EmitAsync(CreateUpdateGroup(), timeout.Token);
        await sink.DisposeAsync();

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://example.invalid/mitmi/observed-values", request.RequestUri!.ToString());

        var payload = JsonNode.Parse(Assert.Single(handler.Bodies))!.AsObject();
        Assert.Equal(1, payload["payloadSchemaVersion"]!.GetValue<int>());
        Assert.Equal("webhook-test", payload["sessionId"]!.GetValue<string>());
        Assert.Equal("192.0.2.10:502", payload["upstreamEndpoint"]!.GetValue<string>());
        Assert.Equal("holdingRegisters", payload["table"]!.GetValue<string>());
        Assert.Equal("zeroBasedPdu", payload["addressBase"]!.GetValue<string>());
        Assert.Equal("107-109", payload["requestedAddressRange"]!.GetValue<string>());

        var observedCell = Assert.Single(payload["observedCells"]!.AsArray());
        Assert.Single(payload["changedCells"]!.AsArray());
        Assert.Equal(1, observedCell!["address"]!.GetValue<int>());
        Assert.Equal("register", observedCell["valueKind"]!.GetValue<string>());
        Assert.Equal(0x2222, observedCell["currentValue"]!.GetValue<int>());
        Assert.Equal("2222", observedCell["currentValueHex"]!.GetValue<string>());
        Assert.Equal(0x1111, observedCell["previousValue"]!.GetValue<int>());
        Assert.Equal("1111", observedCell["previousValueHex"]!.GetValue<string>());

        Assert.Equal(1, sink.Delivered);
        Assert.Equal(0, sink.Failed);
        Assert.Equal(0, sink.Dropped);
        Assert.Contains(
            eventSink.Events,
            sessionEvent =>
                sessionEvent.Name == SessionEventNames.IntegrationObservedValueWebhookSummary &&
                sessionEvent.Level == SessionEventLevel.Info &&
                sessionEvent.Message.Contains("delivered=1"));
    }

    [Fact]
    public async Task EmitAsync_drops_newest_update_when_delivery_queue_is_full()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var eventSink = new RecordingSessionEventSink();
        var handler = new BlockingHttpMessageHandler();
        await using var sink = new ModbusObservedValueWebhookSink(
            CreateOptions(queueCapacity: 1),
            eventSink,
            new HttpClient(handler));

        await sink.EmitAsync(CreateUpdateGroup(), timeout.Token);
        await handler.WaitUntilBlockedAsync(timeout.Token);

        for (var index = 0; index < 16; index++)
        {
            await sink.EmitAsync(CreateUpdateGroup(correlationId: $"002A:01:{index}"), timeout.Token);
        }

        Assert.True(sink.Dropped > 0);
        Assert.Contains(
            eventSink.Events,
            sessionEvent => sessionEvent.Name == SessionEventNames.IntegrationObservedValueWebhookDropped);

        handler.Release();
        await sink.DisposeAsync();

        Assert.Contains(
            eventSink.Events,
            sessionEvent =>
                sessionEvent.Name == SessionEventNames.IntegrationObservedValueWebhookSummary &&
                sessionEvent.Level == SessionEventLevel.Warning &&
                sessionEvent.Message.Contains("dropped="));
    }

    [Fact]
    public async Task EmitAsync_emits_first_failure_warning_and_counts_failed_delivery()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var eventSink = new RecordingSessionEventSink();
        var handler = new RecordingHttpMessageHandler(HttpStatusCode.InternalServerError);
        await using var sink = new ModbusObservedValueWebhookSink(
            CreateOptions(),
            eventSink,
            new HttpClient(handler));

        await sink.EmitAsync(CreateUpdateGroup(), timeout.Token);
        await sink.DisposeAsync();

        Assert.Equal(0, sink.Delivered);
        Assert.Equal(1, sink.Failed);
        Assert.Contains(
            eventSink.Events,
            sessionEvent =>
                sessionEvent.Name == SessionEventNames.IntegrationObservedValueWebhookDeliveryFailed &&
                sessionEvent.Level == SessionEventLevel.Warning);
    }

    private static ObservedValueWebhookOptions CreateOptions(
        ObservedValueWebhookRangeFilter? rangeFilter = null,
        int queueCapacity = 16)
    {
        return new ObservedValueWebhookOptions(
            Enabled: true,
            Url: new Uri("http://example.invalid/mitmi/observed-values"),
            Trigger: new ObservedValueWebhookTriggerOptions(
                rangeFilter is null ? [] : [rangeFilter.Value]),
            Delivery: new ObservedValueWebhookDeliveryOptions(
                TimeSpan.FromSeconds(1),
                queueCapacity));
    }

    private static ModbusObservedValueUpdateGroup CreateUpdateGroup(
        string correlationId = "002A:01")
    {
        var observedAt = DateTimeOffset.Parse("2026-07-03T10:00:10+00:00");
        var firstObservedAt = DateTimeOffset.Parse("2026-07-03T10:00:00+00:00");
        var unchangedCell = new ModbusObservedValueCellUpdate(
            Address: 0,
            PreviousValue: ModbusObservedValue.Register(0x1111),
            CurrentValue: ModbusObservedValue.Register(0x1111),
            Changed: false,
            FirstObservedAt: firstObservedAt,
            LastObservedAt: observedAt,
            LastChangedAt: firstObservedAt);
        var changedCell = new ModbusObservedValueCellUpdate(
            Address: 1,
            PreviousValue: ModbusObservedValue.Register(0x1111),
            CurrentValue: ModbusObservedValue.Register(0x2222),
            Changed: true,
            FirstObservedAt: firstObservedAt,
            LastObservedAt: observedAt,
            LastChangedAt: observedAt);

        return new ModbusObservedValueUpdateGroup(
            new SessionId("webhook-test"),
            new NetworkEndpoint("192.0.2.10", 502),
            UnitId: 1,
            FunctionCode: 3,
            Operation: "readHoldingRegisters",
            Table: ModbusObservedTable.HoldingRegisters,
            Address: 107,
            Quantity: 3,
            AddressBase: "zeroBasedPdu",
            AddressRange: "107-109",
            CorrelationId: correlationId,
            ObservedAt: observedAt,
            ObservedCells: [unchangedCell, changedCell],
            ChangedCells: [changedCell]);
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode statusCode;

        public RecordingHttpMessageHandler(HttpStatusCode statusCode)
        {
            this.statusCode = statusCode;
        }

        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(statusCode);
        }
    }

    private sealed class BlockingHttpMessageHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource blocked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            blocked.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }

        public async Task WaitUntilBlockedAsync(CancellationToken cancellationToken)
        {
            await blocked.Task.WaitAsync(cancellationToken);
        }

        public void Release()
        {
            release.TrySetResult();
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
