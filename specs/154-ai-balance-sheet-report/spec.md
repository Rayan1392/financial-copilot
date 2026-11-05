# Feature 154 — AI Balance Sheet Report (ترازنامه / صورت وضعیت مالی)

**Status:** Revision 3, specs only (2026-11-04). Implementation is not authorized by this document.
- Every product decision D-01 to D-13 is closed (§13).
- What remains is data and source evidence, E-1 to E-8 (§14). Each gap affects only the behavior listed against it.

**Bounded context:** FinancialData reporting (read-only), exposed through AI Orchestration V2. Billing, Conversation and ingestion are reused unchanged.

**Companion documents:**
- [discovery.md](discovery.md): repository findings R-01 to R-49 and evidence gaps E-1 to E-8
- [design.md](design.md): technical and UX design
- [tasks.md](tasks.md): ordered tasks

**References:**
- [Feature 144](../144-quarterly-financial-reports-table/spec.md) and its [banking extension](../144-quarterly-financial-reports-table/banking/spec.md): the UI and typed-report reference
- [Feature 083](../083-ai-full-financial-statement-table-query/user-story.md): full statement table, preserved
- [Feature 153](../153-mobile-pwa-workspace/spec.md): mobile workspace

## 1. User story

As an analyst, investor, fund manager, or investment-company manager, I want to ask in natural Persian for a listed company's balance sheet. I want a curated standalone statement of financial position as of its latest reporting date, an on-demand comparison with a meaningful earlier date, and a funding-structure trend. With these I can judge how the company finances its assets and how that changed, without reconstructing figures myself.

## 2. Scope

**In scope**
- **Default report.** The latest eligible standalone `BalanceSheet` snapshot for one company, with:
  - curated summary and selected detail rows for the Banking or General template;
  - the highlighted equity-to-assets ratio;
  - reconciliation checks;
  - a «مقایسه» action.
- **Single-date report.** The same report anchored at one explicitly requested `PeriodEnd`, matched exactly.
- **Comparison view.** Two snapshots of the same company and scope, with one selected baseline:
  - the closing balance at the end of the previous fiscal year (the default);
  - the previous fiscal quarter-end («فصل قبل»);
  - the previous report («گزارش قبلی»);
  - the same stage of the previous fiscal year;
  - two explicit dates.

  The view opens from the «مقایسه» action, which uses the standard baselines already loaded in the response, or from a natural-language comparison request.
- **Funding-structure chart.** Up to five latest distinct snapshot dates.
- **Layouts and export.** Wide RTL layouts, mobile layouts, and PNG export for both views. Telegram gets a text rendering.
- **Routing.** The binding terminology in §4, with Feature 083 and the other existing routes preserved.

**Out of scope**
- Consolidated values. These are never substituted.
- Query-time provider calls, scraping, ingestion or synchronization changes, schema migrations, and catalog seeding. v1 never uses embedded comparatives. Preferring a stored, verifiably linked restated comparative is a post-v1 increment, gated by E-2.
- Comparisons of more than two dates, comparisons across companies, and comparisons across scopes.
- Standalone-quarter balance arithmetic, and aggregation of balances across dates.
- A single-value balance-sheet capability (R-27). V1 changes (R-02) and Feature 141 changes.
- Holding, investment and insurance templates. Inferring a company's industry.
- Telegram images, Excel and PDF export.
- Updating `specs/README.md` and `implementation-checklist.md`. These are part of release task T21.

## 3. Product decisions supplied by the Product Owner

These are business decisions, not repository findings. Revision 2 items are marked ², revision 3 items ³, and the post-implementation owner instruction of 2026-11-05 ⁴.

| ID | Decision |
|---|---|
| PD-01 | The report title is «ترازنامه (صورت وضعیت مالی)». |
| PD-02 ² | **Terminology (binding).** The **BalanceSheet** aliases are «ترازنامه», «صورت وضعیت مالی», «وضعیت مالی», «بیلان», «صورت دارایی‌ها و بدهی‌ها», Balance Sheet, and Statement of Financial Position. The **IncomeStatement** aliases are «صورت سود و زیان», «گزارش سود و زیان», «صورت درآمد و هزینه», «صورت عملکرد مالی», «عملکرد مالی», Income Statement, Profit and Loss Statement (P&L), Statement of Profit or Loss, and Statement of Operations. In a report request, «عملکرد مالی X» and «صورت عملکرد مالی X» mean the income statement, even after a balance sheet in the same conversation. «وضعیت مالی X» and «صورت وضعیت مالی X» mean the balance sheet. The user is never asked to choose the statement type for these phrases. Bare «عملکرد» and bare «وضعیت» keep their existing behavior. **This closes D-02.** |
| PD-03 ² | The alias fixes the statement type. The whole request decides the form: report, comparison, analysis, single item, or complete statement. Semantic intent, existing normalization, company resolution and conversation context are reused. |
| PD-04 ² | Ordinary and latest standalone requests go to Feature 154. So do explicit comparisons of standalone balance sheets. Feature 083 is kept for full-table requests and its supported variant filters (consolidated, audited-only, restated, period length). The two dates of a comparison request are comparison operands and never trigger the Feature 083 single-statement route. Analysis, single-value, cash-flow and income-statement behavior is preserved. **This closes D-01.** |
| PD-05 ² | «گزارش ترازنامه فصلی فولاد» is a balance-sheet request. «فصلی» never triggers income-statement routing or standalone-quarter arithmetic. V1 stays frozen. |
| PD-06 ² | A balance-sheet amount is a reported balance as of `PeriodEnd`, and `PeriodType` is metadata. Balances are never subtracted to make a standalone quarter, and never aggregated across dates. A difference is never labelled «فصل دوم», «عملکرد فصل» or «مانده فصل». A difference appears only as an explicitly labelled comparison result. |
| PD-07 | Read the existing `FinancialStatements`, `FinancialStatementLineItems` and `FinancialStatementSourceItems` tables. Reuse the `NoavaranCurrentApi` provider, the unit metadata and the company-resolution path. وبملت's blank `CompanyId` is valid through `ExternalCompanyId` 452. |
| PD-08 | Filter to `StatementType = BalanceSheet` and `IsComposing = false` before selecting. The default anchor is the latest eligible `PeriodEnd`. |
| PD-09 ² | **Same-`PeriodEnd` versions.** Rank by audit status, then restatement status, then publication date, as in Feature 144. Equally preferred versions that disagree on mapped values produce a conflict outcome. Equivalent records are separated by a stable identifier. A material conflict is never hidden by sorting on an ID, and an older date is never selected silently. **This closes D-05.** |
| PD-10 | **Metadata shown.** Jalali snapshot date, fiscal-period length, standalone scope, audit status, restatement status, and publication date, each only when verified and available. Fixture amounts are in million IRR. |
| PD-11 ² | **Comparison modes and rules** are as defined in §8: the baselines, the source-integrity rules, the change rules, and the labels. **This closes D-07.** |
| PD-12 ² | **Rows.** Rows come from one structure with two row sets.<br>• Bank status follows Feature 144's classification and is never inferred.<br>• «سرمایه» uses item **57**. Item 188 is a different concept and never replaces it. **This closes D-04.**<br>• The WebMellat fixture contains subtotals 135, 137, 139 and 140, so banks show the reported subtotals (D-06, bank part).<br>• Items 286, **60** and 146 stay out of the curated summary. **This closes D-11.**<br>• Visible items are never summed to reconstruct totals. |
| PD-13 ² | **Alias policy.**<br>• Primaries are 135, 137, 138, 139, 140, 143, 147 and 148. Their «- کدال» counterparts 298–305 are fallback candidates for each pair.<br>• Item 306 is primary for receivables and 307 for payables, each with its source label. Items 293 and 308 are separate fallback candidates.<br>• A fallback is enabled only after semantic equivalence is verified; matching sample values are not enough.<br>• For a verified alias conflict, the preferred value is kept and the discrepancy and provenance are shown.<br>• An unverified fallback never blocks a verified primary.<br>**This closes D-03 as policy.** Gap E-1 remains. |
| PD-14 | **Ratio row** «نسبت حقوق صاحبان سهام به کل دارایی‌ها».<br>• Bold yellow.<br>• Reported total equity / reported total assets × 100, from the same snapshot and version.<br>• Two decimals.<br>• «—» when an input is missing or assets are zero.<br>• Zero and negative values are preserved. |
| PD-15 ² | **Reconciliation.** Reported assets are checked against reported total liabilities and equity, and against canonical total liabilities plus total equity. The two checks stay separate. Any exact discrepancy is shown and no value is altered. **This closes D-10** (exact equality). |
| PD-16 ² | **Layouts** are as defined in §9: the wide default, the wide two-sided comparison (supplied amendment), mobile default, and mobile comparison. |
| PD-17 ² | **Chart.**<br>• Up to five latest distinct eligible standalone dates in chronological order. Distinct dates are selected before versions are ranked.<br>• It sits above the table on wide desktop and in the PNG, and below the table on mobile.<br>**This closes D-08 and D-09.** |
| PD-18 ² | **PNG.** The Feature 144 export path is reused.<br>• A single-date PNG uses the wide two-sided layout.<br>• A comparison PNG uses the wide two-sided comparison layout, with both dates, the baseline label, percentage changes and the yellow ratio row. |
| PD-19 ² | The fixtures in §11.1 include item 57 values. The earlier WebMellat statement 403733 has a verified identity and date but no supplied amounts. Synthetic values are allowed when they are clearly labelled. |
| PD-20 ³ | **Single date.** An ordinary request for a standalone balance sheet at one explicit date goes to Feature 154 and must match that exact `PeriodEnd`. If no such snapshot exists, the no-data response names the requested date. Feature 083 keeps only the full-table and variant requests it actually supports (R-44). **This closes D-12.** |
| PD-21 ³ | **«عملکرد مالی».** In report requests, «عملکرد مالی» and «صورت عملکرد مالی» follow exactly the route of «صورت سود و زیان», which today is Feature 144 (R-46). Ordinary balance-sheet requests using «وضعیت مالی» or «صورت وضعیت مالی» go to Feature 154. Explicit analysis and other established intents are preserved. **This closes D-13.** |
| PD-22 ³ | **Subgroups.** A General row goes into a current or non-current subgroup only when its classification is verified. A row whose only known placement is its parent section is shown directly under assets or under liabilities. The rows still needing evidence are listed in E-8. **This closes D-06.** |
| PD-23 ³ | **Two different baselines.** «فصل قبل» is the issuer's previous **fiscal quarter-end**. «گزارش قبلی» is the nearest earlier eligible `PeriodEnd`. If the fiscal quarter-end cannot be verified, or no snapshot exists at it, the response names that baseline as unavailable and offers the previous available report with its actual date. A quarterly balance is never derived by subtraction. |
| PD-24 ³ | **Standard baselines in the first response.** The initial typed report includes the available standard baselines, loaded by a bounded batched read. Opening «مقایسه» from that response uses the loaded data and adds no charge. A custom date outside the response goes through the existing request path. |
| PD-25 ³ | **Layout values.** The initial design values are a 1100 px report-container width for the two-sided comparison and a 1600 px comparison PNG. Both are validated with long Persian labels and WebMellat amounts. The supplied two-sided screenshot is the layout reference. The Feature 154 chart is shown by default, and Feature 144's existing `ShowChart` behavior is preserved. |
| PD-26 ³ | **Evidence scoping.** Each gap disables only the behavior it affects:<br>• an unverified alias disables only its fallback;<br>• v1 uses the exact-date, separately stored baseline, with its restatement status labelled when known, and claims a link to the anchor report only when verified;<br>• missing fiscal evidence makes only the baselines that depend on it unavailable;<br>• a consolidated report is suggested only when its existence is verified. |
| PD-27 ⁴ | **Review requests and starter cards** (Product Owner instruction, 2026-11-05). In V2 only:<br>• «بررسی» or «بررسی کن» together with a balance-sheet alias opens the Feature 154 report, for example «صورت وضعیت مالی فاذر را بررسی کن». This overrides the earlier "explicit analysis preserved" rule for «بررسی» with a balance-sheet alias. An explicit «تحلیل» («تحلیل ترازنامه X») still goes to Feature 081 analysis.<br>• «صورت وضعیت» is a balance-sheet alias, unless it heads another statement name (for example «صورت وضعیت جریان وجوه نقد …»). Bare «وضعیت X» keeps its existing route.<br>• «ترازنامه X را بده» and «ترازنامه X؟» open the default report.<br>• An income-statement alias together with «فصلی» and «بررسی کن», without «تحلیل», opens the Feature 144 quarterly report. So the starter card renamed from «گزارش سود و زیان فصلی فولاد را بررسی کن» to «عملکرد مالی فصلی فولاد را بررسی کن» behaves exactly like the old card. «تحلیل صورت سود و زیان X» stays on analysis.<br>• A review that names more than one statement («بررسی ترازنامه و سود و زیان X») keeps the analysis route.<br>• All of these are recognized deterministically on both V2 paths, without the LLM proposal. V1 is unchanged. |

