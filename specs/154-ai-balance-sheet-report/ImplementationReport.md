# Feature 154 — Implementation report (revision 3, after independent review rounds 1 and 2)

- **Date:** 2026-11-04. **Branch:** `develop` at `573cfd8`, local and uncommitted. Nothing was pushed, deployed or committed.
- **Scope:** V2 only. There are no schema migrations, no new configuration keys, no provider calls and no ingestion changes. Feature 141 code is unchanged.
- **Progress log:** [evidence/implementation-progress.md](evidence/implementation-progress.md). **T01 evidence:** [evidence/T01-development-evidence.md](evidence/T01-development-evidence.md).
- **Review round 1:** all 13 findings (F154-R-01 to R-13) are addressed (§2); the reviewer closed them in round 2.
- **Review round 2:** the new Major F154-R-14 is fixed. The Minor F154-R-15 is accepted as documented debt (§6a).
- **Round 2026-11-05 (live and release readiness):**
  - Live Development verification: **17/17 PASS-live**, no product defect (§4a). Template selection in rows 1–3 is "PASS-live (classification substituted)".
  - Visual sign-off package created: sign-off **PENDING** (§4b).
  - T23 done (§5).
  - E-5 recorded per environment: Development verified; production and staging pending (§5).
- **Review round 3 (verifier):**
  - The Major F154-V-06 is fixed: per-column audit and restatement badges now appear on the PNG and on mobile (§2a).
  - The Minors V-04, V-05 and V-07 are addressed (§4a, §5).

## 1. Files

**Application: policies, contract, use case**
- `FinancialData/BalanceSheetReport/`:
  - `BalanceSheetReportContracts.cs`: contract 154.3, reader port and use-case port; adds the `too_many_report_versions` outcome.
  - `BalanceSheetRowRegistry.cs`, `BalanceSheetStatementSelector.cs`, `BalanceSheetReportBuilder.cs`
  - `BalanceSheetBaselinePolicies.cs`: resolver, `BaselineSourcePolicy` and comparison builder.
  - `BalanceSheetFormatting.cs`, `BalanceSheetReportQueryUseCase.cs`
- `FinancialData/Ingestion/FiscalCalendarPolicy.cs`, extracted from Feature 144; `QuarterlyFinancialReportsNormalizer.cs` delegates to it with unchanged behavior.
- `FinancialStatementTableContracts.cs`: query fields and the `BalanceSheetReport` carrier.

**Application: routing**
- **Shared with V1 and identical to HEAD** apart from the `partial` modifier: `FinancialStatementTableIntentRules.cs` and `FinancialStatementAnalysisIntentRules.cs`.
- **V2-only entry points:**
  - `FinancialStatementTableIntentRules.V2.cs` and `FinancialStatementAnalysisIntentRules.V2.cs`. They canonicalize the PD-02 aliases to the alias whose route they follow, then reuse the shared rules.
  - The canonical mapping is «وضعیت مالی», «بیلان» and «balance sheet» → «ترازنامه», and «عملکرد مالی» and «P&L» → «صورت سود و زیان». The «ۀ» normalization is also V2-only.
- `Feature154RoutingScope.cs`: V2-only gating (§4, deviation 1).
- `DeterministicCapabilityInterpreter.cs`, `CapabilityInterpretationGovernance.cs` (precedence and hybrid interpreter), `ConversationTaskStateContracts.cs` (dialogue gate follow-ups), `SemanticQueryFrameEnrichment.cs`, `SemanticCapabilityExecutors.cs`: all gated by the scope.
- `AiQueryOrchestrationService.cs` (V1): a single statement marks V1 execution (deviation 1). There is no behavior change.

**Infrastructure and API**
- `EfCoreBalanceSheetStatementReader.cs` and `BalanceSheetReportTextRenderer.cs`.
- `FinancialStatementTableServices.cs`: dispatch to Feature 154, and the shared `StatementMetadata` parser.
- `ServiceCollectionExtensions.cs`: DI registration and the scope, derived from the existing `AiOrchestration:Mode`.
- `FinancialCopilotWorkflowDefinition.cs`: the V2 deterministic branch uses the V2 entry points.
- `AiFacadeContracts.cs` and `AiFacadeController.cs`: HTTP pass-through.

**Frontend**
- `components/app/balance-sheet-report/*` (types, model, wide, compact, funding chart, image, container) and `png-brand.ts`.
- Modified: `quarterly-financial-table-image.ts`, `message-list.tsx`, `lib/chat.functions.ts`, `routes/_app.chat.tsx` (R-03 landing copy), `package.json`.

**Tests**
- Unit:
  - `Feature154RoutingBaselineTests.cs` with `TestData/Feature154/routing-baseline.json` (T02).
  - `BalanceSheetReport154Tests.cs`, `BalanceSheetReport154RoutingTests.cs`, `BalanceSheetReport154Fixtures.cs`.
  - `BalanceSheetReport154ReviewTests.cs`, `BalanceSheetReport154V1FreezeTests.cs`.
- Integration:
  - `BalanceSheetReport154PostgreSqlTests.cs`, `BalanceSheetReport154EndpointTests.cs`, `BalanceSheetReport154FollowUpEndpointTests.cs`, `BalanceSheetReport154V1FreezeEndpointTests.cs`.
  - `AiFacadeV2EndpointTests.cs`: the Feature 083 case is now «ترازنامه تلفیقی غالبر» (intentional re-route under PD-04).
- Frontend: `__tests__/balance-sheet-report.test.tsx`, the backend-generated fixture `__tests__/fixtures/balance-sheet-154.json`, the browser fixture, and `scripts/balance-sheet-report.browser.mjs` (`npm run test:balance-sheet-browser`).
- Live (2026-11-05): `tests/FinancialCopilot.IntegrationTests/BalanceSheetReport154LiveDevelopmentTests.cs`.
  - A `[SkippableFact]`, gated by `FEATURE154_LIVE_DEV_READONLY_CONNECTION_STRING`.
  - The factory `BalanceSheetReport154LiveApiFactory` points `FinancialIngestionDbContext` at the read-only Development role and every other context at a disposable PostgreSQL database or in-memory stores.
  - The test seam `LivePredicateClassification` stands in for classification.

