# 2026-07-03 Modbus Protocol Analyzer Slice

## Summary

Added the first Modbus protocol-analyzer slice based on physical validation feedback from the Teletek fire alarm panel test.

Implemented behavior:

- Interprets common Modbus function PDUs for address and value discovery:
  - `1` read coils.
  - `2` read discrete inputs.
  - `3` read holding registers.
  - `4` read input registers.
  - `5` write single coil.
  - `6` write single register.
  - `15` write multiple coils.
  - `16` write multiple registers.
- Adds zero-based PDU address, quantity, address range, operation name, byte count, and hex values to Modbus transaction logs where available.
- Adds the same interpreted fields to protocol-frame capture metadata.
- Emits `protocol.analyzer_summary` events at session shutdown with observed unit, function, operation, zero-based address range, read/write counts, request/response counts, and exception counts.
- Keeps analyzer behavior inside `Mitmi.Protocols.Modbus`; the application layer still only owns generic observer lifecycle and event delivery.

## Verification

Confirmed automated behavior with:

```text
dotnet test Mitmi.slnx --no-restore
```

The suite passed with 51 tests.

## Notes

The analyzer intentionally reports Modbus protocol data unit addresses as zero-based offsets. Many device manuals use one-based or `3xxxx`/`4xxxx` reference notation, so a later UI/export layer should make address notation configurable instead of silently translating in the capture model.

This is not yet a device-profile system. It provides protocol evidence that can help an engineer discover undocumented device behavior without assigning domain meaning to registers.