## 4. Terminology and routing

V2 serves the report on both the semantic path and the deterministic path (R-23). V1 and Feature 141 are unchanged.

### 4.1 Statement type

- An alias from PD-02 fixes the statement type.
  - Spacing, ZWNJ, «ی/ي», «ک/ك» and «ه/ۀ» variants are matched through the existing `NormalizeText`.
  - English aliases are matched case-insensitively.
- The new aliases «وضعیت مالی», «بیلان», "balance sheet" and "statement of financial position" (BalanceSheet) and «عملکرد مالی», "P&L", "statement of profit or loss" and "statement of operations" (IncomeStatement) are added to the single alias owner, `FinancialStatementTableIntentRules` (R-45).
- An explicit alias overrides any statement type carried in conversation context.
- Bare «عملکرد» and bare «وضعیت» (without «مالی») keep their current behavior (baseline T02).
- PD-27 (V2 only): «صورت وضعیت» is also a balance-sheet alias, except when it heads another statement name such as «صورت وضعیت جریان وجوه نقد». Bare «وضعیت X» is unchanged.
- Balance-sheet aliases are never extracted as company hints. Neither are «عملکرد مالی» or a possessive form such as «‌اش» (R-26).

### 4.2 Form of the request (BalanceSheet)

| Request form | Example | Route |
|---|---|---|
| Ordinary or latest report | «ترازنامه وبملت», «آخرین ترازنامه فاذر», «وضعیت مالی وبملت», «بیلان شگل», «گزارش ترازنامه فصلی فولاد», «ترازنامه فاذر را بده», «ترازنامه فاذر؟», «صورت وضعیت فاذر» (PD-27) | Feature 154 default report |
| Review (PD-27) | «صورت وضعیت مالی فاذر را بررسی کن», «ترازنامه وبملت را بررسی کن», «بررسی وضعیت مالی وبملت»: «بررسی» with exactly one statement (a balance-sheet alias) and no other analysis cue | Feature 154 default report (with any comparison cue or date, the matching Feature 154 view) |
| Comparison | «ترازنامه وبملت را با پایان سال مالی قبل مقایسه کن», «مقایسه ترازنامه وبملت», «ترازنامه وبملت نسبت به گزارش قبلی», «ترازنامه وبملت در مقایسه با مدت مشابه سال قبل», «ترازنامه وبملت ۱۴۰۵/۰۶/۳۱ و ۱۴۰۵/۰۳/۳۱ را مقایسه کن» | Feature 154 comparison view (§8) |
| Full table | «همه اقلام ترازنامه وبملت», «جدول کامل ترازنامه شگل» | Feature 083 (unchanged) |
| Explicit variant filter | «ترازنامه تلفیقی وبملت», «ترازنامه حسابرسی‌شده فاذر», «ترازنامه تجدید ارائه‌شده شگل», «ترازنامه ۱۲ ماهه شگل» | Feature 083 (unchanged) |
| Single specified date, without a comparison | «ترازنامه وبملت در ۱۴۰۵/۰۳/۳۱», «ترازنامه ۱۴۰۴/۱۲/۲۹ فاذر» | Feature 154 single-date report. The `PeriodEnd` must match exactly; otherwise `no_snapshot_at_date` names the date (PD-20). |
| Single date with a Feature 083-only filter | «ترازنامه تلفیقی وبملت ۱۴۰۵/۰۳/۳۱», «همه اقلام ترازنامه وبملت در ۱۴۰۵/۰۳/۳۱» | Neither path supports it: Feature 083 has no date filter and Feature 154 is standalone and curated. Feature 154 returns typed `unsupported_date_with_variant`, naming the date and the filter. It never drops the date silently. |
| Analysis | «تحلیل ترازنامه وبملت», «ترازنامه وبملت را بررسی و تحلیل کن», «بررسی ترازنامه و سود و زیان وبملت» (two statements) | Feature 081 (unchanged) |
| Single item | «جمع دارایی‌های شگل چقدر است؟», «سرمایه فاذر چند است؟» | Unchanged (R-27) |

Comparison cues and baseline mapping (§8.2):

| Cue | Baseline |
|---|---|
| «مقایسه» or «تغییرات», with no stated baseline | Fiscal-year end |
| «پایان سال مالی قبل», «پایان سال قبل» | Fiscal-year end |
| «فصل قبل», «پایان فصل قبل» | Previous fiscal quarter-end, labelled «پایان فصل قبل» with its date (PD-23) |
| «گزارش قبلی», «گزارش قبل», «دوره قبل» | Previous report, labelled «گزارش قبلی» with its actual date |
| «مدت مشابه سال قبل», «دوره مشابه سال قبل» | Same stage of the previous fiscal year |
| Two dates | Explicit dates |

A comparison cue combined with «فصلی» or «فصل» never triggers the Feature 144 path (PD-05).

### 4.3 Income statement and other preserved routes

