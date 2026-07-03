# 2026-07-03 Modbus Observed-Value State Model Design

## Summary

Added a design note for the Modbus observed-value state model needed before webhook delivery.

Documented decisions:

- State should consume matched transaction analyzer results, not raw TCP chunks.
- Zero-based PDU addresses remain the state and filter source of truth.
- Read responses update state only when matched with request context.
- Write requests update state only after successful matched write responses.
- Exception responses, unknown functions, malformed frames, and unmatched responses do not update state.
- State keys are session, upstream endpoint, unit, table, and zero-based address.
- Value storage remains protocol-level: booleans for bit tables and unsigned 16-bit words for register tables.
- Future webhooks should consume state update groups instead of reconstructing payloads from raw frames.
- The model must be bounded and keep forwarding independent from diagnostics.

## Physical-Test Decision

No physical retest is required for this design-only slice. Physical validation becomes useful after webhook delivery behavior exists or after state-derived output becomes field-facing runtime behavior.