**Docs and evidence (2026-11-05)**
- `specs/README.md` and `specs/implementation-checklist.md`: Feature 154 entries (T23).
- Live evidence:
  - `evidence/live-development-verification.md`, `.json` and `live-development-queries.sql`;
  - `evidence/live-development-discovery.sql` and `live-development-discovery-output.txt`.
- Release readiness:
  - `evidence/visual-signoff-P03-P04-P11.md`;
  - `evidence/E-5-bank-groupid-per-environment.md`.

## 2. Review findings

| ID | Status | Fix | Proving test |
|---|---|---|---|
| R-01 | Fixed | In V2, an explicit alias in the new message overrides the carried statement type (executor). V2 builders drop possessive forms («اش», «شان», «ی», «همین») from company hints. | `FollowUpEndpointTests.FinancialPerformancePossessive_AfterBalanceSheet…` (Feature 144 income statement for وبملت); `RoutingTests.Possessives_AreNeverCompanyHints` |
| R-02 | Fixed | Same override, with the company carried from Feature 120 state. | `FollowUpEndpointTests.BalanceSheetPossessive_AfterIncomeStatement…` (both phrases) |
| R-03 | Fixed | The V2 enricher records the report family (`;report=feature154…` / `feature144…` / `feature083…`) inside the governed statementType slot, which Feature 120 carries. The gate treats «همین … برای X» as continuing the active statement-table task, and the executor repeats the carried family. | `FollowUpEndpointTests.RepeatForAnotherCompany_RepeatsFeature154` (شگل, General template) |
| R-04 | Fixed | Inside an active Feature 154 task, a comparison or baseline cue or a date without its own alias continues the task (gate), and `ResolveBalanceSheetFollowUp` maps the cue (executor). | `FollowUpEndpointTests.ComparisonFollowUp_…` (گزارش قبلی, پایان سال مالی قبل, فصل قبل, مدت مشابه سال قبل, a date) |
| R-05 | Fixed | Shared rule files are restored to HEAD; all new aliases and the normalization are V2-only. The interpreter, hybrid, gate, enricher and executor return HEAD behavior while V1 executes. | `Feature154RoutingBaselineTests.V1ReachableMethods_AreUnchangedForEveryPhrase` (46 phrases against the HEAD capture); `V1FreezeTests` (the 10 reviewer phrases through the methods called at `LlmAiIntentDetector` :81/:89, plus `BuildQuery`); `V1FreezeEndpointTests` (forced-V1 endpoint, intents equal those captured on HEAD with the same factory) |
| R-06 | Fixed | The card root and the final-total tables carry explicit ink. | Browser checks of contrast in light and dark themes for final totals, ratio row and notices: ≥ 4.59:1 in the comparison view, ≥ 12.9:1 in the default view. Evidence regenerated, including `*-dark.png`. |
| R-07 | Fixed | `fiscal_year_end` uses the latest persisted year end strictly earlier than the anchor's (§8.2). In a transitional year, `same_stage_previous_year` and `previous_fiscal_quarter_end` are unavailable with `fiscal_calendar_changed` and offer `previous_report`. | `ReviewTests.TransitionalAnchorYear_…` (2 tests) |
| R-08 | Fixed | One date plus a baseline cue anchors on that date (exact match) and selects the cued baseline. | `FollowUpEndpointTests.OneDatePlusBaselineCue_AnchorsOnRequestedDate` |
| R-09 | Fixed | The message names every requested date and the actual filter, with a rationale for each filter kind. | `ReviewTests.UnsupportedDateWithVariant_NamesEveryDateAndTheFilter` (5 filters) |
| R-10 | Fixed | Hard budget of 9 line-item statements, counting peer versions, in the order anchor → baselines → chart. If the anchor's versions do not fit, the outcome is `too_many_report_versions` (fail closed). A baseline that does not fit is unavailable with that reason, and a chart date is left out. When the header window is truncated, its oldest date is excluded and re-read with all its versions. | `ReviewTests.AnchorTieGroupBeyondReadBound_FailsClosed`, `PeerVersions_NeverExceedNineLineItemStatements`, `SplitBoundaryDate_IsReReadWithAllVersions` |
| R-11 | Fixed | The PostgreSQL test counts every command on every `FinancialIngestionDbContext` of the request. | `BalanceSheetReport154PostgreSqlTests`: **5 total** (company resolution 1, classification 1, reader headers 2, line items 1). Line items for 5 statements, 5 snapshots, 5 chart points, 0 commands on replay. |
| R-12 | Fixed | On compact layouts the dates render as two lines (month/day over year), with a compact amount axis. | Browser check of non-overlapping tick boxes at 320, 360 and 390 px; mobile evidence regenerated. |
| R-13 | Fixed | New tests for each item. | AC-P09: vitest «ShowChart true/false» (the Feature 154 chart always shows and the UI config is never fetched); the Feature 144 General behavior stays covered by `quarterly-financial-chart.test.tsx` «shows the chart only when the API effective setting is true». G04, G05, G08: vitest data tests plus browser checks (labelled single point, ratio line broken into 2 segments, conflict note). X02: `FollowUpEndpointTests.Unauthenticated_IsRejected` (401). P06: browser Enter and Space toggle `aria-expanded`. Endpoint: `LatestRequest_IsFeature154_VariantRequest_IsFeature083`. |

| R-14 (round 2) | Fixed | The R-04 continuation was too broad: any later message with a comparison or baseline cue or a date was forced into the statement-table task. Now `LooksLikeBalanceSheetFollowUpCue` accepts only bare follow-ups: after the cue, the dates and glue words are removed, nothing with its own subject may remain (metric, product, period, company). The gate's «همین … برای X» branch requires no other detected capability, or the statement-table capability itself. The executor repeats a carried Feature 154 or 144 report only for those same bare forms; every other carried-over message behaves as with an active Feature 083 task. | `FollowUpEndpointTests.MessageWithOwnSubject_IsNotForcedIntoTheActiveBalanceSheetTask`, covering the 4 reviewer reproductions: «فروش ماهانه وبملت را با سال قبل مقایسه کن», «روند فروش وبملت در مقایسه با دوره قبل», «فروش وبملت در ۱۴۰۴/۱۲/۲۹ چقدر بود», «قیمت وبملت نسبت به هفته قبل». Each keeps the same intent as a fresh conversation and returns no Feature 154 payload. `PreservedT02Phrases_RouteUnchangedInsideActiveBalanceSheetTask` (all 28 preserved T02 phrases, with an active Feature 154 task versus an active Feature 083 task: same intent, same payload type). The bare follow-ups still pass: `ComparisonFollowUp_…` (including «با ۱۴۰۴/۱۲/۲۹ مقایسه کن») and `RepeatForAnotherCompany_RepeatsFeature154`. Unit: `RoutingTests.OnlyBareCuesContinueTheActiveReport` and `T02Phrases_NeverTriggerTheFollowUpOverride`. |

