using Mitmi.Application.Configuration;
using Mitmi.Application.Sessions;
using Mitmi.Domain;
using Mitmi.Host.Console;

namespace Mitmi.IntegrationTests;

public sealed class TextWriterSessionEventSinkTests
{
    [Fact]
    public async Task EmitAsync_filters_console_events_by_configured_minimum_level()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        await using var sink = new TextWriterSessionEventSink(
            output,
            error,
            new LoggingRuntimeOptions(
                new LogSinkRuntimeOptions(true, ProductLogLevel.Warning, null),
                new LogSinkRuntimeOptions(false, ProductLogLevel.Info, null)));

        await sink.EmitAsync(CreateEvent(SessionEventLevel.Info, "info.event"), CancellationToken.None);
        await sink.EmitAsync(CreateEvent(SessionEventLevel.Warning, "warning.event"), CancellationToken.None);
        await sink.EmitAsync(CreateEvent(SessionEventLevel.Error, "error.event"), CancellationToken.None);

        Assert.DoesNotContain("info.event", output.ToString());
        Assert.Contains("warning.event", output.ToString());
        Assert.Contains("error.event", error.ToString());
    }

    [Fact]
    public async Task EmitAsync_writes_file_events_using_independent_minimum_level()
    {
        using var tempDirectory = new TemporaryDirectory();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var logPath = Path.Combine(tempDirectory.Path, "logs", "mitmi.log");

        await using (var sink = new TextWriterSessionEventSink(
            output,
            error,
            new LoggingRuntimeOptions(
                new LogSinkRuntimeOptions(true, ProductLogLevel.Error, null),
                new LogSinkRuntimeOptions(true, ProductLogLevel.Info, logPath))))
        {
            await sink.EmitAsync(CreateEvent(SessionEventLevel.Info, "info.event"), CancellationToken.None);
            await sink.EmitAsync(CreateEvent(SessionEventLevel.Error, "error.event"), CancellationToken.None);
        }

        var fileLog = await File.ReadAllTextAsync(logPath);
        Assert.DoesNotContain("info.event", output.ToString());
        Assert.Contains("error.event", error.ToString());
        Assert.Contains("info.event", fileLog);
        Assert.Contains("error.event", fileLog);
    }

    private static SessionEvent CreateEvent(SessionEventLevel level, string name)
    {
        return new SessionEvent(
            DateTimeOffset.Parse("2026-07-02T09:00:00+00:00"),
            level,
            name,
            new SessionId("test"),
            new ConnectionId(1),
            $"Message for {name}.");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mitmi-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
