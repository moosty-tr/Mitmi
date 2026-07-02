namespace Mitmi.Protocols.Modbus.Framing;

public static class ModbusTcpWarningCodes
{
    public const string DuplicatePendingRequest = "MODBUS_DUPLICATE_PENDING_REQUEST";
    public const string InvalidLength = "MODBUS_INVALID_LENGTH";
    public const string NonZeroProtocolIdentifier = "MODBUS_NON_ZERO_PROTOCOL_IDENTIFIER";
    public const string ResponseFunctionMismatch = "MODBUS_RESPONSE_FUNCTION_MISMATCH";
    public const string ResponseWithoutRequest = "MODBUS_RESPONSE_WITHOUT_REQUEST";
}
