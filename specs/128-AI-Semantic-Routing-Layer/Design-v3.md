# Feature 128 — AI Semantic Routing Layer (Design v3)

Status: implementation-ready design; rollout remains gated by shadow validation.

This revision supersedes `Design-v2.md`. It applies only the four approved revisions:

1. arbitration is deterministic and evidence-led; hard vetoes outrank model confidence,
2. semantic metric codes are non-authoritative hints; the application catalog assigns canonical codes,
3. Missing, NotFound, Ambiguous, and Resolved are separate states,
4. the golden dataset is introduced incrementally at slices 3, 4, 5, 6+, 8+, and 10.

## 1. Scope and non-goals

Feature 128 adds a governed semantic routing layer to the active V2 orchestration path. It
interprets user intent and slots, resolves canonical entities, arbitrates deterministic and
model proposals, validates a registry-owned query frame, and dispatches one typed capability.

The V1 path is frozen. No V1 intent, route, metric, or parser is added or changed. Existing
Feature 125 relative-valuation and Feature 129 product-comparison/trend behavior remains behind
its current adapters until a capability mapping is explicitly registered.

This feature does not let a model select SQL, tools, providers, billing, route names, or
canonical metric definitions.

## 2. Current architecture baseline

The public entry point remains `POST /api/ai/v1/query`. In the active development configuration,
the request reaches the V2 workflow. The existing layers are retained:

```text
AiFacadeController
  -> V2 workflow runner
  -> ConversationDialogueGate
  -> interpretation + entity resolution + slot validation
  -> SemanticRoutingRolloutCoordinator
  -> SemanticExecutionCoordinator
  -> SemanticCapabilityDispatcher
  -> typed capability executor/use case
```

`IConversationalCapabilityRegistry`, `ICanonicalQueryEntityResolver`,
`ICapabilitySlotValidator`, semantic executors, billing hooks, feedback collection, and semantic
telemetry are existing seams and are the integration points for this feature.

## 3. Target routing contract

Every V2 query follows this lifecycle:

```text
raw message
  -> deterministic interpretation
  -> semantic proposal (when enabled; timeout/failure is fail-soft)
  -> deterministic arbitration with hard vetoes
  -> canonical entity and metric resolution
  -> registry/version/slot validation
  -> rollout decision
  -> one typed execution OR shadow-only comparison
```

The returned `ValidatedQueryFrame` is the only input accepted by a semantic executor. The model
does not bypass validation or dispatch directly.

## 4. Interpretation sources

The deterministic interpreter supplies baseline candidates, lexical/domain evidence, explicit
metric and entity mentions, and a fallback frame.

The semantic model may supply:

- intent/capability candidates from the governed registry,
- entity mentions and entity kind/scope hints,
- period, comparison, presentation, and missing-slot hints,
- candidate metric codes and evidence.

Model output is untrusted proposal data. Candidate metric codes are hints only. The application
resolves metric aliases and assigns canonical `MetricCode` values through the existing governed
metric catalog/direct-metric registry. An unrecognized model metric hint is retained as evidence
and cannot become an executable metric slot.

## 5. Deterministic arbitration

`SemanticArbitrator` produces one decision from deterministic evidence and the model proposal.
Its ordering is fixed and observable:

1. reject disabled, stale, or unregistered capabilities;
2. apply hard vetoes for semantic/domain incompatibility;
3. prefer resolved canonical entities and explicit user evidence;
4. prefer capability/slot compatibility from the registry;
5. use domain evidence and discrete rule scores;
6. use model confidence only as a low-authority tie-breaker;
7. if no candidate clears the gates, return clarification or disambiguation.

Resolved semantic/domain contradiction always overrides model confidence. In particular, a
resolved product-specific mention vetoes `product_revenue_mix` (a company-wide mix candidate)
unless the selected capability explicitly declares and accepts a product-scoped slot. A high
model confidence cannot override this veto.

Arbitration is pure and deterministic for the same registry version, input, and resolution
evidence. It records the selected candidate, rejected candidates, veto reason, evidence classes,
and model-confidence contribution.

## 6. Entity state model

The system preserves four distinct states:

- `Missing`: the required entity was not supplied;
- `NotFound`: a supplied mention could not be mapped to the catalog;
- `Ambiguous`: a supplied mention maps to multiple candidates;
- `Resolved`: exactly one canonical entity was selected.

`Missing` produces targeted clarification. `NotFound` produces targeted clarification or a
no-data outcome, never direct disambiguation. `Ambiguous` produces disambiguation with candidates.
`Resolved` may proceed to capability validation and execution.

## 7. Capability registry contract

Each capability owns its code, version, aliases/examples, allowed slots, route, output type,
data requirements, precedence group, and suggestion policy. Optional metadata may describe
supported intents, metric families, periods, entity scope, allowed entity combinations, execution
target, policy code, and rollout key.

The registry is the source of truth for executable capabilities, canonical metrics, and slot
compatibility. A frame with a stale registry version, undeclared slot, missing required slot,
unsupported value, ambiguous entity, or not-found entity is rejected before execution.

## 8. Product scope and Feature 129

