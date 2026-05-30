# Tasks

## Infrastructure — Generic input source

- [ ] Add a generic `LineItemMetricInputSource` (`INormalizedMetricInputSource`) keyed by
      `MetricCode` that supplies any mapped income/balance line-item observation to the engine,
      provider-agnostically. Register one instance per source metric
      (`REVENUE`, `GROSS_PROFIT`, `OPERATING_PROFIT`, `EPS`, `TOTAL_EQUITY`, `FINANCE_COSTS`,
      `INCOME_TAX`, plus existing `NET_PROFIT`). Preserve/subsume
      `NetProfitMetricInputSource`/`MonthlySalesMetricInputSource` without regression.

## Infrastructure — Cumulative → discrete quarter

- [ ] Add `CodalDiscreteQuarterDeriver` — converts cumulative income observations
      (`cumulative(n) − cumulative(n−1)` within a fiscal year; Q1 = 3-month as-is) into discrete
      quarterly observations for QoQ calculators. YoY uses matching cumulative periods directly.

## Domain / Semantic — Growth + EBIT definitions (015 catalog)

- [ ] Register `FinancialMetricDefinition` + bilingual aliases for:
      `REVENUE_GROWTH_YOY/QOQ`, `GROSS_PROFIT_GROWTH_YOY/QOQ`,
      `OPERATING_PROFIT_GROWTH_YOY/QOQ`, `EPS_GROWTH_YOY/QOQ`, `EBIT`, `EBIT_GROWTH_YOY/QOQ`,
      `EQUITY_GROWTH_YOY/QOQ`. (`NET_PROFIT_GROWTH_*`, `MONTHLY_SALES_GROWTH_*` already exist.)
- [ ] Register `MetricCalculationPolicy` for each (e.g. `yoy-quarterly-v1`, `qoq-quarterly-v1`,
      `ebit-composite-v1`) with dependencies and `MissingDataPolicy` (EBIT documents the
      `OPERATING_PROFIT` proxy fallback).
- [ ] Register vendor-precomputed growth-ratio metric codes + aliases for ingestion via the `025`
      pattern: `SALES_GROWTH_RATE` (6902), `NET_PROFIT_GROWTH_RATE` (6903),
      `EQUITY_GROWTH_RATE` (8092), `TOTAL_ASSETS_GROWTH_RATE` (6904),
      `TOTAL_DEBT_GROWTH_RATE` (8091), `TANGIBLE_FIXED_ASSETS_GROWTH_RATE` (6905). Add their ids
      to `CodalDbRatioItemMap`.

## Domain — Calculators

- [ ] Register `PercentageGrowthMetricCalculator` instances for each engine-derived growth
      metric (target code + source code + YoY/QoQ offset).
- [ ] Add `AdditiveCompositeMetricCalculator` (if no equivalent exists) and register `EBIT`
      = `NET_PROFIT + FINANCE_COSTS + INCOME_TAX`.
- [ ] Confirm growth calculators consume discrete-quarter observations for QoQ (via
      `CodalDiscreteQuarterDeriver`) and cumulative for YoY.

## Tests

- [ ] `CodalDiscreteQuarterDeriverTests` (unit, ~5 tests): Q1/Q2/Q3/Q4 discrete values from
      cumulative sequence; fiscal-year boundary handled.
- [ ] `EbitCompositeCalculatorTests` (unit, ~4 tests): EBIT = NET_PROFIT + FINANCE_COSTS +
      INCOME_TAX; proxy fallback to OPERATING_PROFIT when components missing; missing-data policy.
- [ ] Growth calculator tests (unit, ~8 tests): YoY/QoQ for revenue, EPS, operating profit,
      gross profit, EBIT, equity; zero/missing denominator → missing value + warning, no throw.
- [ ] Integration test: after ingest + recalc, scanning by `EPS_GROWTH_YOY > X` and
      `SALES_GROWTH_RATE` returns Codal companies; engine-derived vs vendor-precomputed growth are
      distinct rows with distinct policy versions.
