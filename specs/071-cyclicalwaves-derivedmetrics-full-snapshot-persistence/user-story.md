# User Story - CyclicalWaves DerivedMetrics Full Snapshot Persistence

## Story

As a FinancialCopilot operator,

I want CyclicalWaves `/api/custom-filtering/ticker/{ticker}` fields to be persisted into
`DerivedMetrics`,

so that symbol lookup, screening, and diagnostics can read all supported CyclicalWaves snapshots
from the canonical metric store instead of seeing only monthly-sales rows.

## Provider Scope

This story applies only to the CyclicalWaves provider.

Do not change:

* Noavaran Amin;
* `NoavaranCurrentApi`;
* `MonthlyReports` / `MonthlyReportLineItems` semantics;
* Noavaran `OutputType` 0/1/4 logic;
* Noavaran monthly activity recalculation;
* Noavaran million-Rial normalization.

## Problem

CyclicalWaves ticker responses include quarterly sales/profit/margin snapshots, monthly sales
snapshots, and valuation ratios. The normalizers persist many of those fields into normalized
line-item tables, but the recalculation/passthrough path persists only a small subset into
`DerivedMetrics` for the latest monthly period.

The same provider response currently reaches the pipeline through two dataset requests:
`FetchFinancialStatementsAsync(ticker)` and `FetchMonthlyReportsAsync(ticker)`. Both use
`GET /api/custom-filtering/ticker/{ticker}` and receive the same combined payload. This double
fetch is a dataset-abstraction artifact, not a CyclicalWaves payload requirement.

For ExternalCompanyId `3` and PeriodEnd `2026-05-31`, `DerivedMetrics` must not be limited to:

* `AVG_12M_MONTHLY_SALES`
* `MONTHLY_PRODUCTION_QUANTITY`
* `MONTHLY_SALES`
* `MONTHLY_SALES_GROWTH_MOM`
* `MONTHLY_SALES_GROWTH_YOY`
* `MONTHLY_SALES_QUANTITY`
* `MONTHLY_SALES_RATE`

## Required Persisted Metric Groups

At minimum, CyclicalWaves sync/recalculation must persist:

* quarterly sales: `last_quarter_sale`, `penultimate_quarter_sale`,
  `last_year_same_quarter_sale`, `average_4_quarter_sale`;
* quarterly net profit: `last_quarter_net_profit`, `penultimate_quarter_net_profit`,
  `last_year_same_quarter_net_profit`;
* quarterly gross profit: `last_quarter_gross_profit`, `penultimate_quarter_gross_profit`,
  `last_year_same_quarter_gross_profit`;
* quarterly operating profit: `last_quarter_operating_profit`,
  `penultimate_quarter_operating_profit`, `last_year_same_quarter_operating_profit`;
* margins: latest, penultimate, and last-year-same-quarter net/gross/operating profit margins;
* monthly sales: `average_12_month_sale`, `last_year_average_12_month_sale`,
  `last_month_sale`, `penultimate_month_sale`, `last_year_same_month_sale`;
* valuation: `pe`, `ps`.

## Revenue Persistence Boundary

Persisting `REVENUE` into `DerivedMetrics` is intended for explicit quarterly revenue lookup,
screening, and financial snapshots. It must not change monthly-sales natural-language routing.

Natural-language monthly-sales questions such as `فروش`, `آخرین فروش`, `فروش ماه`,
`فروش ماهانه`, `فروش این ماه`, `فروش YTD`, `متوسط فروش 12 ماهه`, and
`متوسط فروش ۱۲ ماهه` continue to resolve to `MONTHLY_SALES` and use the monthly-sales snapshot
renderer. `REVENUE` is selected only when the user explicitly asks for revenue, quarterly
revenue/sales, `درآمد فصلی`, or `فروش فصلی`.

## Units and Evidence

CyclicalWaves monetary values are already in Rials.

* Do not multiply by 1,000,000.
* Use Rials passthrough normalization.
* `SourceEvidenceJson` for monetary metrics must contain CyclicalWaves evidence with:
  `sourceUnit = Rials`, `canonicalUnit = Rials`, and
  `unitNormalizationPolicy = cyclicalwaves-precomputed-rials-passthrough-v1`.
* CyclicalWaves-derived metrics must not cite `NoavaranCurrentApi` as primary source evidence.

## Period Rules

* Monthly fields use monthly `PeriodType`; `last_month_sale`, `penultimate_month_sale`,
  `last_year_same_month_sale`, and `average_12_month_sale` align to the monthly periods derived
  from CyclicalWaves month markers.
* Quarterly fields use quarterly `PeriodType`; latest-quarter values align to the latest-quarter
  period derived from CyclicalWaves quarter markers.
