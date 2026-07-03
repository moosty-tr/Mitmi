using Mitmi.Domain;
using Mitmi.Protocols.Modbus.Diagnostics;
using Mitmi.Protocols.Modbus.Framing;

namespace Mitmi.Protocols.Modbus.Tests;

public sealed class ModbusObservedValueStateTests
{
    private static readonly SessionId SessionId = new("observed-state");
    private static readonly NetworkEndpoint UpstreamEndpoint = new("192.0.2.10", 502);

    [Fact]
    public void ObserveMatchedTransaction_updates_register_state_and_detects_changes()
    {
        var state = new ModbusObservedValueState();
        var firstObservedAt = DateTimeOffset.Parse("2026-07-03T10:00:00+00:00");
        var secondObservedAt = DateTimeOffset.Parse("2026-07-03T10:00:05+00:00");
        var changedObservedAt = DateTimeOffset.Parse("2026-07-03T10:00:10+00:00");

        var firstGroup = state.ObserveMatchedTransaction(
            SessionId,
            UpstreamEndpoint,
            Match(ReadHoldingRegistersRequest(), ReadHoldingRegistersResponse(lastValue: 0x0064)),
            firstObservedAt);

        Assert.NotNull(firstGroup);
        Assert.Equal(ModbusObservedTable.HoldingRegisters, firstGroup!.Table);
        Assert.Equal((ushort)0x006B, firstGroup.Address);
        Assert.Equal((ushort)3, firstGroup.Quantity);
        Assert.Equal("107-109", firstGroup.AddressRange);
        Assert.Equal("zeroBasedPdu", firstGroup.AddressBase);
        Assert.Equal(3, firstGroup.ObservedCells.Count);
        Assert.Equal(3, firstGroup.ChangedCells.Count);
        Assert.Equal((ushort)0x022B, firstGroup.ObservedCells[0].CurrentValue.RegisterValue);
        Assert.Null(firstGroup.ObservedCells[0].PreviousValue);

        var unchangedGroup = state.ObserveMatchedTransaction(
            SessionId,
            UpstreamEndpoint,
            Match(ReadHoldingRegistersRequest(), ReadHoldingRegistersResponse(lastValue: 0x0064)),
            secondObservedAt);

        Assert.NotNull(unchangedGroup);
        Assert.Equal(3, unchangedGroup!.ObservedCells.Count);
        Assert.Empty(unchangedGroup.ChangedCells);
        Assert.All(unchangedGroup.ObservedCells, cell => Assert.False(cell.Changed));

        var changedGroup = state.ObserveMatchedTransaction(
            SessionId,
            UpstreamEndpoint,
            Match(ReadHoldingRegistersRequest(), ReadHoldingRegistersResponse(lastValue: 0x0065)),
            changedObservedAt);

        Assert.NotNull(changedGroup);
        var changedCell = Assert.Single(changedGroup!.ChangedCells);
        Assert.Equal((ushort)0x006D, changedCell.Address);
        Assert.Equal((ushort)0x0064, changedCell.PreviousValue!.Value.RegisterValue);
        Assert.Equal((ushort)0x0065, changedCell.CurrentValue.RegisterValue);
        Assert.Equal(firstObservedAt, changedCell.FirstObservedAt);
        Assert.Equal(changedObservedAt, changedCell.LastObservedAt);
        Assert.Equal(changedObservedAt, changedCell.LastChangedAt);
    }

    [Fact]
    public void ObserveMatchedTransaction_updates_coil_state_from_successful_write_response()
    {
        var state = new ModbusObservedValueState();

        var group = state.ObserveMatchedTransaction(
            SessionId,
            UpstreamEndpoint,
            Match(WriteMultipleCoilsRequest(), WriteMultipleCoilsResponse()),
            DateTimeOffset.Parse("2026-07-03T10:05:00+00:00"));

        Assert.NotNull(group);
        Assert.Equal(ModbusObservedTable.Coils, group!.Table);
        Assert.Equal("19-28", group.AddressRange);
        Assert.Equal(10, group.ObservedCells.Count);
        Assert.Equal(10, group.ChangedCells.Count);
        Assert.True(group.ObservedCells.Single(cell => cell.Address == 19).CurrentValue.BooleanValue);
        Assert.False(group.ObservedCells.Single(cell => cell.Address == 20).CurrentValue.BooleanValue);
        Assert.True(group.ObservedCells.Single(cell => cell.Address == 27).CurrentValue.BooleanValue);
    }

