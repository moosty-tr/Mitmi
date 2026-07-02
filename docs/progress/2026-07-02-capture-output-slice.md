# 2026-07-02 Capture Output Slice

## Summary

Implemented the first experimental capture output slice.

Confirmed automated behavior:

- Forwarded TCP bytes are emitted to a configured capture sink after successful pass-through writes.
- The console host creates a bounded asynchronous NDJSON capture sink when capture is enabled.
- Startup output reports the concrete capture file path.
- v1 capture records include UTC timestamp, session ID, connection ID, protocol ID, direction, payload length, and optional base64 raw payload.
- Raw payload is omitted when the record does not include one.

## Notes

Capture records currently represent forwarded byte chunks, not fully decoded Modbus transactions. Replay, protocol metadata enrichment, shutdown loss summaries, and long-running retention behavior remain future slices.