* `PE_TTM` and `PS_TTM` keep the current/latest quarter policy already used by the system.
* CyclicalWaves vendor period markers must parse both compact and dashed formats:
  * `yyyyMMdd` for real API values such as `last_month_sale_date = "20260521"` and
    `last_quarter_date = "20260320"`;
  * `yyyy-MM-dd` for existing fixtures and backward-compatible payloads.
* Invalid, blank, or null vendor period markers must be handled safely and fall back to the
  existing receive-time period resolver.

## Provider-Aware Recalculation Rule

Provider-specific CyclicalWaves recalculation must read only CyclicalWaves-origin normalized rows.
For example, `MONTHLY_SALES`, `AVG_12M_MONTHLY_SALES`, `MONTHLY_SALES_GROWTH_MOM`,
`MONTHLY_SALES_GROWTH_YOY`, `REVENUE`, `AVG_4Q_REVENUE`, `PE_TTM`, `PS_TTM`, and margin metrics
must not mix Noavaran, Codal, or other provider rows into a CyclicalWaves-origin recalculation.

Implementation strategy:

* Keep the existing `DerivedMetrics` uniqueness key unchanged:
  `ExternalCompanyId + MetricCode + MetricVersion + CalculationPolicyVersion + PeriodEnd`.
* Resolve the provider identity from the recalculation source payload/request.
* Pass that provider identity into normalized metric input reading.
* Filter normalized statement/monthly sources by `ProviderName` when the recalculation is
  provider-specific.
* Preserve provider identity in `SourceEvidenceJson`.

This is preferred over changing the `DerivedMetrics` key because the immediate defect is accidental
source mixing during input selection. A key change would require a broader migration and product
decision about whether multiple provider-specific rows for the same metric/period should coexist.
If future cross-provider fallback is desired, it must be represented by explicit policy versions
and regression tests, not by unfiltered source reads.

## Single-Fetch Ingestion Rule

For CyclicalWaves full sync, `GET /api/custom-filtering/ticker/{ticker}` must be called once per
ticker. The single persisted `ProviderRawPayload` must be reused by both:

* `CyclicalWavesFinancialStatementNormalizer`;
* `CyclicalWavesMonthlyReportNormalizer`.

This must not change the provider-neutral ingestion model. The generic ingestion processor may
route one raw provider payload to multiple dataset normalizers when a provider endpoint returns a
combined snapshot, but the architecture must not become a CyclicalWaves-only special case.

The expected performance improvement is fewer duplicate ticker-detail calls: `N` requests for `N`
tickers instead of `2N`. This reduces provider endpoint quota usage, provider-throttling exposure,
duplicate checksum/store lookups, and the network-bound portion of full-sync duration.

## Acceptance Criteria

* CyclicalWaves normalization and recalculation persist all supported snapshot fields into
  `DerivedMetrics`.
* CyclicalWaves full sync performs one remote ticker-detail request per ticker while still
  populating both `FinancialStatements` and `MonthlyReports`.
* Exactly one raw ticker-detail payload is persisted for the shared CyclicalWaves response.
* Compact real vendor dates are parsed:
  `last_month_sale_date = "20260521"` yields `VendorPeriodDate = 2026-05-21`, and
  `last_quarter_date = "20260320"` yields `VendorPeriodDate = 2026-03-20`.
* Dashed vendor dates such as `2026-05-21` and `2026-03-20` continue to parse.
* M0/M1/M12 and Q0/Q1/Q4 periods align to parsed vendor dates when present.
* CyclicalWaves-derived metrics have CyclicalWaves-only source evidence when the recalculation
  originates from CyclicalWaves, even if other providers have rows for the same company/period.
* `AVG_12M_MONTHLY_SALES` stores the exact Rial value from `average_12_month_sale`.
* `AVG_12M_MONTHLY_SALES` remains user-facing only through the Persian display title
  `متوسط فروش ۱۲ ماهه`; the internal code is not shown to users.
* Repository/database-level regression tests prove CyclicalWaves sync does not persist only monthly
  sales metrics.
* Regression tests prove the single-fetch path does not change derived-metric recalculation
  outputs or remove recalculation requests for either financial statements or monthly reports.
* Noavaran behavior and unit normalization remain unchanged.

## Verification Query

```sql
SELECT "MetricCode", "PeriodType", "PeriodStart", "PeriodEnd", "Value", "Unit", "SourceEvidenceJson"
FROM public."DerivedMetrics"
WHERE "ExternalCompanyId" = '3'
  AND "SourceEvidenceJson"::text ILIKE '%CyclicalWaves%'
ORDER BY "PeriodEnd" DESC, "MetricCode";
```

Expected result: the query shows all supported CyclicalWaves metrics, not only monthly sales
metrics.