## 2a. Review round 3 (verifier) findings

| ID | Status | Fix | Proving test / evidence |
|---|---|---|---|
| **F154-V-06** (Major; AC-C08, AC-C11, §8.3 item 1, §8.5, AC-P11, §9.4) | Fixed | One owner for a dated balance's status text: `statementBadgeText` in `balance-sheet-report-model.ts`, which gives the audit status plus «تجدید ارائه‌شده» exactly when `IsRepresented`. Wide, compact and PNG all use it.<br>**PNG:** the comparison date columns gain a second header line with that balance's badge, via the pure `balanceSheetPngColumnHeaders`; the comparison header height goes from 48 to 64 px. The single-date header metadata already lists the anchor's status and restatement.<br>**Compact:** in comparison, the «مانده در [تاریخ گزارش]» strip carries the anchor badge, and a new baseline line «مانده در [تاریخ مبنا] (مبنا)» carries the baseline badge. The default header already lists the anchor status. | vitest: 3 new tests (compact restated baseline, compact restated anchor, PNG column headers including the restated baseline; 18/18).<br>Browser: 4 new checks (24/24): PNG date-column badges, restated-baseline PNG and wide header, restated-anchor single-date PNG, mobile anchor/baseline badges with a restated baseline at 320 px.<br>New images: `png-desktop-comparison-restated-baseline.png`, `png-desktop-default-restated-anchor.png`, `mobile-320-comparison-restated-baseline.png`. |
| F154-V-04 (Minor) | Fixed | Template selection in live rows 1–3 is labelled "PASS-live (classification substituted)" in the JSON (`templateSelection`), the live md and §4a. It adds no new Development gate (same predicate + FK method as E-5 Development); E-5 elsewhere must query the real view. | `live-development-verification.md` §3 and §5 |
| F154-V-05 (Minor) | Fixed | Five live rows were added on ساربیل (236) and منجیل (15763), moving these clauses from fixture-only to live:<br>• AC-C03 "not the previous report";<br>• AC-C06 exact 9M dates;<br>• AC-C06 missing match with an unchanged calendar;<br>• a 9M S-9 shape.<br>The verifier's figures are confirmed: +3,886,750 / pp −5.141, +9,009,478, +5,671,570. | `live-development-verification.md` rows 13–17 |
| F154-V-07 (Minor) | Fixed | The E-5 record names the `docker-compose.yml:71` override `QuarterlyFinancialReports__BankingIndustryGroupId`, which reads the server `.env` (with the same default, also in `.env.example:207`), and requires checking that effective value.<br>It notes that production runs `ApplyMigrationsOnStartup=true`, so its view is likely the 3-MarketId migration version, and that the check must use the deployed view.<br>It lists staging as documented by spec 139, with the owner to confirm it exists. | `evidence/E-5-bank-groupid-per-environment.md` |
| F154-V-08 (Minor; round 4; AC-P10, §9.5) | Fixed | `png-desktop-default-restated-anchor.png` had been drawn in a fallback font: canvas text does not wait for web fonts.<br>The Feature 154 export (`downloadBalanceSheetReportImage`) now first awaits `ensureBalanceSheetPngFonts`. That function calls `document.fonts.load` for each Vazirmatn weight the canvas uses (400/600/700/800), with a sample covering both the Arabic and the Latin unicode-range subsets, then awaits `document.fonts.ready`. It is bounded at 3 s, never throws, and falls back gracefully on timeout, error or a missing FontFaceSet.<br>The browser script also waits and asserts the faces before every export.<br>Feature 144's exporter and the shared `png-brand.ts` are **unchanged** (see §7). | vitest: 3 new tests with a stubbed `document.fonts` (loads every weight and awaits `ready`; bounded timeout, rejection or no FontFaceSet → false; export renders only after the fonts resolve). Total 21/21.<br>Browser 25/25, including "PNG fonts: Vazirmatn loaded before all 6 PNG exports". By inspection, all 6 PNGs in `evidence/browser/` render in Vazirmatn. The desktop and mobile comparison PNGs are byte-identical again; a first attempt with an Arabic-only sample left the Latin-subset glyphs («—», «()», «·») unloaded on one page. |
| Also | Done | The live test no longer rewrites the checked-in JSON by default. It writes to `%TEMP%/feature154-live-development-verification.json` unless `FEATURE154_LIVE_DEV_WRITE_EVIDENCE=1`. The T23 Completion Log row is updated. | A subset run left the evidence JSON byte-identical (md5 checked) |

The reviewer's probe reproductions are now regression tests in `BalanceSheetReport154FollowUpEndpointTests`, `BalanceSheetReport154V1Freeze*` and `BalanceSheetReport154ReviewTests`.

## 3. Test results

Baselines were captured before any change. The integration baseline ran on the clean HEAD worktree `D:/f154-baseline`.

