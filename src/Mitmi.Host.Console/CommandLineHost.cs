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
        if (!File.Exists(configurationPath))
        {
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
                        $"Configuration file '{configurationPath}' was not found, so a default one was created. Review endpoints before using MITMI with real devices.")
                ]);
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

        var protocolRegistry = BuildProtocolRegistry();
        var context = ConfigurationValidationContext.ForFile(
            configurationPath,
            protocolRegistry,
            warnOnPrivilegedPorts: !OperatingSystem.IsWindows());

        var validationResult = new ConfigurationValidator().Validate(parseResult.Document, context);
        if (validationResult.HasErrors)
        {
            RenderIssues(error, validationResult.Issues);
            return ExitCodes.ConfigurationInvalid;
        }

        RenderStartupDiagnostics(output, validationResult.RuntimeConfiguration!, validationResult.Warnings);

        if (options.ValidateConfig)
        {
            await output.WriteLineAsync("Configuration valid.");
            return ExitCodes.Success;
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
            await using var captureSink = CreateTrafficCaptureSink(
                validationResult.RuntimeConfiguration!,
                eventSink);
            if (captureSink is not null)
            {
                await output.WriteLineAsync($"Writing capture records to {captureSink.CaptureFilePath}.");
            }

            var protocolTrafficObserverFactory = BuildProtocolTrafficObserverFactory(
                validationResult.RuntimeConfiguration!,
                eventSink);
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

    private static NdjsonTrafficCaptureSink? CreateTrafficCaptureSink(
        RuntimeConfiguration configuration,
        ISessionEventSink eventSink)
    {
        if (!configuration.Capture.Enabled)
        {
            return null;
        }

        return new NdjsonTrafficCaptureSink(
            configuration.Capture,
            eventSink,
            DateTimeOffset.UtcNow);
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

            await File.WriteAllTextAsync(configurationPath, DefaultConfigurationTemplate, cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
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

    private static ProtocolRegistry BuildProtocolRegistry()
    {
        var registry = new ProtocolRegistry();
        ModbusProtocolRegistration.Register(registry);
        return registry;
    }

    private static IProtocolTrafficObserverFactory? BuildProtocolTrafficObserverFactory(
        RuntimeConfiguration configuration,
        ISessionEventSink eventSink)
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
            return new ModbusTcpTrafficObserverFactory(eventSink);
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
        writer.WriteLine($"  Default config: {DefaultConfigurationFileName} beside the application executable.");
    }

    private const string DefaultConfigurationTemplate = """
        {
          "configurationVersion": 1,
          "logging": {
            "console": {
              "enabled": true,
              "minimumLevel": "Info"
            },
            "file": {
              "enabled": true,
              "minimumLevel": "Info",
              "path": "./logs/mitmi.log"
            }
          },
          "capture": {
            "enabled": true,
            "outputPath": "./captures",
            "retention": {
              "mode": "Manual"
            }
          },
          "metrics": {
            "enabled": true,
            "sink": "Log"
          },
          "session": {
            "id": "default",
            "protocol": "modbus-tcp",
            "listenEndpoint": {
              "address": "0.0.0.0",
              "port": 1502
            },
            "upstreamEndpoint": {
              "address": "127.0.0.1",
              "port": 502
            },
            "diagnostics": {
              "decodeProtocol": true,
              "captureRawPayloads": true
            }
          }
        }
        """;
}
