# ADR-0005: Capture Enabled By Default With Output Path And Retention Warnings

## Status

Accepted for planning.

## Context

MITMI v0.1 is a diagnostic proxy. Captures are central to diagnosis, replay foundations, support workflows, and later automation.

If captures are disabled by default, users may discover too late that the most important evidence was not preserved. If captures are enabled by default, MITMI must be clear about where data is written and how long it may remain there.

Industrial traffic captures may contain sensitive operational details, process values, device behavior, addresses, and timing patterns.

## Decision

MITMI v0.1 will enable captures by default.

MITMI must clearly report the active capture output path at startup.

MITMI must emit a retention and sensitivity warning at startup when capture is enabled.

Capture settings must remain configurable so a user can disable captures or change the output path explicitly.

## Rationale

Capture-on-by-default matches the diagnostic-proxy purpose. A user running MITMI usually expects to be able to inspect what happened after the fact.

Clear output paths reduce field confusion and support friction.

Retention warnings make the data responsibility visible before long-running captures accumulate.

## Guardrails

- Captures and logs are separate outputs.
- Raw traffic payloads belong in captures, not normal `Info` logs.
- Startup output should show whether capture is enabled.
- Startup output should show the resolved capture output path.
- Startup output should warn that captures may contain sensitive industrial data.
- Startup output should warn that v0.1 retention is limited or manual unless automatic retention is implemented.
- Capture format should be versioned from the beginning.
- Configuration should allow capture to be disabled explicitly.

## Recommended v0.1 Default

Suggested default:

- Capture enabled: `true`
- Capture output path: an explicit application data or configured relative folder such as `./captures`
- Retention: manual cleanup warning in v0.1 unless a simple retention policy is implemented

The exact path can be finalized during implementation planning. The key requirement is that the resolved path is visible to the user at startup.

## Trade-Offs

Advantages:

- Better diagnostic usefulness by default.
- Better replay foundation.
- Fewer missed evidence scenarios.
- Easier support workflows.

Risks:

- Capture files can grow quickly.
- Captures can contain sensitive industrial information.
- Users may assume retention cleanup exists when it does not.
- Default relative paths can surprise users depending on working directory.

Mitigations:

- Show resolved output path at startup.
- Emit retention and sensitivity warnings.
- Keep capture configuration explicit.
- Plan retention controls before commercial release.

## Future Considerations

Future versions may add:

- Maximum capture file size.
- File rotation.
- Time-based retention.
- Disk quota safeguards.
- Capture encryption.
- Redaction/anonymization.
- Diagnostics bundle export.
- Capture metadata indexing.
