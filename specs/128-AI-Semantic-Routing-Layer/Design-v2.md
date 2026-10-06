# Feature 128 — AI Semantic Routing Layer (Design v2)

## 1. Executive Summary

**Verdict on the current `design.md`: MAJOR_REDESIGN.** The document has the right safety principle—an LLM may understand a request but may not execute tools or SQL—but it is still an idea/discovery note. Its proposed fallback-only migration is insufficient: the current deterministic interpreter can report high confidence for the wrong capability, so low-confidence fallback cannot correct the failure.

This revision is implementation-ready for a bounded Phase 1. It adopts **dual interpretation plus arbitration**:

1. Produce a legacy/deterministic interpretation and an AI semantic interpretation for every Phase 1 query.
2. Extract entities, metrics, period, modifiers, and intent as structured data; do not let the model select an executable tool directly.
3. Resolve entities through existing canonical application services. Product resolution is scoped to the resolved company.
4. Compare interpretations using structural/domain evidence, required-slot compatibility, contradictions, and resolution outcomes—not raw confidence alone.
5. Map the winning semantic intent to a registered capability, validate the resulting frame, enforce entitlement/policy, and dispatch through the existing typed executor.
6. Execute exactly one selected capability. Shadow comparison is observational and must not duplicate financial reads, billing, or side effects.

The design uses the existing `IConversationDialogueGate`, `ICapabilityInterpreter`, `IConversationalCapabilityRegistry`, `ICanonicalQueryEntityResolver`, `ICapabilitySlotValidator`, `ISemanticCapabilityDispatcher`, `ISemanticExecutionCoordinator`, and V2 workflow. It does not introduce a second orchestration stack or extend the frozen V1 path.

## 2. Current Architecture Findings

Repository evidence was taken from the indexed codebase and the current V2 implementation.

### Existing request path

The public facade is `POST /api/ai/v1/query` in `AiFacadeController`. The configured production path is Microsoft Agent Framework V2, selected by `MicrosoftAgentFrameworkAiQueryOrchestrationService`. V2 enters `FinancialCopilotAgentWorkflowRunner` / `FinancialCopilotWorkflowDefinition` and persists the conversation exchange through existing workflow functions.

### Existing semantic foundation

The repository already contains:

- `CapabilityDefinition`, `IConversationalCapabilityRegistry`, `ConversationalCapabilityRegistry`, and `InitialConversationalCapabilityCatalog`;
- `QueryInterpretation`, `CapabilityCandidate`, `EntityMention`, `MetricSelection`, period/comparison/presentation selections, provenance, registry version, and validation;
- `ValidatedQueryFrame`, `ResolvedQuerySlot`, `ICapabilitySlotValidator`, and explicit validation states (`Valid`, `Missing`, `Ambiguous`, `Unsupported`, `Invalid`);
- `ISemanticCapabilityDispatcher` and `ISemanticExecutionCoordinator` with typed `IConversationalCapabilityExecutor` implementations;
- canonical company/industry resolution contracts and a `CompanyResolverService` used by existing financial use cases;
- structured-output provider contracts (`IAiModelExecutionService`, `AiStructuredOutputContract`, validation, timeout/fallback, usage telemetry);
- semantic routing modes (`Legacy`, `Shadow`, `Canary`, `SemanticPrimary`, `Rollback`) and bounded routing telemetry;
- conversation task state, clarification/disambiguation outcomes, billing reservation/finalization, feedback collection, and workflow telemetry;
- Feature 125 relative-valuation semantic resolution and Feature 129 monthly product comparison/trend use cases.

### Current gaps relevant to Feature 128

1. `ConversationDialogueGate` currently obtains the winning capability from `ICapabilityInterpreter` before resolution and frame construction. It is deterministic-first.
2. `HybridCapabilityInterpreter` exists, but the current registration uses `NoOpQueryInterpretationProposalProvider`; it is not the authoritative V2 interpretation path.
3. The current hybrid logic returns immediately when the deterministic interpretation is high-confidence. That is precisely the behavior Feature 128 must remove for Phase 1.
4. `SemanticRoutingRolloutCoordinator` can select shadow/primary modes, but its existing comparison contract is route-level and does not contain the entity/slot/contradiction evidence needed for arbitration.
5. Some later product capabilities are protected by deterministic phrase guards in the workflow. These are useful compatibility safeguards, but they must not become the long-term semantic contract or bypass the registry.
6. `CapabilityDefinition` already has slots, output, data requirements, execution route, version, aliases, examples, and precedence metadata, but it needs explicit supported-intent, supported-metric, policy, and target metadata for entity-aware arbitration.
7. The V1 `LlmAiIntentDetector` contains extensive phrase/rule behavior. V1 is frozen for new work; Feature 128 must integrate with V2 and must not add new V1 intents or routes.

