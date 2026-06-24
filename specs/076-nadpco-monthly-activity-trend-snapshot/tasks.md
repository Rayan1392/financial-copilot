# Tasks - NADPCO Monthly Activity Trend Snapshot

## Task 1 - Define Derived Snapshot Entity

Create a new persistence row in the Financial Ingestion bounded context:

`CompanyMonthlyActivityTrendSnapshotRow`

Suggested table name:

`CompanyMonthlyActivityTrendSnapshots`

Fields:

- `Id`
- `ExternalCompanyId`
- `CompanySymbol`
- `CompanyName`
- `IndustryId`
- `IndustryTitle`
- `CategoryId`
- `CategoryTitle`
- `ReportYear`
- `ReportMonth`
- `FiscalEndDate`
- `FiscalYear`
- `FiscalMonthIndex`
- `FiscalMonthNameFa`
- `CalendarYear`
- `CalendarMonth`
- `MonthlySalesAmount`
- `MonthlyProductionQuantity`
- `MonthlySalesQuantity`
- `MonthlyAverageSalesRate`
- `HasMixedProductUnits`
- `ProductUnitSummary`
- `SameMonthPreviousYearSalesAmount`
- `SameMonthPreviousYearProductionQuantity`
- `SameMonthPreviousYearSalesQuantity`
- `Average12MonthSalesAmount`
- `Average12MonthPeriodCount`
- `YtdSalesAmount`
- `YtdProductionQuantity`
- `YtdSalesQuantity`
- `YtdPreviousMonthSalesAmount`
- `SalesAmountMomGrowthPercent`
- `SalesAmountYoYGrowthPercent`
- `ProductionQuantityYoYGrowthPercent`
- `SalesQuantityYoYGrowthPercent`
- `CurrentMonthOutputType`
- `YtdOutputType`
- `YtdPreviousMonthOutputType`
- `SourceProviderName`
- `SourceReportId`
- `SourceRawPayloadId`
- `IsComparablePreviousYearAvailable`
- `IsAverage12MonthComplete`
- `DataCompletenessScore`
- `CalculatedAtUtc`

### Required indexes

- Unique: `ExternalCompanyId + ReportYear + ReportMonth`
- Non-unique: `CompanySymbol + ReportYear + ReportMonth`
- Non-unique: `ExternalCompanyId + FiscalYear + FiscalMonthIndex`
- Non-unique: `SourceProviderName + CalculatedAtUtc`

### Notes

- Monetary values are stored in million Rials for this Noavaran-specific trend snapshot.
- If the existing codebase requires canonical monetary storage for generic metric rows, keep that policy inside `DerivedMetrics`; do not silently convert this snapshot table without a clearly named unit column or value object.

---

## Task 2 - Add EF Configuration and Migration

Files:

- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/FinancialIngestionRows.cs`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/FinancialIngestionConfigurations.cs`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/FinancialIngestionDbContext.cs`

Requirements:

1. Add `DbSet<CompanyMonthlyActivityTrendSnapshotRow>`.
2. Configure decimal precision for monetary and percentage fields.
3. Configure Persian text fields with appropriate max lengths.
4. Create migration:

`AddCompanyMonthlyActivityTrendSnapshots`

Acceptance:

- Migration creates the table and indexes.
- Migration down method drops the table.
- Model snapshot is updated.

---

## Task 3 - Create Calculation Contracts

Create Application-layer contracts:

`CompanyMonthlyActivityTrendSnapshotContracts.cs`

Suggested interfaces:

```csharp
public interface ICompanyMonthlyActivityTrendSnapshotCalculator
{
    Task RecalculateAsync(long externalCompanyId, int reportYear, int reportMonth, CancellationToken cancellationToken);
    Task RecalculateRangeAsync(long externalCompanyId, int fromYear, int fromMonth, int toYear, int toMonth, CancellationToken cancellationToken);
}

