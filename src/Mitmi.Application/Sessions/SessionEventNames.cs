namespace Mitmi.Application.Sessions;

public static class SessionEventNames
{
    public const string CaptureRecordDropped = "capture.record_dropped";
    public const string CaptureRecordLossSummary = "capture.record_loss_summary";
    public const string CaptureSinkFailed = "capture.sink_failed";
    public const string ClientAccepted = "client.accepted";
    public const string ConnectionClosed = "connection.closed";
    public const string DiagnosticsEventDropped = "diagnostics.event_dropped";
    public const string DiagnosticsEventLossSummary = "diagnostics.event_loss_summary";
    public const string ForwardingStarted = "forwarding.started";
    public const string IntegrationObservedValueWebhookDeliveryFailed = "integration.observed_value_webhook.delivery_failed";
    public const string IntegrationObservedValueWebhookDropped = "integration.observed_value_webhook.dropped";
    public const string IntegrationObservedValueWebhookSummary = "integration.observed_value_webhook.summary";
    public const string ListenerBindFailed = "listener.bind_failed";
    public const string ListenerStarted = "listener.started";
    public const string ListenerStopped = "listener.stopped";
    public const string MetricsConnectionSummary = "metrics.connection_summary";
    public const string MetricsSessionSummary = "metrics.session_summary";
    public const string MetricsSinkFailed = "metrics.sink_failed";
    public const string ProtocolDecodeWarning = "protocol.decode_warning";
    public const string ProtocolAnalyzerSummary = "protocol.analyzer_summary";
    public const string ProtocolFrameDecoded = "protocol.frame_decoded";
    public const string ProtocolObservationDropped = "protocol.observation_dropped";
    public const string ProtocolObservationLossSummary = "protocol.observation_loss_summary";
    public const string ProtocolObserverFailed = "protocol.observer_failed";
    public const string ProtocolTransactionMatched = "protocol.transaction_matched";
    public const string ProtocolTransactionObserved = "protocol.transaction_observed";
    public const string ProtocolTransactionWarning = "protocol.transaction_warning";
    public const string SessionStopped = "session.stopped";
    public const string UpstreamConnected = "upstream.connected";
    public const string UpstreamConnectionFailed = "upstream.connection_failed";
}