## 3. Root Architectural Problem

The problem is not merely missing synonyms. The current routing decision conflates four different questions:

1. What does the user mean? **Intent and semantic slots.**
2. Which real domain records do the mentions identify? **Entity resolution.**
3. Which supported application operation can satisfy the request? **Capability selection.**
4. Is that operation allowed and safe for this actor and data state? **Validation, policy, and execution.**

A deterministic route can score highly because it recognizes `فولاد`, `محصول`, or `فروش`, while still selecting the wrong product-level operation. Confidence is an evidence field, not authority. A route cannot be accepted until its required entities and semantic dimensions are compatible with a governed capability.

## 4. Scope and Non-Scope

### Phase 1 scope

- V2 AI facade queries only.
- Persian, English, and mixed-language natural-language understanding.
- Dual interpretation and arbitration for the bounded capability set in Section 11.
- Structured semantic frame proposal and strict application validation.
- Company, symbol, product, period, metric, statement-item, industry, fund, index, date, comparison, and presentation semantics as applicable.
- Existing canonical entity resolution and existing typed use cases/executors.
- Targeted clarification for unresolved or materially ambiguous requests.
- Shadow/primary/canary/rollback control and auditable telemetry.
- Golden natural-language regression coverage, including the two real فولاد / محصولات گرم failures.

### Non-scope

- Direct LLM-to-SQL or LLM-to-arbitrary-tool execution.
- Replacing deterministic financial calculations, metric policies, product matching, or authorization.
- Rewriting the V1 route, adding V1 capabilities, or migrating every existing capability in one feature.
- Autonomous investment advice, portfolio actions, provider acquisition, or database schema redesign unrelated to routing evidence.
- Embeddings/vector search as a prerequisite. The Phase 1 semantic call is structured extraction/classification, not semantic database retrieval.
- Executing both legacy and semantic tools to compare answers. Compare interpretations; execute one governed capability.

## 5. Terminology

| Term | Meaning |
|---|---|
| Intent | The user’s requested outcome, such as `product_sales_value`, `product_sales_trend`, or `company_comparison`. It is not executable. |
| Entity | A domain object mentioned or implied by the query: company, product, period, metric, and so on. |
| Semantic frame | Structured, bounded representation of intent, entities, metrics, time, modifiers, presentation, and uncertainty. |
| Entity extraction | Finding mentions and roles in text. It produces raw mentions and does not assign database IDs. |
| Entity resolution | Mapping a mention to a canonical company/product/industry/etc. through application-owned authorities. |
| Capability | A governed application operation with a schema, policy, entitlement, data requirements, version, and execution target. |
| Tool/executor | The typed implementation invoked after capability validation. It is never selected or parameterized freely by the LLM. |
| Arbitration | Selection among legacy and semantic interpretations using structural and domain evidence. |
| Shadow | Compute and record an interpretation without allowing it to execute or charge a second operation. |

## 6. Target Architecture

```text
POST /api/ai/v1/query
        |
        v
V2 workflow / ConversationDialogueGate
        |
        +--> load conversation task state and normalize query
        |
        +--> Legacy interpreter -------------------+
        |                                           |
        +--> Semantic router (structured output) ---+--> extracted interpretations
                                                        |
                                                        v
                                           shared entity/metric resolution
                                                        |
                                                        v
                                           candidate compatibility checks
                                                        |
                                                        v
                                               Arbitration decision
                                                        |
                                                        v
                                      Intent -> Capability Registry mapping
                                                        |
                                                        v
                                          ValidatedQueryFrame / policy checks
                                                        |
                                                        v
                                  one SemanticExecutionCoordinator dispatch
                                                        |
                                                        v
                                          existing typed executor/use case
                                                        |
                                                        v
                                    result, persistence, billing, telemetry
```

Entity resolution is before final arbitration because a capability cannot be judged against an unresolved object. For example, `محصولات گرم` must be checked as a product mention under the resolved `فولاد` company. Capability mapping remains after arbitration because the application, not the model, owns the route registry and execution target.

The legacy route may remain operational during migration, but it is an input to the decision, not an authority that can bypass semantic understanding.

## 7. Shadow + Arbitration Strategy

### Interpretation contract

For every in-scope V2 query, the gate creates:

- `LegacyInterpretation`: the current deterministic candidate set and evidence;
- `SemanticInterpretation`: a structured proposal from the configured semantic model, or an explicit `Unavailable` result;
- `ResolvedInterpretationEvidence`: canonical resolution outcomes shared by both candidates where the mentions are equivalent.

