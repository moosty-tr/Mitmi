using Mitmi.Domain;

namespace Mitmi.Application.Sessions;

public sealed record SessionEvent(
    DateTimeOffset Timestamp,
    SessionEventLevel Level,
    string Name,
    SessionId SessionId,
    ConnectionId? ConnectionId,
    string Message,
    Exception? Exception = null);
