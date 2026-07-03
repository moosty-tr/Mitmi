using System.Net;
using System.Net.Sockets;
using Mitmi.Application.Configuration;
using Mitmi.Application.Sessions;
using Mitmi.Domain;
using Mitmi.Protocols.Modbus.Diagnostics;
using NModbus;
using NModbus.Data;

namespace Mitmi.IntegrationTests;

public sealed class ModbusTcpProxyIntegrationTests
{
    [Fact]
    public async Task RunAsync_proxies_nmodbus_client_to_nmodbus_server_and_emits_modbus_diagnostics()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var factory = new ModbusFactory();
        using var upstreamListener = new TcpListener(IPAddress.Loopback, 0);
        upstreamListener.Start();
        var upstreamPort = ((IPEndPoint)upstreamListener.LocalEndpoint).Port;
        var listenPort = ReserveTcpPort();
        var dataStore = new DefaultSlaveDataStore();
        dataStore.HoldingRegisters.WritePoints(0, [0x1234, 0x5678]);

        var slaveNetwork = factory.CreateSlaveNetwork(upstreamListener);
        slaveNetwork.AddSlave(factory.CreateSlave(1, dataStore));
        var slaveTask = slaveNetwork.ListenAsync(timeout.Token);

        var eventSink = new RecordingSessionEventSink();
        using var runnerCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        var runnerTask = new TcpDiagnosticSessionRunner(
            new ModbusTcpTrafficObserverFactory(eventSink)).RunAsync(
            CreateConfiguration(listenPort, upstreamPort),
            eventSink,
            runnerCancellation.Token);

        await eventSink.WaitForAsync(SessionEventNames.ListenerStarted, timeout.Token);

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, listenPort, timeout.Token);
        var master = factory.CreateMaster(client);
        var registers = await master.ReadHoldingRegistersAsync(1, 0, 2);

        Assert.Equal([0x1234, 0x5678], registers);

        var matched = await eventSink.WaitForAsync(SessionEventNames.ProtocolTransactionMatched, timeout.Token);
        Assert.Contains("function=3", matched.Message);
        Assert.Contains("operation=readHoldingRegisters", matched.Message);
        Assert.Contains("address=0", matched.Message);
        Assert.Contains("quantity=2", matched.Message);
        Assert.Contains("values=1234,5678", matched.Message);
        Assert.Contains("exception=False", matched.Message);
        Assert.Contains(eventSink.Events, sessionEvent => sessionEvent.Name == SessionEventNames.ProtocolFrameDecoded);
        Assert.Contains(eventSink.Events, sessionEvent => sessionEvent.Name == SessionEventNames.ProtocolTransactionObserved);

        client.Close();
        await eventSink.WaitForAsync(SessionEventNames.ConnectionClosed, timeout.Token);

        await runnerCancellation.CancelAsync();
        await runnerTask.WaitAsync(timeout.Token);
        Assert.Contains(
            eventSink.Events,
            sessionEvent =>
                sessionEvent.Name == SessionEventNames.ProtocolAnalyzerSummary &&
                sessionEvent.Message.Contains("operation=readHoldingRegisters") &&
                sessionEvent.Message.Contains("address_range=0-1") &&
                sessionEvent.Message.Contains("reads=1"));
        await timeout.CancelAsync();
        await IgnoreExpectedShutdownAsync(slaveTask);
    }

    private static RuntimeConfiguration CreateConfiguration(int listenPort, int upstreamPort)
    {
        return new RuntimeConfiguration(
            ConfigurationVersion: 1,
            ConfigurationFilePath: Path.Combine(Path.GetTempPath(), "mitmi.modbus.integration.json"),
            Logging: new LoggingRuntimeOptions(
                new LogSinkRuntimeOptions(true, ProductLogLevel.Info, null),
                new LogSinkRuntimeOptions(false, ProductLogLevel.Info, null)),
            Capture: new CaptureRuntimeOptions(false, Path.Combine(Path.GetTempPath(), "mitmi-captures"), CaptureRetentionMode.Manual),
            Metrics: new MetricsRuntimeOptions(false, "Log"),
            Session: new SessionRuntimeOptions(
                new SessionId("modbus-integration"),
                new ProtocolId("modbus-tcp"),
                new NetworkEndpoint(IPAddress.Loopback.ToString(), listenPort),
                new NetworkEndpoint(IPAddress.Loopback.ToString(), upstreamPort),
                new SessionDiagnosticsRuntimeOptions(DecodeProtocol: true, CaptureRawPayloads: false)));
    }

    private static int ReserveTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task IgnoreExpectedShutdownAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception exception) when (
            exception is OperationCanceledException ||
            exception is ObjectDisposedException ||
            exception is SocketException)
        {
        }
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
            try
            {
                return await waiter.Task;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                var events = string.Join(
                    Environment.NewLine,
                    Events.Select(sessionEvent => $"{sessionEvent.Name}: {sessionEvent.Message}"));
                throw new TimeoutException($"Timed out waiting for session event '{name}'. Observed events:{Environment.NewLine}{events}");
            }
        }
    }
}
