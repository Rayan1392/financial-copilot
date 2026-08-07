# AI Query Semantic and Dialogue Layer — Flow Review and Recommendations

**Review date:** 2026-08-06  
**Scope:** Current working tree, with emphasis on implemented features under `specs/` and the active Microsoft Agent Framework V2 path.  
**Change boundary:** This document is an architecture and product-flow review only. No application code, configuration, database schema, or tests were changed.

## Executive summary

The product already has a strong **financial metric semantic layer**: metric definitions, aliases, policies, calculators, symbol resolution, specialized query use cases, conversation memory, missing-answer feedback, and an agent workflow. The missing piece is a separate **conversational query semantic layer** that decides what the user means, whether the request is executable, what information is missing, why an execution produced no answer, and what the assistant should ask or suggest next.

Today, semantic behavior is split among:

- exact or substring phrase checks in individual routes;
- prompt instructions given to the agent model;
- local symbol extraction heuristics;
- specialized use cases that often return `null` for several different failure causes;
- result-type inference after tool execution;
- frontend rendering that understands clarification text but has no general dialogue/suggestion model.

This works for known phrasings but is brittle for paraphrases. It also means an unsupported or unresolved request can reach the model-only `Unknown` path, where the response is not grounded, is not deterministically localized, and may be vague or English.

The recommended solution is not merely a larger system prompt. Add a governed semantic layer before execution and a deterministic outcome layer after execution:

1. Normalize and interpret the message into a structured `QueryFrame`.
2. Match it against a central capability registry.
3. Resolve symbols and other entities through one canonical resolver.
4. Fill required slots from the current turn or explicit dialogue state.
5. Ask one focused clarification when execution is not yet safe.
6. Distinguish unsupported, ambiguous, missing-input, no-data, partial-data, and technical-failure outcomes.
7. Compose a localized response with relevant next actions from actual available capabilities.

The highest-priority safety improvement is to stop returning raw agent prose for unknown requests and stop treating every `null` as “data not found.”

## Sources reviewed

The review used the current code as the source of truth and used the specs to understand intended behavior. Relevant features include:

| Area | Relevant specs | Observed state |
|---|---|---|
| Financial metric semantics | `015-financial-semantic-layer` | Implemented; provides metric ontology, aliases, calculations, ambiguity handling, and metadata. It is metric-focused, not a full dialogue layer. |
| Conversation memory | `019-conversation-memory` | Implemented; enriches prompts and stores conversation context. Deterministic preflight routes still commonly inspect only the latest raw message. |
| Missing-answer feedback | `028-missing-answer-feedback` | Backend collection and classification exist. The spec explicitly excludes user-facing feedback. Native V2 side-effect coverage is incomplete for non-lookup failure paths. |
| Assisted query metadata | `034-frontend-assisted-query-metadata` | Implemented for user-initiated scanner/filter composition. It is not used to guide an unsupported or incomplete chat request. |
| Dynamic metric aliases | `046-dynamic-metric-alias-learning` | Supporting code exists, but automatic learning is disabled by default and no active configuration was found. Its scope is metric aliases, not intents, entities, or capability phrases. |
| Agent Framework orchestration | `047`, `056` | Active runtime path. `appsettings.Development.json` selects `MicrosoftAgentFrameworkV2`. |
| Direct metric routing | `072-centralized-metric-alias-routing` | Current code has a central direct-metric routing registry. Other capabilities still retain independent phrase lists and local heuristics. |
| Database metric registry | `074-database-backed-metric-registry` | Not implemented as specified. |
| Monthly trend data | `077-monthly-activity-trend-analysis` | Implemented and returns chart-ready persisted monthly snapshots. |
| Monthly trend chart UI | `078`, `113` | Implemented in current code despite stale “not implemented” status text in some specs. Some final audit/evaluation tasks remain incomplete. |

Several spec status lines and task checklists do not match the current implementation. Therefore, future architecture work should validate behavior against code and tests, not rely on the checklist status alone.

## Current query-to-response flow

