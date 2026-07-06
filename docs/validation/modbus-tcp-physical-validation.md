# Modbus TCP Physical Validation Runbook

## Purpose

Validate MITMI v0.1 as an explicit in-path Modbus TCP diagnostic proxy against real or hardware-adjacent equipment.

This runbook is for field-readiness evidence, not for proving basic forwarding logic. Automated integration tests already cover local client/server forwarding, Modbus diagnostics, file logging, metrics, and capture output.

## When To Run

Run this before calling a build field-ready for engineer-laptop deployment.

Run it again after changes that affect:

- TCP forwarding behavior.
- Modbus frame decoding or transaction correlation.
- Capture or log output.
- Startup diagnostics and operator workflow.
- Configuration path resolution.
- Shutdown behavior.
- Observed-value webhook delivery behavior.

## Safety Boundary

Start on a bench or non-production network.

Use read-only Modbus functions first, such as holding-register reads. Do not test writes against live equipment until the read-only path is understood, the device owner approves the test, and rollback expectations are clear.

MITMI v0.1 must not modify traffic. If a test requires write traffic, the write must come from the existing client or test client; MITMI should still forward unchanged.

## Test Topology

```text
Modbus client -> MITMI listen endpoint -> Modbus TCP device or gateway
```

The client must be explicitly retargeted to MITMI's listen endpoint. Do not use transparent interception, packet capture, ARP spoofing, DNS spoofing, or NAT for v0.1 validation.

## Prerequisites

- A Modbus TCP device, gateway, PLC simulator connected through real network hardware, or an approved test rig.
- A Modbus TCP client that can be retargeted to MITMI.
- Known read-only register address, unit ID, function code, and expected value range.
- Permission from the equipment owner.
- MITMI configuration file with capture enabled and file logging enabled.
- Known local folder for logs and captures.

## Recommended Configuration Checks

Before starting MITMI:

- `session.listenEndpoint` points to the engineer laptop interface and chosen port.
- `session.upstreamEndpoint` points to the original Modbus TCP device or gateway.
- `session.protocol` is `modbus-tcp`.
- `session.diagnostics.decodeProtocol` is `true`.
- `session.diagnostics.captureRawPayloads` is `true` for validation runs.
- `session.protocolOptions["modbus-tcp"].reportAddressColumns` includes `zeroBasedPdu`; add `oneBased` or `reference` when those columns help compare with device manuals.
- `capture.enabled` is `true`.
- `logging.file.enabled` is `true`.
- If validating webhooks, `integrations.observedValueWebhook.enabled` is `true`, the URL points to an approved test receiver, and filters use zero-based PDU ranges.

Run configuration validation before connecting the real client:

```text
mitmi --config path\to\mitmi.config.json --validate-config
```

## Execution Steps

1. Confirm the client can communicate directly with the upstream device without MITMI.
2. Stop direct client communication.
3. Start MITMI with the validation configuration.
4. Confirm startup output shows the expected listen endpoint, upstream endpoint, log path, capture path, and capture retention warning.
5. Retarget the client to MITMI's listen endpoint.
6. Run a read-only request repeatedly for at least 2 minutes.
7. Change a safe simulator/device value if available and confirm the client sees the update through MITMI.
8. Stop the client.
9. Stop MITMI with normal console cancellation.
10. Export a diagnostics bundle if this is a support or release-readiness run.
11. Preserve the MITMI configuration, file log, capture files, analyzer summary, discovery report, diagnostics bundle if exported, and notes about network topology.

## Pass Criteria

- MITMI starts without validation errors.
- The client connects to MITMI without requiring elevated packet-capture privileges.
- Read-only requests succeed through MITMI.
- Values returned through MITMI match direct-client expectations.
- File log contains connection lifecycle events.
- File log contains `protocol.frame_decoded` and `protocol.transaction_matched` events.
- File log contains `protocol.analyzer_summary` events with unit ID, function, address range, and read/write counts.
- Capture file contains `trafficChunk` records in both directions.
- Capture file contains `protocolFrame` records with Modbus metadata and correlation IDs.
- Analyzer summary exists under `captures/summaries` and contains observed Modbus functions, zero-based address ranges, request/response counts, and read/write counts.
- Discovery report exists under `captures/reports` and summarizes observed Modbus functions and configured address columns in a human-readable table. The zero-based PDU address range must remain present.
- Shutdown emits normal listener/session stop events.
- No capture, diagnostics, or session event loss warnings appear during the short run.
- If validating webhooks, the receiver gets the expected changed-value payloads and the MITMI log contains `integration.observed_value_webhook.summary` with no failed or dropped deliveries.

## Fail Criteria

- The client cannot connect to MITMI when direct connection works.
- MITMI connects to the wrong upstream endpoint.
- Forwarded values differ from direct-client values.
- Frequent decode warnings appear for known-good traffic.
- Capture output is missing, empty, or missing one direction.
- MITMI shutdown leaves files unwritten or incomplete.
- Analyzer summary is missing after a run that produced decoded Modbus transactions.
- Discovery report is missing after a run that produced decoded Modbus transactions.
- Operator cannot identify the active log and capture paths from startup output.
- Enabled webhook delivery reports repeated failures or drops during the validation run.

## Evidence To Keep

- MITMI version or commit SHA.
- Configuration file used for the run.
- Client tool and version.
- Device or gateway model and firmware, if shareable.
- Network topology notes.
- Start and stop timestamps.
- File log.
- Capture NDJSON file.
- Modbus analyzer summary NDJSON file.
- Modbus device discovery Markdown report.
- Diagnostics bundle zip, if exported.
- Webhook receiver logs, if webhook behavior is part of the validation.
- Any firewall or interface binding changes made for the test.

## Physical-Test Decision

A physical test is not required for every code slice. It is required before treating MITMI v0.1 as field-ready because software-only tests do not cover real network adapters, firewalls, switch behavior, device timing, client retargeting friction, or operator artifact collection.