Both interpretations are captured even when legacy confidence is high. A high legacy confidence may reduce an arbitration score, but it cannot suppress the semantic attempt.

### Rollout modes

| Mode | Interpretation work | Execution | Purpose |
|---|---|---|---|
| `Shadow` | Legacy + semantic | Legacy only, subject to safety gate | Measure disagreement without changing answers. |
| `Canary` | Legacy + semantic | Semantic winner for stable cohort; safe legacy fallback on semantic failure | Controlled exposure. |
| `SemanticPrimary` | Legacy + semantic | Arbitrated winner, normally semantic when structurally superior | Phase 1 target for the bounded scope. |
| `Rollback` | Legacy + semantic where available | Legacy only, with safety gate | Immediate operational rollback while preserving evidence. |

“Shadow” in Phase 1 means shadow comparison of interpretations, not a fallback-only implementation. The system must record cases where legacy is high-confidence but semantic evidence identifies a different capability.

### Required safeguards

- Never execute both candidate capabilities for comparison.
- Never allow a model-proposed capability code that is absent, disabled, stale, or unauthorized in the registry.
- Never turn an unresolved product into a company-level revenue-mix query merely because the company resolved.
- If arbitration cannot establish a safe winner, return targeted clarification or unsupported—not a guess.

## 8. SemanticFrame Contract

The model output is a proposal. The application creates the final `ValidatedQueryFrame` only after resolution and validation.

### Model proposal

```json
{
  "intent": "product_sales_value",
  "intentCandidates": [
    { "value": "product_sales_value", "evidence": ["چقدر فروخته"], "confidence": 0.88 },
    { "value": "product_revenue_composition", "evidence": [], "confidence": 0.18 }
  ],
  "entities": [
    { "type": "company", "raw": "فولاد", "role": "subject" },
    { "type": "product", "raw": "محصولات گرم", "role": "object", "scope": "company" }
  ],
  "metrics": [
    { "raw": "فروخته", "candidateCode": "MONTHLY_SALES", "role": "sales_amount" }
  ],
  "time": { "periodType": "latest", "current": "latest", "comparison": null },
  "modifiers": { "comparison": null, "ranking": null, "scope": "company_product" },
  "presentation": "summary",
  "replyLanguage": "fa",
  "confidence": 0.88,
  "ambiguities": [],
  "evidence": ["محصولات گرم", "چقدر فروخته"]
}
```

The proposal must not contain SQL, formulas, connection details, arbitrary tool names, executable arguments, authorization claims, or canonical IDs invented by the model. The application must reject unknown enum values, excessive collection sizes, invalid confidence ranges, unsupported language values, overlong text, and duplicate entity spans.

### Final validated frame

The existing `ValidatedQueryFrame` remains the execution boundary:

```text
ValidatedQueryFrame
  CapabilityCode
  RegistryVersion
  ResolvedQuerySlot[]
  QueryInterpretation
```

`ResolvedQuerySlot` carries type, normalized value, provenance, confidence, validation state, capability code, and a bounded detail/reason code. Only `Valid` required slots can reach an executor.

## 9. Entity Model

The semantic model supports the following first-class entity types. Not every capability requires every type.

| Entity | Required semantics |
|---|---|
| Company | Legal company identity; may be mentioned by Persian name, ticker, English name, or provider alias. |
| Symbol | Tradable ticker; linked to canonical company where applicable. |
| Product | Product title/code/unit; product resolution is scoped to the resolved company. |
| Industry | Broader classification, distinct from a comparison group. |
| Industry group | Feature 125 comparison cohort; must not be inferred from `IndustryId` alone. |
| Fund | Fund identity and fund-specific capability scope. |
| Index | Market/index identity. |
| Date | Gregorian/Jalali date mention or bounded date expression. |
| Reporting period | Monthly, quarterly, semi-annual, nine-month, annual, TTM, latest, or explicit Jalali period. |
| Financial statement item | Net profit, assets, liabilities, equity, revenue, EPS, and other governed statement items. |
| Financial ratio/metric | P/E, P/S, ROE, ROA, growth, margin, sales amount, quantity, rate, and canonical `MetricCode`. |
| Analysis topic | Technical, fundamental, equilibrium price, suspicious volume, report, and other governed topics. |
| Comparison dimension | Company-vs-company, period-vs-period, product-vs-product, industry-relative, and ranking dimensions. |
| Presentation | Table, chart, gauge, summary, or list. |

