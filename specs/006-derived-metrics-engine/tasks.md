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
