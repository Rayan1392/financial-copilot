# Tasks

- Create derived metric service consuming semantic metric definitions and versions from `015-financial-semantic-layer`.
- Create versioned metric calculation policies and dependency evidence handling.
- Implement extensible `IFinancialMetricCalculator` registration/resolution through `IFinancialMetricRegistry` rather than hardcoded metric formula branching.
- Persist derived metrics.
- Add recalculation command.
- Add unit tests for each metric.
- Add integration test for ingestion -> derived metric flow.
- Add tests that valuation metric source/as-of metadata remains available for explanation and freshness evaluation.
- Add tests that persisted metrics remain reproducible against their original semantic-definition and calculation-policy versions.
- Add tests that calculators can be registered and validated independently for future metric growth.
- Add tests proving a newly registered metric calculator does not require modification to orchestration dispatch logic.

## Implementation Status - 2026-05-27

Implemented in this story:

- Added registered deterministic calculation strategies for net-profit growth, monthly-sales YoY/MoM growth, TTM sales, TTM earnings, TTM EPS, P/E, and P/S.
- Extended the governed Phase 1 semantic catalog with TTM aggregation, EPS, observed-price, market-capitalization, and shares-outstanding dependencies and versioned calculation policies.
- Added Application calculation and recalculation-command contracts that resolve calculators through `IFinancialMetricRegistry` and persist derived results without AI or scanner formula dispatch.
- Added normalized source input strategies for ingested net-profit and monthly-sales observations.
- Added persisted `DerivedMetrics` storage retaining semantic version, policy version, quality warnings, source/as-of evidence, and dependency evidence, with PostgreSQL migration.
- Verified normal formulas, missing/zero denominator behavior, quote evidence retention, custom strategy registration, and normalized-ingestion to persisted-calculation integration.
