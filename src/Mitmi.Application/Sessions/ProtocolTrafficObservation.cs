using Mitmi.Domain;

namespace Mitmi.Application.Sessions;

public sealed record ProtocolTrafficObservation(
    SessionId SessionId,
    ConnectionId ConnectionId,
    TrafficDirection Direction,
    ReadOnlyMemory<byte> Payload);
