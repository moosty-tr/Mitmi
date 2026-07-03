using System.Globalization;
using System.Text.Json;
using Mitmi.Application.Diagnostics;
using Mitmi.Protocols.Modbus;

namespace Mitmi.Host.Console;

internal sealed record ModbusReportAddressOptions(
    bool ShowOneBased,
    bool ShowReference)
{
    private const string ProtocolOptionsPropertyName = "protocolOptions";
    private const string ReportAddressColumnsPropertyName = "reportAddressColumns";
    private const string ZeroBasedColumnName = "zeroBasedPdu";
    private const string OneBasedColumnName = "oneBased";
    private const string ReferenceColumnName = "reference";

    public static ModbusReportAddressOptions Default { get; } = new(
        ShowOneBased: false,
        ShowReference: false);

    public IReadOnlyList<string> ColumnNames
    {
        get
        {
            var columns = new List<string> { ZeroBasedColumnName };
            if (ShowOneBased)
            {
                columns.Add(OneBasedColumnName);
            }

            if (ShowReference)
            {
                columns.Add(ReferenceColumnName);
            }

            return columns;
        }
    }

    public static ModbusReportAddressOptions FromProtocolOptions(
        IReadOnlyDictionary<string, JsonElement>? protocolOptions,
        ICollection<ConfigurationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);

        if (protocolOptions is null ||
            !protocolOptions.TryGetValue(ModbusTcpProtocolPlugin.ProtocolId, out var modbusOptions))
        {
            return Default;
        }

        if (modbusOptions.ValueKind != JsonValueKind.Object)
        {
            issues.Add(InvalidProtocolOptions(
                $"{ProtocolOptionsPropertyName}.{ModbusTcpProtocolPlugin.ProtocolId} must be an object."));
            return Default;
        }

        JsonElement? reportAddressColumns = null;
        foreach (var property in modbusOptions.EnumerateObject())
        {
            if (string.Equals(
                property.Name,
                ReportAddressColumnsPropertyName,
                StringComparison.OrdinalIgnoreCase))
            {
                reportAddressColumns = property.Value;
                continue;
            }

            issues.Add(InvalidProtocolOptions(
                $"{ProtocolOptionsPropertyName}.{ModbusTcpProtocolPlugin.ProtocolId}.{property.Name} is not supported."));
        }

        if (reportAddressColumns is null)
        {
            return Default;
        }

        if (reportAddressColumns.Value.ValueKind != JsonValueKind.Array)
        {
            issues.Add(InvalidProtocolOptions(
                $"{ProtocolOptionsPropertyName}.{ModbusTcpProtocolPlugin.ProtocolId}.{ReportAddressColumnsPropertyName} must be an array."));
            return Default;
        }

        var sawZeroBased = false;
        var showOneBased = false;
        var showReference = false;
        var index = 0;

        foreach (var column in reportAddressColumns.Value.EnumerateArray())
        {
            if (column.ValueKind != JsonValueKind.String)
            {
                issues.Add(InvalidProtocolOptions(
                    $"{ProtocolOptionsPropertyName}.{ModbusTcpProtocolPlugin.ProtocolId}.{ReportAddressColumnsPropertyName}[{index.ToString(CultureInfo.InvariantCulture)}] must be a string."));
                index++;
                continue;
            }

            var columnName = column.GetString();
            switch (columnName)
            {
                case var value when string.Equals(value, ZeroBasedColumnName, StringComparison.OrdinalIgnoreCase):
                    sawZeroBased = true;
                    break;

                case var value when string.Equals(value, OneBasedColumnName, StringComparison.OrdinalIgnoreCase):
                    showOneBased = true;
                    break;

                case var value when string.Equals(value, ReferenceColumnName, StringComparison.OrdinalIgnoreCase):
                    showReference = true;
                    break;

                default:
                    issues.Add(InvalidProtocolOptions(
                        $"{ProtocolOptionsPropertyName}.{ModbusTcpProtocolPlugin.ProtocolId}.{ReportAddressColumnsPropertyName}[{index.ToString(CultureInfo.InvariantCulture)}] has unsupported column '{columnName}'. Supported columns are '{ZeroBasedColumnName}', '{OneBasedColumnName}', and '{ReferenceColumnName}'."));
                    break;
            }

            index++;
        }

        if (!sawZeroBased)
        {
            issues.Add(InvalidProtocolOptions(
                $"{ProtocolOptionsPropertyName}.{ModbusTcpProtocolPlugin.ProtocolId}.{ReportAddressColumnsPropertyName} must include '{ZeroBasedColumnName}' so reports preserve the Modbus PDU source address."));
        }

        return issues.Any(issue =>
            issue.Severity == ConfigurationIssueSeverity.Error &&
            issue.Code == ConfigurationIssueCodes.InvalidProtocolOptions)
            ? Default
            : new ModbusReportAddressOptions(showOneBased, showReference);
    }

    private static ConfigurationIssue InvalidProtocolOptions(string message) =>
        new(
            ConfigurationIssueSeverity.Error,
            ConfigurationIssueCodes.InvalidProtocolOptions,
            message);
}
