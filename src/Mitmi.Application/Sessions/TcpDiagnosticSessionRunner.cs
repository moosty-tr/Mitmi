using System.Buffers;
using System.Net;
using System.Net.Sockets;
using Mitmi.Application.Configuration;
using Mitmi.Domain;

namespace Mitmi.Application.Sessions;

public sealed class TcpDiagnosticSessionRunner
{
    private const int BufferSize = 81920;

    private readonly IProtocolTrafficObserverFactory? trafficObserverFactory;
    private readonly ITrafficCaptureSink? trafficCaptureSink;
    private readonly ISessionMetricsSink? sessionMetricsSink;
    private long nextConnectionId;

    public TcpDiagnosticSessionRunner(
        IProtocolTrafficObserverFactory? trafficObserverFactory = null,
        ITrafficCaptureSink? trafficCaptureSink = null,
        ISessionMetricsSink? sessionMetricsSink = null)
    {
        this.trafficObserverFactory = trafficObserverFactory;
        this.trafficCaptureSink = trafficCaptureSink;
        this.sessionMetricsSink = sessionMetricsSink;
    }

    public async Task RunAsync(
        RuntimeConfiguration configuration,
        ISessionEventSink eventSink,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(eventSink);

        var session = configuration.Session;
        var metricsCollector = sessionMetricsSink is null ? null : new SessionMetricsCollector(session.Id);
        var listenAddress = IPAddress.Parse(session.ListenEndpoint.Address);
        var listener = new TcpListener(listenAddress, session.ListenEndpoint.Port);

        try
        {
            listener.Start();
        }
        catch (SocketException exception)
        {
            await EmitAsync(
                eventSink,
                SessionEventLevel.Error,
                SessionEventNames.ListenerBindFailed,
                session.Id,
                connectionId: null,
                $"Failed to bind listener at {session.ListenEndpoint}: {exception.Message}",
                exception,
                cancellationToken);
            throw;
        }

        await EmitAsync(
            eventSink,
            SessionEventLevel.Info,
            SessionEventNames.ListenerStarted,
            session.Id,
            connectionId: null,
            $"Listening at {session.ListenEndpoint}; forwarding to {session.UpstreamEndpoint}.",
            exception: null,
            cancellationToken);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var connectionId = new ConnectionId(Interlocked.Increment(ref nextConnectionId));
                await HandleConnectionAsync(
                    configuration,
                    client,
                    connectionId,
                    trafficCaptureSink,
                    metricsCollector,
                    sessionMetricsSink,
                    trafficObserverFactory?.Create(session.Id, connectionId),
                    eventSink,
                    cancellationToken);
            }
        }
        finally
        {
            listener.Stop();
            await EmitAsync(
                eventSink,
                SessionEventLevel.Info,
                SessionEventNames.ListenerStopped,
                session.Id,
                connectionId: null,
                $"Stopped listening at {session.ListenEndpoint}.",
                exception: null,
                cancellationToken: CancellationToken.None);
            if (metricsCollector is not null && sessionMetricsSink is not null)
            {
                await EmitSessionMetricsSummaryAsync(
                    sessionMetricsSink,
                    eventSink,
                    session.Id,
                    metricsCollector.CreateSummary(),
                    CancellationToken.None);
            }

            await EmitAsync(
                eventSink,
                SessionEventLevel.Info,
                SessionEventNames.SessionStopped,
                session.Id,
                connectionId: null,
                "Diagnostic session stopped.",
                exception: null,
                cancellationToken: CancellationToken.None);
        }
    }

    private static async Task HandleConnectionAsync(
        RuntimeConfiguration configuration,
        TcpClient client,
        ConnectionId connectionId,
        ITrafficCaptureSink? trafficCaptureSink,
        SessionMetricsCollector? metricsCollector,
        ISessionMetricsSink? sessionMetricsSink,
        IProtocolTrafficObserver? trafficObserver,
        ISessionEventSink eventSink,
        CancellationToken cancellationToken)
    {
        var session = configuration.Session;
        using var acceptedClient = client;

        await EmitAsync(
            eventSink,
            SessionEventLevel.Info,
            SessionEventNames.ClientAccepted,
            session.Id,
            connectionId,
            $"Accepted client connection {connectionId}.",
            exception: null,
            cancellationToken);

        var connectionMetrics = metricsCollector?.BeginConnection(connectionId);
        using var upstream = new TcpClient();
        try
        {
            await upstream.ConnectAsync(
                session.UpstreamEndpoint.Address,
                session.UpstreamEndpoint.Port,
                cancellationToken);
        }
        catch (Exception exception) when (exception is SocketException or OperationCanceledException or IOException)
        {
            var level = cancellationToken.IsCancellationRequested
                ? SessionEventLevel.Info
                : SessionEventLevel.Error;
            if (!cancellationToken.IsCancellationRequested)
            {
                metricsCollector?.RecordUpstreamConnectionFailure();
            }

            await EmitAsync(
                eventSink,
                level,
                SessionEventNames.UpstreamConnectionFailed,
                session.Id,
                connectionId,
                $"Failed to connect upstream for connection {connectionId} to {session.UpstreamEndpoint}: {exception.Message}",
                exception,
                cancellationToken.IsCancellationRequested ? CancellationToken.None : cancellationToken);
            await DisposeProtocolTrafficObserverAsync(
                trafficObserver,
                eventSink,
                session.Id,
                connectionId,
                CancellationToken.None);
            await CompleteConnectionAsync(
                metricsCollector,
                sessionMetricsSink,
                connectionMetrics,
                eventSink,
                session.Id,
                connectionId,
                CancellationToken.None);
            return;
        }

        await EmitAsync(
            eventSink,
            SessionEventLevel.Info,
            SessionEventNames.UpstreamConnected,
            session.Id,
            connectionId,
            $"Connected upstream for connection {connectionId} to {session.UpstreamEndpoint}.",
            exception: null,
            cancellationToken);

        await EmitAsync(
            eventSink,
            SessionEventLevel.Info,
            SessionEventNames.ForwardingStarted,
            session.Id,
            connectionId,
            $"Forwarding connection {connectionId} without modification.",
            exception: null,
            cancellationToken);

        using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var clientToUpstream = ForwardAsync(
            acceptedClient.GetStream(),
            upstream.GetStream(),
            session.Id,
            connectionId,
            session.Protocol,
            TrafficDirection.ClientToServer,
            configuration.Session.Diagnostics.CaptureRawPayloads,
            trafficCaptureSink,
            connectionMetrics,
            trafficObserver,
            eventSink,
            connectionCts.Token);
        var upstreamToClient = ForwardAsync(
            upstream.GetStream(),
            acceptedClient.GetStream(),
            session.Id,
            connectionId,
            session.Protocol,
            TrafficDirection.ServerToClient,
            configuration.Session.Diagnostics.CaptureRawPayloads,
            trafficCaptureSink,
            connectionMetrics,
            trafficObserver,
            eventSink,
            connectionCts.Token);

        await Task.WhenAny(clientToUpstream, upstreamToClient);
        await connectionCts.CancelAsync();

        acceptedClient.Close();
        upstream.Close();

        await ObserveForwardingCompletionAsync(clientToUpstream, cancellationToken);
        await ObserveForwardingCompletionAsync(upstreamToClient, cancellationToken);
        await DisposeProtocolTrafficObserverAsync(
            trafficObserver,
            eventSink,
            session.Id,
            connectionId,
            CancellationToken.None);

        await CompleteConnectionAsync(
            metricsCollector,
            sessionMetricsSink,
            connectionMetrics,
            eventSink,
            session.Id,
            connectionId,
            CancellationToken.None);
    }

    private static async ValueTask CompleteConnectionAsync(
        SessionMetricsCollector? metricsCollector,
        ISessionMetricsSink? sessionMetricsSink,
        SessionMetricsCollector.ConnectionMetricsCollector? connectionMetrics,
        ISessionEventSink eventSink,
        SessionId sessionId,
        ConnectionId connectionId,
        CancellationToken cancellationToken)
    {
        if (metricsCollector is not null && sessionMetricsSink is not null && connectionMetrics is not null)
        {
            await EmitConnectionMetricsSummaryAsync(
                sessionMetricsSink,
                eventSink,
                sessionId,
                connectionId,
                metricsCollector.CloseConnection(connectionMetrics),
                cancellationToken);
        }

        await EmitAsync(
            eventSink,
            SessionEventLevel.Info,
            SessionEventNames.ConnectionClosed,
            sessionId,
            connectionId,
            $"Connection {connectionId} closed.",
            exception: null,
            cancellationToken);
    }

    private static async Task ForwardAsync(
        NetworkStream source,
        NetworkStream destination,
        SessionId sessionId,
        ConnectionId connectionId,
        ProtocolId protocolId,
        TrafficDirection direction,
        bool captureRawPayloads,
        ITrafficCaptureSink? trafficCaptureSink,
        SessionMetricsCollector.ConnectionMetricsCollector? connectionMetrics,
        IProtocolTrafficObserver? trafficObserver,
        ISessionEventSink eventSink,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var bytesRead = await source.ReadAsync(buffer, cancellationToken);
                if (bytesRead == 0)
                {
                    break;
                }

                await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                connectionMetrics?.RecordForwarded(direction, bytesRead);

                if (trafficCaptureSink is not null)
                {
                    await CaptureTrafficAsync(
                        trafficCaptureSink,
                        eventSink,
                        sessionId,
                        connectionId,
                        protocolId,
                        direction,
                        buffer.AsMemory(0, bytesRead),
                        captureRawPayloads,
                        cancellationToken);
                }

                if (trafficObserver is not null)
                {
                    await ObserveTrafficAsync(
                        trafficObserver,
                        eventSink,
                        sessionId,
                        connectionId,
                        direction,
                        buffer.AsMemory(0, bytesRead),
                        cancellationToken);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async ValueTask EmitConnectionMetricsSummaryAsync(
        ISessionMetricsSink metricsSink,
        ISessionEventSink eventSink,
        SessionId sessionId,
        ConnectionId connectionId,
        ConnectionMetricsSummary summary,
        CancellationToken cancellationToken)
    {
        try
        {
            await metricsSink.EmitConnectionSummaryAsync(summary, cancellationToken);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException ||
            !cancellationToken.IsCancellationRequested)
        {
            await EmitAsync(
                eventSink,
                SessionEventLevel.Warning,
                SessionEventNames.MetricsSinkFailed,
                sessionId,
                connectionId,
                $"Metrics sink failed while emitting connection summary: {exception.Message}",
                exception,
                cancellationToken);
        }
    }

    private static async ValueTask EmitSessionMetricsSummaryAsync(
        ISessionMetricsSink metricsSink,
        ISessionEventSink eventSink,
        SessionId sessionId,
        SessionMetricsSummary summary,
        CancellationToken cancellationToken)
    {
        try
        {
            await metricsSink.EmitSessionSummaryAsync(summary, cancellationToken);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException ||
            !cancellationToken.IsCancellationRequested)
        {
            await EmitAsync(
                eventSink,
                SessionEventLevel.Warning,
                SessionEventNames.MetricsSinkFailed,
                sessionId,
                connectionId: null,
                $"Metrics sink failed while emitting session summary: {exception.Message}",
                exception,
                cancellationToken);
        }
    }

    private static async ValueTask CaptureTrafficAsync(
        ITrafficCaptureSink trafficCaptureSink,
        ISessionEventSink eventSink,
        SessionId sessionId,
        ConnectionId connectionId,
        ProtocolId protocolId,
        TrafficDirection direction,
        ReadOnlyMemory<byte> payload,
        bool captureRawPayloads,
        CancellationToken cancellationToken)
    {
        try
        {
            await trafficCaptureSink.CaptureAsync(
                new TrafficCaptureRecord(
                    DateTimeOffset.UtcNow,
                    sessionId,
                    connectionId,
                    protocolId,
                    direction,
                    payload.Length,
                    captureRawPayloads ? payload.ToArray() : null),
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException ||
            !cancellationToken.IsCancellationRequested)
        {
            await EmitAsync(
                eventSink,
                SessionEventLevel.Warning,
                SessionEventNames.CaptureSinkFailed,
                sessionId,
                connectionId,
                $"Capture failed while observing {direction} traffic: {exception.Message}",
                exception,
                cancellationToken);
        }
    }

    private static async ValueTask ObserveTrafficAsync(
        IProtocolTrafficObserver trafficObserver,
        ISessionEventSink eventSink,
        SessionId sessionId,
        ConnectionId connectionId,
        TrafficDirection direction,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        try
        {
            await trafficObserver.ObserveAsync(
                new ProtocolTrafficObservation(
                    sessionId,
                    connectionId,
                    direction,
                    payload.ToArray()),
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException ||
            !cancellationToken.IsCancellationRequested)
        {
            await EmitAsync(
                eventSink,
                SessionEventLevel.Warning,
                SessionEventNames.ProtocolObserverFailed,
                sessionId,
                connectionId,
                $"Protocol diagnostics failed while observing {direction} traffic: {exception.Message}",
                exception,
                cancellationToken);
        }
    }

    private static async Task ObserveForwardingCompletionAsync(
        Task forwardingTask,
        CancellationToken cancellationToken)
    {
        try
        {
            await forwardingTask;
        }
        catch (Exception exception) when (
            exception is OperationCanceledException ||
            exception is IOException ||
            exception is ObjectDisposedException ||
            exception is SocketException ||
            cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static async ValueTask DisposeProtocolTrafficObserverAsync(
        IProtocolTrafficObserver? trafficObserver,
        ISessionEventSink eventSink,
        SessionId sessionId,
        ConnectionId connectionId,
        CancellationToken cancellationToken)
    {
        if (trafficObserver is not IAsyncDisposable asyncDisposableObserver)
        {
            return;
        }

        try
        {
            await asyncDisposableObserver.DisposeAsync();
        }
        catch (Exception exception)
        {
            await EmitAsync(
                eventSink,
                SessionEventLevel.Warning,
                SessionEventNames.ProtocolObserverFailed,
                sessionId,
                connectionId,
                $"Protocol diagnostics failed while flushing observations: {exception.Message}",
                exception,
                cancellationToken);
        }
    }

    private static ValueTask EmitAsync(
        ISessionEventSink eventSink,
        SessionEventLevel level,
        string name,
        SessionId sessionId,
        ConnectionId? connectionId,
        string message,
        Exception? exception,
        CancellationToken cancellationToken)
    {
        return eventSink.EmitAsync(
            new SessionEvent(
                DateTimeOffset.UtcNow,
                level,
                name,
                sessionId,
                connectionId,
                message,
                exception),
            cancellationToken);
    }
}
