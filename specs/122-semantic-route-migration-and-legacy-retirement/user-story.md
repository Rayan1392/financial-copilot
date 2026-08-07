# Feature 122 — Semantic Route Migration and Legacy Retirement

## Status

`[ ]` Not yet implemented

## Story

As a Financial Copilot user,

I want equivalent questions to use the same semantic routing behavior across every supported capability,

so that small wording changes do not produce a different route, false no-data answer, or generic fallback.

## Business Context

Features 117–121 establish contracts and policy, but existing routes continue to contain local phrase matching, token extraction, prompt duplication, and post-execution intent inference until migrated. This feature moves the active capability surface behind the new semantic execution boundary and retires duplicated legacy behavior safely.

The first migration slice is monthly activity/sales trend and direct symbol metric lookup, as recommended by `docs/ai-query-semantic-dialogue-layer-review.md`.

## Goals

- Make `QueryInterpretation` plus validated slots the input to routing.
- Migrate all active routes through capability adapters without rewriting their business use cases.
- Preserve successful structured result contracts and financial calculations.
- Align native MAF V2 and V1 rollback behavior.
- Remove local phrase/token routing only after parity and canary gates pass.
- Move Billing reservation to the correct execute/non-execute boundary according to policy.

## Scope

### In Scope

- Semantic execution dispatcher and capability adapters.
- First migration: monthly trend and direct metric lookup.
- Subsequent migration of scanner, comprehensive analysis, statements, product mix, disclosures, sales quality, and P/S gauge.
- Explicit typed execution results and outcome diagnosis.
- V1/V2 parity, feature flags, shadow comparison, canary, and rollback.
- Retirement of duplicated prompt phrases, local symbol extraction, and result-object-only intent inference.

### Out of Scope

- Rewriting financial calculations or data repositories.
- Changing ComprehensiveAnalysis faithfulness or default-date policy.
- Introducing new capabilities.
- Removing V1 rollback before production parity is demonstrated.
- A single unconstrained “universal” agent tool.

## Target Execution Boundary

```csharp
public interface IConversationalCapabilityExecutor
{
    string CapabilityCode { get; }

    Task<CapabilityExecutionResult> ExecuteAsync(
        ValidatedQueryFrame frame,
        QueryExecutionContext context,
        CancellationToken cancellationToken);
}
```

`CapabilityExecutionResult` must carry:

- capability code/version;
- typed status/reason;
- structured business payload or reference;
- evidence/freshness metadata already produced by the use case;
- safe warnings;
- no raw exception or agent prose.

The dispatcher selects only enabled registry capabilities and compatible validated frames.

## Migration Order

### Slice A — Monthly Activity Trend

- Interpret trend intent, symbol, monthly sales metric, period, comparison, and chart presentation.
- Resolve the symbol via Feature 119.
- Support natural variants including `چارت روند فروش فولاد`.
- Use Feature 120 for `نمودارش رو هم بده`.
- Invoke the existing persisted trend use case and preserve `monthlyActivityTrendResult` exactly.
- Distinguish missing symbol, ambiguous/unknown symbol, no trend rows, stale/ineligible data, and failure.

### Slice B — Direct Symbol Metric Lookup

- Preserve Feature 015/072 metric aliases and direct metric capability flags.
- Distinguish point lookup from trend, screening, analysis, and gauge.
- Support metric follow-up using typed task state.
- Preserve `SymbolLookupTable`, confidence, explainability, freshness, and Billing behavior.

### Slice C — Remaining Deterministic Routes

- product revenue mix;
- financial statement table;
- financial statement period analysis;
- disclosure listing;
- monthly sales quality ranking;
- P/S gauge visualization;
- any verified personalized insight explanation route.

### Slice D — Agent Tool Routes

- stock screening;
- comprehensive analysis;
- symbol lookup when still delegated to tool calling.

The LLM may select/propose a registered capability, but application validation and the dispatcher own execution.

## Routing and Billing Policy

1. Semantic interpretation and dialogue gate occur before business execution.
2. Only an executable validated frame reaches a capability executor.
3. Billing reservation/finalization follows one documented operation policy and is never duplicated by adapters.
4. Clarification/unsupported/ambiguity paths do not accidentally invoke data tools.
5. A temporary executor failure maps through Feature 117.
6. Tool calling cannot bypass capability enablement, slots, entity resolution, or faithfulness policy.

## Rollout and Legacy Retirement

- Add per-capability semantic-routing flags.
- Support shadow interpretation against legacy routing without double execution or charging.
- Record agreement/disagreement and expected route.
- Canary Slice A/B before broader migration.
- Keep legacy path available for rollback until acceptance and observation windows pass.
- Remove local phrase/token logic only after all callers migrate and architecture tests prevent reintroduction.

## Acceptance Criteria

1. Monthly trend and direct metric lookup execute from validated query frames.
2. `چارت روند فروش فولاد` returns the same trend payload as the canonical phrase.
3. `نمودارش رو هم بده` after a فولاد monthly-sales answer returns the فولاد trend.
4. `P/E فولاد` remains metric lookup only.
5. `سهام با P/E زیر ۵` remains screening only.
6. `تحلیل فولاد` retains combined/faithful analysis behavior required by existing policy.
7. No migrated route uses first-token/local stop-word symbol extraction.
8. No migrated route infers outcome solely from non-null response objects.
9. V1 and V2 produce equivalent capability, slots, outcome, and structured facts for the golden set.
10. Interpretation/clarification cannot create duplicate Billing operations.
11. Shadow mode performs no second financial execution and no user-visible mutation.
12. Legacy phrase logic is retired only after canary and regression gates pass.

## Dependencies

- Features `117`–`121`
- `007`–`009` scanner/orchestration/explainability
- `015` financial semantic layer
- `045` symbol lookup
- `047` and `056` orchestration
- `066` comprehensive analysis query
- `069`, `070`, `075`–`078`, and `113` monthly sales/trend capabilities
- `072` direct metric routing
- `114`/`115` P/S visualization when enabled
- `116` sales-growth scanner when enabled

## Priority

**High after foundations.** This is where the semantic layer begins controlling production query execution.
