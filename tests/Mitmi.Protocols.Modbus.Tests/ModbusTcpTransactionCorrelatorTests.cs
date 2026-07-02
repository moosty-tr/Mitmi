using Mitmi.Protocols.Modbus.Framing;

namespace Mitmi.Protocols.Modbus.Tests;

public sealed class ModbusTcpTransactionCorrelatorTests
{
    [Fact]
    public void Observe_matches_response_to_request_by_transaction_and_unit()
    {
        var correlator = new ModbusTcpTransactionCorrelator();
        var request = Decode(ReadHoldingRegistersRequest(), ModbusTcpFrameDirection.ClientToServer);
        var response = Decode(ReadHoldingRegistersResponse(), ModbusTcpFrameDirection.ServerToClient);

        var requestEvent = correlator.Observe(request);
        var responseEvent = correlator.Observe(response);

        Assert.Equal(ModbusTcpTransactionEventKind.RequestObserved, requestEvent.Kind);
        Assert.Equal(ModbusTcpTransactionEventKind.ResponseMatched, responseEvent.Kind);
        Assert.Same(request, responseEvent.RequestFrame);
        Assert.Equal(request.CorrelationKey.ToString(), responseEvent.CorrelationId);
        Assert.Empty(responseEvent.Warnings);
    }

    [Fact]
    public void Observe_keeps_response_pending_when_request_has_not_been_observed_yet()
    {
        var correlator = new ModbusTcpTransactionCorrelator();
        var response = Decode(ReadHoldingRegistersResponse(), ModbusTcpFrameDirection.ServerToClient);

        var responseEvent = correlator.Observe(response);

        Assert.Equal(ModbusTcpTransactionEventKind.ResponseAwaitingRequest, responseEvent.Kind);
        Assert.Empty(responseEvent.Warnings);
    }

    [Fact]
    public void Observe_matches_response_that_arrived_before_request_diagnostics()
    {
        var correlator = new ModbusTcpTransactionCorrelator();
        var response = Decode(ReadHoldingRegistersResponse(), ModbusTcpFrameDirection.ServerToClient);
        var request = Decode(ReadHoldingRegistersRequest(), ModbusTcpFrameDirection.ClientToServer);

        var responseEvent = correlator.Observe(response);
        var matchedEvent = correlator.Observe(request);

        Assert.Equal(ModbusTcpTransactionEventKind.ResponseAwaitingRequest, responseEvent.Kind);
        Assert.Equal(ModbusTcpTransactionEventKind.ResponseMatched, matchedEvent.Kind);
        Assert.Same(request, matchedEvent.RequestFrame);
        Assert.Same(response, matchedEvent.Frame);
    }

    [Fact]
    public void Observe_allows_exception_response_to_match_original_function_code()
    {
        var correlator = new ModbusTcpTransactionCorrelator();
        var request = Decode(ReadHoldingRegistersRequest(), ModbusTcpFrameDirection.ClientToServer);
        var exceptionResponse = Decode(ExceptionResponse(), ModbusTcpFrameDirection.ServerToClient);

        correlator.Observe(request);
        var responseEvent = correlator.Observe(exceptionResponse);

        Assert.Equal(ModbusTcpTransactionEventKind.ResponseMatched, responseEvent.Kind);
        Assert.True(exceptionResponse.IsExceptionResponse);
        Assert.Empty(responseEvent.Warnings);
    }

    [Fact]
    public void Observe_warns_when_response_function_does_not_match_request()
    {
        var correlator = new ModbusTcpTransactionCorrelator();
        var request = Decode(ReadHoldingRegistersRequest(), ModbusTcpFrameDirection.ClientToServer);
        var response = Decode(WriteSingleCoilResponse(), ModbusTcpFrameDirection.ServerToClient);

        correlator.Observe(request);
        var responseEvent = correlator.Observe(response);

        Assert.Equal(ModbusTcpTransactionEventKind.ResponseMatched, responseEvent.Kind);
        Assert.Contains(responseEvent.Warnings, warning => warning.Code == ModbusTcpWarningCodes.ResponseFunctionMismatch);
    }

    private static ModbusTcpFrame Decode(byte[] frameBytes, ModbusTcpFrameDirection direction)
    {
        var result = new ModbusTcpFrameDecoder().Append(frameBytes, direction);
        return Assert.Single(result.Frames);
    }

    private static byte[] ReadHoldingRegistersRequest() =>
    [
        0x00, 0x2A,
        0x00, 0x00,
        0x00, 0x06,
        0x01,
        0x03,
        0x00, 0x6B,
        0x00, 0x03
    ];

    private static byte[] ReadHoldingRegistersResponse() =>
    [
        0x00, 0x2A,
        0x00, 0x00,
        0x00, 0x09,
        0x01,
        0x03,
        0x06,
        0x02, 0x2B,
        0x00, 0x00,
        0x00, 0x64
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

    private static byte[] WriteSingleCoilResponse() =>
    [
        0x00, 0x2A,
        0x00, 0x00,
        0x00, 0x06,
        0x01,
        0x05,
        0x00, 0x13,
        0xFF, 0x00
    ];
}
