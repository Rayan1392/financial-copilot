# User Story: Local company data for every grounded analysis, and no "too large" refusal

## Feature Folder

`162-context-aware-financial-report-follow-up/changes/local-company-data-context`

## Layers / Area

Layers: backend
Area: AI, FinancialData

## Priority

To be set by the owner

## Complexity

L

## Depends On

162 change `sol-deep-analysis-web-search` (released v1.19.0, live 2026-11-22), 163 (released), 154 (`[~]`), 161 (`[~]`), 120 (monthly activity trend). 154 and 161 are used only through their implemented query use cases; see `tasks.md` "Before Starting".

## References

- `specs/162-context-aware-financial-report-follow-up/changes/local-company-data-context/design-proposal.md` (approved at Gate A on 2026-11-22, amendments G1–G3)
- `specs/162-context-aware-financial-report-follow-up/changes/sol-deep-analysis-web-search/design-proposal.md` (PO-1…PO-16 stay binding)
- `specs/bugs.md` BUG-042
- `docs/grounded-analysis-operations.md`, `docs/domain-glossary.md` (صورت سود و زیان، صورت وضعیت مالی، صورت جریان وجوه نقد، گزارش فعالیت ماهانه)
- Code: `src/backend/FinancialCopilot.Application/AI/ReportDiscussion/`, `src/backend/FinancialCopilot.Application/AI/SymbolGroundedFallback/`, `src/backend/FinancialCopilot.Application/AI/GroundedAnalysis/`, `IFinancialStatementTableQueryUseCase`, `IBalanceSheetReportQueryUseCase`, `ICashFlowReportQueryUseCase`, `IMonthlyActivityTrendQueryUseCase`
- Data model: none touched (read-only use of existing query use cases; no migration)

## Standards Source Of Truth

Obey `.claude/rules/*`. Do not copy rule text into this spec.

## User Story

As a Sapio user asking for an interpretation of a company's income statement (or any fundamental analysis of one symbol),
I want the analysis to use Sapio's own copy of the company's three financial statements and monthly sales trend,
so that the answer is grounded on Sapio data instead of figures searched on the web, and a long statement is never refused as "too large".

## Business Context

A 25-credit Deep + web analysis of اخشان cited «گزارش فعالیت ماهانه اخشان» from the web although Sapio holds that data;
an analysis of فاراک was refused with «… بیش از حد بزرگ است» (BUG-042). Both damage trust in the paid analysis on web,
PWA, Telegram and API.

## Scope

- A company data pack for the grounded analysis paths: quarterly income statement, balance sheet, cash flow statement
  (when Feature 161 is enabled) and the monthly activity trend totals (when the company has monthly data), each the
  default result of the existing chat answer.
- Feature 162 analysis turns (any active report family, after the routing step answers `Answered`) and Feature 163
  confirmed runs use the pack.
- Prompt: `<company_data>` sections and a rule not to search for figures the pack holds; every current web search target
  stays (G2).
- Budget: degradation order instead of the `NarrowRequired` / `over_input_budget` refusal; compact rendering; larger
  technical input budgets (OQ-123).
- Telemetry: sections used and omitted, read latency, codal.ir citation count (G3).

## Out Of Scope

- Local product-level rows (G1; backlog).
- A server guard on web citations (G3).
- Changes to the 162 routing step, to a cash-flow `ReportFamily` (OQ-110), to prices, tiers, UI, Telegram rendering, and
  to the legacy path (`GroundedAnalysis:Enabled=false`).

## Functional Requirements

1. FR-1 Pack contents: for the analysed company the pack holds, in this order, the shown section (162 active report, or
   the 163 income statement) and then the other available sections among: quarterly income statement
   (`BuildV2QuarterlyQuery` default), balance sheet (Feature 154 default view), cash flow statement (Feature 161 default,
   only when `CashFlowReport:Enabled`), monthly activity trend (Feature 120 default). A section of another company
   (symbol mismatch after canonicalisation) is never used.
2. FR-2 Reads run sequentially on the request scope (BUG-035), each with its own timeout (default 3 s) inside an
   overall budget (default 8 s). A failed, timed-out, disabled or empty section is omitted and listed as
   `not_available` with a reason code (`disabled`, `no_data`, `timeout`, `error`, `other_company`); the turn continues.
   Cancellation of the request still cancels the turn.
3. FR-3 The pack is loaded only after the 162 routing step decided `Answered` (a hand-back loads nothing) and only after
   the 163 offer was confirmed and found current (the offer step is unchanged).
4. FR-4 The shown section is the one the user saw (162: from the stored payload; 163: the statement just read); it is not
   read twice.
5. FR-5 Prompt: the user content has one `<company_data>` block with one `<section>` per pack entry (title, scope,
   period keys, facts, calculations, `shown` flag for the shown section) and the `not_available` list. Evidence ids are
   unique across sections (one F and one C counter).
6. FR-6 The analysis prompts (162 and 163, all four tiers) state that `<company_data>` is Sapio's own copy of the
   company's financial statements and monthly activity totals, that the model must not search the web for those
   figures or for a section listed as not available, and that the web is for outside context. All current search
   targets and web rules stay (G2). `MissingInReport` / `NotAnswerable` refer to `<company_data>`.
