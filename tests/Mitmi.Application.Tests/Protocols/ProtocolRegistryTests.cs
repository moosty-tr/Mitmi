using Mitmi.Application.Protocols;
using Mitmi.Protocols.Modbus;

namespace Mitmi.Application.Tests.Protocols;

public sealed class ProtocolRegistryTests
{
    [Fact]
    public void Modbus_registration_adds_modbus_tcp_protocol()
    {
        var registry = new ProtocolRegistry();

        ModbusProtocolRegistration.Register(registry);

        Assert.Contains(ModbusTcpProtocolPlugin.ProtocolId, registry.ProtocolIds);
    }

    [Fact]
    public void Register_rejects_duplicate_protocol_ids()
    {
        var registry = new ProtocolRegistry();
        ModbusProtocolRegistration.Register(registry);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ModbusProtocolRegistration.Register(registry));

        Assert.Contains(ModbusTcpProtocolPlugin.ProtocolId, exception.Message);
    }
}
