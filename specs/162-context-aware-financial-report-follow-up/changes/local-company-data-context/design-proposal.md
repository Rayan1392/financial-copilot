# Change proposal — Local company data for every grounded analysis, and no "too large" refusal (Features 162 and 163)

Status: Gate A: Approved by the owner on 2026-11-22 (with amendments G1–G3, §8)

## Feature Folder   `162-context-aware-financial-report-follow-up/changes/local-company-data-context`
## Layers / Area    backend · AI, FinancialData, Billing
## Priority         to be set by the owner
## Complexity       L
## Depends On       162 change `sol-deep-analysis-web-search` (released v1.19.0, live since BUG-041 fix 2026-11-22), 163 (released), 154 (balance sheet, `[~]`), 161 (cash flow, `[~]`), 120 (monthly activity trend)
## References       `src/backend/FinancialCopilot.Application/AI/ReportDiscussion/` (`ReportDiscussionHandler`, `ReportDiscussionContextBuilder`, `ReportDataSetBuilder`, `GroundedAnalysis`, `NumericGroundingValidator`, `ExternalEvidenceValidator`), `src/backend/FinancialCopilot.Application/AI/SymbolGroundedFallback/`, `src/backend/FinancialCopilot.Application/AI/GroundedAnalysis/GroundedAnalysisOptions.cs`, `IFinancialStatementTableQueryUseCase`, `IBalanceSheetReportQueryUseCase`, `ICashFlowReportQueryUseCase`, `IMonthlyActivityTrendQueryUseCase`, `IProductRevenueMixQueryUseCase`, `specs/162-context-aware-financial-report-follow-up/changes/sol-deep-analysis-web-search/design-proposal.md`, `docs/grounded-analysis-operations.md`

## 1. Owner reports (2026-11-22)

1. «صورت سود و زیان اخشان را تفسیر کن» (Deep + web, 25 credits): the answer cites «منبع: گزارش فعالیت ماهانه
   اخشان» from the web for the monthly sales of آرد آزاد and شیر. Sapio already holds that monthly activity data. The
   owner asks: for an income-statement analysis or any fundamental analysis of one symbol, send the model the three
   financial statements and the monthly production and sales report (several periods, when the company files one) from
   local data, exactly the data that answers «صورت سود و زیان», «ترازنامه», «جریان وجوه نقد» and «روند تولید و فروش»,
   so the model does not search for them.
2. «صورت سود و زیان فاراک را تفسیر کن» returns «داده‌های صورت سود و زیان فصلی فاراک برای پاسخ یکجا بیش از حد بزرگ
   است…» (zero credits). The owner asks to remove this limit (BUG-042).

## 2. Discovery

