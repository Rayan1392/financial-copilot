# Feature 156 — Symbol Snapshot Default Card

Status: Gate A: Approved by the owner on 2026-11-09

| Field | Value |
|---|---|
| Layers / Area | backend, frontend · AI, Conversations, FinancialData |
| Priority (proposed) | P1 |
| Complexity (proposed) | M |
| Depends On | None (reuses 141, 154 and the existing monthly-trend and quarterly-report capabilities) |
| Evidence | Feature 155 demand theme **T06 — نماد تنها بدون درخواست مشخص** (UX: default symbol snapshot instead of a refusal), `specs/155-admin-unanswered-question-demand-report/design.md` §5 |
| References | `specs/POLICY-V1-FREEZE.md`, `docs/domain-glossary.md`, `docs/architecture.md`, `src/backend/FinancialCopilot.Application/AI/Orchestration/ChatCapabilityAvailability.cs`, `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs`, `src/frontend/src/components/app/message-list.tsx` |

## 1. Problem

When a user sends only a ticker (for example `اخابر`, `کگهر`, `کگل`), the V2 routing finds a company but no capability or metric. `ConversationTaskStateService` sets `SemanticRoutingDisposition` to `Clarification` (missing slot) or `Unsupported` (no frame), and step 3 of the workflow (`FinancialCopilotWorkflowDefinition.ExecuteAgentStepAsync`, line ~563) returns a clarification or refusal. In production, the Feature 141 fallback is off, so the user gets a dead end on one of the most natural first messages.

## 2. Goal and scope

**In scope**

- A message that is only one ticker of a company in the `NoavaranEligibleCompanies` view gets a **Symbol Snapshot**: one assistant message with three sections and next-step buttons.
  1. **Latest price** — price, daily change %, trading date (from `IMarketQuoteResolver`).
  2. **Latest monthly sales** — latest reported month, sales amount, change vs. previous month and vs. same month last year (from `IMonthlyActivityTrendQueryUseCase`, `IncludeChartPayload = false`).
  3. **Latest quarterly report** — latest quarter's revenue, gross profit, net profit and EPS, with the period and audit status (from `IQuarterlyFinancialReportsQueryUseCase`, latest period plus the same quarter a year earlier for growth).
- Up to four **suggested actions** (existing `SuggestedAction` contract) that run existing capabilities, for example:
  - `روند فروش ماهانه {symbol} را نشان بده` → `monthly_activity_trend`
  - `گزارش فصلی {symbol}` → `financial_statement_table` (quarterly)
  - `ترازنامه {symbol}` → `financial_statement_table` (Feature 154)
  - `ترکیب فروش محصولات {symbol}` → `product_revenue_mix`
  - `اطلاعیه‌های کدال {symbol}` → `disclosure_listing` (if room)
- Web chat (desktop and mobile PWA) and Telegram (text rendering + the existing suggested-action list).

**Out of scope (v1)**

