# Feature 154 — Discovery: repository findings

Status: read-only discovery, 2026-11-04, at `develop` HEAD `573cfd8`. It was revised the same day for revision 2 of the spec (R-40 to R-47 and §8) and revision 3 (R-48, R-49, and gap rescoping including the new E-8). No code, configuration, test, migration, or existing spec was changed. Nothing was queried in any database. Line numbers are as of that commit.

This file records **repository findings** (R-xx) and **evidence gaps** (E-xx, §8). The Product Owner's decisions are in [spec.md §3](spec.md#3-product-decisions-supplied-by-the-product-owner), and the decision register is in [spec.md §13](spec.md#13-decision-register). Each finding is marked **Verified** (read in code or specs) or **Inference** (reasoned from code, not executed).

The Codal screenshots mentioned in the revision-2 request, including the two-sided comparison layout, were not attached to the conversation and are not in the repository. The layouts are specified from the written description only (E-7).

Backend paths are relative to `src/backend/`, and frontend paths are relative to `src/frontend/src/`.

## 1. Feature numbering and conventions

- **R-01 (Verified).** The highest existing feature folder is `specs/153-mobile-pwa-workspace/`, so this feature is **154**. Recent features (144, 153) use `spec.md`, `design.md` and `tasks.md`. Feature 153 adds `discovery.md` for evidence. Older features use `user-story.md` + `tasks.md`; `specs/README.md:3` says each folder holds one user story and its tasks.
- **R-02 (Verified).** `specs/POLICY-V1-FREEZE.md`: no new capability, intent or route may be added to V1. New AI capabilities ship on V2 only.
- **R-03 (Verified).** The chat landing copy already announces this capability: «گزارش ترازنامه به‌زودی اضافه می‌شود» (`routes/_app.chat.tsx:55`).

## 2. Data model

- **R-04 (Verified). Statement header.** `NormalizedFinancialStatementRow` (`FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/FinancialIngestionRows.cs:215-272`) maps to `FinancialStatements` and has these columns: `Id`, `ProviderName`, `ExternalCompanyId` (string), `ExternalStatementId`, `StatementType` (enum name string), `StatementTitle`, `PeriodType`, `PeriodStart`, `PeriodEnd` (DateOnly), `IsAudited`, `IsRepresented`, `IsComposing`, `WarningsJson`, `LogicalVendor`, `SourceMode`, `VendorPeriodDate`, `PublishedAt` (DateOnly?), `LastSynchronizedAt` and `CompanyId` (Guid?).
  - `FinancialStatementType` is `{ IncomeStatement, BalanceSheet, CashFlow }` (`FinancialCopilot.Domain/Financial/Entities/FinancialEntities.cs:122-127`).
  - Statements persist only `ThreeMonths`, `SixMonths`, `NineMonths` or `TwelveMonths` (`Domain/Financial/Periods/FiscalPeriod.cs:3-13`).
- **R-05 (Verified). Variant flags.**
  - `IsComposing=true` means consolidated (تلفیقی); `false` means standalone (جداگانه / غیرتلفیقی).
  - `IsAudited` means audited.
  - `IsRepresented` means restated (تجدید ارائه‌شده).
  - Evidence: `FinancialStatementTableServices.cs:701` and `specs/142-manual-noavaran-financial-statement-backfill/spec.md:71`.
  - Spec 142 warns that the flag columns were added with a `false` migration default, so very old rows cannot be trusted as genuinely false.
- **R-06 (Verified). Unit and scale.**
  - `FinancialStatements` has no unit column. Scale is recorded in `WarningsJson` as `AssumedScale="MillionRials"` together with `SourceAmountUnit` (`NadpcoApiFinancialStatementNormalizer.cs:280-281`).
  - Feature 144 requires `AssumedScale=MillionRials` for monetary rows. The rule is in `specs/144-quarterly-financial-reports-table/spec.md:110` and enforced in `QuarterlyFinancialReportsNormalizer.cs:603-604`.
  - Catalog `Unit` is `N/A` for every balance-sheet item in the snapshot.
  - The million-IRR contract is therefore established for Noavaran current-API statements through `AssumedScale`, not through a column.
- **R-07 (Verified). Source-item catalog.**
  - `FinancialStatementSourceItemCatalogRow` (`FinancialIngestionRows.cs:274-291`) has `Id`, `ProviderName`, `StatementType`, `SourceItemId`, `TitleFa`, `TitleEn`, `Unit` and `LastSynchronizedAt`. It has **no MetricCode, parent, hierarchy, template, or industry column**. MetricCode lives in `FinancialStatementSourceItemMetricMappings` (L293-300).
  - The unique index is on `(ProviderName, StatementType, SourceItemId)` (`FinancialIngestionConfigurations.cs:163`). **SourceItemId is scoped by provider and statement type.** For example, IncomeStatement 143 is net profit but BalanceSheet 143 is total liabilities. IncomeStatement 304–307 are bank income items, while BalanceSheet 304–307 are equity/receivable/payable items.
  - Catalog row `Id` values are environment-specific surrogates; resolve by the tuple, not by the UUID (`FinancialStatementTableServices.cs:37-40`).