```text
Web or Telegram user
        |
        v
POST /api/ai/v1/query
        |
        v
AiFacadeController
  - validates the message
  - constructs AiQueryRequest
        |
        v
MicrosoftAgentFrameworkAiQueryOrchestrationService
  - selects V2 because MicrosoftAgentFrameworkV2 is active
        |
        v
FinancialCopilotWorkflowDefinition (7 steps)
  1. conversation validation and memory/context loading
  2. billing reservation
  3. deterministic preflight routes OR agent/tool loop
  4. result computation, intent, explainability, confidence
  5. feedback/memory side effects
  6. persistence
  7. final response
        |
        v
AiQueryResponse
        |
        v
Frontend maps structured blocks and reloads persisted messages
```

Key implementation locations:

- API entry: `src/TahlilApp.Api/Controllers/AiFacadeController.cs`
- mode selection: `MicrosoftAgentFrameworkAiQueryOrchestrationService`
- active workflow: `FinancialCopilotWorkflowDefinition`
- trend intent parsing: `MonthlyActivityTrendIntentRules`
- trend retrieval: `MonthlyActivityTrendQueryUseCase`
- frontend send/map flow: chat API/query functions and `AssistantBlock`

### Step 3: two different routing systems

The active workflow first checks deterministic routes for specialized capabilities, including:

- monthly sales quality ranking;
- disclosure listing;
- monthly activity/sales trend;
- product revenue mix;
- financial statement tables;
- financial statement period analysis;
- P/S gauge;
- direct metric lookup and direct metric follow-up.

If none matches, the message goes to the agent model. The agent can invoke three tools:

- `screen_stocks`;
- `lookup_symbol_metrics`;
- `query_comprehensive_analysis`.

The system prompt also tells the model to answer in the user's language and briefly explain supported help when no tool fits. Those are prompt instructions, not enforced response contracts.

### Step 4: intent is inferred after execution

For agent-routed messages, the final intent is largely inferred from which result objects are non-null. If the agent invokes no known tool and returns prose, the response becomes `Unknown`.

Grounding checks are applied to scanner and symbol-lookup prose. They are not equivalently applied to the `Unknown` path. Consequently, raw candidate prose can become the final response even though it has no supporting capability result. This is the main source of meaningless, ungrounded, or wrong-language answers.

### Current public response contract

`AiQueryResponse` already exposes useful fields:

- `Intent`;
- structured results for scanner, lookup, analysis, trends, statements, and other routes;
- `ClarificationRequired` and `ClarificationMessage`;
- `TextAnswer`;
- confidence and explainability fields.

However, it does not represent:

- what the semantic layer understood;
- missing slots;
- ambiguity candidates;
- unsupported portions of a request;
- the reason no data was returned;
- a stable outcome such as `Unsupported`, `NoData`, or `Failed`;
- contextual next actions;
- response language as an enforced property.

### Frontend behavior

The frontend can render a clarification message, text, tables, and chart data. Suggested follow-up questions are currently primarily associated with scanner explainability. Unknown and no-data outcomes do not receive a general list of capability-driven actions.

The new-chat UI also contains fixed example prompts. At least some advertised prompts, such as a broad market summary or portfolio analysis, do not have a corresponding active deterministic route or agent tool. This can send the user directly into the least-governed `Unknown` path.

## Existing supported capability surface

The following table describes the effective capability surface in the active path. It should become the starting point for a central capability registry.

| Capability | Typical required input | Current execution route | Main output |
|---|---|---|---|
| Stock screening | metric conditions and thresholds | `screen_stocks` | ranked metric table |
| Symbol metric lookup | symbol + one or more metrics | direct route or `lookup_symbol_metrics` | metric table |
| Comprehensive analysis | symbol and optional analysis topic/date | `query_comprehensive_analysis` | faithful analysis posts |
| Monthly activity trend | symbol; optional metric/period | deterministic use case | chart-ready time series |
| Product revenue mix | symbol and optional period | deterministic use case | product/revenue composition |
| Financial statement table | symbol + statement/period details | deterministic use case | structured statement table |
| Financial statement period analysis | symbol + comparison/period | deterministic use case | period analysis |
| Disclosure listing | symbol/topic/date filters | deterministic use case | disclosures |
| Monthly sales quality ranking | ranking/filter requirements | deterministic use case | ranked results |
| P/S gauge visualization | symbol | deterministic use case | gauge model |
| Personalized insight explanation | context-dependent | intent exists; support is narrower than its name implies | explanatory text |
| Clarification | missing information | partially represented | plain clarification text |
| Unknown | no recognized result | raw agent prose | unstructured text |

