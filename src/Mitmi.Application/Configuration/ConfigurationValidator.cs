using System.Net;
using Mitmi.Application.Diagnostics;
using Mitmi.Domain;

namespace Mitmi.Application.Configuration;

public sealed class ConfigurationValidator
{
    private const int SupportedConfigurationVersion = 1;
    private const string DefaultConsoleLevel = "Info";
    private const string DefaultFileLevel = "Info";
    private const string DefaultMetricsSink = "Log";
    private const string DefaultCaptureOutputPath = "./captures";
    private const string DefaultCaptureRetentionMode = "Manual";

    public ConfigurationValidationResult Validate(
        MitmiConfigurationDocument? document,
        ConfigurationValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var issues = new List<ConfigurationIssue>();

        if (document is null)
        {
            issues.Add(Error(ConfigurationIssueCodes.MissingConfiguration, "Configuration is missing."));
            return new ConfigurationValidationResult(null, issues);
        }

        ValidateVersion(document.ConfigurationVersion, issues);

        var logging = ValidateLogging(document.Logging, context, issues);
        var capture = ValidateCapture(document.Capture, context, issues);
        var metrics = ValidateMetrics(document.Metrics, issues);
        var session = ValidateSession(document.Session, context, issues);

        if (issues.Any(issue => issue.Severity == ConfigurationIssueSeverity.Error))
        {
            return new ConfigurationValidationResult(null, issues);
        }

        return new ConfigurationValidationResult(
            new RuntimeConfiguration(
                SupportedConfigurationVersion,
                context.ConfigurationFilePath,
                logging!,
                capture!,
                metrics!,
                session!),
            issues);
    }

    private static void ValidateVersion(int? configurationVersion, ICollection<ConfigurationIssue> issues)
    {
        if (configurationVersion is null)
        {
            issues.Add(Error(ConfigurationIssueCodes.MissingConfigurationVersion, "Configuration version is required."));
            return;
        }

        if (configurationVersion != SupportedConfigurationVersion)
        {
            issues.Add(Error(
                ConfigurationIssueCodes.UnsupportedConfigurationVersion,
                $"Configuration version '{configurationVersion}' is not supported. Supported version is '{SupportedConfigurationVersion}'."));
        }
    }

    private static LoggingRuntimeOptions? ValidateLogging(
        LoggingConfigurationDocument? logging,
        ConfigurationValidationContext context,
        ICollection<ConfigurationIssue> issues)
    {
        if (logging is null)
        {
            issues.Add(Error(ConfigurationIssueCodes.MissingLoggingConfiguration, "Logging configuration is required."));
            return null;
        }

        var console = ValidateLogSink(
            "console",
            logging.Console,
            requiresPath: false,
            defaultMinimumLevel: DefaultConsoleLevel,
            context,
            issues);

        var file = ValidateLogSink(
            "file",
            logging.File,
            requiresPath: true,
            defaultMinimumLevel: DefaultFileLevel,
            context,
            issues);

        if (console is null || file is null)
        {
            return null;
        }

        return new LoggingRuntimeOptions(console, file);
    }

    private static LogSinkRuntimeOptions? ValidateLogSink(
        string name,
        LogSinkConfigurationDocument? sink,
        bool requiresPath,
        string defaultMinimumLevel,
        ConfigurationValidationContext context,
        ICollection<ConfigurationIssue> issues)
    {
        if (sink is null)
        {
            issues.Add(Error(
                ConfigurationIssueCodes.MissingLoggingConfiguration,
                $"Logging sink '{name}' is required."));
            return null;
        }

        var enabled = sink.Enabled ?? true;
        var levelText = string.IsNullOrWhiteSpace(sink.MinimumLevel)
            ? defaultMinimumLevel
            : sink.MinimumLevel;

        if (!Enum.TryParse<ProductLogLevel>(levelText, ignoreCase: true, out var minimumLevel))
        {
            issues.Add(Error(
                ConfigurationIssueCodes.InvalidLoggingLevel,
                $"Logging sink '{name}' has unsupported minimum level '{levelText}'."));
            return null;
        }

        string? resolvedPath = null;
        if (requiresPath && enabled)
        {
            if (string.IsNullOrWhiteSpace(sink.Path))
            {
                issues.Add(Error(
                    ConfigurationIssueCodes.FileLoggingPathMissing,
                    $"Logging sink '{name}' is enabled but does not define a file path."));
                return null;
            }

            resolvedPath = ResolvePath(sink.Path, context.ConfigurationDirectory);
            AddRelativePathWarning(sink.Path, resolvedPath, $"{name} log path", issues);
        }
        else if (!string.IsNullOrWhiteSpace(sink.Path))
        {
            resolvedPath = ResolvePath(sink.Path, context.ConfigurationDirectory);
            AddRelativePathWarning(sink.Path, resolvedPath, $"{name} log path", issues);
        }

        if (enabled && minimumLevel == ProductLogLevel.Debug)
        {
            issues.Add(Warning(
                ConfigurationIssueCodes.DebugLoggingEnabled,
                $"Logging sink '{name}' is set to Debug. Debug logs can be high-volume and sensitive."));
        }

        return new LogSinkRuntimeOptions(enabled, minimumLevel, resolvedPath);
    }

