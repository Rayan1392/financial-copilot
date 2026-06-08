# Noavaran Current API Ingestion

## User Story

As a data administrator, I want Noavaran Amin API services to provide the current financial dataset from 1403 onward so TahlilApp-AI can keep normalized financial data fresh after the archival source stops updating.

## Acceptance Criteria

1. Current API ingestion is modeled separately from the archive import.
2. Current API ingestion supports scheduled incremental updates.
3. Dataset coverage starts from the configured 1403 boundary unless overridden by an admin operation.
4. The current API can fill gaps not available in the archive.
5. Current API rows preserve provenance as `NoavaranAmin / NoavaranCurrentApi`.
6. Company/security identity resolution reuses the canonical mapping created during archive import.
7. Current API ingestion supports company catalog updates, financial statements, fundamental indexes, and monthly activity as applicable.
8. Cache invalidation and derived-metric recalculation run after successful normalized changes.
9. DataAdmin exposes current API health separately from archive import state.
10. Failures in current API ingestion do not change archive freeze/import state.

## Out of Scope

- Re-importing archive data.
- Replacing StockMarketDB trading statistics ingestion.
