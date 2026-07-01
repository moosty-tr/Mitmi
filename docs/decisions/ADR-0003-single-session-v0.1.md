# ADR-0003: Single Diagnostic Session For v0.1

## Status

Accepted for planning.

## Context

MITMI v0.1 is intended to prove the diagnostic proxy experience before expanding into a broader platform. Supporting multiple diagnostic sessions from the beginning would affect configuration, runtime orchestration, logging, capture storage, metrics cardinality, shutdown behavior, error isolation, and future licensing limits.

## Decision

MITMI v0.1 will support one diagnostic session only.

The first configuration model should use a single `session` concept rather than a `sessions` array.

The architecture should still avoid hard-coding assumptions that would make multi-session support impossible later, but v0.1 implementation and user experience should not expose multi-session behavior.

## Rationale

One session is enough to validate the first product promise:

"Put MITMI between one Modbus TCP client path and one upstream Modbus TCP server, then observe and diagnose the communication."

This keeps the first release focused on:

- Reliable pass-through.
- Modbus-aware request/response diagnostics.
- Capture quality.
- Operational clarity.
- Low configuration complexity.

## Consequences

Positive:

- Simpler configuration.
- Simpler console UX.
- Simpler lifecycle management.
- Simpler logs, metrics, and captures.
- Lower risk of concurrency bugs in the first runnable version.
- Easier field troubleshooting.

Negative:

- Users with several client/server paths must run multiple MITMI processes.
- Multi-session support will require a later configuration migration.
- Some future Free/Professional licensing limits around concurrent sessions cannot be validated in v0.1.

## Guardrails

- Keep the runtime concept named `session` so future multi-session support can become a collection of the same concept.
- Include a configuration version field from the beginning.
- Avoid global singletons that would prevent multiple sessions later.
- Include session identifiers in logs and captures, even if v0.1 has only one session.
- Document that running multiple MITMI processes is the workaround for multiple paths in v0.1.

## Alternatives Considered

### Multiple Sessions From The Beginning

Rejected for v0.1. It adds orchestration and support complexity before the single-session diagnostic proxy is proven.

### `sessions` Array With Maximum One Entry

Rejected for v0.1. It preserves a future shape but creates unnecessary configuration ceremony for the first user experience.

### Completely Global Configuration Without A Session Concept

Rejected. It is simple now but creates a worse migration path later. MITMI should still model one session explicitly.
