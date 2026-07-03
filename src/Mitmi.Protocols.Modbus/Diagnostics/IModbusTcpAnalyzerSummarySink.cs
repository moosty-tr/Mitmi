namespace Mitmi.Protocols.Modbus.Diagnostics;

public interface IModbusTcpAnalyzerSummarySink
{
    ValueTask EmitAsync(
        IReadOnlyList<ModbusTcpAnalyzerSummaryRecord> records,
        CancellationToken cancellationToken);
}
