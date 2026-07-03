# 2026-07-03 Diagnostics Concurrency And Capture Context

## Summary

Closed a Modbus diagnostics race and added protocol-aware fields to the experimental capture record contract.

Implemented behavior:

- `ModbusTcpTrafficObserver` now serializes observation processing so bidirectional forwarding cannot concurrently mutate decoder or transaction-correlation state.
- This fixes a race where a normal request/response exchange could emit `protocol.transaction_observed` for both directions without producing `protocol.transaction_matched`.
- `TrafficCaptureRecord` now has optional `correlationId`, `protocolMetadata`, and `decodeWarnings` fields.
- The NDJSON capture writer emits those protocol-aware fields only when records include them, preserving compact raw byte records for the current runtime path.

## Verification

Confirmed automated behavior with:

```text
dotnet test Mitmi.slnx --no-restore
```

The suite passed with 48 tests.

## Notes

This does not yet wire live Modbus frame metadata into capture files. That should be a separate enrichment slice so protocol decoding remains behind the bounded diagnostics path instead of moving back into the forwarding hot path.

No physical hardware test is required for this slice. Physical validation becomes useful once the software capture-enrichment path and operator workflow are ready to validate against real device timing and network-adapter behavior.
