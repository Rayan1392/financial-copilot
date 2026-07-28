# User Story — Data Ingestion and Normalization

## Story

As a scanner user,  
I need the system to have normalized and fresh financial data,  
so that scanner results are fast, reproducible, and explainable.

## Acceptance Criteria

- Worker consumes symbol sync requests.
- Worker consumes financial statement sync requests.
- Worker consumes monthly production/sales sync requests.
- Raw provider payload is saved before normalization.
- Normalized tables are updated idempotently.
- Sync runs and errors are persisted.
- Derived metric recalculation is triggered after successful normalization.
- Admin can see sync run status.

## Technical Notes

- Use RabbitMQ messages for sync requests.
- `012-admin-data-operations` exposes admin-authorized commands/status endpoints; this story owns ingestion processing and persistence.
- Use unique external provider keys for idempotency.
- Store checksum/hash to detect duplicate payloads.