    private static CaptureRuntimeOptions? ValidateCapture(
        CaptureConfigurationDocument? capture,
        ConfigurationValidationContext context,
        ICollection<ConfigurationIssue> issues)
    {
        var enabled = capture?.Enabled ?? true;
        var outputPath = capture?.OutputPath;
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            outputPath = DefaultCaptureOutputPath;
        }

        var retentionModeText = capture?.Retention?.Mode;
        if (string.IsNullOrWhiteSpace(retentionModeText))
        {
            retentionModeText = DefaultCaptureRetentionMode;
        }

        if (!Enum.TryParse<CaptureRetentionMode>(retentionModeText, ignoreCase: true, out var retentionMode))
        {
            issues.Add(Error(
                ConfigurationIssueCodes.UnknownCaptureRetentionMode,
                $"Capture retention mode '{retentionModeText}' is not supported."));
            return null;
        }

        var resolvedOutputPath = ResolvePath(outputPath, context.ConfigurationDirectory);
        AddRelativePathWarning(outputPath, resolvedOutputPath, "capture output path", issues);

        if (enabled)
        {
            issues.Add(Warning(
                ConfigurationIssueCodes.CaptureManualRetention,
                "Capture retention is manual in v0.1. Review and clean capture files explicitly."));
            issues.Add(Warning(
                ConfigurationIssueCodes.CaptureSensitiveData,
                "Captures may contain sensitive industrial data, process values, addresses, and timing patterns."));
        }

