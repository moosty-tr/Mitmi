# ADR-0001: Modbus Library Selection

## Status

Accepted for v0.1 planning.

## Context

MITMI v0.1 will be Modbus-aware from day one while remaining a protocol-independent diagnostic proxy platform. The first protocol implementation is Modbus TCP.

The Modbus dependency must be commercially usable, compatible with .NET 10 projects, suitable for a long-lived product, and isolated from MITMI's core architecture.

## Decision

Use NModbus as the initial Modbus dependency.

Package:

- NuGet: `NModbus`
- Version: `3.0.83`
- GitHub: `https://github.com/NModbus/NModbus`
- License: MIT

The dependency is referenced from `Mitmi.Protocols.Modbus`, not from the core. This keeps protocol-specific behavior outside the platform core.

## Rationale

NModbus is a mature C# Modbus implementation with support for Modbus TCP/UDP and serial ASCII/RTU scenarios. That makes it more aligned with MITMI's likely roadmap than a TCP-only helper.

NModbus should be treated as a protocol implementation aid, not as MITMI's traffic model. MITMI still needs its own protocol-neutral envelope, capture records, pipeline context, and diagnostics model.

## Trade-Offs

Advantages:

- Commercially usable MIT license.
- Mature and widely used compared with many smaller Modbus packages.
- Supports future serial Modbus scenarios.
- Reduces the need to hand-roll every Modbus protocol detail.

Risks:

- It is primarily a Modbus client/server library, not a transparent diagnostic proxy framework.
- Some proxy-specific needs may still require MITMI-owned parsing of MBAP headers, transaction IDs, direction, timing, and raw payload preservation.
- If NModbus APIs leak into the core, future protocol independence will be weakened.

## Guardrails

- Do not reference NModbus from MITMI core projects.
- Preserve raw Modbus TCP frames in captures where possible.
- Use NModbus behind adapter interfaces owned by the Modbus plugin.
- Keep Modbus semantic metadata optional and plugin-provided.
- Revisit the choice before v1.0 if replay, serial support, or advanced diagnostics expose library limitations.

## Alternatives Considered

### FluentModbus

FluentModbus is also MIT-licensed and attractive, especially for modern C# usage. It remains a viable alternative if NModbus proves awkward for MITMI's diagnostic proxy needs.

### Hand-Rolled Modbus Parser

A small hand-rolled Modbus TCP frame decoder would give MITMI maximum control over raw frame preservation and diagnostics. The downside is higher maintenance burden and greater risk of subtle protocol mistakes.

The likely long-term answer may be hybrid: MITMI-owned lightweight frame decoding for proxy/capture purposes, with NModbus used for protocol semantics, validation, test endpoints, and later replay support.
