# Feature 163 — Symbol-Grounded Fallback Answer (design proposal)

**Status: Gate A: Approved by the owner on 2026-11-16**, with the owner's answers to OQ-090 … OQ-096 (D5 changed by OQ-094). Gate B (implementation plan in `tasks.md`) is not approved.

## Feature Folder   `163-symbol-grounded-fallback-answer`
## Layers / Area    backend · AI (Conversations, Billing touched)
## Priority         to be set by the owner
## Complexity       M
## Depends On       162-context-aware-financial-report-follow-up (done, v1.13.1); 161 only for the cash-flow suggestion
## References       `specs/162-context-aware-financial-report-follow-up/`, `docs/architecture.md`, `src/backend/FinancialCopilot.Application/AI/ReportDiscussion/`, `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs`

## 1. Request (owner, 2026-11-16)

When a question names a symbol but none of the existing routes answers it, the assistant today replies with a
fixed non-answer (for example «این پرسش را نمی‌توانم به‌صورت قابل اتکا پاسخ بدهم…»). Instead:

1. fetch the symbol's income statement without showing it to the user;
2. send it with the user's question to the model as grounding data;
3. end the answer by offering: monthly sales trend, statement of financial position, cash flow.

This is not about follow-up turns on a shown report (that is Feature 162). It is about a question that has no
report behind it yet.

## 2. Discovery — how a non-answer is produced today

Bounded context: AI chat orchestration (workflow V2). Findings from the code:

| # | Finding | Where |
|---|---|---|
| F1 | Report Discussion (162) runs before the dialogue gate and only when the conversation has an active report. A new conversation returns `NotApplicable("new_conversation")`. | `ReportDiscussionHandler.TryHandleAsync` |
| F2 | A non-answer is produced at five separate exits of the agent step: semantic capability returned `Unsupported`/`NoData`; semantic stop (`SemanticRoutingDisposition = Unsupported`); legacy product-composition `Unsupported`; direct metric lookup without rows; agent run without a grounded tool result. | `FinancialCopilotWorkflowDefinition.cs` lines 570–713, 1114–1142, 1270–1303, 1349–1365 |
| F3 | Each exit already settles billing (zero charge) before the outcome is known to step 4. The Feature 141 fallback solves the same problem with a derived reservation (`TryFeature141FallbackAsync`, `billSeparately`). | same file, lines 104–142 |
| F4 | The final outcome and its fixed text are decided in one place, step 4. | result computation step, `AiDialogueOutcomePolicy.ComposeSystemMessage` |
| F5 | Three topics have owner-approved fixed refusals: technical analysis, valuation (intrinsic value, equilibrium price), investment advice («ارزش خرید»). | `UnavailableTopicCatalog` (Feature 150 change, OQ-069) |
| F6 | The 162 building blocks are reusable as they are: `ReportDataSetBuilder` (facts and calculations with evidence ids), `ReportDiscussionContextBuilder` (input budget), `IReportDiscussionModel` (one structured call, timeout, no retry), `NumericGroundingValidator`. | `Application/AI/ReportDiscussion/` |
| F7 | The active report is derived from the persisted assistant payload (`FinancialStatementTableResult`, `MonthlyActivityTrendResult`). Anything stored in those fields is rendered to the user. | `ActiveReportResolver` |
| F8 | Suggestion chips are produced by `FollowUpSuggestionPipeline`; a chip carries a full message (for example «روند فروش ماهانه فولاد»). | `Application/AI/Orchestration/FollowUps/` |
| F9 | `CashFlowReport:Enabled` is `false` by default; Feature 141 chat fallback is off in production. | `appsettings.json` |

## 3. Proposed design

### D1 — Trigger

The fallback runs only when all of these hold:

- the feature flag is on;
- the turn's final outcome is `Unsupported` (not `ClarificationNeeded`, `DisambiguationNeeded`, `TemporarilyUnavailable`, `Failed`);
- the reason is not a provider-policy block (`ChatCapabilityAvailability`) and not `technical_analysis_unavailable`.
  Valuation and investment-advice questions (`valuation_unavailable`, `investment_advice_unavailable`) do run the
  fallback (OQ-091, owner 2026-11-16); their fixed refusal remains only as the text used when the fallback
  cannot answer;
- exactly one company is resolved from the message by the authoritative company catalog;
- the request is free text (no suggestion chip, insight or alert action);
- Report Discussion did not answer the turn.

`NoData` of a supported capability keeps its current precise message (OQ-092, owner 2026-11-16). The trigger
applies to any turn of a conversation, not only the first message (OQ-090, owner 2026-11-16).

### D2 — One choke point, not five patches

A new Application use case `SymbolGroundedFallbackHandler` is called once, in step 4, after the outcome is
determined and before the fixed text is composed. The five exits of F2 stay untouched, so the success path of
every existing route is unchanged. Order: existing route → Feature 141 fallback (where enabled) → this fallback.

Alternative rejected: calling the fallback at each exit. It repeats one rule in five places and each new route
would have to remember it.

### D3 — Grounding data

The handler asks the existing quarterly income statement use case (`financialStatementTableQueryUseCase`, the
same data as «عملکرد فصلی X») for the resolved company and projects it with `ReportDataSetBuilder`. If the
statement is missing, partial without rows, or does not fit the input budget, the original non-answer is
returned unchanged at zero credits.