| Suite | Command | Result | New vs pre-existing |
|---|---|---|---|
| Unit, Feature 154 | `dotnet test tests/FinancialCopilot.UnitTests --filter "FullyQualifiedName~154"` | 123 passed, 0 failed | — |
| Unit, full: includes Feature 083/144/150, dialogue gate, task state and frame enrichment (`SemanticRouteExecutorTests`) | `dotnet test tests/FinancialCopilot.UnitTests` | 2961 passed, 27 failed, 2988 total | 0 new. The 27 failures equal the baseline set and include `Feature150SharedChatRoutingTests.NetProfitMetricWords_AreNotExtractedAsCompanyEntities`, which also fails on HEAD. |
| Integration, Feature 154: follow-ups (including R-14 and 28 T02 phrases), V1 freeze, PostgreSQL, endpoint | `--filter FullyQualifiedName~BalanceSheetReport154` | 60 passed, 0 failed. The PostgreSQL test runs; it is not skipped. | — |
| Integration, full (round 2, after the R-14 fix) | no filter | 715 passed, 164 failed, 113 skipped, 992 total | 163 of the failures equal the baseline set. The 1 other is the flaky `TahlilAppTopUpPostgreSqlTests.Concurrent_invoice_identity_conflict…`: it also failed in 1 of 2 runs on clean HEAD, and passed in the previous full run. No billing code is touched, so it is classified as pre-existing flaky. |
| Frontend | `npx vitest run --maxWorkers=3 --exclude "scripts/**"` | 404 passed, 2 failed, 49 files | Both failures are pre-existing (`markdown-message` quarterly 16 rows, and the `monthly-product-trend-chart` failed message). |
| Frontend types | `npx tsc --noEmit` | No errors in Feature 154 files | Pre-existing test-file errors only |
| Browser and PNG | `npm run test:balance-sheet-browser` | **25** checks passed (round 4; round 3 added 4 badge checks, round 4 the font check); all images regenerated 2026-11-05 after F154-V-08 | — |
| Frontend, Feature 154 (round 4) | `npx vitest run src/components/app/__tests__/balance-sheet-report.test.tsx` | 21 passed (3 badge tests from round 3, 3 font-loading tests from round 4) | — |
| Frontend, full (round 3) | `npx vitest run --maxWorkers=3 --exclude "scripts/**"` | 410 passed, 2 failed, 50 files | Both failures are the same pre-existing two (`markdown-message` 16 rows, `monthly-product-trend-chart` failed message). The 50th file is the owner's `admin-settings-button.test.tsx`. |
| Unit, Feature 154 (round 3) | `--filter "FullyQualifiedName~154"` | 123 passed, 0 failed | — |
| Integration, Feature 154 (round 3) | `--filter FullyQualifiedName~BalanceSheetReport154`, with the live variable set | 63 passed, 0 failed (includes the live test) | The checked-in evidence JSON stayed unchanged; that run wrote to `%TEMP%` |
| **Live Development** (2026-11-05, round 3) | `dotnet test tests/FinancialCopilot.IntegrationTests --filter "FullyQualifiedName~BalanceSheetReport154LiveDevelopmentTests"`, with the read-only connection variable and `FEATURE154_LIVE_DEV_WRITE_EVIDENCE=1` | 1 test passed; **17/17** checks **PASS-live** | Skipped (never counted as a pass) when the variable is absent |
| Feature 144 PNG/chart scripts | temporary patched copies (Vite `@` alias added) | 3 of 3 passed (round 1; no Feature 144 frontend change since) | The original scripts fail at HEAD (missing `@` alias), which is pre-existing. |

The full integration run rewrote Feature 141 evaluation artifacts as a side effect; I restored them after each run.

**Build notes**
- From round 2 onward, Visual Studio had `FinancialCopilot.API` running (PID 44780). It was left untouched.
- Builds therefore used `dotnet build FinancialCopilot.Infrastructure` plus the test projects with `-p:BuildProjectReferences=false`. The API project itself is unchanged in rounds 2 and 3.

**Branch notes**
- During the session the owner committed 918f0a3 and 587bb62 on `develop`: chat sidebars and header, frontend only, no overlap with Feature 154 files.
- `src/frontend/src/components/app/sidebar.tsx` has uncommitted changes that are not part of this work and were left as they are.

## 4. Acceptance criteria

Legend:
- **F** = fixture-verified, against PostgreSQL, unit, endpoint, vitest or browser fixtures.
- **L** = **PASS-live** on the normal V2 path against Development (2026-11-05, §4a).
- **P** = pending.

Live checks never count a fixture, unit or PostgreSQL-seeded result. Live results are listed in §4a.

| AC | Status | Notes |
|---|---|---|
| R01, R02, R03, R05–R11 | F | On both V2 paths, plus endpoint tests. |
| R04 | F | All three follow-up forms run through the endpoint with Feature 120 state (R-01, R-02, R-03). |
| R12 | F | V1 is unchanged (method-level and forced-V1 endpoint tests); CyclicalWaves returns the existing message; V1 never receives Feature 154. |
| S01 | F; **L** | 409865 is selected through (`NoavaranCurrentApi`, 452) with `CompanyId` NULL. The fixture's exact Id `cb18b9c3-…` is absent; the live Id is `4ce25b02-…` (§4a). |
| S02–S09 | F | — |
| M01 | F; L (Development GroupId evidence) | Other environments are pending. |
| M02, M05–M12 | F | M09: all fallbacks disabled (E-1). |
| M03, M04 | F; **L** | Values and placement equal the stored Development values. Template selection is **PASS-live (classification substituted)** (§4a). The exact supplied cash, deposits and 1405/06/31 values are absent (E-4). |
| Q01–Q03 | F | — |
| C01, C04, C05, C07–C12 | F | — |
| C02, C03, C06, C13 | F; **L** | Live cases include:<br>• the transitional (S-6) and S-9-shaped unavailable cases;<br>• AC-C03 "not the previous report" (ساربیل);<br>• AC-C06 exact 9M dates (ساربیل) and missing match (منجیل).<br>Only the S-2 synthetic +2.10 pp and the exact S-9 dates are fixture-only (§4a). |
| P01, P02, P05–P10, P12, P13 | F | P13 covers renderer text; there is no Telegram adapter test (Telegram uses the unchanged `TextAnswer` path). |
| P03, P04, P11 | F; P (E-7) | The sign-off package is ready (§4b). The human sign-off is **PENDING**. |
| G01–G08 | F | — |
| X01–X04 | F | X04: 5 total database round trips. |

## 4a. Live Development results (2026-11-05)

**Full record:** [evidence/live-development-verification.md](evidence/live-development-verification.md), with `.json`, `live-development-queries.sql` and the discovery files.

**Method:**
- The normal V2 endpoint path (`POST /api/ai/v1/query`, `MicrosoftAgentFrameworkV2`).
- `FinancialIngestionDbContext` uses the read-only Development role `feature141_app_reader` with `default_transaction_read_only=on`.
- Auth, SemanticCatalog and FinancialProvider contexts use a disposable PostgreSQL database; billing, conversation and memory are in-memory.
- Expected values come from independent SQL.

**Safety:** a write through the app context was rejected with **25006**, and the session showed read-only = `on`. No grant, no owner account and no Development write.

