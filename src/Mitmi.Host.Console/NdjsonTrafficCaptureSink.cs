using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Mitmi.Application.Configuration;
using Mitmi.Application.Sessions;
using Mitmi.Domain;

namespace Mitmi.Host.Console;

internal sealed class NdjsonTrafficCaptureSink : ITrafficCaptureSink, IAsyncDisposable
{
    private const int CaptureFormatVersionValue = 1;
    private const int DefaultCapacity = 4096;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly Channel<TrafficCaptureRecord> channel;
    private readonly ISessionEventSink eventSink;
    private readonly TextWriter writer;
    private readonly Task writerTask;
    private readonly object droppedRecordContextGate = new();
    private int dropWarningEmitted;
    private long droppedRecords;
    private SessionId? droppedRecordSessionId;

    public NdjsonTrafficCaptureSink(
        CaptureRuntimeOptions capture,
        ISessionEventSink eventSink,
        DateTimeOffset startedAt,
        int capacity = DefaultCapacity)
        : this(
            capture,
            eventSink,
            capacity,
            CreateFileWriter(capture, startedAt))
    {
    }

    internal NdjsonTrafficCaptureSink(
        CaptureRuntimeOptions capture,
        ISessionEventSink eventSink,
        DateTimeOffset startedAt,
        int capacity,
        TextWriter writer)
        : this(
            capture,
            eventSink,
            capacity,
            CreateProvidedWriter(capture, startedAt, writer))
    {
    }

    private NdjsonTrafficCaptureSink(
        CaptureRuntimeOptions capture,
        ISessionEventSink eventSink,
        int capacity,
        CaptureWriter captureWriter)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(eventSink);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        this.eventSink = eventSink;
        writer = captureWriter.Writer;
        CaptureFilePath = captureWriter.FilePath;

        channel = Channel.CreateBounded<TrafficCaptureRecord>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

        writerTask = Task.Run(WriteRecordsAsync);
    }

    public string CaptureFilePath { get; }

    public long DroppedRecords => Interlocked.Read(ref droppedRecords);

    public ValueTask CaptureAsync(TrafficCaptureRecord record, CancellationToken cancellationToken)
    {
        if (channel.Writer.TryWrite(record))
        {
            return ValueTask.CompletedTask;
        }

        return EmitDroppedRecordWarningAsync(record, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        channel.Writer.TryComplete();
        try
        {
            await writerTask;
            await writer.FlushAsync();
        }
        finally
        {
            await writer.DisposeAsync();
        }

        await EmitDroppedRecordSummaryAsync();
    }

    private static CaptureWriter CreateFileWriter(
        CaptureRuntimeOptions capture,
        DateTimeOffset startedAt)
    {
        ArgumentNullException.ThrowIfNull(capture);

        Directory.CreateDirectory(capture.OutputPath);
        var captureFilePath = Path.Combine(capture.OutputPath, BuildCaptureFileName(startedAt));
        return new CaptureWriter(
            new StreamWriter(new FileStream(
                captureFilePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.ReadWrite)),
            captureFilePath);
    }

    private static CaptureWriter CreateProvidedWriter(
        CaptureRuntimeOptions capture,
        DateTimeOffset startedAt,
        TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(writer);

        return new CaptureWriter(
            writer,
            Path.Combine(capture.OutputPath, BuildCaptureFileName(startedAt)));
    }

    private static string BuildCaptureFileName(DateTimeOffset startedAt)
    {
        return $"mitmi-capture-{startedAt.UtcDateTime.ToString("yyyyMMddTHHmmssfffffff'Z'", CultureInfo.InvariantCulture)}.ndjson";
    }

    private async ValueTask EmitDroppedRecordWarningAsync(
        TrafficCaptureRecord record,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref droppedRecords);
        lock (droppedRecordContextGate)
        {
            droppedRecordSessionId = record.SessionId;
        }

        if (Interlocked.CompareExchange(ref dropWarningEmitted, 1, 0) != 0)
        {
            return;
        }

        await eventSink.EmitAsync(
            new SessionEvent(
                DateTimeOffset.UtcNow,
                SessionEventLevel.Warning,
                SessionEventNames.CaptureRecordDropped,
                record.SessionId,
                record.ConnectionId,
                "Capture queue is full; dropping newest capture records until the writer catches up."),
            cancellationToken);
    }

    private async ValueTask EmitDroppedRecordSummaryAsync()
    {
        var dropped = DroppedRecords;
        if (dropped == 0)
        {
            return;
        }

        SessionId sessionId;
        lock (droppedRecordContextGate)
        {
            sessionId = droppedRecordSessionId ?? new SessionId("unknown");
        }

        await eventSink.EmitAsync(
            new SessionEvent(
                DateTimeOffset.UtcNow,
                SessionEventLevel.Warning,
                SessionEventNames.CaptureRecordLossSummary,
                sessionId,
                ConnectionId: null,
                $"Dropped {dropped} capture records because the capture queue was full."),
            CancellationToken.None);
    }

    private async Task WriteRecordsAsync()
    {
        await foreach (var record in channel.Reader.ReadAllAsync())
        {
            var document = CaptureRecordDocument.From(record);
            await writer.WriteLineAsync(JsonSerializer.Serialize(document, JsonOptions));
        }
    }

    private sealed record CaptureRecordDocument(
        int CaptureFormatVersion,
        string TimestampUtc,
        string SessionId,
        long ConnectionId,
        string Direction,
        string ProtocolId,
        int PayloadLength,
        string? RawPayloadBase64,
        string? CorrelationId,
        IReadOnlyDictionary<string, string>? ProtocolMetadata,
        IReadOnlyList<CaptureWarningDocument>? DecodeWarnings)
    {
        public static CaptureRecordDocument From(TrafficCaptureRecord record)
        {
            return new CaptureRecordDocument(
                CaptureFormatVersionValue,
                record.Timestamp.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
                record.SessionId.Value,
                record.ConnectionId.Value,
                FormatDirection(record.Direction),
                record.ProtocolId.Value,
                record.PayloadLength,
                record.RawPayload is null ? null : Convert.ToBase64String(record.RawPayload),
                record.CorrelationId,
                EmptyToNull(record.ProtocolMetadata),
                EmptyToNull(record.DecodeWarnings?.Select(CaptureWarningDocument.From).ToArray()));
        }

        private static string FormatDirection(TrafficDirection direction)
        {
            return direction switch
            {
                TrafficDirection.ClientToServer => "clientToServer",
                TrafficDirection.ServerToClient => "serverToClient",
                _ => direction.ToString()
            };
        }

        private static IReadOnlyDictionary<string, string>? EmptyToNull(
            IReadOnlyDictionary<string, string>? values)
        {
            return values is null || values.Count == 0 ? null : values;
        }

        private static IReadOnlyList<T>? EmptyToNull<T>(IReadOnlyList<T>? values)
        {
            return values is null || values.Count == 0 ? null : values;
        }
    }

    private sealed record CaptureWarningDocument(
        string Code,
        string Message)
    {
        public static CaptureWarningDocument From(TrafficCaptureWarning warning)
        {
            return new CaptureWarningDocument(warning.Code, warning.Message);
        }
    }

    private sealed record CaptureWriter(
        TextWriter Writer,
        string FilePath);
}
