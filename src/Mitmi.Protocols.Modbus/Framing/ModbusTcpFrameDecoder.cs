namespace Mitmi.Protocols.Modbus.Framing;

public sealed class ModbusTcpFrameDecoder
{
    private const int HeaderLength = 7;
    private const int MinimumMbapLengthValue = 2;
    private const int MaximumMbapLengthValue = 254;

    private readonly List<byte> buffer = [];

    public ModbusTcpDecodeResult Append(
        ReadOnlySpan<byte> bytes,
        ModbusTcpFrameDirection direction)
    {
        for (var index = 0; index < bytes.Length; index++)
        {
            buffer.Add(bytes[index]);
        }

        var frames = new List<ModbusTcpFrame>();
        var warnings = new List<ModbusTcpDecodeWarning>();

        while (buffer.Count >= HeaderLength)
        {
            var protocolId = ReadUInt16(buffer, offset: 2);
            var length = ReadUInt16(buffer, offset: 4);

            if (length is < MinimumMbapLengthValue or > MaximumMbapLengthValue)
            {
                warnings.Add(new ModbusTcpDecodeWarning(
                    ModbusTcpWarningCodes.InvalidLength,
                    $"Discarded one byte while resynchronizing after invalid MBAP length '{length}'."));
                buffer.RemoveAt(0);
                continue;
            }

            var frameLength = 6 + length;
            if (buffer.Count < frameLength)
            {
                break;
            }

            var rawFrame = buffer.GetRange(0, frameLength).ToArray();
            var frameWarnings = new List<ModbusTcpDecodeWarning>();
            if (protocolId != 0)
            {
                frameWarnings.Add(new ModbusTcpDecodeWarning(
                    ModbusTcpWarningCodes.NonZeroProtocolIdentifier,
                    $"MBAP protocol identifier is '{protocolId}', expected '0'."));
            }

            frames.Add(new ModbusTcpFrame(
                direction,
                transactionId: ReadUInt16(rawFrame, offset: 0),
                protocolId,
                length,
                unitId: rawFrame[6],
                functionCode: rawFrame[7],
                rawFrame,
                frameWarnings));

            buffer.RemoveRange(0, frameLength);
        }

        return new ModbusTcpDecodeResult(frames, warnings);
    }

    private static ushort ReadUInt16(IReadOnlyList<byte> bytes, int offset)
    {
        return (ushort)((bytes[offset] << 8) | bytes[offset + 1]);
    }
}
