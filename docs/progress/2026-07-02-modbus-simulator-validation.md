# 2026-07-02 Modbus Simulator Validation

## Summary

Simulator validation confirmed the current v0.1 diagnostic proxy path works with a Modbus server simulator and a Modbus client.

Confirmed behavior:

- A default config file is created when no config file exists.
- `protocol.frame_decoded` console events are emitted.
- `protocol.transaction_matched` console events are emitted.
- Edited values in the Modbus server simulator reflected almost instantly in the Modbus client.

Observed sample event names:

- `protocol.transaction_observed`
- `protocol.frame_decoded`
- `protocol.transaction_matched`

This validates that the proxy can forward live Modbus TCP traffic unchanged while producing Modbus-aware diagnostics.

## Notes

This validation does not yet cover file logging, capture output, long-running retention behavior, or replay. Those remain future slices.