- **R-08 (Verified). Relationships.**
  - `FinancialStatementLineItems.SourceItemCatalogId → FinancialStatementSourceItems.Id` is a **declared** foreign key with `SetNull`. It is in `FinancialIngestionConfigurations.cs:198-201`, migration `20260630154843_AddNoavaranFinancialStatementSourceCatalogAndVariants.cs:148-154`, and the model snapshot.
  - `FinancialStatementLineItems.FinancialStatementId → FinancialStatements.Id` is a **logical** relationship with **no declared FK**. Initial migration `20260527045316_InitialFinancialIngestion.cs:29-41` creates the column with no constraint, and the code joins on it explicitly.
  - Line items are unique on `(FinancialStatementId, SourceItemCatalogId)`, filtered to non-null (`FinancialIngestionConfigurations.cs:184-203`).
  - `NormalizedFinancialStatementLineItemRow` has `Id`, `FinancialStatementId`, `SourceItemCatalogId`, `MetricCode` (nullable) and `Value` (`decimal?`).
- **R-09 (Verified). Balance-sheet catalog items.** These are from `specs/141-schema-aware-semantic-query-engine/SemanticCatalog/source-item-snapshot.json`, provider `NoavaranCurrentApi`, `StatementType=BalanceSheet` (107 items). Only 147 (`TOTAL_EQUITY`) and 188 (`CAPITAL`) carry a MetricCode, which matches `NadpcoApiStatementItemMaps.cs:26-33`. Items relevant to this feature:

  | SourceItemId | TitleFa (catalog) | Notes |
  |---:|---|---|
  | 1 | موجودی نقد | |
  | 2 | مطالبات از بانک مرکزی | banking |
  | 4 | مطالبات از بانک‌ها و موسسات اعتباری | banking |
  | 18 | موجودی مواد و کالا | |
  | 19 | سفارشات و پیش پرداخت‌ها | |
  | 21 | دارایی‌های ثابت مشهود | |
  | 39 | بدهی به بانک‌ها و موسسات اعتباری | banking |
  | 45 | پیش دریافت‌ها | (79 = long-term portion of customer advances, separate item) |
  | 47 | تسهیلات مالی دریافتی کوتاه مدت | |
  | 51 | تسهیلات مالی دریافتی بلند مدت | |
  | 57 | سرمایه | share-capital candidate |
  | 58 | وجوه ناشی از افزایش سرمایه | |
  | 60 | صرف سهام | |
  | 64 | مازاد تجدید ارزیابی | |
  | 71 | سود (زیان) انباشته | |
  | 135 / 298 | جمع دارایی‌های جاری / … - کدال | candidate alias |
  | 137 / 299 | جمع دارایی‌های غیر جاری / … - کدال | candidate alias |
  | 138 / 300 | جمع کل دارایی‌ها / … - کدال | candidate alias |
  | 139 / 301 | جمع بدهی‌های جاری / … - کدال | candidate alias |
  | 140 / 302 | جمع بدهی‌های غیر جاری / … - کدال | candidate alias |
  | 143 / 303 | جمع کل بدهی‌ها / … - کدال | candidate alias |
  | 144 | جمع کل بدهی‌های بهره‌دار | reported analytical item |
  | 145 | خالص سرمایه در گردش | reported analytical item |
  | 146 | جمع اندوخته‌ها | distinct from 147 |
  | 147 / 304 | جمع حقوق صاحبان سهام / … - کدال | candidate alias; 147 = `TOTAL_EQUITY` |
  | 148 / 305 | جمع بدهی‌ها و حقوق صاحبان سهام / … - کدال | candidate alias |
  | 160 | جمع حقوق صاحبان سهام شرکت اصلی | parent-attributable equity; not in scope |
  | 170 | سهام خزانه | banking addition; 297 = treasury-share premium, separate |
  | 188 | سرمایه پرداخت شده | share-capital candidate; `CAPITAL` |
  | 223 | مجموع سپرده های جذب شده | banking |
  | 286 | نوع دوره مالی | period metadata (categorical) |
  | 291 | مجموع تسهیلات اعطایی | banking |
  | 293 | مجموع دریافتنی های تجاری و غیر تجاری | candidate alias with 306 |
  | 306 | دریافتنی های تجاری و سایر دریافتنی ها | candidate alias with 293 |
  | 307 | پرداختنی های تجاری و سایر پرداختنی ها | candidate alias with 308 |
  | 308 | مجموع پرداختنی های تجاری و غیر تجاری | candidate alias with 307 |

- **R-10 (Verified). No approved balance-sheet aliases.**
  - `specs/141-schema-aware-semantic-query-engine/artifacts/navigation-hierarchy.yaml:12413-12519` (for example BS/298) has `canonicalConceptBindings: []` and states that the catalog "does not approve canonical semantic equivalence".
  - The registry report `evaluation/financial-source-item-registry-2026-10-10.md:21` says 295 of 296 items are semantically unbound.
  - The only approved primary/fallback pairs in the repository are **IncomeStatement** pairs (139←310, 140←311, 142←312, 143←313, `specs/144-…/spec.md:47-56`).
  - The direction of the receivable/payable pairs is not uniform. 293 is the «مجموع» aggregate while 306 is the Codal-style line; for payables it is reversed, with 307 the Codal-style line and 308 the «مجموع» aggregate. Equivalence cannot be read from naming either.
