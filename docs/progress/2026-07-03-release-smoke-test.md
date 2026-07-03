# 2026-07-03 Release Smoke Test

## Summary

Added a repeatable release smoke test for the published MITMI console host.

Implemented artifacts:

- Added `scripts/Invoke-ReleaseSmokeTest.ps1`.
- The script publishes `src/Mitmi.Host.Console` in Release mode.
- The script runs the published executable from a clean temporary folder with `--help`, `--init-config`, `--validate-config`, and `--bundle-diagnostics`.
- The script verifies that the generated configuration file and diagnostics bundle exist.
- The script inspects the diagnostics bundle for configuration, file log, capture, analyzer summary, discovery report, and manifest entries.
- Updated `README.md` with the release smoke-test command.

## Physical-Test Decision

No physical retest is required for this slice because it does not change forwarding, Modbus decoding, capture/log output semantics, startup workflow, shutdown behavior, or webhook delivery behavior. It adds packaging validation around already implemented commands.
