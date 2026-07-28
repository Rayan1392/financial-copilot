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

## Shared Monthly Sales Routing Rule

For direct symbol lookup, the following user intents are monthly-sales intents and must resolve to
`MONTHLY_SALES`, not generic quarterly `REVENUE`:

* `فروش`
* `آخرین فروش`
* `فروش ماه`
* `فروش ماهانه`
* `فروش این ماه`
* `فروش YTD`
* `متوسط فروش 12 ماهه`
* `متوسط فروش ۱۲ ماهه`

`REVENUE` is selected only when the user explicitly asks for revenue, quarterly revenue/sales,
`درآمد فصلی`, or `فروش فصلی`.

## Regression Safety Rule

The original user message has priority over parser/tool output for monthly-sales routing. If the
original message is a monthly-sales question, parser output such as `REVENUE`, `sales`, or
`AVG_12M_MONTHLY_SALES` must not override `MONTHLY_SALES` routing or the monthly-sales snapshot
renderer. For example, if the original user message is `آخرین فروش کچاد چقدر بوده؟` and the LLM
rewrites the tool argument as `REVENUE کچاد`, `MONTHLY_SALES` wins.

## Renderer Ownership

`MonthlySalesSnapshotRenderer` owns monthly-sales snapshot responses.

Noavaran mode columns:

* `فروش ماهانه`
* `فروش ماه مشابه دوره قبل`
* `فروش YTD`
* `فروش YTD تا ماه قبل`

CyclicalWaves default mode columns:

* `فروش ماهانه`
* `متوسط فروش ۱۲ ماهه`
* `فروش YTD`
* `فروش YTD تا ماه قبل`

`GenericMetricRenderer` owns PE, PS, EPS, explicit `REVENUE`, net profit, margins, price metrics,
and other non-monthly point lookups. It must not render monthly-sales snapshot responses.

## Production/Sales Market Context Rule

For all AI answers related to monthly production and sales, including but not limited to:

* `آخرین فروش کچاد چقدر بوده؟`
* `فروش ماهانه کچاد`
* `گزارش تولید و فروش کچاد`
* `تولید و فروش ماهانه کچاد`
* `فروش کچاد در آخرین ماه`
* `فروش اردیبهشت کچاد`
* `تولید کچاد چقدر بوده؟`

the response table must stay compact and focused on identity plus production/sales metrics. It
must not include market quote columns:

* `آخرین قیمت`
* `درصد تغییر آخرین قیمت`
* `LATEST_PRICE`
* `DAILY_CHANGE_PCT`

This rule is general for monthly production/sales answers across providers. It does not remove quote
columns globally: price, valuation, screening, ratio, and market-statistic questions may still show
market quote context when relevant.

## Acceptance Criteria

### Alias Routing

The following phrases must resolve to `MONTHLY_SALES` and the monthly-sales snapshot workflow, not
generic quarterly `REVENUE` lookup:

* آخرین فروش
* فروش ماهانه
* فروش کچاد

If the LLM parser emits a generic sales term such as `فروش`, `sales`, `revenue`, or `REVENUE` while
the original user message is a latest/monthly sales question, the backend must preserve the
monthly-sales snapshot route.

### Default CyclicalWaves Sales Layout

For general/latest CyclicalWaves sales questions, the table columns are:

* نماد
* شرکت
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

Default CyclicalWaves sales questions must not show:

* فروش ماه مشابه دوره قبل
* آخرین قیمت
* درصد تغییر آخرین قیمت

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

* نماد
* شرکت
* فروش ماهانه
* فروش ماه مشابه دوره قبل
* فروش YTD
* فروش YTD تا ماه قبل

The previous-period value is calculated from persisted `MONTHLY_SALES` rows by finding the latest
`PeriodEnd`, subtracting one Persian year, and selecting the same company and matching prior-year
month. Missing prior-year rows render Missing/null.

`AVG_12M_MONTHLY_SALES` must not be used as a substitute in explicit same-period requests.

Explicit same-month previous-period/year questions may show `فروش ماه مشابه دوره قبل`, but still
must not show `آخرین قیمت`, `درصد تغییر آخرین قیمت`, `LATEST_PRICE`, or `DAILY_CHANGE_PCT`.

### Production And Sales Report Layout

For production/sales report questions such as `گزارش تولید و فروش کچاد` or
`تولید و فروش ماهانه کچاد`, the table must remain production/sales-focused only. It may include
identity columns and relevant production/sales metrics, but it must not include market quote
columns.

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

* `آخرین فروش`, `فروش ماهانه`, and `فروش کچاد` resolve to `MONTHLY_SALES`, not `REVENUE`.
* Default query `آخرین فروش کچاد چقدر بوده؟` with CyclicalWaves rows renders exactly the compact
  sales table: `نماد`, `شرکت`, `فروش ماهانه`, `متوسط فروش ۱۲ ماهه`, `فروش YTD`, and
  `فروش YTD تا ماه قبل`.
* The default CyclicalWaves regression table does not contain `REVENUE`, `آخرین قیمت`,
  `درصد تغییر آخرین قیمت`, `LATEST_PRICE`, or `DAILY_CHANGE_PCT`.
* The default CyclicalWaves table does not include `فروش ماه مشابه دوره قبل`.
* `AVG_12M_MONTHLY_SALES` values are divided by 1,000,000 for display.
* `فروش ماه مشابه سال قبل کچاد چقدر بوده؟` uses the prior-period layout, may include
  `فروش ماه مشابه دوره قبل`, and does not include market quote columns.
* Missing prior-year same-month rows render Missing/null and do not fall back to average sales.
* API response display names never contain `AVG_12M_MONTHLY_SALES`, `Average 12 Month Sales`, or
  `Average 12-Month Monthly Sales`.
* `گزارش تولید و فروش کچاد` renders a production/sales-focused table only and does not include
  market quote columns.
* Noavaran monthly-sales behavior from spec 069 remains unchanged.
* `درآمد فصلی کچاد` resolves to `REVENUE` and the generic metric renderer.
* If the LLM rewrites `آخرین فروش کچاد چقدر بوده؟` as `REVENUE کچاد`, `MONTHLY_SALES` still wins.
