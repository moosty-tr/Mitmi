# ADR-0008: Latency-First Forwarding With Asynchronous Diagnostics

## Status

Accepted for planning.

## Context

MITMI v0.1 is an explicit in-path diagnostic proxy. Because traffic flows through MITMI, the product can affect the very communication it is meant to observe.

Diagnostic depth is valuable, but forwarding reliability and latency matter more in the first version.

## Decision

MITMI v0.1 will prefer minimum latency for forwarding behavior.

Diagnostic detail should be gathered asynchronously wherever practical.

Forwarding must not depend on successful semantic decoding, log writing, capture flushing, or metrics export. If MITMI can safely forward bytes, it should forward bytes.

## Rationale

The first product promise is reliable diagnostic pass-through. A diagnostic proxy that adds noticeable delay, stalls on disk I/O, or blocks because decoding is slow can create misleading failures.

Asynchronous diagnostics allow MITMI to remain useful without placing every supportability concern directly in the hot forwarding path.

## Guardrails

- Forwarding should not wait for file log writes.
- Forwarding should not wait for capture writes, except during controlled shutdown or unavoidable backpressure decisions.
- Forwarding should not wait for metrics export.
- Modbus decoding failures should produce diagnostic warnings but should not stop pass-through by default.
- Diagnostic queues must be bounded or otherwise controlled.
- If diagnostic data is dropped because of pressure, MITMI must report that loss clearly.
- Critical transport errors still affect forwarding and must be reported immediately.

## Trade-Offs

Advantages:

- Lower risk that MITMI changes timing-sensitive behavior.
- Better field trust for a diagnostic proxy.
- Cleaner separation between forwarding and observability.
- Easier to reason about pass-through correctness.

Risks:

- Some diagnostic events may lag behind live traffic.
- Under sustained load, diagnostic queues may fill.
- Bounded queues require a policy for dropping, slowing, or failing.
- Capture completeness and latency-first forwarding can conflict.

## Recommended v0.1 Policy

For v0.1, prefer:

- Forward traffic first.
- Decode best-effort.
- Log asynchronously.
- Capture asynchronously.
- Emit metrics asynchronously.
- Report diagnostic loss or pressure explicitly.

If capture completeness becomes more important than latency for a future mode, introduce it as an explicit operating mode rather than changing diagnostic proxy defaults silently.

## Future Considerations

Future versions may support selectable operating modes:

- Low-latency diagnostic mode.
- Full-fidelity capture mode.
- Replay/test mode.
- Fault simulation mode.

Those modes may make different trade-offs, but v0.1 should keep the default simple and conservative.
