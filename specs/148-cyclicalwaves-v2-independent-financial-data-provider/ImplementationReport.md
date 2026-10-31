# Feature 148 — Implementation Report

**Date:** 2026-10-31 · **Branch:** `develop` (working tree, not committed) · **Status:** FEATURE_148_PARTIALLY_IMPLEMENTED: all code is in place, but one automated verification task (T148-08.4: CyclicalWaves-mode lookup, statement table and scanner through the AI facade, and Telegram) is partial; external live ingestion is not executed (§13)

## 1. Outcome

CyclicalWaves v2 (TahlilApp API v2) is implemented as an independent provider:

- all 7 documented endpoints
- independent `cyclicalwaves` schema
- daily and manual synchronization
- a Data Management console page
- Chat provider selection with no fallback
- a basic C3 scanner

The legacy `back1.cyclicalwaves.com` integration is retired in code, schedules, triggers and configuration. C1, C2 and N1 are implemented with reliable structured provenance.

Everything was verified with automated tests. A read-only live call to the real provider also succeeded. No production deployment, production database change, data deletion or commit was made.

## 2. Tasks

`tasks.md` holds **49 tasks**: the 46 originally in the file plus 3 N1 tasks.

- **Done:** 48.
- **Partial:** 1. T148-08.4 has no end-to-end AI-facade or LLM Chat run and no Telegram run; dispatcher, policy, legacy-path and DI isolation are unit-tested.
- **Blocked:** 0.

The previously reported total of 41, and the per-group 42, were both wrong: the file already had 46 tasks before N1.

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

**NoavaranAmin:** no ingestion, persistence, repository, catalog, formula or `CompanyResolverService` change. The only NoavaranAmin edits are the read-boundary filters C1 and N1 and the C2 registration.

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
- symbol lookup (money in million IRR, EPS in IRR, `pe = 0` shown as «قابل محاسبه نیست»)
- quarterly reports and statement tables
- analysis
- the scanner

`CompanyResolverService` and every NoavaranAmin registration stay as they were.

## 9. Scanner coverage (C3)

The existing parser, column policy and ranker are reused. The CyclicalWaves executor provides:

- **12 direct codes:** `PE_TTM`, `PS_TTM`, `REVENUE`, `NET_PROFIT`, `GROSS_PROFIT`, `OPERATING_PROFIT`, the 3 margins, `MONTHLY_SALES`, `AVG_12M_MONTHLY_SALES`, `AVG_4Q_REVENUE`.
- **10 growth codes:** revenue and the three profits, each QoQ and YoY, plus monthly sales MoM and YoY. They use the Domain growth formula: `(current − base) ÷ |base| × 100`, unavailable when the base is 0.
- **Feature 116 sales-growth plans:** previous-month and same-month-last-year baselines, with Percent, Positive and Multiple kinds.
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

All runs are on this machine, 2026-10-31. PostgreSQL tests used disposable PG18 at `127.0.0.1:55441`; Docker is unavailable here.

| Suite | Passed | Failed | Skipped | Notes |
|---|---|---|---|---|
| Unit, Feature 148 (`CyclicalWavesV2*` + dispatcher) | 185 | 0 | 0 | domain, mapping, monetary resolver, parser/fixtures, token/auth/resilience, coordinator, worker, DI composition, legacy guard (11 endpoints), C1 filter, scanner, lookup, quarterly table, analysis matching, availability matrix |
| Unit, full suite | 2 751 | 20 | 0 | all 20 also fail on a clean `HEAD` worktree (HEAD: 21; the extra one is the known fresh-worktree fixture-hash test) |
| Architecture (incl. 3 new Feature 148 rules) | 14 | 1 | 0 | `MigratedSemanticExecutors_DoNotReintroduceLegacySymbolExtraction` is pre-existing: `HEAD` already contains the string |
| Integration, Feature 148 | 33 | 0 | 0 | v2 Admin 12, PostgreSQL 5 (migration up/down/up, stores, FK isolation, run-lease concurrency, Full run leaves `public` untouched), live contract 1 (real provider), legacy `410` 7, alerts and N1 8 |
| Integration, updated by approved decision | 22 | 2 | 0 | C2 analysis suites rebased to CyclicalWaves mode (15 passed), Feature 125 retired answer (5 passed), C2 in NoavaranAmin mode (1), retired provider-health (1). The 2 failures (`ShadowDefaultMode_RunsLegacyRoute…`, `…هم‌گروه چطوره؟`) also fail on a clean `HEAD` worktree. |
| Integration, full suite | 686 | 66 | 112 | every failure also fails on a clean `HEAD` worktree (HEAD: 67, so one fewer failure now); no new failure |
| Frontend: new page test | 2 | 0 | 0 | `npm run build` passed |
| Frontend: full `npm test` | 194 | 2 | — | not attributed to a baseline, because that would need a stash (state change). The failing files (`markdown-message`, `monthly-product-trend-chart`, Playwright scripts) do not import the changed modules. |

