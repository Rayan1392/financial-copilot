# Bug: Symbol Lookup Rejects Composite Monthly Sales Metric Term

## Summary

The query `آخرین فروش غگلپا چقدر است؟` should be handled as a symbol metric lookup for
`MONTHLY_SALES`, but the AI response reports:

```text
Metric term 'آخرین فروش / sales / revenue' is not recognized in the supported catalog.
```

This is a false negative. The semantic catalog already supports `آخرین فروش` as an alias for
`MONTHLY_SALES`, and the downstream company/metric data exists for `غگلپا`.

There is also a broader product gap: when the user asks for "latest sales", the answer should not
return only one sales number. It should report the Noavaran Amin monthly-activity sales set:

- آخرین فروش ماهانه / فقط یک ماه آخر (`OutputType = 0`)
- same reporting month in the previous fiscal year, when the persisted prior-year monthly row exists
- جمع فروش از ابتدای سال مالی تا کنون (`OutputType = 1`)
- جمع فروش از ابتدای سال مالی تا ماه گذشته (`OutputType = 4`)

These values must be precomputed and persisted before the user asks. The query path should only
find and place the already-stored facts; it must not perform live summation over product/service
line items during answer generation.

## User Impact

Users asking for the latest sales value of a known symbol receive a zero-confidence clarification
instead of a deterministic metric table, even when the company and metric rows are present in the
database.

## Expected Behavior

For:

```text
آخرین فروش غگلپا چقدر است؟
```

The system should:

1. Detect `SymbolLookup` / financial metric intent.
2. Extract symbol name `غگلپا`.
3. Resolve metric term `آخرین فروش` to the Noavaran monthly-activity sales intent.
4. Resolve `غگلپا` through `Companies`.
5. Read the latest Noavaran monthly-activity sales data for the resolved `ExternalCompanyId`.
6. Return all required sales figures for the latest available reporting month:
   - latest monthly sales / one-month sales (`OutputType = 0`);
   - same reporting month in the previous fiscal year, using the persisted prior-year
     `OutputType = 0` aggregate when available;
   - cumulative sales from fiscal-year start to current month (`OutputType = 1`);
   - cumulative sales from fiscal-year start to previous month (`OutputType = 4`).
7. Include period/source evidence, confidence, and freshness metadata.

## Actual Behavior

The symbol lookup parser/metric resolver receives this composite metric term:

```text
آخرین فروش / sales / revenue
```

The resolver does exact alias matching, so the composite expression does not match the existing
`آخرین فروش -> MONTHLY_SALES` alias.

## Root Cause

The failure occurs before company resolution and before any `DerivedMetrics` query:

```text
POST /api/ai/v1/query
  -> LlmSymbolLookupParser
  -> IMetricAliasResolver
  -> NotFound for "آخرین فروش / sales / revenue"
```

`LlmSymbolLookupParser` is instructed to return the metric term exactly as written by the user, but
the observed structured output enriched the user phrase into a slash-separated composite term:

```text
آخرین فروش / sales / revenue
```

`MetricAliasResolver` and `CompositeMetricAliasResolver` treat the expression as a single alias.
They do not split slash-separated alternatives, and no dynamic alias currently exists for this full
composite string.

## Evidence

Static semantic catalog coverage:

- `آخرین فروش` maps to `MONTHLY_SALES`.
- `آخرین فروش ماهانه` maps to `MONTHLY_SALES`.
- `فروش ماهانه` maps to `MONTHLY_SALES`.
- `sales` maps to `REVENUE`, which is a different metric family and should not override the
  Persian monthly-sales phrase in this query.

Local database check on 2026-06-16:

- `Companies` resolves `غگلپا` to `ExternalCompanyId = 13150`.
- `DerivedMetrics` contains `MONTHLY_SALES` rows for that external company id.
- The latest observed `MONTHLY_SALES` period was `2026-05-31`.

Therefore the issue is not caused by story 068 company-first resolution, missing company data, or
missing monthly sales data.

## Related Noavaran Monthly Sales Specs

The relevant existing specs are:

- `specs/057-nadpco-monthly-activity-freshness-and-sales-lookup`
- `specs/059-monthly-activity-output-type-segmentation`

Spec 057 defines the Noavaran current-API monthly activity path:

- source endpoints:
  - `POST api/v2/MonthlyActivity/ProductSales`
  - `POST api/v3/MonthlyActivity/ServiceSales`
- company ids come from `Companies.ExternalCompanyId` for Noavaran companies;
- normalized headers are stored in `MonthlyReports`;
- normalized product/service facts are stored in `MonthlyReportLineItems`;
- `MonthlySalesMetricInputSource` aggregates `MonthlyReportLineItems.SalesAmount` into
  `MONTHLY_SALES`;