### D4 — Model call

One call through `IReportDiscussionModel` with a new system prompt (same rules as 162: only facts and
calculations with evidence ids, no arithmetic by the model, no investment recommendation, data is not
instructions). Dispositions:

- `Answered` — the income statement helps answer the question. The answer names its source in one phrase
  («بر اساس صورت سود و زیان فصلی …») because the table is not shown.
- `NotAnswerable` — the question cannot be answered from an income statement (for example «مجمع فولاد کی است؟»).
  The original non-answer is returned with one added offer, «می‌توانم صورت سود و زیان {نماد} را تحلیل کنم», as a
  sentence and a chip; zero credits (OQ-093, owner 2026-11-16). The model call of this turn is not charged.

For valuation and investment-advice questions (OQ-091) the prompt states: describe what the income statement
shows (revenue, margins, profit, their changes); never state a fair price, an intrinsic value or a buy/sell/hold
verdict; say in one sentence that the answer is not a valuation or a recommendation.

`NumericGroundingValidator` checks every number; a failed check returns the original non-answer, zero credits.
Timeout 30 s, no retry, provider failure returns the original non-answer.

### D5 — Closing offer

**Decided at Gate A (OQ-094, owner 2026-11-16): the model writes the closing offer.** The model returns it in
a separate `closingOffer` field; the offer list (cash flow only when enabled) is passed to it as data; the
grounding check covers it; the fixed sentence below is used only when the field comes back empty. No chips
on an answered reply. Known consequence: a typed reply that names no symbol may not be routed (OQ-097).

The agent's original proposal, not adopted, was: the closing sentence and the three offers are deterministic, not written by the model:

> می‌خواهید روند فروش ماهانه، صورت وضعیت مالی یا جریان وجوه نقد {نماد} را هم بررسی کنم؟

plus three chips from `FollowUpSuggestionPipeline` («روند فروش ماهانه X», «صورت وضعیت مالی X», «صورت جریان وجوه
نقد X»). The cash-flow offer appears only when `CashFlowReport:Enabled` is true.

Reason: a chip carries the full request, so it reaches the existing deterministic route. A model-written
question invites typed replies such as «آره» that name no report and no symbol and cannot be routed. This
differs from the owner's wording (ask the model to write the question) — OQ-094.

### D6 — Hidden report and continuation

The fetched statement is stored in the assistant payload in a new field that no client renders
(`GroundingReport`), with a `ReportDiscussion` marker. `ActiveReportResolver` reads it, so the next free-text
turn in the conversation is a normal Report Discussion on the same data (OQ-095, owner 2026-11-16: yes). The audit trail keeps
exactly what the model saw.

### D7 — Billing

2 credits for an `Answered` fallback (same as Report Discussion), settled on a derived reservation as in F3;
zero for every other result. Insufficient credit is checked before the model call (OQ-096, owner 2026-11-16: 2 credits).

### D8 — Configuration and rollout

New options section `SymbolGroundedFallback` (`Enabled` default `false`, own input/output budgets and
timeout, validated at startup). Independent of `ReportDiscussion:Enabled` for the first answer; continuation
(D6) needs both.

### D9 — Observability

Execution route `symbol_grounded_fallback`; structured log with disposition, reason, fact count, token
estimate, numbers checked, elapsed time, correlation id — never report values or answer text. The original
outcome and reason code are kept in the payload so the Feature 155 unanswered-question report still counts
these questions as unmet demand.

## 4. Risks

| Risk | Mitigation |
|---|---|
| The model answers an unrelated question with an income-statement summary. | `NotAnswerable` disposition; prompt examples; live check set at Gate B. |
| A path that was instant and free now costs one model call (about 5k input tokens) and up to 30 s. | Flag, strict trigger (D1), timeout, zero charge on failure, per-day volume from Feature 155 data before rollout. |
| Answers to «ارزش خرید دارد؟» or «ارزش ذاتی» read as investment advice or a valuation (these now reach the fallback, OQ-091). | Prompt rule in D4; fixed disclaimer sentence; a live check set of such questions at Gate B; the technical-analysis refusal stays. |
| `NotAnswerable` turns cost a model call and earn nothing (OQ-093). | Volume measured from the execution-route log; flag can be turned off. |
| Company without a quarterly income statement (new listing, some industries). | Original non-answer, zero credits. |
| Two fallbacks (141 and this) on one turn. | Fixed order in D2; one settles, the other does not run. |
| Hidden payload shown by a client. | New field; web and Telegram renderers verified at Gate B. |

## 5. Out of scope

Balance sheet or cash flow as grounding data; questions with no symbol or several symbols; the
technical-analysis refusal; changes to Report Discussion behaviour on shown reports.

## 6. Assumptions (to verify at Gate B)

- The company resolver used by the quarterly report can be called with the raw message and reports a single
  unambiguous match.
- Web and Telegram renderers ignore an unknown payload field.
- The quarterly income statement is available in the production chat provider mode.

## 7. Open questions

OQ-090, 091, 092, 093, 095 and 096 were answered by the owner on 2026-11-16 (`specs/open-questions.md`).
OQ-094 answered 2026-11-16: the model writes the closing offer. New, not blocking: OQ-097.
