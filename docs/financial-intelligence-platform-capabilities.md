# AI-Native Financial Intelligence Platform Extensions

## Purpose

FinancialCopilot begins with a chat-driven Scanner MVP, but its architecture should support an AI-native Financial Intelligence Platform. This extension preserves the existing decisions:

- A modular monolith backend.
- A single public AI facade endpoint: `POST /api/ai/v1/query`.
- Deterministic financial calculations and validated scanner plans.
- Explainable answers and billing-grade accounting.
- Provider-neutral AI orchestration for hosted or local models.
- Tenant-aware SaaS readiness.

The capabilities in this document are added as bounded contracts and evolution paths. They are not a mandate to deploy new infrastructure or deliver every advanced feature in Phase 1.

## Financial Semantic Layer

### Goal

A formal Financial Semantic Layer provides unambiguous, versioned, and auditable financial terminology for calculations, scanner filters, explanations, ranking systems, and future research tools.

The metric examples currently required by the Scanner MVP, such as EPS, TTM EPS, Forward P/E, quarterly growth, YoY growth, QoQ growth, operating margin, net margin, revenue growth, operating cash flow, and free cash flow, are only a small initial catalog. The domain may grow to hundreds of metrics, ratios, derived indicators, Persian/English aliases, industry-specific formulas, period-specific calculations, and data requirements.

### Domain Concepts

| Concept | Responsibility |
|---|---|
| `FinancialMetricDefinition` | Stable semantic identity and business meaning of a metric. |
| `MetricCode` | Canonical machine-readable identifier, such as `NET_PROFIT_GROWTH_YOY`, used throughout execution and evidence. |
| `MetricVersion` | Historical version of a metric definition and its effective lifetime. |
| `MetricCalculationPolicy` | Deterministic calculation policy, fallback policy, and evidence requirements. |
| `MetricAlias` | Localized or alternative terminology mapped to a semantic metric identifier. |
| `MetricCategory` | Valuation, profitability, cash flow, growth, liquidity, or another governed grouping. |
| `MetricUnit` | Percent, ratio, currency, quantity, score, or other interpreted output unit. |
| `MetricFormula` | Governed formula description/reference associated with a policy, not free-form LLM logic. |
| `MetricDependency` | Input metric or normalized data dependency needed for calculation. |
| `Requirement` | Required period, report type, freshness, completeness, and provider evidence constraints. |
| `MetricCalculator` | Registered deterministic calculator strategy for one supported metric code. |
| `MetricResolutionResult` | Alias-resolution result, candidate definitions, ambiguity state, and required clarification. |
| `MetricCalculationContext` | Required observations, period/comparison context, policy version, and source evidence inputs. |
| `MetricCalculationResult` | Deterministic value, formula/policy/version, period, dependencies, source evidence, and confidence inputs. |

### Extensibility Rules

- Do not implement metric calculation or interpretation as a large `switch`/`case` or `if`/`else` chain.
- Do not hardcode metric names, formulas, bilingual aliases, or calculation logic in orchestrator, parser, or controller services.
- Resolve Persian and English terminology through `MetricAlias` entries into stable semantic identifiers.
- Register calculators through dependency injection or a strategy/plugin-like registry.
- Allow a new metric to be introduced through its definition, policies, aliases, dependency metadata, calculator, persistence mapping where necessary, and tests without modifying unrelated metric implementations.
- Persist metric-definition and calculation-policy versions with computed values and answer evidence.
- Keep historical outputs reproducible after new policy versions are activated.
- Let AI explanations refer to resolved definitions and versions; never let an LLM choose or alter the calculation policy.
- Treat this as a core architectural rule: **Financial terminology must be extensible through semantic definitions and calculator strategies, not hardcoded procedural logic.**

Suggested contracts:

```csharp
public interface IFinancialMetricCalculator
{
    string MetricCode { get; }

    Task<MetricCalculationResult> CalculateAsync(
        MetricCalculationContext context,
        CancellationToken cancellationToken);
}

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

### Use By Platform Capabilities

```text
User expression
-> IMetricAliasResolver
-> Canonical MetricCode
-> IFinancialMetricRegistry
-> IMetricCalculationPolicyProvider
-> IFinancialMetricCalculator
-> MetricCalculationResult
```

Example:

```text
"رشد سود خالص فصل آخر" (latest-quarter net profit growth)
-> Alias resolution with language and period context
-> NET_PROFIT_GROWTH_QOQ or NET_PROFIT_GROWTH_YOY, depending on resolved comparison context
-> Resolve FinancialMetricDefinition and policy version
-> Execute the registered deterministic calculator
-> Return formula, period, source evidence, calculation version, and confidence inputs
```

When the comparison basis cannot be resolved without changing meaning, the system returns a clarification request instead of choosing a metric silently.

### Backend And AI Ownership

The backend owns:

- Canonical metric definitions and metric codes.
- Formulas, calculation policies, period handling, dependencies, and validation.
- Calculator strategy registration and deterministic calculation results.
- Confidence-score inputs and rules derived from resolved evidence.

The AI layer may only:

- Map natural language to candidate semantic metrics through controlled contracts.
- Request clarification when user intent is ambiguous.
- Explain backend-calculated results and referenced definition versions.

The AI layer must not define or invent metric definitions or formulas, choose an unvalidated calculation policy, or calculate financial values directly.

This capability is foundational for explainable AI, financial consistency, trust, ranking systems, evaluation datasets, and future AI-assisted research.

## Derived Feature Foundation

### Goal

The Derived Feature Foundation prepares deterministic evidence for future intelligent ranking, anomaly detection, recommendations, portfolio insights, and ML-assisted workflows without building a full ML platform in the Scanner MVP.

### Domain Concepts

| Concept | Responsibility |
|---|---|
| `DerivedFeature` | A computed financial/market signal available to consuming use cases. |
| `FeatureDefinition` | Meaning, expected output, required inputs, and computation policy. |
| `FeatureSnapshot` | Time-specific computed feature value and its evidence/version references. |
| `FeatureVersion` | Version of computation behavior and definition. |
| `FeatureComputationJob` | Scheduled or event-triggered asynchronous computation unit. |
| `FeatureDependency` | Referenced metrics, features, observations, and required windows. |

Potential future definitions include `MomentumScore`, `EarningsQualityScore`, `RelativeStrength`, `VolatilityScore`, `LiquidityScore`, `GrowthConsistency`, and `SmartMoneySignal`.

### Design Rules

- Compute features asynchronously where appropriate using worker/RabbitMQ workflows.
- Store historical snapshots with definition version, input versions, observation window, and freshness evidence.
- Require deterministic and reproducible computation for deterministic feature definitions.
- Expose features through stable Application interfaces for ranking or AI orchestration.
- Do not bury feature computation in model prompts.
- Do not introduce online ML serving, model training pipelines, or dedicated feature-store infrastructure in Phase 1.

## AI Evaluation And Regression

### Goal

An internal AI Evaluation and Regression capability measures whether prompt, provider, orchestration, semantic-resolution, and answer-generation changes improve or silently damage quality.

### Domain Concepts

| Concept | Responsibility |
|---|---|
| `GoldenQuestion` | Curated user question and relevant context/language. |
| `GoldenAnswer` | Approved structured expectations and allowed answer criteria. |
| `EvaluationDataset` | Versioned collection of evaluation cases. |
| `PromptVersion` | Prompt/template/workflow instruction revision under evaluation. |
| `EvaluationRun` | Execution against an identified dataset and configured workflow/model setup. |
| `EvaluationScore` | Metric-specific scoring outcome and evidence. |
| `RegressionResult` | Comparison against an approved baseline with severity/status. |

### Evaluation Targets

- Scanner interpretation accuracy and semantic metric resolution.
- Correct clarification for ambiguous questions.
- Rejection of unsupported or invented filters.
- Stable ranking and table projection from deterministic evidence.
- Hallucination detection and citation sufficiency.
- Financial metric extraction/explanation correctness.
- Protection of backend-derived confidence and billing metadata.

Structured results can be scored deterministically. Prose evaluation may use explicit rubrics or controlled model-assisted review and must record that distinction. Evaluation jobs are internal quality activities and do not run in the production user-query critical path.

## AI Observability And Telemetry

### Goal

Operational telemetry enables monitoring and investigation of cost, latency, provider behavior, fallback, workflow errors, tool failures, and contested or low-quality answers.

### Domain Concepts

| Concept | Responsibility |
|---|---|
| `AiExecutionTrace` | End-to-end trace of an AI facade workflow execution. |
| `PromptTrace` | Protected evidence of prompt/template/version invocation subject to privacy policy. |
| `ToolExecutionTrace` | Routed Application tool invocation and outcome. |
| `ProviderLatency` | Provider/model request timing and availability observation. |
| `TokenUsage` | Normalized provider usage measures when supplied. |
| `CostTelemetry` | Operational cost observation reconcilable with, but not authoritative over, Billing. |
| `WorkflowTelemetry` | Stage duration, errors, retries, fallback, and bottleneck evidence. |

### Operational Rules

- Use OpenTelemetry-compatible traces, metrics, structured logs, and correlation identifiers.
- Correlate facade request, Conversation/Message, workflow, tools, providers, cache, data access, confidence computation, reservation, and ledger outcome.
- Categorize errors and expose retry/fallback visibility.
- Apply tenant isolation, privacy, consent, redaction, and retention policy to prompts, responses, memory context, and data citations.
- Use Billing ledger records as the accounting source of truth; telemetry is operational evidence.

Potential future sinks include OpenTelemetry collector-compatible systems, internal dashboards, and Langfuse only where privacy and tenant policy allow its use.

## Conversation Memory Strategy

### Phase 1 Boundary

Phase 1 persists `Conversation` and `Message` history. Persistence is not equivalent to personalized AI memory and does not authorize reuse of sensitive user information across conversations.

### Future Memory Types

| Memory type | Example purpose |
|---|---|
| `ShortTermConversationMemory` | Compact context needed within the current conversational task. |
| `LongTermUserMemory` | Explicitly consented durable information about user preferences. |
| `PortfolioAwareMemory` | Authorized context from portfolio holdings or exposure preferences. |
| `PreferenceMemory` | Preferred sectors, risk appetite, horizon, and presentation preferences. |
| `ResearchMemory` | Prior research objectives or followed analyses. |
| `WatchlistMemory` | Authorized watchlist-related contextual signals. |

### Policy Rules

- Memory must be tenant-aware, subject-scoped, purpose-scoped, protected, and auditable.
- Long-term or sensitive memory requires explicit user consent and controllable retention/deletion behavior.
- Orchestration may consume only authorized memory through a stable context-provider boundary.
- Material memory use should be explainable to the user.
- Provider adapters must not own product memory or leak it through telemetry/logging.
- Memory never replaces authoritative Conversation, watchlist, portfolio, billing, or financial records.
- Do not introduce vector-memory infrastructure in Phase 1 merely for future readiness.

Suggested future extension points:

```csharp
public interface IMemoryContextProvider
public interface IMemoryConsentService
public interface IMemoryAuditService
```

## Architectural Position

These platform capabilities remain inside the modular monolith as domain/Application/worker contracts at first:

```text
Single AI Facade
-> Orchestration and Provider-Neutral AI Execution
-> Versioned Financial Semantic Layer
-> Deterministic Metrics and Future Derived Features
-> Explainable Answer and Billing-Grade Accounting
-> Correlated Operational Telemetry
-> Internal Evaluation and Regression
-> Optional Consent-Aware Future Memory
```

This progression extends the product from an AI chat interface into an AI-native Financial Intelligence Platform without weakening deterministic financial behavior, public API simplicity, accounting consistency, or tenant isolation.
