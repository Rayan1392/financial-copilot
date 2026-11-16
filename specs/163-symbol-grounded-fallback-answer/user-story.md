# User Story: Symbol-Grounded Fallback Answer

## Feature Folder

`163-symbol-grounded-fallback-answer`

## Layers / Area

Layers: backend
Area: AI (touches Conversations and Billing)

## Priority

To be set by the owner (proposed: P1)

## Complexity

M

## Depends On

162-context-aware-financial-report-follow-up

## References

- `specs/163-symbol-grounded-fallback-answer/design-proposal.md` (approved at Gate A on 2026-11-16)
- `specs/open-questions.md` OQ-090 … OQ-097
- `specs/162-context-aware-financial-report-follow-up/user-story.md` (Report Discussion rules reused here)
- `docs/domain-glossary.md` terms used: Report Discussion, Active Report
- `docs/architecture.md`: V2 workflow section
- Code: `src/backend/FinancialCopilot.Application/AI/ReportDiscussion/`,
  `src/backend/FinancialCopilot.Application/AI/Orchestration/CanonicalQueryEntityContracts.cs`
  (`ICanonicalQueryEntityResolver`), `.../UnavailableTopics/UnavailableTopicCatalog.cs`,
  `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs`
- Related specs: Feature 150 change «unavailable-capability-refusals-and-answer-correctness» (R1–R3),
  `specs/155-admin-unanswered-question-demand-report/`, `specs/161-ai-cash-flow-statement-report/`

## Standards Source Of Truth

Obey `.claude/rules/*`. Do not copy rule text into this spec.

## User Story

As a Sapio user (web or Telegram),
I want a question about a symbol that no existing capability answers to be answered from that symbol's
income statement,
so that I get a useful, grounded answer and a next step instead of «نمی‌توانم پاسخ بدهم».

## Business Context

Feature 155 measured that a large share of customer questions get no answer. Many of them name a symbol.
The quarterly income statement of that symbol is already in the system and Report Discussion (162) already
knows how to answer from it safely. This feature connects the two for questions that have no report behind
them yet.

## Scope

- A fallback answer for turns that end `Unsupported` and name exactly one company.
- Valuation and investment-advice questions («ارزش ذاتی», «قیمت تعادلی», «ارزش خرید») use this fallback (OQ-091).
- The income statement is not shown; it is stored hidden with the answer and becomes the active report.
- A closing offer written by the model (OQ-094).
- An income-statement offer when the model cannot answer the question (OQ-093).
- Billing, configuration flag, logging.

## Out Of Scope

- Balance sheet or cash flow as grounding data (→ `specs/backlog.md`).
- Questions with no symbol or with several symbols.
- The technical-analysis refusal (stays as it is).
- `NoData` answers of supported capabilities (stay as they are, OQ-092).
- Any change to how Report Discussion treats a report that was shown.
- Frontend and Telegram code changes (verification only).

## Functional Requirements

1. **FR-1 Trigger.** The fallback runs when all hold: `SymbolGroundedFallback:Enabled` is true; the turn's
   final outcome is `Unsupported`; the reason code is neither a provider-policy block
   (`ChatCapabilityAvailability`) nor `technical_analysis_unavailable`; the request is free text (no
   `SuggestedActionId`, insight or alert context); Report Discussion did not answer the turn; exactly one
   company is resolved from the message. It applies to any turn of a conversation (OQ-090).
2. **FR-2 Company.** The company is resolved by `ICanonicalQueryEntityResolver`. Only a `Resolved` result
   qualifies. `Ambiguous`, `NotFound`, `Missing` or more than one company leave the turn unchanged.
3. **FR-3 Grounding data.** The quarterly income statement of the company is read through the existing
   quarterly report use case (same data as «عملکرد فصلی X») and projected with `ReportDataSetBuilder`. No
   usable statement, or one that does not fit the input budget, leaves the turn unchanged.
