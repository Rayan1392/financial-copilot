# Tasks — Financial Statement Schema Fix

## Acceptance Gate

All tasks below complete; existing tests pass plus new tests added; after migration + re-ingest,
the verification queries in the user story return their expected results; scanner integration
test confirms CodalDb derived growth metrics still flow end-to-end.

## Schema & Persistence

**Task 1.1: Add `StatementType` to `NormalizedFinancialStatementRow`**
- File: `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/FinancialIngestionRows.cs`
- Add `public string StatementType { get; set; } = string.Empty;` under `PeriodType`.
- Brief doc comment: stringified `FinancialCopilot.Domain.Financial.Entities.FinancialStatementType` enum value.

**Task 1.2: Update `NormalizedFinancialStatementRowConfiguration`**
- File: `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/FinancialIngestionConfigurations.cs`
- `builder.Property(row => row.StatementType).HasMaxLength(32).IsRequired();`
- Remove the existing `HasIndex(row => new { row.ProviderName, row.ExternalStatementId }).IsUnique();`.
- Add `HasIndex(row => new { row.ProviderName, row.ExternalStatementId, row.StatementType }).IsUnique();`.
- Add `HasIndex(row => new { row.ProviderName, row.StatementType });` (non-unique support index).

**Task 1.3: EF migration `AddStatementTypeAndFixUniqueKey`**
- Generate with:
  ```powershell
  dotnet ef migrations add AddStatementTypeAndFixUniqueKey --project src/backend/FinancialCopilot.Infrastructure --startup-project src/backend/FinancialCopilot.API --context FinancialIngestionDbContext --output-dir Financial/Ingestion/Persistence/Migrations
  ```
- After EF generates the migration, **edit `Up()`** to prepend raw-SQL truncation before the
  schema changes (so the NOT NULL `StatementType` column can be added without backfilling):

  ```csharp
  // Spec 029: clean slate before adding NOT NULL StatementType.
  // The Worker MUST be stopped before applying this migration.
  migrationBuilder.Sql(@"
      TRUNCATE TABLE
          ""FinancialStatementLineItems"",
          ""FinancialStatements"",
          ""DerivedMetrics"",
          ""MetricRecalculationRequests"",
          ""MonthlyReportLineItems"",
          ""MonthlyReports"",
          ""ProviderRawPayloads""
      RESTART IDENTITY CASCADE;

      -- Reset CodalDb watermark so the next sync re-ingests everything.
      DELETE FROM ""CodalDbSyncStates"";
  ");
  ```
- EF's generated `AddColumn` for `StatementType` should be NOT NULL with no default (because the
  truncate above leaves the table empty).
- Verify the generated migration drops `IX_FinancialStatements_ProviderName_ExternalStatementId`
  and creates `IX_FinancialStatements_ProviderName_ExternalStatementId_StatementType` (unique)
  plus `IX_FinancialStatements_ProviderName_StatementType` (non-unique).
- `Down()` does the schema reversal (drop new indexes, drop new column, recreate old index). It
  cannot restore truncated data; document this in the migration's XML doc comment.

## Normalizer Fixes

**Task 2.1: Fix `CyclicalWavesFinancialStatementNormalizer`**
- File: `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/CyclicalWaves/CyclicalWavesFinancialStatementNormalizer.cs`
- Add `using FinancialCopilot.Domain.Financial.Entities;` and
  `using FinancialCopilot.Domain.Financial.Periods;`.
- Replace the single `statement.PeriodType = "IncomeStatement";` line at line 96 with:
  ```csharp
  statement.StatementType = nameof(FinancialStatementType.IncomeStatement);
  statement.PeriodType = nameof(FiscalPeriodType.ThreeMonths);
  ```

**Task 2.2: Update `CodalDbFinancialStatementNormalizer`**
- File: `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/CodalDb/CodalDbFinancialStatementNormalizer.cs`
- Change `UpsertStatementRowAsync` to take an additional `FinancialStatementType statementType` parameter.
- In `NormalizeAsync`, the two call sites become:
  - Income row: `externalStatementId = stmt.StmtId.ToString(CultureInfo.InvariantCulture)`,
    `statementType = FinancialStatementType.IncomeStatement` (no `:INC` suffix).
  - Balance row: same `externalStatementId`, `statementType = FinancialStatementType.BalanceSheet`
    (no `:BS` suffix).
- Inside the upsert: filter the lookup by `StatementType` too:
  ```csharp
  var statement = await dbContext.FinancialStatements.SingleOrDefaultAsync(
      row => row.ProviderName == ProviderName &&
          row.ExternalStatementId == externalStatementId &&
          row.StatementType == statementType.ToString(),
      cancellationToken);
  ```
- Set `statement.StatementType = statementType.ToString();` on both new and existing rows.
- Update the XML doc comment that describes the `:INC` / `:BS` suffix workaround — remove it.

**Task 2.3: Fix `FinancialStatementPayloadNormalizer` (configured provider)**
- File: `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/FinancialPayloadNormalizers.cs`
- Extend the `StatementDocument` record at line 145 with a required `string StatementType` field:
  ```csharp
  private sealed record StatementDocument(
      string StatementId,
      string CompanyId,
      decimal? NetProfit,
      string Period,
      string StatementType,
      DateOnly PeriodStart,
      DateOnly PeriodEnd);
  ```
