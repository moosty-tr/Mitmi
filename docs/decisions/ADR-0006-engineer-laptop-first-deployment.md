# ADR-0006: Engineer Laptop First Deployment Target

## Status

Accepted for planning.

## Context

MITMI v0.1 is a single-session Modbus TCP diagnostic proxy. The first target deployment affects configuration defaults, packaging assumptions, logging/capture paths, runtime behavior, documentation, and support expectations.

Possible first targets:

- Engineer laptop.
- Industrial PC.
- Server or VM.
- Edge gateway.

## Decision

MITMI v0.1 will target engineer laptops first.

The installation and runtime assumptions should remain lightweight. MITMI should run as a console application on Windows and Linux without requiring service installation, daemon setup, a web dashboard, centralized management, or elevated packet-capture permissions.

## Rationale

Engineer laptop first matches the diagnostic field workflow:

- A user is near the equipment or test bench.
- The user starts MITMI intentionally for a troubleshooting session.
- The user wants live console feedback.
- The user wants file logs and captures after the run.
- The user can edit a JSON configuration file.
- The user can point a client, simulator, SCADA test instance, or PLC communication path at MITMI.

This keeps v0.1 focused and avoids premature service-management architecture.

## Consequences

Positive:

- Lighter installation assumptions.
- Easier early testing.
- Better fit for CLI-first operation.
- Local logs and captures are natural.
- No need to design production service lifecycle in v0.1.

Negative:

- Laptop sleep, hibernation, VPN changes, Wi-Fi changes, and adapter switching can disrupt sessions.
- Local firewall rules may block listening ports.
- Corporate endpoint security may interfere with proxy behavior.
- Relative output paths can be confusing depending on the working directory.
- Long-running unattended operation is not the primary v0.1 scenario.

## Guardrails

- Startup diagnostics should report listen endpoint, upstream endpoint, log path, capture path, and likely configuration issues.
- Documentation should mention firewall and port-binding considerations.
- MITMI should not require elevated packet-capture permissions in v0.1.
- Avoid requiring installation as a Windows Service or Linux daemon for v0.1.
- Use explicit output paths or clearly resolve relative paths at startup.
- Treat laptop sleep or network changes as expected operational hazards, not hidden bugs.

## Not In v0.1

- Windows Service installation.
- Linux systemd unit management.
- Remote management.
- Fleet deployment.
- Centralized dashboards.
- Production gateway hardening.

These may become important later, but they should not shape the first diagnostic proxy implementation.
