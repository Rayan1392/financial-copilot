# User Story — Admin Data Operations

## Story

As an admin,  
I want to trigger and inspect data sync jobs,  
so that I can manage provider data freshness during MVP and pilot.

## Acceptance Criteria

- Admin can trigger symbol sync.
- Admin can trigger financial statement sync.
- Admin can trigger monthly report sync.
- Admin can view sync run history.
- Admin can view provider health.
- Admin endpoints require admin authorization.
- Job status includes started time, completed time, status, error count, and processed records.

## Technical Notes

- This can initially be API-only; no admin UI required in Phase 1.
- This story owns ingestion/provider operational endpoints only. Billing administration, credit lines, invoices, and adjustments are owned by `013-billing-and-credits-domain`.
- Sync trigger endpoints enqueue RabbitMQ requests processed and persisted by `005-data-ingestion-and-normalization`; they do not duplicate ingestion logic.
- Provider health endpoint exposes capability implemented behind the provider abstraction in `004-third-party-data-provider-abstraction`.