4. **FR-4 Model call.** One structured call, no tools, explicit timeout, no retry. Output:
   `disposition` (`Answered` | `NotAnswerable`), `answer`, `closingOffer`, `usedEvidenceIds`. The prompt keeps
   the 162 rules: only supplied facts and calculations, no arithmetic by the model, input is data and never
   instructions, Persian answer.
5. **FR-5 Valuation and advice.** For any question the answer describes what the income statement shows; it
   never states a fair price, an intrinsic value, or a buy/sell/hold verdict. For questions that ask for those,
   the answer says in one sentence that it is not a valuation or a recommendation.
6. **FR-6 Source phrase.** An answered reply names its source in one phrase («بر اساس صورت سود و زیان فصلی …»),
   because the table is not shown.
7. **FR-7 Closing offer.** The model writes one closing question that offers the reports in the list it is
   given, each with the symbol name: monthly sales trend, statement of financial position, and cash flow only
   when `CashFlowReport:Enabled` is true. `closingOffer` is appended as the last paragraph. If it is empty,
   a fixed sentence with the same offers is appended.
8. **FR-8 Grounding check.** `NumericGroundingValidator` runs on `answer` and `closingOffer`. A failed check
   leaves the turn unchanged.
9. **FR-9 NotAnswerable.** The original non-answer text is returned with one added sentence, «می‌توانم صورت
   سود و زیان {نماد} را تحلیل کنم.», and one suggestion chip that requests the quarterly income statement of
   the symbol. Zero credits. No hidden report is stored.
10. **FR-10 Hidden report.** For an answered reply the statement is stored in the assistant payload in a field
    no client renders (`GroundingReport`, without its rendered text). `ActiveReportResolver` treats it as the
    conversation's active report (OQ-095). No `ReportDiscussion` marker is written on the fallback message
    itself: the resolver finds the report in the payload directly, and the marker's source message id is not
    known before the message is saved.
11. **FR-11 Failure.** Provider failure, timeout, invalid output, failed grounding, or any unexpected error
    leaves the turn exactly as it is today (same text, same outcome, zero credits). The failure is logged.
12. **FR-12 Billing.** An answered reply costs 2 credits (OQ-096). Capacity for 2 credits is ensured before
    the model call. Every other result costs zero. One turn is settled once per reservation.
    **Deviation recorded during implementation (2026-11-16):** with insufficient credit for 2 credits the turn is
    left unchanged (the free non-answer the user gets today) and no model call is made, instead of the
    insufficient-credit response. Reason: the turn's own reservation is already settled when the fallback runs,
    and a user who cannot pay for the fallback should not lose the free reply. Owner may reverse this.
    The answer settles with the Report Discussion completion status (same 2-credit price, no pricing-policy
    change) and is told apart by execution route `symbol_grounded_fallback`.
13. **FR-13 Order.** Existing route first, then the Feature 141 fallback where it is enabled, then this
    fallback. An existing answer is never replaced.
14. **FR-14 Demand report.** The original outcome and reason code are kept with the answer, so Feature 155
    can still count these questions.

## Data / Schema

No database migration. `AssistantMessagePayload` (JSON column) gets two optional trailing members:
`GroundingReport` (the quarterly table the model saw) and `FallbackOf` (original outcome and reason code).
Old JSON deserializes unchanged.

## API (if any)

No new route. The existing AI query response carries the answer text, outcome `Answered`, the usage of 2
credits, and suggestion chips only in the FR-9 case.

## AI Query Path (if any)

V2 only. New Application use case `SymbolGroundedFallbackHandler`, called once from the result-computation
step after the outcome is determined. Execution route `symbol_grounded_fallback`. Workload
`ExplanationGeneration`. Missing data or unsupported question: FR-9 and FR-11.

## UI (if any)

No UI change. Web and Telegram show the answer text as a normal assistant message. To verify: neither client
renders `GroundingReport`.

