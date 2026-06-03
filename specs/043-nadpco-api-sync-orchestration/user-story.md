# NADPCO API Synchronization Orchestration

## User Story

As a data administrator, I want bounded full and incremental NADPCO API synchronization
operations so remote data can be refreshed safely, observed operationally, and reconciled with
existing PostgreSQL projections.

## Context

The vendor endpoints expose different filters: company catalog, Jalali year ranges, period
types, company IDs, index IDs, and monthly date bounds. The platform must use bounded requests,
persist progress, isolate failures, and avoid a single unrestricted vendor call.

## Acceptance Criteria

1. Add full and incremental orchestration for company catalog, statements, fundamental
   indexes, product sales, and service sales.
2. Persist per-dataset watermarks or cursors appropriate to each endpoint. Document endpoints
   that lack a reliable modified-since cursor and use overlap-window reconciliation for them.
3. Partition work into bounded company/date/item batches with configurable concurrency.
4. Store raw payloads before normalization and advance progress only after successful
   persistence.
5. Isolate per-batch failures, retain retry diagnostics, and expose provider health plus sync
   status through protected DataAdmin operations.
6. Invalidate scanner caches after successful normalized changes.
7. Keep orchestration provider-specific while normalization, recalculation, and scanner reads
   remain provider-neutral.
8. Never send unbounded history requests or leak vendor credentials in logs.

## Out Of Scope

- Removing CodalDB synchronization.
- Query-time synchronization.
- Assuming a modified-since cursor where the vendor contract does not provide one.

