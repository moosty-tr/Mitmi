namespace Mitmi.Application.Diagnostics;

public static class ConfigurationIssueCodes
{
    public const string CaptureManualRetention = "CAPTURE_MANUAL_RETENTION";
    public const string CaptureSensitiveData = "CAPTURE_SENSITIVE_DATA";
    public const string ConfigurationFileExists = "CONFIGURATION_FILE_EXISTS";
    public const string ConfigurationFileCreated = "CONFIGURATION_FILE_CREATED";
    public const string DebugLoggingEnabled = "DEBUG_LOGGING_ENABLED";
    public const string EmptySessionId = "EMPTY_SESSION_ID";
    public const string FileLoggingPathMissing = "FILE_LOGGING_PATH_MISSING";
    public const string IdenticalListenAndUpstreamEndpoints = "IDENTICAL_LISTEN_AND_UPSTREAM_ENDPOINTS";
    public const string InvalidJson = "INVALID_JSON";
    public const string InvalidListenEndpoint = "INVALID_LISTEN_ENDPOINT";
    public const string InvalidLoggingLevel = "INVALID_LOGGING_LEVEL";
    public const string InvalidUpstreamEndpoint = "INVALID_UPSTREAM_ENDPOINT";
    public const string MissingConfiguration = "MISSING_CONFIGURATION";
    public const string MissingConfigurationFile = "MISSING_CONFIGURATION_FILE";
    public const string MissingConfigurationVersion = "MISSING_CONFIGURATION_VERSION";
    public const string MissingLoggingConfiguration = "MISSING_LOGGING_CONFIGURATION";
    public const string MissingSession = "MISSING_SESSION";
    public const string PrivilegedListenPort = "PRIVILEGED_LISTEN_PORT";
    public const string RelativePathResolved = "RELATIVE_PATH_RESOLVED";
    public const string ReservedSessionId = "RESERVED_SESSION_ID";
    public const string UnknownCaptureRetentionMode = "UNKNOWN_CAPTURE_RETENTION_MODE";
    public const string UnknownMetricsSink = "UNKNOWN_METRICS_SINK";
    public const string UnknownProtocol = "UNKNOWN_PROTOCOL";
    public const string UnsupportedConfigurationVersion = "UNSUPPORTED_CONFIGURATION_VERSION";
}
