# User Story - CyclicalWaves Monthly Sales Average Snapshot

## Story

As a FinancialCopilot user,

I want CyclicalWaves sales metric questions such as:

* آخرین فروش کچاد
* فروش ماهانه کچاد
* فروش کچاد

to show the latest monthly sales, the 12-month average monthly sales, and YTD sales facts from
persisted CyclicalWaves-derived metrics.

## Provider Scope

This story applies only to **CyclicalWaves** sales metrics persisted in `DerivedMetrics`:

* `MONTHLY_SALES`
* `AVG_12M_MONTHLY_SALES`
* `MONTHLY_SALES_YTD`
* `MONTHLY_SALES_YTD_PREVIOUS_MONTH`
* sales-related metrics sourced from CyclicalWaves evidence

This story does **not** apply to Noavaran Amin monthly activity data:

* `MonthlyReports`
* `MonthlyReportLineItems`
* `OutputType` 0/1/4 logic
* Noavaran monthly activity composite snapshot
* previous fiscal-year same-month lookup from spec 069

If this story conflicts with spec 069, spec 069 remains authoritative for Noavaran data.

## Acceptance Criteria

### Default CyclicalWaves Sales Layout

For general/latest CyclicalWaves sales questions, the table columns are:

* فروش ماهانه
* متوسط فروش ۱۲ ماهه
* فروش YTD
* فروش YTD تا ماه قبل

Mappings:

| Display label | Metric source |
| --- | --- |
| فروش ماهانه | `MONTHLY_SALES` |
| متوسط فروش ۱۲ ماهه | `AVG_12M_MONTHLY_SALES` |
| فروش YTD | existing YTD sales metric |
| فروش YTD تا ماه قبل | existing previous-month YTD sales metric |

`AVG_12M_MONTHLY_SALES` is stored in canonical Rials in `DerivedMetrics`; in this table it is
displayed in million Rials by dividing by 1,000,000.

The internal metric code `AVG_12M_MONTHLY_SALES` must never appear in UI labels, API display names,
table headers, generated text, or Persian-facing assistant output. The mandatory Persian display
title is exactly:

```text
متوسط فروش ۱۲ ماهه
```

### Explicit Same-Month Previous-Period Layout

Only when the user explicitly asks for same-month previous-period/year sales, such as:

* فروش ماه مشابه دوره قبل کچاد چقدر بوده؟
* فروش ماه مشابه سال قبل کچاد چقدر بوده؟
* فروش مدت مشابه سال قبل کچاد چقدر بوده؟
* فروش ماه مشابه دوره قبل را هم نشان بده

the table replaces the average column with:

* فروش ماه مشابه دوره قبل

In this explicit mode, the table columns are:

* فروش ماهانه
* فروش ماه مشابه دوره قبل
* فروش YTD
* فروش YTD تا ماه قبل

The previous-period value is calculated from persisted `MONTHLY_SALES` rows by finding the latest
`PeriodEnd`, subtracting one Persian year, and selecting the same company and matching prior-year
month. Missing prior-year rows render Missing/null.

`AVG_12M_MONTHLY_SALES` must not be used as a substitute in explicit same-period requests.

### Rendering

The response remains table-only with unit metadata:

* backend compatibility text may be exactly `Unit: million Rials`;
* Persian UI renders the localized metadata label `واحد: میلیون ریال`;
* no LLM-generated prose, fallback text, report suggestions, or false missing-data narrative may
  appear when the table has valid values;
* monetary monthly-sales table values are displayed in million Rials;
* no stock price or daily price-change context is shown for monthly sales snapshots.

## Regression Coverage

Tests must prove:

* `آخرین فروش کچاد چقدر بوده؟` with CyclicalWaves rows uses `متوسط فروش ۱۲ ماهه`.
* The default CyclicalWaves table does not include `فروش ماه مشابه دوره قبل`.
* `AVG_12M_MONTHLY_SALES` values are divided by 1,000,000 for display.
* `فروش ماه مشابه دوره قبل کچاد چقدر بوده؟` uses the prior-period layout and does not include
  `متوسط فروش ۱۲ ماهه`.
* Missing prior-year same-month rows render Missing/null and do not fall back to average sales.
* API response display names never contain `AVG_12M_MONTHLY_SALES`, `Average 12 Month Sales`, or
  `Average 12-Month Monthly Sales`.
* Noavaran monthly-sales behavior from spec 069 remains unchanged.
