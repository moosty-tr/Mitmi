using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mitmi.Application.Configuration;
using Mitmi.Protocols.Modbus.Diagnostics;

namespace Mitmi.Host.Console;

internal sealed class NdjsonModbusAnalyzerSummarySink : IModbusTcpAnalyzerSummarySink
{
    private const int SummaryFormatVersionValue = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public NdjsonModbusAnalyzerSummarySink(
        CaptureRuntimeOptions capture,
        DateTimeOffset startedAt)
    {
        ArgumentNullException.ThrowIfNull(capture);

        var summaryDirectoryPath = Path.Combine(capture.OutputPath, "summaries");
        SummaryFilePath = Path.Combine(summaryDirectoryPath, BuildSummaryFileName(startedAt));
    }

    public string SummaryFilePath { get; }

    public async ValueTask EmitAsync(
        IReadOnlyList<ModbusTcpAnalyzerSummaryRecord> records,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(records);

        var directory = Path.GetDirectoryName(SummaryFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = new FileStream(
            SummaryFilePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.ReadWrite,
            bufferSize: 4096,
            useAsync: true);
        await using var writer = new StreamWriter(stream);

        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = SummaryRecordDocument.From(record);
            await writer.WriteLineAsync(
                JsonSerializer.Serialize(document, JsonOptions).AsMemory(),
                cancellationToken);
        }
    }

    private static string BuildSummaryFileName(DateTimeOffset startedAt)
    {
        return $"mitmi-modbus-analyzer-summary-{startedAt.UtcDateTime.ToString("yyyyMMddTHHmmssfffffff'Z'", CultureInfo.InvariantCulture)}.ndjson";
    }

    private sealed record SummaryRecordDocument(
        int SummaryFormatVersion,
        string TimestampUtc,
        string SessionId,
        int UnitId,
        int FunctionCode,
        string Operation,
        ushort? Address,
        ushort? Quantity,
        string? AddressRange,
        string AddressBase,
        int Reads,
        int Writes,
        int Requests,
        int Responses,
        int Exceptions)
    {
        public static SummaryRecordDocument From(ModbusTcpAnalyzerSummaryRecord record)
        {
            return new SummaryRecordDocument(
                SummaryFormatVersionValue,
                record.Timestamp.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
                record.SessionId.Value,
                record.UnitId,
                record.FunctionCode,
                record.Operation,
                record.Address,
                record.Quantity,
                record.AddressRange,
                record.AddressBase,
                record.Reads,
                record.Writes,
                record.Requests,
                record.Responses,
                record.Exceptions);
        }
    }
}