- «عملکرد مالی X» and «صورت عملکرد مالی X» resolve to IncomeStatement, including directly after a balance-sheet report (PD-02).
  - They follow exactly the route of «صورت سود و زیان X» (PD-21): the Feature 144 report for ordinary report requests, and the same analysis handling when an analysis cue such as «تحلیل» is present.
  - «صورت عملکرد X» and «صورت درآمد X» without «مالی» keep their current Feature 083 route (R-46).
- PD-27: an income-statement alias with «فصلی» and «بررسی کن», and without «تحلیل», opens the Feature 144 quarterly report exactly like «گزارش سود و زیان فصلی X را بررسی کن» does. Example: «عملکرد مالی فصلی فولاد را بررسی کن». The shorthand «عملکرد فصلی X را بررسی کن» is the same request (owner instruction 2026-11-05). On the V2 quarterly path only, and only when the message names no statement of its own, «عملکرد فصلی» reads as «صورت سود و زیان فصلی». «تحلیل صورت سود و زیان X», «تحلیل عملکرد مالی فصلی X» and «صورت سود و زیان X را بررسی کن» (no quarter cue) keep their routes.
- Explicit income-statement, quarterly, cash-flow, analysis, disclosure-listing, Feature 131/132 and single-value requests keep their baseline behavior.

### 4.4 Follow-ups

Feature 120 slot carry-over applies (R-28):
- After a balance-sheet report for X, «عملکرد مالی‌اش» opens the income statement for X.
- After an income-statement report for X, «وضعیت مالی‌اش را نشان بده» and «ترازنامه‌اش» open this report for X.
- «همین را برای شگل» repeats the active report type for شگل.
- «با گزارش قبلی مقایسه کن» after a default report opens the comparison for the same company.

### 4.5 Orchestration paths

| Path | Feature 154 behavior |
|---|---|
| V2 semantic route (`financial_statement_table`, SemanticPrimary) | Runs the balance-sheet report use case for both the default and comparison forms. |
| V2 deterministic branch | Uses the same recognition and the same use case, so the results are identical. |
| V1 | No change. Under emergency rollback, balance-sheet requests receive Feature 083. |
| Feature 141 | Not used. |
| `ChatProvider = CyclicalWaves` | The existing provider-unavailable message (R-15). |
| Telegram | Text rendering (§9.6). |

## 5. Data source, snapshot semantics and selection

1. **Provider and link.** The provider is `NoavaranCurrentApi`, linked by `(ProviderName, ExternalCompanyId)` of the provider-aware resolved company (R-17, R-18). A blank `CompanyId` is valid.
2. **Eligibility.**
   - `StatementType = BalanceSheet` and `IsComposing = false`.
   - The `WarningsJson` scope agrees with `IsComposing`.
   - `AssumedScale = MillionRials` (R-06, R-16).
   - Ineligible statements carry a typed reason.
3. **Distinct dates first.** Group eligible statements by `PeriodEnd`.
   - The default anchor is the newest date.
   - A single-date request anchors on the requested date only if an eligible snapshot has exactly that `PeriodEnd`.
   - Otherwise the outcome is `no_snapshot_at_date`. The localized message names the requested Jalali date and lists the nearest available earlier and later dates as options. It never moves to another date.
4. **Version per date (PD-09).**
   - Rank `IsAudited` descending, then `IsRepresented` descending, then `PublishedAt` descending.
   - If equally ranked versions differ on any mapped registry value, the date is `conflicting_report_revision`.
   - If they are identical on every mapped value, choose the version with the highest `ExternalStatementId`, then the highest `Id`, as the stable identifier.
   - A conflict at the anchor date fails the report with a typed outcome. It never falls back to an older date.
   - Risk: old rows may carry a default `false` flag (R-05).
5. **No eligible standalone statement.**
   - The outcome is `no_standalone_balance_sheet`, with a localized message naming the company and «ترازنامه جداگانه (غیرتلفیقی)».
   - A Feature 083 consolidated view may be suggested **only** if the query-time check confirms such a statement exists (E-6).
   - No consolidated value is shown.
6. **Snapshot semantics (PD-06).** `PeriodType` is shown as the report length («گزارش ۳/۶/۹/۱۲ ماهه»). It never enters arithmetic.
7. **Fiscal calendar.**
   - The fiscal-year end of a statement is its persisted `JalaliFiscalYearEnd` evidence (R-40).
   - The fiscal start is resolved by the Feature 144 rule, including transitional-year rebasing (R-41).
   - `PeriodStart` is never treated as verified issuer metadata.
8. **Metadata (PD-10).**
   - Company name and symbol, and the Jalali `PeriodEnd`.
   - Report length, and «حسابرسی‌شده» or «حسابرسی‌نشده». The standalone basis is not labelled; only a consolidated statement shows «تلفیقی» (owner instruction 2026-11-05).
   - «تجدید ارائه‌شده» when restated; otherwise nothing.
   - Jalali `PublishedAt` when present, and «میلیون ریال».
   - Absent metadata is omitted.

## 6. Financial presentation

### 6.1 Template

- The banking template applies only when the resolved company has exactly one eligible `NoavaranEligibleCompanies.GroupId` that equals `QuarterlyFinancialReports:BankingIndustryGroupId` (R-19). This configuration has one owner, shared with Feature 144.
- Any other known group gives the General template. Missing or contradictory evidence gives `company_classification_unavailable`.
- Bank status is never inferred from the symbol, name or line items.
- The GroupId differs between environments, so each environment needs its own check (R-20, E-5).
- The template IDs are `BankingBalanceSheet` and `GeneralBalanceSheet`.

### 6.2 Row rules

- Rows resolve by `(NoavaranCurrentApi, BalanceSheet, SourceItemId)` through the catalog. A blank `MetricCode` is valid (R-07, R-09).
- **Common rows** always render, and show «—» when absent. **Template additions** render only when observed. A reported zero counts as observed.
- Duplicate observations for one item in one statement give `ambiguous`, which shows «—» with a reason.
- **Excluded items:**
  - 286 (period metadata);
  - 60 (share premium);
  - 146 (reserves; equal to 147 in فاذر but a distinct concept);
  - 160;
  - 188, which never substitutes for 57;
  - any unregistered item.
- Totals are read from their reported items and never computed.
- **Labels** are the catalog source titles (R-09), with spacing and ZWNJ normalized and the «- کدال» suffix never shown.

### 6.3 Row registry

Sections: **A** = assets (right side), **L** = liabilities, **E** = equity (left side). Subgroups: **C** = current, **N** = non-current.

**Common rows**

| Row ID | Label | Sec / Sub | Primary | Fallback candidate |
|---|---|---|---|---|
| `cash` | موجودی نقد | A (section level; E-8) | 1 | — |
| `current_assets_total` | جمع دارایی‌های جاری | A / C subtotal | 135 | 298 (E-1) |
| `non_current_assets_total` | جمع دارایی‌های غیرجاری | A / N subtotal | 137 | 299 (E-1) |
| `total_assets` | جمع کل دارایی‌ها | A final total | 138 | 300 (E-1) |
| `current_liabilities_total` | جمع بدهی‌های جاری | L / C subtotal | 139 | 301 (E-1) |
| `non_current_liabilities_total` | جمع بدهی‌های غیرجاری | L / N subtotal | 140 | 302 (E-1) |
| `total_liabilities` | جمع کل بدهی‌ها | L total | 143 | 303 (E-1) |
| `share_capital` | سرمایه | E | 57 | — (188 excluded) |
| `retained_earnings` | سود (زیان) انباشته | E | 71 | — |
| `total_equity` | جمع حقوق صاحبان سهام | E total | 147 | 304 (E-1) |
| `total_liabilities_and_equity` | جمع بدهی‌ها و حقوق صاحبان سهام | L+E final total | 148 | 305 (E-1) |

**Banking additions.** In the banking template, detail rows sit directly under their section, followed by the reported subtotals 135 and 137 (assets) and 139 and 140 (liabilities). The subtotals are not presented as sums of the listed details.

| Row ID | Label | Sec | Source |
|---|---|---|---|
| `bank_receivables_central_bank` | مطالبات از بانک مرکزی | A | 2 |
| `bank_receivables_banks` | مطالبات از بانک‌ها و موسسات اعتباری | A | 4 |
| `bank_loans_granted` | مجموع تسهیلات اعطایی | A | 291 |
| `bank_deposits` | مجموع سپرده‌های جذب‌شده | L | 223 |
| `bank_due_to_banks` | بدهی به بانک‌ها و موسسات اعتباری | L | 39 |
| `bank_treasury_shares` | سهام خزانه | E | 170, sign preserved |

**General additions (PD-22).** A row is placed in a subgroup only when its classification is verified.
- The catalog and the Feature 141 navigation hierarchy have no verified current/non-current parent for any balance-sheet item (R-12, R-48).
- The only verified basis available is a source label that itself states maturity. That covers 47 («کوتاه مدت») and 51 («بلند مدت»), and the reported subtotals 135, 137, 139 and 140 by their own definition.
- Every other General row is shown directly under its section. Its subgroup awaits E-8.