public interface ICompanyMonthlyActivityTrendSnapshotRepository
{
    Task UpsertAsync(CompanyMonthlyActivityTrendSnapshot snapshot, CancellationToken cancellationToken);
    Task<IReadOnlyList<CompanyMonthlyActivityTrendSnapshot>> GetCompanyTrendAsync(long externalCompanyId, int fromYear, int fromMonth, int toYear, int toMonth, CancellationToken cancellationToken);
    Task<CompanyMonthlyActivityTrendSnapshot?> GetLatestAsync(long externalCompanyId, CancellationToken cancellationToken);
}
```

Keep deterministic calculation in Application/Infrastructure services. The LLM must not calculate these metrics.

---

## Task 4 - Implement Company-Month Aggregator

Create service:

`CompanyMonthlyActivityTrendSnapshotCalculator`

Responsibilities:

1. Load Noavaran monthly report rows for one company/month.
2. Select `OutputType = 0` as the authoritative monthly period.
3. Aggregate product and service line items into company-level totals.
4. Include negative sales values in net sales.
5. Exclude zero header rows from quantity/unit analysis but keep them in evidence counts if needed.
6. Detect mixed product units:
   - If all non-zero quantity rows use the same unit, aggregate quantity.
   - If multiple units exist, set `HasMixedProductUnits = true` and keep quantity totals nullable or mark them as mixed.
7. Calculate weighted average sales rate only when units are compatible and total sales quantity is non-zero:

```text
MonthlyAverageSalesRate = MonthlySalesAmount / MonthlySalesQuantity
```

8. Load outputType 1 for YTD values when available.
9. Load outputType 4 for YTD-to-previous-month values when available.
10. Persist all source provenance fields.

Acceptance:

- For the attached company 194 sample, outputType 0 produces a net monthly sales amount equal to the sum of non-zero productSaleValue rows, including the negative return line.
- outputType 1 and 4 are not used to create the monthly bar value.

---

## Task 5 - Implement Comparable Period and Average Calculations

Inside the calculator or a dedicated helper:

### Same-month previous-year

- Match by `ExternalCompanyId`, `ReportMonth`, and `ReportYear - 1` using outputType 0 / persisted snapshot values.
- If absent, leave comparable fields null and set `IsComparablePreviousYearAvailable = false`.

### Trailing 12-month average

- Use the latest 12 available single-month company-level snapshots ending at the current report period.
- If fewer than 12 months exist, calculate the average over available periods but set:
  - `Average12MonthPeriodCount = N`
  - `IsAverage12MonthComplete = false`
  - Lower `DataCompletenessScore`

### Growth percentages

- `SalesAmountMomGrowthPercent` compares current month with the immediately previous available month.
- `SalesAmountYoYGrowthPercent` compares current month with same month previous year.
- If denominator is null or zero, return null and add missing/zero-denominator metadata.

---

## Task 6 - Integrate With Noavaran Ingestion

File candidates:

- `NadpcoApiMonthlyActivityNormalizer.cs`
- Monthly activity recalculation coordinator from spec 057/059
- Existing ingestion workflow that currently triggers company product revenue mix recalculation

Requirement:

After successful persistence of monthly activity rows for a company/month:

1. Recalculate the trend snapshot for that company/month.
2. Recalculate affected future rows that depend on this month for trailing 12-month averages.
3. Recalculate the latest annual comparison snapshot if spec 077 is implemented.
4. Replace previous calculated rows for the same company/month deterministically.

Important:

- Do not run the LLM in ingestion.
- Do not block the whole ingestion batch if one company-month trend calculation fails; record failure and continue according to existing ingestion failure-isolation policy.

---

## Task 7 - Backfill Command / Admin Operation

Add an admin/backfill operation to rebuild trend snapshots from already persisted Noavaran monthly activity data.

Endpoint: `POST /api/v1/admin/noavaran-current/trend-snapshot-backfill`

**No request body.** Configuration is driven entirely from appsettings:

```json
"TrendSnapshotBackfill": {
  "FromYear": 1404,
  "FromMonth": 1,
  "ToYear": 1405,
  "ToMonth": 3,
  "ForceRebuild": false
}
```

Eligible company IDs are enumerated from the `NoavaranEligibleCompanies` view via `NoavaranCompanyScope.EligibleCompanies`. One HTTP call processes all eligible companies and returns an aggregate result.

Acceptance:

- Can rebuild from 1403 onward (set `FromYear` in config).
- Iterates all eligible companies automatically.
- Reports processed companies, processed months, skipped months, and failed months.

---

## Task 8 - Repository Queries

Implement repository methods optimized for AI/chart retrieval:

1. `GetLatestAsync(externalCompanyId)`
2. `GetCompanyTrendAsync(externalCompanyId, fromYear, fromMonth, toYear, toMonth)`
3. `GetAnnualComparisonBaseAsync(externalCompanyId, latestReportYear, latestReportMonth)`
4. `GetLatestAvailablePeriodsAsync(externalCompanyId, count)`

All repository methods must read the trend snapshot table, not raw line-item tables.

---

## Task 9 - Automated Tests

### Unit tests

- Aggregates outputType 0 monthly sales from product rows.
- Includes negative sales values in net sales.
- Does not use outputType 1 or 4 to calculate monthly bar values.
- Calculates trailing 12-month average from available snapshots.
- Flags incomplete 12-month average when fewer than 12 periods exist.
- Calculates YoY growth when previous-year same month exists.
- Returns null YoY growth when previous-year denominator is zero/missing.
- Detects mixed units and suppresses unsafe quantity aggregation.

### Integration tests

- Noavaran ingestion writes monthly activity rows and triggers trend snapshot upsert.
- Re-ingesting the same company/month replaces the previous snapshot.
- Backfill rebuilds snapshots for a bounded date range.
- Repository methods return only persisted trend snapshots.

### Regression tests

- Monthly production/sales query path does not aggregate `MonthlyReportLineItems` at request time.
- Monthly production/sales answers do not include market quote columns such as `LATEST_PRICE`, `DAILY_CHANGE_PCT`, `آخرین قیمت`, or `درصد تغییر آخرین قیمت`.

---

## Checklist Gate

Before marking this spec complete:

- [x] `CompanyMonthlyActivityTrendSnapshots` table exists with required indexes
- [x] Snapshot values are calculated from Noavaran monthly activity data only
- [x] outputType 0 is the only source for monthly chart bars
- [x] outputType 1 and 4 are stored as YTD context only
- [x] 12-month average is persisted or deterministically derived into snapshot rows
- [x] Previous-year comparable values are persisted or directly available in snapshot queries
- [x] Backfill from 1403 onward is available
- [ ] AI/query path can read trend data without raw line-item aggregation
- [x] Unit and integration tests pass
- [x] `dotnet build FinancialCopilot.sln -c Release` passes
- [x] `dotnet test` passes
