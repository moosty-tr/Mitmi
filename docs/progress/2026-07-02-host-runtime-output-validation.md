# 2026-07-02 Host Runtime Output Validation

## Summary

Added an end-to-end console host integration test for runtime diagnostic outputs.

Confirmed automated behavior:

- `CommandLineHost` can run from a JSON configuration file and proxy a real NModbus TCP client/server exchange.
- File logging writes Modbus transaction diagnostics and session metrics during the run.
- Capture output writes NDJSON records for both client-to-server and server-to-client traffic.
- Capture records include version, protocol ID, payload length, direction, and base64 raw payload when configured.
- Relative log and capture paths resolve under the configuration file directory in the runtime host path.

## Notes

This covers the host composition path for local file log and capture output without requiring physical hardware. Physical validation is still useful later for real device timing, network-adapter behavior, and operator-facing field workflow.
