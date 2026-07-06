using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Mitmi.Application.Sessions;
using Mitmi.Domain;
using Mitmi.Protocols.Modbus.Diagnostics;

namespace Mitmi.Host.Console;

internal sealed class ModbusObservedValueWebhookSink : IModbusObservedValueUpdateSink, IAsyncDisposable
{
    private const int PayloadSchemaVersionValue = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ObservedValueWebhookOptions options;
    private readonly ISessionEventSink eventSink;
    private readonly HttpClient httpClient;
    private readonly Channel<WebhookPayloadDocument> channel;
    private readonly Task deliveryTask;
    private readonly CancellationTokenSource shutdown = new();
    private readonly object sessionContextGate = new();
    private int disposed;
    private int dropWarningEmitted;
    private int failureWarningEmitted;
    private long delivered;
    private long failed;
    private long dropped;
    private SessionId? summarySessionId;

    public ModbusObservedValueWebhookSink(
        ObservedValueWebhookOptions options,
        ISessionEventSink eventSink)
        : this(options, eventSink, new HttpClient())
    {
    }

    internal ModbusObservedValueWebhookSink(
        ObservedValueWebhookOptions options,
        ISessionEventSink eventSink,
        HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(eventSink);
        ArgumentNullException.ThrowIfNull(httpClient);

        if (!options.Enabled)
        {
            throw new ArgumentException("Observed-value webhook options must be enabled.", nameof(options));
        }

        if (options.Url is null)
        {
            throw new ArgumentException("Observed-value webhook URL is required.", nameof(options));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Delivery.QueueCapacity);

        this.options = options;
        this.eventSink = eventSink;
        this.httpClient = httpClient;

        channel = Channel.CreateBounded<WebhookPayloadDocument>(new BoundedChannelOptions(options.Delivery.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
        deliveryTask = Task.Run(DeliverAsync);
    }

    public long Delivered => Interlocked.Read(ref delivered);

    public long Failed => Interlocked.Read(ref failed);

    public long Dropped => Interlocked.Read(ref dropped);

    public ValueTask EmitAsync(
        ModbusObservedValueUpdateGroup updateGroup,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(updateGroup);
        cancellationToken.ThrowIfCancellationRequested();

        if (Volatile.Read(ref disposed) != 0 || !options.ShouldDeliver(updateGroup))
        {
            return ValueTask.CompletedTask;
        }

        RememberSessionId(updateGroup.SessionId);
        var payload = WebhookPayloadDocument.From(
            updateGroup,
            options.FilterObservedCells(updateGroup),
            options.FilterChangedCells(updateGroup));

        if (channel.Writer.TryWrite(payload))
        {
            return ValueTask.CompletedTask;
        }

        return EmitDroppedWarningAsync(updateGroup.SessionId, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        channel.Writer.TryComplete();
        try
        {
            await deliveryTask.WaitAsync(options.Delivery.Timeout);
        }
        catch (TimeoutException)
        {
            await EmitDeliveryFailureAsync(
                GetSummarySessionId().Value,
                "Observed-value webhook delivery did not drain before shutdown timeout; remaining queued updates were abandoned.");
            await shutdown.CancelAsync();
            await ObserveDeliveryTaskCancellationAsync();
        }

        shutdown.Dispose();
        httpClient.Dispose();

        await EmitSummaryAsync();
    }

    private async Task DeliverAsync()
    {
        try
        {
            await foreach (var payload in channel.Reader.ReadAllAsync(shutdown.Token))
            {
                await DeliverPayloadAsync(payload, shutdown.Token);
            }
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
        }
    }

    private async ValueTask DeliverPayloadAsync(
        WebhookPayloadDocument payload,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(options.Delivery.Timeout);
            using var response = await httpClient.PostAsJsonAsync(
                options.Url!,
                payload,
                JsonOptions,
                timeout.Token);

            if (response.IsSuccessStatusCode)
            {
                Interlocked.Increment(ref delivered);
                return;
            }

            var statusCode = response.StatusCode;
            var reasonPhrase = response.ReasonPhrase;
            await EmitDeliveryFailureAsync(
                payload.SessionId,
                $"Observed-value webhook returned HTTP {(int)statusCode} {reasonPhrase}.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            await EmitDeliveryFailureAsync(
                payload.SessionId,
                $"Observed-value webhook delivery failed: {exception.Message}",
                exception);
        }
    }

    private async ValueTask ObserveDeliveryTaskCancellationAsync()
    {
        try
        {
            await deliveryTask;
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
        }
    }

    private async ValueTask EmitDeliveryFailureAsync(
        string sessionId,
        string message,
        Exception? exception = null)
    {
        Interlocked.Increment(ref failed);
        if (Interlocked.CompareExchange(ref failureWarningEmitted, 1, 0) != 0)
        {
            return;
        }

        await eventSink.EmitAsync(
            new SessionEvent(
                DateTimeOffset.UtcNow,
                SessionEventLevel.Warning,
                SessionEventNames.IntegrationObservedValueWebhookDeliveryFailed,
                new SessionId(sessionId),
                ConnectionId: null,
                message,
                exception),
            CancellationToken.None);
    }

    private async ValueTask EmitDroppedWarningAsync(
        SessionId sessionId,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref dropped);
        RememberSessionId(sessionId);

        if (Interlocked.CompareExchange(ref dropWarningEmitted, 1, 0) != 0)
        {
            return;
        }

        await eventSink.EmitAsync(
            new SessionEvent(
                DateTimeOffset.UtcNow,
                SessionEventLevel.Warning,
                SessionEventNames.IntegrationObservedValueWebhookDropped,
                sessionId,
                ConnectionId: null,
                "Observed-value webhook queue is full; dropping newest updates until delivery catches up."),
            cancellationToken);
    }

    private async ValueTask EmitSummaryAsync()
    {
        var sessionId = GetSummarySessionId();
        var deliveredCount = Delivered;
        var failedCount = Failed;
        var droppedCount = Dropped;
        var level = failedCount > 0 || droppedCount > 0
            ? SessionEventLevel.Warning
            : SessionEventLevel.Info;

        await eventSink.EmitAsync(
            new SessionEvent(
                DateTimeOffset.UtcNow,
                level,
                SessionEventNames.IntegrationObservedValueWebhookSummary,
                sessionId,
                ConnectionId: null,
                $"Observed-value webhook summary delivered={deliveredCount.ToString(CultureInfo.InvariantCulture)} failed={failedCount.ToString(CultureInfo.InvariantCulture)} dropped={droppedCount.ToString(CultureInfo.InvariantCulture)}."),
            CancellationToken.None);
    }

    private SessionId GetSummarySessionId()
    {
        lock (sessionContextGate)
        {
            return summarySessionId ?? new SessionId("unknown");
        }
    }

    private void RememberSessionId(SessionId sessionId)
    {
        lock (sessionContextGate)
        {
            summarySessionId = sessionId;
        }
    }

    private sealed record WebhookPayloadDocument(
        int PayloadSchemaVersion,
        string SessionId,
        string UpstreamEndpoint,
        int UnitId,
        int FunctionCode,
        string Operation,
        string Table,
        string AddressBase,
        ushort RequestedAddress,
        ushort RequestedQuantity,
        string RequestedAddressRange,
        string ObservedAtUtc,
        string CorrelationId,
        IReadOnlyList<WebhookCellDocument> ObservedCells,
        IReadOnlyList<WebhookCellDocument> ChangedCells,
        string Summary)
    {
        public static WebhookPayloadDocument From(
            ModbusObservedValueUpdateGroup updateGroup,
            IReadOnlyList<ModbusObservedValueCellUpdate> observedCells,
            IReadOnlyList<ModbusObservedValueCellUpdate> changedCells)
        {
            ArgumentNullException.ThrowIfNull(updateGroup);
            ArgumentNullException.ThrowIfNull(observedCells);
            ArgumentNullException.ThrowIfNull(changedCells);

            return new WebhookPayloadDocument(
                PayloadSchemaVersionValue,
                updateGroup.SessionId.Value,
                updateGroup.UpstreamEndpoint.ToString(),
                updateGroup.UnitId,
                updateGroup.FunctionCode,
                updateGroup.Operation,
                FormatTable(updateGroup.Table),
                updateGroup.AddressBase,
                updateGroup.Address,
                updateGroup.Quantity,
                updateGroup.AddressRange,
                updateGroup.ObservedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
                updateGroup.CorrelationId,
                observedCells.Select(WebhookCellDocument.From).ToArray(),
                changedCells.Select(WebhookCellDocument.From).ToArray(),
                BuildSummary(updateGroup, changedCells));
        }

        private static string BuildSummary(
            ModbusObservedValueUpdateGroup updateGroup,
            IReadOnlyList<ModbusObservedValueCellUpdate> changedCells)
        {
            var changedText = changedCells.Count == 1 ? "1 changed cell" : $"{changedCells.Count.ToString(CultureInfo.InvariantCulture)} changed cells";
            return $"Observed {changedText} for unit {updateGroup.UnitId.ToString(CultureInfo.InvariantCulture)} {updateGroup.Operation} {FormatTable(updateGroup.Table)} range {updateGroup.AddressRange}.";
        }
    }

    private sealed record WebhookCellDocument(
        ushort Address,
        string ValueKind,
        object CurrentValue,
        string? CurrentValueHex,
        object? PreviousValue,
        string? PreviousValueHex,
        string FirstObservedAtUtc,
        string LastObservedAtUtc,
        string? LastChangedAtUtc)
    {
        public static WebhookCellDocument From(ModbusObservedValueCellUpdate cell)
        {
            return new WebhookCellDocument(
                cell.Address,
                FormatValueKind(cell.CurrentValue.Kind),
                FormatValue(cell.CurrentValue),
                FormatRegisterHex(cell.CurrentValue),
                cell.PreviousValue is null ? null : FormatValue(cell.PreviousValue.Value),
                cell.PreviousValue is null ? null : FormatRegisterHex(cell.PreviousValue.Value),
                cell.FirstObservedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
                cell.LastObservedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
                cell.LastChangedAt?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        }
    }

    private static string FormatTable(ModbusObservedTable table)
    {
        return table switch
        {
            ModbusObservedTable.Coils => "coils",
            ModbusObservedTable.DiscreteInputs => "discreteInputs",
            ModbusObservedTable.HoldingRegisters => "holdingRegisters",
            ModbusObservedTable.InputRegisters => "inputRegisters",
            _ => table.ToString()
        };
    }

    private static string FormatValueKind(ModbusObservedValueKind valueKind)
    {
        return valueKind switch
        {
            ModbusObservedValueKind.Boolean => "boolean",
            ModbusObservedValueKind.Register => "register",
            _ => valueKind.ToString()
        };
    }

    private static object FormatValue(ModbusObservedValue value)
    {
        return value.Kind switch
        {
            ModbusObservedValueKind.Boolean => value.BooleanValue,
            ModbusObservedValueKind.Register => value.RegisterValue,
            _ => value.ToString()
        };
    }

    private static string? FormatRegisterHex(ModbusObservedValue value)
    {
        return value.Kind == ModbusObservedValueKind.Register
            ? value.RegisterValue.ToString("x4", CultureInfo.InvariantCulture)
            : null;
    }
}
