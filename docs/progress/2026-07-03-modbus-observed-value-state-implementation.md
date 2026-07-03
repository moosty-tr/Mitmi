# 2026-07-03 Modbus Observed-Value State Implementation

## Summary

Added the first protocol-internal Modbus observed-value state model.

Implemented behavior:

- Tracks observed Modbus cells by session, upstream endpoint, unit ID, table, and zero-based PDU address.
- Expands matched read responses for coils, discrete inputs, holding registers, and input registers.
- Expands successful matched write responses for single and multiple coil/register writes using request payload values.
- Distinguishes observed cells from changed cells.
- Treats first observations as changes with no previous value.
- Ignores exception responses and unmatched or unsupported transactions.
- Bounds retained cells and update group size with visible counters for skipped cells.

## Verification

Automated validation:

```text
dotnet test Mitmi.slnx --no-restore
```

Result: 60 tests passed.

## Physical-Test Decision

No physical retest is required for this slice because the state model is not wired into runtime output or webhook delivery. It is protocol-internal code covered by unit tests and does not change forwarding, decoding, correlation, capture/log output, startup workflow, or shutdown behavior.