- Company names (`ایران خودرو`), multiple tickers, or a ticker with any other words except the allowed fillers in §4.
- Price charts, technical analysis, valuation, buy/sell advice (themes T02–T05).
- New data ingestion or new persistence.
- Companies outside `NoavaranEligibleCompanies` (they keep today's behaviour).

## 3. UX design

### 3.1 Card layout (web, RTL)

```text
┌────────────────────────────────────────────┐
│ اخابر · مخابرات ایران          [دنبال کردن] │
│ بورس · گروه مخابرات                         │
├────────────────────────────────────────────┤
│ آخرین قیمت     ۳٬۴۵۰ ریال    ▲ ۲٫۱٪         │
│                تاریخ معامله: ۱۴۰۵/۰۸/۱۸     │
├────────────────────────────────────────────┤
│ فروش ماهانه — مهر ۱۴۰۵                       │
│ ۱۲٬۳۴۰ میلیارد ریال                          │
│ نسبت به ماه قبل ▲ ۴٪ · نسبت به سال قبل ▲ ۳۱٪ │
├────────────────────────────────────────────┤
│ گزارش فصلی — ۶ ماهه ۱۴۰۵ (حسابرسی‌نشده)      │
│ درآمد  ·  سود ناخالص  ·  سود خالص  ·  EPS     │
│ رشد نسبت به دوره مشابه سال قبل               │
├────────────────────────────────────────────┤
│ (روند فروش ماهانه) (گزارش فصلی کامل)         │
│ (ترازنامه) (ترکیب فروش محصولات)              │
└────────────────────────────────────────────┘
```

(Numbers are illustrative only.)

Design rules:

- **One card, three rows**, scannable in under five seconds. Numbers use the existing Persian formatters (`toPersianNumerals`, `formatNumber`, `FinancialNumberFormatter`) and the existing LTR isolation for signed percents.
- **Every row shows its period/date.** A user must never mistake an old month or quarter for current data. A stale price (market closed or no trade today) shows the trading date in muted text.
- **Partial data is normal, not an error.** A missing section shows one muted line (for example `گزارش ماهانه‌ای برای این نماد ثبت نشده است`) and the other sections still render. The suggestion for a missing section is dropped.
- **Buttons are the next step**, not a menu of everything: at most four, ordered by what the card just showed (monthly first, then quarterly, balance sheet, product mix).
- **Mobile PWA:** rows stack; the three numbers stay one per line; buttons wrap; no horizontal scroll. Compact density (`useAnswerDensity`) is respected.
- **Accessibility:** card is a `section` with an `aria-label` (`خلاصهٔ نماد اخابر`); each row has a heading; up/down changes have text, not only colour or arrows; buttons keep the existing `role="group"` label.
- A short text line above the card explains what happened, so the turn does not feel like a misunderstanding: `خلاصهٔ آخرین داده‌های «اخابر». برای جزئیات یکی از گزینه‌های زیر را انتخاب کنید.`

### 3.2 Telegram

The same three sections as plain text (one block per section, period on each line), followed by the existing `AppendSuggestedActions` rendering (`TelegramAssistantResponseRenderer`, max 4).

## 4. Detection rule (when a message is a "bare ticker")

A deterministic rule in the Application layer, `SymbolSnapshotIntentRules`, evaluated **before** the LLM-assisted interpretation so the turn needs no model call:

1. Normalize with `QueryNormalization.Normalize` (Arabic ي/ك → ی/ک, digits, ZWNJ, whitespace).
2. Strip trailing punctuation (`؟ ? . ! ،`) and an optional leading filler: `نماد`, `سهم`, `سهام`.
3. The remainder must be **one token** (tickers like `وبملت`, `کگل`; multi-word tickers, if any exist in the view, are matched against the full remainder).
4. The token must resolve to **exactly one** row of `NoavaranEligibleCompanies` by `Symbol` (exact match after the same normalization). No fuzzy matching, no company-name matching.

The turn becomes a Symbol Snapshot only when all of these also hold:

- **No pending clarification in the conversation.** If the previous assistant turn asked "which symbol?" (`ConversationTaskState.PendingAction` is set), the ticker answers that question and the existing follow-up flow wins. This is the most important guard.
- The feature flag `SymbolSnapshot:Enabled` is on.
- The provider mode allows it (see OQ-008).

## 5. Architecture (Clean Architecture / DDD)

Bounded context: **AI Assistant (Conversations)**, reading **FinancialData** through existing Application ports. No new domain model; this is an application-level composition (read model), so no aggregate.

```text
Telegram / Web ──► AiFacadeController ──► V2 workflow (Infrastructure)
                                            │ step 3: semantic frame "symbol_snapshot"
                                            ▼
                     SemanticCapabilityExecutors (Application)
                                            ▼
                     SymbolSnapshotUseCase (Application, new)
                      ├─ IEligibleSymbolResolver (new port; Infra reads NoavaranEligibleCompanies)
                      ├─ IMarketQuoteResolver                (existing)
                      ├─ IMonthlyActivityTrendQueryUseCase   (existing, IncludeChartPayload=false)
                      ├─ IQuarterlyFinancialReportsQueryUseCase (existing)
                      └─ SymbolSnapshotFollowUpSuggestionService (new, same pattern as
                         QuarterlyFinancialReportFollowUpSuggestionService)
                                            ▼
                     SymbolSnapshotResult (plain record) ──► AiQueryResponse.SymbolSnapshotResult
                                            ▼
                     Frontend SymbolSnapshotCard (new; reuses formatters, pills, buttons)
```

Decisions:

- **New capability on the V2 path only** (`symbol_snapshot` in the conversational capability registry, route `CapabilityExecutionRoutes.SymbolSnapshot`), per `specs/POLICY-V1-FREEZE.md`. The V1 orchestrator is not touched.
- **The use case composes; it does not recompute.** Each section uses the same use case as the full capability, so the card's numbers equal what the user sees after pressing the button (one owner per number).
- **One typed payload**, `SymbolSnapshotResult { Company, Price?, MonthlySales?, QuarterlyReport?, SectionStatuses, SuggestedActions }`. Each section is a small summary record mapped from the existing result; the large chart and table payloads are not sent.
- **Adapters stay humble.** The controller, Telegram renderer and React card only format; "is this a bare ticker", "which sections are present" and "which buttons" are decided in Application.
- **Eligibility port:** `IEligibleSymbolResolver.ResolveExactAsync(normalizedSymbol)` returns the company identity (`CompanyId`, `ExternalCompanyId`, `Symbol`, `Name`, market) or nothing. Infrastructure implements it on `NoavaranEligibleCompanies` with a short in-memory cache (the view changes rarely), instead of the existing `CanonicalQueryEntityResolver` path that loads the whole `Companies` table per call.

## 6. Reliability and failure behaviour (Release It!)

- **Sections are independent bulkheads.** Each section runs with its own timeout (proposed 2 s; total budget 4 s) and its own try/catch. A failure or timeout marks the section `Unavailable`, is logged with the correlation id, and does not fail the turn.
- **Sequential reads in v1.** `DbContext` is not thread-safe; v1 runs the three reads one after another. Measured latency decides whether parallel scopes are worth it later.
- **All three sections empty** → the turn is `NoData` with a clear message and the suggestions that still make sense; it is not a refusal.
- **No retries** inside the turn (read-only; the user can resend; avoids duplicated load).
- **Bounded payload:** summaries only; at most 4 suggestions.
- **Observability:** `workflow.intent = SymbolSnapshot`, per-section status and latency tags, and a structured log line; Feature 155 can then classify these turns as answered, and T06 should drop.
- **Feature flag** `SymbolSnapshot:Enabled` (default off). Turn on in Development first, then production after the owner's sign-off.

## 7. Billing

The snapshot needs no LLM call, so usage-based pricing would charge the minimum. Proposal in OQ-006: charge the minimum credit when at least one section has data; free when all sections are empty (same spirit as Feature 141 non-answers being free). One reservation, execution route `symbol_snapshot`.

## 8. Testing and verification

- **Unit (Application):** detection rule table — bare ticker, `نماد اخابر`, `اخابر؟`, Arabic ک/ی variants, ZWNJ; negative cases: `سلام`, `اخابر سود`, two tickers, non-eligible ticker, ticker when a pending clarification exists.
- **Unit (use case):** all sections present; each section missing; each section throwing/timing out; all empty → `NoData`; suggestion list drops missing sections and round-trips through `ICapabilityInterpreter` like `QuarterlyFinancialReportFollowUpSuggestionService`.
- **Routing tests:** bare ticker reaches `symbol_snapshot` and never the Feature 141 fallback or a refusal; V1 freeze test stays green.
- **Architecture tests:** Application does not reference Infrastructure; new port implemented only in Infrastructure.
- **Frontend (vitest):** card renders three rows, partial rows, RTL numbers, accessible labels, buttons call `onSuggested`; mobile density.
- **Live Development check** on the disposable PostgreSQL with `اخابر`, `کگهر`, `کگل` (and one bank, because bank quarterly reports use `BankingIncomeStatement`).
- Definition of done per `specs/test-baseline.md` (no new failures, no new type errors).

## 9. Alternatives considered

| Option | Pros | Cons | Verdict |
|---|---|---|---|
| **A. New typed `SymbolSnapshotResult` + compact card** | Fast to read; one card; mobile-friendly; numbers come from the same use cases | One new payload type and one React component | **Recommended** |
| B. Fill the existing response fields (lookup table + monthly chart + full quarterly table) in one message | Almost no frontend work | Very long message (chart + 5-quarter table), poor on mobile; message-list rules were not designed for three big results at once | Rejected for UX |
| C. Reply with a clarification plus buttons only ("what do you want about اخابر?") | Smallest change | Still feels like a refusal; user needs one more round-trip before seeing any data | Fallback if data latency is too high |
| D. Let the LLM decide what to show | Flexible | Costs tokens, non-deterministic, harder to test | Rejected |

## 10. Risks

- **Tickers that are common words** (for example a ticker equal to a month name or a common noun). Mitigated by the exact-match + whole-message rule; the list of eligible tickers that collide with common words is checked during planning.
- **Stealing a clarification reply** — mitigated by the pending-action guard (§4), covered by tests.
- **Provider mode:** in `CyclicalWaves` mode, monthly activity is unavailable (`ChatCapabilityAvailability.CyclicalWavesSupportedRoutes`). See OQ-008.
- **Latency** of three reads in one turn; mitigated by per-section timeouts and summaries.
- **Stale data looking current** — mitigated by a period/date on every row.

## 11. Open questions (also in `specs/open-questions.md`)

- **OQ-006** Billing of a snapshot turn.
- **OQ-007** Which follow-up buttons, in which order, and how many.
- **OQ-008** Behaviour in `CyclicalWaves` provider mode, and which mode production runs today.
- **OQ-009** Matching scope for v1 (ticker only vs. also company name; allowed filler words).
- **OQ-010** Telegram in v1 or web only.

## 12. Next step

Gate A is recorded above. The implementation plan is `tasks.md` and needs separate **Gate B — Implementation Plan Approval**.

## 13. Planning clarifications (after Gate A, presented for Gate B)

Code reading during planning refined these points. None changes the approved scope.

| Topic | Design said | Code shows / plan does |
|---|---|---|
| Quarter shown | Sketch showed a cumulative `۶ ماهه` label | The quarterly use case returns standalone fiscal quarters. The row shows, for example, `فصل دوم ۱۴۰۵`. |
| Bank rows | Revenue, gross profit, net profit, EPS | Bank statements have no sales or gross-profit row. Banks show net interest income (306), net fee income (307), net profit (143) and EPS (160), with the labels of the banking template. |
| Year-on-year growth | "Same quarter a year earlier" | `Comparisons` holds only quarter-on-quarter pairs. The plan requests 5 quarters, finds the period by fiscal year and quarter number, and reuses the normalizer's comparison rule (sign changes and non-positive bases show no percent), so that rule keeps one owner. |
| Symbol columns | `Symbol` | The view has `CompanySymbol` and `TseSymbol`; both are matched. Duplicate keys across companies are not matched. |
| Detection | One token | Lookup of a compact key (spaces and ZWNJ removed, message at most 40 characters) in the eligible-symbol set, so `وب ملت` and `وبملت` behave the same and `اخابر سود` cannot match. |
| Where detection runs | Before LLM interpretation | Inside `ConversationDialogueGate.PrepareAsync`: a matched ticker replaces the hybrid interpreter result with a `symbol_snapshot` interpretation, and the rest of the gate runs unchanged (entity resolution, slots, conversation state). |
| Suggested actions | Inside `SymbolSnapshotResult` | Use the top-level `SuggestedActions`, which the existing selection check and persistence already handle. |
| Telegram | New text rendering | `TextAnswer` carries the three sections (the Feature 154 pattern), so `TelegramAssistantResponseRenderer` needs no new branch. |
| Conversation history | Not stated | The typed result is persisted, so a reopened conversation shows the card. |
| Common-word tickers | Checked during planning | Not done: this session has no read-only database connection. It is task 1.1 of the plan. |