`Clarification` and `Unknown` should be outcomes of semantic interpretation rather than peer business capabilities.

## Case study: “chart of the sales trend”

Consider this natural Persian request:

```text
چارت روند فروش فولاد
```

The monthly trend route detects `روند فروش` through substring matching. Its local extraction then removes the matched phrase, leaving roughly:

```text
چارت فولاد
```

The stop-word set filters words such as “trend,” “chart” in some supported forms, and “sales,” but does not cover every paraphrase. In this example, `چارت` can become the first symbol candidate. The use case then attempts to resolve `چارت` as a company symbol and reports that no trend data exists for that “symbol.”

This is not a data problem. It is an interpretation problem incorrectly presented as a no-data result.

Related weaknesses are:

- Phrase matching is local to the capability and depends on an enumerated vocabulary.
- Entity extraction occurs before canonical company resolution.
- Each route can develop a different symbol-extraction behavior.
- A company name, alias, Persian/Arabic character variation, punctuation, or ZWNJ may affect routing differently.
- An ambiguous company and an unknown company can both collapse to `null`.
- A follow-up such as `نمودارش رو بده` cannot reliably reuse the prior symbol in deterministic preflight routing, even if the conversation memory exists in the prompt context.
- Some deterministic branches place clarification prose in `TextAnswer` but do not consistently propagate `ClarificationRequired = true` and a `ClarificationMessage` into the public contract.

The desired interpretation is structured:

```json
{
  "language": "fa",
  "capability": "monthly_activity_trend",
  "entities": [{ "type": "symbol", "raw": "فولاد", "canonical": "فولاد" }],
  "metric": "monthly_sales",
  "presentation": "chart",
  "missingSlots": [],
  "confidence": 0.96
}
```

The word `چارت` belongs to the presentation slot. It must never compete with `فولاد` for the symbol slot.

## Root causes

### 1. Metric semantics and conversational semantics are conflated

The existing semantic layer is effective at answering “what does this metric mean and how is it calculated?” It does not answer “what is the user trying to do, which capability can do it, and what should happen if information is missing?”

### 2. Capability knowledge is duplicated

Supported phrases and business rules appear in the system prompt, deterministic intent-rule classes, direct metric routing, parser logic, frontend suggestions, and tests. These sources can drift independently.

### 3. Interpretation is not represented as data

The workflow jumps from raw text to route-specific code. There is no shared object describing capability candidates, resolved entities, requested metric, time range, comparison, output format, confidence, or missing information.

### 4. Failure causes collapse into `null` or prose

The system needs to distinguish at least:

- missing required input;
- ambiguous symbol or capability;
- unknown symbol;
- unsupported request;
- supported request with no stored rows;
- supported request with stale data;
- partial result;
- policy restriction;
- temporary tool/provider failure.

These cases require different user messages and different telemetry.

### 5. Memory is context, not dialogue state

Conversation memory helps the LLM, but route-specific preflight logic does not consistently consume a typed state such as `activeSymbol`, `activeCapability`, `requestedPeriod`, or `pendingClarification`.

### 6. Unknown output is prompt-governed only

“Reply in the user's language” and “explain what you can help with” are useful prompt rules, but neither is guaranteed or validated. The least-understood requests therefore receive the least-controlled responses.

## Proposed target architecture

Keep the existing financial metric layer and add a separate component around orchestration:

```text
User message + conversation state
              |
              v
1. Query normalization and language detection
              |
              v
2. Conversational semantic interpreter
   -> capability candidates
   -> entities, metrics, periods, presentation
              |
              v
3. Canonical entity/metric resolution
              |
              v
4. Dialogue policy and slot completion
   -> execute | clarify | disambiguate | guide
              |
              v
5. Existing deterministic routes / agent tools
              |
              v
6. Outcome diagnosis
   -> answered | partial | no data | unsupported | failed
              |
              v
7. Localized response and suggestion composer
              |
              v
Persistence + telemetry + frontend action chips
```

### 1. Query normalizer

