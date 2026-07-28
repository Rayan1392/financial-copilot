# Bug: Nadpco MonthlyReportLineItems Contain Extra Non-API Products and Inflated Sales Totals

**Date:** 2026-06-25
**Severity:** Critical — all downstream monetary calculations (revenue mix, trend snapshots, AI answers, YoY comparisons, screening metrics) read inflated totals from `MonthlyReportLineItems`

---

## Summary

For company **کسرا** (companyId=202, ExternalCompanyId="202"), period **1404/12**, outputTypeId=0, the `MonthlyReportLineItems` table contains **7 product rows that are absent from the Nadpco API response** plus the 6 rows that are present. The extra rows inflate the monthly sales total from the correct **2,119,732** million Rials to **3,019,385** million Rials — a difference of **899,653** million Rials (+42%). This happens because `BuildExternalReportId` in `NadpcoApiMonthlyActivityNormalizer` includes `categoryId` in the report identity key when the API record has no `activityId`, causing the same company/month/outputType ingestion to create **multiple separate `MonthlyReport` rows** (one per category). The `CompanyProductRevenueMixCalculator` and `CompanyMonthlyActivityTrendSnapshotCalculator` then query ALL matching reports and aggregate their line items together, summing products across categories that should never be combined.

## Canonical Identity Correction (2026-06-29)

The intended contract is now explicit:

- `MonthlyReports.ExternalReportId` must never include `categoryId`, category title, industry, or
  other line-item grouping metadata.
- This applies in current API ingestion, manual/monthly backfill, fallback identity paths when
  `activityId` is absent, and any grouping of product or service rows by category.
- Canonical fallback key for product sales:
  `ProductSales:{externalCompanyId}:{jalaliYear}-{jalaliMonth:D2}:output-{outputType}`.
- Canonical fallback key for service sales:
  `ServiceSales:{externalCompanyId}:{jalaliYear}-{jalaliMonth:D2}:output-none`.
- Category remains line-item evidence only.

---

## Business Impact

- **AI monthly sales answers** for کسرا 1404/12 report 3,019,385 million Rials instead of 2,119,732 — a 42% overstatement.
- **CompanyProductRevenueMix** totals are inflated; product revenue shares are distorted; the AI misstates which products are dominant.
- **CompanyMonthlyActivityTrendSnapshots** for کسرا carry inflated monthly figures; the trend chart shows incorrect growth/decline trajectories.
- **Average 12-month sales**, **YoY comparison**, and **YTD accumulation** all compound the error if multiple months are affected by the same pattern.
- **Screening and derived metrics** (revenue per share, growth rates) that derive from `CompanyProductRevenueMix` or trend snapshot tables inherit the inflated base.
- The bug affects any company/month where the Nadpco API returns records without an `activityId` and with multiple distinct `categoryId` values.

---

## Reproduction Scenario

| Field | Value |
|---|---|
| companyId | 202 |
| symbol | کسرا |
| companyTitle | سرامیک های صنعتی اردکان |
| period | 1404/12 |
| outputTypeId | 0 (دوره یک ماهه) |
| Expected API total | 2,119,732 million Rials |
| Observed DB total | 3,019,385 million Rials |
| Difference | +899,653 million Rials (+42%) |

---

## Expected API Line Items

| Title | ProductionQty | SalesQty | SalesRate | SalesAmount (M Rials) | Unit |
|---|---|---|---|---|---|
| کاتالیست ها | 72,000 | 82,000 | 3 | 307,624 | کیلوگرم |
| ضدسایش ها برگشت از فروش | 0 | -19,800 | 1 | -24,849 | کیلوگرم |
| ضدسایش ها | 353,070 | 222,039 | 1 | 319,617 | کیلوگرم |
| سود سرمایه گذاری ها درآمد ارائه خدمات | 0 | 1 | 1,200,311 | 1,200,311 | میلیون ریال |
| سایر | 0 | 139,727 | 0 | 19,867 | کیلوگرم |
| بستر کاتالیست ها | 221,985 | 180,466 | 1 | 297,162 | کیلوگرم |
| **Total** | | | | **2,119,732** | |

---

## Observed Database Line Items

Based on reported extra rows + expected rows, the database contains:

| Title | SalesAmount (M Rials) | Source |
|---|---|---|
| کاتالیست ها | 307,624 | API |
| ضدسایش ها برگشت از فروش | -24,849 | API |
| ضدسایش ها | 319,617 | API |
| سود سرمایه گذاری ها درآمد ارائه خدمات | 1,200,311 | API |
| سایر | 19,867 | API |
| بستر کاتالیست ها | 297,162 | API |
| کاتالیست اکتیو آلومینا | 307,624 | **EXTRA** |
| گلوله های سایز 75 - 15 | 271,942 | **EXTRA** |
| گلوله های توزیعی | 219,733 | **EXTRA** |
| گلوله های سیلیسی | 77,429 | **EXTRA** |
| لاینر | 27,447 | **EXTRA** |
| گلوله سایز 15-75 ADM900 | 20,228 | **EXTRA** |
| گلوله های سایز 75 - 15 برگشت از فروش | -24,750 | **EXTRA** |
| **Total** | **3,019,385** | |

Extra rows total: **899,653** million Rials

---

## Extra Rows Found In Database

These rows exist in `MonthlyReportLineItems` but are **absent** from the 1404/12 outputTypeId=0 Nadpco API response provided:

| Title | SalesAmount (M Rials) | Suspicion |
|---|---|---|
| کاتالیست اکتیو آلومینا | 307,624 | Same amount as "کاتالیست ها" — likely same product under a different category |
| گلوله های سایز 75 - 15 | 271,942 | Different product set — separate category |
| گلوله های توزیعی | 219,733 | Different product set — separate category |
| گلوله های سیلیسی | 77,429 | Different product set — separate category |
| لاینر | 27,447 | Different product set — separate category |
| گلوله سایز 15-75 ADM900 | 20,228 | Different product set — separate category |
| گلوله های سایز 75 - 15 برگشت از فروش | -24,750 | Return-from-sales row for a separate product set |

The product names "گلوله های سایز 75-15", "گلوله های توزیعی", "گلوله های سیلیسی", "لاینر" suggest a **second category** (grinding media / balls) that was stored in a separate `MonthlyReport` row under a different `categoryId`.

---

## Item-by-Item Comparison

| Title | Classification | Notes |
|---|---|---|
| کاتالیست ها | **matched** | Present in API and DB |
| ضدسایش ها برگشت از فروش | **matched** | Present in API and DB |
| ضدسایش ها | **matched** | Present in API and DB |
| سود سرمایه گذاری ها درآمد ارائه خدمات | **matched** | Present in API and DB |
| سایر | **matched** | Present in API and DB |
| بستر کاتالیست ها | **matched** | Present in API and DB |
| کاتالیست اکتیو آلومینا | **extra in DB / suspected duplicate alias** | Same SalesAmount (307,624) as "کاتالیست ها"; likely same product from different category ingestion OR a different category's output stored in a separate MonthlyReport that is incorrectly joined in aggregation |
| گلوله های سایز 75 - 15 | **extra in DB** | Not in the provided API payload for 1404/12 outputType=0 |
| گلوله های توزیعی | **extra in DB** | Not in the provided API payload for 1404/12 outputType=0 |
| گلوله های سیلیسی | **extra in DB** | Not in the provided API payload for 1404/12 outputType=0 |
| لاینر | **extra in DB** | Not in the provided API payload for 1404/12 outputType=0 |
| گلوله سایز 15-75 ADM900 | **extra in DB** | Not in the provided API payload for 1404/12 outputType=0 |
| گلوله های سایز 75 - 15 برگشت از فروش | **extra in DB** | Return-from-sales for a product not present in the provided API payload |

---

## Root Cause Analysis

### Confirmed Root Cause: `BuildExternalReportId` includes `categoryId` when `activityId` is absent

**File:** `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoApiMonthlyActivityNormalizer.cs:331-353`

```csharp
private static string BuildExternalReportId(
    string sourceKind, long? activityId, int companyId,
    int year, byte month, int? OutputType, int? categoryId)
{
    var outputPart = OutputType?.ToString(CultureInfo.InvariantCulture) ?? "none";

    if (activityId is not null)
    {
        return string.Create(CultureInfo.InvariantCulture,
            $"{sourceKind}:{activityId.Value}:output-{outputPart}");
    }

    // ← BUG: when activityId is absent, categoryId is baked into the report identity key
    var categoryPart = categoryId?.ToString(CultureInfo.InvariantCulture) ?? "none";
    return string.Create(CultureInfo.InvariantCulture,
        $"{sourceKind}:{companyId}:{year:D4}-{month:D2}:output-{outputPart}:category-{categoryPart}");
}
```

