# Feature 118 — Conversational Capability Registry and Query Frame

## Status

`[ ]` Not yet implemented

## Story

As a Financial Copilot user,

I want the assistant to understand natural variations of supported questions without requiring exact command wording,

so that equivalent requests reach the same governed capability and unsupported requests are identified honestly.

## Business Context

Capability knowledge is currently duplicated across system prompts, deterministic phrase arrays, parser rules, tool descriptions, frontend examples, and tests. The application has a governed metric semantic layer but no single registry for user objectives such as metric lookup, stock screening, sales trend, financial statements, disclosure listing, product mix, P/S gauge, or comprehensive analysis.

This feature creates the application-owned capability catalog and a schema-constrained query interpretation contract. It does not move every route immediately; Feature 122 performs staged migration.

## Goals

- Represent enabled AI capabilities once with aliases, examples, slots, route, output, and data requirements.
- Convert raw text into a validated, provider-neutral `QueryInterpretation`.
- Separate capability, entity, metric, period, comparison, and presentation semantics.
- Support deterministic rules plus optional LLM interpretation without allowing the LLM to define executable behavior.
- Establish routing confidence and ambiguity policy.
- Generate downstream prompt/tool/UI metadata from the same registry.

## Scope

### In Scope

- Versioned `CapabilityDefinition` registry.
- Persian/English query normalization.
- Structured interpretation contracts and validation.
- Capability candidate ranking with evidence and confidence.
- Required/optional slot definitions and defaults metadata.
- Presentation semantics such as table, chart, gauge, summary, and list.
- Registry-driven prompt/tool descriptions and discoverability metadata.
- Feature flags and startup validation for capability enablement.

### Out of Scope

- Canonical company resolution implementation (Feature 119).
- Persisted dialogue state (Feature 120).
- Frontend action rendering (Feature 121).
- Full route migration/removal of legacy phrase rules (Feature 122).
- User-text-generated SQL, formulas, metrics, or capabilities.

## Capability Definition

Conceptual contract:

```csharp
public sealed record CapabilityDefinition(
    string Code,
    int Version,
    bool Enabled,
    IReadOnlyList<LocalizedAlias> Aliases,
    IReadOnlyList<LocalizedExample> Examples,
    IReadOnlyList<SlotDefinition> RequiredSlots,
    IReadOnlyList<SlotDefinition> OptionalSlots,
    string ExecutionRoute,
    string OutputType,
    IReadOnlyList<string> DataRequirements,
    string PrecedenceGroup,
    SuggestionPolicy SuggestionPolicy);
```

Initial capability codes must cover the active product surface:

```text
stock_screening
symbol_metric_lookup
comprehensive_analysis
monthly_activity_trend
product_revenue_mix
financial_statement_table
financial_statement_period_analysis
disclosure_listing
monthly_sales_quality_ranking
ps_gauge_visualization
personalized_insight_explanation (only if an executable route is verified)
```

`clarification` and `unknown` are outcomes, not business capabilities.

## Query Interpretation Contract

```csharp
public sealed record QueryInterpretation(
    string OriginalText,
    string NormalizedText,
    string ReplyLanguage,
    IReadOnlyList<CapabilityCandidate> CapabilityCandidates,
    IReadOnlyList<EntityMention> EntityMentions,
    IReadOnlyList<MetricSelection> Metrics,
    PeriodSelection? Period,
    ComparisonSelection? Comparison,
    PresentationPreference? Presentation,
    IReadOnlyList<string> MissingSlots,
    IReadOnlyList<string> UnsupportedParts,
    decimal Confidence,
    IReadOnlyList<InterpretationEvidence> Evidence,
    int RegistryVersion);
```

The persisted/audited form must distinguish user-explicit, conversation-inferred, policy-defaulted, and model-proposed values.

## Interpretation Policy

1. Normalize orthography and conversational form without losing the original text.
2. Detect presentation words separately from entity candidates. For example, `چارت` and `نمودار` map to `Chart`; they can never become symbols.
3. Resolve metric phrases through Feature 015/072 semantic services.
4. Produce one or more capability candidates with evidence.
5. Validate all proposed codes and slots against the registry.
6. Reject model-proposed formulas, SQL, route names, metric codes, or capabilities absent from governed registries.
7. Apply deterministic precedence for known conflicts, including:
   - plural condition + threshold → screening;
   - named symbol + metric → symbol lookup;
   - trend/history/chart language + sales/monthly activity → trend;
   - general analysis language + symbol → comprehensive analysis;
   - explicit P/S gauge language → gauge, while plain P/S remains lookup.
8. Below the configured confidence threshold, return ambiguity/missing-slot information for dialogue policy rather than guessing.

## LLM Boundary

The LLM may propose a schema-constrained interpretation and semantic evidence. The application must:

- validate every enum/code against registries;
- recompute deterministic confidence factors where applicable;
- reject unknown fields and excessive payloads;
- never execute model-provided SQL or formulas;
- retain a deterministic fallback for known high-value capabilities;
- treat malformed model output as a safe outcome from Feature 117.

## Registry Ownership and Generation

The registry is the source for:

- route enablement and required slots;
- agent prompt/tool capability descriptions;
- API metadata for assisted guidance;
- frontend example prompts;
- Telegram help/capability menus;
- regression dataset generation/coverage checks.

Generated artifacts must not be edited as independent business truth.

## Acceptance Criteria

1. Every enabled AI business capability has exactly one registered definition and stable code/version.
2. Duplicate aliases, unknown routes, invalid slot definitions, and conflicting precedence fail startup or build-time validation.
3. Query interpretation is schema-constrained and contains provenance for every inferred/defaulted field.
4. Presentation words cannot be resolved as company symbols.
5. Equivalent Persian, English, and mixed-language paraphrases produce the same capability candidate when semantics match.
6. An LLM cannot introduce an unregistered capability, metric, formula, route, or SQL fragment.
7. Low-confidence and multi-capability requests produce structured ambiguity instead of silent routing.
8. Prompt/tool descriptions and exposed capability metadata are derived from the registry.
9. Disabling a capability removes it from routing and guidance without deleting historical persisted responses.
10. Existing Feature 015 metric semantics remain the source of truth for metric meaning.
11. Interpretation adds no provider-specific business logic.
12. Registry and interpretation latency meet the agreed AI facade budget and are observable.

## Dependencies

- Feature `117`
- `015-financial-semantic-layer`
- `017-ai-evaluation-and-regression`
- `018-ai-observability-and-telemetry`
- `034-frontend-assisted-query-metadata`
- `047` and `056` orchestration
- `072-centralized-metric-alias-routing`

## Priority

**High.** This is the semantic foundation for all later dialogue and route migration work.
