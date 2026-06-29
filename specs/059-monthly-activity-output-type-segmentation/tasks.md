# Spec 059 — Tasks: Monthly Activity Output Type Segmentation

## Prerequisites
- Spec 042 complete (NADPCO monthly activity sync)
- Spec 057 complete (monthly activity freshness and backfill)
- Build and tests pass before starting

---

## Story A — Fetch and store all 5 output types

### Task A-1 — Add `OutputType` column to `NormalizedMonthlyReportRow`

**File:** `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/FinancialIngestionRows.cs`

Add property:
```csharp
/// <summary>
/// NADPCO outputTypeId (0–4). See spec 059.
/// 0 = single month, 1 = YTD, 2 = adjustments, 3 = YTD prev (adjusted), 4 = YTD prev.
/// Null for ServiceSales rows (endpoint has no outputTypeId parameter).
/// </summary>
public int? OutputType { get; set; }
```

### Task A-2 — Update EF configuration for `MonthlyReports`

**File:** `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/FinancialIngestionConfigurations.cs`

In `NormalizedMonthlyReportRowConfiguration.Configure`, add:
```csharp
builder.Property(row => row.OutputType);
```
No `HasMaxLength` needed (int).

### Task A-3 — Create EF migration

Create migration `AddOutputTypeToMonthlyReports`:

```csharp
// Up:
migrationBuilder.AddColumn<int>(
    name: "OutputType",
    table: "MonthlyReports",
    type: "integer",
    nullable: true);

// Down:
migrationBuilder.DropColumn(name: "OutputType", table: "MonthlyReports");
```

Update `FinancialIngestionDbContextModelSnapshot.cs` to include `OutputType` in the `MonthlyReports` entity section.

### Task A-4 — Update `NadpcoApiMonthlyActivityNormalizer` to persist `OutputType`

**File:** `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoApiMonthlyActivityNormalizer.cs`

1. When upserting a `NormalizedMonthlyReportRow`, set `row.OutputType = item.OutputType` (from `NadpcoApiMonthlyActivityItem`).
2. Update `BuildExternalReportId`: when a vendor-assigned `activityId` is present, still include the output type in the key to prevent collision across output types:
   - Old: `$"{sourceKind}:{activityId.Value}"`
   - New: `$"{sourceKind}:{activityId.Value}:output-{outputPart}"`
   - Rationale: the vendor assigns different `ActivityID` values per output type for the same company-month, but the previous format without output type can produce duplicate `ExternalReportId` values when two output types return the same `ActivityID` (edge case observed in the wild).
   - Fallback path rule: when `activityId` is absent, the canonical key is
     `"{sourceKind}:{companyId}:{year:D4}-{month:D2}:output-{outputPart}"`.
     Do not append `categoryId`, category title, industry, or any grouping metadata.
3. `ServiceSales` items already have `OutputType = null`; no change needed there.

### Task A-5 — Update `NadpcoApiDataProviderClient.FetchMonthlyReportsAsync` to call all 5 output types

**File:** `src/backend/FinancialCopilot.Infrastructure/Financial/Providers/NadpcoApi/NadpcoApiDataProviderClient.cs`

Replace the single `ProductSales` call with a loop over `outputTypeId` values 0–4:

```csharp
private static readonly int[] ProductSalesOutputTypes = [0, 1, 2, 3, 4];
```

In `FetchMonthlyReportsAsync`:
- Call `BuildMonthlyActivityEndpoint("api/v2/MonthlyActivity/ProductSales", fromToken, toToken, outputType)` for each value in `ProductSalesOutputTypes`.
- Collect results into a list of `(outputType, payloadJson)` pairs.
- On failure for any single output type (non-401, non-5xx-total-failure): log a warning and continue with remaining types (same isolation pattern as ServiceSales).
- Serialize the envelope to include all five result arrays. Update `NadpcoMonthlyActivityEnvelope`:

