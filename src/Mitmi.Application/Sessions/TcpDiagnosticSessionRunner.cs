using System.Buffers;
using System.Net;
using System.Net.Sockets;
using Mitmi.Application.Configuration;
using Mitmi.Domain;

namespace Mitmi.Application.Sessions;

public sealed class TcpDiagnosticSessionRunner
{
    private const int BufferSize = 81920;

    private long nextConnectionId;

    public async Task RunAsync(
        RuntimeConfiguration configuration,
        ISessionEventSink eventSink,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(eventSink);

        var session = configuration.Session;
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
                await HandleConnectionAsync(configuration, client, connectionId, eventSink, cancellationToken);
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
            await EmitAsync(
                eventSink,
                level,
                SessionEventNames.UpstreamConnectionFailed,
                session.Id,
                connectionId,
                $"Failed to connect upstream for connection {connectionId} to {session.UpstreamEndpoint}: {exception.Message}",
                exception,
                cancellationToken.IsCancellationRequested ? CancellationToken.None : cancellationToken);
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
            connectionCts.Token);
        var upstreamToClient = ForwardAsync(
            upstream.GetStream(),
            acceptedClient.GetStream(),
            connectionCts.Token);

        await Task.WhenAny(clientToUpstream, upstreamToClient);
        await connectionCts.CancelAsync();

        acceptedClient.Close();
        upstream.Close();

        await ObserveForwardingCompletionAsync(clientToUpstream, cancellationToken);
        await ObserveForwardingCompletionAsync(upstreamToClient, cancellationToken);

        await EmitAsync(
            eventSink,
            SessionEventLevel.Info,
            SessionEventNames.ConnectionClosed,
            session.Id,
            connectionId,
            $"Connection {connectionId} closed.",
            exception: null,
            cancellationToken: CancellationToken.None);
    }

    private static async Task ForwardAsync(
        NetworkStream source,
        NetworkStream destination,
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
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
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
