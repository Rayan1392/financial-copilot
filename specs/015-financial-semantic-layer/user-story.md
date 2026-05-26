# User Story - Financial Semantic Layer and Ontology

## Story

As a financial intelligence platform owner,
I want a versioned semantic layer for financial terminology and calculations,
so that scanner interpretation, derived metrics, ranking, and AI explanations use consistent and auditable meanings as the metric catalog grows.

## Acceptance Criteria

- A `FinancialMetricDefinition` represents a stable semantic metric identity independently from provider fields, database columns, prompt wording, and UI labels.
- Metric concepts include `MetricCode`, `MetricVersion`, `MetricCalculationPolicy`, `MetricAlias`, `MetricCategory`, `MetricUnit`, `MetricFormula`, `MetricDependency`, `MetricCalculator`, `MetricResolutionResult`, `MetricCalculationContext`, `MetricCalculationResult`, and data `Requirement` metadata.
- Definitions can represent Phase 1 metrics such as EPS, TTM EPS, Forward P/E, quarterly/YoY/QoQ growth, operating/net margin, revenue growth, operating cash flow, and free cash flow, but these are examples only. The design must support hundreds of future metrics, ratios, derived indicators, aliases, industry-specific formulas, and period-specific calculations.
- The metric catalog is not implemented as a hardcoded list, large `switch` statement, or application-service `if/else` chain.
- Each computed metric references its semantic metric identifier, active definition version, calculation-policy version, source inputs, dependencies, and effective period.
- Metric versions and policies remain historically auditable when definitions, aliases, data requirements, or formulas change.
- Persian and English aliases resolve to the same stable semantic metric identity where their financial meaning is equivalent.
- Scanner parsing resolves user terminology to semantic metric identifiers rather than raw property names or formula assumptions.
- Explainable answers identify the resolved metric definition and calculation-policy version used for relevant displayed values.
- Calculation implementations are independently unit testable and registered through dependency injection or a strategy/plugin-like registry so adding a new metric minimally affects existing code.
- A new metric can be registered without changing core orchestration logic or adding procedural metric dispatch branches.
- Metric resolution follows `User expression -> IMetricAliasResolver -> Canonical MetricCode -> IFinancialMetricRegistry -> IMetricCalculationPolicyProvider -> IFinancialMetricCalculator -> MetricCalculationResult`.
- For ambiguous phrases such as latest-quarter net profit growth, resolution returns candidate codes such as `NET_PROFIT_GROWTH_QOQ` or `NET_PROFIT_GROWTH_YOY` and requires context or clarification instead of silently selecting a formula.
- Semantic definitions may be used by future ranking, research, evaluation, and data-citation capabilities without placing LLM logic inside metric calculation.

## Technical Notes

- This capability extends `003-financial-domain-model` and governs deterministic calculations implemented by `006-derived-metrics-engine`.
- Phase 1 may start with a small registered catalog covering scanner metrics. The architecture must permit catalog growth without redesigning scanner, explanation, or persistence contracts.
- Treat formulas and policies as controlled domain configuration/code with review and versioning, not arbitrary runtime expressions executed from user or LLM input.
- The backend owns canonical definitions, formulas, policies, period handling, dependencies, validation, and confidence rules.
- AI may map language to candidate metrics, ask clarification questions, and explain resolved results; it must not define or invent formulas, choose unvalidated policies, calculate financial values directly, or decide confidence rules.
- Core rule: Financial terminology must be extensible through semantic definitions and calculator strategies, not hardcoded procedural logic.

Suggested extensibility contract:

```csharp
public interface IFinancialMetricCalculator
{
    string MetricCode { get; }

    Task<MetricCalculationResult> CalculateAsync(
        MetricCalculationContext context,
        CancellationToken cancellationToken);
}
```

Additional boundary concepts may include:

```csharp
public interface IFinancialMetricRegistry
{
    IFinancialMetricCalculator Resolve(string metricCode);

    IReadOnlyCollection<FinancialMetricDefinition> GetSupportedMetrics();
}

public interface IMetricAliasResolver
{
    MetricResolutionResult ResolveAlias(
        string userExpression,
        string language,
        MetricResolutionContext context);
}

public interface IMetricCalculationPolicyProvider
{
    MetricCalculationPolicy GetPolicy(
        string metricCode,
        string policyVersion);
}
```