| # | Finding | Where |
|---|---|---|
| F1 | The 162 analysis prompt carries **only the active report** (one `ReportFamily`: quarterly income statement, balance sheet or monthly sales). For an income statement, nothing about monthly sales, the balance sheet or cash flow reaches the model. | `ReportDiscussionHandler.AnalyzeAsync`, `ReportDataSetBuilder.Build` |
| F2 | 163 confirmed runs carry only the quarterly income statement (`ReadStatementAsync`). | `SymbolGroundedFallbackHandler` |
| F3 | The Deep + web prompt tells the model that web research "is part of a deep analysis" and lists "the company's own disclosures" as a search target; `WebRules` names "the company's disclosures on codal.ir" as the preferred source. With no monthly data in the prompt, searching codal for the monthly activity report is the expected behaviour. | `GroundedAnalysis.WebSearchUseRule`, `GroundedAnalysis.WebRules` |
| F4 | `MonthlyActivityTrendResponse` (the «روند تولید و فروش» answer) holds **company totals only**: latest month, previous month, same month last year, 12-month average, YTD, MoM/YoY growth and the monthly chart of the current and previous fiscal years. It has no products, quantities or rates. Product-level rows (product, sales amount, share, production quantity, sales quantity, rate) live in `ProductRevenueMixResponse` (one month per query) and `MonthlyProductTrendResult` (one product per query). The 25-credit answer above discussed آرد آزاد and شیر at product level. | `MonthlyActivityTrendQueryContracts.cs`, `ProductRevenueMixContracts.cs`, `MonthlyProductTrendContracts.cs` |
| F5 | A cash-flow `ReportFamily` does not exist (OQ-110 open, default "after Feature 161"). `CashFlowReport:Enabled` is `false` in `appsettings.json` and not set in `docker/appsettings.Production.json`; whether the server `.env` turns it on is not visible from the repository. | `ReportDiscussionContracts.cs`, `appsettings.json` |
| F6 | The "too large" refusal is `NarrowRequired`: `ReportDiscussionContextBuilder.Build` drops the oldest periods one at a time and returns null when even the newest period does not fit `InputTokenTarget` (Deep 10,000, Simple 6,000; validation 4,000–30,000). The estimate is `chars ÷ 2.5`. The analysis system prompt alone is about 10–11k characters (≈4,300 estimated tokens), the history cap is 1,200 tokens, and every fact line repeats its full period label. So the Simple profile leaves roughly 1,500 tokens for data; a long statement with many rows does not fit even one period. 163 has the same rule (`SymbolGroundedFallbackPromptBuilder.BuildAnalysis`). | `ReportDiscussionContextBuilder.cs`, `GroundedAnalysisOptions.cs`, `SymbolGroundedFallbackPromptBuilder.cs` |
| F7 | The real limit is the model's context window (`gpt-6.2-sol`, hundreds of thousands of tokens). At $2 per 1M input tokens, 30,000 input tokens cost about $0.06 per analysis; credits are fixed per tier (PO-6), so a larger input does not change any price. | sol-deep-analysis design §2 F8 |
| F8 | BUG-035: the financial lookups behind a turn must run sequentially on the request's DbContext. | `specs/159-.../changes/bug-035-analysis-search-failure/` |

Not verified yet (planning task): the exact cause for فاراک (which profile the user had, the estimated tokens and the
fact count). The zero-credit reply logs `ReportDiscussionDiagnostics` with `EstimatedInputTokens` and `FactCount`; the
production log line of that turn, or a Development run with فاراک's data, confirms F6 before the fix.

## 3. Proposed design

### D1 — Company data pack (one owner, Application layer)

A new use case `CompanyAnalysisDataPack` in `AI/GroundedAnalysis/` (port `ICompanyAnalysisDataSource`, implemented in
Infrastructure over the existing query use cases). For one company it loads, **sequentially** (F8), the same default
results the four chat answers produce:

