# User Story — Data Ingestion and Normalization

## Story

As a scanner user,  
I need the system to have normalized and fresh financial data,  
so that scanner results are fast, reproducible, and explainable.

## Acceptance Criteria

- Worker can trigger symbol sync.
- Worker can trigger financial statement sync.
- Worker can trigger monthly production/sales sync.
- Raw provider payload is saved before normalization.
- Normalized tables are updated idempotently.
- Sync runs and errors are persisted.
- Derived metric recalculation is triggered after successful normalization.
- Admin can see sync run status.

## Technical Notes

- Use RabbitMQ messages for sync requests.
- Use unique external provider keys for idempotency.
- Store checksum/hash to detect duplicate payloads.
