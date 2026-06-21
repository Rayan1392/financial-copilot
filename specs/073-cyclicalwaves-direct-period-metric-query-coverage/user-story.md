# User Story - CyclicalWaves Direct Period Metric Query Coverage

## Story

As a FinancialCopilot user,
I want to ask direct AI questions about CyclicalWaves snapshot fields such as latest-quarter margins,
previous-quarter margins, same-quarter last-year margins, latest/prior/same-month sales, average
12-month sales, P/E, and P/S,
so that the answer returns the persisted provider facts with the correct Persian labels, period
selection, units, provenance, and confidence instead of falling back to missing data or a generic
latest metric lookup.

## Background

Specs `020` and `071` persist the CyclicalWaves snapshot fields into normalized rows and
`DerivedMetrics`. Specs `045` and `072` support direct symbol metric lookup and centralized alias
routing. The remaining gap is that direct user questions may specify a relative period embedded in
the provider field name, for example:

* `حاشیه سود خالص فصل قبل کچاد`
* `حاشیه سود عملیاتی فصل مشابه سال قبل شغدیر`
* `فروش ماه قبل کچاد`
* `متوسط فروش ۱۲ ماهه سال قبل کگل`

Without explicit period-aware routing, the generic lookup path may resolve only the latest value,
choose the wrong renderer, or mark the metric as unsupported even though the value is already
persisted.

## Provider Scope

This story applies to CyclicalWaves-origin persisted observations only. It must not change
Noavaran monthly-activity aggregation, Noavaran unit normalization, CodalDB calculations, or market
quote ingestion.

## Required Direct-Question Coverage Matrix

| Provider field | Canonical metric | Required period selector | Persian display label |
| --- | --- | --- | --- |
| `last_quarter_net_profit_margin` | `NET_PROFIT_MARGIN` | Q0 / latest quarter | حاشیه سود خالص آخرین فصل |
| `penultimate_quarter_net_profit_margin` | `NET_PROFIT_MARGIN` | Q1 / previous quarter | حاشیه سود خالص فصل قبل |
| `last_year_same_quarter_net_profit_margin` | `NET_PROFIT_MARGIN` | Q4 / same quarter last year | حاشیه سود خالص فصل مشابه سال قبل |
| `last_quarter_gross_profit_margin` | `GROSS_PROFIT_MARGIN` | Q0 / latest quarter | حاشیه سود ناخالص آخرین فصل |
| `penultimate_quarter_gross_profit_margin` | `GROSS_PROFIT_MARGIN` | Q1 / previous quarter | حاشیه سود ناخالص فصل قبل |
| `last_year_same_quarter_gross_profit_margin` | `GROSS_PROFIT_MARGIN` | Q4 / same quarter last year | حاشیه سود ناخالص فصل مشابه سال قبل |
| `last_quarter_operating_profit_margin` | `OPERATING_PROFIT_MARGIN` | Q0 / latest quarter | حاشیه سود عملیاتی آخرین فصل |
| `penultimate_quarter_operating_profit_margin` | `OPERATING_PROFIT_MARGIN` | Q1 / previous quarter | حاشیه سود عملیاتی فصل قبل |
| `last_year_same_quarter_operating_profit_margin` | `OPERATING_PROFIT_MARGIN` | Q4 / same quarter last year | حاشیه سود عملیاتی فصل مشابه سال قبل |
| `average_12_month_sale` | `AVG_12M_MONTHLY_SALES` | M0 / latest month snapshot | متوسط فروش ۱۲ ماهه |
| `last_year_average_12_month_sale` | `AVG_12M_MONTHLY_SALES` | M12 / same month last year snapshot | متوسط فروش ۱۲ ماهه سال قبل |
| `last_month_sale` | `MONTHLY_SALES` | M0 / latest month | فروش آخرین ماه |
| `penultimate_month_sale` | `MONTHLY_SALES` | M1 / previous month | فروش ماه قبل |
| `last_year_same_month_sale` | `MONTHLY_SALES` | M12 / same month last year | فروش ماه مشابه سال قبل |
| `pe` | `PE_TTM` | latest persisted valuation ratio | نسبت قیمت به سود |
| `ps` | `PS_TTM` | latest persisted valuation ratio | نسبت قیمت به فروش |

## Acceptance Criteria

### Period-Aware Metric Resolution

- The symbol metric lookup parser returns a structured period selector in addition to
  `symbolName` and `metricCode` when the user asks for a relative period such as latest quarter,
  previous quarter, same quarter last year, latest month, previous month, same month last year, or
  last-year average.
- The lookup service resolves CyclicalWaves period selectors against persisted `DerivedMetrics`
  using `ExternalCompanyId`, `MetricCode`, `PeriodType`, `PeriodEnd`, and CyclicalWaves source
  evidence. It must not query the legacy `Symbols` table and must not recalculate provider
  passthrough values.
- When no explicit period selector is present for a margin metric, the lookup returns the latest
  persisted quarter (Q0). When no explicit selector is present for `MONTHLY_SALES` or
  `AVG_12M_MONTHLY_SALES`, it returns the latest persisted month snapshot (M0).