Each extracted entity has `raw`, normalized text, type, role, source span, and parent scope. Each resolved entity has canonical ID, display value, match kind, resolution state, confidence, and evidence. The model may propose a product scope; the resolver must enforce that scope.

### Required examples

`فولاد محصولات گرمش چقدر فروخته؟` must resolve approximately to:

```json
{
  "intent": "product_sales_value",
  "company": { "raw": "فولاد", "state": "resolved" },
  "product": { "raw": "محصولات گرم", "scope": "company", "state": "resolved_or_clarify" },
  "metric": { "code": "MONTHLY_SALES", "dimension": "sales_amount" },
  "period": "latest"
}
```

`روند فروش محصولات گرم فولاد؟` must use `product_sales_trend`, retain both company and product, and require a trend-capable product executor. It must not collapse to company-wide `monthly_activity_trend` without product resolution.

`ترکیب فروش فولاد چیه؟` must be distinguishable from product-specific sales: it expresses `product_revenue_composition` / the existing `product_revenue_mix` capability and does not contain a product object unless the user explicitly names one.

## 10. Entity Resolution Design

Extraction and resolution are separate stages.

1. The semantic router extracts mentions and roles only.
2. `ICanonicalQueryEntityResolver` resolves company, symbol, industry, fund, and index using canonical repositories.
3. Product resolution runs only after company resolution and is delegated to the existing company-scoped product/read-model authority. Feature 129’s deterministic product identity rules remain authoritative: stable valid product code and compatible unit first; normalized title plus compatible unit next; no fuzzy or LLM product matching.
4. Dates and reporting periods are parsed by application-owned Jalali/Gregorian period services.
5. Financial metrics and statement items resolve through the governed financial semantic catalog and aliases. The model cannot define formulas or choose an unregistered metric code.
6. Resolution outcomes are `Resolved`, `Ambiguous`, `NotFound`, or `Missing`, with bounded candidates and evidence.

The resolver must preserve scope: a product mention is not globally resolved, and an industry-relative request must validate the correct `GroupId`/cohort semantics used by Feature 125. `NotFound` and `Ambiguous` remain distinct from `Missing` so the response can ask a targeted question.

## 11. Capability Registry Design

Reuse `IConversationalCapabilityRegistry` and `CapabilityDefinition`. Extend the existing definition/adapter rather than creating a parallel registry.

Each capability must expose:

- `CapabilityId`/code and immutable registry version;
- supported intent(s), including allowed intent aliases;
- required and optional slots;
- supported metric codes and period types;
- allowed entity combinations and scope rules;
- execution route and typed executor target;
- output type and data requirements;
- policy/permission requirement and entitlement code;
- enabled/availability status;
- bounded multilingual aliases/examples used only as model context and deterministic evidence;
- rollout mode and deprecation/version metadata.

### Phase 1 intent-to-capability coverage

The initial catalog is intentionally bounded:

| Semantic intent | Existing/target governed capability |
|---|---|
| `company_lookup` / point lookup | `symbol_metric_lookup` where the requested result is an existing metric/company lookup. |
| `company_comparison` | Feature 125 relative-valuation routes, especially `symbol_pair_within_industry`; cohort validation remains application-owned. |
| `monthly_sales_analysis` | Existing `monthly_activity_trend` or deterministic monthly comparison route based on period/product dimensions. |
| `product_sales_value` | Product-scoped monthly product sales executor/adapter; no company-wide fallback when product is explicit. |
| `product_sales_trend` | Product-scoped monthly product trend executor/adapter, using Feature 129 read contracts where supported. |
| `product_revenue_composition` | Existing `product_revenue_mix`. |
| `financial_statement_value_search` | Existing `financial_statement_value_search`. |
| statement-period analysis | Existing `financial_statement_period_analysis`. |

The registry is the only source of executable capability availability. A semantic intent without an enabled compatible capability becomes `Unsupported` or a targeted clarification; it does not produce an ad hoc tool call.

## 12. Arbitration Algorithm

Arbitration produces a decision object containing the selected interpretation, capability candidate, score components, vetoes, and reason code.

### Hard vetoes

- Required company/product/period/metric is missing, invalid, or ambiguous.
- Product-scoped semantic evidence conflicts with a company-wide capability schema.
- Candidate capability is disabled, unregistered, version-stale, unauthorized, or has no executor.
- Required metric or period is unsupported by the capability.
- Entity resolution yields contradictory companies or incompatible industry membership.
- The only available interpretation is model-proposed with invalid structured output.

### Evidence score

Scores are normalized within the candidate set; they are not compared as calibrated probabilities across systems.

