# 2026-07-06 Observed-Value Webhook Delivery Implementation

## Summary

Added the first runtime implementation of observed-value webhooks.

Implemented behavior:

- Top-level `integrations.observedValueWebhook` configuration.
- Disabled-by-default webhook delivery.
- One configured webhook target.
- `ChangedCellsOnly` trigger mode.
- Optional filters by unit ID, Modbus table, and zero-based PDU address range.
- One JSON payload per observed-value update group after filtering.
- Bounded delivery queue with newest-update drops when full.
- One HTTP POST attempt with a short timeout.
- Failure, drop, and shutdown-summary session events.
- No retries, durable delivery, transformation, scripting, multiple targets, or stored secrets.

The Modbus protocol observer now wires matched transactions into `ModbusObservedValueState` and emits update groups through `IModbusObservedValueUpdateSink`. HTTP delivery remains host-owned; it is not part of Modbus parsing or forwarding.

## Verification

Automated validation added for:

- Webhook payload shape and range filtering.
- Queue-full newest-update drops.
- HTTP failure warning and failure counting.
- Configuration rejection when protocol decoding is disabled.
- Configuration rejection for unsupported authentication modes.
- Console-host runtime delivery through a local HTTP endpoint while Modbus forwarding still succeeds.

## Physical-Test Decision

Physical or hardware-adjacent validation should be run before calling webhook delivery field-ready.

Automated tests cover payload shape, filtering, local delivery, queue loss, and failure events. They do not prove firewall behavior, target-system timing, operator setup, or whether webhook delivery perturbs a real commissioning workflow.
