using Mitmi.Application.Protocols;

namespace Mitmi.Protocols.Modbus;

public static class ModbusProtocolRegistration
{
    public static void Register(ProtocolRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        registry.Register(new ModbusTcpProtocolPlugin());
    }
}
