# User Story — CodalDB Derived Growth Metrics

> Depends on `023` (source line items), `025` (precomputed-ratio ingestion pattern). Schema
> reference: [docs/codaldb-datasource.md](../../docs/codaldb-datasource.md).

## Story

As a scanner user,
I want growth metrics (year-over-year and quarter-over-quarter) for every fundamental item Codal
provides — revenue, net profit, operating profit, gross profit, EPS, EBIT, and equity — plus the
growth ratios Codal already computes,
so that I can scan for companies by growth (e.g. "EPS growth > 30% YoY") with explainable,
versioned results.

## Context

Growth comes from two complementary sources, kept distinct:

1. **Engine-derived growth** — computed deterministically by the `006` engine
   (`PercentageGrowthMetricCalculator`) from the source line items ingested in `023`. The engine
   already produces `NET_PROFIT_GROWTH_YOY/QOQ` and `MONTHLY_SALES_GROWTH_YOY/MOM`; this story
   adds the remaining fundamentals.
2. **Vendor-precomputed growth ratios** — present in `FinancialRatios` and ingested exactly like
   `025` (as `DerivedMetricRow`s with `CalculationPolicyVersion = "codal-ratio-source-v1"`):
   Sales growth (6902), Net profit growth (6903), Equity growth (8092), Total assets growth
   (6904), Total debt growth (8091), Tangible fixed assets growth (6905).

These remain **separate metric codes** so a user/explanation can distinguish a platform-computed
growth from a vendor-supplied one.

## Cumulative-period handling

Codal statements are **cumulative** (3/6/9/12-month). This story defines comparison semantics:

- **YoY** compares the same cumulative period one fiscal year earlier (e.g. 12-month vs prior-
  year 12-month, 6-month-cumulative vs prior-year 6-month-cumulative) — directly comparable.
- **QoQ** requires **discrete-quarter** values, derived as `cumulative(n) − cumulative(n−1)`
  within the same fiscal year (Q1 = 3-month cumulative as-is). A `CodalDiscreteQuarterDeriver`
  produces discrete quarterly source observations that the QoQ calculator consumes.

## Engine-derived growth metrics (new)

| Source metric | YoY code | QoQ code |
|---|---|---|
| `REVENUE` | `REVENUE_GROWTH_YOY` | `REVENUE_GROWTH_QOQ` |
| `NET_PROFIT` | `NET_PROFIT_GROWTH_YOY` *(exists)* | `NET_PROFIT_GROWTH_QOQ` *(exists)* |
| `GROSS_PROFIT` | `GROSS_PROFIT_GROWTH_YOY` | `GROSS_PROFIT_GROWTH_QOQ` |
| `OPERATING_PROFIT` | `OPERATING_PROFIT_GROWTH_YOY` | `OPERATING_PROFIT_GROWTH_QOQ` |
| `EPS` | `EPS_GROWTH_YOY` | `EPS_GROWTH_QOQ` |
| `EBIT` *(derived, see below)* | `EBIT_GROWTH_YOY` | `EBIT_GROWTH_QOQ` |
| `TOTAL_EQUITY` | `EQUITY_GROWTH_YOY` | `EQUITY_GROWTH_QOQ` |

**`EBIT`** is itself a derived source-composite metric (not a Codal line item):
recommended `EBIT = NET_PROFIT + FINANCE_COSTS + INCOME_TAX` (inputs ingested in `023`), with
`OPERATING_PROFIT` (140) as a documented proxy fallback when components are missing.

## Acceptance Criteria

- Each new growth metric is a registered `FinancialMetricDefinition` (with bilingual aliases) and
  a registered `IFinancialMetricCalculator` (reusing `PercentageGrowthMetricCalculator` with the
  appropriate source `MetricCode` and comparison offset), plus a `MetricCalculationPolicy`
  (`CalculationPolicyVersion` e.g. `"yoy-quarterly-v1"` / `"qoq-quarterly-v1"`). No bespoke
  per-metric calculation classes are added where the existing generic calculator suffices.
- `EBIT` is registered with an additive composite calculator
  (`NET_PROFIT + FINANCE_COSTS + INCOME_TAX`); its `MissingDataPolicy` documents the
  `OPERATING_PROFIT` proxy fallback. `EBIT_GROWTH_*` depend on `EBIT`.
- A `CodalDiscreteQuarterDeriver` converts cumulative income observations to discrete-quarter
  observations for QoQ; YoY uses matching cumulative periods directly. Comparison semantics are
  unit-tested against known Codal-style cumulative sequences.
- Source observations feed calculators provider-agnostically: a **generic
  `LineItemMetricInputSource`** (keyed by `MetricCode`) supplies any mapped income/balance metric
  to the engine, replacing the need for one input-source class per metric (DRY). Existing
  `NetProfitMetricInputSource` / `MonthlySalesMetricInputSource` behavior is preserved or
  subsumed without regression.
- The vendor-precomputed growth ratios (6902, 6903, 8092, 6904, 8091, 6905) are ingested as
  scannable `DerivedMetricRow`s using the `025` mechanism, under their own metric codes
  (`SALES_GROWTH_RATE`, `NET_PROFIT_GROWTH_RATE`, `EQUITY_GROWTH_RATE`,
  `TOTAL_ASSETS_GROWTH_RATE`, `TOTAL_DEBT_GROWTH_RATE`, `TANGIBLE_FIXED_ASSETS_GROWTH_RATE`),
  marked vendor-precomputed.
- All growth metrics are scannable with **no scanner-engine code changes**; explanations cite the
  metric/policy version and (for vendor ratios) the precomputed source.
- Re-running the recalculation after ingestion is idempotent on the `DerivedMetricRow` unique key;
  missing/zero-denominator periods yield a missing value with a warning (existing engine
  behavior), never an exception.

## Technical Notes

- Reuse `PercentageGrowthMetricCalculator(targetCode, sourceCode)` and
  `TrailingTwelveMonthSumMetricCalculator` where applicable; only `EBIT`'s additive composite may
  need a small new calculator (`AdditiveCompositeMetricCalculator`) if none exists.
- Keep engine-derived growth and vendor-precomputed growth as distinct codes; do not let one
  overwrite the other (distinct `CalculationPolicyVersion`).
- Confirm the existing growth calculators' period-offset logic matches Codal cumulative semantics
  before reuse; if it assumes discrete quarters, route Codal income through
  `CodalDiscreteQuarterDeriver` first.

## Dependencies

- `023` (source line items + inputs), `025` (vendor-precomputed ingestion pattern),
  `006` (calculator strategies + recalculation), `015` (metric definitions/aliases),
  `008`/`009` (scanner + explanation).
