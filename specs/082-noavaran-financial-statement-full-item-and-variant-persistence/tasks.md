# Feature 082 - Tasks

## Feature
Noavaran Financial Statement Full-Item And Variant Persistence

## Status

- [ ] Proposed

## Dependencies

- `029-financial-statement-schema-fix`
- `040-nadpco-api-financial-statement-sync`
- `051-noavaran-archive-and-current-api-strategy`

## Task 082.1 - Replace Curated Vendor Request Allowlists For Current-API Statements

### Goal
Request full current-API statement payloads while keeping the request bounded by company and year /
period filters.

### Acceptance Criteria

- `api/v2/FS/IncomeStatement/Values`, `api/v2/FS/BalanceSheet/Values`, and
  `api/v2/FS/CashFlow/Values` send `items: []`.
- Existing bounded query parameters such as `fromYear`, `toYear`, `perTId`, `isAudited`,
  `isRepresented`, and `isComposing` remain available.
- No endpoint is widened into an unbounded all-history request.

## Task 082.2 - Add Full Vendor Statement Item Persistence

### Goal
Persist all returned vendor items instead of only governed mapped rows.

### Design Expectations

- Reuse the normalized financial statement header as the parent row.
- Use one shared source-item catalog across all statement types; do not create separate catalog
  tables for income statement, balance sheet, and cash flow.
- The minimum shared catalog shape is:
  - `ProviderName`
  - `StatementType`
  - `SourceItemId`
  - `TitleFa`
  - `TitleEn`
  - `Unit`
- Persist a separate governed mapping table from source-item catalog rows to `MetricCode`.
- Statement fact rows must reference the shared catalog row directly or through a structurally
  equivalent persisted key.
- Persist at minimum:
  - parent statement id
  - source-item catalog reference
  - numeric value
  - source unit
  - optional cached `MetricCode` only as a denormalized helper, not as the primary mapping source

### Acceptance Criteria

- Unmapped items are retained, not dropped.
- Reviewed mapped items remain easy to query by governed `MetricCode`.
- The source-item catalog is populated/upserted from vendor payload metadata.
- Current-API statement `MetricCode` resolution no longer depends on hardcoded runtime item maps.
- Duplicate reruns of the same statement do not create duplicate item rows.

## Task 082.3 - Stop Pre-Persistence Variant Collapse

### Goal
Persist standalone and consolidated statements separately.

### Acceptance Criteria

- The ingestion path no longer picks a single canonical row per
  `(StatementType, Company, PeriodType, PeriodEnd)` before persistence.
- Same-period rows that differ by `IsComposing=false` and `IsComposing=true` are both stored.
- If needed, the normalized statement identity / unique key / supporting indexes are expanded so
  the storage model can represent both rows safely.

## Task 082.4 - Persist Variant Flags Structurally

### Goal
Make variant filtering queryable without reparsing `WarningsJson`.

### Acceptance Criteria

- `IsComposing` is queryable as a first-class persisted field or an equivalent indexed structural
  fact on normalized financial statements.
- Review whether `IsAudited` and `IsRepresented` also need first-class persisted fields so spec
  `081` and future features do not depend on evidence JSON parsing for variant routing.
- Query-time retrieval for `اصلی` versus `تلفیقی` does not depend on statement title text.

## Task 082.5 - Preserve Governed Metric Promotion

### Goal
Keep deterministic calculations stable while expanding source coverage.

### Acceptance Criteria

- Governed mapping ownership moves to the persisted mapping table for current-API statements.
- Derived-metric recalculation and existing metric readers continue to read governed metrics only.
- Full-item persistence does not silently invent new semantic metric codes.
- Persisted source-item-to-`MetricCode` mappings remain explicitly reviewed and governable.

## Task 082.6 - Query Contract Follow-Through

### Goal
Align downstream specs and repositories with the richer persistence model.

### Acceptance Criteria

- Spec `081` retrieval can explicitly select standalone vs consolidated rows from persisted
  storage.
- Period-analysis and future direct-item lookup features can trace the exact source statement row
  used for an answer.
- Documentation is updated to explain that full vendor coverage and variant separation are now
  data-layer guarantees, not renderer heuristics.

## Task 082.7 - Tests

### Unit / Integration Coverage

- Statement request body uses `items: []` for all three endpoints.
- Same company / statement type / period with `IsComposing=false` and `IsComposing=true` both
  persist.
- If the same period also differs by audited or represented flags, those rows remain retrievable
  according to the chosen schema rules.
- Unmapped returned item IDs persist as vendor observations.
- Reviewed mapped item IDs still populate governed metric paths used by existing readers.
- A rerun after the vendor later publishes a 12-month statement stores the new statement instead of
  being blocked by an earlier completed 3/6/9-month run.

## Task 082.8 - Documentation And Checklist

### Acceptance Criteria

- Add this feature to `specs/README.md`.
- Add an implementation-checklist row immediately after spec `081`.
- Update related spec notes in `040` and `081` so the ownership boundary is explicit.