| Section | Source (existing use case, default query of that answer) | Included when |
|---|---|---|
| Quarterly income statement | `IFinancialStatementTableQueryUseCase` with `BuildV2QuarterlyQuery` | always (the active report or 163's statement is reused, not read twice) |
| Balance sheet | `IBalanceSheetReportQueryUseCase` (Feature 154 default view) | outcome `Available` |
| Cash flow statement | `ICashFlowReportQueryUseCase` (Feature 161 default) | `CashFlowReport:Enabled` and outcome available |
| Monthly production and sales | `IMonthlyActivityTrendQueryUseCase` (Feature 120 default: current and previous fiscal year) | the company has monthly activity data (no industry rule: a company without monthly reports simply has no section) |
| Products of the latest months (see OQ-121) | `IProductRevenueMixQueryUseCase` for the latest N reported months | monthly section present and OQ-121 (b) |

- Each section is projected by `ReportDataSetBuilder` into the existing `ReportDataSet` shape (cash flow and product mix
  get new projections). Evidence ids carry a section prefix (`IS-F1`, `BS-F3`, `CF-C2`, `MS-F7`, `PM-F4`) so ids are
  unique across sections; `NumericGroundingValidator` checks the answer against the union of all sections.
- The section the user is looking at (the active report in 162) comes first and is marked `shown="true"`.
- Each read has its own timeout (proposal: 3 s, configurable) and an overall budget (proposal: 8 s). A section that
  fails, times out or is empty is left out and listed in the prompt as `not_available` with its reason; the turn never
  fails because a supporting section is missing. Telemetry: section counts, sizes, read latency and omissions (no
  values).
- No migration, no new table, no new payload field except an optional `SectionsUsed` list on the stored run for
  diagnosis.

### D2 — Where the pack is used

- **162 analysis turns** (any active family) after the routing step decides `Answered`. The routing step itself is
  unchanged in this change (hand-backs still never load the pack or reach the analysis model, PO-14).
- **163 confirmed runs**. The offer step is unchanged (no model call, no reservation; it still requires a quarterly
  income statement).
- Not used: the legacy single-call path (`GroundedAnalysis:Enabled=false`), which stays as rollback.
- Billing unchanged: the four fixed tiers (PO-6). A larger input never changes credits.

### D3 — Prompt: Sapio data first, web only for outside context

- `<report>` becomes `<company_data>` with one `<section>` per pack entry (identity, scope, periods, facts,
  calculations) plus the `not_available` list.
- New rule in every analysis prompt: "`<company_data>` is Sapio's own copy of this company's financial statements and
  monthly activity reports. Never search the web for this company's financial statements, monthly activity, production
  or sales figures, or any figure that `<company_data>` holds or lists as not available. Use the web only for outside
  context: industry and commodity prices, regulation, supply and demand, and material events."
- `WebSearchUseRule(Deep)` drops "the company's own disclosures" from its search list; `WebRules` keeps codal.ir only
  for material-event disclosures, not for financial or monthly figures.
- Server guard (see OQ-122): `ExternalEvidenceValidator` removes an external item whose source is on codal.ir and which
  carries a number, because every numeric codal figure Sapio cites (statements, monthly activity) is in its own data.
  The removed count is logged; the answer is kept when the rest is grounded.
- `MissingInReport` in the analysis step means "not in `<company_data>`".

### D4 — No "too large" refusal (BUG-042)

- The analysis paths (162 and 163) never answer `NarrowRequired` for size. Budgeting becomes a degradation order instead
  of a refusal:
  1. full pack;
  2. drop older periods of the **supporting** sections (keep at least the newest two periods / 12 months each);
  3. drop the recent-messages history beyond the last two messages;
  4. drop supporting sections whole, in reverse priority (products, cash flow, balance sheet, monthly);
  5. drop older periods of the shown section down to its newest period.
- Prompt size: remove the repeated period label from each fact line (one period header per column group) and keep the
  numbers unchanged, so the same data needs far fewer tokens.
- Technical budgets (PO-8, not credits): Deep `InputTokenTarget` 10,000 → 40,000 and Simple 6,000 → 24,000, with the
  validation upper bound raised from 30,000 to 120,000 (see OQ-123). This is the soft target of the degradation order.
- Only a hard ceiling remains: if even system prompt + newest period of the shown section exceeds the model's configured
  context limit, the existing zero-credit reply is kept as an impossible-in-practice safety net, logged as an error.
- The legacy path keeps its 4,000–6,000 bounds and its refusal (out of scope, rollback only).

## 4. Out of scope

- The routing step's decision rules (whether «جریان نقدش چطوره؟» after an income statement is answered from the pack
  instead of showing the cash-flow report). Possible follow-up for `specs/backlog.md`.
- A cash-flow `ReportFamily` (OQ-110 stays as is; the cash-flow statement only enters as a supporting section).
- Industry or peer data, market prices, valuation data.
- Prices, plans and tiers; the Telegram and web UI (no UI change: the answer only gets better grounded).

## 5. Risks and trade-offs

| Risk | Mitigation |
|---|---|
| Up to four extra DB reads per analysis (latency) | Only after `Answered`; per-read and overall timeouts; reads are indexed local queries; telemetry on latency. |
| Larger inputs raise OpenAI cost (≈$0.02–0.08 per analysis) | Fixed credits are unchanged by design (PO-6/PO-7); cost telemetry and `CostAlertUsd` already exist. |
| More data can blur the focus of a narrow question | The shown section is first and marked; the prompt keeps "choose only what matters". |
| Grounding against more numbers accepts a coincidental match | Same validator and tolerance as today; ids are section-prefixed so usedEvidenceIds stay traceable. |
| Cash-flow flag off in production → the section is silently absent | Listed as `not_available: disabled`, logged; F5 to be checked on the server before release. |
| Removing codal numeric citations drops a useful material-event figure | Only numeric codal items; text-only event citations stay (OQ-122 lets the owner choose prompt-only). |

