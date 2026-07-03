using System.Globalization;
using Mitmi.Protocols.Modbus.Framing;

namespace Mitmi.Protocols.Modbus.Diagnostics;

public static class ModbusTcpPduAnalyzer
{
    private const int DataOffset = 8;

    public static ModbusTcpPduAnalysis Analyze(
        ModbusTcpFrame frame,
        ModbusTcpFrame? requestFrame = null)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (frame.IsExceptionResponse)
        {
            var requestAnalysis = requestFrame is null ? null : AnalyzeRequest(requestFrame);
            return new ModbusTcpPduAnalysis(
                frame.UnitId,
                frame.FunctionCode,
                frame.OperationFunctionCode,
                requestAnalysis?.Operation ?? FormatUnknownOperation(frame.OperationFunctionCode),
                requestAnalysis?.Address,
                requestAnalysis?.Quantity,
                requestAnalysis?.AddressRange,
                ValuesHex: null,
                ByteCount: null,
                frame.ExceptionCode);
        }

        return frame.Direction == ModbusTcpFrameDirection.ClientToServer
            ? AnalyzeRequest(frame)
            : AnalyzeResponse(frame, requestFrame);
    }

    private static ModbusTcpPduAnalysis AnalyzeRequest(ModbusTcpFrame frame)
    {
        return frame.OperationFunctionCode switch
        {
            1 => AnalyzeAddressQuantityRequest(frame, "readCoils"),
            2 => AnalyzeAddressQuantityRequest(frame, "readDiscreteInputs"),
            3 => AnalyzeAddressQuantityRequest(frame, "readHoldingRegisters"),
            4 => AnalyzeAddressQuantityRequest(frame, "readInputRegisters"),
            5 => AnalyzeSingleValueRequest(frame, "writeSingleCoil"),
            6 => AnalyzeSingleValueRequest(frame, "writeSingleRegister"),
            15 => AnalyzeMultipleValueRequest(frame, "writeMultipleCoils", valuesAreRegisters: false),
            16 => AnalyzeMultipleValueRequest(frame, "writeMultipleRegisters", valuesAreRegisters: true),
            _ => Unknown(frame)
        };
    }

    private static ModbusTcpPduAnalysis AnalyzeResponse(
        ModbusTcpFrame frame,
        ModbusTcpFrame? requestFrame)
    {
        var requestAnalysis = requestFrame is null ? null : AnalyzeRequest(requestFrame);
        return frame.OperationFunctionCode switch
        {
            1 or 2 => AnalyzeReadBitsResponse(frame, requestAnalysis),
            3 or 4 => AnalyzeReadRegistersResponse(frame, requestAnalysis),
            5 or 6 => AnalyzeSingleValueRequest(frame, requestAnalysis?.Operation ?? OperationName(frame.OperationFunctionCode)),
            15 or 16 => AnalyzeAddressQuantityRequest(frame, requestAnalysis?.Operation ?? OperationName(frame.OperationFunctionCode)),
            _ => Unknown(frame)
        };
    }

    private static ModbusTcpPduAnalysis AnalyzeAddressQuantityRequest(
        ModbusTcpFrame frame,
        string operation)
    {
        var address = TryReadUInt16(frame.RawFrame, DataOffset, out var parsedAddress)
            ? parsedAddress
            : (ushort?)null;
        var quantity = TryReadUInt16(frame.RawFrame, DataOffset + 2, out var parsedQuantity)
            ? parsedQuantity
            : (ushort?)null;

        return new ModbusTcpPduAnalysis(
            frame.UnitId,
            frame.FunctionCode,
            frame.OperationFunctionCode,
            operation,
            address,
            quantity,
            FormatAddressRange(address, quantity),
            ValuesHex: null,
            ByteCount: null,
            frame.ExceptionCode);
    }

    private static ModbusTcpPduAnalysis AnalyzeSingleValueRequest(
        ModbusTcpFrame frame,
        string operation)
    {
        var address = TryReadUInt16(frame.RawFrame, DataOffset, out var parsedAddress)
            ? parsedAddress
            : (ushort?)null;
        var value = TryReadUInt16(frame.RawFrame, DataOffset + 2, out var parsedValue)
            ? FormatWord(parsedValue)
            : null;

        return new ModbusTcpPduAnalysis(
            frame.UnitId,
            frame.FunctionCode,
            frame.OperationFunctionCode,
            operation,
            address,
            Quantity: address.HasValue ? (ushort)1 : null,
            FormatAddressRange(address, address.HasValue ? (ushort)1 : null),
            value,
            ByteCount: null,
            frame.ExceptionCode);
    }

    private static ModbusTcpPduAnalysis AnalyzeMultipleValueRequest(
        ModbusTcpFrame frame,
        string operation,
        bool valuesAreRegisters)
    {
        var address = TryReadUInt16(frame.RawFrame, DataOffset, out var parsedAddress)
            ? parsedAddress
            : (ushort?)null;
        var quantity = TryReadUInt16(frame.RawFrame, DataOffset + 2, out var parsedQuantity)
            ? parsedQuantity
            : (ushort?)null;
        var byteCount = HasOffset(frame.RawFrame, DataOffset + 4)
            ? frame.RawFrame[DataOffset + 4]
            : (int?)null;
        var valuesOffset = DataOffset + 5;
        var valuesHex = byteCount is null
            ? null
            : valuesAreRegisters
                ? FormatWords(frame.RawFrame, valuesOffset, byteCount.Value)
                : FormatBytes(frame.RawFrame, valuesOffset, byteCount.Value);

        return new ModbusTcpPduAnalysis(
            frame.UnitId,
            frame.FunctionCode,
            frame.OperationFunctionCode,
            operation,
            address,
            quantity,
            FormatAddressRange(address, quantity),
            valuesHex,
            byteCount,
            frame.ExceptionCode);
    }

    private static ModbusTcpPduAnalysis AnalyzeReadBitsResponse(
        ModbusTcpFrame frame,
        ModbusTcpPduAnalysis? requestAnalysis)
    {
        var byteCount = HasOffset(frame.RawFrame, DataOffset)
            ? frame.RawFrame[DataOffset]
            : (int?)null;

        return new ModbusTcpPduAnalysis(
            frame.UnitId,
            frame.FunctionCode,
            frame.OperationFunctionCode,
            requestAnalysis?.Operation ?? OperationName(frame.OperationFunctionCode),
            requestAnalysis?.Address,
            requestAnalysis?.Quantity,
            requestAnalysis?.AddressRange,
            byteCount is null ? null : FormatBytes(frame.RawFrame, DataOffset + 1, byteCount.Value),
            byteCount,
            frame.ExceptionCode);
    }

    private static ModbusTcpPduAnalysis AnalyzeReadRegistersResponse(
        ModbusTcpFrame frame,
        ModbusTcpPduAnalysis? requestAnalysis)
    {
        var byteCount = HasOffset(frame.RawFrame, DataOffset)
            ? frame.RawFrame[DataOffset]
            : (int?)null;

        return new ModbusTcpPduAnalysis(
            frame.UnitId,
            frame.FunctionCode,
            frame.OperationFunctionCode,
            requestAnalysis?.Operation ?? OperationName(frame.OperationFunctionCode),
            requestAnalysis?.Address,
            requestAnalysis?.Quantity ?? (byteCount.HasValue ? (ushort)(byteCount.Value / 2) : null),
            requestAnalysis?.AddressRange,
            byteCount is null ? null : FormatWords(frame.RawFrame, DataOffset + 1, byteCount.Value),
            byteCount,
            frame.ExceptionCode);
    }

    private static ModbusTcpPduAnalysis Unknown(ModbusTcpFrame frame)
    {
        return new ModbusTcpPduAnalysis(
            frame.UnitId,
            frame.FunctionCode,
            frame.OperationFunctionCode,
            FormatUnknownOperation(frame.OperationFunctionCode),
            Address: null,
            Quantity: null,
            AddressRange: null,
            ValuesHex: null,
            ByteCount: null,
            frame.ExceptionCode);
    }

    private static bool TryReadUInt16(byte[] bytes, int offset, out ushort value)
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

    private static string? FormatAddressRange(ushort? address, ushort? quantity)
    {
        if (address is null || quantity is null || quantity.Value == 0)
        {
            return null;
        }

        var end = address.Value + quantity.Value - 1;
        return end == address.Value
            ? address.Value.ToString(CultureInfo.InvariantCulture)
            : $"{address.Value.ToString(CultureInfo.InvariantCulture)}-{end.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string FormatWords(byte[] bytes, int offset, int byteCount)
    {
        var words = new List<string>();
        var end = Math.Min(bytes.Length, offset + byteCount);
        for (var index = offset; index + 1 < end; index += 2)
        {
            words.Add(FormatWord((ushort)((bytes[index] << 8) | bytes[index + 1])));
        }

        return string.Join(",", words);
    }

    private static string FormatBytes(byte[] bytes, int offset, int byteCount)
    {
        var values = new List<string>();
        var end = Math.Min(bytes.Length, offset + byteCount);
        for (var index = offset; index < end; index++)
        {
            values.Add(bytes[index].ToString("x2", CultureInfo.InvariantCulture));
        }

        return string.Join(",", values);
    }

    private static string FormatWord(ushort value)
    {
        return value.ToString("x4", CultureInfo.InvariantCulture);
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
            _ => FormatUnknownOperation(functionCode)
        };
    }

    private static string FormatUnknownOperation(byte functionCode)
    {
        return $"function{functionCode.ToString(CultureInfo.InvariantCulture)}";
    }
}
