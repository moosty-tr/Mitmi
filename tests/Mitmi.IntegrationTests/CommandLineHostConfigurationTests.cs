using System.IO.Compression;
using System.Text.Json.Nodes;
using Mitmi.Host.Console;

namespace Mitmi.IntegrationTests;

public sealed class CommandLineHostConfigurationTests
{
    [Fact]
    public async Task RunAsync_init_config_creates_default_config_in_application_directory()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CommandLineHost.RunAsync(
            ["--init-config"],
            output,
            error,
            applicationDirectory: tempDirectory.Path);

        var defaultConfigPath = Path.Combine(tempDirectory.Path, "mitmi.config.json");
        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(defaultConfigPath));
        Assert.Contains("CONFIGURATION_FILE_CREATED", output.ToString());
        Assert.Contains(defaultConfigPath, output.ToString());
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task RunAsync_init_config_matches_checked_in_example_configuration()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CommandLineHost.RunAsync(
            ["--init-config"],
            output,
            error,
            applicationDirectory: tempDirectory.Path);

        var defaultConfigPath = Path.Combine(tempDirectory.Path, "mitmi.config.json");
        var generatedConfiguration = await File.ReadAllTextAsync(defaultConfigPath);
        var exampleConfiguration = await File.ReadAllTextAsync(
            Path.Combine(FindRepositoryRoot(), "examples", "mitmi.config.example.json"));

        Assert.Equal(0, exitCode);
        Assert.Equal(
            NormalizeLineEndings(exampleConfiguration),
            NormalizeLineEndings(generatedConfiguration));
    }

    [Fact]
    public async Task RunAsync_without_config_parameter_reports_missing_default_config()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CommandLineHost.RunAsync(
            ["--validate-config"],
            output,
            error,
            applicationDirectory: tempDirectory.Path);

        var defaultConfigPath = Path.Combine(tempDirectory.Path, "mitmi.config.json");
        Assert.NotEqual(0, exitCode);
        Assert.False(File.Exists(defaultConfigPath));
        Assert.Empty(output.ToString());
        Assert.Contains("MISSING_CONFIGURATION_FILE", error.ToString());
        Assert.Contains("--init-config", error.ToString());
    }

    [Fact]
    public async Task RunAsync_uses_explicit_config_path_when_provided()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var explicitConfigPath = Path.Combine(tempDirectory.Path, "custom.config.json");
        await File.WriteAllTextAsync(explicitConfigPath, ValidConfigurationJson("custom"));

        var exitCode = await CommandLineHost.RunAsync(
            ["--config", explicitConfigPath, "--validate-config"],
            output,
            error,
            applicationDirectory: tempDirectory.Path);

        Assert.Equal(0, exitCode);
        Assert.Contains($"Configuration: {explicitConfigPath}", output.ToString());
        Assert.Contains("Session: custom", output.ToString());
        Assert.DoesNotContain("CONFIGURATION_FILE_CREATED", output.ToString());
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task RunAsync_init_config_creates_missing_explicit_config_path()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var explicitConfigPath = Path.Combine(tempDirectory.Path, "nested", "generated.config.json");

        var exitCode = await CommandLineHost.RunAsync(
            ["--init-config", "--config", explicitConfigPath],
            output,
            error,
            applicationDirectory: tempDirectory.Path);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(explicitConfigPath));
        Assert.Contains("CONFIGURATION_FILE_CREATED", output.ToString());
        Assert.Contains(explicitConfigPath, output.ToString());
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task RunAsync_init_config_refuses_to_overwrite_existing_config()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var explicitConfigPath = Path.Combine(tempDirectory.Path, "mitmi.config.json");
        await File.WriteAllTextAsync(explicitConfigPath, ValidConfigurationJson("existing"));

        var exitCode = await CommandLineHost.RunAsync(
            ["--init-config", "--config", explicitConfigPath],
            output,
            error,
            applicationDirectory: tempDirectory.Path);

        Assert.NotEqual(0, exitCode);
        Assert.Empty(output.ToString());
        Assert.Contains("CONFIGURATION_FILE_EXISTS", error.ToString());
        Assert.Contains("Refusing to overwrite", error.ToString());
    }

    [Fact]
    public async Task RunAsync_rejects_init_config_with_validate_config()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CommandLineHost.RunAsync(
            ["--init-config", "--validate-config"],
            output,
            error,
            applicationDirectory: tempDirectory.Path);

        Assert.NotEqual(0, exitCode);
        Assert.Empty(output.ToString());
        Assert.Contains("--init-config cannot be combined with --validate-config.", error.ToString());
    }

    [Fact]
    public async Task RunAsync_bundle_diagnostics_creates_zip_with_config_log_capture_and_manifest()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var configPath = Path.Combine(tempDirectory.Path, "mitmi.config.json");
        var logPath = Path.Combine(tempDirectory.Path, "logs", "mitmi.log");
        var capturePath = Path.Combine(tempDirectory.Path, "captures", "mitmi-capture-test.ndjson");
        var analyzerSummaryPath = Path.Combine(
            tempDirectory.Path,
            "captures",
            "summaries",
            "mitmi-modbus-analyzer-summary-test.ndjson");
        var discoveryReportPath = Path.Combine(
            tempDirectory.Path,
            "captures",
            "reports",
            "mitmi-modbus-device-discovery-test.md");
        var bundlePath = Path.Combine(tempDirectory.Path, "support", "mitmi-diagnostics.zip");

        await File.WriteAllTextAsync(configPath, ValidConfigurationJson("bundle"));
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        await File.WriteAllTextAsync(logPath, "protocol.analyzer_summary sample");
        Directory.CreateDirectory(Path.GetDirectoryName(capturePath)!);
        await File.WriteAllTextAsync(capturePath, """{"captureFormatVersion":1}""");
        Directory.CreateDirectory(Path.GetDirectoryName(analyzerSummaryPath)!);
        await File.WriteAllTextAsync(analyzerSummaryPath, """{"summaryFormatVersion":1}""");
        Directory.CreateDirectory(Path.GetDirectoryName(discoveryReportPath)!);
        await File.WriteAllTextAsync(discoveryReportPath, "# MITMI Modbus Device Discovery Report");

        var exitCode = await CommandLineHost.RunAsync(
            ["--config", configPath, "--bundle-diagnostics", bundlePath],
            output,
            error,
            applicationDirectory: tempDirectory.Path);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(bundlePath));
        Assert.Contains("Created diagnostics bundle", output.ToString());
        Assert.Empty(error.ToString());

        using var archive = ZipFile.OpenRead(bundlePath);
        Assert.Contains(archive.Entries, entry => entry.FullName == "configuration/mitmi.config.json");
        Assert.Contains(archive.Entries, entry => entry.FullName == "logs/mitmi.log");
        Assert.Contains(archive.Entries, entry => entry.FullName == "captures/mitmi-capture-test.ndjson");
        Assert.Contains(archive.Entries, entry => entry.FullName == "captures/summaries/mitmi-modbus-analyzer-summary-test.ndjson");
        Assert.Contains(archive.Entries, entry => entry.FullName == "captures/reports/mitmi-modbus-device-discovery-test.md");

        var manifestEntry = Assert.Single(archive.Entries, entry => entry.FullName == "manifest.json");
        await using var manifestStream = manifestEntry.Open();
        var manifest = JsonNode.Parse(manifestStream)!.AsObject();
        Assert.Equal(1, manifest["BundleManifestVersion"]!.GetValue<int>());
        Assert.Equal("bundle", manifest["SessionId"]!.GetValue<string>());
        Assert.Equal("modbus-tcp", manifest["ProtocolId"]!.GetValue<string>());
        Assert.Equal(bundlePath, manifest["BundlePath"]!.GetValue<string>());
    }

    [Fact]
    public async Task RunAsync_bundle_diagnostics_refuses_to_overwrite_existing_bundle()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var configPath = Path.Combine(tempDirectory.Path, "mitmi.config.json");
        var bundlePath = Path.Combine(tempDirectory.Path, "mitmi-diagnostics.zip");

        await File.WriteAllTextAsync(configPath, ValidConfigurationJson("existing-bundle"));
        await File.WriteAllTextAsync(bundlePath, "existing");

        var exitCode = await CommandLineHost.RunAsync(
            ["--config", configPath, "--bundle-diagnostics", bundlePath],
            output,
            error,
            applicationDirectory: tempDirectory.Path);

        Assert.NotEqual(0, exitCode);
        Assert.Contains("Diagnostics bundle failed", error.ToString());
        Assert.Contains("already exists", error.ToString());
    }

    [Fact]
    public async Task RunAsync_rejects_bundle_diagnostics_with_validate_config()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await CommandLineHost.RunAsync(
            ["--bundle-diagnostics", "mitmi.zip", "--validate-config"],
            output,
            error,
            applicationDirectory: tempDirectory.Path);

        Assert.NotEqual(0, exitCode);
        Assert.Empty(output.ToString());
        Assert.Contains("--validate-config cannot be combined with --bundle-diagnostics.", error.ToString());
    }

    [Fact]
    public async Task RunAsync_rejects_modbus_report_address_columns_without_zero_based_pdu()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var configPath = Path.Combine(tempDirectory.Path, "mitmi.config.json");
        await File.WriteAllTextAsync(
            configPath,
            ValidConfigurationJson(
                "invalid-report-columns",
                """
                ,
                "protocolOptions": {
                  "modbus-tcp": {
                    "reportAddressColumns": [
                      "oneBased",
                      "reference"
                    ]
                  }
                }
                """));

        var exitCode = await CommandLineHost.RunAsync(
            ["--config", configPath, "--validate-config"],
            output,
            error,
            applicationDirectory: tempDirectory.Path);

        Assert.NotEqual(0, exitCode);
        Assert.Empty(output.ToString());
        Assert.Contains("INVALID_PROTOCOL_OPTIONS", error.ToString());
        Assert.Contains("zeroBasedPdu", error.ToString());
    }

    private static string ValidConfigurationJson(
        string sessionId,
        string sessionTailJson = "") =>
        $$"""
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
              "id": "{{sessionId}}",
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
              {{sessionTailJson}}
            }
          }
          """;

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Mitmi.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root from the test output directory.");
    }

    private static string NormalizeLineEndings(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mitmi-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