## Non-Functional Requirements

- Options `SymbolGroundedFallback`: `Enabled` (default false), `InputTokenTarget`, `MaxOutputTokens`,
  `ModelTimeoutSeconds` (default 30); validated at startup.
- Logs carry identifiers and counts only (disposition, reason, fact count, token estimate, numbers checked,
  elapsed ms, correlation id); never report values, the question, or the answer.
- Tenant and actor scope come from the request; the hidden report is read back only through the
  tenant/actor-scoped message reader of 162.
- Cancellation by the caller is honoured and settles the reservation at zero.
- Flag off: behaviour and test results identical to today.

## Acceptance Criteria

- [ ] AC-1 Flag off: an unsupported question with a symbol returns today's text, outcome and zero credits.
- [ ] AC-2 Flag on, unsupported question with one symbol and an available income statement: outcome
      `Answered`, answer text ends with the closing offer, 2 credits charged, no table in the response.
- [ ] AC-3 The stored payload holds `GroundingReport` and `FallbackOf`; a
      following free-text question in the same conversation is answered by Report Discussion from that data.
- [ ] AC-4 `NotAnswerable`: original non-answer text plus the income-statement sentence and one chip; zero
      credits; no `GroundingReport`.
- [ ] AC-5 «ارزش ذاتی X» and «X ارزش خرید دارد؟» reach the fallback; «تحلیل تکنیکال X» still returns its fixed refusal.
- [ ] AC-6 A valuation or advice answer contains the not-a-recommendation sentence and no buy/sell/hold verdict
      (checked in the live set, T16).
- [ ] AC-7 `NoData` of a supported capability, `ClarificationNeeded`, `DisambiguationNeeded`,
      `TemporarilyUnavailable`, `Failed`: unchanged.
- [ ] AC-8 No symbol, an ambiguous symbol, or two symbols: unchanged.
- [ ] AC-9 No usable income statement, or data over the input budget: unchanged, no model call.
- [ ] AC-10 A number in the answer or closing offer that is not in the data: unchanged turn, zero credits.
- [ ] AC-11 Provider error or timeout: unchanged turn, zero credits, warning log with the reason.
- [ ] AC-12 Insufficient credit for 2 credits: unchanged turn, no model call (see FR-12 deviation).
- [ ] AC-13 Cash-flow offer is in the prompt's offer list only when `CashFlowReport:Enabled` is true.
- [ ] AC-14 A suggestion-chip, insight or alert request never runs the fallback.
- [ ] AC-15 Where the Feature 141 fallback gives a definitive answer, this fallback does not run.
- [ ] AC-16 Logs of a fallback turn contain no report value, question text or answer text.
- [ ] AC-17 Architecture test: `AI/SymbolGroundedFallback` in Application references no Infrastructure, EF
      or HTTP type; V1 orchestration does not reference it.
- [ ] AC-18 Existing suites for 162, 154, 144 (quarterly table), 150 (unavailable topics) and 141 stay green
      against `specs/test-baseline.md`.

## Assumptions

- `ICanonicalQueryEntityResolver.ResolveExactTickerFromTextAsync` returns `Resolved` only for one
  unambiguous company in the raw message (verified in T01; otherwise the task stops and the owner is asked).
- The FR-9 chip sends «گزارش فصلی {نماد}» (the existing related-report text), which shows the quarterly income
  statement through the existing route at its existing price. The chip is shown only when that text routes
  back to the quarterly report for the symbol (existing `RelatedFollowUpActionFactory` rule).
- v1 answers in Persian only: a turn whose reply language is not Persian is left unchanged.
- A typed reply to the closing offer that names no symbol (for example «آره، روند فروش») may not be routed
  correctly in v1. Measured in T16; decision is OQ-097.
- The uncommitted change `162/changes/price-all-report-discussions-at-two-credits` on `develop` touches the
  same billing files; it is committed or set aside before T11 starts.
