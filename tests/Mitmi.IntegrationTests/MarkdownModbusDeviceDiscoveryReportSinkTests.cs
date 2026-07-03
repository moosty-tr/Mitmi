using Mitmi.Application.Configuration;
using Mitmi.Domain;
using Mitmi.Host.Console;
using Mitmi.Protocols.Modbus;
using Mitmi.Protocols.Modbus.Diagnostics;

namespace Mitmi.IntegrationTests;

public sealed class MarkdownModbusDeviceDiscoveryReportSinkTests
{
    [Fact]
    public async Task EmitAsync_writes_configured_address_columns()
    {
        using var tempDirectory = new TemporaryDirectory();
        var configuration = CreateRuntimeConfiguration(tempDirectory.Path);
        var sink = new MarkdownModbusDeviceDiscoveryReportSink(
            configuration,
            DateTimeOffset.Parse("2026-07-03T09:00:00+00:00"),
            new ModbusReportAddressOptions(
                ShowOneBased: true,
                ShowReference: true));

        await sink.EmitAsync(
            [
                new ModbusTcpAnalyzerSummaryRecord(
                    DateTimeOffset.Parse("2026-07-03T09:01:00+00:00"),
                    configuration.Session.Id,
                    UnitId: 1,
                    FunctionCode: 3,
                    Operation: "readHoldingRegisters",
                    Address: 0,
                    Quantity: 2,
                    AddressRange: "0-1",
                    AddressBase: "zeroBasedPdu",
                    Reads: 1,
                    Writes: 0,
                    Requests: 1,
                    Responses: 1,
                    Exceptions: 0),
                new ModbusTcpAnalyzerSummaryRecord(
                    DateTimeOffset.Parse("2026-07-03T09:01:00+00:00"),
                    configuration.Session.Id,
                    UnitId: 1,
                    FunctionCode: 1,
                    Operation: "readCoils",
                    Address: 0,
                    Quantity: 1,
                    AddressRange: "0",
                    AddressBase: "zeroBasedPdu",
                    Reads: 1,
                    Writes: 0,
                    Requests: 1,
                    Responses: 1,
                    Exceptions: 0)
            ],
            CancellationToken.None);

        var report = await File.ReadAllTextAsync(sink.ReportFilePath);

        Assert.Contains("- Address columns: zeroBasedPdu, oneBased, reference", report);
        Assert.Contains("| Unit | Function | Operation | PDU Address Range | One-Based Range | Reference Range | Quantity | Reads | Writes | Requests | Responses | Exceptions |", report);
        Assert.Contains("| 1 | 3 | readHoldingRegisters | 0-1 | 1-2 | 40001-40002 | 2 | 1 | 0 | 1 | 1 | 0 |", report);
        Assert.Contains("| 1 | 1 | readCoils | 0 | 1 | 00001 | 1 | 1 | 0 | 1 | 1 | 0 |", report);
        Assert.Contains("PDU address ranges are zero-based Modbus Protocol Data Unit offsets.", report);
        Assert.Contains("One-based ranges add 1", report);
        Assert.Contains("Reference ranges use common Modbus prefixes", report);
    }

    private static RuntimeConfiguration CreateRuntimeConfiguration(string tempDirectory)
    {
        return new RuntimeConfiguration(
            ConfigurationVersion: 1,
            ConfigurationFilePath: Path.Combine(tempDirectory, "mitmi.config.json"),
            Logging: new LoggingRuntimeOptions(
                new LogSinkRuntimeOptions(true, ProductLogLevel.Info, null),
                new LogSinkRuntimeOptions(true, ProductLogLevel.Info, Path.Combine(tempDirectory, "logs", "mitmi.log"))),
            Capture: new CaptureRuntimeOptions(
                Enabled: true,
                OutputPath: Path.Combine(tempDirectory, "captures"),
                RetentionMode: CaptureRetentionMode.Manual),
            Metrics: new MetricsRuntimeOptions(
                Enabled: true,
                Sink: "Log"),
            Session: new SessionRuntimeOptions(
                new SessionId("report-test"),
                new ProtocolId(ModbusTcpProtocolPlugin.ProtocolId),
                new NetworkEndpoint("127.0.0.1", 1502),
                new NetworkEndpoint("192.0.2.10", 502),
                new SessionDiagnosticsRuntimeOptions(
                    DecodeProtocol: true,
                    CaptureRawPayloads: true)));
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