```csharp
// New envelope shape:
record NadpcoMonthlyActivityEnvelope(
    string ProductSalesType0,   // outputTypeId=0
    string ProductSalesType1,   // outputTypeId=1
    string ProductSalesType2,   // outputTypeId=2
    string ProductSalesType3,   // outputTypeId=3
    string ProductSalesType4,   // outputTypeId=4
    string ServiceSales);
```

**Backward compatibility:** The old `NadpcoMonthlyActivityEnvelope` had only `ProductSales` and `ServiceSales`. Existing stored payloads in `ProviderRawPayloadStore` use the old shape. The normalizer must handle both shapes gracefully (attempt to deserialize as new shape; fall back to old shape with `ProductSales` mapped to type 0, `OutputType = null`).

**File:** `src/backend/FinancialCopilot.Infrastructure/Financial/Providers/NadpcoApi/NadpcoApiPayloadModels.cs` — update/extend `NadpcoMonthlyActivityEnvelope` and add backward-compat logic.

### Task A-6 — Update `NadpcoApiMonthlyActivityNormalizer` to process new envelope shape

**File:** `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoApiMonthlyActivityNormalizer.cs`

Update `NormalizeAsync` to:
1. Try deserializing as new 6-field envelope (`ProductSalesType0`…`ProductSalesType4`, `ServiceSales`).
2. Fall back to old 2-field envelope (`ProductSales`, `ServiceSales`) for backward compat — treat `ProductSales` as type 0, leave `OutputType = null` (old data predates segmentation).
3. For each output type array that is non-empty, parse and normalize items tagged with `OutputType = N`.
4. For `ServiceSales`, normalize as before with `OutputType = null`.
5. Ensure the total upsert loop handles all rows; the existing unique-index constraint on `(ProviderName, ExternalReportId)` naturally deduplicates re-runs.

### Task A-7 — Remove `MonthlyActivityOutputType` setting from `NadpcoApiProviderOptions`

**File:** `src/backend/FinancialCopilot.Infrastructure/Financial/Providers/NadpcoApi/NadpcoApiProviderOptions.cs`

Remove `MonthlyActivityOutputType` property (was `int?`, defaulted to `null`) — it is superseded by the hardcoded all-5 loop. Update `appsettings.json` (API and Worker) to remove the property if present.

**Note:** Verify `MonthlyActivityOutputType` is not referenced outside of `NadpcoApiDataProviderClient.cs` and `NadpcoApiProviderOptions.cs` before removing.

### Task A-8 — Unit tests

**File:** `tests/FinancialCopilot.UnitTests/NadpcoApiMonthlyActivityNormalizerTests.cs` (or adjacent test file)

Add test cases:
1. `Normalize_NewEnvelope_StoresAllFiveOutputTypes` — given a new-shape envelope with data in each output type array, verify 5 separate report rows are created with `OutputType` 0–4.
2. `Normalize_OldEnvelope_BackwardCompat_OutputTypeNull` — given an old-shape envelope, verify rows are created with `OutputType = null` (no error).
3. `Normalize_EmptyOutputTypeArray_NoRowCreated` — given a new-shape envelope where one output type returns `[]`, verify no row is created for that type.
4. `BuildExternalReportId_IncludesOutputType_WhenActivityIdPresent` — verify the new key format includes output type suffix.
5. `BuildExternalReportId_Fallback_DoesNotIncludeCategoryId` — verify a category-scoped payload still
   produces the canonical key without any `:category-{id}` suffix.

---

## Story B — Expose `OutputType` through the API response layer

### Task B-1 — Add `OutputType` to `AdminMonthlyActivityBackfillProgressResponse`

**File:** `src/backend/FinancialCopilot.API/Contracts/AdminDataOperationsContracts.cs`

Add `IReadOnlyDictionary<int, int> OutputTypeCounts` to `AdminMonthlyActivityBackfillProgressResponse` — a count of stored rows per output type (key = outputTypeId, value = row count).

### Task B-2 — Populate `OutputTypeCounts` in the backfill progress query

**File:** `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/MonthlyActivityBackfillCoordinator.cs`