**Substitution (not live):**
- The role has no SELECT on `NoavaranEligibleCompanies`, `IndustryGroups` or `Industries`.
- Classification therefore reads `Companies` with the deployed view's predicate: 4 MarketIds, and non-null GroupId/IndustryId backed by foreign keys.
- Template selection in rows 1–3 is labelled **PASS-live (classification substituted)** (F154-V-04).
- This adds no new Development gate: it is the same method that verified E-5 for Development. E-5 in any other environment must query that environment's real deployed view.
- Everything else read live data through production code.

| # | AC | Request | Company | Anchor → baseline (statement IDs) | Key expected = actual | Result |
|---|---|---|---|---|---|---|
| 1 | S01 + M03 | «ترازنامه وبملت» | 452 | 409865 1405/06/31 6M `4ce25b02` | 16 rows = stored; assets 59,110,304,681; ratio 10.9655%. Template Banking: **PASS-live (classification substituted)** | PASS-live |
| 2 | M04 | «ترازنامه شگل» | 167 | 388474 1404/12/29 3M `edaa0f04` | 20 rows = stored; capital 6,149,600; PD-22 placement; ratio 30.4427%. Template General: **PASS-live (classification substituted)** | PASS-live |
| 3 | M04 | «ترازنامه فاذر» | 290 | 403362 1405/03/31 3M `4cc567ec` | 23 rows = stored; capital 18,126,475; PD-22 placement; ratio 63.4400%. Template General: **PASS-live (classification substituted)** | PASS-live |
| 4 | C02 | «ترازنامه وبملت را با پایان سال مالی قبل مقایسه کن» | 452 | 409865 → 403749 1404/12/29 12M audited `f797c618` | Audited wins over the same-date 394247; changes and percent = stored; pp −0.7327 | PASS-live |
| 5 | C03 | «ترازنامه شگل را با پایان سال مالی قبل مقایسه کن» | 167 | 388474 → 387608 **1404/09/30** `6e377554` | Non-Esfand year end from evidence; pp −4.4564 | PASS-live |
| 6 | C06 | «ترازنامه وبملت در مقایسه با مدت مشابه سال قبل» | 452 | 409865 → 384526 1404/06/31 6M `88c595cd` | pp −4.7097 | PASS-live |
| 7 | C06 | «ترازنامه شگل در مقایسه با مدت مشابه سال قبل» | 167 | 388474 → 373612 1403/12/30 3M `975cd431` | pp +8.9750 | PASS-live |
| 8 | C06 (S-6) | «ترازنامه بکهنوج در مقایسه با مدت مشابه سال قبل» | 13097 | 387517 → unavailable `fiscal_calendar_changed` | Alternatives offered, none selected | PASS-live |
| 9 | C13 | «ترازنامه وبملت را با فصل قبل مقایسه کن» | 452 | 409865 → 403733 1405/03/31 3M `d0855f5e` | «پایان فصل قبل»; pp −0.2897 | PASS-live |
| 10 | C13 | «ترازنامه شگل را با فصل قبل مقایسه کن» | 167 | 388474 (3M) → 387608 1404/09/30 | A 3M anchor resolves to the previous fiscal-year end | PASS-live |
| 11 | C13 (transitional) | «ترازنامه بکهنوج را با فصل قبل مقایسه کن» | 13097 | 387517 → unavailable `fiscal_calendar_changed` | «گزارش قبلی (۱۴۰۴/۰۷/۳۰)» offered, not selected | PASS-live |
| 12 | C13 (S-9 shape) | «ترازنامه ومعین را با فصل قبل مقایسه کن» | 12548 | 403340 → unavailable `no_snapshot_at_date` (1404/12/29) | Names «پایان فصل قبل (۱۴۰۴/۱۲/۲۹)»; offers «گزارش قبلی (۱۴۰۴/۰۹/۳۰)» | PASS-live |
| 13 | C03 (not the previous report) | «ترازنامه ساربیل را با پایان سال مالی قبل مقایسه کن» | 236 | 409030 9M 1405/06/31 → 387010 **1404/09/30** audited `2d815cfe` | Previous report is 388181 at 1404/12/29 (a different statement); TA +3,886,750; pp −5.1410 | PASS-live |
| 14 | C03 (contrast) | «ترازنامه ساربیل را با گزارش قبلی مقایسه کن» | 236 | 409030 → 388181 1404/12/29 3M `a2d357e9` | TA +5,671,570; pp +28.9404 | PASS-live |
| 15 | C06 (exact dates) | «ترازنامه ساربیل در مقایسه با مدت مشابه سال قبل» | 236 | 409030 9M 1405/06/31 → 382074 **9M 1404/06/31** `f1fb48ef` | TA +9,009,478; pp −0.3583 | PASS-live |
| 16 | C13 (S-9 shape, 9M) | «ترازنامه ساربیل را با فصل قبل مقایسه کن» | 236 | 409030 → unavailable `no_snapshot_at_date` (1405/03/31) | Offers «گزارش قبلی (۱۴۰۴/۱۲/۲۹)», not selected | PASS-live |
| 17 | C06 (missing match) | «ترازنامه منجیل در مقایسه با مدت مشابه سال قبل» | 15763 | 409088 9M 1405/06/31 → unavailable `no_matching_stage` (1404/06/31); calendar unchanged | Alternatives offered, none selected | PASS-live |

**Defects:** none found in the product.

**Test-expectation correction:** one test expectation required `previous_report` to be the *first* alternative. Spec §8.2 does not order alternatives, so the check now requires it to be present. Rerun: passed.

**Exact fixture absent (E-4; kept separate; detail in the evidence §7).** These remain fixture-only:
- AC-S01: the surrogate Id `cb18b9c3-…`.
- AC-M03: the supplied cash 409,287,621 (live 3,536,996,227) and deposits 27,590,264,477 (live 27,590,264,476). Loans, capital and treasury match.
- AC-M04: the supplied 1405/06/31 values.
- AC-C03: the S-2 synthetic +2.10 pp.
- AC-C13: the exact S-9 dates.

The clauses AC-C03 "not the previous report", AC-C06 exact 9M dates and AC-C06 missing match are now **live** (rows 13, 15, 17).

## 4b. Visual sign-off package (E-7: AC-P03, AC-P04, AC-P11)

**Package:** [evidence/visual-signoff-P03-P04-P11.md](evidence/visual-signoff-P03-P04-P11.md). Status **PENDING** (owner —, date —).