- After `Deserialize<StatementDocument>` at line 97, validate both enum-shaped string fields and
  throw `FinancialProviderException(FinancialProviderErrorCode.InvalidResponse, ...)` on
  invalid values:
  ```csharp
  if (!Enum.TryParse<FiscalPeriodType>(document.Period, out _))
      throw new FinancialProviderException(FinancialProviderErrorCode.InvalidResponse,
          $"Unknown PeriodType value '{document.Period}'.");
  if (!Enum.TryParse<FinancialStatementType>(document.StatementType, out _))
      throw new FinancialProviderException(FinancialProviderErrorCode.InvalidResponse,
          $"Unknown StatementType value '{document.StatementType}'.");
  ```
- Set `statement.StatementType = document.StatementType;` alongside the existing field assignments.
- The EF lookup also filters by `StatementType` (mirrors Task 2.2).

## Tests

**Task 3.1: Update existing unit tests**
- File: `tests/FinancialCopilot.UnitTests/CodalDbFinancialStatementNormalizerTests.cs`
  - Drop any assertion checking `ExternalStatementId.EndsWith(":INC")` / `":BS")`.
  - Add `statement.StatementType == "IncomeStatement"` / `"BalanceSheet"` assertions.
  - Adjust the idempotency test: the second sync produces the same `(ExternalStatementId,
    StatementType)` pair, not new rows.
- File: `tests/FinancialCopilot.UnitTests/CyclicalWavesNormalizerTests.cs` (if it covers
  statement normalization — check first; if it only tests symbol normalization, add a new test
  class instead).
  - Assert `StatementType = "IncomeStatement"` and `PeriodType = "ThreeMonths"` on the output.
- Search the test projects for hand-constructed `new NormalizedFinancialStatementRow { ... }` —
  every one must now set `StatementType` or the test will fail at SaveChanges (the column is
  NOT NULL).

**Task 3.2: New unit test — configured-provider validation**
- File: `tests/FinancialCopilot.UnitTests/ConfiguredFinancialProviderNormalizerTests.cs` (new)
- Three cases:
  - Valid payload with `Period="ThreeMonths"`, `StatementType="IncomeStatement"` → row persists
    with both fields.
  - `Period="IncomeStatement"` → `FinancialProviderException` (InvalidResponse).
  - `StatementType="NotARealType"` → `FinancialProviderException` (InvalidResponse).

**Task 3.3: New integration test — schema uniqueness**
- File: `tests/FinancialCopilot.IntegrationTests/FinancialStatementSchemaTests.cs` (new)
- Insert `(provider=X, externalId=Y, type=IncomeStatement)` and `(provider=X, externalId=Y, type=BalanceSheet)` — both succeed.
- Insert a duplicate `(provider=X, externalId=Y, type=IncomeStatement)` — fails with the EF
  unique-constraint violation.

**Task 3.4: Update existing scanner-integration test fixtures**
- Files: `tests/FinancialCopilot.IntegrationTests/CodalDbGrowthMetricScannerTests.cs`,
  `tests/FinancialCopilot.IntegrationTests/DerivedMetricPersistenceTests.cs`,
  `tests/FinancialCopilot.IntegrationTests/ScannerExecutionEndpointTests.cs`,
  `tests/FinancialCopilot.IntegrationTests/MetricRecalculationOutboxTests.cs` etc.
- Wherever an in-memory test seeds `NormalizedFinancialStatementRow`, add
  `StatementType = nameof(FinancialStatementType.IncomeStatement)` (or `BalanceSheet` for
  balance items). Build will fail until all sites are updated.

## Documentation

**Task 4.1: Update `docs/codaldb-datasource.md`**
- Remove `:INC` / `:BS` suffix references.
- Describe the `(ExternalStatementId, StatementType)` natural-key pair.

**Task 4.2: Create `docs/financial-statement-model.md`**
- Short doc (~30 lines) explaining:
  - The distinction between `StatementType` (income/balance/cashflow) and `PeriodType`
    (duration).
  - The `StatementDocument` JSON contract expected from configured HTTP providers (required
    fields, enum validation).
  - The current limitation that balance-sheet rows reuse the period's `PeriodType` and the
    deferred plan for point-in-time semantics.

## Implementation Checklist Update

**Task 5.1: Mark Order 28 in-progress / completed in `specs/implementation-checklist.md`**
- During implementation: `[~]` then `[x]` on completion.
- Add a completion-log row under the bottom table summarizing the truncate-and-reingest strategy,
  test counts, and operator runbook notes.

## Operator Coordination Notes (not a code task, but required for rollout)

- **Stop the Worker process before applying the migration.** The truncate cascade would race
  with consumers otherwise. Restart after migration completes.
- **Trigger a fresh full sync** with `POST /api/v1/admin/codaldb/full-sync` after migration.
- **Expect scanner queries to return empty results temporarily** until the recalculation worker
  drains the outbox produced by the fresh sync.
- The migration's `Down()` cannot restore truncated data; it only reverses the schema. Document
  this clearly in the migration's XML doc comment so a future rollback is undertaken with eyes
  open.
