# Feature 161 — Discovery: repository findings

Status: read-only discovery, 2026-11-14, at `develop` HEAD `4cd4b77` (v1.12.3). No code, configuration, test, migration or existing spec was changed. **No database was queried.** The supplied فلوله and وبملت amounts are treated as fixtures, not as verified Development data. Line numbers are as of that commit.

> **Gate A (2026-11-14):** the owner chose item 128 («جریان وجه نقد عملیاتی - جدید») as operating cash flow instead of the derivation `122 − 121` discussed below (design-proposal.md D-03). The findings below are kept as recorded during discovery.

This file records **repository findings** (R-xx) and **evidence gaps** (E-xx). The proposed design is in [design-proposal.md](design-proposal.md). Each finding is marked **Verified** (read in code or specs) or **Inference** (reasoned from code, not executed).

Backend paths are relative to `src/backend/`, frontend paths are relative to `src/frontend/src/`.

## 1. Numbering and conventions

- **R-01 (Verified).** The highest feature folder is `specs/160-self-service-profile-editing/`. 157 is an unused gap; numbers are not reused, and `162` already appears as a branch name (`feature/162-capability-source-map`). This feature is therefore **161**.
- **R-02 (Verified).** `specs/POLICY-V1-FREEZE.md`: new capabilities ship on the V2 path only. Feature 154 followed this with a V2-only partial class (`FinancialCopilot.Application/AI/Orchestration/FinancialStatementTableIntentRules.V2.cs`) that leaves the shared V1 rules untouched.
- **R-03 (Verified).** The workflow (`specs/FEATURE_DEVELOPMENT_WORKFLOW.md`) forbids `user-story.md` and `tasks.md` before Gate A. The specification, plan and tasks requested for this feature are therefore drafted inside the `design-proposal*.md` files, marked *Proposed — not approved*.

## 2. Data model and ingestion

- **R-04 (Verified). Cash-flow statements are already ingested.** Noavaran current API `POST api/v2/FS/CashFlow/Values` (`FinancialCopilot.Infrastructure/Financial/Providers/NadpcoApi/NadpcoApiDataProviderClient.cs:74, 122, 401-402`). The normalizer persists them with `StatementType = CashFlow` (`NadpcoApiFinancialStatementNormalizer.cs:29`) using the same header/line-item/catalog tables as the balance sheet. `FinancialStatementType` is `{ IncomeStatement, BalanceSheet, CashFlow }` (`FinancialCopilot.Domain/Financial/Entities/FinancialEntities.cs:122-127`).
- **R-05 (Verified, inherited from Feature 154 R-04…R-08).** Same storage facts apply to cash flow:
  - Header columns `Id, ProviderName, ExternalCompanyId, ExternalStatementId, StatementType, PeriodType, PeriodStart, PeriodEnd, IsAudited, IsRepresented, IsComposing, WarningsJson, PublishedAt, CompanyId` (`FinancialIngestionRows.cs:215-272`). `CompanyId` is null for Noavaran rows; statements are linked by `(ProviderName, ExternalCompanyId)`.
  - No unit column; scale is `AssumedScale="MillionRials"` inside `WarningsJson`, parsed by `EfCoreFinancialStatementTableRepository.StatementMetadata.Parse`.
  - `PeriodStart` is Gregorian-derived; the issuer fiscal-year end is `JalaliFiscalYearEnd` in `WarningsJson`, owned by `FiscalCalendarPolicy` (`FinancialCopilot.Application/FinancialData/Ingestion/FiscalCalendarPolicy.cs`).
  - Catalog `FinancialStatementSourceItems` is unique on `(ProviderName, StatementType, SourceItemId)`. **SourceItemId is scoped by statement type**: CashFlow 5 is «سود پرداختی بابت تسهیلات…», IncomeStatement 5 is G&A expense. Catalog row `Id` values are environment-specific surrogates — resolve by tuple.
  - `FinancialStatementLineItems.FinancialStatementId` has no declared FK; joins are explicit. `Value` is `decimal?`.
  - The catalog has no parent/section/hierarchy column. **Section placement cannot be read from the database.**
