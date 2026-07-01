using Mitmi.Application.Configuration;
using Mitmi.Application.Diagnostics;
using Mitmi.Application.Protocols;
using Mitmi.Protocols.Modbus;

namespace Mitmi.Application.Tests.Configuration;

public sealed class ConfigurationValidatorTests
{
    [Fact]
    public void Validate_accepts_minimal_valid_configuration()
    {
        var result = Validate(ValidConfigurationJson());

        Assert.False(result.HasErrors);
        Assert.NotNull(result.RuntimeConfiguration);
        Assert.Equal("default", result.RuntimeConfiguration.Session.Id.Value);
        Assert.Equal(ModbusTcpProtocolPlugin.ProtocolId, result.RuntimeConfiguration.Session.Protocol.Value);
        Assert.Equal("0.0.0.0", result.RuntimeConfiguration.Session.ListenEndpoint.Address);
        Assert.Equal(1502, result.RuntimeConfiguration.Session.ListenEndpoint.Port);
        Assert.Equal("127.0.0.1", result.RuntimeConfiguration.Session.UpstreamEndpoint.Address);
        Assert.Equal(502, result.RuntimeConfiguration.Session.UpstreamEndpoint.Port);
    }

    [Fact]
    public void Validate_resolves_relative_output_paths_against_configuration_directory()
    {
        var configurationPath = Path.Combine(Path.GetTempPath(), "mitmi-validation", "mitmi.json");
        var result = Validate(ValidConfigurationJson(), configurationPath);

        Assert.False(result.HasErrors);
        Assert.NotNull(result.RuntimeConfiguration);

        var configurationDirectory = Path.GetDirectoryName(Path.GetFullPath(configurationPath))!;
        Assert.Equal(
            Path.GetFullPath(Path.Combine(configurationDirectory, "logs", "mitmi.log")),
            result.RuntimeConfiguration.Logging.File.Path);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(configurationDirectory, "captures")),
            result.RuntimeConfiguration.Capture.OutputPath);
        Assert.Contains(result.Warnings, issue => issue.Code == ConfigurationIssueCodes.RelativePathResolved);
    }

    [Fact]
    public void Validate_reports_missing_session_with_stable_error_code()
    {
        var json = """
            {
              "configurationVersion": 1,
              "logging": {
                "console": { "enabled": true, "minimumLevel": "Info" },
                "file": { "enabled": true, "minimumLevel": "Info", "path": "./logs/mitmi.log" }
              }
            }
            """;

        var result = Validate(json);

        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, issue => issue.Code == ConfigurationIssueCodes.MissingSession);
    }

    [Fact]
    public void Validate_rejects_unknown_protocol()
    {
        var json = ValidConfigurationJson().Replace(
            $"\"protocol\": \"{ModbusTcpProtocolPlugin.ProtocolId}\"",
            "\"protocol\": \"unknown\"");

        var result = Validate(json);

        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, issue => issue.Code == ConfigurationIssueCodes.UnknownProtocol);
    }

    [Fact]
    public void Validate_rejects_invalid_listen_endpoint_before_runtime_start()
    {
        var json = ValidConfigurationJson().Replace("\"port\": 1502", "\"port\": 70000");

        var result = Validate(json);

        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, issue => issue.Code == ConfigurationIssueCodes.InvalidListenEndpoint);
    }

    [Fact]
    public void Validate_warns_when_capture_is_enabled()
    {
        var result = Validate(ValidConfigurationJson());

        Assert.False(result.HasErrors);
        Assert.Contains(result.Warnings, issue => issue.Code == ConfigurationIssueCodes.CaptureManualRetention);
        Assert.Contains(result.Warnings, issue => issue.Code == ConfigurationIssueCodes.CaptureSensitiveData);
    }

    [Fact]
    public void Parse_rejects_unknown_configuration_members()
    {
        var json = """
            {
              "configurationVersion": 1,
              "unexpected": true
            }
            """;

        var parseResult = ConfigurationDocumentParser.Parse(json);

        Assert.True(parseResult.HasErrors);
        Assert.Contains(parseResult.Issues, issue => issue.Code == ConfigurationIssueCodes.InvalidJson);
    }

    private static ConfigurationValidationResult Validate(
        string json,
        string? configurationPath = null)
    {
        var parseResult = ConfigurationDocumentParser.Parse(json);
        Assert.False(parseResult.HasErrors);

        var registry = new ProtocolRegistry();
        ModbusProtocolRegistration.Register(registry);

        var context = ConfigurationValidationContext.ForFile(
            configurationPath ?? Path.Combine(Path.GetTempPath(), "mitmi.json"),
            registry,
            warnOnPrivilegedPorts: false);

        return new ConfigurationValidator().Validate(parseResult.Document, context);
    }

    private static string ValidConfigurationJson() =>
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
              "id": "default",
              "protocol": "{{ModbusTcpProtocolPlugin.ProtocolId}}",
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