    [Fact]
    public void ObserveMatchedTransaction_ignores_exception_responses()
    {
        var state = new ModbusObservedValueState();

        var group = state.ObserveMatchedTransaction(
            SessionId,
            UpstreamEndpoint,
            Match(ReadHoldingRegistersRequest(), ExceptionResponse()),
            DateTimeOffset.Parse("2026-07-03T10:10:00+00:00"));

        Assert.Null(group);
        Assert.Equal(0, state.ObservedCellCount);
    }

    [Fact]
    public void ObserveMatchedTransaction_prefers_existing_cells_when_state_bound_is_reached()
    {
        var state = new ModbusObservedValueState(new ModbusObservedValueStateOptions
        {
            MaxObservedCells = 1,
            MaxCellsPerUpdateGroup = 8
        });

        var group = state.ObserveMatchedTransaction(
            SessionId,
            UpstreamEndpoint,
            Match(ReadHoldingRegistersRequest(), ReadHoldingRegistersResponse(lastValue: 0x0064)),
            DateTimeOffset.Parse("2026-07-03T10:15:00+00:00"));

        Assert.NotNull(group);
        var observedCell = Assert.Single(group!.ObservedCells);
        Assert.Equal((ushort)0x006B, observedCell.Address);
        Assert.Equal(1, state.ObservedCellCount);
        Assert.Equal(2, state.SkippedNewCellCount);

        var existingCellGroup = state.ObserveMatchedTransaction(
            SessionId,
            UpstreamEndpoint,
            Match(ReadHoldingRegistersRequest(quantity: 1), ReadSingleHoldingRegisterResponse(value: 0x1234)),
            DateTimeOffset.Parse("2026-07-03T10:15:05+00:00"));

        Assert.NotNull(existingCellGroup);
        var changedCell = Assert.Single(existingCellGroup!.ChangedCells);
        Assert.Equal((ushort)0x006B, changedCell.Address);
        Assert.Equal((ushort)0x022B, changedCell.PreviousValue!.Value.RegisterValue);
        Assert.Equal((ushort)0x1234, changedCell.CurrentValue.RegisterValue);
        Assert.Equal(1, state.ObservedCellCount);
    }

    private static ModbusTcpTransactionEvent Match(
        byte[] requestBytes,
        byte[] responseBytes)
    {
        var correlator = new ModbusTcpTransactionCorrelator();
        var request = Decode(requestBytes, ModbusTcpFrameDirection.ClientToServer);
        var response = Decode(responseBytes, ModbusTcpFrameDirection.ServerToClient);

        correlator.Observe(request);
        return correlator.Observe(response);
    }

    private static ModbusTcpFrame Decode(
        byte[] frameBytes,
        ModbusTcpFrameDirection direction)
    {
        var result = new ModbusTcpFrameDecoder().Append(frameBytes, direction);
        return Assert.Single(result.Frames);
    }

    private static byte[] ReadHoldingRegistersRequest(ushort quantity = 3) =>
    [
        0x00, 0x2A,
        0x00, 0x00,
        0x00, 0x06,
        0x01,
        0x03,
        0x00, 0x6B,
        (byte)(quantity >> 8),
        (byte)quantity
    ];

    private static byte[] ReadHoldingRegistersResponse(ushort lastValue) =>
    [
        0x00, 0x2A,
        0x00, 0x00,
        0x00, 0x09,
        0x01,
        0x03,
        0x06,
        0x02, 0x2B,
        0x00, 0x00,
        (byte)(lastValue >> 8), (byte)lastValue
    ];

    private static byte[] ReadSingleHoldingRegisterResponse(ushort value) =>
    [
        0x00, 0x2A,
        0x00, 0x00,
        0x00, 0x05,
        0x01,
        0x03,
        0x02,
        (byte)(value >> 8), (byte)value
    ];

    private static byte[] ExceptionResponse() =>
    [
        0x00, 0x2A,
        0x00, 0x00,
        0x00, 0x03,
        0x01,
        0x83,
        0x02
    ];

    private static byte[] WriteMultipleCoilsRequest() =>
    [
        0x00, 0x33,
        0x00, 0x00,
        0x00, 0x09,
        0x01,
        0x0F,
        0x00, 0x13,
        0x00, 0x0A,
        0x02,
        0xCD,
        0x01
    ];

    private static byte[] WriteMultipleCoilsResponse() =>
    [
        0x00, 0x33,
        0x00, 0x00,
        0x00, 0x06,
        0x01,
        0x0F,
        0x00, 0x13,
        0x00, 0x0A
    ];
}
