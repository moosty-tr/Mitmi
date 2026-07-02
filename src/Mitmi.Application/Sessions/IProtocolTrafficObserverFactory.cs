using Mitmi.Domain;

namespace Mitmi.Application.Sessions;

public interface IProtocolTrafficObserverFactory
{
    IProtocolTrafficObserver Create(SessionId sessionId, ConnectionId connectionId);
}