**Images**, regenerated 2026-11-05 and newer than every Feature 154 frontend source:
- `browser/desktop-1280-default(-dark).png`
- `browser/desktop-1440-comparison(-dark).png`
- `browser/desktop-1440-comparison-previous-report.png`
- `browser/desktop-900-comparison-stacked.png`
- `browser/mobile-320-comparison.png` (also 360 and 390)
- `browser/png-desktop-default.png`, `browser/png-desktop-comparison.png`, `browser/png-mobile-comparison.png`
- the reference `reference-codal-two-sided-comparison.png`

**The package documents:**
- a per-AC checklist from spec §9.2 and §9.5;
- the V-03 axis notes;
- the two-line mobile date ticks;
- the intentional differences: neutral direction colors, «مانده در [تاریخ]» labels, a single full-width yellow ratio row, selected items only, and the chart;
- three observations for the owner to decide: the pink negative-cell background, PNG without ▲/▼ glyphs, and retained earnings shown as «—» in the fixture;
- round 3 (F154-V-06): badge checklist items for AC-P11 and mobile, and new images `png-desktop-comparison-restated-baseline.png`, `png-desktop-default-restated-anchor.png` and `mobile-320-comparison-restated-baseline.png`. Browser checks: 24/24.

## 5. T01 evidence, T23 and E-5 per environment

- **E-5 per environment** ([evidence/E-5-bank-groupid-per-environment.md](evidence/E-5-bank-groupid-per-environment.md)):

  | Environment | Status | Date | Method / next check |
  |---|---|---|---|
  | Development | **Verified** | 2026-11-04, re-checked 2026-11-05 | Read the deployed `pg_views` definition (4 MarketIds plus INNER JOINs, which differs from the migration; F154-V-02), then apply the live predicate as the read-only role. 452 = configured `c96520f9-…`; 167 and 290 are non-bank; the bank GroupId has 17 rows. The live V2 runs selected Banking for 452 and General for 167, 290, 13097 and 12548. |
  | Production (`sapioai.ir`; `deploy-prod.yml` → `ops/deploy/prod-deploy.sh`) | **PENDING** | — | In a read-only session, against the **deployed** view (likely the 3-MarketId migration version, since `ApplyMigrationsOnStartup=true`): the `pg_views` definition; one eligible row each for 452, 167 and 290; 452's GroupId = the **effective** value from the server `.env` `QuarterlyFinancialReports__BankingIndustryGroupId` (the `docker-compose.yml:71` override, default `c96520f9-…`); 167 and 290 differ. SQL is in the E-5 record. |
  | VPS `develop` deployment (`docs/deployment/deploy-develop-to-vps.md`) | **PENDING, or same as production** | — | It targets the same compose directory as production. Run the same check if the owner confirms it is a separate database. |
  | Staging (documented by spec 139: `specs/139-tahlilapp-session-exchange/StagingReadinessChecklist.md`) | **PENDING**: the owner confirms it exists | — | If it exists, run the same check against its database and its effective configuration. |
- **T23:** done.
  - Feature 154 is in `specs/README.md` under "Financial Statement Reporting", after 144.
  - `specs/implementation-checklist.md` has a Stage 23 row `[~] 154` after 083, which stays `[~]` while gates are open, and a Completion Log row.
  - The R-03 landing copy was updated (`routes/_app.chat.tsx`).
  - Terminology and Feature 083 coexistence are documented in spec §4 and §6b of this report.
  - The configuration check record is the E-5 file.
- **E-3:** verified for 452, 167 and 290.
- **E-4:** the live runs passed against stored Development values (§4a). The exact supplied fixture values and dates are absent from Development.
- **E-6:** verified.

## 5a. E-1, E-2 and E-8: approved scoped behavior (reported separately)

None of these is a defect or a live-check failure. Each is v1 behavior the spec approved while its evidence is missing. Each affects only the behavior listed in spec §14.

| Gap | v1 behavior (approved) | What closes it |
|---|---|---|
| E-1 | All alias fallbacks are disabled (AC-M09). A row is shown only from its primary source item. | Source or analyst evidence that an alias item is equivalent |
| E-2 | No anchor-link wording | Evidence for the linking rule |
| E-8 | General rows 1, 306, 18, 19, 21, 307 and 45 stay at section level (PD-22). Only 47 and 51 sit in the current and non-current subgroups. The live runs confirmed this placement for شگل and فاذر. | Source or analyst evidence for each row's current or non-current classification |

## 6a. Known architecture debt (release notes)

- **F154-R-15, accepted.** The report-family tag (`;report=feature154_balance_sheet_report` / `feature144_quarterly_report`) travels inside the governed `statementType` slot value, because the `financial_statement_table` capability's slot whitelist rejects other slot types.
  - **Consequence.** If V1 is re-enabled in the middle of a conversation, the frozen V1 executor parses a carried tagged slot as having no statement type. The exposure is bounded by the Feature 120 task-state expiry (20 minutes or 6 turns).
  - **V1 code** is not modified for this.
  - **Recommended follow-up,** before another report family is added: a typed slot, or a registry whitelist entry for a dedicated report-family slot, so the tag no longer shares the `statementType` value.

## 6b. Deliberate change to Feature 083 conversation behavior (V2)

**Spec reference:** §4.1 — "An explicit alias overrides any statement type carried in conversation context" (also design §4.6, F154-R-01).

**What changed.** Inside an active balance-sheet statement-table task (after a Feature 154 report or a Feature 083 table), an explicit statement alias in the new message now overrides the carried statement type.

| Message, inside an active balance-sheet task | HEAD 573cfd8 | Now (V2) |
|---|---|---|
| «گزارش فصلی وبملت» | The carried `BalanceSheet` won: a Feature 083 balance-sheet table | Its own route: the Feature 144 quarterly report |
| «صورت جریان وجوه نقد وبملت» | The carried `BalanceSheet` won: a Feature 083 balance-sheet table | Its own route: the cash-flow statement table |