## 6. Verification (for the Gate B plan)

- Unit: pack assembly per section availability, sequential reads, timeouts and omissions; section-prefixed ids; the
  union grounding; degradation order with a synthetic oversized statement (فاراک-sized) never returning
  `NarrowRequired`; prompt contains the no-search rule and no "company's own disclosures" search target; codal numeric
  guard.
- Architecture: Application does not reference Infrastructure; the pack reads only through the port.
- Integration (deterministic fake model, no live OpenAI call): 162 income-statement analysis for a manufacturing
  fixture sends four sections; a non-manufacturing fixture sends no monthly section; 163 confirmed run sends the pack;
  billing still settles the fixed tier.
- Owner-run live checks (no live LLM calls by the agent): اخشان Deep + web shows no web citation for monthly or
  statement figures; فاراک income-statement analysis answers.

## 7. Open questions (also in `specs/open-questions.md`)

| ID | Question | Proposal |
|---|---|---|
| OQ-121 | Monthly section: only the «روند تولید و فروش» totals, or also product rows (product, sales amount, share, production and sales quantity, rate)? | (b) totals **plus** product rows of the latest 3 reported months and the same month last year (the اخشان answer needed آرد آزاد/شیر rate vs quantity); (a) totals only, exactly the «روند تولید و فروش» answer. |
| OQ-122 | How strictly to stop web use for data Sapio has? | (b) prompt rule **plus** server removal of numeric codal.ir citations; (a) prompt rule only, with a telemetry counter. |
| OQ-123 | Input budgets for the analysis profiles | Deep 40,000 / Simple 24,000 estimated tokens, validation up to 120,000; or another value. |
| OQ-124 | Periods per supporting section before trimming | Income statement: the report default; balance sheet: the Feature 154 default view; cash flow: the Feature 161 default; monthly: current and previous fiscal year (24 months). |

## 8. Gate A record (owner, 2026-11-22)

Owner's words: «ریز اطلاعات تولید و فروش را ما در دیتابیس داریم (ولی فعلا برای این نسخه اشکالی نداره که از همان منبع
دیتا دریافت کنه) بنابراین ریز قیمت نهاده ها را از پرامپت حذف نکن چون دیتای خیلی مفیدی هست. با این اصلاحات با طرح Gate A
موافقم».

Amendments, as the agent understood them (the owner may correct them at Gate B):

| # | Amendment | Effect on the design |
|---|---|---|
| G1 | OQ-121 → (a). In this version the monthly section carries only the «روند تولید و فروش» totals. Product-level production and sales detail may still come from the web (as in the اخشان answer); a local product section is deferred (backlog). | D1: the «Products of the latest months» row is removed. |
| G2 | The web prompt keeps every search target it has today: raw-material and product prices, industry conditions, regulation and the company's own disclosures. `WebSearchUseRule` and `WebRules` are **not** shortened. | D3: the sentences removing "the company's own disclosures" and limiting codal.ir are withdrawn. Only the new rule is added: do not search for figures that `<company_data>` holds (the three statements and the monthly totals). |
| G3 | OQ-122 → (a): no server guard on codal.ir citations (it would also remove the product-level figures allowed by G1); prompt rule plus a telemetry counter of codal.ir citations. | D3: the `ExternalEvidenceValidator` guard is withdrawn. |

OQ-123 and OQ-124 are approved as proposed with the design.

Planning refinements (no design change):

- R1: the pack loader depends directly on the existing Application use-case interfaces
  (`IFinancialStatementTableQueryUseCase`, `IBalanceSheetReportQueryUseCase`, `ICashFlowReportQueryUseCase`,
  `IMonthlyActivityTrendQueryUseCase`); they already are policy-owned ports implemented outside, so no extra port is
  added.
- R2: evidence ids stay `F`/`C` with one counter across all sections (`F1…F240`) instead of section prefixes, so the
  existing evidence-id pattern of `NumericGroundingValidator` and the JSON contracts stay unchanged.