```text
candidateScore =
    0.20 * intentFit
  + 0.20 * requiredSlotCompleteness
  + 0.20 * entityResolutionQuality
  + 0.15 * capabilitySchemaCompatibility
  + 0.10 * metricPeriodCompatibility
  + 0.05 * linguisticEvidence
  + 0.05 * legacySemanticAgreement
  + 0.05 * modelConfidence
  - contradictionPenalty
```

`contradictionPenalty` is a deterministic policy value and can veto the candidate. Required-slot completeness and entity resolution quality are calculated by the application. Model confidence is intentionally only one small component.

### Decision outcomes

- `Resolved`: one candidate passes all hard checks and exceeds the configured margin over the next compatible candidate.
- `ClarificationRequired`: required information is missing or the top candidates are materially tied.
- `DisambiguationRequired`: one or more entities have bounded competing candidates.
- `Fallback`: semantic service is unavailable, but the legacy candidate passes the safety gate and no semantic-only contradiction is known.
- `Unsupported`: the user intent is understood but no enabled capability supports it.
- `Rejected`: malformed/untrusted output, policy violation, or invalid frame.

The minimum margin, weights, and veto rules are versioned policy, not prompt text. Every decision records the policy version.

## 13. Confidence and Ambiguity Model

Maintain separate values:

- `semanticConfidence`: model’s bounded interpretation confidence;
- `legacyConfidence`: deterministic candidate confidence;
- `entityResolutionConfidence`: weakest required resolved entity evidence;
- `arbitrationConfidence`: application-calculated confidence in the selected candidate after compatibility checks;
- `dataConfidence`: freshness/completeness of the eventual data result, where an existing result contract supports it.

Confidence bands are `Low`, `Medium`, and `High`, but a high band never overrides a hard veto. The response and telemetry must distinguish:

`Resolved`, `ClarificationRequired`, `Fallback`, `Unsupported`, and `Rejected`.

Clarifications must be targeted, for example: “Which company do you mean?”; “Which product under فولاد do you mean?”; “Do you want the latest month or a comparison?”; or “Should I compare these companies within the same industry group?”

## 14. Integration With Existing Routing

### V2 integration

The smallest safe integration is to evolve `ConversationDialogueGate.PrepareAsync` into an interpretation coordinator:

1. Run deterministic interpretation.
2. Run the semantic proposal provider through the existing structured-output model execution service.
3. Convert both to a common `QueryInterpretation` shape; preserve provenance (`ModelProposed`, `UserExplicit`, `PolicyDefaulted`, or `ConversationInferred`).
4. Resolve entities and validate candidate slots.
5. Arbitrate and build `ValidatedQueryFrame` or a clarification/fallback result.
6. Pass the selected frame into the already existing semantic execution branch.

`FinancialCopilotWorkflowDefinition` and `FinancialCopilotAgentWorkflowRunner` should continue to call `ISemanticExecutionCoordinator`; the feature does not add a second executor or change typed use-case contracts. `AiQueryRequest.SemanticFrame` carries the selected frame; `SemanticShadowFrame` may carry the non-executing comparison frame only for telemetry/replay.

### Legacy compatibility

The current legacy agent/tool loop remains available for routes not yet admitted to the semantic scope and for explicit rollback. Existing response DTOs, conversation persistence, Billing hooks, and API facade contracts remain compatible. New semantic metadata is additive and bounded.

### V1 boundary

Do not add Feature 128 behavior to `LlmAiIntentDetector` or other frozen V1 routing. If the API mode is V1, preserve current behavior and report the mode in telemetry. The semantic-primary implementation is a V2 integration concern.

## 15. Failure Handling

| Failure | Required behavior |
|---|---|
| Semantic timeout | Record timeout; use safe legacy fallback only if structural safety checks pass; otherwise clarify. |
| LLM unavailable/provider error | Same as timeout; no arbitrary tool execution. |
| Invalid JSON/schema | Reject semantic proposal, record redacted validation reason, use safe fallback or clarification. |
| Unknown intent/capability | `Unsupported` or targeted clarification; never infer a new route. |
| Entity not found | `DisambiguationRequired` with the normalized mention; do not query using raw text as an ID. |
| Entity ambiguous | Ask for a choice; preserve bounded candidates in task state. |
| Product cannot resolve under company | Ask for product clarification or return product data unavailable; do not widen to company totals. |
| Semantic/legacy disagreement | Arbitrate. If tied or contradictory, clarify; record both interpretations. |
| Capability unavailable/unauthorized | `Unsupported` or policy-safe response; do not expose registry internals. |
| Partial extraction | Execute only if all required slots are valid; otherwise clarify. |
| Executor failure | Existing typed status mapping and Billing finalization apply; no retry that duplicates a financial side effect. |

