# Tasks

- Add EF Core entities/mappings.
- Add migrations.
- Implement sync run tracking.
- Implement RabbitMQ publisher/consumer.
- Implement raw payload save.
- Implement normalizers.
- Add integration tests with mock provider payloads.
- Add integration tests for idempotent consumption of sync requests issued by admin operations or schedules.

## Implementation Status - 2026-05-27

Implemented in this story:

- Registered the ingestion processor, run reader, recalculation publisher, and RabbitMQ request contracts in Infrastructure composition.
- Persisted normalized companies/symbols, financial statements and line items, monthly reports and line items, synchronization runs, and derived-metric recalculation requests.
- Added PostgreSQL migrations for normalized ingestion storage and the raw provider payload table required before normalization.
- Retained provider payload checksums and idempotency keys to prevent duplicate normalized records and duplicate recalculation requests.
- Verified symbol, statement, and monthly synchronization; raw payload persistence before failed normalization; persisted failure/status reads; and repeated-request idempotency with integration tests.

Explicitly deferred to dependent stories:

- `006-derived-metrics-engine` consumes stored recalculation requests and persists deterministic calculated observations.
- `012-admin-data-operations` publishes administrator-triggered synchronization requests and exposes protected synchronization-status endpoints through `IDataSyncRunReader`.
