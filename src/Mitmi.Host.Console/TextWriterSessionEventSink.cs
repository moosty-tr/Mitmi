using Mitmi.Application.Sessions;

namespace Mitmi.Host.Console;

internal sealed class TextWriterSessionEventSink : ISessionEventSink
{
    private readonly TextWriter output;
    private readonly TextWriter error;

    public TextWriterSessionEventSink(TextWriter output, TextWriter error)
    {
        this.output = output;
        this.error = error;
    }

    public async ValueTask EmitAsync(SessionEvent sessionEvent, CancellationToken cancellationToken)
    {
        var writer = sessionEvent.Level == SessionEventLevel.Error ? error : output;
        await writer.WriteLineAsync(Render(sessionEvent));
    }

    private static string Render(SessionEvent sessionEvent)
    {
        var connection = sessionEvent.ConnectionId is null
            ? string.Empty
            : $" connection={sessionEvent.ConnectionId}";

        return $"{sessionEvent.Timestamp:O} [{sessionEvent.Level}] {sessionEvent.Name} session={sessionEvent.SessionId}{connection}: {sessionEvent.Message}";
    }
}
