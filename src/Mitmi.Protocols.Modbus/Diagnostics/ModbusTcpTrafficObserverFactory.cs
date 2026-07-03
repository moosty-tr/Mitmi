using Mitmi.Application.Sessions;
using Mitmi.Domain;

namespace Mitmi.Protocols.Modbus.Diagnostics;

public sealed class ModbusTcpTrafficObserverFactory : IProtocolTrafficObserverFactory
{
    private readonly ISessionEventSink eventSink;
    private readonly bool captureRawPayloads;
    private readonly ITrafficCaptureSink? trafficCaptureSink;

    public ModbusTcpTrafficObserverFactory(
        ISessionEventSink eventSink,
        ITrafficCaptureSink? trafficCaptureSink = null,
        bool captureRawPayloads = false)
    {
        this.eventSink = eventSink;
        this.trafficCaptureSink = trafficCaptureSink;
        this.captureRawPayloads = captureRawPayloads;
    }

    public IProtocolTrafficObserver Create(SessionId sessionId, ConnectionId connectionId)
    {
        return new ModbusTcpTrafficObserver(
            eventSink,
            trafficCaptureSink,
            captureRawPayloads);
    }
}
