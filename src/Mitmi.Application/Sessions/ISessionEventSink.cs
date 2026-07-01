namespace Mitmi.Application.Sessions;

public interface ISessionEventSink
{
    ValueTask EmitAsync(SessionEvent sessionEvent, CancellationToken cancellationToken);
}
