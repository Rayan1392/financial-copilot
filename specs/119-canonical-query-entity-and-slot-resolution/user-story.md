# Feature 119 — Canonical Query Entity and Slot Resolution

## Status

`[x]` Implemented and verified through migrated production routes

## Story

As a Financial Copilot user,

I want the assistant to correctly recognize the company, symbol, metric, period, comparison, and presentation in my question,

so that conversational words are not mistaken for symbols and ambiguity is resolved instead of being reported as missing data.

## Business Context

Different routes currently extract symbols with different rules. The monthly trend path can select the first remaining non-stop-word token before canonical company resolution, which makes phrases such as `چارت روند فروش فولاد` fragile. Some use cases return `null` for unknown, ambiguous, or missing entities.

This feature establishes one canonical resolution boundary for every symbol-bearing query and a reusable slot-resolution pipeline for the query frame from Feature 118.

## Goals

- Resolve all company/symbol mentions through one provider-neutral service.
- Represent `Resolved`, `Ambiguous`, `NotFound`, and `Missing` explicitly.
- Keep presentation, metric, time, comparison, and entity vocabularies separate.
- Support ticker, company name, approved alias, orthographic variation, and bounded typo candidates.
- Validate slot compatibility against capability definitions.
- Return structured disambiguation candidates without silently choosing.

## Scope

### In Scope

- Canonical entity-resolution result contract.
- Company/symbol resolver adapter over existing canonical identity data.
- Entity mention extraction from `QueryInterpretation`.
- Metric, period, comparison, and presentation slot validation.
- Ambiguity ranking and safe thresholds.
- Data-availability-neutral identity resolution.
- Migration helpers for legacy routes.

### Out of Scope

- Persisting multi-turn task state (Feature 120).
- Rendering suggestion chips (Feature 121).
- Creating company identities from user prompts.
- Fuzzy matching that silently executes against a low-confidence company.
- Provider-specific symbol catalogs becoming identity authority.

## Entity Resolution Contract

Conceptual result:

```csharp
public abstract record EntityResolutionResult
{
    public sealed record Resolved(CanonicalEntity Entity, ResolutionEvidence Evidence) : EntityResolutionResult;
    public sealed record Ambiguous(IReadOnlyList<EntityCandidate> Candidates) : EntityResolutionResult;
    public sealed record NotFound(string NormalizedMention) : EntityResolutionResult;
    public sealed record Missing(string EntityType) : EntityResolutionResult;
}
```

Each resolved entity includes canonical ID, display symbol, company name, entity type, and safe identity provenance. It must not expose provider credentials or internal database schema.

## Resolution Policy

Resolution order:

1. exact canonical ticker;
2. exact normalized company name;
3. approved active alias;
4. unambiguous normalized identity variant;
5. bounded fuzzy candidates for user disambiguation only.

Rules:

- Persian/Arabic character and ZWNJ normalization uses Feature 118.
- Presentation terms (`چارت`, `نمودار`, `graph`), politeness, verbs, metrics, and period expressions are excluded from entity candidates by semantic category, not an ever-growing local stop-word list.
- Fuzzy resolution above an execution threshold may be configurable, but the safe default is to ask when the match is not exact/unambiguous.
- Ambiguity must preserve candidate IDs and localized labels in deterministic order.
- Identity resolution happens before checking whether the requested dataset has rows.
- “entity not found” and “supported capability but no rows” remain distinct outcomes.

## Slot Model

Initial reusable slot types:

```text
CompanyOrSymbol
Metric
Period
ComparisonBaseline
Threshold
StatementType
AnalysisTopic
Presentation
ResultLimit
Sort
```

Each slot stores value, source/provenance, confidence, validation state, and capability compatibility.

## Ambiguity and Missing Input

- One clear missing required slot is returned for focused clarification.
- Multiple missing slots are prioritized according to the capability definition; the assistant asks only the highest-value question first.
- Ambiguous company candidates return `DisambiguationNeeded`, not `NoData`.
- A recognized company with no requested data returns `NoData`, not `entity_not_found`.
- Unsupported metric/capability combinations identify the unsupported slot without discarding understood entities.

## Acceptance Criteria

1. Every migrated symbol-bearing route consumes the same canonical entity-resolution contract.
2. `چارت روند فروش فولاد` resolves `فولاد`; `چارت` is classified as presentation.
3. Exact ticker and full company-name variants resolve to the same canonical company.
4. Ambiguous aliases return ordered candidates and never execute silently.
5. Unknown entities are distinct from known entities with no data.
6. Missing symbol is distinct from unknown symbol.
7. Metric, period, comparison, and presentation slots include provenance and validation state.
8. A provider catalog cannot create a parallel canonical identity during query execution.
9. Raw local token-first symbol extraction is not used after a route is migrated.
10. Resolver behavior is deterministic across V1 and V2.
11. Resolution is tenant-safe and does not expose internal identifiers unnecessarily.
12. Existing metric ontology and period policies remain authoritative.

## Dependencies

- Feature `117`
- Feature `118`
- `003-financial-domain-model`
- `015-financial-semantic-layer`
- `045-symbol-metric-point-lookup`
- `064-trading-instrument-unification`
- `072-centralized-metric-alias-routing`

## Priority

**High.** This removes a concrete class of wrong-symbol and false no-data failures.