- **R-11 (Verified). Item 286.** It is «نوع دوره مالی», a categorical, non-monetary item with no effective unit (`specs/141-…/source-item-unit-review-report.md:32`, `evaluation/financial-source-item-registry-2026-10-10.md:22`).
- **R-12 (Verified). No hierarchy for detail grouping.** The catalog has no parent or section column, so current/non-current placement of a detail row cannot be read from the database. Feature 083 currently classifies sides by MetricCode or Persian title substring (`FinancialStatementTableServices.cs:224-253`). This feature must not reuse that substring classification for its curated rows.

## 3. Ingestion, provider and version selection

- **R-13 (Verified). Noavaran current API.**
  - Balance sheets are fetched from `POST api/v2/FS/BalanceSheet/Values` (`NadpcoApiDataProviderClient.cs:62, 397-398`).
  - The normalizer persists **every** variant, all line items with `SourceItemCatalogId`, `ExternalCompanyId = com_ID`, and `PublishedAt = anouncementDate`. It does not set `CompanyId` (`NadpcoApiFinancialStatementNormalizer.cs:45, 49-56, 76-78, 211-242`).
- **R-14 (Verified). CodalDB archive** (`NoavaranArchiveSql`). It writes only mapped items 147 and 188, with no `SourceItemCatalogId`, and it does not set `IsAudited/IsComposing/IsRepresented/PublishedAt` columns (`CodalDbFinancialStatementNormalizer.cs:120-139`). **Archive rows cannot supply this report.**
- **R-15 (Verified). CyclicalWaves.** It writes IncomeStatement only (`CyclicalWavesFinancialStatementNormalizer.cs:98-125`). When `FinancialData:ChatProvider=CyclicalWaves`, the swapped table use case returns "unavailable for provider" for any non-income statement (`CyclicalWavesV2QuarterlyIncomeStatementUseCase.cs:219-234`). The current configuration is `NoavaranAmin` (`FinancialCopilot.API/appsettings.json:198-200`).
- **R-16 (Verified). Existing variant-selection rules.**
  - **Feature 144 (income statement, approved).**
    - Separate scope only; a consolidated report is never substituted.
    - Within each fiscal slot, rank IsAudited, then IsRepresented, then AnnouncementDate, then ExternalReportId, all descending.
    - Same-rank ties with differing values give typed `conflicting_report_revision`; otherwise the highest ReportId wins. Mixed scope gives `conflicting_report_scope`.
    - Evidence: `specs/144-…/spec.md:92`, `QuarterlyFinancialReportsNormalizer.cs:115-143`.
    - The repository loads with `PeriodEnd` descending and drops rows whose `WarningsJson` scope disagrees with `IsComposing` (`FinancialStatementTableServices.cs:42-71`).
  - **Feature 083 (latest full statement table).**
    - Defaults to `IsComposing=false` with no fallback to consolidated.
    - Orders by **AnnouncementDate**, then PeriodEnd, then period months, then ExternalStatementId, then Id, all descending (`FinancialStatementTableServices.cs:109-176`; `specs/083-…/user-story.md` "Latest ordering").
    - This differs from the PeriodEnd-first rule supplied for this feature (PD-08).
  - **Feature 081 (statement analysis).** Standalone default; tie-break AnnouncementDate, then PeriodEnd, then months (`FinancialStatementAnalysisServices.cs:272-345`).
  - **Provider precedence.** `SourcePriorityResolution.cs:9-32` governs ingestion ownership only ("Scanner reads remain source-agnostic", L40-41). No reader merges providers for balance sheets.

## 4. Company resolution and classification

- **R-17 (Verified). Provider-aware resolution.**
  - `ICompanyResolutionOutcomeService.ResolveAsync(symbol, "NoavaranCurrentApi")` (`Application/FinancialData/Ingestion/CompanyResolutionContracts.cs:48`) is implemented by `CompanyResolverService.ResolveAsync` (`FinancialCopilot.Infrastructure/Financial/Ingestion/CompanyResolverService.cs:28-104`).
  - It works in tiers: ticker, TSE symbol, ISINs, instrument code, symbol/name, then a unique name fragment of at least 3 characters.
  - It returns typed `Ambiguous` and `UnsupportedProviderCompany` outcomes.
  - The older `ResolveBySymbolAsync` (L106-168) is not provider-aware and returns the first match. Feature 083 uses it.
- **R-18 (Verified). Blank `CompanyId`.**
  - Noavaran statements leave `FinancialStatements.CompanyId` null (`FinancialIngestionRows.cs:267-271`). Feature 144 links statements only by `(ProviderName="NoavaranCurrentApi", ExternalCompanyId)` from the resolved company (`QuarterlyFinancialReportsContracts.cs:274`; `FinancialStatementTableServices.cs:43-44`).
  - Spec 068 makes `ExternalCompanyId` (NADPCO `coID`) the canonical company key.
  - Bank Mellat (وبملت) is `com_ID 452` in `specs/144-…/banking/research.md:17`.
  - A blank `CompanyId` is therefore expected and does not block resolution.
