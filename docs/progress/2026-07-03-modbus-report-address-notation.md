# 2026-07-03 Modbus Report Address Notation

## Summary

Added configurable address columns for the Modbus device-discovery Markdown report.

Implemented behavior:

- Added strict `session.protocolOptions["modbus-tcp"].reportAddressColumns` parsing.
- Reports always require and display `zeroBasedPdu` so the Modbus PDU offset remains the source of truth.
- Reports can also display `oneBased` ranges for manuals that count from 1.
- Reports can also display function-code-based `reference` ranges using common 0xxxx, 1xxxx, 3xxxx, and 4xxxx Modbus conventions.
- The checked-in default configuration now enables `zeroBasedPdu`, `oneBased`, and `reference` report columns explicitly.
- The machine-readable analyzer summary and capture metadata remain zero-based and unchanged.
- Updated the physical validation runbook to check configured report columns while preserving zero-based PDU ranges.

## Verification

Automated validation:

```text
dotnet test Mitmi.slnx --no-restore
```

Result: 56 tests passed.

Release smoke validation:

```text
.\scripts\Invoke-ReleaseSmokeTest.ps1
```

Result: passed.

## Physical-Test Decision

No physical retest is required for this slice because it does not change forwarding, Modbus frame decoding, transaction correlation, capture/log output semantics, startup workflow, shutdown behavior, or webhook delivery behavior. The change is a report-format projection of already captured analyzer summary data.
