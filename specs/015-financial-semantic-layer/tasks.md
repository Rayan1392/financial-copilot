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

## Implementation Status - 2026-05-26

Implemented in this story:

- Added versioned semantic domain contracts for `FinancialMetricDefinition`, `MetricAlias`, `MetricCategory`, `MetricUnit`, controlled `MetricFormula` metadata, `MetricDependency`, `MetricCalculator`, calculation inputs/results, and resolution outcomes, building on canonical `MetricCode` and version/policy primitives from `003`.
- Added `IFinancialMetricCalculator`, `IFinancialMetricRegistry`, `IMetricAliasResolver`, `IMetricCalculationPolicyProvider`, and `IMetricDependencyResolver`, with registry/strategy resolution rather than formula routing branches.
- Added a registered Phase 1 semantic catalog with canonical codes, public aliases, effective versions, policy metadata, and dependencies for net-profit growth, monthly sales growth, TTM valuation measures, and their governed inputs.
- Added Persian and English alias resolution with explicit ambiguity for `رشد سود خالص آخرین فصل`, resolving to either `NET_PROFIT_GROWTH_YOY` or `NET_PROFIT_GROWTH_QOQ` only when comparison context is supplied.
- Added explicit quarter-over-quarter period-comparison semantics and calendar-period preservation for complete quarterly intervals.
- Extended `DerivedMetric` evidence with dependency definition/policy versions for historical audit, and added Application handoff contracts that retain original scanner terminology plus explanation metric/policy evidence.
- Added EF Core semantic-catalog persistence/read model structures for definition versions, aliases, policy versions, and metric dependencies, with DbContext model isolation from Billing persistence.
- Added authenticated `GET /api/ai/v1/metadata/metrics`, returning registered semantic definition/version, localized alias, category/unit, supported-period, and public calculation-policy metadata.
- Added tests for effective-version selection, bilingual alias resolution, Persian ambiguity/context selection, dependencies/policy metadata, independent strategy registration, stored audit evidence, semantic handoff contracts, metadata API output, persistence models, and architecture protection from hardcoded scanner/orchestrator metric routing.

Explicitly deferred to dependent stories:

- `006-derived-metrics-engine` owns executable production calculator implementations, deterministic numeric calculation, recalculation and persistence of calculated metric observations.
- `007-natural-language-scanner-parser` owns full scanner-plan generation and parser/orchestrator invocation of semantic resolution; this story provides its canonical metric handoff contract.
- `009-explainable-results` owns completed answer assembly; this story provides its versioned metric-evidence handoff contract.
- `005-data-ingestion-and-normalization` owns synchronization/population of persisted normalized and semantic catalog records beyond the delivered schema/read-model foundation.
