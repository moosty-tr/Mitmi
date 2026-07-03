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

    public IReadOnlyList<SessionEvent> CreateEvents(SessionId sessionId)
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
            .Select(entry => new SessionEvent(
                DateTimeOffset.UtcNow,
                SessionEventLevel.Info,
                SessionEventNames.ProtocolAnalyzerSummary,
                sessionId,
                ConnectionId: null,
                entry.Render()))
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

        public string Render()
        {
            var address = Key.Address is null
                ? "unknown"
                : Key.Address.Value.ToString(CultureInfo.InvariantCulture);
            var quantity = Key.Quantity is null
                ? "unknown"
                : Key.Quantity.Value.ToString(CultureInfo.InvariantCulture);
            var range = Key.AddressRange ?? "unknown";

            return string.Join(
                " ",
                "Modbus analyzer summary",
                $"unit={Key.UnitId.ToString(CultureInfo.InvariantCulture)}",
                $"function={Key.FunctionCode.ToString(CultureInfo.InvariantCulture)}",
                $"operation={Key.Operation}",
                $"address={address}",
                $"quantity={quantity}",
                $"address_range={range}",
                "address_base=zeroBasedPdu",
                $"reads={Reads.ToString(CultureInfo.InvariantCulture)}",
                $"writes={Writes.ToString(CultureInfo.InvariantCulture)}",
                $"requests={Requests.ToString(CultureInfo.InvariantCulture)}",
                $"responses={Responses.ToString(CultureInfo.InvariantCulture)}",
                $"exceptions={Exceptions.ToString(CultureInfo.InvariantCulture)}.");
        }
    }
}
