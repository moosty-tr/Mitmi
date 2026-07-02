using Mitmi.Domain;

namespace Mitmi.Application.Sessions;

public sealed record TrafficCaptureRecord(
    DateTimeOffset Timestamp,
    SessionId SessionId,
    ConnectionId ConnectionId,
    ProtocolId ProtocolId,
    TrafficDirection Direction,
    int PayloadLength,
    byte[]? RawPayload);
