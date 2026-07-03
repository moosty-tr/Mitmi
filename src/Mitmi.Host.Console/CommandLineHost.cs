using Mitmi.Application.Configuration;
using Mitmi.Application.Diagnostics;
using Mitmi.Application.Protocols;
using Mitmi.Application.Sessions;
using Mitmi.Protocols.Modbus;
using Mitmi.Protocols.Modbus.Diagnostics;

namespace Mitmi.Host.Console;

public static class CommandLineHost
{
    private const string DefaultConfigurationFileName = "mitmi.config.json";
    private const string DefaultConfigurationTemplateResourceName = "Mitmi.Host.Console.DefaultConfigurationTemplate";

    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default,
        string? applicationDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        var options = CliOptions.Parse(args);
        if (options.ShowHelp)
        {
            WriteUsage(output);
            return ExitCodes.Success;
        }

        if (options.Errors.Count > 0)
        {
            foreach (var cliError in options.Errors)
            {
                await error.WriteLineAsync(cliError);
            }

            WriteUsage(error);
            return ExitCodes.InvalidCommandLine;
        }

        var configurationPath = ResolveConfigurationPath(options, applicationDirectory);
        if (options.InitConfig)
        {
            return await InitializeConfigurationAsync(configurationPath, output, error, cancellationToken);
        }

        if (!File.Exists(configurationPath))
        {
            RenderIssues(
                error,
                [
                    new(
                        ConfigurationIssueSeverity.Error,
                        ConfigurationIssueCodes.MissingConfigurationFile,
                        $"Configuration file '{configurationPath}' was not found. Run 'mitmi --init-config --config \"{configurationPath}\"' to create a template.")
                ]);
            return ExitCodes.ConfigurationInvalid;
        }

        string json;
        try
        {
            json = await File.ReadAllTextAsync(configurationPath, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            RenderIssues(
                error,
                [
                    new ConfigurationIssue(
                        ConfigurationIssueSeverity.Error,
                        ConfigurationIssueCodes.MissingConfigurationFile,
                        $"Configuration file '{configurationPath}' could not be read: {exception.Message}")
                ]);
            return ExitCodes.ConfigurationInvalid;
        }

        var parseResult = ConfigurationDocumentParser.Parse(json);
        if (parseResult.HasErrors)
        {
            RenderIssues(error, parseResult.Issues);
            return ExitCodes.ConfigurationInvalid;
        }

        var configurationDocument = parseResult.Document!;
        var protocolRegistry = BuildProtocolRegistry();
        var context = ConfigurationValidationContext.ForFile(
            configurationPath,
            protocolRegistry,
            warnOnPrivilegedPorts: !OperatingSystem.IsWindows());

        var validationResult = new ConfigurationValidator().Validate(configurationDocument, context);
        if (validationResult.HasErrors)
        {
            RenderIssues(error, validationResult.Issues);
            return ExitCodes.ConfigurationInvalid;
        }

        var protocolOptionIssues = new List<ConfigurationIssue>();
        var modbusReportAddressOptions = ModbusReportAddressOptions.FromProtocolOptions(
            configurationDocument.Session!.ProtocolOptions,
            protocolOptionIssues);
        if (protocolOptionIssues.Count > 0)
        {
            RenderIssues(error, protocolOptionIssues);
            return ExitCodes.ConfigurationInvalid;
        }

        RenderStartupDiagnostics(output, validationResult.RuntimeConfiguration!, validationResult.Warnings);

        if (options.ValidateConfig)
        {
            await output.WriteLineAsync("Configuration valid.");
            return ExitCodes.Success;
        }

        if (options.DiagnosticsBundlePath is not null)
        {
            return await ExportDiagnosticsBundleAsync(
                validationResult.RuntimeConfiguration!,
                options.DiagnosticsBundlePath,
                output,
                error,
                cancellationToken);
        }

        await output.WriteLineAsync("Starting diagnostic session. Press Ctrl+C to stop.");
        using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            shutdown.Cancel();
        };

