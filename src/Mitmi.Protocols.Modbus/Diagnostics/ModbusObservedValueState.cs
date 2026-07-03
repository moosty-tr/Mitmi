using System.Globalization;
using Mitmi.Domain;
using Mitmi.Protocols.Modbus.Framing;

namespace Mitmi.Protocols.Modbus.Diagnostics;

public sealed class ModbusObservedValueState
{
    private const int DataOffset = 8;
    private readonly Dictionary<ObservedCellKey, ObservedCellState> cells = [];
    private readonly ModbusObservedValueStateOptions options;

    public ModbusObservedValueState(ModbusObservedValueStateOptions? options = null)
    {
        this.options = options ?? new ModbusObservedValueStateOptions();
        if (this.options.MaxObservedCells <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Max observed cells must be greater than zero.");
        }

        if (this.options.MaxCellsPerUpdateGroup <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Max cells per update group must be greater than zero.");
        }
    }

    public int ObservedCellCount => cells.Count;

    public int SkippedNewCellCount { get; private set; }

    public int SkippedUpdateCellCount { get; private set; }

    public ModbusObservedValueUpdateGroup? ObserveMatchedTransaction(
        SessionId sessionId,
        NetworkEndpoint upstreamEndpoint,
        ModbusTcpTransactionEvent transactionEvent,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(upstreamEndpoint);
        ArgumentNullException.ThrowIfNull(transactionEvent);

        if (transactionEvent.Kind != ModbusTcpTransactionEventKind.ResponseMatched ||
            transactionEvent.RequestFrame is null ||
            transactionEvent.Frame.IsExceptionResponse ||
            transactionEvent.Frame.OperationFunctionCode != transactionEvent.RequestFrame.OperationFunctionCode)
        {
            return null;
        }

        if (!TryExtractObservedValues(
            transactionEvent,
            out var table,
            out var address,
            out var quantity,
            out var operation,
            out var values))
        {
            return null;
        }

        var observedUpdates = new List<ModbusObservedValueCellUpdate>();
        foreach (var value in values)
        {
            if (observedUpdates.Count >= options.MaxCellsPerUpdateGroup)
            {
                SkippedUpdateCellCount++;
                continue;
            }

            var key = new ObservedCellKey(
                sessionId,
                upstreamEndpoint,
                transactionEvent.Frame.UnitId,
                table,
                value.Address);

            var isExistingCell = cells.TryGetValue(key, out var previousState);
            if (!isExistingCell && cells.Count >= options.MaxObservedCells)
            {
                SkippedNewCellCount++;
                continue;
            }

            var previousValue = isExistingCell ? previousState!.Value : (ModbusObservedValue?)null;
            var changed = previousValue is null || previousValue.Value != value.Value;
            var firstObservedAt = isExistingCell ? previousState!.FirstObservedAt : observedAt;
            var lastChangedAt = changed
                ? observedAt
                : previousState!.LastChangedAt;

            cells[key] = new ObservedCellState(
                value.Value,
                firstObservedAt,
                observedAt,
                lastChangedAt,
                transactionEvent.CorrelationId,
                operation,
                transactionEvent.Frame.OperationFunctionCode);

            observedUpdates.Add(new ModbusObservedValueCellUpdate(
                value.Address,
                previousValue,
                value.Value,
                changed,
                firstObservedAt,
                observedAt,
                lastChangedAt));
        }

        if (observedUpdates.Count == 0)
        {
            return null;
        }

        return new ModbusObservedValueUpdateGroup(
            sessionId,
            upstreamEndpoint,
            transactionEvent.Frame.UnitId,
            transactionEvent.Frame.OperationFunctionCode,
            operation,
            table,
            address,
            quantity,
            "zeroBasedPdu",
            FormatAddressRange(address, quantity),
            transactionEvent.CorrelationId,
            observedAt,
            observedUpdates.ToArray(),
            observedUpdates.Where(update => update.Changed).ToArray());
    }

