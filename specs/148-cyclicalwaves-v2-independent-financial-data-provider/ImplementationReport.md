# Feature 148 — Implementation Report

**Date:** 2026-10-31 · **Branch:** `develop` · **Commit:** split locally from `39d885d` into an independent Feature 148 commit (git history separation, §17); not pushed · **Status:** IMPLEMENTED_WITH_KNOWN_CHAT_ROUTING_LIMITATIONS, closed (§17)

## 1. Outcome

CyclicalWaves v2 (TahlilApp API v2) is implemented as an independent provider:

- all 7 documented endpoints
- independent `cyclicalwaves` schema
- daily and manual synchronization
- a Data Management console page
- Chat provider selection with no fallback
- a basic C3 scanner

The legacy `back1.cyclicalwaves.com` integration is retired in code, schedules, triggers and configuration. C1, C2 and N1 are implemented with reliable structured provenance.

Everything was verified with automated tests, including end-to-end Chat and Telegram runs through the real request pipeline (§12). A read-only live call to the real provider also succeeded. No production deployment, production database change, data deletion, commit or push was made by this work; see §17 for the commit that the owner made.

## 2. Tasks

`tasks.md` holds **50 tasks**: the 46 originally in the file, 3 N1 tasks and T148-06.10 (C1-M).

- **Done:** 49.
- **Partial:** 1. T148-08.4: the Chat and Telegram end-to-end suites run and pass, and every provider requirement (provider, ticker source, repository, values, units, no cross-provider read, no legacy call, persistence) is proven. But five of the ten acceptance questions (and Q10 partly), in their exact wording, are stopped by pre-existing shared routing defects that are identical in NoavaranAmin mode (§12.3), so their expected behavior is not met.
- **Blocked:** 0.

## 3. Files and components

| Layer | New | Changed (narrow) |
|---|---|---|
| Domain | `Financial/CyclicalWavesV2/ScreenerMetricCatalog.cs` (29 definitions, comparer), `MonetaryAmountUnits.cs` (IRR conversion, growth formula) | — |
| Application | `FinancialData/CyclicalWavesV2/*` (ports, records, `CyclicalWavesV2SyncCoordinator`), `Scanner/CyclicalWavesV2/*` (`ScreenerMetricMapping`, `MonetaryThresholdResolver`), `AI/Orchestration/ChatCapabilityAvailability.cs`, `FinancialData/Ingestion/UnavailableComprehensiveAnalysisQueryUseCase.cs` | dispatcher availability check (`SemanticCapabilityExecutionContracts.cs`), policy messages (`AiDialogueOutcomePolicy.cs`), scanner issue (`ScannerExecutionContracts.cs`, `SemanticCapabilityExecutors.cs`), `FinancialProviderErrorCode.ProviderRetired` |
| Infrastructure | `Providers/CyclicalWavesV2/*` (options, token service, auth and resilience handlers, data client and parser), `Ingestion/CyclicalWavesV2/*` (3 stores, read model, entity resolver, lookup, quarterly table, statement-table wrapper, analysis query, scanner executor), `Persistence/CyclicalWavesV2Rows.cs`, `CyclicalWavesV2Configurations.cs`, migration, `Providers/CyclicalWaves/LegacyCyclicalWavesCallBlockingHandler.cs`, `Scanner/LegacyCyclicalWavesDerivedMetricFilter.cs`, `CyclicalWavesV2ServiceCollectionExtensions.cs` | `ServiceCollectionExtensions.cs` (one call), `FinancialIngestionDbContext.cs` (`DbSet`s), C1 filter call in `EfCoreSymbolMetricLookupService.cs` and `EfCoreScannerExecutionService.cs`, N1 in `ConditionalTrackerEvaluationProcessor.cs`, legacy-path and Feature 141 gates in `FinancialCopilotWorkflowDefinition.cs`, scanner issue in `ScannerToolAdapter.cs` |
| Worker | `CyclicalWavesV2SyncWorker.cs` | `Program.cs` (3 legacy workers unhosted, v2 hosted) |
| API | `CyclicalWavesV2AdminController.cs`, `CyclicalWavesV2AdminContracts.cs`, `LegacyCyclicalWavesRetirement.cs` | `AdminDataOperationsController.cs`, `CyclicalWavesPsVisualizationAdminController.cs` (`410` triggers, retired health) |
| Frontend | `routes/admin_.data.cyclicalwaves-v2.tsx`, `components/admin/cyclicalwaves-v2-page.tsx`, its test | `data-admin-client.ts`, `data-management-shell.tsx`, `data-management-overview-page.tsx`, generated `routeTree.gen.ts` |
| Configuration | — | API/Worker `appsettings.json`, API `appsettings.Development.json`, `docker-compose.yml`, `.env.example` |
| Docs | `docs/providers/cyclicalwaves-v2/` (2 PDFs + provenance), `contract-verification.md`, `c1-census.md`, this report | 13 superseded/disabled spec headers, `specs/README.md`, root `README.md`, `implementation-checklist.md` |
| Tests | `UnitTests/CyclicalWavesV2/*` (6 suites + fixtures), `IntegrationTests/CyclicalWavesV2*` (Admin, PostgreSQL, live), `LegacyCyclicalWavesRetirementEndpointTests.cs` | architecture tests, N1 tests in `ConditionalTrackerEndpointTests.cs`, dispatcher tests, provider-health expectation |

