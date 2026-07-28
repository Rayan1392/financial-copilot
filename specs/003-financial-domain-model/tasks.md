# Tasks

- Implement domain entities/value objects.
- Implement an extensible metric identity/registry foundation; do not constrain the platform to a closed enum or central formula switch statement.
- Implement period comparison policy.
- Define calculation policy primitives/value objects needed by derived-metric services.
- Align `MetricCode` and calculation-policy primitives with semantic definition/version contracts owned by `015-financial-semantic-layer`.
- Add tests for financial domain invariants and period/comparison policy rules.

## Implementation Status - 2026-05-26

Implemented in this story:

- Added normalized Domain concepts for `Symbol`, `Company`, `Industry`, `FinancialStatement`, `FinancialStatementLineItem`, `MonthlyReport`, `MonthlyReportLineItem`, `MarketSnapshot`, and `DerivedMetric`.
- Added provider-neutral external references and source evidence metadata while keeping internal GUID identities authoritative.
- Added `SymbolCode`, `FiscalPeriod`, `MetricCode`, `MetricVersion`, `CalculationPolicyVersion`, and `Percentage` value objects plus supported period types for monthly, 3/6/9/12-month, latest month, latest quarter, and TTM use.
- Added deterministic `PeriodComparisonPolicy` for YoY and monthly MoM comparison, including validation that latest selectors are resolved before comparison.
- Added extensible metric identity registration (`IMetricIdentityRegistry` / `MetricIdentityRegistry`) and policy inputs (`MetricCalculationPolicy`, requirements, units, missing-data policy) without a closed metric enum or calculation dispatch switch.
- Added semantic-version and calculation-policy-version evidence to `DerivedMetric`, aligned for extension by `015-financial-semantic-layer` and deterministic consumption by `006-derived-metrics-engine`.
- Added missing/stale observation warnings and nullable values for incomplete or aged report, quote, and derived-metric data.
- Added unit tests for financial entity invariants, source/external identity handling, missing/stale observations, version evidence, all supported period representations, YoY/MoM rules, extensible metric registration, and calculation-policy inputs.

Explicitly deferred to dependent stories:

- `015-financial-semantic-layer` owns full semantic definitions, bilingual alias resolution, ambiguity handling, dependency graph/catalog governance, and calculator registration contracts.
- `005-data-ingestion-and-normalization` owns persistence and provider-to-domain normalization execution.
- `006-derived-metrics-engine` owns formula implementations, deterministic calculation execution, persistence, and recalculation workflows.
