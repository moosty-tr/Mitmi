using Mitmi.Application.Configuration;
using Mitmi.Protocols.Modbus.Diagnostics;

namespace Mitmi.Host.Console;

internal sealed class ModbusAnalyzerArtifactsSink : IModbusTcpAnalyzerSummarySink
{
    private readonly NdjsonModbusAnalyzerSummarySink summarySink;
    private readonly MarkdownModbusDeviceDiscoveryReportSink discoveryReportSink;

    public ModbusAnalyzerArtifactsSink(
        RuntimeConfiguration configuration,
        DateTimeOffset startedAt)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        summarySink = new NdjsonModbusAnalyzerSummarySink(configuration.Capture, startedAt);
        discoveryReportSink = new MarkdownModbusDeviceDiscoveryReportSink(configuration, startedAt);
    }

    public string SummaryFilePath => summarySink.SummaryFilePath;

    public string DiscoveryReportFilePath => discoveryReportSink.ReportFilePath;

    public async ValueTask EmitAsync(
        IReadOnlyList<ModbusTcpAnalyzerSummaryRecord> records,
        CancellationToken cancellationToken)
    {
        await summarySink.EmitAsync(records, cancellationToken);
        await discoveryReportSink.EmitAsync(records, cancellationToken);
    }
}