In `GetProgressAsync`, add a query against `MonthlyReports` grouped by `OutputType` to produce the counts dictionary.

### Task B-3 — Add `OutputType` counts to Noavaran sync state response

**File:** `src/backend/FinancialCopilot.API/Contracts/AdminDataOperationsContracts.cs`

Extend `AdminNadpcoApiSyncStateItem` with `int? StoredOutputType` (or add a separate per-output-type summary to the Noavaran sync state endpoint).

---

## Story C — AI query routing by output type (separate milestone, do not block A/B)

### Task C-1 — Define `MonthlyActivityQueryIntent` enum

**File:** `src/backend/FinancialCopilot.Application/FinancialData/Providers/MonthlyActivityOutputTypeContracts.cs` (new file)

```csharp
public enum MonthlyActivityQueryIntent
{
    SingleMonth = 0,
    YearToDate = 1,
    Adjustment = 2,
    YearToDateAdjusted = 3,
    YearToDatePrevious = 4
}

public interface IMonthlyActivityOutputTypeResolver
{
    MonthlyActivityQueryIntent Resolve(string? userQueryHint, bool hasExplicitMonth);
}
```

### Task C-2 — Implement `DefaultMonthlyActivityOutputTypeResolver`

**File:** `src/backend/FinancialCopilot.Infrastructure/Financial/Providers/NadpcoApi/DefaultMonthlyActivityOutputTypeResolver.cs`

Rules:
- If `hasExplicitMonth == true` → `SingleMonth` (0)
- If the query explicitly asks for year-to-date / from fiscal-year start → `YearToDate` (1)
- If the query asks for latest sales without explicit month qualification → compose the grouped
  latest-sales view from persisted facts: `SingleMonth` (0), prior fiscal-year same-month
  `SingleMonth` (0 from the previous year), `YearToDate` (1), and `YearToDatePrevious` (4)
  when available.
- Otherwise → `SingleMonth` (0)

Register in DI as `AddSingleton<IMonthlyActivityOutputTypeResolver, DefaultMonthlyActivityOutputTypeResolver>`.

### Task C-3 — Wire resolver into metric lookup

Identify the metric query path for `MONTHLY_SALES`, `MONTHLY_SALES_QUANTITY`, `MONTHLY_PRODUCTION_QUANTITY`, `MONTHLY_SALES_RATE` (spec 057 Phase C metrics) and pass the resolved `OutputType` as a filter when producing persisted aggregate facts. For latest-sales symbol lookup, compose the response from already-persisted aggregates; do not sum `MonthlyReportLineItems` live in the query path.

For prior-period cells, select the persisted single-month aggregate for the same
`ExternalCompanyId` and same Shamsi/reporting month one fiscal year earlier. If it is absent,
return a missing comparable-period value with source/freshness context instead of fabricating it.

For latest sales, monthly sales, sales quantity/rate, monthly production, and the grouped
monthly-sales snapshot, suppress market quote context in the lookup response: do not include
`LATEST_PRICE` or `DAILY_CHANGE_PCT`. Add an API-boundary regression assertion for this behavior.

This task requires spec 057 Phase C metrics to be implemented first.

---

## Checklist Gate

Before marking this spec complete:
- [ ] All 5 output types are fetched per company-month ingestion request
- [ ] `OutputType` column exists in `MonthlyReports` table and is populated
- [ ] `ExternalReportId` includes output type to prevent cross-type key collisions
- [ ] `ExternalReportId` fallback remains canonical and never includes `categoryId`
- [ ] Old stored payloads (two-field envelope) are normalized without error (`OutputType = null`)
- [ ] Unit tests cover new-shape, old-shape, and empty-array cases
- [ ] `AdminMonthlyActivityBackfillProgressResponse` includes `OutputTypeCounts`
- [ ] Build passes: `dotnet build FinancialCopilot.sln -c Release`
- [ ] Tests pass: `dotnet test` (unit + architecture)
- [ ] Story C tasks are independent and tracked separately
