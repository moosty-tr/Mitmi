using System.Text.Json.Nodes;
using Mitmi.Application.Configuration;
using Mitmi.Application.Sessions;
using Mitmi.Domain;
using Mitmi.Host.Console;

namespace Mitmi.IntegrationTests;

public sealed class NdjsonTrafficCaptureSinkTests
{
    [Fact]
    public async Task CaptureAsync_writes_versioned_ndjson_records()
    {
        using var tempDirectory = new TemporaryDirectory();
        var eventSink = new RecordingSessionEventSink();
        var startedAt = new DateTimeOffset(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);
        var recordTimestamp = new DateTimeOffset(2026, 7, 2, 12, 1, 0, TimeSpan.Zero);
        string captureFilePath;

        await using (var captureSink = new NdjsonTrafficCaptureSink(
            new CaptureRuntimeOptions(true, tempDirectory.Path, CaptureRetentionMode.Manual),
            eventSink,
            startedAt,
            capacity: 4))
        {
            captureFilePath = captureSink.CaptureFilePath;
            await captureSink.CaptureAsync(
                new TrafficCaptureRecord(
                    recordTimestamp,
                    new SessionId("integration"),
                    new ConnectionId(7),
                    new ProtocolId("modbus-tcp"),
                    TrafficDirection.ClientToServer,
                    PayloadLength: 3,
                    RawPayload: [0x01, 0x02, 0x03]),
                CancellationToken.None);
        }

        Assert.EndsWith("mitmi-capture-20260702T1200000000000Z.ndjson", captureFilePath);
        var lines = await File.ReadAllLinesAsync(captureFilePath);
        var document = JsonNode.Parse(Assert.Single(lines))!.AsObject();

        Assert.Equal(1, document["captureFormatVersion"]!.GetValue<int>());
        Assert.Equal("2026-07-02T12:01:00.0000000Z", document["timestampUtc"]!.GetValue<string>());
        Assert.Equal("integration", document["sessionId"]!.GetValue<string>());
        Assert.Equal(7, document["connectionId"]!.GetValue<int>());
        Assert.Equal("trafficChunk", document["kind"]!.GetValue<string>());
        Assert.Equal("clientToServer", document["direction"]!.GetValue<string>());
        Assert.Equal("modbus-tcp", document["protocolId"]!.GetValue<string>());
        Assert.Equal(3, document["payloadLength"]!.GetValue<int>());
        Assert.Equal("AQID", document["rawPayloadBase64"]!.GetValue<string>());
        Assert.Empty(eventSink.Events);
    }

    [Fact]
    public async Task CaptureAsync_omits_raw_payload_when_record_does_not_include_it()
    {
        using var tempDirectory = new TemporaryDirectory();
        var eventSink = new RecordingSessionEventSink();
        string captureFilePath;

        await using (var captureSink = new NdjsonTrafficCaptureSink(
            new CaptureRuntimeOptions(true, tempDirectory.Path, CaptureRetentionMode.Manual),
            eventSink,
            new DateTimeOffset(2026, 7, 2, 12, 0, 0, TimeSpan.Zero),
            capacity: 4))
        {
            captureFilePath = captureSink.CaptureFilePath;
            await captureSink.CaptureAsync(
                new TrafficCaptureRecord(
                    DateTimeOffset.UtcNow,
                    new SessionId("integration"),
                    new ConnectionId(8),
                    new ProtocolId("modbus-tcp"),
                    TrafficDirection.ServerToClient,
                    PayloadLength: 2,
                    RawPayload: null),
                CancellationToken.None);
        }

        var document = JsonNode.Parse(Assert.Single(await File.ReadAllLinesAsync(captureFilePath)))!.AsObject();

        Assert.Equal("trafficChunk", document["kind"]!.GetValue<string>());
        Assert.Equal("serverToClient", document["direction"]!.GetValue<string>());
        Assert.Equal(2, document["payloadLength"]!.GetValue<int>());
        Assert.False(document.ContainsKey("rawPayloadBase64"));
        Assert.False(document.ContainsKey("correlationId"));
        Assert.False(document.ContainsKey("protocolMetadata"));
        Assert.False(document.ContainsKey("decodeWarnings"));
    }

