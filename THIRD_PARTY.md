# Third-Party Dependencies

This file tracks third-party dependencies that are intentionally introduced into MITMI.

## NModbus

- Purpose: Initial Modbus protocol support for the Modbus plugin.
- Package: `NModbus`
- Version: `3.0.83`
- Source: https://github.com/NModbus/NModbus
- License: MIT
- Used by: `src/Mitmi.Protocols.Modbus`

Architecture note: NModbus must remain isolated to protocol-specific projects. MITMI core should not reference this package directly.