        System.Console.CancelKeyPress += cancelHandler;
        try
        {
            await using var textWriterEventSink = new TextWriterSessionEventSink(
                output,
                error,
                validationResult.RuntimeConfiguration!.Logging);
            await using var eventSink = new BoundedSessionEventSink(
                textWriterEventSink,
                validationResult.RuntimeConfiguration!.Session.Id);
            var startedAt = DateTimeOffset.UtcNow;
            await using var captureSink = CreateTrafficCaptureSink(
                validationResult.RuntimeConfiguration!,
                eventSink,
                startedAt);
            if (captureSink is not null)
            {
                await output.WriteLineAsync($"Writing capture records to {captureSink.CaptureFilePath}.");
            }

            var analyzerArtifactsSink = CreateModbusAnalyzerArtifactsSink(
                validationResult.RuntimeConfiguration!,
                startedAt,
                modbusReportAddressOptions);
            if (analyzerArtifactsSink is not null)
            {
                await output.WriteLineAsync($"Writing Modbus analyzer summary to {analyzerArtifactsSink.SummaryFilePath}.");
                await output.WriteLineAsync($"Writing Modbus discovery report to {analyzerArtifactsSink.DiscoveryReportFilePath}.");
            }

            var protocolTrafficObserverFactory = BuildProtocolTrafficObserverFactory(
                validationResult.RuntimeConfiguration!,
                eventSink,
                captureSink,
                analyzerArtifactsSink);
            var sessionMetricsSink = CreateSessionMetricsSink(
                validationResult.RuntimeConfiguration!,
                eventSink);

            await new TcpDiagnosticSessionRunner(
                protocolTrafficObserverFactory,
                captureSink,
                sessionMetricsSink).RunAsync(
                validationResult.RuntimeConfiguration!,
                eventSink,
                shutdown.Token);
            return ExitCodes.Success;
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            return ExitCodes.Success;
        }
        catch (Exception exception)
        {
            await error.WriteLineAsync($"Runtime failure: {exception.Message}");
            return ExitCodes.RuntimeFailure;
        }
        finally
        {
            System.Console.CancelKeyPress -= cancelHandler;
        }
    }

    private static async Task<int> InitializeConfigurationAsync(
        string configurationPath,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        if (File.Exists(configurationPath))
        {
            RenderIssues(
                error,
                [
                    new ConfigurationIssue(
                        ConfigurationIssueSeverity.Error,
                        ConfigurationIssueCodes.ConfigurationFileExists,
                        $"Configuration file '{configurationPath}' already exists. Refusing to overwrite it.")
                ]);
            return ExitCodes.ConfigurationInvalid;
        }

        if (!await TryCreateDefaultConfigurationAsync(configurationPath, error, cancellationToken))
        {
            return ExitCodes.ConfigurationInvalid;
        }

        RenderIssues(
            output,
            [
                new ConfigurationIssue(
                    ConfigurationIssueSeverity.Warning,
                    ConfigurationIssueCodes.ConfigurationFileCreated,
                    $"Configuration template '{configurationPath}' was created. Review endpoints before using MITMI with real devices.")
            ]);
        await output.WriteLineAsync($"Created configuration template at {configurationPath}.");
        return ExitCodes.Success;
    }

    private static NdjsonTrafficCaptureSink? CreateTrafficCaptureSink(
        RuntimeConfiguration configuration,
        ISessionEventSink eventSink,
        DateTimeOffset startedAt)
    {
        if (!configuration.Capture.Enabled)
        {
            return null;
        }

        return new NdjsonTrafficCaptureSink(
            configuration.Capture,
            eventSink,
            startedAt);
    }

    private static ModbusAnalyzerArtifactsSink? CreateModbusAnalyzerArtifactsSink(
        RuntimeConfiguration configuration,
        DateTimeOffset startedAt,
        ModbusReportAddressOptions reportAddressOptions)
    {
        if (!configuration.Capture.Enabled ||
            !configuration.Session.Diagnostics.DecodeProtocol ||
            !string.Equals(
                configuration.Session.Protocol.Value,
                ModbusTcpProtocolPlugin.ProtocolId,
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return new ModbusAnalyzerArtifactsSink(
            configuration,
            startedAt,
            reportAddressOptions);
    }

    private static async Task<int> ExportDiagnosticsBundleAsync(
        RuntimeConfiguration configuration,
        string bundlePath,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        try
        {
            var resolvedBundlePath = Path.GetFullPath(bundlePath);
            await DiagnosticsBundleExporter.ExportAsync(configuration, resolvedBundlePath, cancellationToken);
            await output.WriteLineAsync($"Created diagnostics bundle at {resolvedBundlePath}.");
            return ExitCodes.Success;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            await error.WriteLineAsync($"Diagnostics bundle failed: {exception.Message}");
            return ExitCodes.RuntimeFailure;
        }
    }

    private static ISessionMetricsSink? CreateSessionMetricsSink(
        RuntimeConfiguration configuration,
        ISessionEventSink eventSink)
    {
        if (!configuration.Metrics.Enabled)
        {
            return null;
        }

        return new SessionEventMetricsSink(eventSink);
    }

    private static string ResolveConfigurationPath(
        CliOptions options,
        string? applicationDirectory)
    {
        if (options.HasExplicitConfigurationPath)
        {
            return Path.GetFullPath(options.ConfigurationPath!);
        }

        var baseDirectory = string.IsNullOrWhiteSpace(applicationDirectory)
            ? AppContext.BaseDirectory
            : applicationDirectory;

        return Path.GetFullPath(Path.Combine(baseDirectory, DefaultConfigurationFileName));
    }

    private static async Task<bool> TryCreateDefaultConfigurationAsync(
        string configurationPath,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        try
        {
            var directory = Path.GetDirectoryName(configurationPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var file = new FileStream(
                configurationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);
            await using var template = OpenDefaultConfigurationTemplate();
            await template.CopyToAsync(file, cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            RenderIssues(
                error,
                [
                    new ConfigurationIssue(
                        ConfigurationIssueSeverity.Error,
                        ConfigurationIssueCodes.MissingConfigurationFile,
                        $"Configuration file '{configurationPath}' does not exist and could not be created: {exception.Message}")
                ]);
            return false;
        }
    }

    private static Stream OpenDefaultConfigurationTemplate()
    {
        return typeof(CommandLineHost).Assembly.GetManifestResourceStream(DefaultConfigurationTemplateResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded default configuration template '{DefaultConfigurationTemplateResourceName}' was not found.");
    }

    private static ProtocolRegistry BuildProtocolRegistry()
    {
        var registry = new ProtocolRegistry();
        ModbusProtocolRegistration.Register(registry);
        return registry;
    }

    private static IProtocolTrafficObserverFactory? BuildProtocolTrafficObserverFactory(
        RuntimeConfiguration configuration,
        ISessionEventSink eventSink,
        ITrafficCaptureSink? trafficCaptureSink,
        IModbusTcpAnalyzerSummarySink? analyzerSummarySink)
    {
        if (!configuration.Session.Diagnostics.DecodeProtocol)
        {
            return null;
        }

        if (string.Equals(
            configuration.Session.Protocol.Value,
            ModbusTcpProtocolPlugin.ProtocolId,
            StringComparison.OrdinalIgnoreCase))
        {
            return new BoundedProtocolTrafficObserverFactory(
                new ModbusTcpTrafficObserverFactory(
                    eventSink,
                    trafficCaptureSink,
                    configuration.Session.Diagnostics.CaptureRawPayloads,
                    analyzerSummarySink),
                eventSink);
        }

        return null;
    }

    private static void RenderStartupDiagnostics(
        TextWriter output,
        RuntimeConfiguration configuration,
        IReadOnlyList<ConfigurationIssue> warnings)
    {
        output.WriteLine("MITMI v0.1 diagnostic proxy configuration");
        output.WriteLine($"  Configuration: {configuration.ConfigurationFilePath}");
        output.WriteLine($"  Session: {configuration.Session.Id}");
        output.WriteLine($"  Protocol: {configuration.Session.Protocol}");
        output.WriteLine($"  Listen endpoint: {configuration.Session.ListenEndpoint}");
        output.WriteLine($"  Upstream endpoint: {configuration.Session.UpstreamEndpoint}");
        output.WriteLine($"  Console log level: {RenderSink(configuration.Logging.Console)}");
        output.WriteLine($"  File log level: {RenderSink(configuration.Logging.File)}");
        output.WriteLine($"  File log path: {configuration.Logging.File.Path ?? "(disabled)"}");
        output.WriteLine($"  Capture: {(configuration.Capture.Enabled ? "enabled" : "disabled")}");
        output.WriteLine($"  Capture path: {configuration.Capture.OutputPath}");
        output.WriteLine($"  Metrics: {(configuration.Metrics.Enabled ? configuration.Metrics.Sink : "disabled")}");
        output.WriteLine("  Client retargeting: configure clients to connect to MITMI's listen endpoint.");

        if (warnings.Count == 0)
        {
            return;
        }

        output.WriteLine();
        output.WriteLine("Warnings:");
        RenderIssues(output, warnings);
    }

    private static string RenderSink(LogSinkRuntimeOptions sink)
    {
        return sink.Enabled ? sink.MinimumLevel.ToString() : "disabled";
    }

    private static void RenderIssues(TextWriter writer, IReadOnlyList<ConfigurationIssue> issues)
    {
        foreach (var issue in issues)
        {
            writer.WriteLine($"[{issue.Severity}] {issue.Code}: {issue.Message}");
        }
    }

    private static void WriteUsage(TextWriter writer)
    {
        writer.WriteLine("Usage:");
        writer.WriteLine("  mitmi [--config <path>] [--validate-config]");
        writer.WriteLine("  mitmi --init-config [--config <path>]");
        writer.WriteLine("  mitmi --bundle-diagnostics <zip-path> [--config <path>]");
        writer.WriteLine($"  Default config: {DefaultConfigurationFileName} beside the application executable.");
    }
}
