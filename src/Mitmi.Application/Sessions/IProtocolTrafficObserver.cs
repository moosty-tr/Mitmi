namespace Mitmi.Application.Sessions;

public interface IProtocolTrafficObserver
{
    ValueTask ObserveAsync(
        ProtocolTrafficObservation observation,
        CancellationToken cancellationToken);
}
