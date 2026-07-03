using Mitmi.Domain;

namespace Mitmi.Application.Sessions;

public sealed record TrafficCaptureRecord(
    DateTimeOffset Timestamp,
    SessionId SessionId,
    ConnectionId ConnectionId,
    ProtocolId ProtocolId,
    TrafficDirection Direction,
    int PayloadLength,
    byte[]? RawPayload,
    string? CorrelationId = null,
    IReadOnlyDictionary<string, string>? ProtocolMetadata = null,
    IReadOnlyList<TrafficCaptureWarning>? DecodeWarnings = null);