**What happens in practice:**

The Nadpco API for `MonthlyActivity/ProductSales` organizes products into categories. When the API returns records **without an `activityId`** (the fallback identity path), each distinct `categoryId` in the response produces a **different `ExternalReportId`**:

- Category A (e.g., categoryId=5 "کاتالیست ها"): `ProductSales:202:1404-12:output-0:category-5`
- Category B (e.g., categoryId=7 "گلوله ها"): `ProductSales:202:1404-12:output-0:category-7`

These two `ExternalReportId` values are different → the upsert at line 53-65 creates **two separate `MonthlyReport` rows** for the same company/month/outputType.

**How the aggregation reads both:**

`CompanyProductRevenueMixCalculator.RecalculateAsync` (line 26-41) queries:

```csharp
var reports = await dbContext.MonthlyReports
    .Where(r => r.ProviderName == ProviderName
             && r.ExternalCompanyId == externalCompanyId
             && r.ReportType == "ProductSales"
             && r.OutputType == 0)
    .ToListAsync(ct);
// then filters by PeriodStart date
var matchingReports = reports
    .Where(r => r.PeriodStart == periodStart)
    .Select(r => r.Id)
    .ToHashSet();
```

This query returns **all** `MonthlyReport` rows for the company/month/outputType — including both the category-A row and the category-B row. It then loads ALL line items from both and sums them, producing an inflated total.

**`CompanyMonthlyActivityTrendSnapshotCalculator` is subject to the same aggregation pattern** — it also queries reports by `ExternalCompanyId + PeriodStart + OutputType` to build trend snapshots.

**Status: CONFIRMED** — this is the primary root cause of the extra-product rows and inflated totals.

---

### Secondary Contributing Issue: Natural Key Hash Collision via CategoryId (previously documented)

**File:** `NadpcoApiMonthlyActivityNormalizer.cs:355-370` (see also `specs/bugs/duplicate-monthly-report-line-items-natural-key-collision-2026-06-25.md`)

When `activityId` IS present (so the same `MonthlyReport` row is used), but nested product items don't carry `CategoryID`/`CategoryTitle`, the `category` component of the natural key is taken from the **parent record's** `CategoryID` (as a numeric string). This produces different `ProductCode` hashes for the same logical product appearing under two different parent records → duplicate line items within a single report. This is a separate but compounding bug that may also affect the كسرا dataset.

**Status: CONFIRMED (existing bug; separate fix scope)**

---

### Ruled Out: CodalDB / Archive Data Contamination

The `CompanyProductRevenueMixCalculator` filters `r.ProviderName == ProviderName` where `ProviderName = "NoavaranCurrentApi"`. CodalDB/archive rows use a different `ProviderName` and would not be included. **Ruled out.**

### Ruled Out: Cross-Company Contamination

The query filters by `ExternalCompanyId == externalCompanyId`. **Ruled out.**

### Ruled Out: Cross-Period Contamination

The query filters by `PeriodStart == periodStart` (Gregorian date). Each Jalali month maps to a distinct Gregorian date range. **Ruled out.**

### Ruled Out: Soft-Delete Rows Being Included

There is no soft-delete mechanism on `MonthlyReportLineItems` or `MonthlyReports`. The schema has no `IsDeleted` / `DeletedAt` column. **Ruled out.**

### Ruled Out: OutputType=1/3/4 Contamination

`CompanyProductRevenueMixCalculator` explicitly filters `r.OutputType == 0`. **Ruled out for revenue mix.** However, `NormalizeAsync` (line 108-109) only triggers recalculation for `SourceKind == "ProductSales" && OutputType is null or 0` — OutputType=null (legacy) is included and may match the aggregation query if legacy rows exist.

### Ruled Out: Idempotency Failure (Re-ingestion Appending New Rows)