**NoavaranAmin:** no ingestion, persistence, repository, catalog, formula or `CompanyResolverService` change. The only NoavaranAmin edits are the read-boundary filters C1/C1-M and N1 (including the professional scanner's availability check) and the C2 registration.

## 4. Database migration

`20261031045622_Feature148CyclicalWavesV2Foundation` (`FinancialIngestionDbContext`) is additive:

- It runs `EnsureSchema("cyclicalwaves")` and creates 11 tables: `Tickers`, `MetricDefinitions` (29 seeded rows, English and Persian names), `FinancialValues`, `ScreenerMetadata`, `IncomeStatementLineDefinitions`, `IncomeStatements`, `IncomeStatementValues`, `Analyses`, `AnalysisCategories`, `AnalysisTags`, `SyncRuns`.
- Amounts are `numeric(38,6)` and nulls are preserved.
- It adds unique keys and a partial unique index (`SyncRuns.Status = 'Running'`), and every FK stays inside the schema.
- `Down` drops the 11 tables and then the empty schema (`DROP SCHEMA IF EXISTS`, non-cascading).
- The model snapshot diff touches only v2 entities.
- Verified on disposable PG18 (port 55441): `Up`, then `Down` to the previous migration (schema gone, `public` intact), then `Up`. No FK crosses the schema, and a Full run leaves the `public` row counts unchanged.

Deviation: the `FiscalMonth` check is `BETWEEN 1 AND 12` rather than `IN (3,6,9,12)`, because live statements carry fiscal-year-change fields.

## 5. Provider integration

`CyclicalWavesV2TokenService` is process-local and single-flight:

- It sends `x-client-id: default` on every auth request.
- It reads the JWT `exp` claim, with a 4-minute fallback (live tokens last 900 s), and renews `RefreshSkewSeconds` (60) before expiry.
- It stores the rotated refresh token before returning a new access token.
- A rejected refresh triggers one login. A failed login raises `Unauthorized` with no retry loop.
- Tokens and credentials are never logged.

`CyclicalWavesV2AuthHandler` handles a 401 with one renewal and one replay. `CyclicalWavesV2ResilienceHandler` adds a per-attempt timeout and bounded, jittered retries on 5xx, 408 and network errors; other 4xx responses are never retried. Redirects are off.

`CyclicalWavesV2DataClient` covers tickers, screener (`404 → null`), statements (`404 → null`, `400 → InvalidResponse`), lines and blogs. Blogs always send `filter[type]=analysis` with encoded brackets and an optional `filter[from_date]`.

The parser ignores unknown fields, keeps nulls, and reads `YYYYMMDD` strings and integers, UTC `published_at`, and Tehran-local `publishDate`.

The live contract was verified on 2026-10-31 (see `contract-verification.md`). This resolves B1, B2 and B3:

- `/lines` is a bare array of 82 items.
- Analysis categories are authors, and topics are underscore tags.
- Articles link to tickers by tag or by leading title segment.
- `from_date` uses `YYYY-MM-DD`.

## 6. Background synchronization

`CyclicalWavesV2SyncCoordinator` (Application) runs the Catalog, Ticker, Screener, IncomeStatements, Analysis (Full or Incremental) and Full scopes:

- It takes a global lease. A run that finds the lease held is recorded as `SkippedAlreadyRunning`.
- A failed item is counted and the run continues. Authentication failure, an unavailable catalog or an empty catalog ends the run as `Failed`; an empty catalog is never applied, so it cannot deactivate everything.
- Calls are paced and the lease is renewed periodically.
- The error summary holds ticker and error category only, up to 4 KB.

Storage behavior:

- The screener is latest-state: only changed values are written, and no daily snapshots are kept.
- Statements are upserted by reported period. The latest publication wins, unknown line keys create their own definition, and no period is ever deleted.
- Articles are upserted by provider id. An unchanged hash causes no write; a changed article replaces its tags and categories. No article is ever deleted.
- Incremental analysis starts from the Tehran date of the newest stored article minus 2 days.

`CyclicalWavesV2SyncWorker` is always hosted and gated by `IngestionEnabled`:

- It runs a startup catch-up when the last completed Full run is older than the previous scheduled occurrence.
- It runs Full at `DailySyncCronUtc` (default `30 23 * * *`).
- Every 30 s it expires stale leases and claims queued Admin runs.
- `ChatProvider` has no effect on it.

## 7. Admin operations

Routes are under `api/v1/admin/cyclicalwaves-v2` (DataAdmin policy, AuthenticatedActor rate limit):

- `POST catalog/sync` and `POST tickers/{ticker}/sync` run inline.
- `POST screener/sync`, `income-statements/sync`, `analysis/sync?mode=incremental|full` and `sync` are queued: `202 {runId}`, or `409 {existingRunId}`.
- When ingestion is off, these return `409 IngestionDisabled`.
- `GET status?recent=` returns the flags, cron, next UTC run, whether credentials are configured, the Chat provider, the active run, the last success per scope, and recent runs with their errors.

The frontend page `/admin/data/cyclicalwaves-v2` has:

- six actions plus a ticker input
- a status header
- a recent-runs table with errors
- 5-second polling while a run is active; buttons are disabled meanwhile

The legacy overview card is relabelled «CyclicalWaves (قدیمی، متوقف‌شده)».

## 8. Chat provider selection

`FinancialData:ChatProvider` is validated at startup; it accepts `NoavaranAmin` (default) or `CyclicalWaves`. `ChatCapabilityAvailability` is the single owner of the capability matrix:

| Route | NoavaranAmin | CyclicalWaves |
|---|---|---|
| `symbol_lookup`, `scanner` | existing path + C1 filter | v2 lookup / v2 scanner |
| `financial_statement_table` | existing | v2 quarterly income statement; other statements unavailable |
| `comprehensive_analysis` | **unavailable (C2)**; no analysis table is readable | v2 `Analyses` |
| `ps_gauge_visualization`, `industry_relative_valuation_read` | **retired** | **retired** |
| everything else | existing | unavailable («این پرسش با دادهٔ منبع فعال (CyclicalWaves) قابل پاسخ نیست.») |

Enforcement points:

1. The semantic dispatcher, before any executor runs.
2. The legacy workflow path: a route gate before the direct branches, the analysis tool removed in NoavaranAmin mode (with a v2 tool description in CyclicalWaves mode), and the P/S gauge never attached.
3. The Feature 141 fallback and tool, which are off in CyclicalWaves mode and never used for a policy-blocked answer.
4. C2 registration: `IComprehensiveAnalysisQueryUseCase` resolves to a no-read implementation in NoavaranAmin mode. This covers the semantic executor, the agent tool and the V1 orchestrator.

Call-site inventory: `IComprehensiveAnalysisQueryUseCase` is used by `SemanticCapabilityExecutors.cs`, `ComprehensiveAnalysisToolAdapter.cs` (agent tool) and `AiQueryOrchestrationService.cs` (V1). All three are covered by item 4. `IQueryComprehensiveAnalysisUseCase` has no Chat consumer and is therefore not swapped.

CyclicalWaves-mode DI swaps:

- the entity resolver (exact ticker, then ISIN, then fuzzy candidates only; `ExternalCompanyId = cw2:{id}`; industries always NotFound)
- symbol lookup (money in million IRR, EPS in IRR, `pe = 0` shown as «قابل محاسبه نیست»; a "latest" period selector carried over from the previous turn is accepted for P/E, P/S and the 4-quarter average, fixed 2026-10-31)
- quarterly reports and statement tables
- analysis
- the scanner

`CompanyResolverService` and every NoavaranAmin registration stay as they were.

## 9. Scanner coverage (C3)

The existing parser, column policy and ranker are reused. The CyclicalWaves executor provides:

- **12 direct codes:** `PE_TTM`, `PS_TTM`, `REVENUE`, `NET_PROFIT`, `GROSS_PROFIT`, `OPERATING_PROFIT`, the 3 margins, `MONTHLY_SALES`, `AVG_12M_MONTHLY_SALES`, `AVG_4Q_REVENUE`.
- **10 growth codes:** revenue and the three profits, each QoQ and YoY, plus monthly sales MoM and YoY. They use the Domain growth formula: `(current − base) ÷ |base| × 100`, unavailable when the base is 0.
- **Feature 116 sales-growth plans:** previous-month and same-month-last-year baselines, with Percent, Positive and Multiple kinds. The baseline, growth-percent and multiple columns are filled from the same screener fields (they were empty before the 2026-10-31 fix).
- **Operators:** all six; "between" is two bound conditions.

Monetary thresholds are converted to IRR deterministically from the user's own words: ریال, تومان, هزار, میلیون, میلیارد, هزار میلیارد, همت. Persian and Arabic digits and separators are normalized. A missing or ambiguous unit asks «واحد مبلغ (ریال/تومان، میلیون/میلیارد) را مشخص کنید.»; a threshold of 0 needs no unit.

Null values never match and are counted. `pe = 0` never matches. Mixed reporting periods produce a warning.

These are unavailable, and the answer names the criterion: industry universe, EPS growth, EBIT, balance-sheet, production and quantity criteria, and the 12-month-average baseline.

Limitation: "between" was verified with a constructed two-condition plan. The live LLM parser's output for «بین ۵ و ۱۰» was not captured.

## 10. Legacy retirement

- **Guard:** `LegacyCyclicalWavesCallBlockingHandler` is inserted at index 0 on all 3 legacy typed clients: `CyclicalWavesDataProviderClient` (which also serves P/S), `CyclicalWavesDataAcquisitionClient` and `ComprehensiveAnalysisBlogClient`. It runs before the legacy auth handler, so not even the login is sent. It throws `ProviderRetired`, and it is open only when `CyclicalWavesLegacy:AllowCalls=true`. Tested for all 11 legacy endpoint paths, with zero sends.
- **Schedules:** `ComprehensiveAnalysisDailySyncWorker`, `CyclicalWavesDataAcquisitionWorker` and `IndustryRelativeValuationCalculationWorker` are unhosted; the classes are kept. `IndustryRelativeValuation:Enabled=false` (compose default `false`), and the API's `CyclicalWavesPsSync:AllowManualSyncWhenWorkerDisabled=false`.
- **Admin triggers:** `410 ProviderRetired`, with service spies at zero calls, for:
  - `cyclicalwaves/full-sync`
  - `comprehensive-analysis/full-sync` and `daily-sync`
  - `cyclicalwaves/ps-visualization/sync`, `snapshot` and `history`
  - `data-sync/*` requests that name CyclicalWaves

  `provider-health` reports the retired provider without I/O. Read-only and local endpoints are unchanged: sync-runs, both backfills, P/S reads, the dry-run.
- **Capabilities:** P/S gauge and relative valuation are retired, and `ComprehensiveAnalysisPublishedDetector` is unregistered.
- **Configuration:** legacy credentials are now optional in compose; `docker compose config` resolves with every legacy and v2 variable unset.
- **Data:** no legacy record is deleted or modified.

## 11. N1 conditional alerts

Census (read-only, Development database):

- Only `FinancialMetric` rules read `DerivedMetrics`.
- Quotes and trades come from `TsetmcWebService`.
- `FeatureSnapshots` and the alert-relevant insight events carry no CyclicalWaves provenance.
- No alert rules exist locally.

`ConditionalTrackerEvaluationProcessor` classifies the selected latest row with the same structured-provenance filter as C1. When the row is legacy-derived:

- It records `LastDecision = LegacyDataRetired` with the Persian reason «این هشدار به دادهٔ منبع قدیمی CyclicalWaves وابسته است…».
- The rule stays `Active`.
- No trigger and no notification intent are created.
- No other row or source is substituted.

All other alerts are evaluated as before. Tests show a legacy alert is skipped and kept, a NoavaranAmin alert still triggers and notifies, and the existing alert tests stay green.

## 12. Test results

Runs on this machine, 2026-10-31 (verification pass). Baseline = a clean worktree at `7c28937`, the commit before Feature 148, run the same way. PostgreSQL tests used the disposable PG18 at `127.0.0.1:55441`; Docker is unavailable here.

### 12.1 Focused and new tests

| Suite | Passed | Failed | Skipped |
|---|---|---|---|
| Unit, Feature 148 (`CyclicalWavesV2*` + dispatcher) | 197 | 0 | 0 |
| Integration, Chat and Telegram E2E (`Feature148ChatProviderE2ETests.cs`) | 28 | 0 | 0 |
| Integration, v2 Admin endpoints | 12 | 0 | 0 |
| Integration, v2 PostgreSQL (migration up/down/up, 29 seeded definitions, unchanged values not rewritten, FK isolation, one running run, Full run touches only `cyclicalwaves`) | 5 | 0 | 0 |
| Integration, live provider contract (read-only, credentials from user-secrets) | 1 | 0 | 0 |
| Integration, legacy `410` triggers and N1 alerts | 15 | 0 | 0 |
| Integration, tests updated by approved decisions (C2, retired capabilities) | 22 | 2 | 0 |
| Frontend, v2 admin page | 2 | 0 | 0 |

The 2 failures in the decision-updated group (`ShadowDefaultMode_RunsLegacyRoute…`, `…هم‌گروه چطوره؟`) fail at the baseline too.

### 12.2 Full suites against the baseline

| Suite | Current: passed / failed / skipped | Baseline `7c28937` | New failures |
|---|---|---|---|
| Unit | 2 761 / 22 / 0 | 2 581 / 21 / 0 | 0 functional, 2 load-dependent (below) |
| Integration (PostgreSQL tests skipped in both) | 624 / 66 / 204 | 572 / 67 / 198 | 0 |
| Architecture | 14 / 1 / 0 | 11 / 1 / 0 | 0 |
| Frontend `vitest run` | 198 / 2 (5 files) | — | 0 (below) |
| Frontend `npm run build` | passed | — | — |

- **Unit:** 20 failures are common to both. Two are new in the full run, `TelegramAssistantResponseRenderer089Tests.Monthly_trend_is_rendered_as_a_deterministic_png…` (PNG render took 46 s and timed out) and `TelegramChannelMonthlyReportHandlerTests.Unresolved_company_stops_before_ingestion` (`RegexMatchTimeoutException`). Both pass when run alone, twice, and neither their code nor their tests changed between `7c28937` and HEAD: load-dependent timeouts, not regressions. The baseline's extra failure (fixture-hash manifest) passes now.
- **Integration:** all 66 current failures also fail at the baseline; one baseline failure (`EveryEnabledCapability_HasExactlyOneProductionExecutor`) now passes.
- **Architecture:** the one failure (`MigratedSemanticExecutors_DoNotReintroduceLegacySymbolExtraction`) fails at the baseline too.
- **Frontend:** the failing files are `markdown-message`, `monthly-product-trend-chart` and three Node/Playwright scripts picked up by vitest (`TextEncoder` environment error). The only frontend files changed since `7c28937` are admin files, the two admin clients and the generated route tree, and none of the failing files import them. A baseline vitest run was not executed (it would need `node_modules` in the baseline worktree), so this attribution rests on the unchanged code.

### 12.3 T148-08.4 Chat acceptance questions (CyclicalWaves mode, exact wording)

| # | Question | Result | Cause when not met |
|---|---|---|---|
| 1 | فروش آخرین ماه فولاد چقدر است؟ | met: 639,418,160 million IRR, v2 only, persisted and replayed | — |
| 2 | سود خالص آخرین فصل فولاد چقدر بوده؟ | not met: clarification `entity_not_found` | «سود»/«خالص» are not router stop words |
| 3 | صورت سود و زیان فصل اخیر فولاد را نمایش بده | met: stored v2 statement, net profit 471,272,793 | — |
| 4 | روند سود خالص چهار فصل اخیر فولاد چطور بوده؟ | not met: "no data" | no statement type from this wording (same guard in both providers) |
| 5 | سهم‌هایی با P/E کمتر از ۵ را پیدا کن | not met: routed to symbol lookup | `CapabilityRoutingPrecedence` ignores «کمتر از» |
| 6 | شرکت‌هایی با P/S کمتر از ۱ | not met: routed to symbol lookup | same |
| 7 | نمادهایی با رشد فروش ماهانه بیشتر از ۲۰ درصد | not met: unavailable answer | classified as `product_sales_trend` |
| 8 | آخرین تحلیل‌های منتشرشده درباره فولاد چیست؟ | met: v2 analysis 148001 | — |
| 9 | شرکت‌های صنعت فولاد کدام‌اند؟ | met: fixed "cannot answer reliably" reply, no data read | — |
| 10 | تولید گندله کگل چقدر بوده؟ | partly met: no value invented and only v2 read, but the reply is a symbol clarification rather than "unavailable" | agent lookup cannot resolve a product metric |

Every not-met row behaves identically in NoavaranAmin mode and in the Shadow routing mode, and the routing code involved is unchanged by Feature 148. A further pre-existing defect makes all of them worse: `LlmQueryInterpretationProposalProvider.Parse` calls `TryGetInt32` on `periodCount` even when it is `null`, which throws, so every real model proposal that follows its own instruction ("use null for absent scalar fields") is discarded and routing falls back to the deterministic interpreter. Router-supported wordings prove the CyclicalWaves data path for Q2 and Q4–Q7 (see `tasks.md`, T148-08.4).

Note for test hosts: `FinancialData:ChatProvider` is read while services are composed, so a `WebApplicationFactory` must set it with `UseSetting`. Telegram test hosts need an InMemory `AuthDbContext`.

## 13. Operational verification

| Check | Status |
|---|---|
| Application build (solution, 0 warnings) | automated verified |
| Migration on disposable PostgreSQL (up/down/up, isolation, 29 definitions) | automated verified |
| Unchanged financial values not rewritten | automated verified |
| Daily schedule registration and catch-up | automated verified (Worker and DI tests) |
| Admin routes authorized (DataAdmin) | automated verified |
| Legacy HTTP calls blocked, legacy workers unregistered | automated verified (plus zero outbound HTTP in every E2E test) |
| Alerts never fire on legacy or mixed metrics | automated verified (N1 tests; mixed rows use the same C1-M rule) |
| Provider authentication and contract parsing | **live provider verified**, read-only, 2026-10-31, credentials from user-secrets |
| Telegram gateway → Chat → provider | automated verified; **live Telegram not executed** (no authorized test bot or destination; no message sent) |
| Full live ingestion, Admin-triggered live run, scheduled 23:30 UTC run | **not executed** (needs ingestion enabled in a deployed non-production environment) |
| CyclicalWaves-mode Chat with the real LLM | **not executed** |

## 14. Remaining items

- **Commit state:** the owner's local commit `39d885d` ("149", 2026-10-31 11:51, not pushed) contains both Feature 149 and Feature 148 as they stood at that time, plus 29 Feature 141 evaluation artifacts produced by test runs. Consider rewording that commit or splitting it before pushing; this pass did not rewrite history.
- **Pre-existing Chat routing defects (owner decision):** proposal parser rejects `periodCount: null`; «سود»/«خالص» not stop words; «کمتر از/بیشتر از» not thresholds in routing precedence; no statement type for "روند سود خالص …"; product metrics in the agent lookup end in a symbol clarification. They block T148-08.4's exact-wording acceptance and affect NoavaranAmin users equally. Recommended as a separate routing change with its own regression run.
- **Security, v2 credentials — rotation required:** the v2 identifier and password were removed from tracked files and now live only in user-secrets (API, Worker, integration tests) and the ignored `.env`; commit `39d885d` adds neither value. But the identifier is in git history (legacy settings, 2026-05-28 to 2026-10-18), and the password value is identical to the local PostgreSQL password committed in `src/backend/FinancialCopilot.API/appsettings.Development.json` and `.claude/settings.local.json` (allow-rules since June 2026). Rotate the v2 password and use a unique value; rotate the local database password as well.
- **Security, N2 (separate remediation, unchanged):** `src/backend/FinancialCopilot.API/appsettings.json` contains committed NoavaranCurrentApi credentials (values not reproduced). High priority: rotate and move to secrets in a separate change. NoavaranAmin authentication was not modified.
- **C1-M effect:** `AVG_12M_MONTHLY_SALES` has no valid `DerivedMetrics` row left (all 10 146 rows are legacy).
- **Product limitation L1:** v2 has no company names or industries; such questions are NotFound or unavailable in CyclicalWaves mode.
- **Product limitation:** analysis items carry only the title, the published summary and the date.
- **Cosmetic, pre-existing:** the analysis answer starts with the lookup tool's English summary ("Found metric data for 1 symbol(s).").

## 15. Configuration and deployment prerequisites

1. Deploy with the defaults:
   - `FinancialData:ChatProvider=NoavaranAmin` (the C1/C1-M exclusion is unconditional and has no setting)
   - `CyclicalWavesLegacy:AllowCalls=false`
   - `CyclicalWavesV2:IngestionEnabled=false`

   The migration is applied by the API-startup migrator. Legacy calls, schedules and triggers stop, and C1, C2 and N1 take effect.
2. Rotate the v2 password first (§14). Set `CYCLICALWAVES_V2_IDENTIFIER` and `CYCLICALWAVES_V2_PASSWORD` in the server's ignored `.env` or secret store, plus `CYCLICALWAVES_V2_INGESTION_ENABLED=true`. Then, from `/admin/data/cyclicalwaves-v2`, run in order: catalog, one ticker, analysis full, Full.
3. Set `CYCLICALWAVES_V2_DAILY_SYNC_ENABLED=true` and observe two scheduled runs.
4. Try `FINANCIAL_DATA_CHAT_PROVIDER=CyclicalWaves` in non-production only. Promote it to production only by explicit decision.
5. Rollback options:
   - `ChatProvider=NoavaranAmin`
   - `IngestionEnabled=false`
   - the migration `Down` (drops only the `cyclicalwaves` schema)

## 16. C1-M enforcement (2026-10-31)

Product Owner decision: mixed provenance is permanently excluded and the `Include` option is removed.

- `LegacyCyclicalWavesDerivedMetricFilter` is now a static rule with no options. `FinancialDataOptions` keeps only `ChatProvider`; the old settings were removed from both `appsettings.json` files, `docker-compose.yml`, `.env.example` and the README, and binding them has no effect (unit-tested).
- Excluded: exclusively legacy rows, mixed rows, provider-less rows of the four registered legacy-only calculation policies (7 887 `cw-avg-12m…` rows the earlier census missed), and unparseable evidence. Kept: pure NoavaranAmin rows (positive control in the E2E tests).
- Read paths: symbol lookup, both scanner queries, the professional scanner's availability and freshness check (previously unfiltered), and N1 alerts. Other `DerivedMetrics` reads are ingestion writers, the missing-answer feedback counter, the Noavaran backfill verifier and the Telegram channel ingestion verifier; none selects financial evidence for an answer.
- No row was modified or deleted (E2E test `HistoricalLegacyRowsAreStillStored`; census counts unchanged).

## 17. Release readiness and closure (2026-10-31)

**Closure verdict: IMPLEMENTED_WITH_KNOWN_CHAT_ROUTING_LIMITATIONS.** Product Owner decision: close Feature 148
on its verified provider implementation; general Chat routing defects are repaired in a separate feature
(`specs/150-shared-chat-routing-intent-and-entity-resolution-fixes/`), not by expanding this one. T148-08.4
remains recorded as **PARTIAL**, not DONE — five acceptance questions in their exact wording (and Q10 partly)
do not get the expected behavior, for the reasons in §12.3, and that outcome is not changed retroactively.

| Criterion | Result |
|---|---|
| C1-M permanently enforced, `Include` removed, all read paths covered | met |
| Chat E2E tests pass | met (28/28), but exact-wording acceptance incomplete (§12.3, deferred to Feature 150) |
| Telegram gateway-to-Chat automated tests pass | met (5/5; live smoke is a deployment-stage check) |
| No Feature 148 regression | met (§12.2) |
| Migration and frontend build verified | met |
| No credentials in the change set | met (secret scan clean; rotation still required, §14) |
| Change set inspected and classified | met (`commit-split-manifest.md`) |
| Production-only checks documented | met (§13) |

### Git history separation

The owner's local commit `39d885d` ("149", 2026-10-31 11:51, never pushed) carried Feature 148, Feature 149
and 29 Feature 141 evaluation artifacts together. It was split, without rewriting any published commit, into:

- **Backup of the original commit:** branch `backup/pre-feature-148-149-split` → `39d885d` (unchanged).
- **Preservation reference for the Feature 141 artifacts:** branch
  `preserved/feature141-evaluation-artifacts-20261031` → `39d885d`. No Feature 141 commit was created.
- **Feature 148 commit:** `100b3cd` ("feat(financial-copilot): integrate independent CyclicalWaves v2
  provider (#148)"), built in a temporary worktree on top of the pre-148 parent `7c28937e`, containing every
  Feature 148 file and the Feature 148-only hunks of the few files Feature 149 also touched. See
  `commit-split-manifest.md` for the file-by-file and hunk-by-hunk classification.
- **Feature 149 commit:** `f200c84` (built on top of `100b3cd`), containing Feature 149's own files plus the
  Feature 149 hunks held back from the Feature 148 commit, plus the one-line constructor-argument fixup in
  `Feature146PostgreSqlIntegrationTests.cs` that Feature 149's own signature change makes necessary.
- Diffing the combined split (`f200c84`) against the original `39d885d` shows **no difference** other than the
  29 excluded Feature 141 artifacts and the addition of `commit-split-manifest.md` itself — the split
  reproduces the intended final implementation exactly.
- Both commits build cleanly on their own (`dotnet build`, 0 warnings/0 errors, checked at `100b3cd` alone and
  at `f200c84`). Feature 148's focused unit tests (195/195) and architecture tests (14/15, the one failure
  pre-existing at the parent commit too) pass on `100b3cd` alone. Feature 149's own integration test and its
  dependency fixup pass on the combined tree (10 passed, 6 skipped for the unavailable PostgreSQL fixture, 0
  failed). `git diff --check` is clean on both commits; a secret-aware scan of the full diff found only
  placeholder/test values, no real credential.
- **Not done, and not authorized in this pass:** moving the `develop` branch pointer to `f200c84`, and
  pushing. `develop` still carries its own later uncommitted verification-pass edits (the files listed below);
  moving the branch is a decision for the owner once those are accounted for, not a git-safety question this
  pass can resolve on its own.

### Uncommitted Feature 148 change set on `develop` (unaffected by the split above; still local-only)

Safe to stage, all inspected, secret scan clean, `git diff --check` clean:

- `src/backend/FinancialCopilot.Application/Scanner/CyclicalWavesV2/ScreenerMetricMapping.cs` (latest-selector fix)
- `tests/FinancialCopilot.IntegrationTests/Feature148ChatProviderE2ETests.cs`
- `tests/FinancialCopilot.UnitTests/CyclicalWavesV2/CyclicalWavesV2DomainAndMappingTests.cs`
- `specs/148-cyclicalwaves-v2-independent-financial-data-provider/{spec,design,plan,impact-analysis,tasks,c1-census,ImplementationReport,commit-split-manifest}.md`
- `specs/implementation-checklist.md`

Stage them by path (never `git add .` or `-A`). Do not stage `.env`, user-secrets, scratch output or test-generated `specs/141-…/evaluation/artifacts/*`.

## Appendix — C1 census SQL (read-only)

The full queries are in the session scratchpad. The core classification expression extracts `sourceProvider` from both evidence arrays with `jsonb_array_elements` and tests membership of `'CyclicalWaves'`. It is executed under `PGOPTIONS='-c default_transaction_read_only=on'`.
