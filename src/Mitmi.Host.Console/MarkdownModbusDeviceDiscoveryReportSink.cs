using System.Globalization;
using System.Text;
using Mitmi.Application.Configuration;
using Mitmi.Protocols.Modbus.Diagnostics;

namespace Mitmi.Host.Console;

internal sealed class MarkdownModbusDeviceDiscoveryReportSink : IModbusTcpAnalyzerSummarySink
{
    public MarkdownModbusDeviceDiscoveryReportSink(
        RuntimeConfiguration configuration,
        DateTimeOffset startedAt,
        ModbusReportAddressOptions addressOptions)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(addressOptions);

        Configuration = configuration;
        AddressOptions = addressOptions;
        var reportDirectoryPath = Path.Combine(configuration.Capture.OutputPath, "reports");
        ReportFilePath = Path.Combine(reportDirectoryPath, BuildReportFileName(startedAt));
    }

    public RuntimeConfiguration Configuration { get; }

    public ModbusReportAddressOptions AddressOptions { get; }

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
        builder.AppendLine($"- Address columns: {string.Join(", ", AddressOptions.ColumnNames)}");
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
        builder.Append("| Unit | Function | Operation | PDU Address Range |");
        if (AddressOptions.ShowOneBased)
        {
            builder.Append(" One-Based Range |");
        }

        if (AddressOptions.ShowReference)
        {
            builder.Append(" Reference Range |");
        }

        builder.AppendLine(" Quantity | Reads | Writes | Requests | Responses | Exceptions |");

        builder.Append("| ---: | ---: | --- | --- |");
        if (AddressOptions.ShowOneBased)
        {
            builder.Append(" --- |");
        }

        if (AddressOptions.ShowReference)
        {
            builder.Append(" --- |");
        }

        builder.AppendLine(" ---: | ---: | ---: | ---: | ---: | ---: |");

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
            if (AddressOptions.ShowOneBased)
            {
                builder.Append(EscapeTableCell(FormatOneBasedRange(record)));
                builder.Append(" | ");
            }

            if (AddressOptions.ShowReference)
            {
                builder.Append(EscapeTableCell(FormatReferenceRange(record)));
                builder.Append(" | ");
            }

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
        builder.AppendLine("PDU address ranges are zero-based Modbus Protocol Data Unit offsets.");
        if (AddressOptions.ShowOneBased)
        {
            builder.AppendLine("One-based ranges add 1 to the PDU address for manuals that count from 1.");
        }

        if (AddressOptions.ShowReference)
        {
            builder.AppendLine("Reference ranges use common Modbus prefixes by function code: 0xxxx coils, 1xxxx discrete inputs, 3xxxx input registers, and 4xxxx holding registers. Device manuals may differ.");
        }

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

    private static string FormatOneBasedRange(ModbusTcpAnalyzerSummaryRecord record)
    {
        if (!TryGetRange(record, offset: 1, out var start, out var end))
        {
            return "unknown";
        }

        return FormatRange(start, end);
    }

    private static string FormatReferenceRange(ModbusTcpAnalyzerSummaryRecord record)
    {
        if (!TryGetRange(record, offset: 0, out var start, out var end))
        {
            return "unknown";
        }

        var referenceBase = GetReferenceBase(record.FunctionCode);
        if (referenceBase is null)
        {
            return "not applicable";
        }

        return FormatRange(
            referenceBase.Value + start,
            referenceBase.Value + end,
            minimumDigits: 5);
    }

    private static bool TryGetRange(
        ModbusTcpAnalyzerSummaryRecord record,
        int offset,
        out int start,
        out int end)
    {
        if (record.Address is null ||
            record.Quantity is null ||
            record.Quantity.Value == 0)
        {
            start = 0;
            end = 0;
            return false;
        }

        start = record.Address.Value + offset;
        end = start + record.Quantity.Value - 1;
        return true;
    }

    private static int? GetReferenceBase(byte functionCode)
    {
        return functionCode switch
        {
            1 or 5 or 15 => 1,
            2 => 10001,
            3 or 6 or 16 => 40001,
            4 => 30001,
            _ => null
        };
    }

    private static string FormatRange(
        int start,
        int end,
        int minimumDigits = 0)
    {
        var startText = FormatAddress(start, minimumDigits);
        var endText = FormatAddress(end, minimumDigits);
        return start == end ? startText : $"{startText}-{endText}";
    }

    private static string FormatAddress(
        int value,
        int minimumDigits)
    {
        return minimumDigits == 0
            ? value.ToString(CultureInfo.InvariantCulture)
            : value.ToString($"D{minimumDigits.ToString(CultureInfo.InvariantCulture)}", CultureInfo.InvariantCulture);
    }
}
