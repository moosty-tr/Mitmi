# 2026-07-03 Diagnostics Bundle Export

## Summary

Added a diagnostics bundle export command for field-support handoff.

Implemented behavior:

- `mitmi --bundle-diagnostics <zip-path> [--config <path>]` validates configuration and creates a diagnostics zip without starting the proxy.
- The bundle includes:
  - The configuration file.
  - The configured file log when it exists.
  - Capture files under the configured capture output path when they exist.
  - A `manifest.json` with session, protocol, source paths, bundle path, and artifact entries.
- Existing bundle files are not overwritten.
- Bundle export cannot be combined with `--init-config` or `--validate-config`.

## Verification

Confirmed automated behavior with:

```text
dotnet test Mitmi.slnx --no-restore
```

The suite passed with 54 tests.

## Notes

This is a host-level support feature. It does not change forwarding, protocol decoding, capture format, or the latency-first proxy path.

Physical hardware is not required to validate the bundling command itself. The existing Teletek panel validation confirms the runtime artifacts are useful; this slice makes those artifacts easier to collect.
