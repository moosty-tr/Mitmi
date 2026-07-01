using Mitmi.Application.Configuration;
using Mitmi.Application.Diagnostics;
using Mitmi.Application.Protocols;
using Mitmi.Application.Sessions;
using Mitmi.Protocols.Modbus;

namespace Mitmi.Host.Console;

public static class CommandLineHost
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken = default)
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

        var configurationPath = Path.GetFullPath(options.ConfigurationPath!);
        if (!File.Exists(configurationPath))
        {
            RenderIssues(
                error,
                [
                    new ConfigurationIssue(
                        ConfigurationIssueSeverity.Error,
                        ConfigurationIssueCodes.MissingConfigurationFile,
                        $"Configuration file '{configurationPath}' does not exist.")
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
            await new TcpDiagnosticSessionRunner().RunAsync(
                validationResult.RuntimeConfiguration!,
                new TextWriterSessionEventSink(output, error),
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

    private static ProtocolRegistry BuildProtocolRegistry()
    {
        var registry = new ProtocolRegistry();
        ModbusProtocolRegistration.Register(registry);
        return registry;
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
        writer.WriteLine("  mitmi --config <path> [--validate-config]");
    }
}