The upsert for `MonthlyReportLineItems` (lines 81-102) is idempotent per `(MonthlyReportId, ProductCode)` — it updates existing rows rather than inserting new ones. Re-running ingestion does not accumulate rows. **Ruled out as a cause of the initial extra rows** (though it won't remove stale rows from prior category-split ingestions).

### Ruled Out: Missing Stale Line Item Cleanup

There is **no DELETE step** before upserting line items. If a product disappears from the API in a later ingestion (e.g., a product was present in month N-1 but absent in month N, or the category structure changed), the old line item row persists indefinitely. This means if the API payload for 1404/12 is later re-fetched and returns fewer products, the orphaned rows from the first ingestion remain.

**This is a contributing factor** — if the extra rows were created by an earlier ingestion run that used a different API payload (e.g., during backfill from an older archive format), those rows would never be removed by subsequent ingestion runs.

---

## Data Model Findings

### MonthlyReports Uniqueness Rules

- **Unique index:** `(ProviderName, ExternalReportId)` — enforced in `FinancialIngestionConfigurations.cs:134`
- There is **no unique constraint on `(ProviderName, ExternalCompanyId, PeriodStart, OutputType, ReportType)`**
- This means multiple rows CAN exist for the same company/period/outputType if they have different `ExternalReportId` values — which is exactly what happens when `categoryId` is included in the ID

### MonthlyReportLineItems Uniqueness Rules

- **Unique index:** `(MonthlyReportId, ProductCode)` — enforced in `FinancialIngestionConfigurations.cs:155`
- No constraint on `(MonthlyReportId, Title)` — the same logical product title can appear multiple times under different `ProductCode` values

### Delete/Replace/Upsert Behavior

- `MonthlyReports`: upsert by `ExternalReportId` — existing rows are updated
- `MonthlyReportLineItems`: upsert by `(MonthlyReportId, ProductCode)` — existing rows are updated; **stale rows are never deleted**
- There is **no "replace all line items for this report" pattern** — orphaned line items from previous ingestion runs accumulate

### Provider/Source Fields

- `MonthlyReports.ProviderName` — "NoavaranCurrentApi" for live API data
- `MonthlyReports.ExternalCompanyId` — the Nadpco company numeric ID as a string
- `MonthlyReports.OutputType` — nullable int (0=single month, 1=YTD, etc.)
- `MonthlyReports.ReportType` — "ProductSales" or "ServiceSales"
- `MonthlyReports.ExternalReportId` — the unstable key that encodes `categoryId` when `activityId` is absent

### Product Title/ProductCode Handling

- `ProductCode` = `BuildLineItemCode(prefix, vendorCode, title, category, unit, index)`
- When `vendorCode` is present: `PRODUCT:{vendorCode}` — stable
- When `vendorCode` is absent: `PRODUCT:NATURAL:{SHA256(title|category|unit|index)[0..16]}` — unstable when `category` derives from `categoryId` numeric string rather than a stable title

---

## Downstream Impacted Features

| Feature | How Affected |
|---|---|
| **Latest monthly sales (AI)** | Reads from `CompanyProductRevenueMix.TotalCompanySalesAmount` — inflated |
| **Monthly production quantity (AI)** | May sum `ProductionQuantity` across all matching reports — inflated |
| **Monthly trend chart (spec 078)** | Reads `CompanyMonthlyActivityTrendSnapshots` — snapshots were calculated from inflated totals |
| **Average 12-month sales** | Any rolling average over monthly totals inherits inflated months |
| **YoY comparison** | Compares current vs prior-year month — if both months affected, the ratio is still wrong |
| **Product revenue mix (AI)** | Revenue shares are computed from inflated total — all percentages wrong |
| **Trend snapshots (spec 076)** | `CompanyMonthlyActivityTrendSnapshotCalculator` uses same query pattern as revenue mix |
| **AI monthly production/sales answers** | Reported figures are wrong for all affected company-months |
| **Screening / derived metrics** | Any metric derived from `CompanyProductRevenueMix` (e.g., revenue per share, growth %) is wrong |

---

## Recommended Fix

### Option 1 (Primary Fix): Remove `categoryId` from `ExternalReportId` — one report per company/month/outputType

**File:** `NadpcoApiMonthlyActivityNormalizer.cs:348-353`

Remove `categoryId` from the fallback path of `BuildExternalReportId`:

```csharp
// BEFORE (BUG):
var categoryPart = categoryId?.ToString(CultureInfo.InvariantCulture) ?? "none";
return string.Create(CultureInfo.InvariantCulture,
    $"{sourceKind}:{companyId}:{year:D4}-{month:D2}:output-{outputPart}:category-{categoryPart}");

// AFTER (FIX):
return string.Create(CultureInfo.InvariantCulture,
    $"{sourceKind}:{companyId}:{year:D4}-{month:D2}:output-{outputPart}");
```

This ensures that all items for the same company/month/outputType — regardless of which category they belong to — are stored under a single `MonthlyReport` row.

**Required companion change:** Add an **authoritative replace strategy** for line items. When a `MonthlyReport` row is matched (upsert), DELETE all existing `MonthlyReportLineItems` for that `MonthlyReportId` before re-inserting from the current payload. This eliminates stale rows from previous ingestion runs:

```csharp
// After upserting the MonthlyReport, before upserting line items:
var staleLineItems = await dbContext.MonthlyReportLineItems
    .Where(li => li.MonthlyReportId == report.Id)
    .ToListAsync(cancellationToken);
dbContext.MonthlyReportLineItems.RemoveRange(staleLineItems);
await dbContext.SaveChangesAsync(cancellationToken);
// Now insert all items from current payload
```

### Option 2 (Schema Fix): Add unique constraint on `(ProviderName, ExternalCompanyId, PeriodStart, OutputType, ReportType)`

Add a unique index to `MonthlyReports` on `(ExternalCompanyId, ProviderName, PeriodStart, OutputType, ReportType)`. This forces the database to reject a second row for the same logical period, making the bug observable at write time instead of silently accumulating rows.

### Option 3 (Query Fix): Aggregate correctly — deduplicate at read time

This is a mitigation, not a fix. In `CompanyProductRevenueMixCalculator` and `CompanyMonthlyActivityTrendSnapshotCalculator`, after loading all line items for matching reports, deduplicate by `NormalizeProductName(Title)` before summing. This reduces the symptom but does not address the duplicate storage.

**Recommended approach:** Apply Options 1 + 2 together. Option 1 fixes the key construction; Option 2 adds a schema-level guard. The authoritative replace strategy ensures future re-ingestions clean up stale rows.

### Database Cleanup Required

After applying the fix, run a **selective** repair script that:

1. Detects canonical/category-suffixed duplicate pairs for the same provider/company/month/outputType.
2. Keeps the canonical report row.
3. Deletes line items under the category-suffixed report row.
4. Deletes the category-suffixed `MonthlyReports` row.
5. Deletes affected `CompanyProductRevenueMixes` and `CompanyMonthlyActivityTrendSnapshots` rows so the
   affected periods can be recalculated cleanly.

See `specs/bugs/repair-category-suffixed-monthly-reports-2026-06-29.sql`.

---

## Regression Tests Required

Add to `NadpcoApiMonthlyActivityNormalizerTests.cs`:

1. **`Normalize_MultiCategoryPayload_CreatesSingleMonthlyReport`** — given a payload with products in two categories (categoryId=5 and categoryId=7) for the same company/month/outputType with no activityId, verify exactly ONE `MonthlyReport` row is created for that company/month/outputType.

2. **`Normalize_MultiCategoryPayload_AllProductsInSingleReport`** — given the above, verify all products across both categories appear as line items under the single report.

3. **`Normalize_Kasra_1404_12_OutputType0_TotalEquals2119732`** — given a payload matching the 1404/12 كسرا scenario, verify the sum of `SalesAmount` across all line items for outputType=0 equals 2,119,732.

4. **`Normalize_ReIngest_SamePayload_IsIdempotent`** — ingesting the same payload twice produces the same total — no accumulation of extra rows.

5. **`Normalize_OutputType1_DoesNotContaminateOutputType0`** — ingesting outputType=1 data for the same company/month does not create a row that is picked up by the OutputType=0 aggregation.

6. **`Normalize_StaleLineItems_AreClearedOnReIngest`** — if a product present in a previous ingestion is absent from the new payload, it must not appear in the final line items.

7. **`RevenueMixCalculator_DoesNotDoubleCountCrossReportProducts`** — if two `MonthlyReport` rows exist for the same company/month/outputType, the calculator must not sum them both (validates that fix option 1 prevents the scenario from ever occurring).

8. **`NormalizerTests_AiMonthlySalesQuery_UsesCorrectAuthoritativeRows`** — confirm the total AI-reported monthly sales for company 202, month 1404/12 equals the sum of line items from a single canonical report row, not from multiple category-split rows.

---

## Open Questions

1. **What `categoryId` values does the Nadpco API return for كسرا 1404/12?** The WarningsJson stored on the `MonthlyReport` rows (if they exist) would show the `LineItems` array with `CategoryID` per item. A SQL query against `MonthlyReports.WarningsJson` for companyId=202, period=1404/12, outputType=0 would reveal whether multiple separate report rows exist.

2. **Were the extra rows introduced by backfill or live ingestion?** If `LastSynchronizedAt` on the extra-product report differs from the main report's timestamp, they came from different ingestion runs.

3. **Does the Nadpco API return an `activityId` for كسرا 1404/12?** If yes, all products would share the same `ExternalReportId` and the category-splitting would not occur — meaning the extra rows came from a different code path (archive import or a legacy payload).

4. **How many other company-months are affected?** A query `SELECT ExternalCompanyId, PeriodStart, OutputType, COUNT(*) FROM MonthlyReports WHERE ProviderName = 'NoavaranCurrentApi' AND ReportType = 'ProductSales' GROUP BY ExternalCompanyId, PeriodStart, OutputType HAVING COUNT(*) > 1` would enumerate all affected periods.

5. **Does the backfill endpoint re-normalize stored raw payloads?** If it does, and if the stored raw payload uses the legacy 2-field envelope format, the `activityId` fallback path would be triggered even for companies where the live API now returns `activityId`.

---

## Agent Findings Summary

### Most Likely Root Cause

**`BuildExternalReportId` in `NadpcoApiMonthlyActivityNormalizer` encodes `categoryId` in the report identity key when the API record does not include an `activityId` (lines 348-353).** This creates multiple separate `MonthlyReport` rows for the same company/month/outputType — one per category. The `CompanyProductRevenueMixCalculator` (and likely `CompanyMonthlyActivityTrendSnapshotCalculator`) queries ALL matching reports for a given company/month/outputType and aggregates their line items, summing products from entirely different categories that should never be combined into one total.

The extra rows for كسرا 1404/12 ("گلوله های سایز 75-15", "گلوله های توزیعی", etc.) are real Nadpco data but belong to a **different product category** that was stored in a separate `MonthlyReport` row under a different `categoryId`-based `ExternalReportId`. The aggregation query had no mechanism to distinguish "different categories in one report" from "different reports for the same period."

### Exact Files That Must Be Fixed

| File | Change Required |
|---|---|
| `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoApiMonthlyActivityNormalizer.cs` | **Primary fix**: Remove `categoryId` from the fallback `ExternalReportId` (lines 348-353). Add authoritative replace strategy: delete all line items for the report before re-inserting (lines 79-102). |
| `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/FinancialIngestionConfigurations.cs` | **Schema guard**: Add unique index on `(ExternalCompanyId, ProviderName, PeriodStart, OutputType, ReportType)` in `NormalizedMonthlyReportRowConfiguration`. |
| `tests/FinancialCopilot.UnitTests/NadpcoApiMonthlyActivityNormalizerTests.cs` | Add regression tests (see section above). |
| Database (migration + SQL) | Merge duplicate `MonthlyReport` rows; recalculate `CompanyProductRevenueMix` and `CompanyMonthlyActivityTrendSnapshots` for all affected company-months. |

### Layer Where the Bug Lives

**Ingestion / persistence** — specifically the report identity key construction in `NadpcoApiMonthlyActivityNormalizer.BuildExternalReportId` and the absence of a stale-row cleanup step. The aggregation code in `CompanyProductRevenueMixCalculator` is semantically correct given the contract that one report = one company/month/outputType; the contract itself is broken by the ingestion code.

### Whether Code Changes Are Required Before Implementing Specs 076/077/078

**Yes, this is a blocking defect.** Specs 076 (trend snapshot) and 078 (trend chart) read from `CompanyMonthlyActivityTrendSnapshots`, which is built from the same inflated monthly totals. Spec 077 (AI monthly trend query) answers directly from these snapshots. All three specs will deliver incorrect answers until the root cause is fixed and the affected data is recalculated. Any regression tests written against 076/077/078 that assert specific totals will produce false positives against corrupted data.
