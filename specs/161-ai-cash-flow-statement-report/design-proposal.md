# Feature 161 Design Proposal — AI Cash Flow Statement Report (صورت جریان‌های نقدی)

Status: Gate A: Approved by the owner on 2026-11-14

| Field | Value |
|---|---|
| Feature folder | `161-ai-cash-flow-statement-report` |
| Layers / Area | backend · frontend · telegram-gateway (text only) — AI · FinancialData |
| Priority (proposed) | P1 |
| Complexity (proposed) | XL (split into gated phases) |
| Depends On | 154-ai-balance-sheet-report (patterns, `FiscalCalendarPolicy`, bank GroupId rule), 144-quarterly-financial-reports-table (ranking and YTD-differencing rules), 117, 120, 121 |
| References | [discovery.md](discovery.md); [mapping matrix](design-proposal-mapping-matrix.md); [routing matrix](design-proposal-routing-matrix.md); [UX specification](design-proposal-ux.md); [draft plan and tests](design-proposal-draft-plan.md); `docs/domain-glossary.md`; `docs/financial-statement-model.md`; `specs/POLICY-V1-FREEZE.md`; `specs/154-ai-balance-sheet-report/design.md` |

> **Gate A record (2026-11-14).** The owner approved this design with one amendment: «جریان وجه نقد عملیاتی - جدید = 121 استفاده کن، بقیه موارد مورد قبول است». Read as: operating cash flow is **item 128** («جریان وجه نقد عملیاتی - جدید»), shown together with the Codal investing total **item 121** and the Codal financing total 123. This replaces the derivation `122 − 121` (former D-03 recommendation); item 122 becomes a consistency check. All other recommendations (D-01…D-18) and the agent's proposals for OQ-074…OQ-086 are accepted. One new decision arising from the amendment is OQ-087 (item 128 not reported). The finalized specification is [user-story.md](user-story.md); the plan awaiting Gate B is [tasks.md](tasks.md).

This is the Gate A proposal. It contains the requested specification (Deliverable A), technical plan (B), mapping matrix (D), routing matrix (E) and UX specification (F). The task breakdown (C) is a **draft** in [design-proposal-draft-plan.md](design-proposal-draft-plan.md); per `specs/FEATURE_DEVELOPMENT_WORKFLOW.md` it becomes `user-story.md` and `tasks.md` only after Gate A, and implementation needs Gate B. **Nothing is implemented. No database was queried; the فلوله and وبملت figures are fixtures, not verified Development data.**

---

## 1. Summary

Add the cash flow statement as the third governed statement report, next to the Feature 144 quarterly income statement and the Feature 154 balance sheet. The design reuses the Feature 154 shape:

- provider-aware company resolution and GroupId-only bank classification;
- a bounded, read-only persisted read behind an Application port (no provider call, no migration);
- deterministic Application policies that own the mapping, the Codal presentation selection, the derivation of operating cash flow, reconciliation, standalone-quarter derivation and industry-aware metrics;
- a typed, versioned result carried on `FinancialStatementTableResult`, persisted and replayed without recomputation;
- typed wide/compact renderers, a waterfall chart, PNG export and a Telegram text rendering.

No new capability ID: the existing `financial_statement_table` capability gains a Feature 161 request form, exactly as Feature 154 did. The LLM never performs arithmetic; it only takes part in interpretation through the existing hybrid interpreter.

## 2. Business scope (Deliverable A, part 1)

### 2.1 Objectives

1. Answer natural-language requests for a company's cash flow statement, single cash-flow figures, deterministic cash-flow analyses and period comparisons.
2. Present operating, investing and financing flows, the cash reconciliation and FX effect on the **Codal** classification only.
3. Give defensible derived indicators (FCF, CapEx coverage, cash conversion, OCF/revenue, net debt cash flow) where they are meaningful, and withhold them where they are not (banks, non-positive denominators, missing data).
4. Never present an unreconciled report as verified.

### 2.2 Personas

| Persona | Need |
|---|---|
| Retail investor (web, `/m`, Telegram) | "Does this company really generate cash?" in plain Persian, one tap to the full statement |
| Active analyst | Exact figures, period-over-period and quarter-by-quarter comparisons, provenance, PNG for sharing |
| Bank-sector investor | Bank cash flows with FX shown explicitly, without misleading industrial ratios |
| Product owner / data admin | Visible verification state and warnings, telemetry for data quality |

### 2.3 In scope (v1, subject to Gate A)

