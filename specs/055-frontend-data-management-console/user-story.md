# Frontend Data Management Console

## User Story

As a data administrator, I want a frontend Data Management Console so I can operate archive imports, current API syncs, StockMarketDB bridge syncs, future TSETMC direct ingestion, provider health, run history, and reconciliation without using backend-only endpoints.

## Acceptance Criteria

1. The console is separate from user/role/subscription administration.
2. It shows logical vendors and physical sources:
   - Noavaran Amin / Archive source
   - Noavaran Amin / Current API
   - CyclicalWaves API
   - StockMarketDB bridge
   - TSETMC direct feed when implemented
3. Archive source UI supports dry-run/import/validate/freeze/re-import request, not recurring scheduled sync.
4. Current API UI supports scheduled sync status, manual trigger, run history, failures, and watermarks.
5. StockMarketDB UI shows bridge sync status and marks it as a transitional datasource.
6. TSETMC UI supports shadow-mode validation and future cutover status.
7. Reconciliation screens show source coverage, conflicts, stale data, and missing periods.
8. Actions are protected by admin permissions and audited.
9. The UI never exposes secrets.
10. The UI uses existing authenticated API bridge and frontend design system.

## Out of Scope

- Replacing the existing identity/admin panel.
- Implementing backend ingestion logic inside the frontend.
