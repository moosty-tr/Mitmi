using Mitmi.Application.Configuration;
using Mitmi.Application.Sessions;

namespace Mitmi.Host.Console;

internal sealed class TextWriterSessionEventSink : ISessionEventSink, IAsyncDisposable
{
    private readonly TextWriter output;
    private readonly TextWriter error;
    private readonly LoggingRuntimeOptions logging;
    private readonly TextWriter? fileWriter;
    private readonly SemaphoreSlim writeLock = new(1, 1);

    public TextWriterSessionEventSink(
        TextWriter output,
        TextWriter error,
        LoggingRuntimeOptions logging)
    {
        this.output = output;
        this.error = error;
        this.logging = logging;

        if (logging.File.Enabled && !string.IsNullOrWhiteSpace(logging.File.Path))
        {
            var directory = Path.GetDirectoryName(logging.File.Path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            fileWriter = new StreamWriter(new FileStream(
                logging.File.Path,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite))
            {
                AutoFlush = true
            };
        }
    }

    public async ValueTask EmitAsync(SessionEvent sessionEvent, CancellationToken cancellationToken)
    {
        var rendered = Render(sessionEvent);
        await writeLock.WaitAsync(cancellationToken);
        try
        {
            if (logging.Console.Enabled && IsEnabled(sessionEvent.Level, logging.Console.MinimumLevel))
            {
                var writer = sessionEvent.Level == SessionEventLevel.Error ? error : output;
                await writer.WriteLineAsync(rendered);
            }

            if (fileWriter is not null && IsEnabled(sessionEvent.Level, logging.File.MinimumLevel))
            {
                await fileWriter.WriteLineAsync(rendered);
            }
        }
        finally
        {
            writeLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await writeLock.WaitAsync();
        try
        {
            if (fileWriter is not null)
            {
                await fileWriter.FlushAsync();
                await fileWriter.DisposeAsync();
            }
        }
        finally
        {
            writeLock.Release();
            writeLock.Dispose();
        }
    }

    private static string Render(SessionEvent sessionEvent)
    {
        var connection = sessionEvent.ConnectionId is null
            ? string.Empty
            : $" connection={sessionEvent.ConnectionId}";

        return $"{sessionEvent.Timestamp:O} [{sessionEvent.Level}] {sessionEvent.Name} session={sessionEvent.SessionId}{connection}: {sessionEvent.Message}";
    }

    private static bool IsEnabled(SessionEventLevel eventLevel, ProductLogLevel minimumLevel)
    {
        return ToSeverity(eventLevel) >= ToSeverity(minimumLevel);
    }

    private static int ToSeverity(SessionEventLevel eventLevel)
    {
        return eventLevel switch
        {
            SessionEventLevel.Info => 1,
            SessionEventLevel.Warning => 2,
            SessionEventLevel.Error => 3,
            _ => 3
        };
    }

    private static int ToSeverity(ProductLogLevel productLogLevel)
    {
        return productLogLevel switch
        {
            ProductLogLevel.Debug => 0,
            ProductLogLevel.Info => 1,
            ProductLogLevel.Warning => 2,
            ProductLogLevel.Error => 3,
            _ => 3
        };
    }
}
