# 2026-07-03 Live Modbus Capture Context

## Summary

Wired decoded Modbus frame context into live capture output while preserving latency-first forwarding.

Implemented behavior:

- Capture records now include a `kind` field so tools can distinguish raw forwarded `trafficChunk` records from decoded `protocolFrame` records.
- The console host passes the configured capture sink into the Modbus protocol observer when both capture and protocol decoding are enabled.
- `ModbusTcpTrafficObserver` emits protocol-frame capture records after decoding Modbus TCP ADUs.
- Protocol-frame records include correlation ID, Modbus metadata, decode or transaction warnings when present, payload length, and raw frame payload when raw payload capture is enabled.
- Raw forwarded byte capture remains in the TCP forwarding path; decoded protocol-frame capture happens through the diagnostics observer path.

## Verification

Confirmed automated behavior with:

```text
dotnet test Mitmi.slnx --no-restore
```

The suite passed with 48 tests.

## Notes

This slice intentionally keeps protocol-frame records as an overlay instead of replacing raw byte chunks. Raw chunks remain the best evidence for replay and byte-for-byte troubleshooting. Protocol-frame records make captures easier to inspect and correlate without making forwarding wait on protocol decoding.

No physical hardware test is required for this slice. A physical test becomes useful for validating timing, network-adapter behavior, and operator workflow once this capture shape is used in a real commissioning-style run.