- **R-06 (Verified). CashFlow catalog snapshot.** `specs/141-schema-aware-semantic-query-engine/SemanticCatalog/source-item-snapshot.json` lists **70** `NoavaranCurrentApi` CashFlow items (snapshot date not recorded in the file). All have `unit = N/A`; only item 1 carries a MetricCode (`OPERATING_CASH_FLOW`). The catalog contains **three coexisting presentations**:
  - *Legacy (pre-revision Standard No. 2, five sections):* 1 «جریان وجه نقد عملیاتی», 58 «جریان وجه نقد بازده سرمایه گذاریها و سود پرداختی بابت تامین مالی», 59 «… سرمایه گذاری - قدیم», 60 «… قبل از فعالیت های تامین مالی», 61 «… تامین مالی - قدیم», 62 «خالص افزایش (کاهش) در وجه نقد», 63 «مانده وجه نقد در پایان سال».
  - *Codal:* 120 (returns on investments and finance servicing — Codal), 121 investing, 122 before financing, 123 financing, 124 net change, 125 closing cash — each suffixed «- کدال». **There is no Codal operating item**; operating must be derived.
  - *New:* 126 investing, 127 financing, 128 operating — each suffixed «- جدید».
  - *Shared:* 33 «مانده وجه نقد در آغاز سال», 35 «تاثیر تغییرات نرخ ارز».
  - *Detail items:* 3–24, 37–48, 70, 71, 83, 95, 96, 103, 104, 114, 117, 119, 129–139 (full list in [design-proposal-mapping-matrix.md](design-proposal-mapping-matrix.md)).
  - *Aggregates of inflows/outflows:* 106–113 («مجموع منابع/مصارف …», «مجموع جریانات نقد ورودی/خروجی»).
- **R-07 (Verified). The only existing cash-flow metric uses a legacy item.** `NadpcoApiStatementItemMaps.CashFlowItemIdToMetricCode` maps **item 1** to `OPERATING_CASH_FLOW` (`NadpcoApiStatementItemMaps.cs:36-41`, comment "The attached sample identifies item 1 as operating cash flow"). That code feeds derived-metric recalculation (`ServiceCollectionExtensions.cs` ≈L1528; `MetricRecalculationProcessor.cs` ≈L37) and the Feature 083 table label «جریان نقد عملیاتی» (`FinancialStatementTableServices.cs:299`). It is **not** the Codal derivation `122 − 121` required for this feature. Two different "operating cash flow" values could appear in the product (OQ-081).
- **R-08 (Inference). Old vs. revised presentation.** Under the revised Iranian Accounting Standard No. 2 (three sections), interest and dividends paid are financing, interest and dividends received are investing, and income tax paid is operating. Under the legacy five-section format, item 120/58 (returns and finance servicing) and taxes are separate sections, so `122 − 121` would include finance-servicing flows and would not be a three-section operating cash flow. A non-zero item 120 is therefore a marker of the legacy presentation (E-04).

## 3. Fixture arithmetic (computed for this discovery, exact integers)

All amounts are million IRR as supplied. Nothing here was read from a database.

| Check | فلوله (255, 6M to 2026-09-22) | وبملت (452, 6M to 2026-09-22) |
|---|---:|---:|
| Operating = 122 − 121 | 6,559,786 + 359,104 = **6,918,890** | 342,438,949 + 43,767,594 = **386,206,543** |
| Cross-check vs 128 | 6,918,890 − 6,918,890 = **0** | 386,206,543 − 386,206,543 = **0** |
| R1: O + I + F = 124 | 6,918,890 − 359,104 + 9,025,064 = 15,584,850 ✔ | 386,206,543 − 43,767,594 − 141,868,091 = 200,570,858 ✔ |
| R2: 33 + 124 + 35 = 125 | 6,692,454 + 15,584,850 + 0 = 22,277,304 ✔ | 2,718,638,069 + 200,570,858 + 617,787,300 = 3,536,996,227 ✔ |
| Financing detail vs 123 | 23 + 24 + 5 + 6 = 24,635,600 − 13,908,702 − 1,457,520 − 244,314 = **9,025,064** (residual 0) | 23 + 24 + 5 + 6 + 117 + 119 = **−141,868,091** (residual 0) |
| Investing detail vs 121 | 10 + 14 = −359,535 + 300 = −359,235 → **residual +131** | 10 + 48 + 4 + 14 + 104 = **−43,767,594** (residual 0) |
| Tax paid (9) | −561,560 | −240,267,354 |
| Cash before FX = 33 + 124 | 22,277,304 | 2,919,208,927 |
| Total change in cash = 124 + 35 (= 125 − 33) | 15,584,850 | 818,358,158 |

