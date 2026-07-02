using Mitmi.Protocols.Modbus.Framing;

namespace Mitmi.Protocols.Modbus.Tests;

public sealed class ModbusTcpFrameDecoderTests
{
    [Fact]
    public void Append_detects_complete_modbus_tcp_adu()
    {
        var decoder = new ModbusTcpFrameDecoder();

        var result = decoder.Append(
            ReadHoldingRegistersRequest(transactionId: 0x1234),
            ModbusTcpFrameDirection.ClientToServer);

        var frame = Assert.Single(result.Frames);
        Assert.Empty(result.Warnings);
        Assert.Equal(0x1234, frame.TransactionId);
        Assert.Equal(0, frame.ProtocolId);
        Assert.Equal(6, frame.Length);
        Assert.Equal(0x01, frame.UnitId);
        Assert.Equal(0x03, frame.FunctionCode);
        Assert.False(frame.IsExceptionResponse);
        Assert.Null(frame.ExceptionCode);
        Assert.Equal(new ModbusTcpCorrelationKey(0x1234, 0x01), frame.CorrelationKey);
    }

    [Fact]
    public void Append_reassembles_split_tcp_reads()
    {
        var decoder = new ModbusTcpFrameDecoder();
        var frameBytes = ReadHoldingRegistersRequest(transactionId: 0x0002);

        var first = decoder.Append(frameBytes.AsSpan(0, 5), ModbusTcpFrameDirection.ClientToServer);
        var second = decoder.Append(frameBytes.AsSpan(5), ModbusTcpFrameDirection.ClientToServer);

        Assert.Empty(first.Frames);
        Assert.Empty(first.Warnings);
        Assert.Single(second.Frames);
    }

    [Fact]
    public void Append_detects_multiple_frames_in_one_read()
    {
        var decoder = new ModbusTcpFrameDecoder();
        var combined = ReadHoldingRegistersRequest(transactionId: 1)
            .Concat(ReadHoldingRegistersRequest(transactionId: 2))
            .ToArray();

        var result = decoder.Append(combined, ModbusTcpFrameDirection.ClientToServer);

        Assert.Collection(
            result.Frames,
            first => Assert.Equal(1, first.TransactionId),
            second => Assert.Equal(2, second.TransactionId));
    }

    [Fact]
    public void Append_warns_for_non_zero_protocol_identifier_but_preserves_frame()
    {
        var decoder = new ModbusTcpFrameDecoder();
        var bytes = ReadHoldingRegistersRequest(transactionId: 1);
        bytes[3] = 0x01;

        var result = decoder.Append(bytes, ModbusTcpFrameDirection.ClientToServer);

        var frame = Assert.Single(result.Frames);
        Assert.Equal(1, frame.ProtocolId);
        Assert.Contains(frame.DecodeWarnings, warning => warning.Code == ModbusTcpWarningCodes.NonZeroProtocolIdentifier);
    }

    [Fact]
    public void Append_resynchronizes_after_invalid_length()
    {
        var decoder = new ModbusTcpFrameDecoder();
        var malformed = new byte[] { 0x99, 0x99, 0x00, 0x00, 0x00, 0x01, 0x01 };
        var valid = ReadHoldingRegistersRequest(transactionId: 0x0102);
        var combined = malformed.Concat(valid).ToArray();

        var result = decoder.Append(combined, ModbusTcpFrameDirection.ClientToServer);

        Assert.Contains(result.Warnings, warning => warning.Code == ModbusTcpWarningCodes.InvalidLength);
        var frame = Assert.Single(result.Frames);
        Assert.Equal(0x0102, frame.TransactionId);
    }

    [Fact]
    public void Append_identifies_exception_response()
    {
        var decoder = new ModbusTcpFrameDecoder();
        var response = new byte[]
        {
            0x00, 0x07,
            0x00, 0x00,
            0x00, 0x03,
            0x01,
            0x83,
            0x02
        };

        var result = decoder.Append(response, ModbusTcpFrameDirection.ServerToClient);

        var frame = Assert.Single(result.Frames);
        Assert.True(frame.IsExceptionResponse);
        Assert.Equal(0x03, frame.OperationFunctionCode);
        Assert.True(frame.ExceptionCode.HasValue);
        Assert.Equal((byte)0x02, frame.ExceptionCode.Value);
    }

    private static byte[] ReadHoldingRegistersRequest(ushort transactionId)
    {
        return
        [
            (byte)(transactionId >> 8), (byte)transactionId,
            0x00, 0x00,
            0x00, 0x06,
            0x01,
            0x03,
            0x00, 0x6B,
            0x00, 0x03
        ];
    }
}
