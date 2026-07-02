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
    private readonly StreamWriter writer;
    private readonly Task writerTask;
    private int dropWarningEmitted;
    private long droppedRecords;

    public NdjsonTrafficCaptureSink(
        CaptureRuntimeOptions capture,
        ISessionEventSink eventSink,
        DateTimeOffset startedAt,
        int capacity = DefaultCapacity)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(eventSink);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        this.eventSink = eventSink;

        Directory.CreateDirectory(capture.OutputPath);
        CaptureFilePath = Path.Combine(capture.OutputPath, BuildCaptureFileName(startedAt));

        channel = Channel.CreateBounded<TrafficCaptureRecord>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

        writer = new StreamWriter(new FileStream(
            CaptureFilePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.ReadWrite));
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
        string? RawPayloadBase64)
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
                record.RawPayload is null ? null : Convert.ToBase64String(record.RawPayload));
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
    }
}