    [Fact]
    public async Task CaptureAsync_writes_protocol_context_when_record_includes_it()
    {
        using var tempDirectory = new TemporaryDirectory();
        var eventSink = new RecordingSessionEventSink();
        string captureFilePath;

        await using (var captureSink = new NdjsonTrafficCaptureSink(
            new CaptureRuntimeOptions(true, tempDirectory.Path, CaptureRetentionMode.Manual),
            eventSink,
            new DateTimeOffset(2026, 7, 2, 12, 0, 0, TimeSpan.Zero),
            capacity: 4))
        {
            captureFilePath = captureSink.CaptureFilePath;
            await captureSink.CaptureAsync(
                new TrafficCaptureRecord(
                    DateTimeOffset.UtcNow,
                    new SessionId("integration"),
                    new ConnectionId(8),
                    new ProtocolId("modbus-tcp"),
                    TrafficDirection.ServerToClient,
                    PayloadLength: 13,
                    RawPayload: null,
                    CorrelationId: "002a:01",
                    ProtocolMetadata: new Dictionary<string, string>
                    {
                        ["transactionId"] = "42",
                        ["unitId"] = "1",
                        ["functionCode"] = "3"
                    },
                    DecodeWarnings:
                    [
                        new TrafficCaptureWarning(
                            "response_function_mismatch",
                            "Response function code did not match the request.")
                    ],
                    Kind: TrafficCaptureRecordKind.ProtocolFrame),
                CancellationToken.None);
        }

        var document = JsonNode.Parse(Assert.Single(await File.ReadAllLinesAsync(captureFilePath)))!.AsObject();

        Assert.Equal("protocolFrame", document["kind"]!.GetValue<string>());
        Assert.Equal("002a:01", document["correlationId"]!.GetValue<string>());

        var metadata = document["protocolMetadata"]!.AsObject();
        Assert.Equal("42", metadata["transactionId"]!.GetValue<string>());
        Assert.Equal("1", metadata["unitId"]!.GetValue<string>());
        Assert.Equal("3", metadata["functionCode"]!.GetValue<string>());

        var warning = Assert.Single(document["decodeWarnings"]!.AsArray());
        Assert.Equal("response_function_mismatch", warning!["code"]!.GetValue<string>());
        Assert.Equal("Response function code did not match the request.", warning["message"]!.GetValue<string>());
    }

    [Fact]
    public async Task CaptureAsync_reports_loss_summary_when_records_are_dropped()
    {
        using var tempDirectory = new TemporaryDirectory();
        var eventSink = new RecordingSessionEventSink();
        var writer = new BlockingTextWriter();

        await using (var captureSink = new NdjsonTrafficCaptureSink(
            new CaptureRuntimeOptions(true, tempDirectory.Path, CaptureRetentionMode.Manual),
            eventSink,
            new DateTimeOffset(2026, 7, 2, 12, 0, 0, TimeSpan.Zero),
            capacity: 1,
            writer))
        {
            await captureSink.CaptureAsync(CreateRecord(TrafficDirection.ClientToServer, [0x01]), CancellationToken.None);
            await writer.WaitForFirstWriteAsync();

            await captureSink.CaptureAsync(CreateRecord(TrafficDirection.ServerToClient, [0x02]), CancellationToken.None);
            await captureSink.CaptureAsync(CreateRecord(TrafficDirection.ClientToServer, [0x03]), CancellationToken.None);

            Assert.Equal(1, captureSink.DroppedRecords);

            writer.ReleaseFirstWrite();
        }

        Assert.Contains(eventSink.Events, sessionEvent => sessionEvent.Name == SessionEventNames.CaptureRecordDropped);

        var summary = Assert.Single(
            eventSink.Events,
            sessionEvent => sessionEvent.Name == SessionEventNames.CaptureRecordLossSummary);
        Assert.Equal(new SessionId("integration"), summary.SessionId);
        Assert.Null(summary.ConnectionId);
        Assert.Contains("Dropped 1 capture records", summary.Message);
    }

    private static TrafficCaptureRecord CreateRecord(
        TrafficDirection direction,
        byte[] payload)
    {
        return new TrafficCaptureRecord(
            DateTimeOffset.UtcNow,
            new SessionId("integration"),
            new ConnectionId(9),
            new ProtocolId("modbus-tcp"),
            direction,
            payload.Length,
            payload);
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

    private sealed class BlockingTextWriter : StringWriter
    {
        private readonly TaskCompletionSource firstWriteStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource releaseFirstWrite = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int shouldBlockFirstWrite = 1;

        public override async Task WriteLineAsync(string? value)
        {
            if (Interlocked.Exchange(ref shouldBlockFirstWrite, 0) == 1)
            {
                firstWriteStarted.TrySetResult();
                await releaseFirstWrite.Task;
            }

            await base.WriteLineAsync(value);
        }

        public Task WaitForFirstWriteAsync()
        {
            return firstWriteStarted.Task;
        }

        public void ReleaseFirstWrite()
        {
            releaseFirstWrite.TrySetResult();
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mitmi-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