For semantic model failure, the fallback is a routing fallback, not permission to bypass application validation.

## 16. Observability

Use the existing correlation ID, `ISemanticDialogueEventSink`, `ISemanticRoutingTelemetrySink`, workflow telemetry, provider telemetry, and OpenTelemetry activities. Extend the event payload or linked trace attributes so developers can answer “Why did this query select this capability?”

Record, with tenant/privacy policy applied:

- query ID/correlation ID, channel, workflow version, registry version, policy version;
- original query only when permitted; otherwise bounded redacted text and a stable query hash;
- legacy interpretation: candidates, route, confidence, evidence categories;
- semantic interpretation: model/provider/model version, structured frame, confidence, validation status;
- extracted entities, resolved entities, unresolved/ambiguous entities, and resolution evidence;
- candidate compatibility scores, vetoes, arbitration decision, and reason code;
- selected intent, capability, execution route, executor status, and result category;
- fallback, clarification, unsupported, rejection, and rollback outcomes;
- provider latency, timeout, token usage, estimated cost, cache status, and Billing outcome;
- selected tool/use case and execution duration.

Do not log secrets, raw authorization headers, full sensitive conversation memory, or unrestricted provider prompts/responses. Prompt/response content capture is opt-in, redacted, sampled, and retention-controlled. A bounded semantic decision record is the minimum audit artifact.

## 17. Performance and Cost

- Use the existing provider-neutral structured-output execution service and a lightweight routing workload/model where configured.
- Phase 1 budget: one semantic interpretation call per in-scope query, bounded by the existing provider timeout plus a routing-specific upper bound; no second semantic call for arbitration.
- Avoid duplicate classification: the V2 semantic coordinator owns the semantic call; the legacy detector is not invoked again for the same request.
- Cache only deterministic, non-sensitive proposals where allowed, keyed by normalized query hash, language, conversation task-state version, registry version, prompt version, and model version. Never reuse a frame across tenants or incompatible task state.
- Shadow interpretation is one model call and no second tool execution. Billing records one user operation and one semantic model usage fact according to existing pricing policy.
- Keep the model prompt bounded by projecting only enabled Phase 1 registry metadata. Do not send the entire database catalog.
- If the semantic call exceeds the latency budget, return a safe deterministic result only when the fallback safety gate passes; otherwise clarify.

## 18. Security and Policy Boundaries

- The LLM has no database, repository, network, or arbitrary tool access.
- The model sees only the bounded query/context required for interpretation, subject to memory consent and redaction policy.
- Capability registry membership, enabled state, tenant entitlement, permissions, route availability, and data authorization are application-owned.
- Canonical IDs are assigned by resolvers, never trusted from model output.
- Slot values are bounded and validated before reaching an executor.
- Product/company scope is enforced by the application.
- Existing tenant isolation, actor-scoped billing, rate limits, and authorization remain unchanged.
- User-visible responses expose safe reason categories, not internal prompts, provider details, registry diagnostics, or hidden policy decisions.

## 19. Migration Plan

### Slice 1 — Contract and policy baseline

Define the semantic proposal schema, frame state model, arbitration policy version, reason codes, and compatibility rules. Add no new production route.

### Slice 2 — Registry metadata completion

Extend the existing `CapabilityDefinition`/catalog with supported intents, metric/period compatibility, entity scope, policy/entitlement, and target metadata. Register only bounded Phase 1 capabilities.

### Slice 3 — Semantic proposal provider

Implement the structured semantic provider behind `IAiModelExecutionService`. Use the registry projection as bounded context. Validate and reject malformed/unknown output.

### Slice 4 — Shared extraction and resolution adapter

Connect the proposal to existing canonical company/industry resolution, product-scoped read-model resolution, period parsing, and governed metric aliases. Preserve distinct resolution states.

### Slice 5 — Entity-aware arbitration

Replace deterministic high-confidence short-circuiting with dual interpretation, hard vetoes, compatibility scoring, margin policy, clarification, and safe fallback.

### Slice 6 — V2 gate integration

Make the gate produce `ValidatedQueryFrame` from the arbitration result. Preserve task-state clarification/disambiguation and keep `SemanticShadowFrame` observational.

### Slice 7 — Product capability admission

Register/adapt product sales value/trend capabilities using existing Feature 129 typed use cases where supported. Remove product-specific workflow bypasses only after equivalent registry-backed tests pass.

### Slice 8 — Shadow rollout