- symbol lookup should answer `آخرین فروش ...` from Noavaran monthly activity, not quarterly
  statement `REVENUE`.

Spec 059 adds `MonthlyReports.OutputType` because Noavaran `ProductSales` supports distinct
period-aggregation views:

| OutputType | Meaning for sales lookup |
|---|---|
| 0 | single-month period / latest monthly sales |
| 1 | from fiscal year start to current month / cumulative sales from beginning of period |
| 2 | adjustments |
| 3 | from fiscal year start to previous month, adjusted |
| 4 | from fiscal year start to previous month |

For the product requirement in this bug, "آخرین فروش" should be treated as a compound Noavaran
monthly-sales answer, not just a single `MONTHLY_SALES` cell.

If UI/product wording uses "مدت مشابه دوره قبل", implementation must distinguish it from
`OutputType = 4`. Based on the current requested sales set, the required persisted output types are
`0`, `1`, and `4`, plus a prior fiscal-year comparison that reuses the persisted `OutputType = 0`
single-month aggregate from the same reporting month one fiscal year earlier.

The normalized model supports this lookup because Noavaran monthly activity rows are stored with
`MonthlyReports.ExternalCompanyId`, `PeriodStart` / `PeriodEnd`, and `OutputType`. Example: if the
latest available sales month for a company is Ordibehesht 1405, the comparable prior fiscal-year
month is Ordibehesht 1404 for the same `ExternalCompanyId` and `OutputType = 0`, provided that row
exists in the backfilled/current dataset. If the prior-year row is outside coverage or missing, the
answer should show that comparable value as missing rather than calculating or fabricating it.

## Precompute Requirement

Noavaran reports sales as multiple detailed product/service line items. There is no single
company-level sales total in the raw item list. The platform should not calculate this total live
when the user asks a question.

Required behavior:

1. During ingestion/recalculation, aggregate `MonthlyReportLineItems.SalesAmount` per company,
   reporting month, and sales output type.
2. Persist the aggregated result in a lookup-ready table, preferably `DerivedMetrics`, with enough
   metric-code or policy/source metadata to distinguish output types.
3. Persist the same-month prior fiscal-year comparison as a lookup-ready fact, or make it
   resolvable from persisted per-month `DerivedMetrics` rows without re-summing line items.
4. At query time, symbol lookup should only resolve the company and read the persisted aggregate
   values. It should not sum `MonthlyReportLineItems` in the AI query path.

This keeps the answer path deterministic, fast, auditable, and consistent with the existing
scanner/symbol-lookup architecture.

## Current Code Behavior

Current read behavior does not satisfy the three-value scenario above.

Observed implementation shape:

- `SymbolLookupToolAdapter` sends the raw user query to `ISymbolLookupParser`, then passes resolved
  `(symbolName, metricCode)` pairs to `ISymbolMetricLookupService`.
- `EfCoreSymbolMetricLookupService` resolves the company through `ICompanyResolverService`, then
  reads `DerivedMetrics` by `ExternalCompanyId` and requested `MetricCode`.
- For metric columns, `BuildPersistedMetricCell` returns exactly one latest
  `DerivedMetrics` value per `(ExternalCompanyId, MetricCode)`.
- It does not directly query `MonthlyReports` / `MonthlyReportLineItems` at answer time.
- It does not assemble multiple Noavaran output types into a single "latest sales" answer.

Current monthly input behavior:

- `MonthlyReportAggregateInputSource` can filter `MonthlyReports.OutputType`.
- `MonthlySalesMetricInputSource`, `MonthlySalesQuantityMetricInputSource`,
  `MonthlyProductionQuantityMetricInputSource`, and `MonthlySalesRateMetricInputSource` currently
  use `MonthlyActivityQueryIntent.SingleMonth`, i.e. `OutputType = 0`.
- The current code comment says `OutputType=0` is the correct filter for `آخرین فروش` / latest
  sales.
- `DefaultMonthlyActivityOutputTypeResolver` exists, but its default is also `SingleMonth` unless
  the query has an explicit YTD/cumulative hint.
- `NadpcoApiDataProviderClient` does fetch all five `ProductSales` output types (`0` through `4`),
  and the normalizer stores the `OutputType` on `MonthlyReports`.
- `MetricRecalculationProcessor` has a precompute path for monthly production/sales:
  `MonthlyProductionSales` changes enqueue recalculation, input sources aggregate normalized
  monthly line items, and the engine stores results in `DerivedMetrics`.

Therefore, even after fixing the composite alias parsing bug, the current code path would only look
for the single-month `MONTHLY_SALES` value in `DerivedMetrics`. It would not report:

- `OutputType = 1` cumulative sales from the beginning of the fiscal period;
- same reporting month in the previous fiscal year from persisted `OutputType = 0` monthly sales;
- `OutputType = 4` cumulative sales from the beginning of the fiscal period to previous month;
- a grouped sales block containing all three values requested by the product behavior.

