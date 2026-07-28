# User Story - Noavaran Monthly Sales Composite Lookup

## Story

As a FinancialCopilot user,

I want the AI assistant to correctly answer Noavaran monthly sales questions such as:

* آخرین فروش غگلپا چقدر است؟
* فروش ماهانه شپدیس چقدر بوده؟
* آخرین فروش کگل را نشان بده

so that I receive the complete Noavaran monthly activity sales snapshot instead of a single sales
number, a quarterly revenue substitute, or a failed metric lookup.

## Business Context

This story is authoritative for **Noavaran Amin monthly activity data**:

* `MonthlyReports`
* `MonthlyReportLineItems`
* `OutputType` 0/1/4 logic
* the Noavaran monthly activity composite snapshot
* previous fiscal-year same-month lookup

CyclicalWaves `DerivedMetrics` behavior is outside this story. If a CyclicalWaves-specific sales
snapshot requirement conflicts with this story, this story remains authoritative for Noavaran data.

## Provider Data Semantics and Unit Policy

Noavaran monthly activity is raw product/service line-item data. Monetary source values are reported
in million Rials by the provider, normalized into the platform canonical monetary unit before lookup,
and exposed through persisted `DerivedMetrics`.

The query path must remain read-only and deterministic:

* do not aggregate `MonthlyReportLineItems` at AI query time;
* read only persisted `DerivedMetrics`;
* select previous fiscal-year same-month sales from persisted `OutputType=0` single-month sales for
  the same company and Shamsi month;
* never substitute quarterly `REVENUE` for a monthly sales question.

## Shared Monthly Sales Routing Rule

The following user intents are owned by the monthly-sales workflow and must resolve to
`MONTHLY_SALES`, not quarterly `REVENUE`: `فروش`, `آخرین فروش`, `فروش ماه`, `فروش ماهانه`,
`فروش این ماه`, `فروش YTD`, `متوسط فروش 12 ماهه`, and `متوسط فروش ۱۲ ماهه`.

`REVENUE` is selected only when the user explicitly asks for revenue, quarterly revenue/sales,
`درآمد فصلی`, or `فروش فصلی`.

## Scope

### Included

* Symbol lookup support for Noavaran monthly sales queries.
* Composite alias normalization.
* Persisted monthly sales lookup metrics.
* Previous fiscal-year same-month comparison support.
* Rich sales response rendering.
* Regression test coverage.

### Excluded

* Live aggregation of `MonthlyReportLineItems`.
* Changes to CyclicalWaves sales metrics.
* Changes to scanner behavior.
* Changes to quarterly revenue metrics.
* Changes to non-Noavaran providers.

## Acceptance Criteria

### Alias Resolution

* `آخرین فروش غگلپا چقدر است؟` resolves to the monthly sales lookup workflow.
* Composite metric expressions such as `آخرین فروش / sales / revenue` do not break metric
  resolution.
* User-language aliases take precedence over translated aliases.

### Company Resolution

* Company lookup continues to use `Companies.ExternalCompanyId`.
* No lookup path depends on the legacy `Symbols` table.

### Persisted Sales Facts

The Noavaran composite snapshot exposes these persisted facts:

| Display fact | Source |
| --- | --- |
| Latest Monthly Sales | `MONTHLY_SALES`, `OutputType=0` |
| Same Month Previous Fiscal Year | persisted prior-year `MONTHLY_SALES`, `OutputType=0` |
| Fiscal Year To Date Sales | `MONTHLY_SALES_YTD`, `OutputType=1` |
| Fiscal Year To Previous Month Sales | `MONTHLY_SALES_YTD_PREVIOUS_MONTH`, `OutputType=4` |

### Response Composition

For Noavaran latest/monthly sales questions, the table columns are:

* فروش ماهانه
* فروش ماه مشابه دوره قبل
* فروش YTD
* فروش YTD تا ماه قبل

The previous fiscal-year same-month cell is calculated by finding the latest `MONTHLY_SALES`
period, subtracting one Persian year from that period, and reading the persisted `MONTHLY_SALES`
value for that prior-year month. If the row is missing, the cell is Missing/null.

Monthly sales monetary values are displayed in **million Rials** with the visible unit note:

```text
Unit: million Rials
```

Only monthly-sales monetary columns use this display conversion. Prices, percentages, ratios,
quantities, and non-sales metrics keep their existing display units. Whole displayed values have no
`.00` suffix.

Monthly production/sales lookup responses must omit market-price context. When the user asks for
latest sales, monthly sales, sales quantity/rate, monthly production, or the Noavaran composite
monthly-sales snapshot, the response must not include `LATEST_PRICE`, `DAILY_CHANGE_PCT`,
`آخرین قیمت`, or `درصد تغییر آخرین قیمت`. This rule is specific to production/sales answers and
does not remove market quote columns from valuation, screening, price, ratio, or market-statistic
questions.

For a monthly-sales snapshot with at least one non-missing monetary sales cell, every final
user-visible narrative or composed text field must be either empty/null or exactly:

```text
Unit: million Rials
```

This includes immediate API response fields and persisted/reloaded chat DTO fields. No LLM-authored
explanatory prose, clarification suggestion, fallback text, report-type suggestion, or false
"value did not return" language may appear when the table has valid sales values.

Frontend rendering treats `Unit: million Rials` as a technical backend compatibility value. In the
Persian chat UI, it is localized to `واحد: میلیون ریال`, rendered as small muted table metadata at
the table container's top-left, and never displayed as standalone assistant paragraph text.

### Regression Coverage

Tests must verify:

* composite alias parsing;
* monthly sales lookup resolution;
* previous fiscal-year comparison lookup;
* persistence-backed retrieval;
* no dependency on `Symbols`;
* no live aggregation during query execution;
* Noavaran monthly-sales table values are rendered in million Rials with a unit note;
* the Noavaran default table contains `فروش ماهانه`, `فروش ماه مشابه دوره قبل`, `فروش YTD`,
  and `فروش YTD تا ماه قبل`;
* monthly production/sales lookup tables do not include latest price or daily price-change columns
  (`آخرین قیمت`, `درصد تغییر آخرین قیمت`, `LATEST_PRICE`, `DAILY_CHANGE_PCT`);
* monthly sales monetary formatted values do not include redundant `.00` decimal suffixes;
* monthly-sales snapshot narrative fields in the actual API/chat DTOs are empty/null or exactly the
  unit note when data is present;
* regression coverage for `آخرین فروش کچاد؟` proves that a valid sales table never appears beside
  missing-data prose or report-type suggestions;
* frontend regression coverage for `آخرین فروش کچاد چقدر بوده؟` proves the UI shows
  `واحد: میلیون ریال`, does not show `Unit: million Rials`, and renders the unit label inside the
  table metadata area rather than as a standalone assistant paragraph.
