# Noavaran Amin Archive and Current API Source Strategy

## User Story

As a TahlilApp-AI product owner and data operator, I want Noavaran Amin data sources to be modeled as one logical vendor with separate archive and current-feed responsibilities so historical data and post-1403 updates are ingested through the correct source without duplicate or conflicting provider behavior.

## Business Context

The current specs treat `CodalDB` and `NadpcoApi` as separate active vendors. This is not the correct product model.

The intended model is:

- Noavaran Amin is the logical vendor.
- The existing NADPCO/CodalDB-style source must be treated as an archival source.
- The archive source is not expected to keep receiving updates.
- The archive source should be synchronized once, audited, and then considered frozen unless an explicit maintenance re-import is requested.
- Data from 1403 onward, when missing from the archive, must come from Noavaran Amin API services.
- CyclicalWaves remains a separate independent vendor.
- StockMarketDB remains a separate market-trading datasource and is not part of Noavaran Amin fundamentals.

## Acceptance Criteria

1. The provider model distinguishes logical vendor from physical source/transport.
2. `NoavaranAmin` is represented as one logical vendor.
3. Archive imports and current API imports are represented as separate source modes under the same logical vendor.
4. Archive source sync is not scheduled automatically as a normal incremental worker.
5. Archive source has explicit one-time import/run-state semantics.
6. Noavaran current API services own data coverage from 1403 onward where the archive lacks data.
7. Source provenance is persisted at row level or batch level, including logical vendor, physical source, import mode, source date range, and ingestion run id.
8. Provider priority rules are explicit per dataset, period, and metric type.
9. Duplicate canonical company/security identities are prevented when the same issuer appears in archive and current API feeds.
10. Existing NADPCO scheduled sync specs are revised or superseded so they do not imply recurring archive refresh.

## Out of Scope

- Implementing the full frontend admin console.
- Implementing direct TSETMC ingestion.
- Removing existing normalized financial tables.
