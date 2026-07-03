# MITMI

MITMI is an industrial communication mediation tool. The current v0.1 path is a Modbus TCP diagnostic proxy: a client connects to MITMI, MITMI connects to the configured upstream device, and traffic is forwarded unchanged while logs, captures, metrics, and Modbus analyzer artifacts are written for diagnosis.

v0.1 is an explicit in-path proxy, not a passive packet sniffer. Clients must be retargeted to MITMI's listen endpoint.

## Current Scope

Implemented v0.1 foundation:

- Single diagnostic session.
- Modbus TCP pass-through proxy.
- Explicit client retargeting.
- JSON configuration with startup validation.
- Console and file logs with independent minimum levels.
- Append-only NDJSON traffic capture.
- Modbus TCP frame decoding and transaction correlation.
- Interpreted Modbus address, quantity, operation, and value metadata for common functions.
- Session-level Modbus analyzer summary logs and NDJSON summary artifacts.
- Basic session and connection metrics emitted through logs.
- Diagnostics bundle export for field-support handoff.

Out of scope for the current path:

- Transparent interception, NAT, ARP spoofing, DNS spoofing, and passive packet capture.
- Replay, traffic modification, fault simulation, caching, queuing, bridging, scripting, and dashboards.
- Multiple concurrent configured sessions.

## Requirements

- .NET SDK 10.
- A Modbus TCP client that can be configured to connect to MITMI instead of the original device endpoint.
- A Modbus TCP server, gateway, PLC, simulator, or approved test device.

## Build And Test

```text
dotnet restore Mitmi.slnx
dotnet test Mitmi.slnx --no-restore
```

Run the release smoke test before handing a build to an operator:

```text
.\scripts\Invoke-ReleaseSmokeTest.ps1
```

The smoke test publishes the console host to a clean temporary folder and runs the published executable through `--help`, `--init-config`, `--validate-config`, and `--bundle-diagnostics`.

## Quick Start

Create a configuration file:

```text
dotnet run --project src/Mitmi.Host.Console -- --init-config --config .\mitmi.config.json
```

Edit the generated file:

- `session.listenEndpoint` is where the client connects to MITMI.
- `session.upstreamEndpoint` is the original Modbus TCP device or server.
- `logging.file.path` is the file log path.
- `capture.outputPath` is where capture and analyzer summary artifacts are written.

Validate configuration before starting the proxy:

```text
dotnet run --project src/Mitmi.Host.Console -- --config .\mitmi.config.json --validate-config
```

Start MITMI:

```text
dotnet run --project src/Mitmi.Host.Console -- --config .\mitmi.config.json
```

Stop MITMI with normal console cancellation. On Windows this is usually `Ctrl+C`.

Without `--config`, MITMI looks for `mitmi.config.json` beside the application executable. During `dotnet run`, that means the build output directory, not the repository root.

## Runtime Artifacts

Default paths are relative to the configuration file directory.

- `logs/mitmi.log`: human-readable session, connection, protocol, analyzer, warning, and metrics events.
- `captures/mitmi-capture-*.ndjson`: versioned traffic capture records with raw payloads when enabled, plus protocol-frame metadata when decoding is enabled.
- `captures/summaries/mitmi-modbus-analyzer-summary-*.ndjson`: machine-readable Modbus function, unit, address range, and read/write count summary records.
- `captures/reports/mitmi-modbus-device-discovery-*.md`: human-readable Modbus device-discovery report for field review.

Useful event names to check in logs:

- `session.listener_started`
- `session.client_accepted`
- `protocol.frame_decoded`
- `protocol.transaction_matched`
- `protocol.analyzer_summary`
- `metrics.session_summary`

Export a support bundle after a run:

```text
dotnet run --project src/Mitmi.Host.Console -- --config .\mitmi.config.json --bundle-diagnostics .\support\mitmi-diagnostics.zip
```

The bundle includes the configuration file, file log, capture directory contents, analyzer summaries, and a manifest.

## Field Validation

Use the physical validation runbook before treating a build as field-ready:

- [Modbus TCP Physical Validation Runbook](docs/validation/modbus-tcp-physical-validation.md)

The current physical evidence is recorded here:

- [2026-07-03 Teletek Fire Alarm Panel Validation](docs/validation/results/2026-07-03-teletek-fire-alarm-panel.md)

## Architecture Notes

The accepted v0.1 architecture and implementation sequence are tracked in:

- [MITMI Vision](VISION.md)
- [v0.1 Diagnostic Proxy Architecture](docs/architecture/v0.1-diagnostic-proxy-architecture.md)
- [v0.1 Implementation Plan](docs/planning/v0.1-implementation-plan.md)
- [Architecture decision records](docs/decisions)

The Modbus protocol project owns Modbus-specific parsing and analyzer behavior. The application layer owns protocol-neutral session orchestration, configuration validation, capture contracts, metrics contracts, and bounded diagnostics policy.
