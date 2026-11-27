# User Story: Sapio forward P/E and P/S estimate on the valuation gauge cards

## Feature Folder

`159-capability-source-map-comprehensive-analysis/changes/valuation-gauge-forward-ratio`

## Layers / Area

Layers: backend | frontend | telegram-gateway (text only, through the shared composer)
Area: AI | FinancialData

## Priority

P2

## Complexity

M

## Depends On

- `148-cyclicalwaves-v2-independent-financial-data-provider` (gauge and income-statement tables)
- `159-capability-source-map-comprehensive-analysis/changes/standalone-valuation-gauge-cards` and
  `.../valuation-gauge-cards-visual-refresh` (released)

## References

- `design-proposal.md` in this folder (approved at Gate A on 2026-11-26; OQ-143 to OQ-147 answered as proposed)
- Evidence: `tools/research/forward-pe-reverse-engineering/REPORT.md` sections 9 and 10, `results/margin-options.csv`
- `docs/providers/cyclicalwaves-v2/README.md` (gauge cards, the «never estimated» rule this change replaces)
- `docs/domain-glossary.md` → Valuation Ratios
- Data model: `FinancialIngestionDbContext` (`CyclicalWavesV2GaugeDetails`, `CyclicalWavesV2IncomeStatements`,
  `CyclicalWavesV2IncomeStatementValues`, `CyclicalWavesV2Tickers`, `Companies`, `Industries`, `MonthlyReports`,
  `MonthlyReportLineItems`)
- Code: `src/backend/FinancialCopilot.Application/FinancialData/ValuationGauges/`,
  `.../AI/Orchestration/ValuationGauges/ValuationGaugeTextComposer.cs`,
  `src/frontend/src/components/app/valuation-gauge-cards.tsx`, `src/frontend/src/lib/chat/valuation-gauges.ts`

## Standards Source Of Truth

Obey `.claude/rules/*`. Do not copy rule text into this spec.

## User Story

As a Sapio user looking at a symbol's P/E or P/S gauge,
I want to see Sapio's forward estimate of that ratio next to today's value,
so that I can judge the valuation against where sales and profit are heading, knowing it is Sapio's estimate.

## Business Context

TahlilApp's own page draws a forward P/E needle and Sapio's cards do not (OQ-136). The provider API has no forward
value. The owner decided on 2026-11-26 that Sapio shows its own estimate under its own name; matching TahlilApp's
number is not a goal.

## Scope

- A forward estimate on the P/E card and on the P/S card (web card, text answer, Telegram text).
- One pure estimator, one input reader, configuration with a flag (default off) and guard thresholds.
- Replacing the «never estimated» rule in the provider README and the glossary line.

## Out Of Scope

- Matching TahlilApp's forward number, and any forward field from the provider (→ stays possible later).
- Stored or precomputed estimates, migrations, new tables, new API routes.
- The equilibrium card, routing, billing, the cheap/expensive label, the PNG renderer.
- Refineries and companies without monthly sales reports (no estimate in this phase → `specs/backlog.md`).
- Filling a missing discrete quarter from year-to-date figures (→ `specs/backlog.md`).

## Functional Requirements

1. **Formula** (constants fixed in code: 12 months, 4-month growth window, 8 quarters):
   - `forecast sales = Σ last 12 monthly sales × (Σ last 4 months ÷ Σ the same 4 months one year earlier)`;
   - `forward margin = median of the last 8 quarterly net margins` (net profit ÷ revenue, discrete quarters);
   - `forward P/S = current P/S × Σ revenue of the last 4 quarters ÷ forecast sales`;
   - `forward P/E = current P/E × Σ net profit of the last 4 quarters ÷ (forecast sales × forward margin)`.
   "Current" is the gauge `close` already shown on the card. Each ratio uses its own gauge (see Assumptions, A1).
2. **Inputs.** Monthly sales: accepted monthly activity reports of the security's company, the month itself (product
   sales output type 0, or service sales), summed over line items. Quarterly revenue and net profit: the stored
   TahlilApp income statements, discrete quarter values, with the line keys Feature 148 already defines.
3. **No estimate** (the card is exactly as today) when any of these holds. Each has a reason code for telemetry:
   - the flag is off;
   - the card is not displayable or has no current value;
   - fewer than 16 consecutive monthly reports (period ends at most 35 days apart), or the latest month ended more than `MaxMonthlyReportAgeDays` ago;
   - the comparison months sum to zero or less;
   - the growth factor is outside `MinGrowthFactor`–`MaxGrowthFactor`;
   - fewer than 8 consecutive quarters (period ends at most 100 days apart, ending at the latest statement) with
     revenue > 0 and a net profit value;
   - forward P/E only: the forward margin is ≤ 0, or the current P/E or the 4-quarter net profit is ≤ 0;
   - the company's industry is in `ExcludedIndustryExternalIds`, or the company cannot be matched uniquely;
   - the input read fails or exceeds its time budget.
   The forward P/S can be shown when only the forward P/E is unavailable.