- **R-19 (Verified). Bank discriminator.**
  - Feature 144 selects the banking template only when the resolved company's single `NoavaranEligibleCompanies.GroupId` equals `QuarterlyFinancialReports:BankingIndustryGroupId` = `c96520f9-1d2e-444a-ab46-5bdffd3797d2` (`FinancialCopilot.API/appsettings.json:596-598`; check at `QuarterlyFinancialReportsContracts.cs:261-271`).
  - Missing, duplicate or contradictory evidence gives `company_classification_unavailable`, and no name or ticker inference is made (`specs/144-…/banking/spec.md:7`).
  - The eligibility view is defined in migration `20260610100455_AddMonthlyActivityBackfillAndLineItemDetails.cs:53-64`.
- **R-20 (Verified, operational risk).** `IndustryGroups.Id` is generated by `Guid.NewGuid()` on first ingestion of a Noavaran `floorID` (`NadpcoApiCompanyNormalizer.cs:185-199`). The configured bank GroupId GUID is therefore environment-specific, and each environment must configure its own value. This risk already exists for Feature 144. `Companies.ReportingType == 1000003` ("Banking", `specs/135-noavaran-reporting-type-routing/Design.md:182`) is a second persisted signal, but it is used only for monthly-report routing.
- **R-21 (Verified).** No balance-sheet-specific bank template, row set, or code exists. Bank balance-sheet items (2, 4, 39, 223, 291) share the single BalanceSheet catalog.

## 5. Routing and orchestration

- **R-22 (Verified). Active path.**
  - `AiOrchestration:Mode = MicrosoftAgentFrameworkV2` (`appsettings.json:599-603`, `appsettings.Development.json:42-43`).
  - `SemanticRouting.DefaultMode = SemanticPrimary` (`appsettings.json:97-106`). `docker-compose.yml:452` falls back to `Shadow` if the `.env` key is absent.
  - `Feature141.Enabled = false` (`appsettings.json:604-605`).
- **R-23 (Verified). Paths that produce financial-statement tables.** Both V2 paths call `IFinancialStatementTableQueryUseCase`:
  - **(A) V2 semantic route.** Capability `financial_statement_table` (`ConversationalCapabilityContracts.cs:66, 223-229`). Interpretation runs through `HybridCapabilityInterpreter` (`CapabilityInterpretationGovernance.cs:326-544`), and execution through `FinancialStatementTableCapabilityExecutor` (`SemanticCapabilityExecutors.cs:384-405`). The workflow maps the payload at `FinancialCopilotWorkflowDefinition.cs:354-407`.
  - **(B) V2 deterministic legacy branch.** Used when there is no semantic frame (`FinancialCopilotWorkflowDefinition.cs:951-995`).
  - **V1** (`AiQueryOrchestrationService.cs:488-491`) produces only the Feature 083 single-statement table and is frozen.
  - **Feature 141** is disabled and serves no table.
- **R-24 (Verified). Feature 083 already serves balance sheets.**
  - «ترازنامه / صورت وضعیت مالی / دارایی و بدهی / دارایی‌ها و بدهی‌ها» resolve to `StatementType=BalanceSheet` (`FinancialStatementTableIntentRules.cs:83-90, 247-259`). The latest single statement is rendered as a markdown two-sided table (`FinancialStatementTableServices.cs:500-541, 706-739`).
  - Golden data and tests pin «آخرین ترازنامه کگل» and «آخرین ترازنامه غالبر» (`tests/…/GoldenDatasets.cs:154-165`; `tests/FinancialCopilot.IntegrationTests/AiFacadeV2EndpointTests.cs:2193-2196`).
  - Feature 083 places **assets on the left** (`specs/083-…/user-story.md:206-208`). This feature reverses that placement for its own report (spec §9.1).
- **R-25 (Verified). Existing «عملکرد» rules.**
  - `IncomeAliases` include «گزارش عملکرد» and «صورت عملکرد» (`FinancialStatementTableIntentRules.cs:65-74`). `QuarterlyReportAliases` include «گزارش عملکرد» (L76-82).
  - The comment at L32-33 preserves the shorthand «عملکرد ۵ فصل اخیر» for the quarterly **income-statement** table.
  - Feature 081 lists «عملکرد مالی غالبر در آخرین دوره منتشر شده چطور است؟» as a period-analysis example (`specs/081-…/user-story.md:74`).
  - **No rule exists for a bare «عملکرد مالی X».** It is not in any alias list, so its routing depends on the model or ends as Unsupported (Inference).
