# 2026-07-02 Embedded Default Config Template

## Summary

Made the checked-in example configuration the source of truth for `mitmi --init-config`.

Implemented behavior:

- `examples/mitmi.config.example.json` is embedded into `Mitmi.Host.Console`.
- `--init-config` copies the embedded example instead of writing a duplicated C# raw string.
- The existing drift test now verifies the embedded resource path through the public host command.

## Rationale

Duplicating the default configuration in code and in an example file creates a quiet maintenance hazard. The drift test could catch mismatches, but it is better for the implementation to have only one source of truth.

This keeps the operator-facing setup path simple while reducing long-term configuration drift risk.