| Row ID | Label | Placement | Basis | Primary | Fallback candidate |
|---|---|---|---|---|---|
| `trade_receivables` | دریافتنی‌های تجاری و سایر دریافتنی‌ها | A (section) | E-8 | 306 | 293 (E-1) |
| `inventories` | موجودی مواد و کالا | A (section) | E-8 | 18 | — |
| `orders_prepayments` | سفارشات و پیش‌پرداخت‌ها | A (section) | E-8 | 19 | — |
| `ppe` | دارایی‌های ثابت مشهود | A (section) | E-8 | 21 | — |
| `trade_payables` | پرداختنی‌های تجاری و سایر پرداختنی‌ها | L (section) | E-8 | 307 | 308 (E-1) |
| `advances_received` | پیش‌دریافت‌ها | L (section) | E-8 | 45 | — |
| `short_term_borrowings` | تسهیلات مالی دریافتی کوتاه مدت | L / C | source label | 47 | — |
| `long_term_borrowings` | تسهیلات مالی دریافتی بلند مدت | L / N | source label | 51 | — |
| `capital_increase_funds` | وجوه ناشی از افزایش سرمایه | E | section only | 58 | — |
| `revaluation_surplus` | مازاد تجدید ارزیابی | E | section only | 64 | — |

`cash` (1) is also placed at section level in both templates until E-8 closes. The common-rows table above should be read with that placement.

**Order within a section:**
1. Section-level details.
2. The current subgroup: its verified details, then its reported subtotal.
3. The non-current subgroup: its verified details, then its reported subtotal.
4. The section total.

A subgroup with no verified details shows only its subtotal row. A subtotal is never presented as the sum of the rows shown.

**General analytical items.** These sit outside the hierarchy and are never summed: `net_working_capital` (145) and `interest_bearing_liabilities` (144). Each is labelled «(گزارش‌شده)».

**Derived row:** `equity_to_assets_ratio` (§7).

Each section's details carry the caption «اقلام منتخب».

### 6.4 Alias resolution (PD-13)

| Primary | Fallback | Fallback verified (E-1) | Result |
|---|---|---|---|
| present | absent | — | `available` (primary) |
| present | present, equal | any | `available` (primary); both recorded in provenance |
| present | present, different | no | `available` (primary); the unverified candidate is recorded only in provenance |
| present | present, different | yes | `available` (primary) with `aliasDiscrepancy`; a visible notice shows both values and the difference |
| absent | present | no | `missing` («—»); provenance notes the unverified candidate |
| absent | present | yes | `available` (fallback), labelled with the fallback's source title in provenance |
| absent | absent | — | `missing` («—») |

### 6.5 Value display and reconciliation

- **Zero** renders «۰». This intentionally differs from Feature 144's zero-as-dash, so zero stays distinct from «—».
- **Negative** values render in accounting parentheses with the Feature 144 negative tint and an accessible «منفی».
- **Missing** values render «—», labelled «داده موجود نیست».
- **Reconciliation (PD-15).** Two separate checks use exact decimal equality:
  - **(R1)** reported `total_assets` against reported `total_liabilities_and_equity`;
  - **(R2)** reported `total_assets` against canonical `total_liabilities` + `total_equity`.

  Each failing check shows its own notice with the exact difference, and no value changes. A check whose inputs are missing is `unavailable` and shows no notice.

## 7. Highlighted ratio row

- `total_equity / total_assets × 100`, from the same selected snapshot and version, at full decimal precision.
- **Display.** Two decimals, half away from zero, with Persian digits and «٪».
- **Unavailable cases.** «—» with reason `missing_total_equity`, `missing_total_assets` or `zero_total_assets`.
- **Edge values.** Zero equity gives «۰٫۰۰٪». A negative result is preserved.
- **Placement.** The row comes after the final totals and spans the full width on two-sided layouts.
- **Style.** The Feature 144 gross-margin tokens for each surface (R-34, R-35, R-37).
- **In comparison.** The row shows both dated ratios and their difference in **percentage points**, computed from unrounded ratios. For example, synthetic case S-1 shows «+۰٫۰۱ واحد درصد», whereas subtracting the rounded ratios would give 0.02.

## 8. Comparison

### 8.1 Model

- A comparison has one company and one scope (standalone). It has an **anchor** snapshot and a **baseline** snapshot.
  - The default report's «مقایسه» action anchors on the default snapshot.
  - With explicit dates, the later date is the anchor.
- Every baseline candidate is resolved deterministically in application logic.
- An unavailable baseline is never replaced by a different one without telling the user.

### 8.2 Baselines

| Kind | Label | Rule |
|---|---|---|
| `fiscal_year_end` (default) | «پایان سال مالی قبل» plus the date | The closing balance at the end of the fiscal year **immediately preceding** the anchor's fiscal year.<br>• The date is the latest `JalaliFiscalYearEnd` value strictly earlier than the anchor's, taken from the company's stored statements for the same provider (any statement type).<br>• The baseline snapshot is the eligible standalone balance sheet with `PeriodEnd` equal to that date.<br>• It is unavailable if fiscal evidence is missing (E-3) or no snapshot exists at that exact date.<br>• Never assume 29 Esfand. Never use the previous published snapshot or an opening balance instead. |
| `previous_fiscal_quarter_end` | «پایان فصل قبل» plus the date | The issuer's fiscal quarter-end immediately before the anchor.<br>• The quarter grid is built from the anchor's fiscal start (`FiscalCalendarPolicy`, R-41) as `JalaliEndAfterMonths(start, 3k)` for k = 1 to 4.<br>• If the anchor is the first quarter-end of its fiscal year, the previous quarter-end is the previous fiscal-year end, found as in `fiscal_year_end`.<br>• It is unavailable when the fiscal evidence is missing, the year is transitional or rebased, the anchor is not on the grid, or no snapshot exists at that exact date.<br>• When unavailable, the response names this baseline and its date (when the date is known), and offers `previous_report` with its actual date (PD-23).<br>• Quarterly balances are never derived by subtraction. |
| `previous_report` | «گزارش قبلی» plus the actual date | The latest eligible distinct `PeriodEnd` strictly before the anchor. It may be more than three months earlier, and it needs no fiscal evidence. |
| `same_stage_previous_year` | «مدت مشابه سال مالی قبل» plus the date | The previous fiscal year is identified as in `fiscal_year_end`, and its start is resolved by the Feature 144 fiscal-start rule (R-41).<br>• The candidate must have the same `PeriodType` as the anchor.<br>• Its `PeriodEnd` must equal that start advanced by the anchor's month count in Jalali months.<br>• The previous fiscal year must be a regular, non-rebased 12-month year.<br>• Matching by 365 days, or by Jalali month and day alone, is never allowed.<br>• If the calendar changed or there is no exact match, it is unavailable. |
| `explicit` | «تاریخ انتخابی» plus the date | Both requested dates must be eligible snapshot dates. A missing date is unavailable and is never moved to a nearby report. |

### 8.3 Baseline source integrity

1. **v1 source (PD-26).** The baseline is always the separately stored snapshot for the exact baseline date, chosen by §5.4.
   - It is labelled with its own audit flag, and with «تجدید ارائه‌شده» when its `IsRepresented` is true. When the restatement status is known, it is always labelled.
   - Provenance names its own statement (`ExternalStatementId` and `PublishedAt`).
2. **Link to the anchor report.** Wording that ties the baseline to the anchor report (for example «منتشرشده همراه گزارش …») appears **only** when E-2 verifies a deterministic link. In v1 there is no such wording.
   - Preferring an embedded restated comparative over the stored snapshot is a post-v1 increment, gated by E-2.
3. **One version per column.** All rows in a column come from **one** selected statement record. Rows from different versions are never mixed to fill a column.
4. **Unavailable baseline.**
   - The anchor stays visible and the baseline is marked unavailable with its reason.
   - The other baseline kinds that are available are listed as alternatives.
   - A conflict at the baseline date makes that baseline unavailable (`conflicting_report_revision`).
   - **Missing fiscal evidence** makes only `fiscal_year_end`, `previous_fiscal_quarter_end` and `same_stage_previous_year` unavailable. `previous_report`, explicit dates and the default report are unaffected (PD-26).

### 8.3a Standard baselines in the first response (PD-24)

- **What the response carries.** The initial typed report, for the default view or a single date, includes the standard candidates for its anchor:
  - `fiscal_year_end` (selected);
  - `previous_fiscal_quarter_end`;
  - `previous_report`;
  - `same_stage_previous_year`.

  Each candidate is either available, with its computed changes, or unavailable, with its reason.
- **How it is read.** One bounded batched read (design §5):
  - at most 6 database round trips per request;
  - line items for at most 9 distinct statements: the anchor, up to 4 baselines, and up to 5 chart dates, de-duplicated so a shared statement is read once.

  The payload holds each snapshot once, keyed by `statementId`; candidates and chart points reference it. A payload therefore carries at most 9 snapshots and 5 chart points.
- **Opening «مقایسه»** uses only this loaded data. It makes no AI request and no new Billing operation.
- **A custom date** not in the response (for example «با ۱۴۰۳/۱۲/۳۰ مقایسه کن») is a new natural-language request through the existing path and is billed normally.
- **Replay** restores the standard comparison, including its candidates and selection, without recomputation.

