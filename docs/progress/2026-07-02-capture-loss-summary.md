# 2026-07-02 Capture Loss Summary

## Summary

Added shutdown loss reporting for the capture queue.

Confirmed automated behavior:

- Capture still drops the newest record when its bounded queue is full.
- The first capture drop still emits a visible warning.
- Disposal now emits a capture loss summary with the number of dropped records.
- The summary is emitted with the affected session ID and no per-connection ID because it describes queue-level loss.

## Notes

This closes the capture loss visibility gap called out by the earlier capture slice. It does not add retention cleanup or protocol metadata enrichment to capture records.
