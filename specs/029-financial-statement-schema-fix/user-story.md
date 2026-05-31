# User Story — Financial Statement Schema Fix

> Depends on `003` (financial domain model), `005` (data ingestion + normalization), `020`
> (CyclicalWaves provider), `023` (CodalDb financial-statement ingestion).
> Optional reference: `004` (Configured HTTP financial provider).

## Story

As a data engineer operating the scanner platform,
I want the `FinancialStatements` table to distinguish **statement type** (`IncomeStatement`,
`BalanceSheet`, `CashFlow`) from **period duration** (`ThreeMonths`, `SixMonths`,
`NineMonths`, `TwelveMonths`, `Monthly`, `TrailingTwelveMonths`),
so that scanner queries, ingestion idempotency, and downstream calculations all reflect the actual
financial model — not provider-specific workarounds.

## Context — three bugs found in production data, plus a quiet third one

Inspecting a populated PostgreSQL database (CodalDb full-sync followed by some CyclicalWaves test
runs) revealed three real issues plus a latent one:

1. **`CyclicalWavesFinancialStatementNormalizer` writes the wrong value into `PeriodType`.** At
   [src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/CyclicalWaves/CyclicalWavesFinancialStatementNormalizer.cs:96](../../src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/CyclicalWaves/CyclicalWavesFinancialStatementNormalizer.cs#L96)
   the normalizer sets `statement.PeriodType = "IncomeStatement"`. That column is supposed to hold
   the `FiscalPeriodType` enum name (e.g. `"ThreeMonths"`). All 1,825 CyclicalWaves rows currently
   in the database therefore fail
   `Enum.Parse<FiscalPeriodType>(observation.statement.PeriodType)` inside
   `LineItemMetricInputSource`, so no derived metric is ever produced from them.

2. **No `StatementType` column exists at all.** `NormalizedFinancialStatementRow` has no field
   for "income statement vs. balance sheet vs. cash flow". The domain layer already defines the
   enum
   ([src/backend/FinancialCopilot.Domain/Financial/Entities/FinancialEntities.cs:119-124](../../src/backend/FinancialCopilot.Domain/Financial/Entities/FinancialEntities.cs#L119-L124))
   but persistence never adopted it. `CodalDbFinancialStatementNormalizer` works around this by
   suffixing `":INC"` / `":BS"` onto `ExternalStatementId` — not queryable, and it muddies the
   provenance trail in `DerivedMetrics.SourceEvidenceJson`.

3. **Unique-key shape forces the suffix workaround.** The current unique index is
   `(ProviderName, ExternalStatementId)`. CodalDb writes two rows per statement (one income, one
   balance), so it has to mangle the external id. The natural key is
   `(ProviderName, ExternalStatementId, StatementType)`.

4. **A third normalizer is silently vulnerable.** `FinancialStatementPayloadNormalizer`
   (`ProviderName = "ConfiguredFinancialProvider"`,
   [FinancialPayloadNormalizers.cs:118](../../src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/FinancialPayloadNormalizers.cs#L118))
   reads `PeriodType` from a JSON field called `Period`. There is no validation; a future HTTP
   provider that happens to send `"IncomeStatement"` in that field would reintroduce the same
   class of bug. The configured provider is not in active production use today but is registered
   in DI and used by `appsettings.json` defaults.

CodalDb's 19,682 rows already use the right `PeriodType` values (`ThreeMonths`, `SixMonths`,
`NineMonths`, `TwelveMonths`). The schema fix brings the other normalizers and the missing
column in line with what CodalDb already does correctly.

## Migration strategy — clean slate via TRUNCATE + re-ingest

After review, the simplest and safest path is to **TRUNCATE all financial-statement-derived
tables and re-ingest from CodalDb** rather than backfilling existing rows. Rationale:

- Backfilling `PeriodType` from `(PeriodEnd - PeriodStart)` is fragile (off-by-day fiscal-year
  boundaries) and the CyclicalWaves rows are test data, not production observations.
- Rewriting CodalDb `ExternalStatementId` in place would invalidate the `documentId` references
  already serialized into `DerivedMetrics.SourceEvidenceJson`, requiring a parallel JSON rewrite.
- Re-running the CodalDb full-sync against the cleaned database is deterministic and predictable;
  it takes minutes to hours but happens once.

The migration therefore does this in order:

1. Truncate `FinancialStatementLineItems`, `FinancialStatements`, `DerivedMetrics`,
   `MetricRecalculationRequests`, `MonthlyReportLineItems`, `MonthlyReports`, and
   `ProviderRawPayloads` (so the next full sync starts cold).
2. Add the `StatementType` column as NOT NULL (no rows to backfill, so no default needed).
3. Drop the old unique index `(ProviderName, ExternalStatementId)`.
4. Create the new unique index `(ProviderName, ExternalStatementId, StatementType)`.
5. Create a supporting non-unique index on `(ProviderName, StatementType)` for scan-filter
   workloads.

Other tables retain their state (Companies, Symbols, Industries, Markets, IndustryGroups,
CodalDbSyncStates, MissingAnswerFeedbacks, etc.) — the company/symbol master and the sync
watermarks stay intact, so re-ingestion only repopulates statements and downstream derived data.

## Acceptance Criteria

- **Schema change**: `NormalizedFinancialStatementRow` gets a non-nullable `StatementType` column
  (`character varying(32)`) holding the string form of the `FinancialStatementType` enum. EF
  configuration enforces max-length 32 and `IsRequired()`.
- **Unique key**: changed from `(ProviderName, ExternalStatementId)` to
  `(ProviderName, ExternalStatementId, StatementType)`. A second non-unique index on
  `(ProviderName, StatementType)` supports filtering scans by type.
- **All three normalizers fixed and consistent**:
  - `CyclicalWavesFinancialStatementNormalizer`: writes
    `StatementType = nameof(FinancialStatementType.IncomeStatement)` and
    `PeriodType = nameof(FiscalPeriodType.ThreeMonths)`. The string literal `"IncomeStatement"`
    on the `PeriodType` column must no longer appear anywhere in the codebase.
  - `CodalDbFinancialStatementNormalizer`: writes `StatementType` explicitly on each of the two
    rows it produces per source statement (`IncomeStatement` on the income row, `BalanceSheet`
    on the balance row). `ExternalStatementId = stmt.StmtId.ToString(CultureInfo.InvariantCulture)`
    on both — no `:INC` / `:BS` suffix.
  - `FinancialStatementPayloadNormalizer` (configured provider): the JSON contract
    `StatementDocument` adds a required `string StatementType` field (validated against the
    `FinancialStatementType` enum); the normalizer writes it into the new column. `PeriodType`
    continues to come from `document.Period` but the normalizer validates it parses as
    `FiscalPeriodType` before saving (throws `FinancialProviderException` with
    `InvalidResponse` if not).
- **Migration**:
  - Truncates `FinancialStatementLineItems`, `FinancialStatements`, `DerivedMetrics`,
    `MetricRecalculationRequests`, `MonthlyReportLineItems`, `MonthlyReports`, and
    `ProviderRawPayloads` (raw SQL inside the migration, run before the schema change).
  - Resets the CodalDb sync watermark by deleting rows from `CodalDbSyncStates` so the next
    incremental sync sees the full dataset (or operator triggers `POST
    /api/v1/admin/codaldb/full-sync` explicitly).
  - Adds the `StatementType` column NOT NULL with no default.
  - Drops the old unique index, creates the new one, creates the supporting index.
- **Domain alignment**: the existing `FinancialStatementType` enum is used (no new enum
  introduced). Persistence stores the enum's `nameof` / `ToString()` value so the column
  round-trips.
- **Test data fix**: existing test fixtures that hand-construct `NormalizedFinancialStatementRow`
  rows must be updated to set `StatementType` explicitly. Failure to do so is a build/test error
  (the column is now required).
- **No scanner regression**: after the migration and a fresh CodalDb full-sync, queries like
  `NET_PROFIT_GROWTH_YOY >= 50` must return results equivalent to pre-migration behavior. The
  recalculation outbox path still works end-to-end.
- **Documentation**: `docs/codaldb-datasource.md` is updated to remove `:INC` / `:BS` references
  and describe the new `(ExternalStatementId, StatementType)` pair. A new short doc
  `docs/financial-statement-model.md` describes the `StatementType` vs. `PeriodType` distinction
  and the `StatementDocument` contract used by configured HTTP providers.

## Operator runbook for this migration

Because the migration truncates tables, the operator must run it in a coordinated way:

1. Stop the Worker process (so no consumer is reading the outbox mid-truncate).
2. Apply the migration:
   `dotnet ef database update --context FinancialIngestionDbContext`.
3. Restart the Worker.
4. Trigger a fresh full sync: `POST /api/v1/admin/codaldb/full-sync` with a DataAdmin token.
5. Watch `GET /api/v1/admin/data-sync/runs` until all ingestion runs complete.
6. Verify with the queries in the **Verification** section below.

Total downtime: only the truncate + schema change itself (seconds). Re-ingestion runs in the
background; scanner queries return empty results for affected metrics until the recalculation
worker catches up.

## Out of Scope (explicitly deferred)

- **Cash-flow line items**. The `CashFlow` enum value exists and the new column accepts it, but
  no normalizer writes cash-flow rows in this story. Future spec under CodalDb deferred items.
- **Balance-sheet period semantics**. Balance-sheet values are point-in-time snapshots, not
  period flows; storing them with the period's `PeriodType` (e.g. `"ThreeMonths"`) is a known
  approximation. A future story can introduce a `Snapshot` value or a `PointInTimeDate` column
  — both would change the metric engine's input contract and warrant their own story.
- **Reformulating earlier specs**. Specs 003, 005, 020, 023 are not retrospectively edited; this
  story closes the gap they collectively left.
- **Backfilling existing rows** (rejected during scope review). Cleaner to truncate and re-ingest;
  the existing CyclicalWaves rows are test data and the CodalDb data re-derives deterministically.

## Dependencies

- `003` Financial Domain Model — provides the `FinancialStatementType` enum that persistence now
  adopts.
- `005` Data Ingestion and Normalization — owns `NormalizedFinancialStatementRow`. This story
  amends it.
- `020` CyclicalWaves Data Provider — introduced the `PeriodType = "IncomeStatement"` bug.
- `023` CodalDb Financial Statement Ingestion — relies on the suffix workaround that this story
  replaces with `StatementType`.
- `004` Third-Party Data Provider Abstraction — owns the configured HTTP provider contract;
  the `StatementDocument` shape changes here.
- `006` Derived Metrics Engine — reads `PeriodType` via `Enum.Parse<FiscalPeriodType>`; will
  process the freshly re-ingested rows after the migration.

## Verification

- Migration applies successfully:
  `dotnet ef database update --project src/backend/FinancialCopilot.Infrastructure --startup-project src/backend/FinancialCopilot.API --context FinancialIngestionDbContext`.
- After re-ingestion completes, these queries must hold:

  ```sql
  -- No more 'IncomeStatement' polluting the PeriodType column.
  SELECT COUNT(*) FROM "FinancialStatements" WHERE "PeriodType" = 'IncomeStatement';
  -- Expected: 0

  -- Statement types are clean and use only the enum values.
  SELECT DISTINCT "StatementType" FROM "FinancialStatements";
  -- Expected: subset of {'IncomeStatement', 'BalanceSheet'}  (and 'CashFlow' eventually)

  -- CodalDb no longer has suffixes on ExternalStatementId.
  SELECT COUNT(*) FROM "FinancialStatements"
  WHERE "ProviderName" = 'CodalDb'
    AND ("ExternalStatementId" LIKE '%:INC' OR "ExternalStatementId" LIKE '%:BS');
  -- Expected: 0

  -- Period types are all valid FiscalPeriodType enum names.
  SELECT DISTINCT "PeriodType" FROM "FinancialStatements";
  -- Expected: subset of {ThreeMonths, SixMonths, NineMonths, TwelveMonths, Monthly, TrailingTwelveMonths}

  -- The combination (ExternalStatementId, StatementType) replaces the old uniqueness model.
  SELECT "ProviderName", "ExternalStatementId", COUNT(*)
  FROM "FinancialStatements"
  WHERE "ProviderName" = 'CodalDb'
  GROUP BY "ProviderName", "ExternalStatementId"
  HAVING COUNT(*) = 2;
  -- Expected: many rows (every CodalDb statement now has one IncomeStatement + one BalanceSheet
  -- row sharing the same ExternalStatementId).
  ```
- Scanner integration test confirms a query for `NET_PROFIT_GROWTH_YOY >= 50` returns expected
  CodalDb companies (using fresh DerivedMetrics rows produced after re-ingestion).
- New schema test confirms two rows with the same
  `(ProviderName, ExternalStatementId)` but different `StatementType` are permitted, while a
  duplicate `(ProviderName, ExternalStatementId, StatementType)` is rejected by the unique index.
- New configured-provider validation test: a `StatementDocument` JSON with
  `"StatementType":"NotARealType"` or `"Period":"NotAPeriod"` causes the normalizer to throw
  `FinancialProviderException` with `InvalidResponse`.
