# 2026-07-02 Upstream Failure Lifecycle Accounting

## Summary

Closed a lifecycle accounting gap for upstream connection failures.

Implemented behavior:

- A client connection that cannot reach upstream now still emits the normal `connection.closed` event.
- Per-connection metrics are emitted for failed upstream attempts with zero forwarded bytes.
- Session metrics now count those failed attempts as both accepted and closed, while preserving the upstream failure count.
- Shutdown cancellation during upstream connect does not inflate the upstream failure count.

## Rationale

Field diagnostics need lifecycle counters that reconcile. An accepted client that fails before forwarding is still a completed connection attempt from the operator's perspective.

This keeps failure reporting predictable without changing forwarding behavior or adding new telemetry infrastructure.