- **R-09 (Verified by arithmetic).** Both samples place 5 (interest paid on facilities) and 6 (dividends paid) in **financing**, and the bank sample places 117/119 (treasury shares) in financing and 4 (dividends received) and 104 (held-for-sale proceeds) in **investing**, with exact closure. This is consistent with the revised three-section presentation.
- **R-10 (Verified by arithmetic).** In both samples 124 excludes the FX effect: `33 + 124 + 35 = 125`. For وبملت, FX is 75.49 % of the total change in cash, so omitting it would break the closing balance by 617,787,300.
- **R-11 (Verified by arithmetic).** The supplied فلوله investing items leave a residual of +131. Items 3 (interest received) or 4 (dividends received) were probably present but not supplied (Inference). Detail-level closure must be tested on full persisted statements (E-08).

## 4. Reusable code paths (what exists today)

| # | Concern | Existing path | Reuse for Feature 161 |
|---|---|---|---|
| 1 | Intent recognition | `FinancialStatementTableIntentRules.cs` (shared V1/V2); `FinancialStatementTableIntentRules.V2.cs` (`CanonicalizeV2`, `ResolveBalanceSheetRequest`, follow-up cues); `DeterministicCapabilityInterpreter.cs`; `CapabilityInterpretationGovernance.cs`; `CapabilityRoutingPrecedence` marker `feature154-v2-governed-report` | Extend the V2 partial only |
| 2 | Company resolution | `ICompanyResolutionOutcomeService.ResolveAsync(symbol, "NoavaranCurrentApi")` (provider-aware, typed Ambiguous/Unsupported) | Reuse as-is |
| 3 | Statement lookup | `EfCoreBalanceSheetStatementReader` (bounded headers, exact-date lookups, consolidated `EXISTS`, batched line items) | Mirror for CashFlow; same bounds style |
| 4 | Line-item retrieval | Batched join line item → catalog by tuple | Same pattern |
| 5 | Metric mapping | `BalanceSheetRowRegistry` (single owner of rows, primaries, unverified candidates) | New `CashFlowLineRegistry` with the same contract |
| 6 | Report construction | `BalanceSheetReportBuilder`, `BalanceSheetStatementSelector` (ranking/conflict), `FiscalCalendarPolicy` | Reuse `FiscalCalendarPolicy`; mirror selector rules |
| 7 | Chart data | Backend sends final values; frontend maps (no arithmetic) | Same; waterfall steps computed in backend |
| 8 | Frontend | `components/app/balance-sheet-report/*` (wide/compact by `ResizeObserver` and `AnswerDensityContext`, canvas PNG, Recharts) | New sibling folder `cash-flow-report/` |
| 9 | Export | Canvas PNG (`balance-sheet-report-image.ts`, `quarterly-financial-table-image.ts`); **no PDF pipeline exists** | PNG only |
| 10 | Tests | `BalanceSheetReport154*Tests.cs` (unit, routing, V1 freeze, PostgreSQL, endpoint, follow-up, live Development), frontend vitest + browser scripts | Same suite shape |

- **R-12 (Verified). Dispatch point.** `FinancialStatementTableQueryUseCase.ExecuteAsync` (`FinancialStatementTableServices.cs:461-490`) branches to Feature 144 (`UseQuarterlyReportTable`) and Feature 154 (`UseBalanceSheetReport`), otherwise Feature 083. A third branch is the natural seam.
- **R-13 (Verified). Carrier.** `FinancialStatementTableResult` (`FinancialStatementTableContracts.cs:77-85`) carries `QuarterlyResult` and `BalanceSheetReport`; the HTTP contract mirrors it (`API/Contracts/AiFacadeContracts.cs:421`, mapping `AiFacadeController.cs:689`); `message-list.tsx:176-243` branches on `balanceSheetReport`.
- **R-14 (Verified). Executor.** `FinancialStatementTableCapabilityExecutor.ExecuteV2Async` (`SemanticCapabilityExecutors.cs:447-485`) builds the query, honours report markers (`Feature154ReportMarker`, `Feature144ReportMarker`, L420-421) for follow-ups, and maps typed outcomes to Feature 117 reason codes (`BalanceSheetOutcome`).
- **R-15 (Verified). Bank classification.** Template selection uses the resolved company's single `NoavaranEligibleCompanies.GroupId` equal to `QuarterlyFinancialReports:BankingIndustryGroupId`; startup fails if unset (`FinancialCopilot.API/Program.cs:147-152`). No ticker or name inference. The GUID is environment-specific (Feature 154 R-20/E-5).
- **R-16 (Verified). Quarterly conversion is income-statement specific.** `QuarterlyFinancialReportsNormalizer.Normalize` filters `StatementType == "IncomeStatement"` (L75-77) and hard-codes its rows; it is not reusable for cash flow without a refactor of the Feature 144 path. Its *rules* are reusable: fiscal slots from `FiscalCalendarPolicy`, ranking (audited, represented, announcement date, report id), conflict on same-rank differing values, `Q(n) = YTD(n) − YTD(n−1)` only for adjacent compatible slots, Q4 = 12M − 9M, never across fiscal years.
- **R-17 (Verified). Version ranking already exists twice** (Feature 144 normalizer L115-143; Feature 154 `BalanceSheetStatementSelector`). A third copy would add a third owner of the same rule.