Note for test hosts: `FinancialData:ChatProvider` is read while services are composed, so a `WebApplicationFactory` must set it with `UseSetting` and not through late `ConfigureAppConfiguration`. Production reads it from appsettings and the environment at startup.

## 13. Operational verification

- **Live provider, read-only:** login, refresh, tickers (683), screener, statements, lines (82), blogs and the `from_date` filter all answered as recorded in `contract-verification.md`. The automated live test passed through the production client pipeline.
- **Not executed:** a full live ingestion into a database, an Admin-triggered live run and the scheduled 23:30 UTC run. The live data load (rollout steps 2–3) needs ingestion to be enabled in a deployed non-production environment, which this authorization did not include.
- **Not executed:** a CyclicalWaves-mode Chat acceptance run with the LLM (rollout step 4).

## 14. Remaining items

- **C1-M, REQUIRES_PRODUCT_OWNER_DECISION:** 4 130 growth rows mix a CyclicalWaves operand and a NoavaranAmin operand. They are excluded by default (design §10.6). Set `FinancialData:LegacyCyclicalWavesMixedProvenance=Include` to keep them; see `c1-census.md`.
- **Security, credentials placed in tracked files at the owner's request:** the v2 account is now in `src/backend/FinancialCopilot.API/appsettings.Development.json` and `.env.example`, and both files are tracked by git. Before committing, replace them with placeholders and keep the real values in user-secrets or the ignored `.env`; rotate them if they are ever pushed.
- **Security, N2 (separate remediation, not changed):** `src/backend/FinancialCopilot.API/appsettings.json` contains committed NoavaranCurrentApi credentials (values not reproduced here). Rotate them and move them to secrets in a separate change.
- **Product limitation L1:** v2 has no company names or industries. In CyclicalWaves mode, questions by company name or industry are NotFound or unavailable.
- **Product limitation:** analysis items carry only the title, the published summary and the date.
- **Verification gaps:** see §2 (T148-08.4) and §13.

## 15. Configuration and deployment prerequisites

1. Deploy with the defaults:
   - `FinancialData:ChatProvider=NoavaranAmin`
   - `ExcludeLegacyCyclicalWavesDerivedMetrics=true`
   - `LegacyCyclicalWavesMixedProvenance=Exclude`
   - `CyclicalWavesLegacy:AllowCalls=false`
   - `CyclicalWavesV2:IngestionEnabled=false`

   The migration is applied by the API-startup migrator. Legacy calls, schedules and triggers stop, and C1, C2 and N1 take effect.
2. Set `CYCLICALWAVES_V2_IDENTIFIER` and `CYCLICALWAVES_V2_PASSWORD` in the server's ignored `.env`, plus `CYCLICALWAVES_V2_INGESTION_ENABLED=true`. Then, from `/admin/data/cyclicalwaves-v2`, run in order: catalog, one ticker, analysis full, Full.
3. Set `CYCLICALWAVES_V2_DAILY_SYNC_ENABLED=true` and observe two scheduled runs.
4. Try `FINANCIAL_DATA_CHAT_PROVIDER=CyclicalWaves` in non-production only. Promote it to production only by explicit decision.
5. Rollback options:
   - `ChatProvider=NoavaranAmin`
   - `IngestionEnabled=false`
   - `ExcludeLegacyCyclicalWavesDerivedMetrics=false`
   - the migration `Down` (drops only the `cyclicalwaves` schema)

## Appendix — C1 census SQL (read-only)

The full queries are in the session scratchpad. The core classification expression extracts `sourceProvider` from both evidence arrays with `jsonb_array_elements` and tests membership of `'CyclicalWaves'`. It is executed under `PGOPTIONS='-c default_transaction_read_only=on'`.