- In both cases the new behavior is identical to a fresh conversation.
- Fresh-conversation routes are identical to HEAD for the reviewer's 20-message set (verified by the reviewer against its own HEAD build).
- V1 is unaffected (frozen-V1 scope).
- **Pinned by** `BalanceSheetReport154FollowUpEndpointTests.ExplicitAlias_InsideActiveFeature083BalanceSheetTask_FollowsItsOwnRoute`. With «همه اقلام ترازنامه وبملت» as the active Feature 083 task, each message's intent and table type equal those of a fresh conversation and never a Feature 083 balance-sheet table, and «گزارش فصلی وبملت» returns the Feature 144 quarterly result.
- `PreservedT02Phrases_RouteUnchangedInsideActiveBalanceSheetTask` compares against the current tree's Feature 083 task, so it cannot show this HEAD difference. This section documents it.

## 6c. PD-27: owner instruction of 2026-11-05 (review requests and starter cards)

**Instruction.** Recorded as PD-27 and D-14 in spec §3/§13; new criteria AC-R13 and AC-R14 in §12; routing table in §4.1–§4.3; design note in `design.md` §4.

The following must open the Feature 154 report for فاذر on both V2 paths, deterministically:
- «صورت وضعیت مالی فاذر را بررسی کن»
- «صورت وضعیت فاذر»
- «ترازنامه فاذر را بده»
- «ترازنامه فاذر؟»

The renamed starter card must behave like the old one.

**Routes before and after** (V2; captured with the scripted-model interpreter and through the normal V2 endpoint):

| Message | Before (current tree prior to PD-27) | After |
|---|---|---|
| «صورت وضعیت مالی فاذر را بررسی کن» | Deterministic: `financial_statement_period_analysis`. Hybrid: used the **LLM proposal**. Endpoint: Feature 081 analysis (`financialStatementAnalysisResult`). | `financial_statement_table`, with no model call → **Feature 154 default report**, فاذر, General |
| «صورت وضعیت فاذر» | `comprehensive_analysis` (hybrid used the model; endpoint: comprehensive analysis) | **Feature 154 default report**, فاذر, General |
| «ترازنامه فاذر را بده» | Feature 154 default report (no model call) | Unchanged |
| «ترازنامه فاذر؟» | Feature 154 default report (no model call) | Unchanged |
| Old card «گزارش سود و زیان فصلی فولاد را بررسی کن» | `financial_statement_table`, hybrid short-circuit → Feature 144 quarterly report (`quarterlyResult`) | Unchanged |
| New card «عملکرد مالی فصلی فولاد را بررسی کن» | `financial_statement_period_analysis` (hybrid used the model) → analysis, no payload | **Feature 144 quarterly report**, identical routing signature, query and semantic result to the old card |
| «تحلیل ترازنامه فاذر» | Feature 081 analysis | Unchanged |
| «تحلیل صورت سود و زیان فولاد» | Analysis | Unchanged |
| «وضعیت فاذر» | `comprehensive_analysis` | Unchanged |

**Implementation** (V2 only; V1 unchanged):

- **`FinancialStatementTableIntentRules.V2.cs`:**
  - «صورت وضعیت» → «ترازنامه» canonical alias. A guard stops it from capturing «صورت وضعیت جریان/سود/درآمد/عملکرد/تغییرات …».
  - `IsPlainReviewRequest`: «بررسی» present, no other analysis cue («تحلیل», «ارزیابی», analyze/analysis/review), and exactly one statement type named.
  - `ResolveBalanceSheetRequest` strips the review cue for such requests.
  - `LooksLikeV2QuarterlyReportTableQuery` accepts an income alias + «فصلی» + plain review.
  - `LooksLikeV2FinancialStatementTableQuery` also accepts a Feature 154 report request, so the deterministic workflow branch takes the table route.
  - Review words are removed from the company hint.
- **`DeterministicCapabilityInterpreter.cs`:** adds the V2-only evidence marker `feature154-v2-governed-report` when it recognizes a governed report table (Feature 144 quarterly or Feature 154).
- **`CapabilityInterpretationGovernance.cs` (`CapabilityRoutingPrecedence`):** prefers `financial_statement_table` when that marker is present.
  - This also fixes a latent Feature 154 defect. The earlier check compared `InterpretationEvidence.Category` against the evidence value, so it never fired. It was harmless until PD-27 because no governed report request carried an analysis cue.
  - V1 never adds the marker, so V1 precedence is unchanged.

**Route changes, exactly.**
- The T02 baseline (all 46 phrases): no phrase changed route on V2, and V1 is unchanged (`Feature154RoutingBaselineTests`: both tests pass).
- A 66-phrase before/after comparison (T02 plus the PD-27 set and boundary probes) changed exactly these 8, all intended:
  - «صورت وضعیت مالی فاذر را بررسی کن»
  - «صورت وضعیت فاذر»
  - «عملکرد مالی فصلی فولاد را بررسی کن»
  - «صورت عملکرد مالی فصلی فولاد را بررسی کن»
  - «بررسی وضعیت مالی وبملت»
  - «ترازنامه وبملت را بررسی کن»
  - «وضعیت مالی فولاد را بررسی کن»
  - «بررسی بیلان فاذر»
- A first draft also re-routed «صورت وضعیت جریان وجوه نقد فولاد» (to BalanceSheet) and «بررسی ترازنامه و سود و زیان وبملت» (to Feature 154). The statement-head guard and the one-statement rule now prevent both; they keep their previous routes.

**Superseded earlier rule.** `BalanceSheetReport154RoutingTests.AnalysisAndSingleValue_AreNotFeature154` listed «بررسی وضعیت مالی وبملت» as analysis. Under PD-27 it is a report request and was moved to the default-report theory. «تحلیل ترازنامه X», «ترازنامه X را بررسی و تحلیل کن» and «بررسی ترازنامه و سود و زیان X» stay on analysis.

**Tests:**
- **Unit** (`BalanceSheetReport154RoutingTests`):
  - 7 new default-report cases on both V2 paths, each asserting no model call;
  - `RenamedIncomeCard_FollowsTheOldCardRoute`: same routing signature, quarterly query and semantic result as the old card, and the V2 workflow branches checked before the table branch treat both alike;
  - `IncomeAnalysisAndUnscopedReview_AreNotTheQuarterlyReport`;
  - `StatementOfPositionAlias_DoesNotCaptureOtherStatements`;
  - bare «وضعیت فاذر» unchanged.