- Explicit period selectors override the default latest-period behavior. For example,
  `حاشیه سود خالص فصل قبل کچاد` must return the Q1 `NET_PROFIT_MARGIN`, not Q0.
- `متوسط فروش ۱۲ ماهه سال قبل` must return the M12 `AVG_12M_MONTHLY_SALES` value sourced from
  `last_year_average_12_month_sale`, not the latest M0 average and not a derived calculation from
  monthly rows.
- `فروش ماه مشابه سال قبل` must return the persisted M12 `MONTHLY_SALES` value when the
  CyclicalWaves M12 snapshot exists. It may use the existing same-period lookup fallback only when
  provider evidence does not contain an explicit M12 row.

### Alias And Intent Routing

- Persian and English aliases are registered for all rows in the coverage matrix above.
- Aliases that include period words such as `فصل قبل`, `دوره قبل`, `فصل مشابه سال قبل`,
  `ماه قبل`, `ماه مشابه سال قبل`, and `سال قبل` must be handled deterministically before the LLM
  fallback can downgrade the request to a generic latest lookup.
- `PE`, `P/E`, `پی به ای`, `قیمت به سود`, and `نسبت قیمت به سود` continue to resolve to `PE_TTM`.
- `PS`, `P/S`, `پی به اس`, `قیمت به فروش`, and `نسبت قیمت به فروش` resolve to `PS_TTM`.
- Monthly sales questions continue to use the monthly-sales renderer when the user asks for a sales
  snapshot; explicit single-metric questions such as `فروش ماه قبل کچاد چقدر بود؟` may use the
  generic metric renderer if the response contains only that requested metric.

### Rendering And Labels

- User-facing table headers and prose use the Persian labels in the coverage matrix. Internal codes
  such as `NET_PROFIT_MARGIN`, `AVG_12M_MONTHLY_SALES`, `PE_TTM`, `PS_TTM`, Q0, Q1, Q4, M0, M1,
  and M12 must not appear as display names in Persian-facing answers.
- Margin values are displayed as percentages/ratios with trimmed insignificant trailing zeros.
- Sales and average-sales values are displayed in the configured monetary display unit for the
  renderer. The persisted CyclicalWaves value remains canonical Rials with CyclicalWaves
  passthrough source evidence.
- Monthly production/sales answers must continue to omit market quote columns (`LATEST_PRICE`,
  `DAILY_CHANGE_PCT`, `آخرین قیمت`, `درصد تغییر آخرین قیمت`).
- Valuation-ratio point lookups for PE/PS may include quote context only under the existing
  `SymbolLookup` quote-enrichment rules from spec `045`; scanner/filter outputs must not inherit
  point-lookup quote enrichment.

### Missing Data And Confidence

- If the requested period-specific value is absent, the cell is returned as `Missing` with a
  data-coverage warning. The service must not silently substitute another period.
- Confidence scoring considers successful symbol resolution, successful alias resolution,
  period-selector match, data freshness, and consistency between structured table values and
  generated text.
- Missing period-specific data is recorded through the existing missing-answer feedback pipeline
  as `DataCoverageGap`; unsupported wording is recorded as `ParserLimitation`.

## Regression Coverage

Tests must prove that the following direct AI queries route to the correct metric and period and
return the seeded persisted value:

- `حاشیه سود خالص آخرین فصل کچاد؟` -> `NET_PROFIT_MARGIN`, Q0
- `حاشیه سود خالص فصل قبل کچاد؟` -> `NET_PROFIT_MARGIN`, Q1
- `حاشیه سود خالص فصل مشابه سال قبل کچاد؟` -> `NET_PROFIT_MARGIN`, Q4
- `حاشیه سود ناخالص فصل قبل کچاد؟` -> `GROSS_PROFIT_MARGIN`, Q1
- `حاشیه سود عملیاتی فصل مشابه سال قبل کچاد؟` -> `OPERATING_PROFIT_MARGIN`, Q4
- `متوسط فروش ۱۲ ماهه کچاد؟` -> `AVG_12M_MONTHLY_SALES`, M0
- `متوسط فروش ۱۲ ماهه سال قبل کچاد؟` -> `AVG_12M_MONTHLY_SALES`, M12
- `فروش آخرین ماه کچاد؟` -> `MONTHLY_SALES`, M0
- `فروش ماه قبل کچاد؟` -> `MONTHLY_SALES`, M1
- `فروش ماه مشابه سال قبل کچاد؟` -> `MONTHLY_SALES`, M12
- `pe کچاد؟` -> `PE_TTM`, latest valuation ratio
- `ps کچاد؟` -> `PS_TTM`, latest valuation ratio

Regression tests must also prove that:

- The legacy `Symbols` table is not used.
- Noavaran monthly-sales behavior from specs `057`, `069`, and `070` remains unchanged.
- Scanner PE/PS filtering behavior remains governed by spec `008` and does not receive extra quote
  columns from this point-lookup feature.
- Internal metric codes are absent from Persian display labels.
