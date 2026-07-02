# 2026-07-02 Bounded Session Events

## Summary

Added a bounded asynchronous session event sink for diagnostic events.

Confirmed automated behavior:

- Queued session events flush to the inner sink during disposal.
- When the event queue is full, the newest event is dropped instead of blocking the caller.
- Event loss is reported with a warning and a shutdown loss summary.
- The console host now composes the bounded event sink around the existing console/file event sink.

## Notes

This reduces direct coupling between forwarding diagnostics and console/file writes. It does not make protocol decoding itself asynchronous yet; it makes event emission bounded and asynchronous once diagnostics produce events.