## 5. Routing today (what a cash-flow question does now)

- **R-18 (Verified). Cash-flow aliases open the Feature 083 full table.** `CashFlowAliases` = «جریان وجه نقد», «جریان وجوه نقد», «صورت جریان وجوه نقد», «صورت جریان نقد», «جریان نقدی» (`FinancialStatementTableIntentRules.cs:92-99`); `ResolveStatementType` returns `CashFlow` after the balance check (L247-259); `LooksLikeFinancialStatementTableQuery` then opens the Feature 083 latest-statement table with **every** persisted item, legacy, new and Codal mixed, titled «صورت جریان وجوه نقد» (`FinancialStatementTableServices.cs:767`).
- **R-19 (Verified). Alias gaps.** After normalization (ZWNJ → space) none of these match: «صورت جریان‌های نقدی» (→ «جریان های نقدی»), «جریان‌های نقدی», «گردش وجوه نقد», «جریان نقد» (bare), «جریان نقد عملیاتی», «جریان نقد آزاد», «cash flow statement» (English appears only in `FinancialStatementAnalysisIntentRules.cs:192` and `DisclosureListingIntentRules.cs:12`).
- **R-20 (Inference). Misroutes to close.**
  - «گزارش جریان نقدی فصلی X»: `LooksLikeQuarterlyReportTableQuery` returns true through the «گزارش» shorthand (L35-36) and `BuildQuarterlyQuery` forces `IncomeStatement` — the Feature 154 R-26 defect, for cash flow.
  - «روند جریان نقد عملیاتی فولاد»: `TrendWords` contains «روند» (`DeterministicCapabilityInterpreter.cs:34`) → `monthly_activity_trend` 0.9, and `StatementWords` (L40) has no cash-flow word.
  - «وضعیت تامین مالی آن چطور است؟»: «وضعیت» is an `AnalysisWords` entry (L35) → `comprehensive_analysis` 0.75.
  - «سود»/«نقدی» tokens in «سود سهام نقدی پرداخت کرده» are `MetricWords` (L44) and may pull a profit metric route.
  - «تحلیل جریان نقد X» reaches Feature 081 (`FinancialStatementAnalysisIntentRules.cs:192-193`), which can only read the item-1 `OPERATING_CASH_FLOW` metric (R-07).
- **R-20a (Inference, pre-existing defect).** `ContainsAny` is a substring match and «جریان نقدی» is a substring of «جریان نقدینگی». «جریان نقدینگی حقیقی فولاد» (real money flow, an existing insight term in `lib/insight-localization.ts:15, 138`) therefore resolves `StatementType = CashFlow` and satisfies `LooksLikeFinancialStatementTableQuery`, opening the Feature 083 cash-flow table unless an earlier route claims it. Feature 161's V2 recognition must use whole-word matching and exclude «نقدینگی»; the shared V1 rule stays frozen (recorded as BUG candidate in the proposal).
- **R-21 (Verified).** `CanonicalizeV2` already guards «صورت وضعیت جریان وجوه نقد …» so it keeps its own statement type (`OtherStatementHeadGuard`, V2 partial L44).
- **R-22 (Verified).** Follow-up carry-over (Feature 120) fills a missing company from active task state; report markers on the `statementType` slot (`;report=feature154_balance_sheet_report`) let bare follow-ups repeat a report (`SemanticCapabilityExecutors.cs:447-470`, `ConversationTaskStateContracts.cs:150-156`).
- **R-23 (Verified).** Feature 081 period analysis is deterministic and source-backed (`specs/081-…/user-story.md:39, 184`); no LLM narrative is used for statement analysis today.
- **R-24 (Verified).** CyclicalWaves writes income statements only; in `FinancialData:ChatProvider = CyclicalWaves` mode a non-income statement returns "unavailable for provider" (Feature 154 R-15). Production runs `NoavaranAmin` (OQ-008).
- **R-25 (Verified).** Telegram has no typed table rendering; it sends `TextAnswer` markdown (Feature 154 R-30). Feature 154 supplies a text renderer (`BalanceSheetReportTextRenderer.cs`).

