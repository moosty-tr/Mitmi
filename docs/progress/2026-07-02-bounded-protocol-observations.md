# 2026-07-02 Bounded Protocol Observations

## Summary

Added a bounded asynchronous protocol traffic observer for diagnostics.

Confirmed automated behavior:

- Queued protocol traffic observations flush to the inner observer during disposal.
- When the protocol diagnostics queue is full, the newest observation is dropped instead of blocking the forwarding path.
- Observation loss is reported with a warning and a shutdown loss summary.
- Inner observer failures are reported as session warning events instead of surfacing back into the forwarding path.
- The console host now wraps Modbus traffic observation with the bounded observer factory.

## Notes

This moves Modbus frame decoding and transaction correlation off the immediate post-forwarding path in the console host. Forwarding still copies payload bytes for diagnostics, but semantic protocol work now happens behind a bounded queue.
