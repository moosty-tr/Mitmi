# 2026-07-03 Observed-Value Webhook Delivery Design

## Summary

Added the first delivery design for observed-value webhooks.

Documented decisions:

- Webhooks should consume observed-value update groups, not raw frames.
- The first implementation should stay disabled by default.
- The first implementation should support one observed-value webhook target instead of a broad integration framework.
- The first trigger mode should be `ChangedCellsOnly`.
- Filters should use unit ID, table, and zero-based PDU ranges.
- Delivery should use a bounded queue and must not block forwarding.
- The first delivery slice should attempt delivery once with a short timeout instead of adding retries immediately.
- Secrets should not be stored directly in `mitmi.config.json`; static header auth, if added, should read from an environment variable.

## Physical-Test Decision

No physical retest is required for this design-only slice.

The next implementation step changes webhook delivery behavior, so physical or hardware-adjacent validation should be run after that implementation before claiming the feature is field-ready.
