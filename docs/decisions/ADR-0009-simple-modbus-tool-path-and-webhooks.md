# ADR-0009: Keep MITMI Simple And Defer Active Features

## Status

Accepted.

## Context

The Modbus TCP diagnostic proxy, protocol analyzer logs, structured analyzer summary, and diagnostics bundle now cover the immediate field workflow: observe traffic, preserve evidence, and help engineers discover device behavior.

Replay and fault simulation were previously considered future directions. The current product direction is intentionally simpler: keep MITMI focused on diagnostics and reverse engineering instead of becoming a broad lab simulator.

The user also identified webhook integrations as useful for automation scenarios, especially when a register value changes or when a specific configured range changes.

## Decision

MITMI will skip replay and fault simulation for the current product direction.

Bridging remains possible, but it should stay late in the roadmap because it requires mapping, type conversion, write semantics, error policy, and safety controls.

Webhook integrations are accepted as a candidate future capability, but not as part of the current report slice. Webhooks should be designed as an explicit integration feature that builds on reliable Modbus analyzer state.

Initial webhook direction:

- Trigger on any observed value update.
- Trigger on updates inside a configured Modbus range.
- Send one JSON document per observed update group rather than one HTTP request per register when multiple values change together.
- Include at least upstream device endpoint, unit ID, function, operation, address base, changed address range, previous values, current values, timestamp, session ID, and a human-readable description.

## Consequences

- Device-discovery reports and analyzer artifacts take priority over replay.
- Change detection needs a durable internal model of observed Modbus values before webhook delivery is implemented.
- Webhook delivery must not block forwarding.
- Webhook retries, authentication, backoff, failure logging, payload size, and privacy/redaction need explicit design before implementation.
