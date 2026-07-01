# ADR-0007: Explicit Client Retargeting For v0.1

## Status

Accepted for planning.

## Context

MITMI v0.1 is an explicit in-path Modbus TCP diagnostic proxy. There are several ways to place a diagnostic tool between a client and server:

- Reconfigure the client to connect to MITMI.
- Reconfigure DNS or hostnames.
- Use transparent proxying or NAT.
- Use ARP spoofing or gateway interception.
- Use passive packet capture.

The first version targets engineer laptops and should keep installation and runtime assumptions lightweight.

## Decision

MITMI v0.1 will require clients to connect to MITMI's configured IP address and port instead of the original PLC/server endpoint.

MITMI will then connect to the configured upstream Modbus TCP server and forward traffic unchanged while recording diagnostics, logs, captures, and metrics.

## Rationale

Explicit client retargeting is simple, understandable, and consistent with the v0.1 diagnostic proxy model.

It avoids:

- Elevated packet-capture permissions.
- OS-specific transparent proxy setup.
- NAT or routing configuration.
- ARP spoofing behavior.
- DNS manipulation.
- Ambiguous security posture.

## Consequences

Positive:

- Easier to implement and test.
- Easier to explain to field users.
- More compatible with Windows and Linux console operation.
- Clearer security posture.
- Fewer surprises for customer IT teams.

Negative:

- Users must be able to change the client's target IP/port, hostname, or simulator configuration.
- Some production systems may not allow quick client retargeting.
- MITMI cannot observe traffic that continues to go directly to the original endpoint.
- This model is less seamless than transparent interception.

## Guardrails

- Startup diagnostics should show the local listen endpoint and upstream endpoint.
- Documentation should explain that clients must connect to MITMI for v0.1.
- MITMI should not attempt transparent interception in v0.1.
- MITMI should not use ARP spoofing, DNS spoofing, or packet capture for v0.1 diagnostic proxy operation.
- Configuration should name the two sides clearly: local listen endpoint and upstream target endpoint.

## Future Considerations

Later versions may explore:

- Passive sniffing.
- Transparent proxying.
- Gateway deployment.
- DNS-based convenience workflows.
- Industrial-PC or edge-gateway placement.

These should be treated as separate capabilities with separate security, platform, and support requirements.