- Default report: latest standalone cash flow statement (year-to-date), with the same stage of the previous fiscal year as the comparison column.
- Explicit period: «شش‌ماهه ۱۴۰۵», «سالانه ۱۴۰۴», a Jalali/ISO period-end date.
- Quarterly view (standalone quarters derived from cumulative statements; default 4, max 8) and annual view (12-month statements; default 3, max 5).
- Metric focus (single figure + KPI card + the full report one tap away, no second query).
- Analysis focus: deterministic fact sentences for earnings quality, operating cash generation, financing dependence, cause of the cash change, financing structure.
- General and Banking templates selected by `QuarterlyFinancialReports:BankingIndustryGroupId`.
- Reconciliation checks, verification status, warnings and provenance per cell.
- Web (wide and compact), `/m`, PNG export, Telegram text.
- Follow-ups: repeat for another company, previous period, previous year, quarter-by-quarter, compare with net profit, financing situation.

### 2.4 Out of scope (v1)

- Consolidated (تلفیقی) cash flows — standalone only, as in Features 144/154; a consolidated statement only triggers a suggestion (OQ-084).
- Legacy five-section presentations (`unsupported_presentation`), fallback to the «- جدید» investing/financing totals 126/127 or legacy totals (OQ-074), and deriving operating cash flow from `122 − 121` when item 128 is missing (OQ-087).
- Two-company comparisons in one answer — v1 returns a guided response with one suggested action per company (OQ-080).
- Qualitative verdicts («پشتوانه نقدی قوی/ضعیف») — facts only, until the owner approves thresholds (OQ-079).
- Regulatory bank liquidity metrics (LCR, NSFR, deposit movements) — not in the data.
- Changing the existing item-1 `OPERATING_CASH_FLOW` metric (OQ-081), Feature 081 internals, V1, Feature 141.
- PDF export (no PDF pipeline exists; PNG only).
- Any migration, provider call, worker or catalog write.

## 3. Decisions proposed for Gate A

Each decision has a recommendation; the alternatives are recorded so the owner can choose. Decisions marked **OQ** are also in `specs/open-questions.md`.

