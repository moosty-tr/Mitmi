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

        Assert.Equal("serverToClient", document["direction"]!.GetValue<string>());
        Assert.Equal(2, document["payloadLength"]!.GetValue<int>());
        Assert.False(document.ContainsKey("rawPayloadBase64"));
    }

    private sealed class RecordingSessionEventSink : ISessionEventSink
    {
        private readonly List<SessionEvent> events = [];

        public IReadOnlyList<SessionEvent> Events => events;

        public ValueTask EmitAsync(SessionEvent sessionEvent, CancellationToken cancellationToken)
        {
            events.Add(sessionEvent);
            return ValueTask.CompletedTask;
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
