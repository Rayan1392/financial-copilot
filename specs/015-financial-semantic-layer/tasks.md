# Tasks

## Domain Contracts

- Define `FinancialMetricDefinition`, `MetricCode`, `MetricVersion`, `MetricCalculationPolicy`, `MetricAlias`, `MetricCategory`, `MetricUnit`, `MetricFormula`, `MetricDependency`, `MetricCalculator`, `MetricResolutionResult`, `MetricCalculationContext`, `MetricCalculationResult`, and metric data-requirement models.
- Define stable canonical `MetricCode`/semantic identifier rules, including codes such as `NET_PROFIT_GROWTH_YOY`, `NET_PROFIT_GROWTH_QOQ`, and `PE_TTM`, plus version/effective-date audit semantics.
- Define Persian and English alias resolution contracts and ambiguity outcomes.
- Define the full `IFinancialMetricCalculator`, `IFinancialMetricRegistry`, `IMetricAliasResolver`, `IMetricCalculationPolicyProvider`, and dependency resolver interfaces documented in the architecture.

## Persistence And Registration

- Design persistence/read models for definition versions, aliases, calculation-policy versions, and metric dependency metadata.
- Implement an extensible DI/strategy registration pattern for Phase 1 metric calculators without central formula switch statements.
- Associate persisted `DerivedMetric` snapshots with definition version, calculation-policy version, source evidence, and dependency versions.

## Integration

- Update scanner plan contracts to use canonical resolved semantic metric codes and retain original user terminology.
- Update Explainable Answer evidence to cite semantic definition and calculation-policy version where a metric is displayed.
- Integrate the registered metric catalog with supported-metric metadata responses.
- Document and validate the resolution pipeline: `User expression -> Alias Resolver -> Canonical MetricCode -> Registry -> Calculation Policy Provider -> Calculator -> Calculation Result`.

## Verification

- Add unit tests for version selection, bilingual alias resolution, ambiguity, dependency resolution, and independent calculator behavior.
- Add tests for the Persian latest-quarter net-profit-growth phrase, requiring contextual selection or clarification between `NET_PROFIT_GROWTH_QOQ` and `NET_PROFIT_GROWTH_YOY`.
- Add architecture tests preventing hardcoded formula-routing logic from accumulating in orchestrator/parser services.
- Add audit/reproducibility tests proving an historical result retains the metric and policy versions originally used.