7. FR-7 Numeric grounding checks the answer against the union of all sections sent, with the existing tolerance and
   rules; external `[Wn]` items are validated as today.
8. FR-8 Budget: the analysis paths never return `NarrowRequired` (162) or `over_input_budget` (163) because of size
   while the system prompt plus the newest period of the shown section fits the hard ceiling. Degradation order: (1) full
   pack; (2) supporting sections trimmed to their newest 2 periods (monthly: 12 months); (3) history cut to the last two
   messages; (4) supporting sections dropped whole: cash flow, then balance sheet, then monthly, then (163 and
   non-income-statement 162) income statement; (5) shown section trimmed oldest first. The first state that fits
   `InputTokenTarget` is sent.
9. FR-9 Hard ceiling: a new technical setting `GroundedAnalysis:MaxInputTokens` (default 120,000). Only above it is the
   existing zero-credit reply kept, logged at error level.
10. FR-10 Compact rendering: each section lists its periods once with short keys (`P0`, `P1`, …); fact and calculation
    lines carry the key instead of the full period label. Values, units and labels are unchanged.
11. FR-11 Technical budgets (code defaults, `appsettings.json` and production config agree): Deep `InputTokenTarget`
    40,000, Simple 24,000; validation 4,000–120,000. Credits are unchanged (PO-6).
12. FR-12 Telemetry (no values, no answer text): sections sent, sections omitted with reason, per-section read latency,
    degradation step reached, estimated input tokens, codal.ir citation count.
13. FR-13 Rollback: `GroundedAnalysis:DataPack:Enabled` (default true). False → the analysis paths send only the shown
    report exactly as before this change (budget fix FR-8–FR-11 still applies).

## Data / Schema

None. No table, column, index or migration. Stored payloads are unchanged except an optional `SectionsUsed` string list
on the stored `GroundedAnalysisRun` (existing JSON payload; old rows read as null).

## API (if any)

No contract change.

## AI Query Path (if any)

V2 only: `FinancialCopilotWorkflowDefinition` → `ReportDiscussionHandler.AnalyzeAsync` (162) and
`SymbolGroundedFallbackHandler.RunConfirmedAsync` (163). Missing data: the section is omitted and named in
`not_available`; the answer is still produced from the rest.

## UI (if any)

None. Telegram and web render the answer as today.

## Non-Functional Requirements

- Latency: at most 8 s added by pack reads (overall budget); typical expected well below 1 s (indexed local reads).
- Cost: larger inputs are monitored by existing cost telemetry (`CostAlertUsd`); credits are fixed.
- Configuration: every new setting has a safe code default because the production image has no `appsettings.json`
  (BUG-041); `docker/appsettings.Production.json` / `.env.example` updated where a non-default value is needed.
- No live OpenAI calls in tests (deterministic fakes).

## Acceptance Criteria

- [x] AC-1 A 162 analysis of a shown quarterly income statement for a company with monthly data sends sections: income
      statement (shown), balance sheet, cash flow (flag on), monthly activity; with the cash-flow flag off the cash-flow
      section is listed `not_available: disabled`.
- [x] AC-2 A company without monthly activity data gets no monthly section and `not_available: no_data`.
- [x] AC-3 A section whose read throws or exceeds its timeout is omitted with its reason; the answer is still produced
      and charged at the fixed tier.
- [x] AC-4 A 162 routing hand-back (`NeedsDifferentData` / `OutsideReport`) performs no pack read.
- [x] AC-5 A 163 confirmed run sends the pack; the offer step reads nothing new and makes no model call.
- [x] AC-6 All four analysis system prompts contain the no-search rule for `<company_data>` figures and still contain
      every current web search target (raw-material and product prices, the company's own disclosures).
- [x] AC-7 An answer quoting a balance-sheet or monthly figure from the pack passes numeric grounding; an invented figure
      still fails.
- [x] AC-8 A synthetic statement larger than today's Simple and Deep budgets (فاراک-sized) is answered in both 162 and
      163 at Simple and Deep; no `NarrowRequired` / `over_input_budget` reply below `MaxInputTokens`.
- [x] AC-9 Code defaults, `appsettings.json` and the production configuration test agree on Deep 40,000 / Simple 24,000 /
      `MaxInputTokens` 120,000 / `DataPack:Enabled` true.
- [x] AC-10 With `DataPack:Enabled=false` the prompt contains only the shown report.
- [x] AC-11 Telemetry logs sections sent/omitted, degradation step and codal.ir citation count, without values.
- [ ] AC-12 Owner-run live check: «صورت سود و زیان اخشان را تفسیر کن» (Deep + web) cites no web source for statement
      or monthly-total figures; «صورت سود و زیان فاراک را تفسیر کن» is answered.

## Assumptions

- A1 The production server `.env` enables `CashFlowReport`: **confirmed by the owner on 2026-11-22** («CashFlowReport__Enabled روی پروداکشن فعال است»).
- A2 The BUG-042 cause is the input budget (F6). Task T01 confirms it from the production log or a Development run
  before the fix; if the cause differs, work on FR-8–FR-11 pauses and the owner is asked.
- A3 `gpt-6.2-sol`'s context window is far above 120,000 tokens; `MaxInputTokens` is a safety ceiling, not a model limit.
