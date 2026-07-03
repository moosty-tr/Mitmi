using System.Globalization;
using Mitmi.Application.Sessions;
using Mitmi.Domain;
using Mitmi.Protocols.Modbus.Framing;

namespace Mitmi.Protocols.Modbus.Diagnostics;

public sealed class ModbusTcpAnalyzerSessionSummary
{
    private readonly object gate = new();
    private readonly Dictionary<SummaryKey, SummaryEntry> entries = [];

    public void Observe(
        ModbusTcpFrame frame,
        ModbusTcpPduAnalysis analysis)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(analysis);

        var key = new SummaryKey(
            analysis.UnitId,
            analysis.OperationFunctionCode,
            analysis.Operation,
            analysis.Address,
            analysis.Quantity,
            analysis.AddressRange);

        lock (gate)
        {
            if (!entries.TryGetValue(key, out var entry))
            {
                entry = new SummaryEntry(key);
                entries.Add(key, entry);
            }

            entry.Record(frame.Direction, IsReadOperation(analysis.Operation), analysis.ExceptionCode.HasValue);
        }
    }

    public IReadOnlyList<ModbusTcpAnalyzerSummaryRecord> CreateRecords(
        SessionId sessionId,
        DateTimeOffset timestamp)
    {
        SummaryEntry[] snapshot;
        lock (gate)
        {
            snapshot = entries.Values
                .OrderBy(entry => entry.Key.UnitId)
                .ThenBy(entry => entry.Key.FunctionCode)
                .ThenBy(entry => entry.Key.Address ?? ushort.MaxValue)
                .ThenBy(entry => entry.Key.Quantity ?? ushort.MaxValue)
                .ToArray();
        }

        return snapshot
            .Select(entry => entry.CreateRecord(sessionId, timestamp))
            .ToArray();
    }

    public IReadOnlyList<SessionEvent> CreateEvents(
        IReadOnlyList<ModbusTcpAnalyzerSummaryRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        return records
            .Select(record => new SessionEvent(
                record.Timestamp,
                SessionEventLevel.Info,
                SessionEventNames.ProtocolAnalyzerSummary,
                record.SessionId,
                ConnectionId: null,
                Render(record)))
            .ToArray();
    }

    private static bool IsReadOperation(string operation)
    {
        return operation.StartsWith("read", StringComparison.Ordinal);
    }

    private sealed record SummaryKey(
        byte UnitId,
        byte FunctionCode,
        string Operation,
        ushort? Address,
        ushort? Quantity,
        string? AddressRange);

    private sealed class SummaryEntry
    {
        public SummaryEntry(SummaryKey key)
        {
            Key = key;
        }

        public SummaryKey Key { get; }

        public int Requests { get; private set; }

        public int Responses { get; private set; }

        public int Reads { get; private set; }

        public int Writes { get; private set; }

        public int Exceptions { get; private set; }

        public void Record(
            ModbusTcpFrameDirection direction,
            bool isReadOperation,
            bool isException)
        {
            if (direction == ModbusTcpFrameDirection.ClientToServer)
            {
                Requests++;
                if (isReadOperation)
                {
                    Reads++;
                }
                else
                {
                    Writes++;
                }
            }
            else
            {
                Responses++;
            }

            if (isException)
            {
                Exceptions++;
            }
        }

        public ModbusTcpAnalyzerSummaryRecord CreateRecord(
            SessionId sessionId,
            DateTimeOffset timestamp)
        {
            return new ModbusTcpAnalyzerSummaryRecord(
                timestamp,
                sessionId,
                Key.UnitId,
                Key.FunctionCode,
                Key.Operation,
                Key.Address,
                Key.Quantity,
                Key.AddressRange,
                "zeroBasedPdu",
                Reads,
                Writes,
                Requests,
                Responses,
                Exceptions);
        }
    }

    private static string Render(ModbusTcpAnalyzerSummaryRecord record)
    {
        var address = record.Address is null
            ? "unknown"
            : record.Address.Value.ToString(CultureInfo.InvariantCulture);
        var quantity = record.Quantity is null
            ? "unknown"
            : record.Quantity.Value.ToString(CultureInfo.InvariantCulture);
        var range = record.AddressRange ?? "unknown";

        return string.Join(
            " ",
            "Modbus analyzer summary",
            $"unit={record.UnitId.ToString(CultureInfo.InvariantCulture)}",
            $"function={record.FunctionCode.ToString(CultureInfo.InvariantCulture)}",
            $"operation={record.Operation}",
            $"address={address}",
            $"quantity={quantity}",
            $"address_range={range}",
            $"address_base={record.AddressBase}",
            $"reads={record.Reads.ToString(CultureInfo.InvariantCulture)}",
            $"writes={record.Writes.ToString(CultureInfo.InvariantCulture)}",
            $"requests={record.Requests.ToString(CultureInfo.InvariantCulture)}",
            $"responses={record.Responses.ToString(CultureInfo.InvariantCulture)}",
            $"exceptions={record.Exceptions.ToString(CultureInfo.InvariantCulture)}.");
    }
}