Run legacy and semantic interpretation for the Phase 1 scope, execute the safe legacy winner, emit comparison telemetry, and verify disagreement dashboards.

### Slice 9 — Canary and rollback

Enable semantic-primary arbitration for a stable actor/tenant cohort, with immediate configuration rollback and fallback safety checks.

### Slice 10 — Regression and evaluation gate

Run unit, integration, architecture, golden dataset, latency/cost, and privacy tests. Promote only when wrong-route, unsupported, clarification, and language-safety thresholds pass.

### Slice 11 — Operational readiness

Document dashboards, alert thresholds, retention, prompt/model/registry versioning, and on-call diagnosis steps.

### Slice 12 — Cleanup

Remove obsolete capability-specific phrase bypasses only after production evidence and a rollback window. Keep legacy interpretation as a fallback until the capability is explicitly retired.

## 20. Testing Strategy

### Unit tests

- Proposal JSON schema, bounds, unknown enum/capability rejection, and provenance.
- Intent/entity/metric/period semantic-frame parsing.
- Persian possessive and word-order variants without phrase-specific examples.
- Company and product scoped resolution, including ambiguous/not-found/missing states.
- Metric and reporting-period catalog resolution.
- Required/optional slot validation and capability compatibility.
- Arbitration score components, hard vetoes, tie/margin behavior, and high-legacy-confidence disagreement.
- Safe fallback and clarification outcomes.
- Registry version/availability/policy checks.

### Integration tests

- `POST /api/ai/v1/query` through V2 with a fake structured-output provider.
- Semantic frame to existing typed executor/use case.
- Product/company scoping through persisted monthly product data.
- Feature 125 company/group validation remains deterministic.
- Billing reservation/finalization occurs once; shadow interpretation does not charge or execute twice.
- Conversation task state preserves clarification and disambiguation.
- Persistence/replay retains selected semantic capability and registry/policy versions.

### Architecture tests

- No production semantic route can bypass `ValidatedQueryFrame` and `ISemanticExecutionCoordinator`.
- No semantic layer references infrastructure persistence directly.
- No LLM output path can invoke arbitrary tools or SQL.
- V1 remains frozen.
- Every Phase 1 executor is registered in the capability registry and has required metadata.

### Golden evaluation

Assert intent, extracted entities, resolved entities, selected capability, arbitration outcome, clarification/fallback state, and execution result category—not only final prose.

## 21. Golden Regression Dataset

The following cases are mandatory and must include Persian/English/mixed variants, possessive forms, reordered phrases, and negative controls.

### Product sales value

- `فولاد محصولات گرمش چقدر فروخته؟`
- `فروش محصولات گرم فولاد چقدر بوده؟`
- `فولاد از محصولات گرم چقدر درآمد داشته؟`
- `How much did فولاد sell in hot products?`

Expected: `product_sales_value`, company `فولاد`, product `محصولات گرم`, sales amount metric, latest period; never `product_revenue_composition` merely because “product” appears.

### Product sales trend

- `روند فروش محصولات گرم فولاد؟`
- `روند محصولات گرم فولاد چطوره؟`
- `فروش ماهانه محصولات گرم فولاد رو نشون بده`
- `Show the recent monthly trend for فولاد hot products`

Expected: `product_sales_trend`, product scoped to فولاد, trend presentation/period; never company-wide monthly trend without product validation.

### Composition distinction

- `ترکیب فروش فولاد چیه؟`
- `فولاد بیشتر از چه محصولی درآمد دارد؟`
- `کدام محصول بیشترین فروش فولاد را دارد؟`

Expected: `product_revenue_composition` / `product_revenue_mix`, no invented explicit product unless named.

### Company comparison and ordinary lookups

- `کگهر و کگل را مقایسه کن`
- `فولاد P/E چقدر است؟`
- `P/E فولاد`
- `فروش ماهانه فولاد را نشان بده`
- `فولاد را بررسی کن`

Expected: comparison, point metric lookup, monthly metric lookup, or analysis capability respectively; assert the correct distinction.

### Negative controls

- `تحلیل بده` → clarification because no symbol/topic is supplied.
- `ترکیب فروش محصولات گرم فولاد` → composition only if the query asks for composition; otherwise clarify the product-level objective.
- unknown company/product → targeted disambiguation/no-data state, never a company-wide fallback.

## 22. Acceptance Criteria

