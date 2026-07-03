# Observed-Value Webhook Delivery Design

## Purpose

This note defines the first maintainable shape for observed-value webhooks after the Modbus observed-value state model exists.

Webhooks are an integration adapter. They must not redefine Modbus analysis, own forwarding decisions, or become a hidden automation engine.

## Design Position

The first implementation should be conservative:

- Disabled by default.
- One configured observed-value webhook target for v0.1.
- Triggered from observed-value update groups, not raw frames.
- Bounded queue between state updates and HTTP delivery.
- Short HTTP timeout.
- No synchronous wait in the forwarding path.
- No traffic modification.

Multiple webhook targets, complex routing, durable delivery, replay of failed events, scripting, templating, and transformation should stay out of the first delivery slice. Those features are integration-platform territory; adding them now would make MITMI bigger than the diagnostic proxy we are trying to harden.

## Configuration Direction

Add a top-level integration section when implementation begins:

```json
{
  "integrations": {
    "observedValueWebhook": {
      "enabled": false,
      "url": "https://example.invalid/mitmi/observed-values",
      "trigger": {
        "mode": "ChangedCellsOnly",
        "ranges": [
          {
            "unitId": 1,
            "table": "holdingRegisters",
            "startAddress": 0,
            "endAddress": 99
          }
        ]
      },
      "delivery": {
        "timeoutMilliseconds": 2000,
        "queueCapacity": 256
      },
      "authentication": {
        "mode": "None"
      }
    }
  }
}
```

`integrations` is top-level because webhook delivery is not Modbus parsing, capture storage, logging, or session configuration. The first target is specific to observed values because that is the accepted use case; a generic integration framework would be premature.

## Trigger Semantics

The first trigger mode should be `ChangedCellsOnly`.

`AllObservations` can be added later if users need heartbeat-style updates or full polling traces. Starting with every observation risks noisy integrations and hides whether state comparison is working.

Range filters should use:

- Unit ID.
- Table.
- Zero-based PDU start address.
- Zero-based PDU end address.

Reference-style filter input should be deferred. If added later, it must normalize to zero-based PDU ranges during configuration validation.

## Payload Contract

Send one JSON document per observed-value update group after filtering.

Required fields:

- Payload schema version.
- Session ID.
- Upstream endpoint.
- Unit ID.
- Function code.
- Operation.
- Table.
- Address base: `zeroBasedPdu`.
- Requested address range.
- Observed UTC timestamp.
- Correlation ID.
- Observed cells.
- Changed cells.
- Human-readable summary.

Cell fields:

- Zero-based PDU address.
- Value kind: `boolean` or `register`.
- Current value.
- Previous value when known and changed.
- First observed UTC timestamp.
- Last observed UTC timestamp.
- Last changed UTC timestamp.

Register values should be emitted as unsigned integer values plus fixed-width lowercase hex text. Boolean values should be JSON booleans.

## Delivery And Failure Policy

The first implementation should attempt delivery once with a short timeout.

I do not recommend retries in the first slice. Retries create ordering, stale-data, backpressure, and shutdown questions that are easy to underestimate. For a diagnostic proxy, visible failure and bounded loss are safer than building an unreliable message broker inside the process.

Initial failure behavior:

- Emit a warning event when delivery first fails.
- Count failed deliveries.
- Count queue drops.
- Emit a shutdown summary.
- Do not log full payloads at `Info`.

Retry policy can be added later after we have evidence about the real target systems and whether stale events are useful or harmful.

## Queue Policy

Use a bounded channel between observed-value state updates and delivery.

When the queue is full, drop the newest update and increment a loss counter. This preserves FIFO ordering for accepted updates and matches MITMI's current latency-first diagnostic posture.

Forwarding must never wait for the webhook queue.

## Authentication

The first implementation should support `None` only, or at most one static header loaded from an environment variable.

Do not store webhook secrets directly in `mitmi.config.json`. If header authentication is added, prefer:

```json
{
  "authentication": {
    "mode": "HeaderFromEnvironment",
    "headerName": "X-MITMI-Token",
    "environmentVariable": "MITMI_WEBHOOK_TOKEN"
  }
}
```

Certificate pinning, OAuth, mTLS, and secret files are out of scope for the first delivery slice.

## Privacy

Observed-value webhook payloads may contain process values, timing patterns, device addresses, and operational behavior.

Defaults must keep webhooks disabled. Startup diagnostics should warn when a webhook target is enabled. Support bundles should include configuration but must not include environment-derived secrets.

## Physical-Test Boundary

Webhook delivery changes need physical or hardware-adjacent validation before MITMI claims field readiness for the feature.

Automated tests can prove filtering, payload shape, queue loss, timeout behavior, and local HTTP delivery. They cannot prove firewall behavior, target-system timing, operator setup, or whether webhook delivery perturbs a real commissioning workflow.