    private static bool TryExtractObservedValues(
        ModbusTcpTransactionEvent transactionEvent,
        out ModbusObservedTable table,
        out ushort address,
        out ushort quantity,
        out string operation,
        out IReadOnlyList<ObservedCellValue> values)
    {
        table = default;
        address = 0;
        quantity = 0;
        operation = string.Empty;
        values = [];

        var functionCode = transactionEvent.Frame.OperationFunctionCode;
        if (!TryGetTable(functionCode, out table) ||
            !TryGetRequestAddressAndQuantity(transactionEvent.RequestFrame!, functionCode, out address, out quantity))
        {
            return false;
        }

        operation = OperationName(functionCode);
        values = functionCode switch
        {
            1 or 2 => TryReadBitResponseValues(transactionEvent.Frame, address, quantity, out var bitReadValues)
                ? bitReadValues
                : [],
            3 or 4 => TryReadRegisterResponseValues(transactionEvent.Frame, address, quantity, out var registerReadValues)
                ? registerReadValues
                : [],
            5 => TryReadSingleCoilRequestValue(transactionEvent.RequestFrame!, address, out var singleCoilValue)
                ? [singleCoilValue]
                : [],
            6 => TryReadSingleRegisterRequestValue(transactionEvent.RequestFrame!, address, out var singleRegisterValue)
                ? [singleRegisterValue]
                : [],
            15 => TryReadMultipleCoilRequestValues(transactionEvent.RequestFrame!, address, quantity, out var coilWriteValues)
                ? coilWriteValues
                : [],
            16 => TryReadMultipleRegisterRequestValues(transactionEvent.RequestFrame!, address, quantity, out var registerWriteValues)
                ? registerWriteValues
                : [],
            _ => []
        };

        return values.Count > 0;
    }

    private static bool TryGetTable(
        byte functionCode,
        out ModbusObservedTable table)
    {
        table = functionCode switch
        {
            1 or 5 or 15 => ModbusObservedTable.Coils,
            2 => ModbusObservedTable.DiscreteInputs,
            3 or 6 or 16 => ModbusObservedTable.HoldingRegisters,
            4 => ModbusObservedTable.InputRegisters,
            _ => default
        };

        return functionCode is 1 or 2 or 3 or 4 or 5 or 6 or 15 or 16;
    }

    private static bool TryGetRequestAddressAndQuantity(
        ModbusTcpFrame requestFrame,
        byte functionCode,
        out ushort address,
        out ushort quantity)
    {
        address = 0;
        quantity = 0;

        if (!TryReadUInt16(requestFrame.RawFrame, DataOffset, out address))
        {
            return false;
        }

        if (functionCode is 5 or 6)
        {
            quantity = 1;
            return true;
        }

        return TryReadUInt16(requestFrame.RawFrame, DataOffset + 2, out quantity) &&
            quantity > 0;
    }

    private static bool TryReadBitResponseValues(
        ModbusTcpFrame responseFrame,
        ushort address,
        ushort quantity,
        out IReadOnlyList<ObservedCellValue> values)
    {
        values = [];
        if (!TryReadByteCount(responseFrame.RawFrame, out var byteCount) ||
            byteCount < RequiredBitByteCount(quantity) ||
            !HasOffset(responseFrame.RawFrame, DataOffset + byteCount))
        {
            return false;
        }

        values = ReadBitValues(responseFrame.RawFrame, DataOffset + 1, address, quantity);
        return true;
    }

    private static bool TryReadRegisterResponseValues(
        ModbusTcpFrame responseFrame,
        ushort address,
        ushort quantity,
        out IReadOnlyList<ObservedCellValue> values)
    {
        values = [];
        var expectedByteCount = checked(quantity * 2);
        if (!TryReadByteCount(responseFrame.RawFrame, out var byteCount) ||
            byteCount != expectedByteCount ||
            !HasOffset(responseFrame.RawFrame, DataOffset + byteCount))
        {
            return false;
        }

        values = ReadRegisterValues(responseFrame.RawFrame, DataOffset + 1, address, quantity);
        return true;
    }

    private static bool TryReadSingleCoilRequestValue(
        ModbusTcpFrame requestFrame,
        ushort address,
        out ObservedCellValue value)
    {
        value = default;
        if (!TryReadUInt16(requestFrame.RawFrame, DataOffset + 2, out var rawValue))
        {
            return false;
        }

        if (rawValue == 0xFF00)
        {
            value = new ObservedCellValue(address, ModbusObservedValue.Boolean(true));
            return true;
        }

        if (rawValue == 0x0000)
        {
            value = new ObservedCellValue(address, ModbusObservedValue.Boolean(false));
            return true;
        }

        return false;
    }

    private static bool TryReadSingleRegisterRequestValue(
        ModbusTcpFrame requestFrame,
        ushort address,
        out ObservedCellValue value)
    {
        value = default;
        if (!TryReadUInt16(requestFrame.RawFrame, DataOffset + 2, out var registerValue))
        {
            return false;
        }

        value = new ObservedCellValue(address, ModbusObservedValue.Register(registerValue));
        return true;
    }

