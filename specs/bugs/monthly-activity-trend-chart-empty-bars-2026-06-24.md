# Bug: Monthly Activity Trend Chart — All Bars Empty Despite Having Data

**Date:** 2026-06-24  
**Spec:** 077 / 078  
**Severity:** High — the chart is the primary deliverable of spec 078; it renders without any bars for every company.

---

## Symptom

When asking `نمودار فروش ماهانه شمواد`, the AI text summary correctly shows:

- آخرین دوره: 1405/03
- فروش: 1,233.559 میلیارد تومان
- مقایسه با ماه مشابه سال قبل: 481.899 (+156٪)
- میانگین ۱۲ ماهه: 918.727

But the chart renders **all 12 monthly bars as `—` (missing)** for both current year (1405) and previous year (1404). The only thing rendered is the flat 918.727 average line.

The missing-data note section lists all 12 months of 1404 as unavailable, even though months 1404/12 exist in the database.

---

## Root Cause

**Two-part mismatch: the calculator writes `null` for `FiscalYear`/`FiscalMonthIndex`, but `BuildChartPoints` searches exclusively by those null fields.**

### Part 1 — Calculator writes null (write path)

`CompanyMonthlyActivityTrendSnapshotCalculator.cs` lines 200–202:

```csharp
var row = new CompanyMonthlyActivityTrendSnapshotUpsertRow(
    ...
    FiscalYear: null,          // ← hardcoded null, never derived
    FiscalMonthIndex: null,    // ← hardcoded null, never derived
    FiscalMonthNameFa: null,   // ← hardcoded null, never derived
    ...
);
```

Every snapshot row in `CompanyMonthlyActivityTrendSnapshots` has `FiscalYear IS NULL` and `FiscalMonthIndex IS NULL` in the database. The fields were scaffolded but their derivation logic was never implemented.

### Part 2 — UseCase searches only by those null fields (read path)

`MonthlyActivityTrendQueryUseCase.cs` `BuildChartPoints()` lines 88–100:

```csharp
var currentFiscalYear = latestSnapshot?.FiscalYear ?? latestYear;
// latestSnapshot.FiscalYear is null → falls back to latestYear (e.g. 1405)
var previousFiscalYear = currentFiscalYear - 1;  // 1404

for (var fiscalMonthIdx = 1; fiscalMonthIdx <= 12; fiscalMonthIdx++)
{
    var currentSnap = snapshots.FirstOrDefault(
        s => s.FiscalYear == currentFiscalYear && s.FiscalMonthIndex == fiscalMonthIdx);
    // FiscalYear is null on every row → no row ever matches → always null

    var previousSnap = snapshots.FirstOrDefault(
        s => s.FiscalYear == previousFiscalYear && s.FiscalMonthIndex == fiscalMonthIdx);
    // Same problem → always null
}
```

Because `FiscalYear` and `FiscalMonthIndex` are `null` on every persisted row, **every `FirstOrDefault` returns null** regardless of what data is in the database. All 12 chart points get null sales amounts and `IsCurrentYearReported = false`.

### Why the text summary is correct

`BuildInsights()` (lines 135–177) reads fields that **are** persisted correctly — `MonthlySalesAmount`, `SameMonthPreviousYearSalesAmount`, `Average12MonthSalesAmount`, `SalesAmountYoYGrowthPercent` — none of which depend on `FiscalYear`/`FiscalMonthIndex`. So the prose summary is accurate while the chart is empty.

### Repository query is also insufficient

`GetAnnualComparisonBaseAsync` (EfCoreCompanyMonthlyActivityTrendSnapshotRepository.cs lines 76–95) filters by:

```csharp
(r.ReportYear == latestReportYear && r.ReportMonth <= latestReportMonth)
|| (r.ReportYear == prevYear)
```

For شمواد with latest = 1405/03 and prevYear = 1404:
- Current window: rows where `ReportYear=1405 AND ReportMonth<=3` → returns 1405/01, 1405/02, 1405/03 ✓
- Previous window: rows where `ReportYear=1404` → returns 1404/12 ✓

The repository fetches the right rows. The failure is entirely in `BuildChartPoints` discarding them because it matches on `FiscalYear`/`FiscalMonthIndex` (both null) instead of `ReportYear`/`ReportMonth`.

---

## Data Evidence

From `CompanyProductRevenueMix` (which mirrors what `MonthlyReports` contains for شمواد):

| ReportYear | ReportMonth | Data |
|---|---|---|
| 1404 | 12 | ✓ present |
| 1405 | 1 | ✓ present |
| 1405 | 2 | ✓ present |
| 1405 | 3 | ✓ present |

These four rows are fetched by `GetAnnualComparisonBaseAsync` but lost in `BuildChartPoints`.

---

## Fix Required

**Option A (minimal, no schema change):** Rewrite `BuildChartPoints` to match by `ReportYear`/`ReportMonth` instead of `FiscalYear`/`FiscalMonthIndex`. For companies whose fiscal year matches the calendar year (all NADPCO companies so far), `ReportYear == FiscalYear` and `ReportMonth == FiscalMonthIndex`. The loop over fiscal months 1–12 maps directly to Jalali months 1–12.

```csharp
// Replace the two FirstOrDefault calls:
var currentSnap = snapshots.FirstOrDefault(
    s => s.ReportYear == currentFiscalYear && s.ReportMonth == fiscalMonthIdx);
var previousSnap = snapshots.FirstOrDefault(
    s => s.ReportYear == previousFiscalYear && s.ReportMonth == fiscalMonthIdx);
```

This is safe because all current data is from NADPCO where the Jalali report year/month equals the fiscal year/month (fiscal year ends Esfand = month 12).

**Option B (complete):** Derive and persist `FiscalYear` and `FiscalMonthIndex` in `CompanyMonthlyActivityTrendSnapshotCalculator` from `FiscalEndDate`. For companies with a standard Esfand fiscal year end, `FiscalYear = ReportYear` and `FiscalMonthIndex = ReportMonth`. This is the correct long-term solution if non-standard fiscal years are ever supported, but requires a backfill of existing rows.

**Recommended:** Apply Option A immediately (one-line change in the use case, no migration needed) and note Option B as a future improvement if non-standard fiscal years are introduced.

---

## Files

| File | Location | Issue |
|---|---|---|
| `CompanyMonthlyActivityTrendSnapshotCalculator.cs` | lines 200–202 | Writes `FiscalYear: null`, `FiscalMonthIndex: null` |
| `MonthlyActivityTrendQueryUseCase.cs` | lines 88, 98, 100 | Reads `FiscalYear`/`FiscalMonthIndex` — always null → no match |
| `EfCoreCompanyMonthlyActivityTrendSnapshotRepository.cs` | lines 76–95 | Fetches correct rows by `ReportYear`/`ReportMonth` (not the problem) |
