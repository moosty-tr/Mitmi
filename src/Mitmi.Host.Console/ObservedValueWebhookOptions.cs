using System.Globalization;
using System.Text.Json;
using Mitmi.Application.Configuration;
using Mitmi.Application.Diagnostics;
using Mitmi.Protocols.Modbus;
using Mitmi.Protocols.Modbus.Diagnostics;

namespace Mitmi.Host.Console;

internal sealed record ObservedValueWebhookOptions(
    bool Enabled,
    Uri? Url,
    ObservedValueWebhookTriggerOptions Trigger,
    ObservedValueWebhookDeliveryOptions Delivery)
{
    private const string IntegrationsPropertyName = "integrations";
    private const string ObservedValueWebhookPropertyName = "observedValueWebhook";
    private const string EnabledPropertyName = "enabled";
    private const string UrlPropertyName = "url";
    private const string TriggerPropertyName = "trigger";
    private const string DeliveryPropertyName = "delivery";
    private const string AuthenticationPropertyName = "authentication";
    private const string ModePropertyName = "mode";
    private const string RangesPropertyName = "ranges";
    private const string UnitIdPropertyName = "unitId";
    private const string TablePropertyName = "table";
    private const string StartAddressPropertyName = "startAddress";
    private const string EndAddressPropertyName = "endAddress";
    private const string TimeoutMillisecondsPropertyName = "timeoutMilliseconds";
    private const string QueueCapacityPropertyName = "queueCapacity";
    private const string ChangedCellsOnlyModeName = "ChangedCellsOnly";
    private const string NoneAuthenticationModeName = "None";
    private const int DefaultTimeoutMilliseconds = 2000;
    private const int DefaultQueueCapacity = 256;

    public static ObservedValueWebhookOptions Disabled { get; } = new(
        Enabled: false,
        Url: null,
        Trigger: ObservedValueWebhookTriggerOptions.Default,
        Delivery: ObservedValueWebhookDeliveryOptions.Default);

    public static ObservedValueWebhookOptions FromIntegrations(
        JsonElement? integrations,
        RuntimeConfiguration configuration,
        ICollection<ConfigurationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(issues);

        if (integrations is null)
        {
            return Disabled;
        }

        if (integrations.Value.ValueKind != JsonValueKind.Object)
        {
            issues.Add(InvalidIntegrationOptions($"{IntegrationsPropertyName} must be an object."));
            return Disabled;
        }

        JsonElement? observedValueWebhook = null;
        foreach (var property in integrations.Value.EnumerateObject())
        {
            if (string.Equals(
                property.Name,
                ObservedValueWebhookPropertyName,
                StringComparison.OrdinalIgnoreCase))
            {
                observedValueWebhook = property.Value;
                continue;
            }

            issues.Add(InvalidIntegrationOptions(
                $"{IntegrationsPropertyName}.{property.Name} is not supported."));
        }

        if (observedValueWebhook is null)
        {
            return Disabled;
        }

        var options = ParseObservedValueWebhook(observedValueWebhook.Value, issues);
        ValidateAgainstRuntime(options, configuration, issues);
        return issues.Any(IsIntegrationError) ? Disabled : options;
    }

    public bool ShouldDeliver(ModbusObservedValueUpdateGroup updateGroup)
    {
        ArgumentNullException.ThrowIfNull(updateGroup);

        return Enabled && FilterChangedCells(updateGroup).Count > 0;
    }

    public IReadOnlyList<ModbusObservedValueCellUpdate> FilterObservedCells(
        ModbusObservedValueUpdateGroup updateGroup)
    {
        ArgumentNullException.ThrowIfNull(updateGroup);

        return updateGroup.ObservedCells
            .Where(cell => MatchesAnyRange(updateGroup, cell))
            .ToArray();
    }

    public IReadOnlyList<ModbusObservedValueCellUpdate> FilterChangedCells(
        ModbusObservedValueUpdateGroup updateGroup)
    {
        ArgumentNullException.ThrowIfNull(updateGroup);

        return updateGroup.ChangedCells
            .Where(cell => MatchesAnyRange(updateGroup, cell))
            .ToArray();
    }

    private static ObservedValueWebhookOptions ParseObservedValueWebhook(
        JsonElement observedValueWebhook,
        ICollection<ConfigurationIssue> issues)
    {
        if (observedValueWebhook.ValueKind != JsonValueKind.Object)
        {
            issues.Add(InvalidIntegrationOptions(
                $"{IntegrationsPropertyName}.{ObservedValueWebhookPropertyName} must be an object."));
            return Disabled;
        }

        var enabled = false;
        Uri? url = null;
        var trigger = ObservedValueWebhookTriggerOptions.Default;
        var delivery = ObservedValueWebhookDeliveryOptions.Default;

        foreach (var property in observedValueWebhook.EnumerateObject())
        {
            if (string.Equals(property.Name, EnabledPropertyName, StringComparison.OrdinalIgnoreCase))
            {
                if (property.Value.ValueKind != JsonValueKind.True &&
                    property.Value.ValueKind != JsonValueKind.False)
                {
                    issues.Add(InvalidIntegrationOptions(
                        Path(EnabledPropertyName) + " must be a boolean."));
                    continue;
                }

                enabled = property.Value.GetBoolean();
                continue;
            }

            if (string.Equals(property.Name, UrlPropertyName, StringComparison.OrdinalIgnoreCase))
            {
                url = ParseUrl(property.Value, issues);
                continue;
            }

            if (string.Equals(property.Name, TriggerPropertyName, StringComparison.OrdinalIgnoreCase))
            {
                trigger = ParseTrigger(property.Value, issues);
                continue;
            }

            if (string.Equals(property.Name, DeliveryPropertyName, StringComparison.OrdinalIgnoreCase))
            {
                delivery = ParseDelivery(property.Value, issues);
                continue;
            }

            if (string.Equals(property.Name, AuthenticationPropertyName, StringComparison.OrdinalIgnoreCase))
            {
                ValidateAuthentication(property.Value, issues);
                continue;
            }

            issues.Add(InvalidIntegrationOptions(
                Path(property.Name) + " is not supported."));
        }

        if (enabled && url is null)
        {
            issues.Add(InvalidIntegrationOptions(Path(UrlPropertyName) + " is required when the webhook is enabled."));
        }

        if (enabled)
        {
            issues.Add(new ConfigurationIssue(
                ConfigurationIssueSeverity.Warning,
                ConfigurationIssueCodes.ObservedValueWebhookEnabled,
                "Observed-value webhook delivery is enabled. Payloads can expose process values, device addresses, and timing patterns."));
        }

        return new ObservedValueWebhookOptions(enabled, url, trigger, delivery);
    }

    private static Uri? ParseUrl(
        JsonElement value,
        ICollection<ConfigurationIssue> issues)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            issues.Add(InvalidIntegrationOptions(Path(UrlPropertyName) + " must be a string."));
            return null;
        }

        var urlText = value.GetString();
        if (string.IsNullOrWhiteSpace(urlText) ||
            !Uri.TryCreate(urlText, UriKind.Absolute, out var url) ||
            (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
        {
            issues.Add(InvalidIntegrationOptions(
                Path(UrlPropertyName) + " must be an absolute http or https URL."));
            return null;
        }

        return url;
    }

    private static ObservedValueWebhookTriggerOptions ParseTrigger(
        JsonElement trigger,
        ICollection<ConfigurationIssue> issues)
    {
        if (trigger.ValueKind != JsonValueKind.Object)
        {
            issues.Add(InvalidIntegrationOptions(Path(TriggerPropertyName) + " must be an object."));
            return ObservedValueWebhookTriggerOptions.Default;
        }

        var ranges = Array.Empty<ObservedValueWebhookRangeFilter>();

        foreach (var property in trigger.EnumerateObject())
        {
            if (string.Equals(property.Name, ModePropertyName, StringComparison.OrdinalIgnoreCase))
            {
                ValidateTriggerMode(property.Value, issues);
                continue;
            }

            if (string.Equals(property.Name, RangesPropertyName, StringComparison.OrdinalIgnoreCase))
            {
                ranges = ParseRanges(property.Value, issues);
                continue;
            }

            issues.Add(InvalidIntegrationOptions(
                Path(TriggerPropertyName, property.Name) + " is not supported."));
        }

        return new ObservedValueWebhookTriggerOptions(ranges);
    }

    private static void ValidateTriggerMode(
        JsonElement mode,
        ICollection<ConfigurationIssue> issues)
    {
        if (mode.ValueKind != JsonValueKind.String)
        {
            issues.Add(InvalidIntegrationOptions(Path(TriggerPropertyName, ModePropertyName) + " must be a string."));
            return;
        }

        var modeText = mode.GetString();
        if (!string.Equals(modeText, ChangedCellsOnlyModeName, StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(InvalidIntegrationOptions(
                Path(TriggerPropertyName, ModePropertyName) + $" only supports '{ChangedCellsOnlyModeName}' in this implementation slice."));
        }
    }

    private static ObservedValueWebhookRangeFilter[] ParseRanges(
        JsonElement ranges,
        ICollection<ConfigurationIssue> issues)
    {
        if (ranges.ValueKind != JsonValueKind.Array)
        {
            issues.Add(InvalidIntegrationOptions(Path(TriggerPropertyName, RangesPropertyName) + " must be an array."));
            return [];
        }

        var parsedRanges = new List<ObservedValueWebhookRangeFilter>();
        var index = 0;
        foreach (var range in ranges.EnumerateArray())
        {
            if (TryParseRange(range, index, issues, out var parsedRange))
            {
                parsedRanges.Add(parsedRange);
            }

            index++;
        }

        return parsedRanges.ToArray();
    }

    private static bool TryParseRange(
        JsonElement range,
        int index,
        ICollection<ConfigurationIssue> issues,
        out ObservedValueWebhookRangeFilter parsedRange)
    {
        parsedRange = default;
        if (range.ValueKind != JsonValueKind.Object)
        {
            issues.Add(InvalidIntegrationOptions(RangePath(index) + " must be an object."));
            return false;
        }

        int? unitId = null;
        ModbusObservedTable? table = null;
        int? startAddress = null;
        int? endAddress = null;

        foreach (var property in range.EnumerateObject())
        {
            if (string.Equals(property.Name, UnitIdPropertyName, StringComparison.OrdinalIgnoreCase))
            {
                unitId = ParseInt32(property.Value, RangePath(index, UnitIdPropertyName), 0, byte.MaxValue, issues);
                continue;
            }

            if (string.Equals(property.Name, TablePropertyName, StringComparison.OrdinalIgnoreCase))
            {
                table = ParseTable(property.Value, RangePath(index, TablePropertyName), issues);
                continue;
            }

            if (string.Equals(property.Name, StartAddressPropertyName, StringComparison.OrdinalIgnoreCase))
            {
                startAddress = ParseInt32(property.Value, RangePath(index, StartAddressPropertyName), 0, ushort.MaxValue, issues);
                continue;
            }

            if (string.Equals(property.Name, EndAddressPropertyName, StringComparison.OrdinalIgnoreCase))
            {
                endAddress = ParseInt32(property.Value, RangePath(index, EndAddressPropertyName), 0, ushort.MaxValue, issues);
                continue;
            }

            issues.Add(InvalidIntegrationOptions(RangePath(index, property.Name) + " is not supported."));
        }

        if (unitId is null)
        {
            issues.Add(InvalidIntegrationOptions(RangePath(index, UnitIdPropertyName) + " is required."));
        }

        if (table is null)
        {
            issues.Add(InvalidIntegrationOptions(RangePath(index, TablePropertyName) + " is required."));
        }

        if (startAddress is null)
        {
            issues.Add(InvalidIntegrationOptions(RangePath(index, StartAddressPropertyName) + " is required."));
        }

        if (endAddress is null)
        {
            issues.Add(InvalidIntegrationOptions(RangePath(index, EndAddressPropertyName) + " is required."));
        }

        if (startAddress is not null && endAddress is not null && endAddress < startAddress)
        {
            issues.Add(InvalidIntegrationOptions(RangePath(index, EndAddressPropertyName) + " must be greater than or equal to startAddress."));
        }

        if (unitId is null || table is null || startAddress is null || endAddress is null || endAddress < startAddress)
        {
            return false;
        }

        parsedRange = new ObservedValueWebhookRangeFilter(
            (byte)unitId.Value,
            table.Value,
            (ushort)startAddress.Value,
            (ushort)endAddress.Value);
        return true;
    }

    private static ObservedValueWebhookDeliveryOptions ParseDelivery(
        JsonElement delivery,
        ICollection<ConfigurationIssue> issues)
    {
        if (delivery.ValueKind != JsonValueKind.Object)
        {
            issues.Add(InvalidIntegrationOptions(Path(DeliveryPropertyName) + " must be an object."));
            return ObservedValueWebhookDeliveryOptions.Default;
        }

        var timeoutMilliseconds = DefaultTimeoutMilliseconds;
        var queueCapacity = DefaultQueueCapacity;

        foreach (var property in delivery.EnumerateObject())
        {
            if (string.Equals(property.Name, TimeoutMillisecondsPropertyName, StringComparison.OrdinalIgnoreCase))
            {
                timeoutMilliseconds = ParseInt32(
                    property.Value,
                    Path(DeliveryPropertyName, TimeoutMillisecondsPropertyName),
                    minimum: 1,
                    maximum: 60_000,
                    issues) ?? timeoutMilliseconds;
                continue;
            }

            if (string.Equals(property.Name, QueueCapacityPropertyName, StringComparison.OrdinalIgnoreCase))
            {
                queueCapacity = ParseInt32(
                    property.Value,
                    Path(DeliveryPropertyName, QueueCapacityPropertyName),
                    minimum: 1,
                    maximum: 65_536,
                    issues) ?? queueCapacity;
                continue;
            }

            issues.Add(InvalidIntegrationOptions(
                Path(DeliveryPropertyName, property.Name) + " is not supported."));
        }

        return new ObservedValueWebhookDeliveryOptions(
            TimeSpan.FromMilliseconds(timeoutMilliseconds),
            queueCapacity);
    }

    private static void ValidateAuthentication(
        JsonElement authentication,
        ICollection<ConfigurationIssue> issues)
    {
        if (authentication.ValueKind != JsonValueKind.Object)
        {
            issues.Add(InvalidIntegrationOptions(Path(AuthenticationPropertyName) + " must be an object."));
            return;
        }

        var sawMode = false;
        foreach (var property in authentication.EnumerateObject())
        {
            if (string.Equals(property.Name, ModePropertyName, StringComparison.OrdinalIgnoreCase))
            {
                sawMode = true;
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    issues.Add(InvalidIntegrationOptions(Path(AuthenticationPropertyName, ModePropertyName) + " must be a string."));
                    continue;
                }

                var mode = property.Value.GetString();
                if (!string.Equals(mode, NoneAuthenticationModeName, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(InvalidIntegrationOptions(
                        Path(AuthenticationPropertyName, ModePropertyName) + $" only supports '{NoneAuthenticationModeName}' in this implementation slice."));
                }

                continue;
            }

            issues.Add(InvalidIntegrationOptions(
                Path(AuthenticationPropertyName, property.Name) + " is not supported."));
        }

        if (!sawMode)
        {
            issues.Add(InvalidIntegrationOptions(Path(AuthenticationPropertyName, ModePropertyName) + " is required when authentication is configured."));
        }
    }

    private static int? ParseInt32(
        JsonElement value,
        string path,
        int minimum,
        int maximum,
        ICollection<ConfigurationIssue> issues)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var result))
        {
            issues.Add(InvalidIntegrationOptions(path + " must be an integer."));
            return null;
        }

        if (result < minimum || result > maximum)
        {
            issues.Add(InvalidIntegrationOptions(
                path + $" must be between {minimum.ToString(CultureInfo.InvariantCulture)} and {maximum.ToString(CultureInfo.InvariantCulture)}."));
            return null;
        }

        return result;
    }

    private static ModbusObservedTable? ParseTable(
        JsonElement value,
        string path,
        ICollection<ConfigurationIssue> issues)
    {
        if (value.ValueKind != JsonValueKind.String)
        {
            issues.Add(InvalidIntegrationOptions(path + " must be a string."));
            return null;
        }

        return value.GetString() switch
        {
            var table when string.Equals(table, "coils", StringComparison.OrdinalIgnoreCase) => ModbusObservedTable.Coils,
            var table when string.Equals(table, "discreteInputs", StringComparison.OrdinalIgnoreCase) => ModbusObservedTable.DiscreteInputs,
            var table when string.Equals(table, "holdingRegisters", StringComparison.OrdinalIgnoreCase) => ModbusObservedTable.HoldingRegisters,
            var table when string.Equals(table, "inputRegisters", StringComparison.OrdinalIgnoreCase) => ModbusObservedTable.InputRegisters,
            var unsupported => AddUnsupportedTable(path, unsupported, issues)
        };
    }

    private static ModbusObservedTable? AddUnsupportedTable(
        string path,
        string? value,
        ICollection<ConfigurationIssue> issues)
    {
        issues.Add(InvalidIntegrationOptions(
            path + $" has unsupported table '{value}'. Supported tables are 'coils', 'discreteInputs', 'holdingRegisters', and 'inputRegisters'."));
        return null;
    }

    private static void ValidateAgainstRuntime(
        ObservedValueWebhookOptions options,
        RuntimeConfiguration configuration,
        ICollection<ConfigurationIssue> issues)
    {
        if (!options.Enabled)
        {
            return;
        }

        if (!string.Equals(
            configuration.Session.Protocol.Value,
            ModbusTcpProtocolPlugin.ProtocolId,
            StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(InvalidIntegrationOptions(
                $"{IntegrationsPropertyName}.{ObservedValueWebhookPropertyName} only supports protocol '{ModbusTcpProtocolPlugin.ProtocolId}' in this implementation slice."));
        }

        if (!configuration.Session.Diagnostics.DecodeProtocol)
        {
            issues.Add(InvalidIntegrationOptions(
                $"{IntegrationsPropertyName}.{ObservedValueWebhookPropertyName} requires session.diagnostics.decodeProtocol to be true."));
        }
    }

    private bool MatchesAnyRange(
        ModbusObservedValueUpdateGroup updateGroup,
        ModbusObservedValueCellUpdate cell)
    {
        if (Trigger.Ranges.Count == 0)
        {
            return true;
        }

        return Trigger.Ranges.Any(range => range.Matches(updateGroup, cell));
    }

    private static bool IsIntegrationError(ConfigurationIssue issue)
    {
        return issue.Severity == ConfigurationIssueSeverity.Error &&
            issue.Code == ConfigurationIssueCodes.InvalidIntegrationOptions;
    }

    private static ConfigurationIssue InvalidIntegrationOptions(string message) =>
        new(
            ConfigurationIssueSeverity.Error,
            ConfigurationIssueCodes.InvalidIntegrationOptions,
            message);

    private static string Path(params string[] properties)
    {
        return string.Join(
            ".",
            new[] { IntegrationsPropertyName, ObservedValueWebhookPropertyName }.Concat(properties));
    }

    private static string RangePath(int index, string? propertyName = null)
    {
        var path = Path(TriggerPropertyName, $"{RangesPropertyName}[{index.ToString(CultureInfo.InvariantCulture)}]");
        return propertyName is null ? path : path + "." + propertyName;
    }
}

internal sealed record ObservedValueWebhookTriggerOptions(
    IReadOnlyList<ObservedValueWebhookRangeFilter> Ranges)
{
    public static ObservedValueWebhookTriggerOptions Default { get; } = new(
        Array.Empty<ObservedValueWebhookRangeFilter>());
}

internal sealed record ObservedValueWebhookDeliveryOptions(
    TimeSpan Timeout,
    int QueueCapacity)
{
    public static ObservedValueWebhookDeliveryOptions Default { get; } = new(
        TimeSpan.FromMilliseconds(2000),
        256);
}

internal readonly record struct ObservedValueWebhookRangeFilter(
    byte UnitId,
    ModbusObservedTable Table,
    ushort StartAddress,
    ushort EndAddress)
{
    public bool Matches(
        ModbusObservedValueUpdateGroup updateGroup,
        ModbusObservedValueCellUpdate cell)
    {
        ArgumentNullException.ThrowIfNull(updateGroup);

        return updateGroup.UnitId == UnitId &&
            updateGroup.Table == Table &&
            cell.Address >= StartAddress &&
            cell.Address <= EndAddress;
    }
}
