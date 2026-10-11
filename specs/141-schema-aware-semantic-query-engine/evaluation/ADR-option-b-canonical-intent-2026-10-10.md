# ADR — Canonical semantic intent before catalog binding

- Status: accepted for Phase 1 shadow implementation
- Date: 2026-10-10
- Decision owner: product owner

## Context

The legacy requirements extractor asks one model stage to decide user intent and select catalog references. This couples natural-language interpretation to current catalog coverage and allows the navigator/planner boundary to contain duplicate operation and period representations. The approved Feature 141 Option B decision separates these responsibilities.

## Decision

Introduce a typed, versioned `CanonicalSemanticIntent` before vectorless catalog navigation. The intent describes the requested financial/business information and constraints only. The navigator later binds those concepts to governed catalog references; semantic completeness validates that binding; the existing planner chooses executable composition; the deterministic compiler and separately authorized executor remain unchanged.

Phase 1 operates only in a shadow evaluation path. It does not replace production or Development routing, widen data authorization, or perform financial-data reads. The new extractor uses the existing provider-neutral `IAiModelExecutionService`, bounded request/retry limits, structured output, and deterministic validation. A semantically valid intent remains valid even when catalog coverage or query authorization is absent.

## Consequences

- Canonical intent types must not carry database/catalog/provider identifiers or SQL.
- Population discovery is an unbound company set, not an entity-resolution result.
- Explicit and inferred user constraints retain distinct provenance.
- Ambiguity is represented and clarified rather than converted into an invented calculation.
- Benchmark corrections are overlays with evidence and preserve original datasets/results unchanged.
- Historical holdouts remain historical. Exact-match accuracy uses only adjudicated gold; unreviewed or disputed fields and their denominator effects are reported separately.
- Phase 2 integration requires successful shadow quality gates and separate governance approval for catalog binding and query authorization.

## Rejected for Phase 1

- Replacing or bypassing the current Navigator/Planner/Compiler/Executor.
- Adding question-specific Persian handlers or copying benchmark answers into runtime prompts.
- Making an unavailable catalog item an invalid intent.
- Altering authorization or removing difficult valid questions to improve a score.

## Post-run assessment (2026-10-10)

The 60-case live shadow run established end-to-end extractor/scorer plumbing but does not meet a Phase 2 quality gate: only 11 cases have reviewed gold, measure-concept gold and output vocabulary are not aligned, 5 cases were rejected as unsupported semantic intent, one provider failure occurred, and the adjudicated sample includes observed measure-qualifier/unit omissions. Therefore no routing or planner integration is recommended now. Close the Phase 1 evidence gaps, freeze an explicit language-independent measure scoring contract, and evaluate newly authored unseen questions before reconsideration. This assessment does not alter the accepted architectural decision to separate semantic extraction from catalog binding.

## Phase 1.5 reassessment (2026-10-10)

The shadow-only implementation now uses a v2 semantic contract with an independent canonical business-measure registry, exact reviewed Persian-term normalization, preserved value/reporting/statement/grain qualifiers, separate requested and effective source units, and deterministic validation. The v3 overlay has 30 adjudicated cases and one explicitly disputed case. Ten reviewed cases had prior prediction exposure; twenty were reviewed blind to this run. A newly authored, frozen 12-case holdout has one ambiguous and two unsupported cases, but no second human/domain reviewer.

Evidence does **not** support Phase 2: the final 60-case rerun returned provider failures on all 60 cases, and the single frozen holdout first pass returned provider failures on all 12. Thus no model semantic accuracy or unsupported-intent accuracy is estimable; the scorecards expose zero completed outputs and zero conditional denominators. Legacy extraction baselines remain available on the same reviewed questions, but they do not encode the new qualifier/unit representation. This is a provider-inconclusive evaluation, not a quality pass or model-semantic regression finding.

The Phase 1.5 types and registry remain evaluation-only. Do not connect them to production Navigator, Planner, Compiler, Executor, or chat routing. Before reconsidering integration: restore/verify provider availability, obtain independent domain review of v3 gold (including the `fa-value-02` conflict), and run the frozen holdout against a stable code/scorer revision. Any post-prediction correction requires a new holdout version. This reassessment does not change the original architecture decision.