| ID | Decision | Recommendation | Alternatives | Evidence |
|---|---|---|---|---|
| D-01 | Capability shape | Extend `financial_statement_table` with `UseCashFlowReport` + a typed `CashFlowRequestCue` (kind: report/metric/analysis, view, period). The conceptual intents CASH_FLOW_REPORT / METRIC / ANALYSIS / COMPARISON map to `kind` + `view`. | New capability IDs (more registry, prompt and governance churn; Feature 154 avoided it) | R-12, R-14 |
| D-02 | Presentation basis | Per statement: required totals are 128 (operating, D-03), 121 (investing, Codal) and 123 (financing, Codal); 124/125/33 for verification; 122 for the subtotal check. If 121 or 123 is missing → `codal_totals_missing` for that column; 128 missing → see D-03. «- جدید» fallback disabled until E-03 is closed and the owner enables it. Legacy never used. | (b) enable the 126/127/128 fallback now with a visible «مبنای ارائه: جدید» badge; (c) legacy totals | R-06, R-08, **OQ-074** |
| D-03 | Operating cash flow | **Approved with amendment:** `OCF = item 128` («جریان وجه نقد عملیاتی - جدید»), only on three-section statements (`120` absent or 0). Check V3: `128 + 121 = 122` when 122 is reported; different → warning `ocf_codal_subtotal_mismatch`, 128 stands, status cannot be `verified`. 128 not reported → `operating_total_missing` for that column, no derivation (OQ-087, proposal (a)). `120 ≠ 0` → `unsupported_presentation`. | Derive `122 − 121` (original recommendation, replaced by the owner) | R-08, §3 fixtures, owner Gate A |
| D-04 | FX treatment | Rules FX-1…FX-4 (§4.4): FX is added only through item 35 and only in R2; 124 is never adjusted; an absent 35 counts as 0 only when R2 closes without it. | Always treat absent 35 as 0 (could hide an R2 break) | R-10, E-07 |
| D-05 | Reconciliation failures | Show the report with an error banner and status `failed`; never show the verified badge; metrics depending on a failed total are marked `unverified`. Exact equality, no tolerance. | (b) withhold the whole report; (c) ±1 million IRR tolerance | **OQ-075**, **OQ-086** |
| D-06 | Version and scope selection | Standalone only (`IsComposing = false`), `AssumedScale = MillionRials`, Feature 144/154 ranking (audited ↓, represented ↓, publishedAt ↓, externalStatementId ↓, id ↓); same-rank peers that differ on any mapped item → `conflicting_report_revision` for that column (anchor → report outcome). Extract the ranking into one shared `FinancialStatementVersionPolicy` used by Features 154 and 161 (behavior-preserving; Feature 154 tests guard it). Feature 144 is not touched. | Copy the ranking a third time (R-17) | R-16, R-17, **OQ-084** |
| D-07 | Views and default comparison | Default = anchor YTD + same stage of the previous fiscal year (same `PeriodType`, previous fiscal year by `FiscalCalendarPolicy`). Quarterly = standalone quarters by YTD differencing. Annual = 12-month statements. | Default = previous report (mixes YTD lengths, not comparable for flows) | R-16 |
| D-08 | Detail rows | Only *verified* items (S, or P after evidence) appear as detail rows. Each section shows a residual row «سایر اقلام … (مابه‌التفاوت)» = subtotal − Σ displayed details, when ≠ 0. Operating shows tax paid as an «از جمله» row only. | Caption «اقلام منتخب» without residual (Feature 154 style) | R-05, R-11, **OQ-077** |
| D-09 | Metrics | Definitions in the [mapping matrix §5](design-proposal-mapping-matrix.md#5-derived-rows-and-metrics-single-owner-cashflowmetricscalculator). CapEx = gross purchases of PP&E, intangibles and productive biological assets (10, 48, 46); FCF = OCF − CapEx; never OCF + investing. | Net CapEx (minus disposals 14, 83, 47); include 130 | **OQ-076** |
| D-10 | Industry handling | Template by GroupId only (Feature 144/154 rule). Bank template: no FCF, CapEx coverage, cash conversion or OCF/revenue; FX always shown; facilities rows keep the Codal title with a note; fixed explanatory notice. Applicability lives in one `CashFlowIndustryPolicy`. | Ticker lists (forbidden) | R-15, **OQ-085** |
| D-11 | Cross-statement metrics | IncomeStatement for the **same** company, provider, standalone scope, `PeriodEnd` and `PeriodType`, ranked by the same policy; YTD basis only (no quarterly ratios in v1). Missing → metric `income_statement_unavailable`. | Nearest period (misaligned) | Feature 081 rule style |
| D-12 | Existing routes | With the flag on: a cash-flow alias opens Feature 161; «جدول کامل/همه اقلام», «تلفیقی», audited/restated filters keep Feature 083 (as Feature 154 did). «تحلیل جریان نقد X» opens the Feature 161 analysis focus instead of Feature 081, because Feature 081 can only read item 1 (R-07). | Keep «تحلیل …» on Feature 081 (inconsistent numbers) | R-18, R-20, **OQ-078** |
| D-13 | Analysis answers | Deterministic fact sentences from typed indicators (no LLM narrative, no verdict words). | LLM narrative grounded on the typed result | R-23, **OQ-079** |
| D-14 | Two companies | Guided response + two suggested actions in v1. | Single-metric two-company comparison in v1 | **OQ-080** |
| D-15 | «مانده وجه نقد» vs «موجودی نقد» | «مانده وجه نقد/وجه نقد پایان دوره» → Feature 161 closing cash (125). «موجودی نقد» keeps today's route. | Route both to Feature 161 | **OQ-082** |
| D-16 | Rollout | Flag `CashFlowReport:Enabled` (default `false`). Off = today's routing byte-for-byte. | No flag (Feature 154 style) | Release It!, **OQ-083** |
| D-17 | Persistence | No migration, no schema, no catalog write, no cache. | — | R-04, R-05 |
| D-18 | Export | PNG only, through the shared canvas brand header/footer. | Add PDF (new pipeline; out of scope) | R-26 |

## 4. Accounting rules (single owners in the Application layer)

### 4.1 Presentation selection — `CashFlowPresentationPolicy`

```text
for each selected statement s:
  if value(120) is reported and ≠ 0                     → column state unsupported_presentation
  else if s has any of {121,123} not reported           → column state codal_totals_missing   (OQ-074 (a): no fallback)
  else if 128 not reported                              → column state operating_total_missing (OQ-087)
  else                                                  → presentationBasis = codal (operating total = 128)
consolidated-only items {22,136..139} present on standalone → warning consolidated_item_on_standalone
legacy {1,58..63} and aggregates {106..113}             → never read for values (1 only as provenance)
126, 127 («- جدید» investing/financing)                 → never read for values
```

### 4.2 Values, missing and zero — `CashFlowLineRegistry`

- A cell is **reported** when a line item exists with a non-null `Value` (0 included). Absent row and null value are both **not reported** (E-09 decides whether they differ).
- Values are signed as persisted (inflow +, outflow −), `decimal`, million IRR. No rounding in the backend; display rounding is the renderer's job.
- Duplicate line items for the same statement and item with different values → cell `ambiguous`, excluded from arithmetic, warning `duplicate_line_item`.

### 4.3 Reconciliation — `CashFlowReconciliation`

| Check | Formula | Mandatory | On failure |
|---|---|---|---|
| R1 | `128 + 121 + 123 = 124` | yes | status `failed`, banner, difference shown |
| R2 | `33 + 124 + FX = 125`, FX per §4.4 | yes | status `failed`, banner |
| V3 | `128 + 121 = 122` | when 122 reported | warning `ocf_codal_subtotal_mismatch`; max status `verified_with_warnings` |
| D-INV | `121 − Σ displayed investing details` | no | residual row; info when ≠ 0 |
| D-FIN | `123 − Σ displayed financing details` | no | residual row; info when ≠ 0 |
| C1 | opening continuity: `33(FY n) = 125(12M of FY n−1)` when both selected | no | warning `opening_balance_discontinuity` (restatement likely) |
| C2 | quarterly: both YTD operands share the same 33 | quarterly view | quarter column `unverified`, warning |

Status: `verified` (all mandatory pass, V3 passed or 122 not reported, no other warning), `verified_with_warnings`, `unverified` (a mandatory check not evaluable: 33, 124 or 125 not reported), `failed` (a mandatory check fails). Exact decimal equality; tolerance only if OQ-086 says so with evidence.

### 4.4 FX rules

- **FX-1.** 35 reported → R2 uses it; shown as its own row and waterfall step.
- **FX-2.** 35 not reported and `33 + 124 = 125` → FX shown as «۰ (گزارش نشده)», state `inferred_zero_by_reconciliation`; bank template shows the row anyway.
- **FX-3.** 35 reported ≠ 0 and `33 + 124 = 125` exactly → warning `fx_possibly_embedded_in_net_change`; R2 fails by design; nothing is adjusted.
- **FX-4.** 35 not reported and R2 does not close → status `failed`, warning `fx_missing_reconciliation_gap` with the gap amount.

124 is never modified, so FX cannot be counted twice.

### 4.5 Period selection and standalone quarters — `CashFlowPeriodSelector`

- Eligible headers: provider `NoavaranCurrentApi`, company `ExternalCompanyId`, `CashFlow`, standalone, `PeriodType ∈ {3,6,9,12}` months, scale `MillionRials`, scope metadata agreeing with `IsComposing` (Feature 144 R-16 rule).
- Fiscal slots: `FiscalCalendarPolicy.ResolveFiscalStart` / `ResolveEffectiveFiscalStarts` (transitional years rebased); a statement whose `PeriodEnd` is not `JalaliEndAfterMonths(start, months)` is not a YTD boundary and is never differenced.
- One statement per `(fiscalStart, months)` via `FinancialStatementVersionPolicy`.
- Standalone quarter `q` of fiscal year `y`: Q1 = YTD3; Qn = YTD(3n) − YTD(3n−3) for flow rows (128, 121, 122, 123, 124, 35, details); both operands Codal, same fiscal start, same presentation, both reported. Otherwise the cell is `unavailable` with reason `missing_predecessor` / `presentation_changed` / `fiscal_boundary_mismatch`.
- Balances are **never differenced**: quarter closing = 125 of YTD(3n); quarter opening = 125 of YTD(3n−3), or 33 for Q1; quarter R2 = opening + Δ124 + Δ35 = closing (C2).
- Annual view: 12-month statements only; opening = 33.
- Requested period «شش‌ماهه ۱۴۰۵»: months 6, fiscal year label 1405 = the Jalali year in which the fiscal year ends (`FiscalCalendarPolicy.FiscalYearLabel`). Not found → `no_statement_for_period` with nearest available periods.

### 4.6 Industry — `CashFlowIndustryPolicy`

| Item | General | Banking |
|---|---|---|
| OCF, investing, financing, net change, closing cash | KPI | KPI |
| FX effect | row when ≠ 0 or not inferred | always row + KPI + waterfall step |
| FCF, CapEx, CapEx coverage | KPI / indicator | not shown |
| Cash conversion, OCF/revenue | indicator (needs IS) | not shown |
| Net debt cash flow | indicator | not shown; rows 23/24 shown with note «طبق طبقه‌بندی کدال در فعالیت‌های تأمین مالی» |
| Dividends paid, treasury net, capital transactions | indicator | indicator |
| FX share of cash change | when 35 ≠ 0 and sign rule holds | indicator |
| Items 3, 42, 95, 96 | detail after E-06 | never detail (residual + provenance) |
| Notice | — | bank notice (UX §6, OQ-085) |

## 5. Architecture (Deliverable B)

### 5.1 Components

Verified existing components are marked **(existing)**; everything else is proposed.

| Layer | Component | Responsibility |
|---|---|---|
| Application policy | `CashFlowLineRegistry` (new) | Single owner of templates, sections, row order, labels, item roles, verification status, exclusions, metric components (the mapping matrix in code) |
| Application policy | `CashFlowPresentationPolicy` (new) | §4.1 (operating total = item 128) |
| Application policy | `FinancialStatementVersionPolicy` (new, extracted) | Ranking and same-rank conflict rule, shared by Features 154 and 161 |
| Application policy | `FiscalCalendarPolicy` **(existing)** | Fiscal starts, labels, Jalali month arithmetic |
| Application policy | `CashFlowPeriodSelector` (new) | Eligibility, fiscal slots, anchor, comparison column, quarterly/annual columns |
| Application policy | `CashFlowStatementBuilder` (new) | Cells, sections, residual rows, provenance, standalone-quarter differencing |
| Application policy | `CashFlowReconciliation` (new) | §4.3, §4.4, verification status, warnings |
| Application policy | `CashFlowMetricsCalculator` + `CashFlowIndustryPolicy` (new) | KPIs and indicators with null/denominator rules; applicability |
| Application policy | `CashFlowWaterfallBuilder` (new) | Waterfall steps with `from`/`to`, so the frontend does no arithmetic |
| Application policy | `CashFlowAnalysisFacts` (new) | Deterministic fact sentences per analysis topic from typed indicators |
| Application use case | `CashFlowReportQueryUseCase` (new) | Resolve → classify → read → select → build → reconcile → metrics → waterfall → facts; typed outcomes; callable without an LLM |
| Application ports | `ICashFlowStatementReader` (new); `ICompanyResolutionOutcomeService`, `IQuarterlyFinancialReportsRepository.GetEligibleCompanyGroupsAsync`, `BankingFinancialReportsConfiguration` **(existing)** | Plain records only |
| Infrastructure | `EfCoreCashFlowStatementReader` (new) | Bounded EF reads (§7); reuses `StatementMetadata.Parse` |
| Infrastructure | `CashFlowReportTextRenderer` (new) | Persian markdown for chat text and Telegram |
| Infrastructure | `FinancialStatementTableQueryUseCase` **(existing)** | Third branch on `UseCashFlowReport && StatementType == CashFlow` |
| AI orchestration | `FinancialStatementTableIntentRules.V2.cs` **(existing, extended)**, `DeterministicCapabilityInterpreter`, `CapabilityInterpretationGovernance`, `CapabilityRoutingPrecedence`, `FinancialStatementTableCapabilityExecutor`, `ConversationTaskStateContracts`, follow-up providers | Recognition, dispatch, outcome mapping, markers, suggestions only (§8) |
| API | `FinancialStatementTableHttpResponse.CashFlowReport` | Pass-through |
| Frontend | `components/app/cash-flow-report/*` (new) | View models and rendering; no financial arithmetic |

All dependencies point inward; every policy is unit-testable with plain records and no database.

### 5.2 Request carried by the existing query

`FinancialStatementTableQuery` gains `UseCashFlowReport`, `CashFlowView` (`default | quarterly | annual`), `CashFlowFocus` (`report | metric | analysis`), `CashFlowMetricId?`, `CashFlowAnalysisTopic?`, `RequestedPeriodMonths?`, `RequestedFiscalYear?`, `PeriodCount?`, `Dates?` (reuses `ResolveDates`).

## 6. Typed contract (`contractVersion: "161.1"`)

All discriminators are strings (Feature 154 R-33). Amounts are `decimal?` million IRR; ratios are full precision with display hints.

```text
CashFlowReportResult
  contractVersion, generatedAtUtc
  outcome: available | company_unresolved | company_ambiguous | provider_unavailable
         | company_classification_unavailable | no_standalone_cash_flow | no_statement_for_period
         | unsupported_presentation | codal_totals_missing | operating_total_missing | conflicting_report_revision
         | ineligible_storage_scale | unsupported_request
  outcomeReason?, message?, nearestPeriods?[], consolidatedAlternativeAvailable: bool
  templateId?: GeneralCashFlow | BankingCashFlow
  company { symbol, name, providerName, externalCompanyId }
  request { view, focus, metricId?, analysisTopic?, requestedPeriod? { months, fiscalYear }, periodCount? }
  statements { statementId → Statement }                       // each stored once; ≤ 12
  anchorStatementId?
  columns[ { columnId, kind: anchor | same_stage_previous_year | standalone_quarter | fiscal_year,
             label, fiscalYear, quarter?, periodMonths, periodEnd, jalaliPeriodEnd,
             derivation: reported_ytd | ytd_difference, statementIds[], state, reason? } ]
  sections[ { sectionId: operating | investing | financing | reconciliation, label,
              rows[ { rowId, label, kind: subtotal | detail | of_which | residual | balance | derived,
                      sourceItemIds[], note?, cells { columnId → Cell } } ] } ]
  changes? { baseColumnId, compareColumnId, rows { rowId → { amount?, percent?, state, reason? } } }
  kpis[ { metricId, label, columnId, value?, unit: million_irr | multiple | percent,
          state: available | unavailable | unverified, reason?, formula, components[] } ]
  waterfall? { columnId, steps[ { stepId, label, kind: start | delta | subtotal | end, value, from, to } ],
               flowSteps[ same shape, starting at 0 ] }                // UX §5.2 toggle; no frontend arithmetic
  verification { status: verified | verified_with_warnings | unverified | failed,
                 checks[ { checkId, columnId, state: passed | failed | not_evaluated,
                           expected?, actual?, difference? } ] }
  warnings[ { code, severity: info | warning | error, columnId?, message } ]
  notices[]                                                   // e.g. bank notice
  analysis? { topic, facts[ { factId, text, metricIds[], columnId } ] }

Statement { statementId, externalStatementId, periodEnd, jalaliPeriodEnd, periodType, periodMonths,
            isAudited, isRepresented, publishedAt?, jalaliFiscalYearEnd?, presentationBasis: codal,
            unit: million_irr }
Cell      { value?, state: available | not_reported | inferred_zero | ambiguous | unavailable,
            reason?, provenance { statementIds[], lineItemIds[], sourceItemIds[], formula? } }
```

Carrier: `FinancialStatementTableResult.CashFlowReport` next to `QuarterlyResult` and `BalanceSheetReport`; persisted and replayed through the same structured-payload path, replay never recomputes. Percent change follows Feature 154: amount always; percent only when the base value is > 0.

## 7. Data retrieval and bounds

`EfCoreCashFlowStatementReader`, read-only, no provider call, existing EF command timeout, no retry inside the use case.

1. **Catalog**: `NoavaranCurrentApi`, `CashFlow`, `SourceItemId ∈ registry ids` (by tuple).
2. **Headers**: same provider and company, `CashFlow`, standalone, supported period types, `PeriodEnd` desc then `LastSynchronizedAt` desc, **limit 48** (Feature 144/154 bound). Exact-date lookup for a needed date outside the window.
3. **Fiscal evidence**: newest 24 headers of any type for the company (Feature 154 rule).
4. **Line items**: one batched query for the selected statements (anchor + comparison, or quarterly operands, **≤ 12**) joined to the catalog.
5. **Income statement companions** (general template, default view only): headers at the same `PeriodEnd`/`PeriodType` and items 15, 143, 313 — one header query and one line query, **≤ 2** statements.
6. **Consolidated EXISTS**: only on `no_standalone_cash_flow`.

Bounds: ≤ 7 round trips; ≤ 12 cash-flow and ≤ 2 income statements; payload ≤ 12 statements, ≤ 9 columns. Enforced by a command-counting interceptor test (Feature 154 AC-X04 style). No cache.

## 8. Routing (summary — full matrix in [design-proposal-routing-matrix.md](design-proposal-routing-matrix.md))

All changes live in the V2 partial and V2-only branches; the V1 rules stay byte-for-byte (POLICY-V1-FREEZE, Feature 154 V1-freeze tests extended). When `CashFlowReport:Enabled = false`, none of the new predicates fire.

1. **Canonical aliases (V2 only, whole-word):** «صورت جریان‌های نقدی», «صورت جریانهای نقدی», «جریان‌های نقدی», «صورت گردش وجوه نقد», «گردش وجوه نقد», «گردش وجه نقد», «صورت جریان نقدی», "cash flow statement", "statement of cash flows", "cash flows" → «صورت جریان وجوه نقد». Guard: never match inside «نقدینگی» (R-20a).
2. **Metric cues** (`CashFlowMetricCues`, single owner): «جریان نقد عملیاتی», «جریان وجه نقد عملیاتی», «جریان نقد سرمایه‌گذاری», «جریان نقد تامین مالی», «خالص جریان نقد», «جریان نقد آزاد», «FCF», «free cash flow», «operating cash flow», «مانده وجه نقد», «وجه نقد پایان دوره», «اثر نرخ ارز», «تسهیلات دریافت», «دریافت تسهیلات», «بازپرداخت تسهیلات», «سود سهام پرداختی», «سود سهام نقدی پرداخت», «مخارج سرمایه‌ای», «capex», «خرید سهام خزانه». Each maps to one `metricId`.
3. **Analysis cues**: «پشتوانه نقدی», «کیفیت سود», «نقد تولید», «وابسته به تسهیلات», «علت افزایش/کاهش موجودی نقد», «وضعیت تامین مالی», «با سود خالص… مقایسه», plus «تحلیل/بررسی» + a cash-flow alias (D-12). Each maps to one `analysisTopic`.
4. **View cues**: «فصلی», «فصل به فصل», «N فصل اخیر» → quarterly; «سالانه», «N سال اخیر» → annual; «روند» + metric → quarterly with that metric focused.
5. **Precedence**: a cash-flow alias, metric or analysis cue beats the quarterly income-statement shorthand (close R-20), `TrendWords`, and «وضعیت» as an analysis word; exactly one statement alias is required («ترازنامه و جریان وجوه نقد» keeps today's route). The interpreter marker `feature161-v2-governed-report` joins the Feature 154 marker in `CapabilityRoutingPrecedence`.
6. **Dispatch**: `FinancialStatementTableQueryUseCase` third branch; Feature 083 keeps full-table and variant requests.
7. **Context**: marker `;report=feature161_cash_flow_report` on the statement-type slot; bare follow-ups continue the task; an explicit other statement alias switches statements.
8. **Outcomes → Feature 117**: `no_standalone_cash_flow`, `no_statement_for_period` → `supported_but_no_rows`; `company_classification_unavailable`, `conflicting_report_revision`, `ineligible_storage_scale`, `unsupported_presentation`, `codal_totals_missing`, `operating_total_missing` → `data_stale_or_ineligible`; ambiguous → `entity_ambiguous` (disambiguation); unresolved/provider → `entity_not_found`. Billing follows the existing no-data policy (no charge for NoData).

## 9. Frontend (summary — full specification in [design-proposal-ux.md](design-proposal-ux.md))

New folder `src/frontend/src/components/app/cash-flow-report/` mirroring Feature 154: `cash-flow-report-types.ts`, `cash-flow-report-model.ts` (formatting only), `cash-flow-report.tsx` (layout chooser), `cash-flow-report-wide.tsx`, `cash-flow-report-compact.tsx`, `cash-flow-waterfall-chart.tsx` (Recharts range bars from backend `from`/`to`), `cash-flow-trend-chart.tsx` (quarterly/annual), `cash-flow-report-image.ts` (canvas PNG with the shared brand helper). `message-list.tsx` branches on `financialStatementTableResult.cashFlowReport`; `/m` uses the compact layout.

## 10. Observability, failure behaviour, security

- Telemetry tags (no personal data): `feature161.template`, `.outcome`, `.view`, `.focus`, `.metric`, `.analysis_topic`, `.verification_status`, `.failed_checks`, `.warnings`, `.residual_nonzero_count`, `.columns`, `.statements_read`, `.read_latency_ms`.
- A failed anchor read fails the report closed with a typed outcome; a failed comparison/quarter column degrades only that column with a reason; a failed IS companion read only marks the dependent indicators `unavailable`.
- Read-only; authorization and billing reuse the existing AI query path; no new endpoint; no secrets in logs.

## 11. Rollout and rollback

1. Merge with `CashFlowReport:Enabled = false` in every environment (routing identical to today; covered by tests).
2. Run T01 evidence and the live Development verification; enable in Development.
3. Visual sign-off (desktop, `/m`, PNG, light/dark) and owner review of bank copy.
4. Enable per environment after E-10 (bank GroupId) is confirmed there.
5. Rollback: set the flag to `false` — no data to revert. Persisted answers keep replaying their stored typed payload; the frontend keeps rendering them.

## 12. Draft user stories and acceptance criteria (Deliverable A, part 2)

These become `user-story.md` after Gate A. Exact fixture values are in [draft plan §6](design-proposal-draft-plan.md#6-expected-results-for-the-supplied-datasets).

**US-1 Full report.** As an investor, I want «صورت جریان وجوه نقد فلوله» to show the latest standalone cash flow statement on the Codal classification, so that I can see where cash came from and went.
- AC-R01: the anchor is the latest eligible standalone statement; the comparison column is the same stage of the previous fiscal year or a typed unavailable reason.
- AC-R02: sections Operating/Investing/Financing/Reconciliation; subtotals equal 128, 121, 123; reconciliation shows 124, 33, FX, 125.
- AC-R03: for فلوله the report shows OCF 6,918,890; investing (359,104); financing 9,025,064; net change 15,584,850; opening 6,692,454; FX ۰; closing 22,277,304; status `verified` (V3: 6,918,890 − 359,104 = 6,559,786 = item 122).
- AC-R04: no value from items 1, 58–63, 106–113, 126, 127 appears in any row.
- AC-R05: header shows company, ticker, period label, period end (Jalali), audited badge, «جداگانه», publication date, «میلیون ریال», source.

**US-2 Bank report.** As a bank-sector investor, I want وبملت's statement with the FX effect explicit and no industrial ratios.
- AC-B01: template `BankingCashFlow` only via GroupId; no ticker condition exists in code (architecture test greps for tickers).
- AC-B02: closing 3,536,996,227 = opening 2,718,638,069 + net change 200,570,858 + FX 617,787,300; waterfall has an FX step.
- AC-B03: no FCF, CapEx coverage, cash conversion, OCF/revenue or net-debt indicator; bank notice shown.
- AC-B04: financing detail closes to 123 with residual 0; investing detail closes to 121 with residual 0.

**US-3 Metric.** As a user, I want «جریان نقد عملیاتی فلوله چقدر است؟» answered with one figure and the period, with the full report one tap away.
- AC-M01: one sentence with metric, value, period and verification status; KPI card highlights the metric; the full report is in the same payload («نمایش کامل» is client-side, no second query or charge).
- AC-M02: «جریان نقد آزاد کگل» for a bank returns a typed «برای بانک‌ها محاسبه نمی‌شود» explanation, not a number.
- AC-M03: «وبملت چقدر سود سهام نقدی پرداخت کرده؟» returns item 6 (76,072,909 for the fixture), never DPS.

**US-4 Analysis.** As a user, I want «آیا سود فلوله پشتوانه نقدی دارد؟» answered with facts (OCF, net profit, cash conversion) and no verdict.
- AC-A01: each analysis topic produces a fixed set of fact sentences from typed indicators; no LLM text; no arithmetic outside the backend.
- AC-A02: when net profit ≤ 0 or IS is missing, the fact names the reason and still shows OCF.

**US-5 Periods.** As an analyst, I want quarter-by-quarter, annual and explicit-period views.
- AC-P01: Qn = YTD(3n) − YTD(3n−3) within one fiscal year only; Q4 = 12M − 9M; missing predecessor → unavailable cell with reason.
- AC-P02: balances are never differenced; quarter opening/closing come from the YTD statements.
- AC-P03: «شش‌ماهه ۱۴۰۵» selects months 6, fiscal-year label 1405; a miss lists nearest periods.

**US-6 Integrity.** As the product owner, I want no unreliable report to look verified.
- AC-V01: R1 and R2 exact; a failure shows the error banner, the difference and no verified badge.
- AC-V02: FX rules FX-1…FX-4 each have a test; 124 is never adjusted.
- AC-V03: every displayed number has provenance (statement ids, line ids, source item ids, formula).

**US-7 Conversation.** As a user, I want follow-ups to keep the company, statement and period.
- AC-C01: the five follow-ups in the routing matrix §5 route as specified.
- AC-C02: all existing Feature 083/144/154 routing tests stay green; V1 is unchanged.

**US-8 Presentation.** As a user, I want the report readable on desktop, `/m` and in a PNG.
- AC-U01 … AC-U08 in the UX specification §9.

Non-functional: p95 use-case latency ≤ 800 ms on Development data (≤ 7 round trips); no new warnings; no new type errors; Persian RTL; accessible (keyboard, `aria-expanded`, table semantics); light/dark.

## 13. Risks

| Risk | Impact | Mitigation |
|---|---|---|
| Section placement of P items wrong | Misclassified detail rows | Only verified items displayed; residual row keeps totals exact; E-06 |
| Codal totals missing on older statements | Historical/quarterly gaps | Typed `codal_totals_missing`; E-03 decides fallback |
| Legacy presentations in history | Wrong OCF | `unsupported_presentation` via item 120 (E-04) |
| Item 128 («- جدید») absent or computed differently from the Codal totals on some statements | Gaps (`operating_total_missing`) or R1 failures | V3 and R1 make any difference visible; T01 measures 128 coverage and `128 + 121 = 122` across Development (E-03); OQ-087 |
| Item-1 `OPERATING_CASH_FLOW` elsewhere disagrees with Feature 161 | Two OCF numbers in the product | OQ-081; Feature 161 shows provenance |
| Routing regressions (quarterly shorthand, trend, «وضعیت», «جریان نقدینگی») | Wrong answers for existing questions | Flag; regression matrix; V2-only predicates; Feature 154/150 suites |
| Bank GroupId differs per environment | Wrong template | Existing startup check + E-10 per environment |
| Waterfall unreadable for banks (opening dominates) | Poor chart | «فقط جریان‌ها» toggle (UX §4.2); visual sign-off |
| Version extraction touches Feature 154 | Regression | Behavior-preserving extraction, Feature 154 suite as guard; alternative: leave 154 alone and accept duplication |

## 14. Open questions

OQ-074 … OQ-086 answered by the owner on 2026-11-14 (agent's proposals accepted). OQ-087 (item 128 not reported) is new and open for Gate B.

## 15. Proposed BUG candidates found during discovery (not fixed by this feature)

- «جریان نقدینگی» matches the cash-flow alias «جریان نقدی» in the shared V1/V2 rules (R-20a). Feature 161 guards its own V2 predicates; the shared rule is a separate bug-fix decision.
- «گزارش جریان نقدی فصلی X» opens the income-statement quarterly table (R-20). Closed for V2 by D-12 precedence when the flag is on; with the flag off it stays as today.
