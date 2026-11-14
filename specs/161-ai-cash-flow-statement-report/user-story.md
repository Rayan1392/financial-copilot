# User Story: AI Cash Flow Statement Report (صورت جریان‌های نقدی)

## Feature Folder

`161-ai-cash-flow-statement-report`

## Layers / Area

Layers: backend | frontend | telegram-gateway (text rendering only)
Area: AI | FinancialData

## Priority

P1

## Complexity

XL (delivered in the gated phases of [tasks.md](tasks.md))

## Depends On

154-ai-balance-sheet-report, 144-quarterly-financial-reports-table, 117, 120, 121

## References

- `specs/161-ai-cash-flow-statement-report/design-proposal.md` (approved at Gate A on 2026-11-14, with the owner's amendment: operating cash flow = item 128)
- [design-proposal-mapping-matrix.md](design-proposal-mapping-matrix.md) — item roles, sections, metrics (single source for the line registry)
- [design-proposal-routing-matrix.md](design-proposal-routing-matrix.md) — routing rows 1–55 and follow-ups U1–U7
- [design-proposal-ux.md](design-proposal-ux.md) — layout, waterfall, states, AC-U01…AC-U08
- [design-proposal-draft-plan.md](design-proposal-draft-plan.md) — synthetic cases S-1…S-15 and exact expected results (§4–§6)
- [discovery.md](discovery.md) — findings R-01…R-28, evidence gaps E-01…E-11
- `docs/domain-glossary.md` terms used: صورت جریان وجوه نقد, جریان نقد عملیاتی, مانده وجه نقد, صورت مالی جداگانه/تلفیقی
- `docs/financial-statement-model.md` (statement headers, line items, source-item catalog); `docs/architecture.md` (V2 orchestration)
- Data model: `FinancialStatement`, `FinancialStatementLineItem`, `SourceItemCatalog` (no change)
- Related specs: `specs/154-ai-balance-sheet-report/design.md`, `specs/144-quarterly-financial-reports-table/design.md`, `specs/POLICY-V1-FREEZE.md`

## Standards Source Of Truth

Obey `.claude/rules/*`. Do not copy rule text into this spec.

## User Story

As an investor or analyst using Sapio (web, `/m`, Telegram),
I want to ask in Persian for a company's cash flow statement, a single cash-flow figure, a cash-flow analysis or a period comparison,
so that I can see where the company's cash came from and went, on the Codal classification, with totals that are checked and never shown as verified when they do not reconcile.

## Business Context

Cash flows are the third primary statement after the quarterly income statement (Feature 144) and the balance sheet (Feature 154). Today a cash-flow question opens the Feature 083 full table, which mixes the legacy, «- جدید» and Codal items, and the only existing cash-flow metric reads legacy item 1. Users and the owner need one trustworthy presentation for both manufacturing companies and banks.

## Scope

- Default report: latest standalone cash flow statement (year-to-date) plus the same stage of the previous fiscal year.
- Explicit period («شش‌ماهه ۱۴۰۵», «سالانه ۱۴۰۴», a period-end date); quarterly view (standalone quarters from cumulative statements, default 4, max 8); annual view (default 3, max 5).
- Metric focus, analysis focus (deterministic facts, no verdict words) and the follow-ups in routing matrix §5.
- General and Banking templates, selected only by `QuarterlyFinancialReports:BankingIndustryGroupId`.
- Reconciliation checks, verification status, warnings, per-cell provenance.
- Web wide/compact layouts, waterfall and trend charts, PNG export, Telegram text.
- Release flag `CashFlowReport:Enabled` (default `false`).

## Out Of Scope

- Consolidated (تلفیقی) cash flows; legacy five-section presentations; fallback to items 126/127 or legacy totals (OQ-074 (a)).
- Deriving operating cash flow from `122 − 121` when item 128 is missing (OQ-087, proposal (a); open).
- Two-company comparison in one answer (guided response only, OQ-080 (a)); verdict words (OQ-079 (a)).
- Changing the existing item-1 `OPERATING_CASH_FLOW` metric (OQ-081 (a) → separate change after evidence).
- Bank regulatory liquidity metrics, PDF export, migrations, provider calls, catalog writes, V1, Feature 141, Feature 081 internals.

## Functional Requirements

1. **Totals.** Operating = item **128** («جریان وجه نقد عملیاتی - جدید»); investing = item **121** (Codal); financing = item **123** (Codal); net change 124; opening 33; FX 35; closing 125. Items 1, 58–63, 106–113, 126, 127 are never used for values.
2. **Presentation policy.** `120 ≠ 0` → `unsupported_presentation`; 121 or 123 not reported → `codal_totals_missing`; 128 not reported → `operating_total_missing` (no derivation, OQ-087).
3. **Reconciliation.** R1 `128 + 121 + 123 = 124` and R2 `33 + 124 + FX = 125` are mandatory and exact (OQ-086 (a)). V3 `128 + 121 = 122` is checked when 122 is reported; a mismatch is a warning (`ocf_codal_subtotal_mismatch`). FX rules FX-1…FX-4 (design §4.4); 124 is never adjusted. Opening continuity C1 and quarterly C2 as in design §4.3.
4. **Failure display.** A failed mandatory check shows the published figures with an error banner, the difference and no verified badge (OQ-075 (a)).
5. **Detail rows.** Only items whose section is verified are shown; each section shows a residual row «سایر اقلام … (مابه‌التفاوت)» when it is not 0 (OQ-077 (a)).
6. **Metrics** per mapping matrix §5: CapEx = gross purchases of items 10, 48, 46; FCF = OCF − CapEx, never OCF + investing (OQ-076 (a)); bank template shows no FCF, CapEx coverage, cash conversion, OCF/revenue or net-debt indicator.
7. **Periods.** Standalone quarters by YTD differencing within one fiscal year (`FiscalCalendarPolicy`); balances never differenced; version ranking through one shared `FinancialStatementVersionPolicy`.
8. **Routing** (V2 only, flag on) as the routing matrix: cash-flow aliases (whole word, never inside «نقدینگی»), metric, analysis and view cues; «تحلیل جریان نقد X» → Feature 161 analysis (OQ-078 (a)); «مانده وجه نقد» → Feature 161, «موجودی نقد» unchanged (OQ-082 (a)); full-table and consolidated requests keep Feature 083. Flag off → today's routing unchanged.
9. **Contract.** Typed `CashFlowReportResult` (`contractVersion "161.1"`, design §6) on `FinancialStatementTableResult.CashFlowReport`, persisted and replayed without recomputation.
10. **Bank notice** copy as written in UX §8 (OQ-085 (a)).

## Data / Schema

No schema change and no migration. Read-only from the existing financial-statement tables through a new Application port `ICashFlowStatementReader` and `EfCoreCashFlowStatementReader`: ≤ 7 round trips, ≤ 12 cash-flow and ≤ 2 income statements per request (design §7).

## API (if any)

| Method | Route | Request | Response | Notes |
|---|---|---|---|---|
| (existing) | V2 AI query endpoints | unchanged | `financialStatementTableResult.cashFlowReport` added | Pass-through of the typed contract; no new endpoint |

## AI Query Path (if any)

Existing `financial_statement_table` capability on the V2 path with `UseCashFlowReport` and a typed `CashFlowRequestCue`; interpreter marker `feature161-v2-governed-report`; conversation marker `;report=feature161_cash_flow_report`. Outcomes map to Feature 117 reason codes (design §8.8), including `operating_total_missing` → `data_stale_or_ineligible`. The LLM performs no arithmetic.

## UI (if any)

`src/frontend/src/components/app/cash-flow-report/*` (types, model, layout chooser, wide, compact, waterfall chart, trend chart, PNG), branched from `message-list.tsx`; `/m` uses the compact layout. Persian, RTL, fa-IR digits, fixed light report surface. Telegram receives the text rendering (< 3,900 characters). Full specification: UX §1–§11.

## Non-Functional Requirements

- p95 use-case latency ≤ 800 ms on Development data; bounded reads enforced by a command-counting test; existing EF command timeout; no retry inside the use case; no cache.
- A failed anchor read fails closed with a typed outcome; a failed comparison column or income-statement read degrades only that column or indicator.
- Telemetry tags `feature161.*` (design §10) without personal data; authorization and billing reuse the existing AI query path (no charge for NoData).
- No company identifier or ticker literal in Feature 161 code (architecture test).
- No new build warnings; no new frontend type errors beyond `specs/test-baseline.md`; keyboard and screen-reader accessible.

## Acceptance Criteria

Exact inputs and values are in [design-proposal-draft-plan.md §4–§6](design-proposal-draft-plan.md).

**US-1 Full report**
- [ ] AC-R01: the anchor is the latest eligible standalone statement; the comparison column is the same stage of the previous fiscal year or a typed unavailable reason.
- [ ] AC-R02: sections Operating / Investing / Financing / Reconciliation; subtotals equal items 128, 121, 123; reconciliation shows 124, 33, FX, 125.
- [ ] AC-R03: for فلوله (255, 1405/06/31) the report shows OCF 6,918,890; investing (359,104); financing 9,025,064; net change 15,584,850; opening 6,692,454; FX ۰; closing 22,277,304; V3 6,559,786 = item 122; status `verified`.
- [ ] AC-R04: no value from items 1, 58–63, 106–113, 126, 127 appears in any row.
- [ ] AC-R05: header shows company, ticker, period label, Jalali period end, audited badge, «جداگانه», publication date, «میلیون ریال», source.
- [ ] AC-R06: 128 not reported → `operating_total_missing`; 121 or 123 not reported → `codal_totals_missing`; 120 ≠ 0 → `unsupported_presentation` (S-5, S-6b, S-6c).

**US-2 Bank report**
- [ ] AC-B01: template `BankingCashFlow` only via GroupId; no ticker or company-id condition exists (architecture test).
- [ ] AC-B02: وبملت (452, 1405/06/31): OCF 386,206,543; closing 3,536,996,227 = opening 2,718,638,069 + net change 200,570,858 + FX 617,787,300; the waterfall has an FX step.
- [ ] AC-B03: no FCF, CapEx coverage, cash conversion, OCF/revenue or net-debt indicator; bank notice shown.
- [ ] AC-B04: financing and investing details close to 123 and 121 with residual 0.

**US-3 Metric**
- [ ] AC-M01: «جریان نقد عملیاتی فلوله چقدر است؟» → one sentence with metric, value, period and verification status; the full report is in the same payload (no second query or charge).
- [ ] AC-M02: «جریان نقد آزاد» for a bank → typed «برای بانک‌ها محاسبه نمی‌شود», not a number.
- [ ] AC-M03: «وبملت چقدر سود سهام نقدی پرداخت کرده؟» → item 6 (76,072,909), never DPS.

**US-4 Analysis**
- [ ] AC-A01: each analysis topic yields a fixed set of deterministic fact sentences from typed indicators; no LLM text; no verdict words.
- [ ] AC-A02: net profit ≤ 0 or income statement missing → the fact states the reason and still shows OCF.

**US-5 Periods**
- [ ] AC-P01: Qn = YTD(3n) − YTD(3n−3) within one fiscal year; Q4 = 12M − 9M; missing predecessor → unavailable cell with reason (S-8 values).
- [ ] AC-P02: balances are never differenced.
- [ ] AC-P03: «شش‌ماهه ۱۴۰۵» selects months 6, fiscal-year label 1405; a miss lists the nearest periods (S-14).

**US-6 Integrity**
- [ ] AC-V01: R1 and R2 exact; a failure shows the error banner, the difference and no verified badge (S-1, S-7).
- [ ] AC-V02: FX-1…FX-4 each tested (S-2…S-4); 124 never adjusted; V3 mismatch alone → `verified_with_warnings` (S-15).
- [ ] AC-V03: every displayed number has provenance (statement ids, line ids, source item ids, formula).

**US-7 Conversation and routing**
- [ ] AC-C01: routing matrix rows 1–55 and follow-ups U1–U7 route as specified with the flag on.
- [ ] AC-C02: with the flag off, every row equals the T-R00 baseline; Feature 083/144/150/154/159 routing suites stay green; V1 unchanged.

**US-8 Presentation**
- [ ] AC-U01…AC-U08 in [design-proposal-ux.md §9](design-proposal-ux.md).

**Live data**
- [ ] AC-L01: T20 records the live Development results for 255 and 452 (and the extra issuers in T20); until then no document claims live validation.

## Assumptions

- The فلوله and وبملت figures are fixtures supplied with the brief; no database has been queried yet (T01/T20).
- Item 128 is reported on the three-section statements that Feature 161 must show; T01 measures its coverage and how often `128 + 121 = 122` holds (E-03). If coverage is poor, OQ-087 is revisited.
- The bank GroupId configured in each environment is correct (E-10).
