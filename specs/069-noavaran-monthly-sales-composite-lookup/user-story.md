# User Story - Noavaran Monthly Sales Composite Lookup

## Story

As a FinancialCopilot user,

I want the AI assistant to correctly answer monthly sales questions such as:

* آخرین فروش غگلپا چقدر است؟
* فروش ماهانه شپدیس چقدر بوده؟
* آخرین فروش کگل را نشان بده

so that I can receive a complete Noavaran monthly sales view instead of a single sales number or a failed metric lookup.

---

## Business Context

The platform currently supports Noavaran monthly activity ingestion and precomputed monthly sales metrics.

However, the Symbol Lookup flow has two gaps:

1. Composite metric terms produced by the LLM (for example `آخرین فروش / sales / revenue`) fail alias resolution even though the semantic catalog already supports `آخرین فروش`.

2. The current lookup path only returns a single latest `MONTHLY_SALES` value while the product requirement for "latest sales" is a richer sales snapshot that includes:

   * latest monthly sales;
   * same reporting month in previous fiscal year;
   * cumulative sales from fiscal year start to current month;
   * cumulative sales from fiscal year start to previous month.

The query path must remain read-only and deterministic.

All required sales values must be precomputed and persisted before user queries are executed.

---

## Provider Data Semantics and Unit Policy

This story is specifically about Noavaran Amin monthly activity data.

- Noavaran Amin monthly activity is raw product/service line-item data.
- Monetary source values are reported in **million Rials**.
- Company-level sales facts are created by summing relevant line items per `ExternalCompanyId`,
  period, provider, and `OutputType`, then normalizing the result to the platform canonical
  monetary unit before writing lookup-ready `DerivedMetrics`.
- The composite monthly-sales lookup reads only persisted `DerivedMetrics`; it must not sum
  `MonthlyReportLineItems` at AI query time.
- Same fiscal/Shamsi month in the previous year is selected from persisted `OutputType=0`
  single-month sales for the same company.
- CyclicalWaves values are outside this story. They are provider-precomputed company metrics in
  Rials and must not receive Noavaran million-Rial conversion or line-item aggregation rules.

---

## Scope

### Included

* Symbol lookup support for Noavaran monthly sales queries.
* Composite alias normalization.
* Persisted monthly sales lookup metrics.
* Previous fiscal year same-month comparison support.
* Rich sales response rendering.
* Regression test coverage.

### Excluded

* Live aggregation of MonthlyReportLineItems.
* Changes to scanner behavior.
* Changes to quarterly revenue metrics.
* Changes to non-Noavaran providers.

---

## Acceptance Criteria

### Alias Resolution

* Query:

```text
آخرین فروش غگلپا چقدر است؟
```

must successfully resolve to the monthly sales lookup workflow.

* Composite metric expressions such as:

```text
آخرین فروش / sales / revenue
```

must not break metric resolution.

* User-language aliases take precedence over translated aliases.

### Company Resolution

* Company lookup must continue to use:

  * Companies
  * ExternalCompanyId

* No lookup path may depend on the legacy Symbols table.

### Persisted Sales Facts

The platform must persist and expose:

| Metric                              | Source                  |
| ----------------------------------- | ----------------------- |
| Latest Monthly Sales                | OutputType=0            |
| Same Month Previous Fiscal Year     | Prior Year OutputType=0 |
| Fiscal Year To Date Sales           | OutputType=1            |
| Fiscal Year To Previous Month Sales | OutputType=4            |

### Query-Time Behavior

* Query execution must not aggregate MonthlyReportLineItems.
* Query execution must only read persisted facts.

### Response Composition

The AI response must include all available sales facts for the latest reporting month:

* Latest Monthly Sales
* Same Month Previous Fiscal Year
* Fiscal Year To Date Sales
* Fiscal Year To Previous Month Sales

along with:

* reporting period
* source metadata
* confidence
* freshness indicators

Monthly sales monetary values must be displayed to users in **million Rials** even though the
persisted canonical value is stored in Rials. The answer must include a visible unit note above
the table, for example:

```text
Unit: million Rials
```

Only monthly-sales monetary columns use this display conversion. Prices, percentages, ratios,
quantities, and non-sales metrics keep their existing display units.
After conversion to million Rials, monthly-sales monetary cells must follow the shared financial
number display policy: whole displayed values have no `.00` suffix, and large sales amounts are
shown as grouped whole numbers unless a non-zero fractional part is intentionally meaningful.

Monthly production/sales lookup responses must not include market-price context. When the user
asks for latest sales, monthly sales, sales quantity/rate, monthly production, or the composite
monthly-sales snapshot, the response must omit `LATEST_PRICE` and `DAILY_CHANGE_PCT`; those
columns remain available for valuation, ratio, and non-monthly metric lookups.

### Regression Coverage

Tests must verify:

* Composite alias parsing.
* Monthly sales lookup resolution.
* Previous fiscal year comparison lookup.
* Persistence-backed retrieval.
* No dependency on Symbols.
* No live aggregation during query execution.
* Monthly sales table values are rendered in million Rials with a unit note above the table.
* Monthly production/sales lookup tables do not include latest price or daily price-change columns.
* Monthly sales monetary formatted values do not include redundant `.00` decimal suffixes.
