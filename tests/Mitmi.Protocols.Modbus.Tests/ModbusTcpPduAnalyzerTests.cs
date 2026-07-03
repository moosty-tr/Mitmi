using Mitmi.Protocols.Modbus.Diagnostics;
using Mitmi.Protocols.Modbus.Framing;

namespace Mitmi.Protocols.Modbus.Tests;

public sealed class ModbusTcpPduAnalyzerTests
{
    [Fact]
    public void Analyze_extracts_address_and_quantity_from_read_input_register_request()
    {
        var frame = Decode(ReadInputRegistersRequest(), ModbusTcpFrameDirection.ClientToServer);

        var analysis = ModbusTcpPduAnalyzer.Analyze(frame);

        Assert.Equal("readInputRegisters", analysis.Operation);
        Assert.Equal((ushort)0x006B, analysis.Address);
        Assert.Equal((ushort)3, analysis.Quantity);
        Assert.Equal("107-109", analysis.AddressRange);
        Assert.Null(analysis.ValuesHex);
    }

    [Fact]
    public void Analyze_extracts_register_values_from_read_input_register_response()
    {
        var request = Decode(ReadInputRegistersRequest(), ModbusTcpFrameDirection.ClientToServer);
        var response = Decode(ReadInputRegistersResponse(), ModbusTcpFrameDirection.ServerToClient);

        var analysis = ModbusTcpPduAnalyzer.Analyze(response, request);

        Assert.Equal("readInputRegisters", analysis.Operation);
        Assert.Equal((ushort)0x006B, analysis.Address);
        Assert.Equal((ushort)3, analysis.Quantity);
        Assert.Equal("107-109", analysis.AddressRange);
        Assert.Equal("0000,005e,0023", analysis.ValuesHex);
        Assert.Equal(6, analysis.ByteCount);
    }

    [Fact]
    public void Analyze_extracts_write_multiple_register_values()
    {
        var frame = Decode(WriteMultipleRegistersRequest(), ModbusTcpFrameDirection.ClientToServer);

        var analysis = ModbusTcpPduAnalyzer.Analyze(frame);

        Assert.Equal("writeMultipleRegisters", analysis.Operation);
        Assert.Equal((ushort)0x0010, analysis.Address);
        Assert.Equal((ushort)2, analysis.Quantity);
        Assert.Equal("16-17", analysis.AddressRange);
        Assert.Equal("000a,00ff", analysis.ValuesHex);
        Assert.Equal(4, analysis.ByteCount);
    }

    private static ModbusTcpFrame Decode(byte[] frameBytes, ModbusTcpFrameDirection direction)
    {
        var result = new ModbusTcpFrameDecoder().Append(frameBytes, direction);
        return Assert.Single(result.Frames);
    }

    private static byte[] ReadInputRegistersRequest() =>
    [
        0x00, 0x2A,
        0x00, 0x00,
        0x00, 0x06,
        0x01,
        0x04,
        0x00, 0x6B,
        0x00, 0x03
    ];

    private static byte[] ReadInputRegistersResponse() =>
    [
        0x00, 0x2A,
        0x00, 0x00,
        0x00, 0x09,
        0x01,
        0x04,
        0x06,
        0x00, 0x00,
        0x00, 0x5E,
        0x00, 0x23
    ];

    private static byte[] WriteMultipleRegistersRequest() =>
    [
        0x00, 0x11,
        0x00, 0x00,
        0x00, 0x0B,
        0x01,
        0x10,
        0x00, 0x10,
        0x00, 0x02,
        0x04,
        0x00, 0x0A,
        0x00, 0xFF
    ];
}
