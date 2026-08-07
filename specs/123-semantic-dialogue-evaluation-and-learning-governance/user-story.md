# Feature 123 — Semantic Dialogue Evaluation and Learning Governance

## Status

`[ ]` Not yet implemented

## Story

As a Financial Copilot product operator,

I want to measure misunderstood questions, clarification success, routing quality, and language safety,

so that the semantic layer improves from evidence without automatically introducing unsafe aliases or regressions.

## Business Context

Features 017, 018, 028, and 046 provide evaluation, observability, missing-answer feedback, and metric alias-learning foundations. Current feedback does not consistently cover all V2 outcomes, and metric alias learning does not govern capability phrases, entities, slot vocabulary, or multi-turn dialogue quality.

This feature closes the loop for Features 117–122 with reason-coded telemetry, a versioned regression corpus, dashboards, reviewed candidate promotion, and rollout gates.

## Goals

- Measure semantic interpretation and end-to-end task success by capability.
- Capture every non-success outcome consistently without changing the user response.
- Build multilingual single-turn and multi-turn golden datasets.
- Detect wrong route, false unsupported, false no-data, language mismatch, and clarification failure.
- Propose aliases/examples from repeated evidence, with human review and rollback.
- Gate semantic-layer rollout on objective quality and safety thresholds.

## Scope

### In Scope

- Versioned semantic evaluation case/result schema.
- Reason-coded missing-answer and dialogue events across all V2/V1 routes.
- Golden datasets for capability, slots, entity resolution, outcome, language, and payload invariants.
- Offline regression runner using provider-neutral/fake model paths.
- Production aggregate dashboards and alerts.
- Candidate capability alias/presentation/slot phrase workflow.
- Human approval, collision checks, canary, and rollback.
- Spec/status evidence requirements.

### Out of Scope

- Unreviewed automatic production routing changes.
- Training a foundation model.
- Storing raw prompts in metrics/log labels.
- Using user feedback as financial truth.
- Automatically creating metrics, formulas, entities, SQL, or capabilities.

## Event and Feedback Taxonomy

Minimum semantic events/reasons:

```text
interpretation_completed
capability_not_recognized
capability_ambiguous
required_slot_missing
entity_resolved
entity_ambiguous
entity_not_found
slot_default_applied
conversation_slot_reused
clarification_requested
clarification_resolved
clarification_abandoned
supported_but_no_rows
stale_or_ineligible_data
partial_answer
provider_or_tool_failure
language_guard_applied
suggestion_presented
suggestion_selected
suggestion_resolved
legacy_semantic_route_disagreement
```

Each event uses bounded codes/versions and correlation IDs. Protected raw text may be linked through existing authorized feedback storage, never copied into high-cardinality telemetry.

## Evaluation Case Contract

A versioned case should declare:

- input message and reply language;
- optional previous turns/task state;
- expected capability candidate/winner;
- expected entity/metric/period/comparison/presentation slots and provenance;
- expected outcome/reason;
- executors that must and must not be called;
- expected structured payload invariants;
- forbidden claims/language;
- registry, policy, and dataset versions.

Financial numeric expectations must use deterministic fixtures, not LLM-generated calculations.

## Required Evaluation Suites

1. **Paraphrase:** Persian, English, mixed, colloquial, punctuation, ZWNJ, digits.
2. **Precedence:** scanner vs lookup, trend vs point, analysis vs metric, gauge vs P/S.
3. **Entity:** ticker, company name, alias, ambiguity, typo candidate, unknown, distractor tokens.
4. **Dialogue:** clarification, disambiguation, pronoun/follow-up, refinement, task switch, expiry.
5. **Outcome:** unsupported, no-data, stale, partial, timeout, exception, validation failure.
6. **Language:** deterministic Persian/English and wrong-language model fixtures.
7. **Grounding/faithfulness:** no invented metrics and verbatim analysis facts where required.
8. **Channel/persistence:** web/Telegram and live/reload semantic equivalence.
9. **Billing/security:** one accounting operation, no leakage, actor isolation.

## Learning Governance

Candidate sources may include repeated:

- unrecognized capability phrases later resolved by user selection;
- clarification answers that consistently identify the same slot/capability;
- legacy/semantic disagreements adjudicated by tests/review;
- user-selected suggested actions after an unsupported/ambiguous turn.

Promotion process:

1. aggregate and redact candidate evidence;
2. require minimum support and distinct-actor thresholds;
3. classify phrase type: capability alias, presentation, period, comparison, or metric alias;
4. check collisions against all enabled capabilities and existing metric/entity vocabularies;
5. require human approval with rationale;
6. add regression cases before activation;
7. canary by registry version;
8. monitor wrong-route/no-answer changes;
9. support immediate deactivation/rollback.

Entity aliases require separate identity-governance approval and cannot be promoted through the generic phrase workflow.

## Success Metrics

- executable-query success rate;
- wrong-route rate;
- false unsupported rate;
- false no-data rate;
- accidental language mismatch rate;
- clarification resolution rate and turns-to-resolution;
- suggestion selection and resolution rate;
- user reformulation/abandonment rate;
- ungrounded response rate;
- per-capability paraphrase coverage;
- semantic-vs-legacy disagreement rate during migration.

Thresholds must be defined per rollout stage and capability. Aggregate improvement must not hide a regression in a high-risk capability.

## Acceptance Criteria

1. Every Feature 117 outcome and Feature 118 capability can be measured by bounded code/version.
2. Missing-answer feedback covers all V2 and V1 rollback routes without affecting responses.
3. The regression runner validates capability, slots, outcome, language, called/not-called executors, and payload invariants.
4. Monthly trend golden cases include natural variants and multi-turn chart follow-up.
5. Wrong-language and raw-unknown fixtures prove Feature 117 safety.
6. Dashboards distinguish unsupported, entity failure, no rows, and technical failure.
7. No raw prompt/user text appears in metric dimensions.
8. Alias candidates cannot activate without collision checks, human approval, regression tests, and rollback metadata.
9. Capability aliases cannot create new metrics, formulas, entities, SQL, or routes.
10. Production rollout gates are evaluated per capability and registry version.
11. User/tenant privacy, retention, and access-control policies apply to protected feedback text.
12. Spec completion requires linked test/dashboard/canary evidence, not checklist edits alone.

## Dependencies

- Features `117`–`122`
- `017-ai-evaluation-and-regression`
- `018-ai-observability-and-telemetry`
- `028-missing-answer-feedback`
- `046-dynamic-metric-alias-learning`
- `047` and `056` orchestration

## Priority

**High for production rollout.** Foundations may begin earlier, but broad semantic routing must not complete without this governance layer.