## 6. Frontend

- **R-26 (Verified).** Recharts `^2.15.4` (`src/frontend/package.json:76`). No waterfall chart exists anywhere in `src/frontend/src`. Recharts supports stacked bars and range bars (`[low, high]` values), so a waterfall needs no new library.
- **R-27 (Verified).** Feature 154 conventions to reuse: teal header `#164e63`, section caption rows `bg-[#d9efed]`, `Intl.NumberFormat("fa-IR-u-nu-arabext")`, parentheses for negatives, «۰» for zero, «—» for missing (`balance-sheet-report-model.ts:26-78`), wide/compact layout chooser (`balance-sheet-report.tsx:23-34`), compact accordion headers, PNG via canvas with the shared brand header/footer.

- **R-28 (Verified).** The app has a theme toggle (`components/app/theme-toggle.tsx`) and a few `dark:` styles, but the Feature 144 and 154 report components contain **no** `dark:` classes: they render a fixed light "paper" surface with explicit colors in both themes (for example the chart series `#2563eb`, `#0d9488`, `#a16207` in `balance-sheet-funding-chart.tsx:21-25`). Consistency means Feature 161 does the same; a dark report surface would be a separate cross-report change.

## 7. Evidence gaps

Each gap names the evidence that closes it. All are read-only and collected by the proposed task T01. No gap is closed by matching sample values alone.

| ID | Gap | Evidence required | Until closed |
|---|---|---|---|
| E-01 | Catalog completeness | Read-only export of every `NoavaranCurrentApi` CashFlow catalog row in Development, with per-item presence counts split by bank / non-bank template and by fiscal year | Only items in the matrix marked *verified* are displayed; any other item is reported in provenance and the residual row |
| E-02 | Sign convention | For every mapped item, the sign of persisted values across ≥ 30 statements (inflow +, outflow −) | An item whose sign is not verified is not used in a derived metric |
| E-03 | Equivalence of the «- جدید» totals (126/127/128) with the Codal totals | Statements where both sets exist: compare 128 with 122 − 121, 126 with 121, 127 with 123 | The "new" fallback stays disabled (OQ-074) |
| E-04 | Legacy five-section presentations | Count of statements with 120 ≠ 0 (or 58/59/61 present and Codal absent), by fiscal year | Such statements get `unsupported_presentation` |
| E-05 | Operating composition | Confirm 9 (tax paid) sits inside Codal operating, and what item 1 means on revised-format statements (cash from operations before tax?) | «نقد حاصل از عملیات (قبل از مالیات)» is not shown; tax paid is shown as a disclosed operating component only |
| E-06 | Section placement of ambiguous detail items | 3, 13, 42, 95, 96, 114, 129, 130, 135, 22, 136–139 — closure tests on full statements (detail sum per section vs 121/123), bank and non-bank separately; analyst sign-off | Item stays *unverified*: not displayed as a detail row; amount falls into the residual row |
| E-07 | FX treatment | Count of statements where `33 + 124 = 125` exactly while 35 ≠ 0 (FX possibly embedded in 124) and where 35 is absent | Rule FX-3 (warning, no adjustment) applies |
| E-08 | Fixture persistence | Statement ids, flags, `JalaliFiscalYearEnd`, `AssumedScale` and all CashFlow items for 255 and 452 at 2026-09-22; IncomeStatement 15/143 for the same statements; GroupIds | Supplied values are unit-test fixtures only; live checks are pending |
| E-09 | Absent item vs null value | Whether the normalizer persists a row for every payload item (with null amount) or only non-null items | Absent and null are both "not reported"; zero is only an explicit 0 |
| E-10 | Bank GroupId per environment | Same as Feature 154 E-5 | Release blocker per environment |
| E-11 | Visual reference | A Codal cash-flow statement screenshot for the table layout (none attached) | Layout specified from the written description; visual sign-off task T-F09 |
