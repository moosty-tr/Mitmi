namespace Mitmi.Application.Sessions;

public static class SessionEventNames
{
    public const string ClientAccepted = "client.accepted";
    public const string ConnectionClosed = "connection.closed";
    public const string ForwardingStarted = "forwarding.started";
    public const string ListenerBindFailed = "listener.bind_failed";
    public const string ListenerStarted = "listener.started";
    public const string ListenerStopped = "listener.stopped";
    public const string SessionStopped = "session.stopped";
    public const string UpstreamConnected = "upstream.connected";
    public const string UpstreamConnectionFailed = "upstream.connection_failed";
}
