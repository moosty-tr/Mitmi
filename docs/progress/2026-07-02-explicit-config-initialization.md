# 2026-07-02 Explicit Configuration Initialization

## Summary

Made configuration template creation an explicit operator action.

Implemented behavior:

- `mitmi --init-config [--config <path>]` creates the default configuration template and exits.
- Normal startup and `--validate-config` now fail when the resolved configuration file is missing.
- Existing configuration files are not overwritten by `--init-config`.
- CLI usage now shows the separate initialization command.

## Rationale

For an in-path industrial diagnostic proxy, silently creating a default configuration during normal startup is too forgiving. A mistyped `--config` path should not produce a runnable endpoint with default listen/upstream settings.

This keeps first-run setup convenient while preserving fail-fast behavior for field runs.