- **R-26 (Verified). Misroute risks the new route must close.**
  - L33 returns `true` for any quarter cue plus «گزارش/جدول/عملکرد», and `BuildQuarterlyQuery` then forces `IncomeStatement` (L210-221). «گزارش ترازنامه فصلی فولاد» would therefore open the income-statement table.
  - `DeterministicCapabilityInterpreter.cs:23` lists «وضعیت» among `AnalysisWords`, and `StatementWords` (L28) lacks «صورت وضعیت مالی». «صورت وضعیت مالی شگل» can therefore score `comprehensive_analysis` deterministically, and that capability is unavailable in NoavaranAmin mode (`ChatCapabilityAvailability.cs:109`) (Inference).
  - «ترازنامه» is not in the interpreter's non-entity word list (`DeterministicCapabilityInterpreter.cs:35-64`). It could be extracted as a company hint in a follow-up (Inference).
- **R-27 (Verified). Single-value requests.**
  - Features 131/132 identify a company **from an exact number** and are **IncomeStatement-only**. The executor hard-codes `IncomeStatement` (`SemanticCapabilityExecutors.cs:432-449`).
  - «جمع دارایی‌های شگل چقدر است؟» is **not** answered by 131/132. It reaches the model's choice, an Unsupported stop, or the legacy statement-analysis rule (`FinancialStatementAnalysisIntentRules.cs:31-58` maps «دارایی» to `TOTAL_ASSETS`) (Inference).
  - `TOTAL_ASSETS` is never written by any normalizer; only 147→`TOTAL_EQUITY` and 188→`CAPITAL` are mapped. That legacy answer is therefore likely empty. This is a pre-existing gap, recorded here and not fixed by this feature.
- **R-28 (Verified). Follow-up state (Feature 120).**
  - The window is 20 minutes, 6 turns, with 0.85 minimum carry-over confidence (`ConversationTaskStateContracts.cs:58-73`).
  - A Missing symbol is filled from the active state. Ambiguous, Invalid or Unsupported symbols are not (L409-462).
  - No test covers «ترازنامه‌اش را هم نشان بده».
- **R-29 (Verified). No-data outcomes.** Feature 117 provides typed outcomes and reason codes (`AiDialogueOutcomePolicy.cs:5-34, 155-200`). The table executor returns `NoData` whenever the use case returns null (`SemanticCapabilityExecutors.cs:403`). That collapses unresolved company, unresolved provider and no matching statement into one outcome (`FinancialStatementTableServices.cs:461-493`). Feature 144 instead uses a typed outcome enum (`QuarterlyFinancialReportsContracts.cs:6-16`).
- **R-30 (Verified). Telegram.** Telegram calls the same orchestration (`TelegramAiAssistantAdapter.cs:144`). `TelegramAssistantResponseRenderer.cs:64-113` has no handling for `FinancialStatementTableResult`, so it sends `TextAnswer` markdown split at 3,900 characters. There is no Telegram image for financial-statement tables.
- **R-31 (Verified). Suggested actions.** There are no balance-sheet follow-up suggestions. Quarterly-table follow-ups come from `QuarterlyFinancialReportFollowUpSuggestionService.cs:21-62`.

## 6. Income-statement report reference (Feature 144)

- **R-32 (Verified). Typed result chain.**
  - `QuarterlyFinancialReportTableResult` (`QuarterlyFinancialReportsContracts.cs:152-171`) is carried as `FinancialStatementTableResult.QuarterlyResult` (`FinancialStatementTableContracts.cs:71-78`) on `AiQueryResponse.FinancialStatementTableResult` (`AiOrchestrationContracts.cs:116, 138`).
  - HTTP passes it through (`API/Contracts/AiFacadeContracts.cs:49, 333, 411-417`; mapping `AiFacadeController.cs:654-686`). Persistence and replay preserve it.
  - Row definitions are code constants (`QuarterlyFinancialReportsNormalizer.cs:7-58`). Template IDs are `GeneralIncomeStatement` and `BankingIncomeStatement` (L473, L515).
- **R-33 (Verified). Enum serialization caveat.** No `JsonStringEnumConverter` was found (`Program.cs:91`), so enums probably serialize as numbers (Inference). `quarterly-financial-table.tsx:77, 102` compare `row.policy` to string names and work only through the `rowId` fallback. A new contract should identify rows and templates by string IDs.
- **R-34 (Verified). Desktop presentation.**
  - `QuarterlyFinancialTableDesktop` (`components/app/quarterly-financial-table.tsx:469-575`) uses the teal header gradient `from-[#164e63] via-[#155e75] to-[#0f766e]` (L481).
  - Gross-margin row: row `font-bold`, label cell `bg-yellow-200 font-extrabold text-amber-950`, value cells `bg-yellow-200 font-bold text-amber-950` (L539-553).
  - Negative cells use `bg-rose-100 text-rose-900` with accounting parentheses. The formatter is `Intl.NumberFormat("fa-IR-u-nu-arabext")` (L73-74).
  - **Valid zero renders as `-`** (spec 144 §"Zero, missing").
- **R-35 (Verified). Mobile.**
  - `/m` sets `AnswerDensityContext` to compact (`routes/m.c.$threadId.tsx:153`). The quarterly table then switches to `QuarterlyReportMobile` (`quarterly-financial-table.tsx:451-467`; `components/mobile/answers/quarterly-report-mobile.tsx`).
  - That view has a cards/table toggle, a collapsible chart, margin styling `ring-gold/50` / `bg-gold/10`, and a sticky first column.
