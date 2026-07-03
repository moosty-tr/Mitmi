# 2026-07-03 Structured Modbus Analyzer Summary

## Summary

Added a machine-readable Modbus analyzer summary artifact for field analysis and support bundles.

Implemented behavior:

- Emits `captures/summaries/mitmi-modbus-analyzer-summary-*.ndjson` at session shutdown when capture and Modbus protocol decoding are enabled.
- Writes one NDJSON record per observed Modbus unit, function, operation, and zero-based PDU address range.
- Includes summary format version, timestamp, session id, unit id, function code, operation, address, quantity, address range, address base, read/write counts, request/response counts, and exception counts.
- Keeps the existing `protocol.analyzer_summary` log event as the human-readable operator view.
- Lets diagnostics bundles include the summary artifact through the existing recursive capture artifact collection.

## Verification

Crash recovery first confirmed the interrupted work was broken:

```text
dotnet test Mitmi.slnx --no-restore
```

The crash-state run failed at compile time because `ModbusTcpTrafficObserverFactory` still called the old analyzer summary API.

After completing the sink and wiring, verification passed with:

```text
dotnet test tests/Mitmi.IntegrationTests/Mitmi.IntegrationTests.csproj --no-restore --filter "FullyQualifiedName~CommandLineHostRuntimeIntegrationTests.RunAsync_writes_file_log_and_capture_records_for_modbus_exchange" --logger "console;verbosity=detailed"
dotnet test Mitmi.slnx --no-restore
```

The full suite passed with 54 tests.

## Notes

No physical retest is required for this slice. It does not change forwarding, Modbus decoding, correlation, or interpreted per-frame analyzer fields already validated with the Teletek panel; it adds a shutdown-time file projection of the same session summary data.
