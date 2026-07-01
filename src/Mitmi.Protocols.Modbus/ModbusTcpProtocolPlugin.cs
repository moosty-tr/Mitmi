using Mitmi.Application.Protocols;

namespace Mitmi.Protocols.Modbus;

public sealed class ModbusTcpProtocolPlugin : IProtocolPlugin
{
    public const string ProtocolId = "modbus-tcp";

    public string Id => ProtocolId;

    public string DisplayName => "Modbus TCP";
}
