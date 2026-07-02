using Mitmi.Application.Sessions;
using Mitmi.Domain;

namespace Mitmi.Protocols.Modbus.Diagnostics;

public sealed class ModbusTcpTrafficObserverFactory : IProtocolTrafficObserverFactory
{
    private readonly ISessionEventSink eventSink;

    public ModbusTcpTrafficObserverFactory(ISessionEventSink eventSink)
    {
        this.eventSink = eventSink;
    }

    public IProtocolTrafficObserver Create(SessionId sessionId, ConnectionId connectionId)
    {
        return new ModbusTcpTrafficObserver(eventSink);
    }
}
