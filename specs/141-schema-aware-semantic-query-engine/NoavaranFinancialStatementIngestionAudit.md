# Noavaran Financial Statement Ingestion Audit

**Audit date:** 2026-10-08  
**Database:** configured development PostgreSQL, read-only transaction  
**Scope:** `ProviderName = NoavaranCurrentApi`, `Dataset = FinancialStatements`

## Result

**Overall: Partially.** The current normalizer persists every item present in a successfully
normalized Noavaran payload, including items without a governed `MetricCode`. The development
database also contains 78,677 line-item observations whose numeric values remain stored but whose
normalized row has no source-catalog relationship. Their original provider payloads are retained,
so identity recovery can be evaluated by a bounded replay. No backfill was run.

## Current persistence path

1. The provider stores each `NoavaranCurrentApi` response in `ProviderRawPayloads` before
   normalization (`FinancialDataSyncProcessor` and `ProviderRawPayloadStore`).
2. `NadpcoApiFinancialStatementNormalizer.NormalizeAsync` parses the income statement, balance
   sheet, and cash-flow arrays and preserves statement identity, variant flags, period dates,
   source checksum, and sync time.
3. `UpsertSourceItemCatalogAsync` upserts
   `FinancialStatementSourceItems` using `(ProviderName, StatementType, SourceItemId)` and copies
   the provider's title and unit.
4. `UpsertLineItemsAsync` writes the original decimal to
   `FinancialStatementLineItems.Value` and the catalog FK to
   `FinancialStatementLineItems.SourceItemCatalogId` whether or not a mapping exists. The
   `MetricCode` is nullable; `FinancialStatementSourceItemMetricMappings` is optional governed
   enrichment.
5. Statement header identity includes provider, external statement ID, statement type, and the
   audited/represented/composing flags. Parent scope remains structurally queryable through
   `IsComposing`.

Null amount entries are retained as null observations and are now called out in the parent
statement's `WarningsJson.InvalidObservations` with source item ID, title, and `NullAmount` reason.
Malformed JSON or a nonnumeric amount that cannot deserialize fails normalization; the raw
payload is stored before that failure and the sync run is marked failed by the processor.

## Bounded development database counts

Counts below came from aggregate `SELECT` queries inside a PostgreSQL `READ ONLY` transaction.
They are a snapshot, not a production report.

| Record set | Income statement | Balance sheet | Cash flow | Total |
|---|---:|---:|---:|---:|
| Normalized statement headers | 12,426 | 12,419 | 12,416 | 37,261 |
| Source-item catalog definitions | 119 | 107 | 70 | 296 |
| Definitions with a governed mapping | 9 | 2 | 1 | 12 |
| Persisted line-item observations | 439,701 | 525,107 | 396,266 | 1,361,074 |
| Unmapped observations (`MetricCode IS NULL`) | 308,878 | 508,659 | 380,498 | 1,198,035 |
| Rows missing `SourceItemCatalogId` | 63,103 | 7,951 | 7,623 | 78,677 |

The 78,677 unlinked rows are all metric-coded. The query found **zero null-valued line-item rows**
in these current-API statements. All 78,677 parent statement checksums matched a retained raw
financial-statement payload. The provider currently has 6,241 retained raw financial-statement
payload rows. Ten recent payloads inspected contained numeric amounts for each item examined; this
sample does not establish complete historical raw-to-normalized equality. The parent
`LastSynchronizedAt` for 44,095 unlinked rows predates the Feature 082 schema migration; 34,582
parents have a later timestamp. That header timestamp is not an item-level refresh marker and does
not prove that a particular line item was re-read after the migration.

## Gap and cause

The missing normalized identity is a legacy/backfill gap, rather than a current
MetricCode-required filter in the Noavaran normalizer. The predecessor `040` path persisted the
curated governed subset as line items keyed by `MetricCode`; migration
`20260630154843_AddNoavaranFinancialStatementSourceCatalogAndVariants` added the nullable
`SourceItemCatalogId` and source catalog/mapping tables without backfilling existing line items.
Rows can remain unlinked when no later successfully normalized payload included that specific
source item. These observations still have their values and metric codes but cannot be directly
resolved to the original source item through the normalized relationship. Raw payload checksums
provide a recovery path for all rows counted in the audit.

The current API normalizer does not use the governed metric map as a persistence filter. There is
no new schema migration required for current full-item persistence or the null-amount diagnostic.
The catalog remains deliberately small; no additional MetricCodes, units, or formulas should be
inferred from titles.

The original `040` API request was also limited to curated item IDs. A source item that the API did
not return at that time cannot be counted from normalized rows alone. The present development
snapshot does not establish how many valid historical vendor items were never requested; current
`082` requests use `items: []`.

## Historical recovery

Replay of the retained raw payloads is needed to restore source-item identity for the 78,677
unlinked rows. Do not run a bulk backfill until its report is reviewed. The implementation should
be bounded, idempotent, resumable, and dry-run capable; it should match source item IDs and
statement variants from the raw payload, preserve existing governed values, and report unresolved
or conflicting observations. Do not infer identity by Persian title. No production or development
backfill was executed during this audit.

## Regression coverage added

- Mapped and unmapped values retain the source catalog relationship and provider unit.
- Different source item IDs with the same Persian title persist as distinct observations.
- Same source IDs under different statement types or providers remain separate catalog identities.
- Null numeric values remain explicit and diagnostic.
- Existing tests continue to cover idempotent re-ingestion, statement variants/revisions,
  malformed payload rejection, mapped metric codes, and existing metric paths.

## Feature 141 query requirement

Feature 141 must make an approved direct source item selectable by provider, statement type, and
source item ID even when `MetricCode` is null. The result must join through
`SourceItemCatalogId` and carry the exact decimal, provider unit, statement/period/variant,
checksum, and source identity as evidence. Direct-item selection must remain separate from
governed metric calculations. The query engine must not treat a historical null source FK as an
approved direct observation or silently substitute a similar title.