### 8.4 Change rules

- A change is computed only between the same registry row, in the same unit, when both cells are `available`.
- **Amount change** = anchor − baseline, as an exact decimal.
- **Percentage change** = amount change / baseline × 100, only when the baseline is greater than 0. A zero or negative baseline shows «—» for the percentage, and the amount change stays available.
- **Inputs not available.** If either cell is `missing`, `ambiguous` or unavailable, there is no change value; it shows «—» with a reason. Alias discrepancies on either side are preserved and shown.
- **Ratio row.** Percentage-point difference, computed before rounding.
- **Labels.**
  - The change column is labelled «درصد تغییر». It is never «… به فصل قبل».
  - No period or quarter wording is attached to differences (PD-06).
  - Direction may be shown with a glyph and color, but the color must not imply that an increase is good or a decrease is bad. Green and red are not used.

### 8.5 Column labels

- Each amount column is labelled «مانده در [تاریخ]», with its own audit and restatement badges.
- The selected baseline (kind and actual date) is stated above the table.

## 9. Presentation

All amounts are in million IRR.

### 9.1 Wide default (container at least 768 px)

- The metadata header comes first. On wide layouts the chart (§10) is placed above the table.
- The table is two-sided RTL:
  - assets on the right; liabilities and then equity on the left;
  - each side has its own «شرح» and «مبلغ» columns and its own row flow;
  - only the final totals align.
- Below the table come the full-width yellow ratio row, the reconciliation notices, and the analytical strip.
- The «مقایسه» action sits in the report header.
- Feature 144 conventions apply for typography, Persian digits, negatives, header style and metadata (R-34). This reverses Feature 083's assets-left placement for this report.

### 9.2 Wide comparison (supplied amendment)

- **Above the table:** the baseline selector and a line naming the selected baseline with its actual date. The chart sits above that, as in §9.1.
- **Two-sided table.** Assets are on the right and liabilities plus equity on the left. Each side has four RTL columns:
  1. «شرح»
  2. «مانده در [تاریخ گزارش]»
  3. «مانده در [تاریخ مبنا]»
  4. «درصد تغییر»
- **Each side** flows independently, and only the final totals align.
- **Absolute amount change** is available in each row's accessible details. It is exposed through the row's description and a keyboard- and pointer-reachable detail popover. A percentage that cannot be calculated shows «—».
- **Ratio row.** Below both final totals there is one full-width yellow row with both dated ratios and the percentage-point difference.
- **Narrow containers.** When the two-sided view does not fit, the sections stack vertically in the order assets, liabilities, equity. The four columns are kept and the ratio row stays last.
  - The initial design value for this switch is a 1100 px report container (PD-25).
  - It is validated, and adjusted if needed, with the longest registry labels and the widest وبملت amounts (T22).
- **Layout reference.** The supplied two-sided screenshot: assets right; liabilities and equity left; two dated balances and percent change on each side; aligned final totals; one full-width yellow ratio row. The screenshot was not received in this conversation, so visual review against it is pending (E-7).

### 9.3 Mobile default (`/m`, compact density, or container narrower than 768 px)

- **Table structure.** One RTL table with «شرح» on the right and «مانده» on the left, one date, and no horizontal scrolling.
- **Initial state.**
  - Collapsed section headers for assets, liabilities and equity, each showing its reported total.
  - Then the «جمع بدهی‌ها و حقوق صاحبان سهام» row.
  - Then the yellow ratio row. Both of these always stay visible.
- **Section headers.**
  - A tap anywhere on a header toggles it inline.
  - The header is a button with `aria-expanded`, `aria-controls` and a chevron state.
  - It responds to Enter and Space and is at least 44 px tall.
- **Text fitting.** Labels wrap and amounts never do. «۵۹٬۱۱۰٬۳۰۴٬۶۸۱» and «(۱۸۱٬۳۵۴٬۵۳۸)» fit at 320 px.
- **Chart and action.** The chart sits below the table, and the «مقایسه» action is in the header.

### 9.4 Mobile comparison

- The baseline selector sits above the table, with the selected baseline and its date.
- The expandable sections are kept.
- Each visible item shows:
  - the anchor balance on its primary line;
  - a secondary line «مانده در [تاریخ مبنا]: … · درصد تغییر: …».

  The absolute change is in the accessible details.
- The ratio row shows both ratios and the percentage-point difference.
- There is no horizontal scrolling at 320 px.

### 9.5 PNG

The Feature 144 canvas path is reused (R-37): brand header, footer «ترازنامه (صورت وضعیت مالی) | ساپیو - دستیار هوشمند بازار», Vazirmatn, at least 2× DPR.

- **Single-date PNG.** The wide two-sided layout with the chart above it.
- **Comparison PNG.** The wide two-sided comparison layout with the chart above it, both dates, the baseline label, percentage changes, and the full-width yellow ratio row.
  - The initial canvas width is 1600 px (PD-25). It is validated so the longest registry labels and the widest وبملت amounts fit without clipping. Labels may wrap to two lines.
- **Device independence.** Desktop and mobile export the same image for the same state.

### 9.6 Text (Telegram, history preview, accessibility)

- **Content.** A deterministic text rendering in the mobile order:
  1. metadata;
  2. sections and totals;
  3. the ratio;
  4. the notices;
  5. for comparisons, the baseline line, then each row's two balances and its percentage change.
- **Format.** No chart. The same «۰», «—» and parentheses rules. It fits within the 3,900-character split (R-30).

## 10. Chart: «روند ساختار تأمین مالی دارایی‌ها»

- **Dates.** Up to five latest distinct eligible standalone dates. Dates are chosen first, then versions are ranked per date (§5.4). A date with a version conflict appears as a gap with a note.
- **Series.**
  - Blue stacked bar: canonical `total_liabilities`.
  - Turquoise stacked bar: canonical `total_equity`.
  - Yellow line with point markers: the §7 ratio on a separate percent axis.
- **Shared policy.** The source policy is the same as the table's, so the anchor's chart values equal its table values.
- **Axes.** The amount axis is «میلیون ریال». The x-axis shows Jalali `PeriodEnd`, oldest on the left and newest on the right.
- **Tooltip and selected-date details.** Reported total assets, liabilities, equity and the ratio. The stack is described as «= جمع کل دارایی‌ها» only when check R2 reconciles **and** both stacked values are non-negative, so that the drawn stack really represents that sum.
- **Single snapshot.** One bar and a labelled ratio point, when the ratio is available.
- **Missing values.** A missing value draws no segment or point and is never shown as zero. The line does not bridge gaps.
- **Negative equity.** Sign-aware stacking: equity hangs below zero and liabilities rise above it. A note names the dates where the bar height is not total assets. The ratio point is negative.
- **Non-reconciling totals.** A warning marker plus the difference in the tooltip. Values are never adjusted.
- **Visibility and contrast.**
  - The Feature 154 chart is shown by default (PD-25). It is not controlled by `QuarterlyFinancialTable:ShowChart`. That setting keeps its existing effect on Feature 144: it gates the General income-statement chart, while the banking chart is always shown (R-37). No new configuration key is added in v1.
  - The colors meet 3:1 non-text contrast in light and dark themes.

## 11. Fixtures

### 11.1 Supplied by the Product Owner

All three are standalone, unaudited `BalanceSheet` snapshots at `PeriodEnd` 2026-09-22 (1405/06/31), in million IRR.

| | وبملت (Banking) | شگل (General) | فاذر (General) |
|---|---|---|---|
| ExternalCompanyId | 452 (CompanyId blank) | 167 | 290 |
| PeriodType | SixMonths | NineMonths | SixMonths |
| Statement | `cb18b9c3-2254-4f4f-82b7-97877c127db0`, Ext 409865, PublishedAt 2026-10-22 (1405/07/30); IS and CF exist for the same date; earlier standalone BS Ext 403733 at 2026-06-21 (1405/03/31), amounts not supplied | — | — |
| Cash (1) | 409,287,621 | 3,263,057 | — |
| Capital (57) | 3,382,000,000 | 6,149,600 | 18,126,475 |
| Additions | Loans 291: 45,209,234,799<br>Deposits 223: 27,590,264,477<br>Treasury 170: −181,354,538<br>Subtotals 135/137/139/140 present<br>(60 = 0, excluded) | Inventory 18: 10,266,826<br>ST borrowings 47: 21,347,261<br>LT borrowings 51: 0<br>Capital-increase funds 58: 10,850,400<br>Receivables 306: 26,549,068<br>Payables 307: 16,116,006 | PPE 21: 87,160,528<br>Advances 45: 17,452,463<br>LT borrowings 51: 0<br>Revaluation 64: 64,629,501<br>Receivables 306: 40,302,278<br>Payables 307: 29,966,070<br>(146 = 85,394,709, excluded) |
| Total assets (138) | 59,110,304,681 | 63,522,534 | 145,343,307 |
| Total liabilities (143) | 52,628,589,262 | 41,014,336 | 59,948,598 |
| Total equity (147) | 6,481,715,419 | 22,508,198 | 85,394,709 |
| Total L&E (148) | 59,110,304,681 | 63,522,534 | 145,343,307 |
| Ratio | **10.97%** | **35.43%** | **58.75%** |