- **R-36 (Verified). Chart.** Recharts `ComposedChart` (`quarterly-financial-chart.tsx`; `recharts ^2.15.4`, `package.json:71`) uses a second percent axis and `connectNulls=false`. Mobile uses `components/mobile/answers/quarterly-trend-chart-mobile.tsx`.
- **R-37 (Verified). Image export.**
  - `downloadQuarterlyFinancialTableImage` (`components/app/quarterly-financial-table-image.ts:210-367`) draws on a native Canvas 2D surface: 1200 px wide, at least 2× DPR, Vazirmatn font, Sapio brand header and footer, and gross-margin fill `#fef08a`.
  - It is triggered by «دانلود تصویر» on desktop (`quarterly-financial-table.tsx:487-496`) and mobile (`quarterly-report-mobile.tsx:126-135`) through `useQuarterlyReportActions` (L423-448).
  - `QuarterlyFinancialTable:ShowChart` controls chart inclusion, exposed through `GET /api/ui/v1/config` (`Program.cs:252-259`).
- **R-38 (Verified). Billing.** The tool runs inside the outer AI operation, with one reservation and finalization (Feature 144 AC-20). Feature 083 says no-data results do not consume credits unless the existing policy says otherwise.
- **R-39 (Verified). Existing tests to extend or keep green.**
  - Backend unit: `tests/FinancialCopilot.UnitTests/`, including `FinancialStatementTable083Tests.cs`, `QuarterlyFinancialReports144Tests.cs`, `QuarterlyFinancialReports144ResolutionTests.cs`, `Feature150SharedChatRoutingTests.cs` and `CyclicalWavesV2/CyclicalWavesV2ChatReadTests.cs`.
  - Backend integration: `tests/FinancialCopilot.IntegrationTests/`, including `AiFacadeV2EndpointTests.cs` and `QuarterlyFinancialReports144PostgreSqlTests.cs`.
  - Frontend: `components/app/__tests__/quarterly-financial-table*.test.ts(x)` and `components/mobile/answers/__tests__/`.
  - Browser and PNG scripts: `src/frontend/scripts/quarterly-financial-*.browser.test.mjs`.

## 7. Fixture arithmetic (computed for this spec)

These figures were computed from the supplied amounts with exact decimals and half-away-from-zero rounding to two places.

| Fixture | Equity / Assets × 100 (unrounded) | Displayed | Liabilities + Equity = Total assets? | Reported L&E (148) = Total assets (138)? |
|---|---|---:|---|---|
| وبملت | 10.965457637174786… | 10.97% | 52,628,589,262 + 6,481,715,419 = 59,110,304,681 ✔ | ✔ |
| شگل | 35.433406985936675… | 35.43% | 41,014,336 + 22,508,198 = 63,522,534 ✔ | ✔ |
| فاذر | 58.753795247000950… | 58.75% | 59,948,598 + 85,394,709 = 145,343,307 ✔ | ✔ |

Calendar conversions: 2026-09-22 is 1405/06/31; 2026-06-21 is 1405/03/31; 2026-10-22 is 1405/07/30.

