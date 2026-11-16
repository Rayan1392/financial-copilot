# User Story: Report Discussion — Context-Aware Financial Report Follow-up

## Feature Folder

`162-context-aware-financial-report-follow-up`

## Layers / Area

Layers: backend
Area: AI · Conversations · Billing

## Priority

P1

## Complexity

L

## Depends On

154-ai-balance-sheet-report, 161-ai-cash-flow-statement-report (cash-flow family only; see `tasks.md` "Before Starting")

## References

- `specs/162-context-aware-financial-report-follow-up/design-proposal.md` (approved at Gate A on 2026-11-15; its "Gate A Decisions" table D1–D9 supersedes the body)
- `specs/POLICY-V1-FREEZE.md` (no V1 work)
- `docs/domain-glossary.md` terms used: conversation, assistant message, financial statement, monthly activity, credit
- V2 workflow: `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs` (`PrepareStartMessageAsync`, step 3)
- Conversation data: `src/backend/FinancialCopilot.Application/Conversations/ConversationContracts.cs` (`AssistantMessagePayload`, `MessageRecord`, `IMessageRepository`), `ConversationDbContext` (no schema change)
- Report payloads: `FinancialStatementTableResult` (quarterly, `BalanceSheetReport`, `CashFlowReport`), `MonthlyActivityTrendResponse`
- Task state: `src/backend/FinancialCopilot.Application/AI/Orchestration/ConversationTaskStateContracts.cs` (`GetActiveAsync`, `PendingDialogueAction`)
- Billing: `src/backend/FinancialCopilot.Infrastructure/Billing/DefaultPricingPolicy.cs`, `AiFacadeBillingHook.cs`
- Model calls: `src/backend/FinancialCopilot.Application/AI/ModelProviders/AiModelContracts.cs` (`AiModelRequest`, `AiWorkloadKind.ExplanationGeneration`)
- Related specs: `specs/154-ai-balance-sheet-report/`, `specs/161-ai-cash-flow-statement-report/`, `specs/155-admin-unanswered-question-demand-report/`

## Standards Source Of Truth

Obey `.claude/rules/*`. Do not copy rule text into this spec.

## User Story

As a Sapio user who has just received a financial report in a conversation,
I want to keep asking questions about that report in the same conversation,
so that I get grounded, CFO-level explanations of its numbers without re-requesting the report or being re-routed.

## Business Context

Today every message is re-classified, so a natural question like «چرا حاشیه سود افت کرد؟» right after an income statement often misroutes or asks for clarification. Report Discussion turns the conversation itself into the context: once a supported report is shown, later messages are answered from that report's stored data by one bounded model call, with numbers that always trace to the report.

## Scope

- Report families (D1): quarterly income statement, balance sheet (154), cash-flow statement (161), company monthly sales report (`MonthlyActivityTrendResponse`).
- Active report derivation from persisted assistant payloads (D6); no new table, no migration.
- One model call per discussion turn returning answer + disposition (D3); hand-back to normal V2 routing for `NeedsDifferentData` / `OutsideReport`.
- Deterministic calculations (D5) and a post-answer numeric grounding check.
- Pricing (D2): 2 credits cash-flow, 1 credit other families, 0 for non-answers and hand-backs.
- V2 only, behind `ReportDiscussion:Enabled` (default `false`).
- Interpretation suggestion first under a shown quarterly income statement, balance sheet or monthly sales trend, at most 5 suggestions per answer (change [`report-interpretation-suggestion`](changes/report-interpretation-suggestion/design-proposal.md), 2026-11-16).

## Out Of Scope

- Rolling conversation summary (→ `specs/backlog.md`).
- Monthly product comparison/trend and monthly sales quality ranking payloads (OQ-088).
- Consolidated statements beyond what the source report already shows.
- Frontend changes, evidence links in the UI, suggestion chips on discussion answers.
- Telegram-specific behavior beyond what the shared V2 API already gives.
- Any V1 change.

## Functional Requirements