Notes:
- The supplied values for 298–305, 293 and 308 equal their primaries. They are candidates only (PD-13).
- Group membership is test setup: a bank GroupId for 452, and non-bank GroupIds for 167 and 290.
- The exact arithmetic is in [discovery.md §7](discovery.md#7-fixture-arithmetic-computed-for-this-spec).

### 11.2 Synthetic comparison fixtures

These values are **synthetic**, clearly labelled, and not production data.

| ID | Purpose | Data |
|---|---|---|
| S-1 | وبملت anchor 409865 against the previous report 403733 (6-month against 3-month) | Synthetic 403733 values: assets 50,000,000,000; liabilities 44,523,000,000; equity 5,477,000,000; L&E 50,000,000,000. Expected changes: assets +9,110,304,681 (۱۸٫۲۲٪), liabilities +8,105,589,262 (۱۸٫۲۱٪), equity +1,004,715,419 (۱۸٫۳۴٪). Ratio difference **+0.01 pp**; subtracting the rounded ratios would give 0.02. |
| S-2 | شگل, non-Esfand issuer (fiscal-year end evidence 1405/09/30): default fiscal-year-end baseline 1404/09/30 | Assets 60,000,000; liabilities 40,000,000; equity 20,000,000. Expected changes +3,522,534 (۵٫۸۷٪), +1,014,336 (۲٫۵۴٪), +2,508,198 (۱۲٫۵۴٪). Ratio **+2.10 pp**. |
| S-3 | وبملت, Esfand issuer: fiscal-year-end baseline 1404/12/29 | Any synthetic values. The baseline date must come from fiscal evidence, not from previous-report logic. |
| S-4 | Zero and negative baselines | `retained_earnings` baseline −1,000 and anchor 500 give change +1,500 and «—» percent. Baseline 0 and anchor 200 give +200 and «—». |
| S-5 | Restated versus original at the baseline date | Two baseline-date records, one with `IsRepresented=true` and one with `false`. |
| S-6 | Fiscal-calendar change | The previous fiscal year is transitional (rebased). |
| S-7 | Missing exact date | An explicit request for 1405/04/31 with no snapshot on that date. |
| S-8 | Negative equity | Equity −5,000 and assets 100,000. |
| S-9 | «فصل قبل» differs from «گزارش قبلی» | Esfand issuer, anchor 9-month at 1405/09/30. No snapshot at the previous fiscal quarter-end 1405/06/31. Latest earlier snapshot at 1405/03/31. |
| S-10 | Single date | Requests for 1405/03/31 (exists: 403733) and 1405/04/31 (does not exist). |

## 12. Acceptance criteria

The **Gate** column names a decision (§13) or evidence gap (§14) that must close before the criterion can pass on live data. Criteria without a gate are testable now with the fixtures.

### Routing (AC-R)

| AC | Criterion | Gate |
|---|---|---|
| AC-R01 | These requests open the default report for the right company on both V2 paths: «ترازنامه وبملت», «گزارش ترازنامه بانک ملت», «صورت وضعیت مالی شگل», «وضعیت مالی وبملت», «بیلان فاذر», «صورت دارایی‌ها و بدهی‌ها شگل», «آخرین ترازنامه فاذر», "balance sheet وبملت" and "statement of financial position فاذر". | — |
| AC-R02 | Word-order, ZWNJ, «ي/ك», «ۀ» and colloquial variants route the same way, for example «وبملت ترازنامه» and «ترازنامه بانک ملت رو نشون بده». An ambiguous company gives the existing typed clarification. | — |
| AC-R03 | «عملکرد مالی وبملت», «صورت عملکرد مالی وبملت», «گزارش سود و زیان شگل», «صورت درآمد و هزینه فاذر», «P&L وبملت», "statement of profit or loss شگل" and "statement of operations فاذر" resolve to IncomeStatement. None of them opens this report or asks the user to choose a statement type. «عملکرد مالی وبملت» and «صورت عملکرد مالی وبملت» return the same capability, report and answer type as «صورت سود و زیان وبملت» (Feature 144). «تحلیل عملکرد مالی وبملت» behaves exactly like «تحلیل صورت سود و زیان وبملت». «صورت عملکرد وبملت» (without «مالی») matches the T02 baseline. | — |
| AC-R04 | After this report for وبملت, «عملکرد مالی‌اش» opens the income statement for وبملت. After an income-statement report for وبملت, «وضعیت مالی‌اش را نشان بده» opens this report for وبملت. «همین را برای شگل» repeats the active type for شگل. Aliases and possessive forms are never resolved as companies. | — |
| AC-R05 | Bare «عملکرد وبملت» and «وضعیت وبملت» behave exactly as in the T02 baseline. | — |
| AC-R06 | «گزارش ترازنامه فصلی فولاد» opens this report. It never opens the Feature 144 table, and no standalone-quarter value is produced. | — |
| AC-R07 | «صورت وضعیت مالی شگل» and «وضعیت مالی شگل» never route to comprehensive analysis. | — |
| AC-R08 | The comparison examples in §4.2, plus «ترازنامه وبملت را با فصل قبل مقایسه کن», open the comparison view with the mapped baseline. «فصل قبل» maps to `previous_fiscal_quarter_end` and «گزارش قبلی» to `previous_report`. None of them reaches Feature 083 or Feature 144, including «مقایسه ترازنامه فصلی وبملت». | — |
| AC-R09 | Full-table and variant-filter examples in §4.2 return the same Feature 083 capability and answer type as the baseline. | — |
| AC-R10 | «ترازنامه وبملت در ۱۴۰۵/۰۳/۳۱» opens the Feature 154 single-date report, not Feature 083. «ترازنامه تلفیقی وبملت ۱۴۰۵/۰۳/۳۱» returns `unsupported_date_with_variant`, naming the date and «تلفیقی». The date is never dropped. | — |
| AC-R11 | Analysis, single-value, cash-flow, explicit income-statement, quarterly, disclosure-listing and Feature 131/132 requests match the baseline. | — |
| AC-R12 | V1 is unchanged and Feature 141 is not invoked. `ChatProvider=CyclicalWaves` returns the existing provider-unavailable message. | — |
| AC-R13 ⁴ | PD-27: «صورت وضعیت مالی فاذر را بررسی کن», «صورت وضعیت فاذر», «ترازنامه فاذر را بده» and «ترازنامه فاذر؟» open the Feature 154 default report for فاذر (General template, latest standalone snapshot) on both V2 paths, deterministically and without the LLM proposal. «تحلیل ترازنامه فاذر» stays on Feature 081 analysis, bare «وضعیت فاذر» keeps its route, and «صورت وضعیت جریان وجوه نقد …» stays a cash-flow request. V1 is unchanged. | — |
| AC-R14 ⁴ | PD-27: the renamed starter card «عملکرد مالی فصلی فولاد را بررسی کن» returns the same capability and payload type as the old card «گزارش سود و زیان فصلی فولاد را بررسی کن», which is the Feature 144 quarterly report for فولاد, on both V2 paths. The shorthand «عملکرد فصلی X را بررسی کن» (for example «عملکرد فصلی فملی را بررسی کن») returns the same result as «عملکرد مالی فصلی X را بررسی کن». «تحلیل صورت سود و زیان فولاد» and «تحلیل عملکرد فصلی X» stay on analysis. | — |

### Selection and metadata (AC-S)

| AC | Criterion | Gate |
|---|---|---|
| AC-S01 | وبملت selects 409865 (`cb18b9c3-…`) through (`NoavaranCurrentApi`, 452), even though `CompanyId` is blank. | E-4 (live) |
| AC-S02 | Same-date IncomeStatement and CashFlow records do not affect selection. | — |
| AC-S03 | A synthetic consolidated BalanceSheet dated after 409865 does not displace it. No consolidated value appears. | — |
| AC-S04 | 403733 (1405/03/31) is not the anchor. | — |
| AC-S05 | A company with only consolidated, or no, BalanceSheet statements returns `no_standalone_balance_sheet` with the localized message and no amounts. A Feature 083 consolidated suggestion appears only when the check confirms a consolidated statement exists. Billing follows the existing no-data policy. | E-6 |
| AC-S06 | At one `PeriodEnd`: audited beats unaudited, then restated beats original, then the later publication date wins. Equally ranked versions that disagree on a mapped value give `conflicting_report_revision`, with no fallback to an older date. Equally ranked identical versions select the highest `ExternalStatementId`. | — |
| AC-S07 | All three fixtures show the date 1405/06/31 and report lengths «۶ ماهه», «۹ ماهه» and «۶ ماهه». Amounts are never annualized, subtracted, summed, or given a quarter label. | — |
| AC-S09 | Single date (S-10). 1405/03/31 anchors on 403733 by exact `PeriodEnd`. 1405/04/31 returns `no_snapshot_at_date` with «۱۴۰۵/۰۴/۳۱» in the message, and lists the nearest available earlier and later dates as options without opening them. | — |
| AC-S08 | The header shows name and symbol, the date, the report length, no basis label for the standalone statement («تلفیقی» only if consolidated), «حسابرسی‌نشده», a restatement label only when restated, the publication date 1405/07/30 for وبملت, and «میلیون ریال». Absent metadata is omitted. Statements with the wrong scale or disagreeing scope are ineligible, with a typed reason. | — |

### Rows and values (AC-M)

| AC | Criterion | Gate |
|---|---|---|
| AC-M01 | GroupId alone selects the template: `BankingBalanceSheet` for 452, and `GeneralBalanceSheet` for both 167 and 290, which share one structure. Missing or duplicate evidence gives `company_classification_unavailable`. Renaming the company changes nothing. | E-5 (per environment) |
| AC-M02 | Rows resolve by provider- and type-scoped SourceItemId. A blank `MetricCode` row renders. Balance-sheet item 143 is never confused with income-statement item 143. | — |
| AC-M03 | وبملت shows: cash 409,287,621; capital (57) 3,382,000,000; loans 45,209,234,799; deposits 27,590,264,477; treasury (181,354,538); the reported subtotals 135, 137, 139 and 140; and the four totals. No General row appears. | E-4 (live) |
| AC-M04 | شگل and فاذر use the General template with the §11.1 values, including capital 6,149,600 and 18,126,475, and receivables and payables from 306 and 307. No Banking row appears.<br>Placement follows PD-22: only 47 sits under the current-liabilities subgroup and only 51 under non-current liabilities. Rows 1, 306, 18, 19, 21, 307 and 45 appear directly under their section, before the subgroups. Subtotals 135, 137, 139 and 140 appear as reported subtotal rows. | E-4 (live); E-8 (future subgroup moves) |
| AC-M05 | A common row with no observation shows «—». An addition with no observation is not rendered. | — |
| AC-M06 | Long-term borrowings (51) = 0 shows «۰». Missing shows «—». Negatives show in parentheses. `0` and `null` stay distinct through serialization and replay. | — |
| AC-M07 | A synthetic statement whose details do not sum to the reported total still shows the reported total. | — |
| AC-M08 | These never appear as rows: 286; 60 (وبملت has 0); 146 (فاذر has 85,394,709 alongside equity 85,394,709 from 147); 160. When 57 is absent and 188 is present, capital shows «—». | — |
| AC-M09 | The alias matrix in §6.4 holds for every pair. Fixtures show exactly one row per concept, taken from the primary. An unverified fallback never blocks a primary. A missing primary with an unverified fallback shows «—». A verified conflict keeps the primary and shows the discrepancy notice with provenance. | E-1 (fallback rows) |
| AC-M10 | Items 144 and 145, when present, appear only in the analytical strip and are never added to a total. | — |
| AC-M11 | Each section's details carry «اقلام منتخب». | — |
| AC-M12 | Synthetic cases where reported 148 ≠ 138 (check R1), or 143 + 147 ≠ 138 (check R2), each show their own notice with the exact difference. Values are unchanged, and the two checks are never merged. | — |

### Ratio (AC-Q)

| AC | Criterion | Gate |
|---|---|---|
| AC-Q01 | The ratio displays «۱۰٫۹۷٪», «۳۵٫۴۳٪» and «۵۸٫۷۵٪» for the three fixtures. The typed values keep full precision, for example 10.965457637… for وبملت, from the same snapshot and version. | — |
| AC-Q02 | Missing equity or assets shows «—» with a reason. Zero assets shows «—». Zero equity shows «۰٫۰۰٪». Case S-8 gives −5.00 and renders as a negative. | — |
| AC-Q03 | The ratio row is bold yellow with the Feature 144 tokens on each surface. It sits after the final totals and is full width on two-sided layouts. | — |

### Comparison (AC-C)

| AC | Criterion | Gate |
|---|---|---|
| AC-C01 | The default and single-date responses contain the four standard candidates, each available or unavailable with a reason. «مقایسه» opens the comparison with `fiscal_year_end` selected, using only the loaded data: there is no AI request, no network call to the AI facade, and no Billing operation. A custom date not in the response is a new request through the existing path. | — |
| AC-C02 | Esfand issuer (S-3): for anchor 1405/06/31 with fiscal-year end 1405/12/29, the baseline is 1404/12/29, labelled «پایان سال مالی قبل (۱۴۰۴/۱۲/۲۹)». The date comes from fiscal evidence. | E-3 (live) |
| AC-C03 | Non-Esfand issuer (S-2): شگل's baseline is 1404/09/30, not 1404/12/29 and not the previous report. It shows the S-2 changes and +2.10 pp. | E-3 (live) |
| AC-C04 | When fiscal evidence is missing, the anchor stays visible. Only `fiscal_year_end`, `previous_fiscal_quarter_end` and `same_stage_previous_year` are unavailable, with reason `missing_fiscal_metadata`. `previous_report` remains available and is listed as the alternative. When no snapshot exists at the derived date, only that baseline is unavailable, and the response names its date. The 29 Esfand fallback is never used. | — |
| AC-C05 | Previous report (S-1): وبملت compares 1405/06/31 (6-month) with 1405/03/31 (3-month), labelled «گزارش قبلی (۱۴۰۵/۰۳/۳۱)». Both original balances and the S-1 changes are shown. No standalone-quarter amount or «فصل» label appears. A synthetic previous report six months earlier is still labelled «گزارش قبلی» with its date. | — |
| AC-C06 | Same stage of the previous fiscal year: a 6-month anchor ending 1405/06/31 (fiscal year starting 1405/01/01) matches a 6-month statement ending 1404/06/31. A 9-month شگل anchor ending 1405/06/31 matches a 9-month statement ending 1404/06/31 (previous fiscal year starting 1403/10/01). The transitional case (S-6) and a missing match are unavailable. Matching never uses 365 days or month and day alone. | E-3 (live) |
| AC-C07 | Explicit dates: two eligible dates compare with the later date as anchor, regardless of the order typed. A missing exact date (S-7) is unavailable, is not moved to a nearby date, and lists alternatives. Comparisons cover one company and standalone scope only. | — |
| AC-C08 | Restatement (S-5), v1. The baseline is the exact-date stored snapshot chosen by §5.4; with equal audit status the restated record wins. Its column carries «تجدید ارائه‌شده» exactly when its `IsRepresented` is true, and its provenance names its own statement. No text claims that it was published with the anchor report. Rows from different versions never share a column. | — (the anchor-link wording is post-v1 and gated by E-2) |
| AC-C09 | Amount change is exactly anchor − baseline. Percentage is calculated only when the baseline is greater than 0. In S-4, a zero or negative baseline gives the amount change and «—» percent. A missing row on either side gives no change. Alias discrepancies are preserved on both sides. | — |
| AC-C10 | The ratio row shows both dated ratios and the percentage-point difference from unrounded values: S-1 shows «+۰٫۰۱» and never 0.02. | — |
| AC-C11 | Amount columns are labelled «مانده در [تاریخ]», each with its own audit and restatement badges. The baseline kind and date are stated above the table. The change header is «درصد تغییر». Color never encodes favorability. | — |
| AC-C12 | Switching between available baselines uses values already computed on the server and does no arithmetic in the frontend. Replay restores the same candidates and selection. | — |
| AC-C13 | Previous fiscal quarter-end:<br>• وبملت 6-month anchor 1405/06/31 gives baseline 1405/03/31, labelled «پایان فصل قبل».<br>• A 3-month anchor gives the previous fiscal-year end.<br>• In S-9 the response says «پایان فصل قبل (۱۴۰۵/۰۶/۳۱)» is unavailable and offers «گزارش قبلی (۱۴۰۵/۰۳/۳۱)» without selecting it.<br>• A transitional year or missing evidence makes it unavailable and offers the previous report.<br>• No amount is ever computed by subtraction. | E-3 (live) |

### Presentation (AC-P)

| AC | Criterion | Gate |
|---|---|---|
| AC-P01 | Wide default at container width ≥ 768 px: assets on the right, liabilities and equity on the left, independent row heights, only the final totals aligned, and the ratio row spanning the full width. | — |
| AC-P02 | Typography, digits, negatives, header style and metadata follow Feature 144. | — |
| AC-P03 | Wide comparison at ≥ 1100 px: two sides, each with the four columns «شرح | مانده در [گزارش] | مانده در [مبنا] | درصد تغییر». The baseline and its date appear above. Only the final totals align. Absolute change is reachable by keyboard and pointer, and announced by screen readers. | E-7 (visual review) |
| AC-P04 | The comparison ratio row spans the full width below both totals, with both ratios and the percentage-point difference. Between 768 and 1099 px, sections stack vertically and keep all four columns. | E-7 |
| AC-P05 | Mobile default at 320, 360 and 390 px: «شرح | مانده», collapsed sections showing totals, then total L&E and the ratio, always visible, with no horizontal scroll. | — |
| AC-P06 | A section header toggles when tapped anywhere. It has `aria-expanded`, `aria-controls` and a visible state, responds to Enter and Space, and is at least 44 px tall. The total stays visible. | — |
| AC-P07 | At 320 px labels wrap, and «۵۹٬۱۱۰٬۳۰۴٬۶۸۱» and «(۱۸۱٬۳۵۴٬۵۳۸)» stay on one line without clipping. | — |
| AC-P08 | Mobile comparison: the selector is above the table and sections expand. Each visible item shows the anchor balance, then «مانده در [مبنا]» and the percentage change on a secondary line. There is no horizontal scroll at 320 px. | — |
| AC-P09 | The Feature 154 chart is shown by default: above the table on wide layouts and in the PNG, and below the table on mobile. It is shown with `QuarterlyFinancialTable:ShowChart` both true and false. With that setting false, Feature 144's General chart is still hidden and its banking chart still shown, exactly as before. | — |
| AC-P10 | Single-date PNG: the wide two-sided layout with chart, brand header and footer, and the full-width ratio row. «۰», «—» and negatives render as on screen. | — |
| AC-P11 | Comparison PNG: the wide two-sided comparison with both dates, the baseline label, percentage changes, the full-width ratio row with the percentage-point difference, and the chart. The longest labels and the widest وبملت amounts are not clipped. | E-7 |
| AC-P12 | Desktop and mobile export identical PNGs for the same state. | — |
| AC-P13 | Telegram receives the §9.6 text for the default report and for comparisons, with no image. | — |

### Chart (AC-G)

| AC | Criterion | Gate |
|---|---|---|
| AC-G01 | The title, blue and turquoise stack, yellow ratio line on its own axis, «میلیون ریال» unit and chronological Jalali axis are as specified. The chart shows at most five distinct dates. A date with three versions counts once and is selected before ranking. | — |
| AC-G02 | The anchor date's chart values equal the table's canonical liabilities, equity and ratio. | E-1 (fallback dates) |
| AC-G03 | The tooltip shows reported total assets. «= جمع کل دارایی‌ها» appears only when check R2 reconciles and both segments are non-negative. | — |
| AC-G04 | A single snapshot shows one bar and a labelled ratio point. | — |
| AC-G05 | Missing equity draws no segment and no point. The line does not bridge the gap and nothing is drawn as zero. | — |
| AC-G06 | Negative equity (S-8) hangs below zero with liabilities above it, plus a negative ratio point and the note. | — |
| AC-G07 | A non-reconciling date shows a warning marker and the difference, and values are unchanged. | — |
| AC-G08 | A date with a version conflict is a gap with an accessible note. | — |

### Cross-cutting (AC-X)

| AC | Criterion | Gate |
|---|---|---|
| AC-X01 | The typed result, including comparison candidates, changes, alias provenance, reconciliation and chart, serializes, persists and replays without loss. Replay does not recompute. Earlier Feature 083 and Feature 144 messages still replay. | — |
| AC-X02 | Existing authorization applies. There is one outer Billing operation. There are no provider calls at query time, no LLM arithmetic and no LLM-generated SQL. | — |
| AC-X03 | Feature 083 (including its balance-sheet variant paths), 081, 131/132, 141, 143 and 144 (both templates) behave as before, and the R-39 suites pass. | — |
| AC-X04 | An instrumented PostgreSQL test of the default report for وبملت, with all four standard baselines resolvable, uses at most 6 database round trips and reads line items for at most 9 distinct statements. The payload carries at most 9 snapshots, each stored once by `statementId`, and at most 5 chart points. After save and replay the standard comparison renders identically with no database read. | — |

## 13. Decision register

| ID | Status (2026-11-04) | Resolution or open question | Affects |
|---|---|---|---|
| D-01 | **Closed** (PD-04) | Feature 083 keeps full-table and variant-filter requests. Comparison dates never trigger 083. A consolidated suggestion appears only when its availability is verified. | AC-R08, AC-R09, AC-S05 |
| D-02 | **Closed** (PD-02) | «عملکرد مالی» and «صورت عملکرد مالی» mean IncomeStatement. «وضعیت مالی» and «صورت وضعیت مالی» mean BalanceSheet. The user is never asked. Bare «عملکرد» and «وضعیت» are unchanged. | AC-R03 to AC-R05 |
| D-03 | **Closed as policy** (PD-13); evidence E-1 open | Primaries and fallback candidates are as in §6.3. Fallbacks need verified equivalence. A verified conflict keeps the primary and shows the discrepancy. | AC-M09, AC-G02 |
| D-04 | **Closed** (PD-12) | Capital is item 57. Item 188 is never a replacement. | AC-M03, AC-M04, AC-M08 |
| D-05 | **Closed** (PD-09) | Ranking is audit, then restatement, then publication. A disagreement is a conflict; identical versions are separated by a stable ID. | AC-S06 |
| D-06 | **Closed** (PD-12, PD-22) | Banks show the reported subtotals. Labels are the source titles. A General row goes into a subgroup only when verified; in v1 only 47 and 51 qualify, by their source labels. Rows 1, 306, 18, 19, 21, 307 and 45 stay at section level until E-8 closes. | AC-M04 |
| D-07 | **Closed** (PD-11, PD-23, PD-24) | The two-date comparison with five baseline kinds is in scope, and «فصل قبل» is distinct from «گزارش قبلی». Standard baselines arrive in the first response. Tables with more than two dates are out of scope. | AC-C01 to AC-C13, AC-X04 |
| D-08 | **Closed** (PD-17) | The chart shows up to five latest distinct dates. | AC-G01 |
| D-09 | **Closed** (PD-17, PD-18, PD-25) | The chart is shown by default: above the table on wide layouts and in the PNG, and below it on mobile. Feature 144's `ShowChart` behavior is preserved. The initial design values are 1100 px and 1600 px, to be validated. | AC-P03, AC-P04, AC-P09 to AC-P11 |
| D-10 | **Closed** (PD-15) | Exact equality, with checks R1 and R2 kept separate. | AC-M12, AC-G07 |
| D-11 | **Closed** (PD-12) | Item 60 is excluded. | AC-M08 |
| D-12 | **Closed** (PD-20) | A single explicit date goes to Feature 154 with an exact `PeriodEnd` match. A missing date is named in the no-data response. A date combined with a Feature 083-only filter returns typed `unsupported_date_with_variant`. | AC-R10, AC-S09 |
| D-13 | **Closed** (PD-21) | «عملکرد مالی» and «صورت عملکرد مالی» follow the «صورت سود و زیان» route (Feature 144), including its analysis handling. «صورت عملکرد» and «صورت درآمد» without «مالی» are unchanged. | AC-R03, AC-R04 |
| D-14 | **Closed** (PD-27, owner instruction 2026-11-05) | «بررسی / بررسی کن» with a single balance-sheet alias opens Feature 154. «صورت وضعیت» is a V2 balance-sheet alias. An income alias with «فصلی» and «بررسی کن» opens Feature 144 like the old starter card. «تحلیل» keeps analysis. | AC-R13, AC-R14 |

**Derived rule.** `unsupported_date_with_variant` follows from PD-20 together with R-44: neither path can honor a date combined with a consolidated or full-table filter. It is recorded here for visibility.

## 14. Evidence gaps

The full register, with the exact evidence required, is in [discovery.md §8](discovery.md#evidence-gaps). Summary:

Each gap disables only the behavior named in the middle column; everything else ships (PD-26).

| Gap | Exact behavior affected while open | ACs |
|---|---|---|
| E-1 Semantic equivalence of each fallback alias | That pair's **fallback** is not used. A present primary is always used; when the primary is absent, the row shows «—», and in the chart that point is a gap. | AC-M09, AC-G02 |
| E-2 Restated-comparative linkage to the anchor report | Only the wording that ties the baseline to the anchor report, and the post-v1 preference for an embedded restated comparative. The v1 exact-date stored baseline and its restatement label are unaffected. | AC-C08 (post-v1 part only) |
| E-3 `JalaliFiscalYearEnd` evidence coverage | For a company lacking evidence, only `fiscal_year_end`, `previous_fiscal_quarter_end` and `same_stage_previous_year` are unavailable. Synthetic tests pass now; live passes need the evidence. | AC-C02, AC-C03, AC-C06, AC-C13 (live) |
| E-4 Fixture persistence | Only the **live** runs against the supplied values. Fixture-based tests are unaffected. | AC-S01, AC-M03, AC-M04 (live) |
| E-5 Bank GroupId in each environment | Template selection in an environment whose configured GroupId is unverified. That is a release blocker for that environment only. | AC-M01 |
| E-6 Consolidated existence check | Only the consolidated suggestion: it is shown only when the query-time `EXISTS` check succeeds. | AC-S05 |
| E-7 Two-sided comparison screenshot not received | Only the visual sign-off against the reference image. The layout is built from the written description. | AC-P03, AC-P04, AC-P11 |
| E-8 Current/non-current classification of General rows 1, 306, 18, 19, 21, 307 and 45 | Only those rows' subgroup placement. They are shown at section level, and moving one into a subgroup needs source or analyst evidence. | AC-M04 (subgroup placement) |