4. **Card payload.** `ValuationGaugeCard` gains an optional `forward` member (additive in payload version 1): value,
   needle angle and side on the card's own axis, method version `sapio-1`, growth factor, margin percent, latest
   sales month, latest quarter. Cards stored without it still render.
5. **Label.** The provider `status`, the position, the insights and the card state do not depend on the estimate.

## Data / Schema

No schema change and no migration. Read-only queries on `FinancialIngestionDbContext`. The reader maps the gauge
ticker to the monthly-report company through the ticker's ISIN and `Companies.SymbolIsin`; zero or several matches
give no estimate.

## API (if any)

No new route. The existing gauge card payload gains the optional `forward` member.

## AI Query Path (if any)

Unchanged: capability `valuation_gauges` on the V2 path, no LLM in the answer. The text answer gains one line per card.

## UI (if any)

- Web (`valuation-gauge-cards.tsx`): a dashed second needle with a ring at the forward value (pinned at the axis end
  when outside), and a chip «P/E آینده‌نگر (برآورد ساپیو) ‹مقدار›» / «P/S آینده‌نگر (برآورد ساپیو) ‹مقدار›» next to
  today's chip. In «جزئیات»: «برآورد ساپیو بر پایهٔ فروش ۱۲ ماه اخیر، رشد سالانهٔ ۴ ماه اخیر و میانهٔ حاشیهٔ سود ۸ فصل؛
  پیش‌بینی قطعی و توصیهٔ سرمایه‌گذاری نیست.» (the P/S card omits the margin clause). The needle has an accessible name.
- Text and Telegram (`ValuationGaugeTextComposer`): «P/E آینده‌نگر (برآورد ساپیو): ‹مقدار›» under the current value,
  only when present. The source line stays «تحلیل‌اپ» for the gauge; the estimate is never attributed to it.
- Numbers use the same digit and rounding rule as the current value (two decimals).

## Non-Functional Requirements

- Configuration `ValuationGaugeCards:ForwardEstimate`: `Enabled` (false), `MinGrowthFactor` (0.5), `MaxGrowthFactor`
  (4), `MaxMonthlyReportAgeDays` (75), `ExcludedIndustryExternalIds` (`["31"]`), `ReadTimeoutMilliseconds` (1000).
  Invalid values fail startup validation.
- The input read has its own budget and runs only when the flag is on and a P/E or P/S card is displayable. A
  timeout or failure drops only the estimate, is logged once with the reason, and never fails the card. Caller
  cancellation is rethrown.
- At most 16 monthly rows and 8 statement rows per request are read; no unbounded query.
- Telemetry through the existing gauge observer: estimate shown or not, reason code, read latency.
- The estimator is pure (no EF, HTTP, clock or provider types) and deterministic.

## Acceptance Criteria

- [x] AC-01 With the flag off, payloads, text answers and cards are identical to today's (existing tests unchanged).
- [x] AC-02 With the flag on and complete inputs, the P/E and P/S cards carry `forward` with the values the formula
      gives, and the web card shows the dashed needle and the «(برآورد ساپیو)» chip.
- [x] AC-03 The estimator reproduces every `expected` member of
      `tools/research/forward-pe-reverse-engineering/results/estimator-fixtures.json` (values to two decimals and
      reason codes).
- [x] AC-04 Each guard in requirement 3 yields no estimate with its own reason code, and the card is otherwise unchanged.
- [x] AC-05 A negative or zero forward margin hides the forward P/E while the forward P/S is still shown.
- [x] AC-06 A company in an excluded industry (fixture: شپنا, industry 31) gets no estimate.
- [x] AC-07 A reader timeout or exception gives the card without an estimate; caller cancellation propagates.
- [x] AC-08 A forward value outside the axis pins the dashed needle at the end; the solid needle is unaffected.
- [x] AC-09 Text answer and Telegram show the forward line only when the value exists, with the Sapio label.
- [x] AC-10 Cards stored before this change render unchanged from history.
- [x] AC-11 Credits charged for gauge answers are unchanged (existing billing tests green).
- [x] AC-12 Architecture tests stay green: the estimator and the port have no infrastructure dependency.
- [x] AC-13 `docs/providers/cyclicalwaves-v2/README.md` and `docs/domain-glossary.md` state the approved definition.

## Assumptions

- A1 (planning clarification of the approved formula): the proposal wrote `forward P/E = forward P/S ÷ margin`. The
  plan uses each ratio's own gauge instead. The two are equal when both gauges imply the same market value, which
  holds for 22 of the 25 research symbols; the exceptions (کگل, کچاد, آپ) have statement irregularities. The research
  numbers were produced this way.
- A2 Production coverage (16 months and 8 quarters per gauge company) was not measured; the local database is the only
  evidence. A read-only count runs before the flag is turned on (tasks, Rollout).
- A3 Industry `31` is the stored code of «فرآورده های نفتی، کک و سوخت هسته ای». It is wider than refineries; the owner approved excluding the whole industry in phase 1 (OQ-148).
- A4 The evidence is 24 symbols on one date (1405/09/03). The estimate differs from TahlilApp's number by about 17% on
  average; the owner accepted this.