Feature 129 use cases and intent rules are reused where a governed capability mapping already
exists. Product value and product trend are not inferred by alias alone. A product-specific
request must resolve a company-scoped product entity and use a capability whose registry metadata
accepts that scope. Until those mappings and executors are registered, the router returns an
explicit unsupported/clarification outcome and does not fall back to company-wide mix.

## 9. Rollout modes

Existing modes remain: `Legacy`, `Shadow`, `Canary`, `SemanticPrimary`, and `Rollback`.

`Shadow` evaluates the semantic candidate and compares route/frame outcomes without executing a
second business operation. It must not call both candidates, bill twice, or produce a second
provider request. `Canary` and `SemanticPrimary` execute one selected route. Rollback disables
semantic execution for the configured capability.

## 10. Billing and execution safety

`SemanticExecutionCoordinator` owns the single billing reservation/finalization lifecycle. A
validation failure does not reserve usage. A semantic execution reserves and finalizes at most
once. A shadow evaluation is telemetry only and is never an additional execution.

## 11. Failure handling

Model timeout, provider failure, invalid JSON, unregistered proposal, or catalog mismatch is
fail-soft: retain the deterministic interpretation and record the reason. Entity resolution
states are preserved rather than collapsed. Missing, not-found, and ambiguous outcomes have
separate reason codes and event names. No-data remains distinct from routing failure.

## 12. Observability

Each decision records correlation id, registry version, capability candidates, selected route,
source evidence, veto/rejection reason, entity resolution state, canonical metric codes, rollout
mode, execution status, and billing outcome. Sensitive raw prompts and financial data are not
written to telemetry.

Required counters/timers include arbitration latency, semantic proposal latency, fallback count,
hard-veto count, entity-state counts, route agreement, shadow mismatch, executor failure,
clarification rate, and billing reservation/finalization mismatch.

## 13. Testing strategy

Unit tests cover arbitration truth tables, hard vetoes, resolved entity scope, metric canonical-
ization, entity states, registry validation, exactly-once billing, and shadow non-execution.
Focused V2 regression tests cover فولاد, محصولات گرم, Feature 125, and Feature 129 behavior.

Golden tests are introduced incrementally:

- slice 3: interpretation schema and deterministic baseline;
- slice 4: proposal parsing and semantic participation;
- slice 5: arbitration and hard vetoes;
- slice 6+: entity/metric/slot resolution;
- slice 8+: rollout and shadow invariants;
- slice 10: end-to-end acceptance and regression set.

## 14. Golden dataset contract

Each case contains id, language, message, expected intent, expected capability, expected entity
state, expected canonical metric codes, expected slot state, expected rollout behavior, and
forbidden routes. Cases include explicit metric lookup, threshold screening, analysis, product
scope, unknown symbol, ambiguous mention, missing entity, and model-confidence contradiction.

The dataset is versioned with the registry and is not a production data source.

## 15. Acceptance criteria

1. V2 only; no V1 changes.
2. deterministic interpretation remains available when the model is unavailable.
3. semantic proposals participate for eligible V2 requests.
4. arbitration is deterministic and emits an audit decision.
5. hard vetoes outrank model confidence.
6. resolved product scope vetoes company-wide mix when incompatible.
7. model metric codes cannot directly become executable canonical codes.
8. canonical metric assignment is catalog-owned.
9. Missing/NotFound/Ambiguous/Resolved remain distinguishable.
10. NotFound never maps directly to DisambiguationRequired.
11. stale/disabled/unknown capabilities are rejected.
12. undeclared/duplicate/invalid slots are rejected.
13. one semantic execution maximum per request.
14. shadow mode never executes both candidates.
15. billing reserves/finalizes once for one execution.
16. Feature 125 regressions remain green.
17. Feature 129 mappings are explicit; no arbitrary product fallback.
18. focused routing tests pass.
19. full regression is reported honestly, including infrastructure skips/failures.
20. rollout remains blocked until golden shadow validation passes.

## 16. Incremental implementation slices

| Slice | Deliverable | Exit signal |
|---|---|---|
| 1 | registry metadata and versioned contracts | catalog validation green |
| 2 | proposal schema and model adapter | invalid proposals fail soft |
| 3 | semantic participation plus deterministic baseline | golden slice 3 green |
| 4 | arbitration evidence and confidence tie-break | golden slice 4 green |
| 5 | hard vetoes and product/entity scope | golden slice 5 green |
| 6 | canonical entity/metric/slot resolution | golden slice 6+ green |
| 7 | explicit Feature 129 capability mapping | no arbitrary product route |
| 8 | rollout/shadow exactly-once invariants | golden slice 8+ green |
| 9 | telemetry and operational dashboards | decision audit complete |
| 10 | end-to-end golden/regression suite | golden slice 10 green |
| 11 | bounded canary configuration | canary rollback verified |
| 12 | production shadow validation and handoff | release gate approved |

## 17. Release gate

The feature is not declared complete while focused tests fail, product mappings remain unresolved,
arbitration can be bypassed, arbitrary tool selection is possible, product resolution is not
company-scoped, billing can occur twice, or shadow can execute both candidates. If code is ready
but operational rollout is pending, the status is `COMPLETE_PENDING_SHADOW_VALIDATION`; if an
environment or mapping blocker remains, use `COMPLETE_WITH_OPERATIONAL_BLOCKERS`.
