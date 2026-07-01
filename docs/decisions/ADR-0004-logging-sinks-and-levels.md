# ADR-0004: Console And File Logging With Independent Levels

## Status

Accepted for planning.

## Context

MITMI v0.1 is a diagnostic proxy intended for field use. Users need live feedback while the proxy is running and durable evidence after the run completes.

Console-only logging is convenient during live troubleshooting but weak for post-run analysis. File-only logging is durable but poor for field visibility.

## Decision

MITMI v0.1 will log to both console and file by default.

Console and file logging must have independently configurable minimum levels.

v0.1 should support these product-level log levels:

- `Debug`
- `Info`
- `Warning`
- `Error`

Recommended defaults:

- Console minimum level: `Info`
- File minimum level: `Info`

`Debug` should be available but opt-in.

## Rationale

Console logging gives field engineers immediate visibility into startup, connection lifecycle, upstream failures, and current proxy state.

File logging gives support and engineering teams a durable record for post-run diagnosis.

Independent levels let users keep the console readable while collecting more detail in files when needed.

## Guardrails

- Do not put raw traffic payloads in normal `Info` logs.
- Keep replay-oriented raw traffic in captures, not regular logs.
- Treat `Debug` logs as potentially sensitive and high-volume.
- Include timestamps, level, session ID, connection ID where available, event name, and message.
- Make log level names stable product concepts even if the underlying logging framework has additional levels.
- Avoid making logging depend on the future web dashboard.

## Trade-Offs

Advantages:

- Better live operator experience.
- Better supportability.
- Separate console and file levels avoid all-or-nothing verbosity.
- Works well for a CLI-first product.

Risks:

- File logs can grow quickly.
- Debug logs may expose operational details or industrial data.
- Too much console output can hide important warnings.
- Logging inside hot forwarding paths can affect latency if implemented carelessly.

Mitigations:

- Default both sinks to `Info`.
- Keep capture and logging separate.
- Plan for file rotation and retention before commercial release.
- Keep structured event data concise in normal logs.

## Future Considerations

Future versions may add:

- File rotation.
- Retention policies.
- JSON log format.
- Diagnostics bundle export.
- Redaction policies.
- Additional sinks such as syslog, OpenTelemetry, or Windows Event Log.

These should be added as logging infrastructure adapters, not as changes to the core diagnostic proxy model.
