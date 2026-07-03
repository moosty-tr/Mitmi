# Modbus Observed-Value State Model

## Purpose

This note defines the internal Modbus value state model needed before webhook delivery is implemented.

The goal is not to infer device meaning. The goal is to maintain a conservative, bounded view of values MITMI has actually observed in matched Modbus TCP transactions so later integrations can emit useful, testable update groups without blocking forwarding.

## Non-Goals

- No HTTP delivery.
- No retry, authentication, backoff, or webhook configuration.
- No device profiles, register naming, scaling, units, or type conversion.
- No write simulation or traffic modification.
- No replacement for raw NDJSON capture records. Captures remain the byte-level evidence.

## Source Of Truth

The source address model remains zero-based Modbus Protocol Data Unit offsets.

One-based and reference-style addresses may be rendered for humans, but state keys, filters, capture metadata, analyzer summaries, and future webhook payloads must carry zero-based PDU addresses.

## Inputs

The state model should consume protocol analyzer results after transaction correlation, not raw TCP chunks.

Accepted update inputs:

- Matched read responses for functions `1`, `2`, `3`, and `4`.
- Matched successful write responses for functions `5`, `6`, `15`, and `16`, using the request payload values as the accepted current values.

Rejected update inputs:

- Requests without successful matched responses.
- Exception responses.
- Responses without enough request context to know the requested address range.
- Malformed or partially decoded frames.
- Unknown function codes.

This is intentionally conservative. A request shows intent; a successful matched response is the first reliable point where MITMI can say the device accepted or returned a value.

## State Key

Each observed cell is keyed by:

```text
sessionId
upstreamEndpoint
unitId
table
zeroBasedPduAddress
```

`table` is derived from the function:

| Function | Table |
| ---: | --- |
| `1`, `5`, `15` | `coils` |
| `2` | `discreteInputs` |
| `3`, `6`, `16` | `holdingRegisters` |
| `4` | `inputRegisters` |

The key must not use operation names as the primary partition because multiple functions can observe or write the same table.

## Value Shape

The model should store protocol-level values only:

| Table | Value Kind |
| --- | --- |
| `coils` | Boolean |
| `discreteInputs` | Boolean |
| `holdingRegisters` | Unsigned 16-bit register word |
| `inputRegisters` | Unsigned 16-bit register word |

Each cell should retain:

- Current value.
- Previous value when changed.
- First observed UTC timestamp.
- Last observed UTC timestamp.
- Last changed UTC timestamp.
- Last correlation ID.
- Last operation and function code.

No signed interpretation, float decoding, bitfield naming, scaling, units, or device-specific labels belong in this state layer.

## Update Group

The state model should produce one update group per matched Modbus transaction that yields values.

An update group should contain:

- Session ID.
- Upstream endpoint.
- Unit ID.
- Function code.
- Operation.
- Table.
- Address base: `zeroBasedPdu`.
- Requested zero-based address range.
- Observed cells.
- Changed cells.
- Previous values for changed cells.
- Current values.
- Correlation ID.
- Observed UTC timestamp.
- Human-readable summary.

The update group is the future webhook payload source. Webhook delivery should not reconstruct state directly from raw frames.

## Change Detection

The state model should distinguish two concepts:

- `observed`: MITMI saw a value in a successful matched transaction.
- `changed`: the observed value differs from the last stored value for the same key.

First observation of a key is both `observed` and `changed` with no previous value.

Later webhook configuration can decide whether to trigger on every observation or only on changed cells. The state model should expose both so delivery policy does not rewrite protocol logic.

## Range Filters

Future webhook filters should target:

```text
unitId
table
zeroBasedPduAddressStart
zeroBasedPduAddressEnd
```

Filters should be evaluated against state keys and update groups, not human address renderings. Reference-style address input can be added later as a configuration convenience only if it normalizes to zero-based PDU ranges before runtime.

## Boundedness

The state model must be bounded. A client can scan large address ranges or many unit IDs, and MITMI should not accumulate unlimited state.

Initial bounds should include:

- Maximum observed cells per session.
- Maximum cells in one update group.
- Loss counters for cells or update groups skipped due to bounds.

When the state is full, MITMI should continue forwarding. Diagnostics should prefer updating existing keys over accepting new keys, and should emit visible loss warnings plus shutdown summaries.

## Concurrency

State mutation should happen behind the existing bounded diagnostics path, never in the forwarding hot path.

Within one Modbus observer, updates should remain serialized with transaction correlation so request/response context is coherent. Cross-session concurrency can be added later when v0.1 grows beyond one configured session.

## Physical-Test Decision

Designing this model does not require a physical test.

A physical test becomes useful after webhook delivery behavior exists or after state-derived runtime output is promoted into field-facing logs, captures, or reports. At that point the physical runbook should verify timing, operator workflow, and that webhook/state diagnostics do not perturb forwarding.