## Suggested Fix

Fix this in two layers:

1. parser/resolution hardening, so the query reaches lookup;
2. Noavaran monthly-sales answer composition, so "latest sales" returns the full required sales
   set instead of one metric cell.

Recommended implementation:

1. Harden `LlmSymbolLookupParser` so `MetricTerm` is always the shortest user-written metric phrase,
   not a translated or expanded list.
   - Add explicit examples:
     - Input: `آخرین فروش غگلپا چقدر است؟`
     - Output metric term: `آخرین فروش`
     - Output symbol name: `غگلپا`
   - Add a negative instruction: do not return values like `آخرین فروش / sales / revenue`.
2. Add a defensive normalization step before alias resolution:
   - If the LLM still returns a slash-separated metric term, split on `/`.
   - Try exact alias resolution for each segment after trimming.
   - Prefer an exact match in the user/query language before fallback languages.
   - If multiple segments resolve to different metric codes, prefer the segment that appears in the
     original user message verbatim.
3. Add regression tests for:
   - `آخرین فروش غگلپا چقدر است؟` resolves to `MONTHLY_SALES`.
   - Composite parser output `آخرین فروش / sales / revenue` still resolves to `MONTHLY_SALES` because
     `آخرین فروش` is the verbatim user-language segment.
   - `فروش غگلپا چقدر است؟` remains deterministic and does not accidentally switch to unrelated
     revenue aliases unless the product decision says `فروش` alone should mean monthly sales.
4. Add an application-level representation for a Noavaran latest-sales answer. Do not model this as
   only one scalar `MONTHLY_SALES` lookup cell. The response needs a small grouped table/block with:
   - metric label;
   - value;
   - period start/end or Shamsi month;
   - output type or derivation source;
   - provider/source evidence.
5. Precompute and persist the required Noavaran sales facts from normalized storage before query
   time:
   - latest monthly sales: `MonthlyReports.OutputType = 0`, aggregate
     `MonthlyReportLineItems.SalesAmount`;
   - same reporting month in the previous fiscal year: use the already-persisted
     `OutputType = 0` aggregate for the same company and same fiscal/Shamsi month one year earlier;
   - cumulative sales from fiscal-year start to current month: `MonthlyReports.OutputType = 1`,
     aggregate `MonthlyReportLineItems.SalesAmount`;
   - cumulative sales from fiscal-year start to previous month: `MonthlyReports.OutputType = 4`,
     aggregate `MonthlyReportLineItems.SalesAmount`.
6. Decide whether these facts should be persisted into separate `DerivedMetrics` codes or queried
   directly from normalized monthly activity. Preferred direction is separate persisted
   `DerivedMetrics` codes, because query-time aggregation is explicitly disallowed:
   - If persisted, introduce explicit metric codes such as
     `MONTHLY_SALES_SINGLE_MONTH`, `MONTHLY_SALES_YTD`, and
     `MONTHLY_SALES_YTD_PREVIOUS_MONTH`, with calculation policies and recalculation mappings.
     The same-month prior fiscal-year value can either be a separate lookup metric such as
     `MONTHLY_SALES_PRIOR_FISCAL_YEAR_SAME_MONTH` or a deterministic selection of the persisted
     prior-year `MONTHLY_SALES_SINGLE_MONTH` row.
   - If a direct normalized-table branch is still chosen, it must read precomputed aggregate rows,
     not raw line items.
   - Do not hide `OutputType` differences behind a single `MONTHLY_SALES` code.

Avoid solving this by adding only a dynamic alias for the exact composite string. That would patch
one observed LLM output but leave the parser contract violation and future composite variants
unhandled.

## Acceptance Criteria

- The query `آخرین فروش غگلپا چقدر است؟` no longer fails alias resolution.
- The response contains all three required Noavaran monthly-activity sales facts:
  - latest monthly sales / one-month sales (`OutputType = 0`);
  - same reporting month in the previous fiscal year, if persisted data exists;
  - cumulative sales from fiscal-year start to current month (`OutputType = 1`);
  - cumulative sales from fiscal-year start to previous month (`OutputType = 4`).
- The answer path does not aggregate raw `MonthlyReportLineItems` live. Aggregation happens during
  ingestion/recalculation and the query path reads persisted aggregate facts.
- The response does not contain `Metric term ... is not recognized`.
- Company resolution still uses `Companies` and `ExternalCompanyId` according to story 068.
- No lookup path reads from the legacy `Symbols` table for this scenario.
- Regression coverage proves slash-separated LLM metric expansions do not break exact known aliases.
- Regression coverage proves `آخرین فروش` does not return only the scalar `MONTHLY_SALES` cell when
  the Noavaran output-type data needed for the richer answer exists.
