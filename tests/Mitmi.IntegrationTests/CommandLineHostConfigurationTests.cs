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

    private static string ValidConfigurationJson(string sessionId) =>
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
