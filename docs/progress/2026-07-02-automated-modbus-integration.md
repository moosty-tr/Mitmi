# 2026-07-02 Automated Modbus Integration

## Summary

Added an automated Modbus TCP client/server integration test for the v0.1 diagnostic proxy path.

Confirmed behavior:

- An NModbus TCP client can read holding registers through MITMI.
- MITMI forwards the request to an NModbus TCP server unchanged.
- The client receives the expected register values through the proxy.
- Modbus frame decoded, transaction observed, and transaction matched events are emitted during the real client/server exchange.

## Notes

NModbus is used directly only in integration test code for local endpoint simulation. Production Modbus dependency ownership remains inside `Mitmi.Protocols.Modbus`.
