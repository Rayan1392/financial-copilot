# Noavaran Financial Statement Full-Item And Variant Persistence

## Status
`[ ]` Proposed

## Story

As a TahlilApp-AI product owner and data operator,

I want Noavaran current-API financial statements to persist all returned statement items and to
store standalone and consolidated variants as separate normalized statements,

so that query features can retrieve the exact requested variant and future metric coverage is not
blocked by curated vendor-request allowlists.

## Business Context

Current financial-statement ingestion was built around a governed subset of item IDs:

- request bodies send curated `items` arrays to:
  - `api/v2/FS/IncomeStatement/Values`
  - `api/v2/FS/BalanceSheet/Values`
  - `api/v2/FS/CashFlow/Values`
- the normalizer persists only item IDs that exist in hardcoded reviewed maps
- `NadpcoApiStatementSelectionPolicy.SelectAll(...)` collapses same-period variants before
  persistence, preferring one audited/represented/composing winner per `(StatementType, ComID,
  PeriodType, PeriodEnd)`

That behavior creates two product gaps:

1. If the vendor later exposes a needed line item, the data is still not stored unless code-first
   mappings are added and the request allowlist is widened.
2. If both standalone (`IsComposing = false`) and consolidated (`IsComposing = true`) statements
   exist for the same period, only one survives in normalized storage, so query features cannot
   deterministically answer `اصلی` versus `تلفیقی`.

## Required Product Rules

1. The current-API financial-statement endpoints must be allowed to request all vendor statement
   items by sending `items: []`.
2. Persisting all returned items does not mean all items become governed scanner/query metrics.
   The system must separate:
   - full vendor observation persistence
   - a shared persisted source-item catalog
   - reviewed/governed metric promotion used by deterministic calculations and AI answers
3. Standalone and consolidated variants are distinct business facts and must both be persisted
   when both exist for the same company, statement type, and fiscal period.
4. Query features such as spec `081` must be able to explicitly retrieve:
   - standalone / parent only
   - consolidated only
   - audited / unaudited when requested
   - represented / original when relevant
5. The system must not rely on raw statement title parsing at query time to infer whether a row is
   standalone or consolidated. That fact must be persisted structurally.
6. The current-API financial-statement item model must use one shared catalog across statement
   types rather than separate per-statement tables. The minimum catalog shape is:
   - `ProviderName`
   - `StatementType`
   - `SourceItemId`
   - `TitleFa`
   - `TitleEn`
   - `Unit`
7. Governed metric resolution must come from a separate persisted mapping table from source-item
   catalog rows to `MetricCode`, not from hardcoded runtime dictionaries.

## Acceptance Criteria

1. The vendor request bodies for `IncomeStatement`, `BalanceSheet`, and `CashFlow` support
   `items: []` so the vendor may return every available statement item for the requested company
   and bounded year/period filter.
2. Every returned vendor line item is persisted in normalized storage or in a dedicated related
   statement-observation table with its source item id, vendor title, numeric value, source unit,
   and parent normalized statement id.
3. A shared persisted financial-statement source-item catalog exists for all three statement
   types, keyed by at least `(ProviderName, StatementType, SourceItemId)`, and stores at minimum
   `TitleFa`, `TitleEn`, and `Unit`.
3. Governed metric promotion remains explicit and reviewed:
   - reviewed catalog items map to governed `MetricCode` values through a separate persisted
     mapping table
   - unreviewed items are retained as stored observations and catalog entries, not discarded
   - query-time and recalculation-time `MetricCode` resolution for current-API statements comes
     from the persisted mapping table, not from hardcoded code dictionaries
5. Same-period financial statements that differ by `IsComposing` must persist as separate
   statement rows. `IsComposing` must be part of the effective persistence identity and retrieval
   filters for normalized financial statements.
6. If the vendor can emit multiple same-period variants that differ by `IsAudited` or
   `IsRepresented`, those differences must also remain structurally retrievable instead of being
   destroyed by a pre-persistence canonical-winner rule.
7. Query-time features must be able to resolve:
   - `اصلی`, `غیرتلفیقی`, `شرکت اصلی`, `parent`, `standalone` -> standalone rows only
   - `تلفیقی`, `گروه`, `consolidated` -> consolidated rows only
8. A later re-sync of the same company/year is allowed to add newly published 12-month rows and
   newly available variant rows; prior successful 3/6/9-month runs must not permanently block that
   data from being stored.
9. Persistence remains idempotent for exact duplicate vendor rows and does not multiply rows across
   reruns of the same payload.
10. Existing query/deterministic-calculation code paths that depend on governed metric codes keep
   working; they simply gain richer persisted source coverage behind them.

## Scope Notes

- This feature is about current-API financial statements only.
- Fundamental-index catch-up already has a separate all-observation persistence path in spec `050`
  and is not redefined here.
- This feature may require additive schema changes if the current `FinancialStatementLineItems`
  table shape cannot represent all vendor-item facts.

## Out Of Scope

- Automatically promoting every vendor item into the governed semantic metric registry.
- Rewriting financial-statement analysis phrasing or AI orchestration behavior outside the data
  retrieval contracts that depend on this persistence.
- Monthly activity, product sales, or service sales ingestion.