Normalize Persian and Arabic character variants, whitespace, ZWNJ, punctuation, numbers, common presentation synonyms, and conversational suffixes while preserving the original message for display and audit.

Language detection should produce a stable requested reply language. For Persian input, all system-created clarification, guidance, no-data, and failure messages must be Persian, independent of model behavior.

### 2. Capability registry

Create one authoritative registry of what the product can actually answer. A capability definition should conceptually include:

```csharp
public sealed record CapabilityDefinition(
    string Code,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<string> ExampleUtterances,
    IReadOnlyList<SlotDefinition> RequiredSlots,
    IReadOnlyList<SlotDefinition> OptionalSlots,
    string ExecutionRoute,
    string OutputType,
    IReadOnlyList<string> DataRequirements,
    SuggestionPolicy SuggestionPolicy);
```

The registry should generate or feed:

- deterministic route metadata;
- agent tool descriptions and prompt capability text;
- user-facing examples;
- frontend suggestion chips;
- unsupported-request guidance;
- conformance tests.

This prevents the UI from advertising a capability the backend cannot execute.

### 3. Structured query interpretation

A conceptual contract:

```csharp
public sealed record QueryInterpretation(
    string OriginalText,
    string NormalizedText,
    string ReplyLanguage,
    IReadOnlyList<CapabilityCandidate> CapabilityCandidates,
    IReadOnlyList<ResolvedEntity> Entities,
    IReadOnlyList<ResolvedMetric> Metrics,
    PeriodSelection? Period,
    ComparisonSelection? Comparison,
    PresentationPreference? Presentation,
    IReadOnlyList<string> MissingSlots,
    IReadOnlyList<string> UnsupportedParts,
    double Confidence,
    IReadOnlyList<InterpretationEvidence> Evidence);
```

The interpreter can use both deterministic rules and an LLM, but its output must be schema-constrained and validated. The LLM proposes an interpretation; the application decides whether it is executable.

### 4. Canonical entity resolution

All capabilities should use the same entity resolver after normalization. It should return a discriminated result rather than `null`:

```text
Resolved(canonical entity)
Ambiguous(candidate entities)
NotFound(normalized input)
Missing
```

The resolver should understand ticker symbols, company names, approved aliases, character variants, and conversation references such as “it,” “this stock,” or “its chart.” Local “first non-stop-word token” extraction should not decide the final symbol.

### 5. Typed dialogue state

Maintain a small task state alongside general conversation memory:

```json
{
  "activeCapability": "monthly_activity_trend",
  "activeSymbol": "فولاد",
  "activeMetric": "monthly_sales",
  "activePeriod": "12m",
  "pendingClarification": null
}
```

Only reuse state when it is recent, unambiguous, and valid for the capability. The assistant should state its assumption when reuse could surprise the user.

### 6. Dialogue decision policy

| Interpretation/execution state | Required behavior |
|---|---|
| High-confidence capability and all required slots | Execute. |
| High-confidence capability with a slot available in valid dialogue state | Fill the slot and execute; make the reference clear in the answer. |
| High-confidence capability with one missing required slot | Ask one focused question and provide relevant examples/actions. |
| Multiple plausible symbols or capabilities | Ask the user to choose from concrete candidates. |
| Supported capability but no stored data | Say what was understood and what data was missing; offer adjacent actions that are actually available. |
| Partially supported request | Answer the supported part when useful, identify the unsupported part, and suggest a supported reformulation. |
| Unsupported request | Transparently state the limitation and show 2–4 relevant supported capabilities. |
| Tool/provider failure | Explain that the request is supported but temporarily failed; offer retry. Never label it unsupported or no-data. |

The policy should prefer a single focused question over a generic help dump. Guidance should be contextual: a sales request should suggest sales metrics, trends, and reports before unrelated features.

### 7. Explicit outcome model

Introduce a stable outcome separate from business intent:

```csharp
public enum DialogueOutcome
{
    Answered,
    PartialAnswer,
    ClarificationNeeded,
    DisambiguationNeeded,
    NoData,
    Unsupported,
    TemporarilyUnavailable,
    Failed
}
```

Extend the public response in a backward-compatible way:

```json
{
  "intent": "MonthlyActivityTrend",
  "outcome": "ClarificationNeeded",
  "replyLanguage": "fa",
  "clarificationRequired": true,
  "clarificationMessage": "روند فروش ماهانه کدام نماد را می‌خواهید؟",
  "interpretation": {
    "capability": "monthly_activity_trend",
    "missingSlots": ["symbol"]
  },
  "suggestions": [
    { "label": "فولاد", "message": "روند فروش ماهانه فولاد را نشان بده" },
    { "label": "فملی", "message": "روند فروش ماهانه فملی را نشان بده" }
  ]
}
```

Existing result fields can remain unchanged while clients gradually adopt `outcome`, `interpretation`, and `suggestions`.

### 8. Deterministic localized response composer

For clarification, ambiguity, no-data, unsupported, and technical-failure outcomes, use application-owned localized templates populated with structured facts. Do not depend on unconstrained agent prose.

The LLM may improve tone only after all facts, available actions, and language have been fixed. The final response should be rejected or replaced if it changes language, invents a capability, or introduces unsupported data.

## Recommended workflow revision

The existing seven-step workflow can be retained, but responsibilities should move:

1. **Conversation and context:** load general memory plus typed dialogue state; detect reply language.
2. **Semantic interpretation:** produce and validate `QueryInterpretation` before billing or business execution.
3. **Dialogue gate:** resolve slots and decide execute, clarify, disambiguate, or guide.
4. **Billing and execution:** reserve usage only for the execution path; invoke the selected route/tool.
5. **Outcome diagnosis:** map typed execution results to `DialogueOutcome`; do not infer solely from non-null payloads.
6. **Response, side effects, and persistence:** compose localized output, record precise feedback, update dialogue state, and persist.
7. **Final contract:** return structured results and action suggestions.

If changing the number of steps is undesirable, semantic interpretation and the dialogue gate can live at the start of current step 3, while outcome diagnosis can become the first responsibility of current step 4.

## Example target dialogues

### Natural variant that should execute

**User:** `چارت روند فروش فولاد رو بده`  
**Assistant:** Resolves capability = monthly sales trend, symbol = فولاد, presentation = chart, then returns the chart. No clarification is needed.

### Missing symbol

**User:** `روند فروش ماهانه رو نشون بده`  
**Assistant:** `روند فروش ماهانه کدام نماد را می‌خواهید؟ نام نماد یا شرکت را بنویسید؛ مثلاً «فولاد» یا «فملی».`

The response should also carry action chips derived from recent symbols when available and consent permits.

### Contextual follow-up

**User:** `فروش ماهانه فولاد چقدر بوده؟`  
**Assistant:** Returns the latest monthly sales.  
**User:** `نمودارش رو هم بده`  
**Assistant:** Uses `activeSymbol = فولاد` and returns the monthly sales chart, explicitly naming فولاد in the answer.

### Ambiguous entity

**User:** asks using an alias that maps to multiple companies.  
**Assistant:** `منظورتان کدام نماد است؟` followed by concrete symbol/company choices. It must not choose silently or report no data.

### Supported request with no data

**Assistant:** `درخواست شما را به‌عنوان «روند فروش ماهانه نماد X» تشخیص دادم، اما برای این نماد داده ماهانه‌ای در بازه موجود نیست.` It can then offer supported alternatives such as latest price, available financial metrics, disclosures, or recent analysis only when those alternatives are confirmed available.

### Unsupported prediction

**User:** asks for a guaranteed future price.  
**Assistant:** should explain in Persian that it cannot provide a guaranteed forecast, then offer concrete supported actions such as current metrics, stored analyst reports, historical sales trends, or stock screening.

### Very vague request

**User:** `کمکم کن`  
**Assistant:** should not invent an answer. It should provide a compact, capability-backed menu such as:

1. بررسی یک نماد و آخرین تحلیل‌های ثبت‌شده
2. مشاهده شاخص‌هایی مثل P/E، P/S، EPS و فروش ماهانه
3. نمودار روند فروش ماهانه یک نماد
4. فیلترکردن سهام با شرط‌های مالی

Every menu item must be generated from an enabled capability, not copied into a separate static list.

## Reuse of existing components

The proposed layer should build on current work rather than replace it:

- Reuse the financial metric ontology and alias resolver for metric slots.
- Reuse the canonical company resolver, but expose typed resolution outcomes and require all routes to consume it.
- Reuse conversation memory for background context; add typed task/dialogue state for routing.
- Reuse assisted-query metadata to populate capability-specific choices.
- Expand missing-answer feedback to record semantic and outcome reason codes.
- Reuse direct metric routing as one capability adapter under the new registry.
- Keep specialized use cases and chart-ready payloads unchanged behind the semantic execution boundary.

Dynamic alias learning should remain a reviewed promotion process. It should not automatically turn arbitrary user phrasing into production routing behavior without support thresholds, collision checks, tests, and rollback.

## Prioritized recommendations

### P0 — Make failure behavior safe and meaningful

1. Replace raw `Unknown` agent prose with a deterministic localized guidance outcome.
2. Enforce reply language after model execution; Persian input must never receive an accidental English system response.
3. Define typed failure/no-result reasons and stop using undifferentiated `null`.
4. Propagate clarification consistently through `ClarificationRequired`, `ClarificationMessage`, intent/outcome, persistence, and frontend rendering.
5. Treat agent/tool exceptions as temporary failures, not unsupported requests or empty data.

These changes provide immediate user benefit even before the full semantic interpreter exists.

### P1 — Centralize meaning and capability knowledge

1. Add the capability registry.
2. Add schema-constrained `QueryInterpretation` and confidence thresholds.
3. Route every entity through one canonical resolver.
4. Move presentation words such as `چارت`, `نمودار`, and “graph” into a presentation vocabulary, not symbol extraction.
5. Generate prompt capability descriptions and frontend suggestions from the registry.

### P1 — Add real multi-turn task state

1. Store active symbol, capability, metric, period, and pending clarification.
2. Resolve pronouns and short follow-ups against this state.
3. Clear or replace state when the user changes task.
4. Keep consent and retention behavior aligned with the existing memory feature.

### P2 — Close the learning and observability loop

Record reason-coded events such as:

- `capability_not_recognized`;
- `symbol_missing`;
- `symbol_ambiguous`;
- `symbol_not_found`;
- `metric_not_supported_for_capability`;
- `supported_but_no_rows`;
- `stale_data`;
- `provider_failure`;
- `language_mismatch_prevented`;
- `clarification_resolved`.

This is more actionable than a generic missing-answer record. Candidate aliases and examples should be promoted only after review and regression evaluation.

### P2 — Reconcile specs and product examples

- Update stale implementation statuses in specs `046`, `072`, `076`, `078`, and `113` after verifying the intended acceptance criteria.
- Remove or disable frontend example prompts that have no executable capability.
- Add a generated capability catalog to product documentation and tests.

## Proposed delivery phases

No implementation is performed by this review. A low-risk delivery sequence would be:

### Phase 0: response safety and diagnosis

- outcome enum and reason codes;
- deterministic Persian clarification/unsupported/no-data/failure messages;
- final language guard;
- consistent clarification contract;
- telemetry for all V2 branches.

### Phase 1: semantic interpretation foundation

- capability registry;
- query normalizer;
- structured query frame;
- canonical entity-resolution result;
- migrate monthly trend and direct metric lookup first.

Monthly trend is the best first migration because it demonstrates phrase variation, symbol extraction, presentation intent, time-series data, and multi-turn follow-up in one feature.

### Phase 2: dialogue and client actions

- typed dialogue state;
- focused clarification/disambiguation;
- contextual suggestion service;
- frontend action chips and capability-driven empty states;
- Telegram-equivalent rendering.

### Phase 3: migrate remaining capabilities and learn

- move all deterministic routes and agent tools under the registry;
- generate prompt/tool descriptions and example prompts;
- evaluate missed paraphrases;
- review and promote aliases;
- remove obsolete duplicated phrase logic.

## Test and evaluation strategy

### Required test dimensions

