using System.Globalization;
using System.Text;
using Mitmi.Application.Configuration;
using Mitmi.Protocols.Modbus.Diagnostics;

namespace Mitmi.Host.Console;

internal sealed class MarkdownModbusDeviceDiscoveryReportSink : IModbusTcpAnalyzerSummarySink
{
    public MarkdownModbusDeviceDiscoveryReportSink(
        RuntimeConfiguration configuration,
        DateTimeOffset startedAt)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        Configuration = configuration;
        var reportDirectoryPath = Path.Combine(configuration.Capture.OutputPath, "reports");
        ReportFilePath = Path.Combine(reportDirectoryPath, BuildReportFileName(startedAt));
    }

    public RuntimeConfiguration Configuration { get; }

    public string ReportFilePath { get; }

    public async ValueTask EmitAsync(
        IReadOnlyList<ModbusTcpAnalyzerSummaryRecord> records,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(records);

        var directory = Path.GetDirectoryName(ReportFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var content = Render(records);
        await File.WriteAllTextAsync(ReportFilePath, content, Encoding.UTF8, cancellationToken);
    }

    private string Render(IReadOnlyList<ModbusTcpAnalyzerSummaryRecord> records)
    {
        var generatedAt = records.Count > 0
            ? records.Max(record => record.Timestamp)
            : DateTimeOffset.UtcNow;

        var builder = new StringBuilder();
        builder.AppendLine("# MITMI Modbus Device Discovery Report");
        builder.AppendLine();
        builder.AppendLine($"- Generated UTC: {generatedAt.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)}");
        builder.AppendLine($"- Session: {Configuration.Session.Id.Value}");
        builder.AppendLine($"- Protocol: {Configuration.Session.Protocol.Value}");
        builder.AppendLine($"- Upstream device: {Configuration.Session.UpstreamEndpoint}");
        builder.AppendLine("- Address base: zeroBasedPdu");
        builder.AppendLine($"- Observed ranges: {records.Count.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"- Total requests: {records.Sum(record => record.Requests).ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"- Total responses: {records.Sum(record => record.Responses).ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"- Total exceptions: {records.Sum(record => record.Exceptions).ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine();

        if (records.Count == 0)
        {
            builder.AppendLine("No decoded Modbus transactions were observed.");
            return builder.ToString();
        }

        builder.AppendLine("## Observed Modbus Ranges");
        builder.AppendLine();
        builder.AppendLine("| Unit | Function | Operation | Address Range | Quantity | Reads | Writes | Requests | Responses | Exceptions |");
        builder.AppendLine("| ---: | ---: | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |");

        foreach (var record in records)
        {
            builder.Append("| ");
            builder.Append(record.UnitId.ToString(CultureInfo.InvariantCulture));
            builder.Append(" | ");
            builder.Append(record.FunctionCode.ToString(CultureInfo.InvariantCulture));
            builder.Append(" | ");
            builder.Append(EscapeTableCell(record.Operation));
            builder.Append(" | ");
            builder.Append(EscapeTableCell(record.AddressRange ?? "unknown"));
            builder.Append(" | ");
            builder.Append(record.Quantity?.ToString(CultureInfo.InvariantCulture) ?? "unknown");
            builder.Append(" | ");
            builder.Append(record.Reads.ToString(CultureInfo.InvariantCulture));
            builder.Append(" | ");
            builder.Append(record.Writes.ToString(CultureInfo.InvariantCulture));
            builder.Append(" | ");
            builder.Append(record.Requests.ToString(CultureInfo.InvariantCulture));
            builder.Append(" | ");
            builder.Append(record.Responses.ToString(CultureInfo.InvariantCulture));
            builder.Append(" | ");
            builder.Append(record.Exceptions.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine(" |");
        }

        builder.AppendLine();
        builder.AppendLine("Addresses are Modbus Protocol Data Unit offsets. Device manuals may use one-based or reference-style addresses such as 30001 or 40001.");
        return builder.ToString();
    }

    private static string BuildReportFileName(DateTimeOffset startedAt)
    {
        return $"mitmi-modbus-device-discovery-{startedAt.UtcDateTime.ToString("yyyyMMddTHHmmssfffffff'Z'", CultureInfo.InvariantCulture)}.md";
    }

    private static string EscapeTableCell(string value)
    {
        return value.Replace("|", "\\|", StringComparison.Ordinal);
    }
}