1. **Activation.** With the flag on in V2 mode, a message in an owned existing conversation enters Report Discussion only when (a) the conversation has an active report (FR-2) and (b) `ConversationTaskState` has no pending clarification/disambiguation. No keyword or lexical rule decides eligibility.
2. **Active report.** Read the last 20 messages of the conversation (bounded, tenant/actor scoped). The active report is the most recent assistant payload that holds a supported typed report with `Outcome = Answered`, provided every later assistant message carries a `ReportDiscussion` marker pointing to it with disposition `Answered` or a zero-credit non-answer. Any other later assistant message (a normal-route answer, or an `OutsideReport` hand-back) makes it inactive. A newer supported report replaces it.
3. **Context.** Build the model input from: fixed system prompt (design-proposal §B, Persian, CFO/IAS), report identity (company, family, scope, periods, unit, currency, provider), the report's compact fact table with evidence IDs, deterministic calculations (FR-5), the last up to 6 messages (bounded by tokens), and the current question. Report data is placed in a delimited data block and treated as data.
4. **Budget.** Input target 5,000 tokens (configurable 4,000–6,000), output cap 1,600 via `MaxOutputTokens` (was 650; raised by BUG-031 because Persian analysis answers were truncated), model timeout 30 seconds, recent messages capped separately. If the whole compact report does not fit, keep the most recent periods with all rows; if the requested data still cannot fit, return a zero-credit "please narrow the periods/items" answer. Never drop required facts silently and never modify the stored report.
5. **Calculations.** Computed in Application code from source facts before the call: gross/operating/net margin, QoQ and YoY growth per line, share of revenue (income statement); current ratio, debt ratio, equity ratio (balance sheet); Feature 161 `CashFlowMetricsCalculator` outputs (cash flow); MoM/YoY/vs-12-month/YTD already in the monthly payload. Each calculation carries formula and operand evidence IDs. Missing operands → calculation absent, not estimated.
6. **Model call.** One call, `AiWorkloadKind.ExplanationGeneration`, structured output `{ disposition: Answered|NeedsDifferentData|OutsideReport|MissingInReport, answer, usedEvidenceIds[] }`, no tools, explicit timeout, no retry.
7. **Grounding check.** Every number in `answer` (Persian, Arabic or Latin digits; separators; percentages) must match a selected fact, a calculation (display rounding tolerance), or a number in the period labels, company identity or user question. On failure: return a deterministic zero-credit message that a reliable answer could not be built from this report, and log the failure (no raw report text).
8. **Dispositions.**
   - `Answered` → answer returned through steps 4–7; charged per FR-9.
   - `MissingInReport` → deterministic Persian message naming what is missing and inviting a new report request; 0 credits; context stays active.
   - `NeedsDifferentData` → hand back: `ConversationDialogueGate` and the normal V2 route run exactly as today; a resulting supported report becomes the new active report.
   - `OutsideReport` → hand back as above; the persisted answer of that turn deactivates the context.