    private static bool TryReadMultipleCoilRequestValues(
        ModbusTcpFrame requestFrame,
        ushort address,
        ushort quantity,
        out IReadOnlyList<ObservedCellValue> values)
    {
        values = [];
        if (!TryReadByteCount(requestFrame.RawFrame, out var byteCount, byteCountOffset: DataOffset + 4) ||
            byteCount < RequiredBitByteCount(quantity) ||
            !HasOffset(requestFrame.RawFrame, DataOffset + 4 + byteCount))
        {
            return false;
        }

        values = ReadBitValues(requestFrame.RawFrame, DataOffset + 5, address, quantity);
        return true;
    }

    private static bool TryReadMultipleRegisterRequestValues(
        ModbusTcpFrame requestFrame,
        ushort address,
        ushort quantity,
        out IReadOnlyList<ObservedCellValue> values)
    {
        values = [];
        var expectedByteCount = checked(quantity * 2);
        if (!TryReadByteCount(requestFrame.RawFrame, out var byteCount, byteCountOffset: DataOffset + 4) ||
            byteCount != expectedByteCount ||
            !HasOffset(requestFrame.RawFrame, DataOffset + 4 + byteCount))
        {
            return false;
        }

        values = ReadRegisterValues(requestFrame.RawFrame, DataOffset + 5, address, quantity);
        return true;
    }

    private static IReadOnlyList<ObservedCellValue> ReadBitValues(
        byte[] bytes,
        int valueOffset,
        ushort address,
        ushort quantity)
    {
        var values = new List<ObservedCellValue>(quantity);
        for (var index = 0; index < quantity; index++)
        {
            var currentByte = bytes[valueOffset + index / 8];
            var bitValue = ((currentByte >> (index % 8)) & 0x01) == 0x01;
            values.Add(new ObservedCellValue(
                checked((ushort)(address + index)),
                ModbusObservedValue.Boolean(bitValue)));
        }

        return values;
    }

    private static IReadOnlyList<ObservedCellValue> ReadRegisterValues(
        byte[] bytes,
        int valueOffset,
        ushort address,
        ushort quantity)
    {
        var values = new List<ObservedCellValue>(quantity);
        for (var index = 0; index < quantity; index++)
        {
            var byteOffset = valueOffset + index * 2;
            values.Add(new ObservedCellValue(
                checked((ushort)(address + index)),
                ModbusObservedValue.Register((ushort)((bytes[byteOffset] << 8) | bytes[byteOffset + 1]))));
        }

        return values;
    }

    private static bool TryReadByteCount(
        byte[] bytes,
        out int byteCount,
        int byteCountOffset = DataOffset)
    {
        byteCount = 0;
        if (!HasOffset(bytes, byteCountOffset))
        {
            return false;
        }

        byteCount = bytes[byteCountOffset];
        return true;
    }

    private static int RequiredBitByteCount(ushort quantity)
    {
        return (quantity + 7) / 8;
    }

    private static bool TryReadUInt16(
        byte[] bytes,
        int offset,
        out ushort value)
    {
        if (!HasOffset(bytes, offset + 1))
        {
            value = 0;
            return false;
        }

        value = (ushort)((bytes[offset] << 8) | bytes[offset + 1]);
        return true;
    }

    private static bool HasOffset(byte[] bytes, int offset)
    {
        return offset >= 0 && offset < bytes.Length;
    }

    private static string FormatAddressRange(
        ushort address,
        ushort quantity)
    {
        var end = address + quantity - 1;
        return end == address
            ? address.ToString(CultureInfo.InvariantCulture)
            : $"{address.ToString(CultureInfo.InvariantCulture)}-{end.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string OperationName(byte functionCode)
    {
        return functionCode switch
        {
            1 => "readCoils",
            2 => "readDiscreteInputs",
            3 => "readHoldingRegisters",
            4 => "readInputRegisters",
            5 => "writeSingleCoil",
            6 => "writeSingleRegister",
            15 => "writeMultipleCoils",
            16 => "writeMultipleRegisters",
            _ => $"function{functionCode.ToString(CultureInfo.InvariantCulture)}"
        };
    }

    private readonly record struct ObservedCellKey(
        SessionId SessionId,
        NetworkEndpoint UpstreamEndpoint,
        byte UnitId,
        ModbusObservedTable Table,
        ushort Address);

    private sealed record ObservedCellState(
        ModbusObservedValue Value,
        DateTimeOffset FirstObservedAt,
        DateTimeOffset LastObservedAt,
        DateTimeOffset? LastChangedAt,
        string LastCorrelationId,
        string LastOperation,
        byte LastFunctionCode);

    private readonly record struct ObservedCellValue(
        ushort Address,
        ModbusObservedValue Value);
}