        return new CaptureRuntimeOptions(enabled, resolvedOutputPath, retentionMode);
    }

    private static MetricsRuntimeOptions ValidateMetrics(
        MetricsConfigurationDocument? metrics,
        ICollection<ConfigurationIssue> issues)
    {
        var enabled = metrics?.Enabled ?? true;
        var sink = string.IsNullOrWhiteSpace(metrics?.Sink) ? DefaultMetricsSink : metrics.Sink;

        if (!string.Equals(sink, DefaultMetricsSink, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(Error(
                ConfigurationIssueCodes.UnknownMetricsSink,
                $"Metrics sink '{sink}' is not supported in this implementation slice."));
        }

        return new MetricsRuntimeOptions(enabled, DefaultMetricsSink);
    }

    private static SessionRuntimeOptions? ValidateSession(
        SessionConfigurationDocument? session,
        ConfigurationValidationContext context,
        ICollection<ConfigurationIssue> issues)
    {
        if (session is null)
        {
            issues.Add(Error(ConfigurationIssueCodes.MissingSession, "A single 'session' object is required."));
            return null;
        }

        if (string.IsNullOrWhiteSpace(session.Id))
        {
            issues.Add(Error(ConfigurationIssueCodes.EmptySessionId, "Session id is required."));
        }

        if (string.Equals(session.Id, "all", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(Error(ConfigurationIssueCodes.ReservedSessionId, "Session id 'all' is reserved for future multi-session operations."));
        }

        if (string.IsNullOrWhiteSpace(session.Protocol))
        {
            issues.Add(Error(ConfigurationIssueCodes.UnknownProtocol, "Session protocol is required."));
        }
        else if (!context.AvailableProtocolIds.Contains(session.Protocol))
        {
            issues.Add(Error(
                ConfigurationIssueCodes.UnknownProtocol,
                $"Protocol '{session.Protocol}' is not registered."));
        }

        var listenEndpoint = ValidateListenEndpoint(session.ListenEndpoint, context, issues);
        var upstreamEndpoint = ValidateUpstreamEndpoint(session.UpstreamEndpoint, issues);

        if (listenEndpoint is not null && upstreamEndpoint is not null &&
            string.Equals(listenEndpoint.Address, upstreamEndpoint.Address, StringComparison.OrdinalIgnoreCase) &&
            listenEndpoint.Port == upstreamEndpoint.Port)
        {
            issues.Add(Warning(
                ConfigurationIssueCodes.IdenticalListenAndUpstreamEndpoints,
                "Listen endpoint and upstream endpoint appear to be identical."));
        }

        if (issues.Any(issue => issue.Severity == ConfigurationIssueSeverity.Error))
        {
            return null;
        }

        var diagnostics = session.Diagnostics ?? new SessionDiagnosticsConfigurationDocument();

        return new SessionRuntimeOptions(
            new SessionId(session.Id!),
            new ProtocolId(session.Protocol!),
            listenEndpoint!,
            upstreamEndpoint!,
            new SessionDiagnosticsRuntimeOptions(
                diagnostics.DecodeProtocol ?? true,
                diagnostics.CaptureRawPayloads ?? true));
    }

    private static NetworkEndpoint? ValidateListenEndpoint(
        EndpointConfigurationDocument? endpoint,
        ConfigurationValidationContext context,
        ICollection<ConfigurationIssue> issues)
    {
        if (endpoint is null)
        {
            issues.Add(Error(ConfigurationIssueCodes.InvalidListenEndpoint, "Listen endpoint is required."));
            return null;
        }

        if (string.IsNullOrWhiteSpace(endpoint.Address) ||
            !IPAddress.TryParse(endpoint.Address, out _))
        {
            issues.Add(Error(
                ConfigurationIssueCodes.InvalidListenEndpoint,
                "Listen endpoint address must be an IP address or wildcard address."));
            return null;
        }

        if (!IsValidPort(endpoint.Port))
        {
            issues.Add(Error(ConfigurationIssueCodes.InvalidListenEndpoint, "Listen endpoint port must be between 1 and 65535."));
            return null;
        }

        if (context.WarnOnPrivilegedPorts && endpoint.Port < 1024)
        {
            issues.Add(Warning(
                ConfigurationIssueCodes.PrivilegedListenPort,
                "Listen port is below 1024 and may require elevated privileges on Unix-like systems."));
        }

        return new NetworkEndpoint(endpoint.Address, endpoint.Port!.Value);
    }

    private static NetworkEndpoint? ValidateUpstreamEndpoint(
        EndpointConfigurationDocument? endpoint,
        ICollection<ConfigurationIssue> issues)
    {
        if (endpoint is null)
        {
            issues.Add(Error(ConfigurationIssueCodes.InvalidUpstreamEndpoint, "Upstream endpoint is required."));
            return null;
        }

        if (string.IsNullOrWhiteSpace(endpoint.Address) ||
            Uri.CheckHostName(endpoint.Address) == UriHostNameType.Unknown)
        {
            issues.Add(Error(
                ConfigurationIssueCodes.InvalidUpstreamEndpoint,
                "Upstream endpoint address must be an IP address or DNS name."));
            return null;
        }

        if (!IsValidPort(endpoint.Port))
        {
            issues.Add(Error(ConfigurationIssueCodes.InvalidUpstreamEndpoint, "Upstream endpoint port must be between 1 and 65535."));
            return null;
        }

        return new NetworkEndpoint(endpoint.Address, endpoint.Port!.Value);
    }

    private static bool IsValidPort(int? port) => port is >= 1 and <= 65535;

    private static string ResolvePath(string path, string configurationDirectory)
    {
        if (Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        return Path.GetFullPath(Path.Combine(configurationDirectory, path));
    }

    private static void AddRelativePathWarning(
        string configuredPath,
        string resolvedPath,
        string label,
        ICollection<ConfigurationIssue> issues)
    {
        if (Path.IsPathRooted(configuredPath))
        {
            return;
        }

        issues.Add(Warning(
            ConfigurationIssueCodes.RelativePathResolved,
            $"Relative {label} '{configuredPath}' resolved to '{resolvedPath}'."));
    }

    private static ConfigurationIssue Error(string code, string message) =>
        new(ConfigurationIssueSeverity.Error, code, message);

    private static ConfigurationIssue Warning(string code, string message) =>
        new(ConfigurationIssueSeverity.Warning, code, message);
}