Synthetic comparison arithmetic used by the acceptance criteria. These values are **synthetic**, not supplied data; see [spec.md §11.2](spec.md#112-synthetic-comparison-fixtures):

| Case | Anchor | Baseline (synthetic) | Amount change | % change | Ratio change |
|---|---|---|---:|---:|---:|
| S-1 وبملت, previous report 1405/03/31 | assets 59,110,304,681; liabilities 52,628,589,262; equity 6,481,715,419 | assets 50,000,000,000; liabilities 44,523,000,000; equity 5,477,000,000 | assets +9,110,304,681; liabilities +8,105,589,262; equity +1,004,715,419 | 18.22 / 18.21 / 18.34 | 10.9654576… − 10.954 = **+0.01 pp**. Subtracting the rounded ratios would give 0.02, so this case proves the difference is taken before display rounding. |
| S-2 شگل, fiscal-year end 1404/09/30 | assets 63,522,534; liabilities 41,014,336; equity 22,508,198 | assets 60,000,000; liabilities 40,000,000; equity 20,000,000 | +3,522,534 / +1,014,336 / +2,508,198 | 5.87 / 2.54 / 12.54 | 35.4334… − 33.3333… = **+2.10 pp** |

## 8. Revision-2 findings: fiscal calendar, comparatives and aliases

- **R-40 (Verified). `PeriodStart` is derived, not issuer metadata.** The Noavaran normalizer sets `PeriodStart = FiscalYearEnd.AddYears(-1).AddDays(1)`, computed in the **Gregorian** calendar (`NadpcoApiFinancialStatementNormalizer.cs:245-259`). The provider's own fiscal-year end is kept only as `JalaliFiscalYearEnd` inside `WarningsJson` (`BuildEvidenceJson`, L262-292). `StatementMetadata.Parse` already reads it (`FinancialStatementTableServices.cs:327-398`).
- **R-41 (Verified). Verified fiscal-start rule.** Feature 144 resolves the fiscal start from persisted `JalaliFiscalYearEnd` evidence:
  - The rule lives in `ResolveFiscalStart` (`QuarterlyFinancialReportsNormalizer.cs:526-537`).
  - An Esfand year end gives month 1 of the same Jalali year. Any other month gives the following month of the previous year.
  - `PeriodStart` is used only as a fallback.
  - Transitional fiscal years are rebased in `ResolveEffectiveFiscalStarts` (L545-567).
  - Period ends are validated with Jalali month arithmetic (`JalaliEndAfterMonths`, L522-523).
  - The non-Esfand defect and its fix are documented in `specs/144-…/non-esfand-fiscal-calendar-investigation.md`.
  - The `Companies` catalog has no fiscal-calendar column (R-04).
- **R-42 (Verified). No embedded comparative columns are stored.**
  - Each provider record (`NadpcoApiStatementRecord`, `Providers/NadpcoApi/NadpcoApiPayloadModels.cs:97-118`) carries one `periodEnd`, one `fiscalYearEnd`, flags, and a single `amount` per `itemID`. There is no prior-period or comparative column.
  - A restated figure can exist only as a separate record with `isRepresented = true` and its own `periodEnd`.
  - The upsert key includes the flags (`NadpcoApiFinancialStatementNormalizer.cs:49-56`; unique index R-04), so one `ExternalStatementId` may hold both a restated and an original variant.
  - **No stored field links a restated record to the later report that published it** (Inference from the record shape). Whether such records exist for balance sheets, and how they could be identified, is evidence gap E-2.
- **R-43 (Verified). Ingestion-time variant preference differs.** `docs/nadpco-api-provider.md:104-106` describes the canonical selection used for derived metrics: audited first, then **non-represented** preferred, then composing preferred. Feature 144's report ranking prefers represented, not non-represented. Feature 154 follows the Product Owner's choice of the Feature 144 report ranking (PD-09). The metric-ingestion rule is unchanged and not reused.
- **R-44 (Verified). Feature 083 has no date filter.**
  - `FinancialStatementTableQuery` (`Application/FinancialData/Ingestion/FinancialStatementTableContracts.cs:12-22`) carries `PeriodMonths`, `IsAudited`, `IsRepresented` and `IsComposing`.
  - `EndingPeriod` is used only by the quarterly table.
  - Feature 083 therefore **cannot** serve "one specified historical statement by date". It would return the latest match. The Product Owner closed D-12 by routing single-date requests to Feature 154 (spec PD-20).
- **R-45 (Verified). Alias coverage today.**
  - `BalanceAliases` lacks «وضعیت مالی» (bare), «بیلان», and the English "balance sheet" / "statement of financial position" (`FinancialStatementTableIntentRules.cs:83-90`). «صورت دارایی‌ها و بدهی‌ها» already matches through the «دارایی‌ها و بدهی‌ها» substring.
  - "balance sheet" appears only in `DisclosureListingIntentRules.cs:11`.
  - `IncomeAliases` (L65-74) already match «گزارش سود و زیان» (through «سود و زیان»), «صورت درآمد و هزینه» (through «صورت درآمد») and «صورت عملکرد مالی» (through «صورت عملکرد»). They lack bare «عملکرد مالی».
  - English "income statement", "profit and loss" and "profit & loss" appear only in the quarterly detector (L19-22). "P&L", "statement of profit or loss" and "statement of operations" are absent.
- **R-46 (Verified). Income-statement aliases already reach two different reports.** `QuarterlyReportAliases` («صورت سود و زیان», «سود و زیان», «گزارش فصلی», «گزارش عملکرد») open the Feature 144 quarterly table. `IncomeAliases` that are not quarterly aliases («صورت درآمد», «صورت عملکرد», «درآمدها و هزینه‌ها») fall to `LooksLikeFinancialStatementTableQuery`, which opens the Feature 083 latest-statement table (L177-191) (Inference: routing traced in code, not executed). Today «صورت عملکرد مالی X» opens 083 and bare «عملکرد مالی X» has no rule. The Product Owner closed D-13 (spec PD-21): both phrases follow the «صورت سود و زیان» route (Feature 144). In code this means adding them where «صورت سود و زیان» is recognized, in `QuarterlyReportAliases` and `IncomeAliases`, so that the quarterly detector claims them before the 083 table predicate does.
- **R-47 (Verified). Feature 144 change-column wording.** The Feature 144 change header is «درصد تغییر به فصل قبل» (`components/app/quarterly-financial-table-constants.ts`). That wording must not be reused for balance-sheet comparisons (PD-06).
- **R-48 (Verified). No verified current/non-current hierarchy.** `specs/141-…/artifacts/navigation-hierarchy.md` places every balance-sheet source item, subtotals included (for example L94 and L133), directly under `source-items-balancesheet`, with navigation review `pending-review`. There is no current or non-current parent node. Together with R-12 (the catalog has no hierarchy column), the only verifiable placement basis in the repository is a source label that itself states maturity: 47 «تسهیلات مالی دریافتی کوتاه مدت» and 51 «… بلند مدت». The reported subtotals 135, 137, 139 and 140 are placed by their own definition. Every other General row stays at section level (spec PD-22, E-8).
- **R-49 (Verified). Feature 144 chart setting.** `QuarterlyFinancialTable:ShowChart` gates the General income-statement chart on screen and in the PNG. The banking chart is shown regardless (`specs/144-…/banking/spec.md`, "Chart and presentation"; R-37). Feature 154 does not read this setting, so its behavior for Feature 144 is unchanged (spec §10).

### Evidence gaps

Each gap names the evidence required to close it, the acceptance criteria it gates, and the task that collects it (T01, read-only). No gap is closed by matching sample values alone.

| ID | Gap | Evidence required | Until closed | Affected ACs |
|---|---|---|---|---|
| E-1 | Semantic equivalence of the fallback candidates: 298–305 for 135–148, 293 for 306, 308 for 307 | (1) Provider documentation or a provider-supplied definition that each «- کدال» item is the Codal-presentation label of the **same reported concept** as its primary, and that 293/308 are the same concept as 306/307 rather than a broader aggregate. (2) Read-only Development inventory over at least the three fixture companies plus additional banks and non-banks, recording per statement whether both items exist, whether either exists alone, and where they disagree. (3) Sign-off by a financial analyst. | Only the affected pair's **fallback** is disabled. A present primary is always used. A missing primary shows «—» (a chart gap), and the unverified candidate is recorded only in provenance. Each pair is verified independently. | AC-M09, AC-G02 |
| E-2 | Storage and linkage of restated comparatives (embedded baseline) | Read-only inventory for the fixture companies and several restating issuers: BalanceSheet records with `IsRepresented = true`, their `PeriodEnd`, `ExternalStatementId`, `PublishedAt`, and the matching original record. Also a provider statement of what `isRepresented` and `statementID` mean for comparative columns. The goal is to find a **deterministic** link from an anchor report to the restated baseline it published. | Nothing in v1 is disabled. v1 always uses the exact-date stored snapshot, with its own restatement label. Two things wait for this evidence: any wording that ties the baseline to the anchor report, and the post-v1 preference for an embedded restated comparative. | AC-C08 (post-v1 part) |
| E-3 | Fiscal-calendar evidence coverage | Read-only check that `JalaliFiscalYearEnd` is present and parseable on BalanceSheet records for 452, 167 and 290. Expected values: 1405/12/29, 1405/09/30, and 1405/12/29 (expected, not verified). Also check that prior-year records carry the prior fiscal-year end, and find at least one transitional-year issuer. | Comparison ACs run on synthetic fixtures. For a live company without evidence, only `fiscal_year_end`, `previous_fiscal_quarter_end` and `same_stage_previous_year` are unavailable. `previous_report`, explicit dates and the default report are unaffected. | AC-C02, AC-C03, AC-C06, AC-C13 (live) |
| E-4 | Fixture persistence | Read-only confirmation of statement IDs, the item values supplied in spec §11.1, the GroupId of each fixture company, and statement 403733's flags and `JalaliFiscalYearEnd`. | Supplied values are used as test fixtures only. | AC-S01, AC-M01, AC-M03, AC-M04, AC-C05 |
| E-5 | Bank GroupId per environment (R-20) | The configured `QuarterlyFinancialReports:BankingIndustryGroupId` equals the `IndustryGroups.Id` of Noavaran's bank floor in each environment. | Release blocker for that environment. | AC-M01 |
| E-6 | Consolidated availability for the no-standalone suggestion | Confirmation that the Feature 083 selection (`IsComposing = true`, same provider and company) returns a statement, checked at query time. | No suggestion is shown. | AC-S05 |
| E-7 | Two-sided comparison screenshot | **Received 2026-11-04** with the implementation request. It is saved as [evidence/reference-codal-two-sided-comparison.png](evidence/reference-codal-two-sided-comparison.png). It confirms the layout:<br>• assets on the right; liabilities and equity on the left;<br>• per side, «شرح | [report date] | پایان سال مالی قبل [date] | درصد تغییرات»;<br>• subtotal rows highlighted;<br>• final totals aligned.<br>The reference colors percent changes green and red. Feature 154 keeps the approved neutral direction coloring (spec §8.4), so that difference is intentional. | Visual sign-off of the implemented layout against this reference (T22). | AC-P03, AC-P04, AC-P11 |
| E-8 | Current/non-current classification of General rows 1 (cash), 306 (receivables), 18 (inventory), 19 (orders and prepayments), 21 (PPE), 307 (payables) and 45 (advances received) | For each row, either provider documentation or Codal-taxonomy evidence placing the item under current or non-current, or a financial analyst's sign-off citing the Codal statement layout. Separate long-term catalog items (74, 79, 50) suggest 306, 45 and 307 are current portions, but this is inference only. | Only those rows' subgroup placement. They are shown directly under their section (spec PD-22). | AC-M04 (subgroup placement) |
