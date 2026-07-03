# 2026-07-03 Physical Validation Runbook

## Summary

Added a physical validation runbook for the v0.1 Modbus TCP diagnostic proxy.

Implemented documentation:

- Defines the supported physical validation topology: explicit client retargeting through MITMI.
- Keeps the first physical run read-only and bench-first.
- Lists required configuration checks for capture, file logging, protocol decoding, and raw payload capture.
- Defines pass/fail criteria for forwarding, logs, captures, protocol-frame records, and shutdown behavior.
- Lists the evidence to preserve from a physical validation run.

## Notes

This is a validation artifact, not a runtime behavior change. It should be used before claiming field readiness because real device timing, network-adapter behavior, firewall behavior, and operator workflow cannot be fully proven by local automated tests.