1. Natural-language understanding for Phase 1 does not depend on a fixed sentence grammar or one phrase-specific fix.
2. Intent and capability are separate concepts in the design and contracts.
3. The LLM cannot directly execute arbitrary tools, access SQL, or choose an unregistered execution target.
4. Semantic output is structured, bounded, versioned, and schema-validated.
5. Entity extraction and entity resolution are separate stages.
6. Product is a first-class entity.
7. Product resolution can be scoped to a resolved company and cannot silently widen to company scope.
8. A high-confidence legacy route cannot automatically bypass semantic understanding for Phase 1 queries.
9. Phase 1 includes shadow comparison plus arbitration, with one final execution path.
10. Arbitration does not rely solely on comparing confidence numbers.
11. Capability compatibility and required entities participate in arbitration.
12. The current legacy execution path remains operational during migration and rollback.
13. `روند فروش محصولات گرم فولاد؟` is represented as product-scoped product sales trend semantics.
14. `فولاد محصولات گرمش چقدر فروخته؟` is represented as product-scoped product sales value semantics.
15. `ترکیب فروش فولاد چیه؟` is distinguishable from product-specific sales queries.
16. Persian possessive forms and word order are handled without hardcoding `فولاد` or `محصولات گرم`.
17. Low confidence, unresolved entities, contradictions, or tied candidates can lead to targeted clarification.
18. The complete routing decision is observable and auditable with correlation, registry, policy, interpretation, resolution, arbitration, and outcome evidence.
19. Semantic model timeout, invalid output, or unavailability has a safe fallback/clarification behavior.
20. Phase 1 remains bounded, reuses existing orchestration/semantic/provider abstractions, and does not require rewriting all Financial Copilot capabilities.

## 23. Implementation-Ready Interface Responsibilities

The implementation should assign responsibilities as follows:

| Responsibility | Existing seam to reuse | Feature 128 change |
|---|---|---|
| Model call and structured validation | `IAiModelExecutionService`, `AiStructuredOutputContract` | Add semantic proposal workload/contract and provider-neutral prompt/version metadata. |
| Legacy interpretation | `ICapabilityInterpreter`, `DeterministicCapabilityInterpreter` | Expose as one candidate; remove high-confidence bypass. |
| Semantic interpretation | `IQueryInterpretationProposalProvider` | Replace no-op provider for enabled Phase 1 scope. |
| Entity resolution | `ICanonicalQueryEntityResolver`, `ICanonicalQueryIndustryResolver`, existing company resolver | Add product-scoped adapter and evidence projection. |
| Capability metadata | `IConversationalCapabilityRegistry`, `CapabilityDefinition` | Add intent/metric/policy/scope metadata. |
| Frame validation | `QueryInterpretationValidator`, `ICapabilitySlotValidator` | Validate the arbitrated candidate and create `ValidatedQueryFrame`. |
| Arbitration | New application policy service under existing orchestration contracts | Score compatibility and issue decision/reason codes. |
| Execution | `ISemanticExecutionCoordinator`, dispatcher, typed executors | Keep one execution path; no arbitrary model tool call. |
| Rollout | `ISemanticRoutingRolloutCoordinator` | Record rich comparison evidence and enforce mode decisions. |
| Telemetry/evaluation | semantic dialogue/workflow telemetry and golden evaluation services | Add frame/resolution/arbitration fields and regression gates. |

## 24. Risks and Open Questions

### Risks

- A small model may extract the product span correctly but fail to distinguish value, trend, and composition; the registry schema and golden set must make these dimensions explicit.
- Product vocabulary and provider data may be incomplete; `NoData` and `NotFound` must not be treated as routing success.
- Duplicate semantic and legacy telemetry can inflate cost or obscure which path executed; one coordinator must own the semantic call and one trace must own the decision.
- Existing phrase guards may mask semantic regressions. They should remain only as temporary compatibility checks with explicit telemetry.
- Registry metadata drift can create valid-looking frames with no compatible executor; startup validation and integration tests must reject this state.

### Open questions to resolve before Slice 3

1. Which configured provider/model is approved for the semantic-routing workload, and what is the exact p95 latency budget for web and Telegram channels?
2. Should product sales value/trend use a new registry capability code or an adapter over Feature 129’s existing `monthly_product_comparison` / `monthly_product_trend` contracts?
3. What is the authoritative product catalog/read model for company-scoped resolution when product code/title/unit data is incomplete?
4. Should semantic proposal content be persisted in conversation payloads by default, or only a redacted decision summary plus hash be retained?
5. Which tenant/actor cohort and minimum observation window are required before moving each capability from Shadow to Canary/SemanticPrimary?
6. Which exact Billing operation code prices the semantic interpretation, and is it included in the existing operation charge or separately metered?

These questions do not block the architecture. They must be answered before production rollout of the affected slice.

FEATURE_128_DESIGN_REVIEW_COMPLETE
