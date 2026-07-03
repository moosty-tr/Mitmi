# 2026-07-03 Modbus Device Discovery Report

## Summary

Added a human-readable Modbus device-discovery report generated from the analyzer summary records.

Implemented behavior:

- Emits `captures/reports/mitmi-modbus-device-discovery-*.md` at session shutdown when capture and Modbus protocol decoding are enabled.
- Keeps the existing structured analyzer summary NDJSON artifact under `captures/summaries`.
- Reports session, protocol, upstream device endpoint, address base, observed range count, request/response totals, exception totals, and a table of unit/function/operation/address ranges.
- Includes the report in diagnostics bundles through existing recursive capture artifact collection.
- Updates field validation evidence requirements to preserve the discovery report.

## Product Direction

Replay and fault simulation are intentionally skipped for the current product direction.

Bridging remains possible but late.

Webhooks are a candidate future integration feature for observed Modbus value changes, but should build on the analyzer state and must not block forwarding.

## Verification

Automated verification:

```text
dotnet test Mitmi.slnx --no-restore
```

Physical retest is not required for this slice because forwarding, decoding, and correlation behavior are unchanged. The new report is a shutdown-time projection of already collected analyzer summary data.