9. **Billing.** Hold raised to 2 credits before the model call when the active report is a cash-flow report; if the wallet cannot cover it, the existing insufficient-credit response is returned without a model call. Answered cash-flow turn = 2 credits; answered other families = 1 credit; `MissingInReport`, narrow-request, grounding failure, provider failure, timeout = 0; a hand-back turn is charged only by the normal route that answers it (the discussion call's cost is absorbed and logged).
10. **Missing source.** If the referenced payload cannot be loaded or deserialized, Report Discussion is not entered and the turn runs the normal route (no answer from memory).
11. **Persistence.** The answer is persisted by the existing `PersistExchangeAsync` with `TextAnswer` and a new optional `ReportDiscussion` payload field `{ SourceAssistantMessageId, ReportFamily, Disposition }`. No new table; old payloads deserialize unchanged.
12. **Observability.** Execution route `report_discussion`; tags: disposition, report family, input/output tokens, latency, grounding result, hand-back; usage facts via the existing accumulator. Unanswered-question reporting (155) sees `MissingInReport` as a no-data outcome with reason code `report_discussion_missing_in_report`.

## Data / Schema

No schema change and no migration. `AssistantMessagePayload` gains one optional, nullable record field serialized in `MessageRow.AssistantPayloadJson`. New bounded read on `IMessageRepository`: recent messages of one conversation scoped by tenant and actor.

## API (if any)

No new route. `POST /api/ai/v1/query` and the messages endpoint keep their contracts; the new payload field is additive and optional.

| Method | Route | Request | Response | Notes |
|---|---|---|---|---|
| POST | `/api/ai/v1/query` | unchanged | unchanged (`TextAnswer`) | Discussion turns answered here |

## AI Query Path (if any)

V2 only. Decision and model call in `PrepareStartMessageAsync` before `ConversationDialogueGate`; `Answered` result short-circuits step 3; hand-backs run the existing gate and route unchanged. No new capability in the semantic registry; no tool calling on the discussion turn.

## UI (if any)

None. The existing assistant text renderer shows the answer in RTL.

## Non-Functional Requirements

- Authorization: conversation, messages and payloads loaded only within the authenticated tenant/actor scope; foreign conversations yield the existing not-found behavior with no report content.
- Timeout: discussion model call `ReportDiscussion:ModelTimeoutSeconds` (default 20); no retry; failure → 0 credits, context unchanged.
- Bounded work: 20-message lookback, 6 recent messages, token budgets as FR-4.
- Idempotency: existing idempotency key and reservation replay; a retried request cannot charge twice or persist twice.
- Logs: never log report values, labels or answer text; log IDs, family, disposition, token counts.
- Prompt injection: fixed system prompt, delimited data block, no tools, structured output only.
- V1 and frozen shared routing rules unchanged; flag off = today's behavior byte-for-byte.

## Acceptance Criteria

- [ ] AC-1 Analytical: after a quarterly income statement, «چرا حاشیه سود ناخالص کم شد؟» is answered by Report Discussion without calling `ConversationDialogueGate` or the agent tool loop; margins come from deterministic calculations with period and unit.
- [ ] AC-2 Exact number: a question for net profit / financial expense of a shown quarter returns the stored value with period and unit; every number passes the grounding check.
- [ ] AC-3 Missing in report: a line item or period not in the report returns `MissingInReport`, 0 credits, no invented number, context still active.
- [ ] AC-4 Different data: «ترازنامه فولاد» or another symbol in the same conversation hands back; the normal route answers; no previous-company facts appear in that request; a returned supported report becomes active.
- [ ] AC-5 Outside report: an unrelated question hands back, the normal route answers, and the next message does not enter Report Discussion.
- [ ] AC-6 Pending clarification: when `ConversationTaskState` has a pending clarification, the message goes to the existing gate even if a report is active.
- [ ] AC-7 Pricing: answered cash-flow turn = 2 credits; answered income/balance/monthly turn = 1 credit; `MissingInReport`, narrow, grounding failure, provider failure, timeout = 0; hand-back turn charged only by the normal route; wallet with 1 credit on a cash-flow discussion gets the insufficient-credit response without a model call.
- [ ] AC-8 Restart: after API restart the active report is derived from PostgreSQL payloads and discussion works.
- [ ] AC-9 Budget: built input ≤ configured target and output cap passed; oversize slice yields the narrow message.
- [ ] AC-10 Isolation: another tenant/actor cannot reach the conversation or its report data.
- [ ] AC-11 Idempotency: a retried request returns the same answer with one charge and one persisted exchange.
- [ ] AC-12 Provider failure/timeout: 0 credits, context unchanged, failure observable, no report data in logs.
- [ ] AC-13 Prompt injection: instruction-like text in a report label cannot change role, call tools, or bypass grounding.
- [ ] AC-14 Flag off / V1: flag off reproduces current routing for all four report families; V1 path untouched (architecture test).
- [ ] AC-15 Monthly sales: after a company monthly sales report, a MoM/YoY question is answered from the payload's values.
- [ ] AC-16 Cash flow: after a Feature 161 report, «چرا جریان نقد عملیاتی منفی شد؟» is answered from 161 facts and metrics at 2 credits.

## Assumptions

- «گزارش فروش ماهانه» = `MonthlyActivityTrendResponse` (single-company monthly sales). Product-level monthly payloads are out of scope until OQ-088 is answered.
- A 20-message lookback bounds the active report; a report older than that is inactive (no idle timeout per D4).
- The discussion call's cost on a hand-back turn is absorbed (D2: the user pays only the normal route).
- The cash-flow family is implemented only after Feature 161 meets its definition of done, unless the owner grants an exception at Gate B.
