# 2026-07-02 Default Configuration Example Drift Test

## Summary

Added a regression test that compares the configuration produced by `mitmi --init-config` with the checked-in example configuration.

Implemented behavior:

- `CommandLineHostConfigurationTests` now verifies generated default config content matches `examples/mitmi.config.example.json`.
- The comparison normalizes line endings so Windows and Unix checkouts do not produce false failures.

## Rationale

The generated default config and the example config are operator-facing contracts. If they drift, first-run behavior and documentation can quietly disagree.

This keeps the current simple template approach while making drift visible in CI before it reaches field use.
