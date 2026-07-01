using System.Net;
using System.Net.Sockets;
using Mitmi.Application.Configuration;
using Mitmi.Application.Sessions;
using Mitmi.Domain;

namespace Mitmi.IntegrationTests;

public sealed class TcpDiagnosticSessionRunnerTests
{
    [Fact]
    public async Task RunAsync_forwards_client_bytes_to_upstream_and_upstream_bytes_to_client()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var upstreamListener = new TcpListener(IPAddress.Loopback, 0);
        upstreamListener.Start();
        var upstreamPort = ((IPEndPoint)upstreamListener.LocalEndpoint).Port;
        var listenPort = ReserveTcpPort();
        var request = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var response = new byte[] { 0x10, 0x20, 0x30 };

        var upstreamTask = AcceptOneExchangeAsync(upstreamListener, request.Length, response, timeout.Token);
        var sink = new RecordingSessionEventSink();
        using var runnerCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var runnerTask = new TcpDiagnosticSessionRunner().RunAsync(
            CreateConfiguration(listenPort, upstreamPort),
            sink,
            runnerCancellation.Token);

        await sink.WaitForAsync(SessionEventNames.ListenerStarted, timeout.Token);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, listenPort, timeout.Token);
        await client.GetStream().WriteAsync(request, timeout.Token);
        var clientReceived = await ReadExactAsync(client.GetStream(), response.Length, timeout.Token);

        Assert.Equal(request, await upstreamTask);
        Assert.Equal(response, clientReceived);

        client.Close();
        await sink.WaitForAsync(SessionEventNames.ConnectionClosed, timeout.Token);

        await runnerCancellation.CancelAsync();
        await runnerTask.WaitAsync(timeout.Token);
    }

    [Fact]
    public async Task RunAsync_reports_upstream_connect_failure()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var listenPort = ReserveTcpPort();
        var upstreamPort = ReserveTcpPort();
        var sink = new RecordingSessionEventSink();
        using var runnerCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var runnerTask = new TcpDiagnosticSessionRunner().RunAsync(
            CreateConfiguration(listenPort, upstreamPort),
            sink,
            runnerCancellation.Token);

        await sink.WaitForAsync(SessionEventNames.ListenerStarted, timeout.Token);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, listenPort, timeout.Token);

        var failure = await sink.WaitForAsync(SessionEventNames.UpstreamConnectionFailed, timeout.Token);

        Assert.Equal(SessionEventLevel.Error, failure.Level);

        await runnerCancellation.CancelAsync();
        await runnerTask.WaitAsync(timeout.Token);
    }

    [Fact]
    public async Task RunAsync_stops_listener_when_cancelled()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var listenPort = ReserveTcpPort();
        var upstreamPort = ReserveTcpPort();
        var sink = new RecordingSessionEventSink();
        using var runnerCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var runnerTask = new TcpDiagnosticSessionRunner().RunAsync(
            CreateConfiguration(listenPort, upstreamPort),
            sink,
            runnerCancellation.Token);

        await sink.WaitForAsync(SessionEventNames.ListenerStarted, timeout.Token);

        await runnerCancellation.CancelAsync();
        await runnerTask.WaitAsync(timeout.Token);

        Assert.Contains(sink.Events, sessionEvent => sessionEvent.Name == SessionEventNames.ListenerStopped);
        Assert.Contains(sink.Events, sessionEvent => sessionEvent.Name == SessionEventNames.SessionStopped);
    }

    private static RuntimeConfiguration CreateConfiguration(int listenPort, int upstreamPort)
    {
        return new RuntimeConfiguration(
            ConfigurationVersion: 1,
            ConfigurationFilePath: Path.Combine(Path.GetTempPath(), "mitmi.integration.json"),
            Logging: new LoggingRuntimeOptions(
                new LogSinkRuntimeOptions(true, ProductLogLevel.Info, null),
                new LogSinkRuntimeOptions(false, ProductLogLevel.Info, null)),
            Capture: new CaptureRuntimeOptions(false, Path.Combine(Path.GetTempPath(), "mitmi-captures"), CaptureRetentionMode.Manual),
            Metrics: new MetricsRuntimeOptions(false, "Log"),
            Session: new SessionRuntimeOptions(
                new SessionId("integration"),
                new ProtocolId("modbus-tcp"),
                new NetworkEndpoint(IPAddress.Loopback.ToString(), listenPort),
                new NetworkEndpoint(IPAddress.Loopback.ToString(), upstreamPort),
                new SessionDiagnosticsRuntimeOptions(DecodeProtocol: false, CaptureRawPayloads: false)));
    }

    private static async Task<byte[]> AcceptOneExchangeAsync(
        TcpListener listener,
        int requestLength,
        byte[] response,
        CancellationToken cancellationToken)
    {
        using var upstream = await listener.AcceptTcpClientAsync(cancellationToken);
        var stream = upstream.GetStream();
        var received = await ReadExactAsync(stream, requestLength, cancellationToken);
        await stream.WriteAsync(response, cancellationToken);
        return received;
    }

    private static async Task<byte[]> ReadExactAsync(
        NetworkStream stream,
        int length,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), cancellationToken);
            if (bytesRead == 0)
            {
                throw new EndOfStreamException($"Expected {length} bytes but received {offset}.");
            }

            offset += bytesRead;
        }

        return buffer;
    }

    private static int ReserveTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class RecordingSessionEventSink : ISessionEventSink
    {
        private readonly object gate = new();
        private readonly List<SessionEvent> events = [];
        private readonly Dictionary<string, List<TaskCompletionSource<SessionEvent>>> waiters = new(StringComparer.Ordinal);

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
            List<TaskCompletionSource<SessionEvent>>? matchingWaiters = null;
            lock (gate)
            {
                events.Add(sessionEvent);
                if (waiters.Remove(sessionEvent.Name, out var waitersForName))
                {
                    matchingWaiters = waitersForName;
                }
            }

            if (matchingWaiters is not null)
            {
                foreach (var waiter in matchingWaiters)
                {
                    waiter.TrySetResult(sessionEvent);
                }
            }

            return ValueTask.CompletedTask;
        }

        public async Task<SessionEvent> WaitForAsync(string name, CancellationToken cancellationToken)
        {
            TaskCompletionSource<SessionEvent> waiter;
            lock (gate)
            {
                var existingEvent = events.FirstOrDefault(sessionEvent => sessionEvent.Name == name);
                if (existingEvent is not null)
                {
                    return existingEvent;
                }

                waiter = new TaskCompletionSource<SessionEvent>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (!waiters.TryGetValue(name, out var waitersForName))
                {
                    waitersForName = [];
                    waiters.Add(name, waitersForName);
                }

                waitersForName.Add(waiter);
            }

            await using var registration = cancellationToken.Register(() => waiter.TrySetCanceled(cancellationToken));
            return await waiter.Task;
        }
    }
}
