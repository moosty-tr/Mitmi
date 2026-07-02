namespace Mitmi.Application.Sessions;

public interface ITrafficCaptureSink
{
    ValueTask CaptureAsync(TrafficCaptureRecord record, CancellationToken cancellationToken);
}
