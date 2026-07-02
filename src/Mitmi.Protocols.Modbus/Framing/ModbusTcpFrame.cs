namespace Mitmi.Protocols.Modbus.Framing;

public sealed class ModbusTcpFrame
{
    public ModbusTcpFrame(
        ModbusTcpFrameDirection direction,
        ushort transactionId,
        ushort protocolId,
        ushort length,
        byte unitId,
        byte functionCode,
        byte[] rawFrame,
        IReadOnlyList<ModbusTcpDecodeWarning> decodeWarnings)
    {
        Direction = direction;
        TransactionId = transactionId;
        ProtocolId = protocolId;
        Length = length;
        UnitId = unitId;
        FunctionCode = functionCode;
        RawFrame = rawFrame.ToArray();
        DecodeWarnings = decodeWarnings.ToArray();
    }

    public ModbusTcpFrameDirection Direction { get; }

    public ushort TransactionId { get; }

    public ushort ProtocolId { get; }

    public ushort Length { get; }

    public byte UnitId { get; }

    public byte FunctionCode { get; }

    public byte OperationFunctionCode => IsExceptionResponse
        ? (byte)(FunctionCode & 0x7F)
        : FunctionCode;

    public bool IsExceptionResponse =>
        Direction == ModbusTcpFrameDirection.ServerToClient &&
        (FunctionCode & 0x80) == 0x80;

    public byte? ExceptionCode =>
        IsExceptionResponse && RawFrame.Length >= 9
            ? RawFrame[8]
            : null;

    public ModbusTcpCorrelationKey CorrelationKey => new(TransactionId, UnitId);

    public byte[] RawFrame { get; }

    public IReadOnlyList<ModbusTcpDecodeWarning> DecodeWarnings { get; }
}