- **Unit** (`BalanceSheetReport154V1FreezeTests.Pd27_IsInvisibleToTheFrozenV1Scope`): 4 phrases. The vacuous evidence assertion in the same class was corrected to check `Value`.
- **Endpoint** (`BalanceSheetReport154Pd27EndpointTests`, normal V2 path; factory now seeds فاذر and فولاد):
  - phrases 1–4 → `balanceSheetReport` for فاذر, General, default view, available;
  - new card → same `intent` and `semanticCapabilityCode` as the old card, with `quarterlyResult` for فولاد;
  - «تحلیل ترازنامه فاذر» and «تحلیل صورت سود و زیان فولاد» → `financial_statement_period_analysis`;
  - «وضعیت فاذر» → `comprehensive_analysis`.

**Results:**

| Suite | Result |
|---|---|
| Unit, Feature 154 | 141 passed |
| Unit, full | 27 failed / 2979 passed. The failure set is identical to the previous run, compared by test name against the verifier's trx; it includes the Feature 120/128/150 routing suites. |
| Integration, Feature 154 | 70 passed, 1 skipped (the live test, without its variable) |
| Related integration suites (the verifier's 28-class set: V2 monthly sales/activity, statement analysis, symbol lookup, comprehensive analysis, product mix, Feature 144 PostgreSQL, Feature 128, Feature 148 chat E2E, …) | 28 failed / 136 passed. The failure set is identical to the verifier's earlier run: 0 new, 0 fixed. |

**Not changed by me:** the starter-card files `src/frontend/src/lib/chat/starter-prompts.ts`, `src/frontend/src/routes/_app.chat.tsx` and `starter-card.tsx` (the lead's parallel edits).

## 6. Deviations

1. **V1 isolation.**
   - The shared semantic components (interpreter, hybrid, gate, enricher, statement-table executor) also run inside the frozen V1 service. They consult `Feature154RoutingScope`, which is active only when `AiOrchestration:Mode` is MicrosoftAgentFrameworkV2 (an existing key) **and** the current flow is not frozen-V1 execution.
   - The V1 service marks its own execution with one `using` statement in `AiQueryOrchestrationService.ExecuteAsync`. This changes no V1 behavior; it is what keeps V1 unchanged when only the orchestration service is swapped (the forced-V1 test factory, or an emergency rollback that swaps the service).
   - The mechanism is an `AsyncLocal` ambient flag, which is visible and documented.
2. **Report-family marker.** It travels in the governed `statementType` slot value (`BalanceSheet;report=feature154_balance_sheet_report`), because the capability's slot whitelist rejects other slot types (`unsupported_slot`). Only the V2 enricher writes it, and the V1 executor never reads it. One consequence: if V1 is re-enabled mid-conversation, V1 would parse a carried marked slot as having no statement type.
3. **English income aliases** («P&L», "statement of profit or loss", "statement of operations") are canonicalized to «صورت سود و زیان», so they follow the same Feature 144 route as the Persian aliases. In round 1 they went to the Feature 083 latest table.
4. **`unsupported_date_with_variant`** maps to NoData with `supported_but_no_rows`.
5. **Chart dates** are the latest five on or before the anchor.
6. **Read budget.** Line items for at most 9 statements, including peer versions. If the anchor does not fit, the outcome is `too_many_report_versions`.
7. **Consolidated suggestion** is part of the outcome text.
8. **PNG scale** is fixed at 2×.
9. **Fixture subtotals** for وبملت 135/137/139/140 use Development values.
10. **Browser checks** use the Vite dev-server fixture.
11. **A comparison with one date and no baseline cue** anchors on the latest date and uses the requested date as the explicit baseline. With a baseline cue, the request anchors on the requested date (R-08).
12. **The text renderer** lives in Infrastructure, as the design specifies.
13. **Billing** is unchanged. No-data outcomes are charged like the existing deterministic statement-table branch, which is the existing policy referenced by AC-S05.

## 7. Remaining release gates

- **E-7 human visual sign-off: PENDING** ([evidence/visual-signoff-P03-P04-P11.md](evidence/visual-signoff-P03-P04-P11.md)).
- **E-5 in production: PENDING** (and in staging if the owner confirms it exists, and in the VPS `develop` deployment if separate). Each check uses that environment's deployed view and its effective `.env`/configuration value. See the E-5 record. Development is verified.
- E-1, E-2 and E-8 evidence (§5a). This is approved scoped behavior, not a blocker of the v1 behavior.
- Background on E-5 for non-Development environments, checked against **that environment's deployed** `NoavaranEligibleCompanies` definition (`pg_views`). In Development the live view differs from the migration (4 MarketIds plus INNER JOINs to `IndustryGroups` and `Industries`; F154-V-02). With the live predicate, Development still verifies: 452, 167 and 290 have one eligible row each, and the bank GroupId has 17 rows.
- **Visual sign-off notes for E-7 / AC-P11 (F154-V-03, informational):**
  - The PNG chart mirrors the on-screen axes. On screen the percent axis is on the left and the amount axis on the right; in the PNG they are reversed (amounts left, percent right).
  - PNG amount ticks are derived from the anchor/maximum stacked total divided into quarters, not from rounded values, so tick labels can be non-round numbers.
  - Neither affects any value. Both are for the human reviewer to accept or request a change.
- ~~Live runs against Development~~: **done 2026-11-05, 17/17 PASS-live** (§4a). The E-4 exact-fixture items in §4a remain fixture-only.
- ~~F154-V-06 PNG/mobile badges~~: **fixed** (§2a). The human visual sign-off still has to accept them (E-7).
- A smoke test against a running deployed API was not attempted. The live checks used the normal V2 endpoint path in the test host, with a fake AI model client.
- ~~T23~~: **done** (§5).
- **Feature 144 follow-up (pre-existing, not changed here):**
  - The Feature 144 PNG exporter (`quarterly-financial-table-image.ts`) and the shared brand helper (`png-brand.ts`) draw canvas text without waiting for web fonts.
  - An export started right after page load can therefore fall back from Vazirmatn. This is the same gap F154-V-08 fixed for Feature 154.
  - Recommended: reuse the same bounded `document.fonts.load` + `ready` wait in the Feature 144 export path.
- Outside Feature 154: the Feature 144 browser scripts need an `@` alias in their Vite server. The Feature 144 checks used temporary patched copies, which passed all 3 scripts.
- Environment left for reviewers: the worktree `D:/f154-baseline` and the disposable PostgreSQL on 127.0.0.1:55441.