| Dimension | Examples |
|---|---|
| Paraphrase robustness | chart/graph/trend/history/show me/how has it changed, in Persian and English |
| Persian normalization | Arabic/Persian ی and ک, ZWNJ, punctuation, Persian digits, conversational suffixes |
| Entity resolution | ticker, full company name, alias, typo candidate, ambiguous name, unknown name |
| Slot completion | missing symbol, missing metric, missing statement type, missing period |
| Multi-turn state | “its chart,” “for last year,” “compare that with فملی,” task switch |
| Outcome diagnosis | unsupported, no rows, stale rows, partial result, timeout, provider exception |
| Language | Persian, English, mixed input, system-generated errors |
| Channel parity | web and Telegram produce equivalent semantic outcomes |
| Billing | clarification and unsupported paths follow an explicit charging policy |
| Grounding | no invented metric values, capabilities, symbols, or analyst conclusions |

### Minimum golden queries for monthly sales trend

```text
چارت روند فروش فولاد
نمودار فروش ماهانه فولاد را نشان بده
فروش فولاد در ماه‌های اخیر چه روندی داشته؟
تاریخچه فروش فولاد
روند درآمد ماهانه شرکت فولاد مبارکه
فولاد رو بررسی کن
نمودارش رو بده
روند فروش رو نشون بده
چارت روند فروش نمادی که قبلاً گفتم
show me FOLD's monthly sales trend
```

The evaluation set should also include adversarial words before and after the symbol to ensure presentation, politeness, dates, and filler tokens never become symbol candidates.

### Suggested product metrics

- executable-query success rate;
- wrong-route rate;
- false unsupported rate;
- false no-data rate;
- accidental language mismatch rate;
- clarification resolution rate;
- average clarification turns before execution;
- user reformulation/abandonment rate;
- unsupported guidance click/use rate;
- hallucinated or ungrounded response rate;
- per-capability paraphrase coverage.

## Acceptance criteria for the new layer

1. Every request receives a typed outcome independent of business intent.
2. Every enabled capability is represented once in the capability registry.
3. Prompt descriptions, frontend examples, and guidance actions cannot advertise disabled or nonexistent capabilities.
4. Every symbol-bearing route uses the canonical entity resolver and supports `Resolved`, `Ambiguous`, `NotFound`, and `Missing` outcomes.
5. Missing required information produces one focused clarification with `ClarificationRequired = true`.
6. A supported request with absent data is distinguishable from an unrecognized or unsupported request.
7. Persian input always receives Persian application-generated clarification, guidance, no-data, and failure messages.
8. Unknown model prose cannot bypass outcome validation and grounding policy.
9. A follow-up such as `نمودارش رو بده` can reuse a recent unambiguous symbol and metric.
10. The monthly trend golden-query suite routes all valid paraphrases to the same capability without treating `چارت` or `نمودار` as a symbol.
11. Feedback records include capability, interpretation confidence, outcome, reason code, and whether a clarification later succeeded.
12. Web and Telegram expose equivalent meaning even if their visual rendering differs.

## Non-goals

The conversational layer should not:

- invent new financial formulas or bypass the existing metric policies;
- infer unavailable database values from general model knowledge;
- generate analysis when the faithfulness policy requires stored source content;
- silently add web-search or market-data providers;
- promise forecasts or investment certainty;
- automatically promote every new phrase to a production alias;
- replace specialized business use cases with one unconstrained agent prompt.

## Recommended next specification

Create a dedicated feature spec, tentatively:

```text
117-conversational-query-semantic-and-guidance-layer
```

Its first milestone should cover the shared contracts and migrate only two representative capabilities:

1. monthly activity/sales trend;
2. direct symbol metric lookup.

This provides broad coverage of paraphrases, entity resolution, metric semantics, chart presentation, missing slots, no-data handling, and multi-turn follow-up before migrating the remaining routes.

## Final recommendation

Treat “I cannot answer this exact wording” as a normal dialogue state, not as an invitation for the model to improvise. The assistant should always know which of these situations it is in:

- it can execute now;
- it needs one specific piece of information;
- the request is ambiguous;
- it supports the request but lacks data;
- it supports only part of the request;
- it does not support the request but can offer relevant alternatives;
- the operation temporarily failed.

Once those states are explicit and backed by a central capability registry, phrase flexibility can improve without weakening financial correctness or faithfulness. The model becomes an interpreter and conversational presenter inside application-owned boundaries, rather than the sole fallback policy for everything the deterministic routes did not recognize.
