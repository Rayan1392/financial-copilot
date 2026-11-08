# Feature 148 Implementation Tasks — CyclicalWaves v2 Independent Financial Data Provider

Implementation was approved on 2026-10-31 and executed the same day. Statuses below reflect verified results; see [ImplementationReport.md](./ImplementationReport.md) for evidence. N1 (conditional alerts) was added by the Product Owner's 2026-10-31 final decision as T148-05.6, T148-06.9 and T148-08.9.

- Sources: [spec.md](./spec.md), [design.md](./design.md), [plan.md](./plan.md), [impact-analysis.md](./impact-analysis.md).
- Final decisions: C1 = B, C2 = C, C3 = A.
- Paths are relative to `src/backend/` unless they are prefixed.
- Implementation tasks include their own focused unit tests. Group T148-08 holds the cross-cutting integration, regression and operational verification.

Rules for every task:

- Do not modify NoavaranAmin ingestion, persistence, catalogs or formulas, except T148-06.7 (C1) and T148-06.9 (N1, read boundary only).
- Do not delete or rewrite legacy rows.
- Never fall back to another provider.
- Never commit secrets.

---

## T148-00 — Preparation

- [x] **T148-00.1 — Commit authoritative contracts** · DONE (2026-10-31)
  - **Objective:** Make the two v2 PDFs the versioned source of truth.
  - **Components:** `docs/providers/cyclicalwaves-v2/`
  - **Dependencies:** The Product Owner supplies the PDF files (blocker B1).
  - **Actions:**
    - Add `tahlilapp-api-client.pdf` and `income-statement-api-client.pdf`.
    - Add `README.md` with the provenance: title, version 2.0, the 2 Aban 1405 date, who supplied them and when, and a SHA-256 of each file.
    - Label `docs/cw_api.txt` and `docs/Cyclical Waves_api_document_2026_05_26.pdf` as legacy in that README.
  - **Verification:** Files present, hashes recorded, no other docs changed.

- [x] **T148-00.2 — Deterministic fixtures and field catalog source** · DONE (2026-10-31)
  - **Objective:** Turn the documented samples into test fixtures and pin the 29-field catalog.
  - **Components:** `tests/FinancialCopilot.UnitTests/CyclicalWavesV2/Fixtures/`
  - **Dependencies:** T148-00.1
  - **Actions:**
    - Transcribe these samples verbatim into fixture JSON:
      - login and refresh `201`
      - tickers array
      - screener object (شلرد)
      - statements response (فولاد: 3 lines; `null`, negative and EPS cases)
      - blogs page with `meta`
      - error bodies `400`/`401`/`404`
    - Add a `screener-fields.md` table with the 29 field keys, the English and Persian names (PDF §3.2), category and unit. This table is the single input for the Domain catalog.
    - Add edge fixtures derived from documented semantics:
      - `pe = 0`
      - a null metric
      - a statement with a null quarter
      - key 86 with null TTM
      - a blog item with an empty summary
  - **Verification:** Fixtures parse as JSON. The field table has exactly 29 rows and every key matches the PDF sample.

- [x] **T148-00.3 — Live contract verification (read-only)** · DONE (2026-10-31)
  - **Objective:** Resolve the undocumented details B2 and B3 from evidence.
  - **Components:** scratch script outside `src/` (not committed); evidence written to `specs/148-…/contract-verification.md`
  - **Dependencies:** T148-00.1; v2 credentials (blocker B4)
  - **Actions:** One login, then one GET each to:
    - `/income-statements/lines`: shape and count.
    - `/custom-filtering/tickers`: count.
    - One screener and one statements call.
    - `/blogs?filter[type]=analysis&paginate=5`: do tag names include tickers? Does the title prefix carry the ticker? What are the exact tag names for the 7 categories?
    - One `filter[from_date]=YYYY-MM-DD` call.
    - Decode the access-token `exp` claim; do not record the token itself.
    - Then update fixtures and the design §10.4 topic map with the evidence.
  - **Verification:** The evidence file records request, status and a redacted sample per endpoint. With no credentials the file says `NOT EXECUTED`, and the tolerant defaults stay in place.

## T148-01 — Persistence

- [x] **T148-01.1 — Domain catalog and comparer** · DONE (2026-10-31)
  - **Objective:** Make the metric catalog and change detection the single owner of these rules.
  - **Components:** `FinancialCopilot.Domain/Financial/CyclicalWavesV2/ScreenerMetricCatalog.cs`, `ScreenerValueComparer.cs`
  - **Dependencies:** T148-00.2
  - **Actions:**
    - Build 29 immutable definitions with fixed ids 1–29: FieldKey, NameEn, NameFa, Category (`MonthlySales`, `QuarterlySales`, `Profit`, `ProfitMargin`, `Valuation`) and Unit (`IRR`, `Percent`, `Ratio`).
    - Add a `pe` rule flag: zero means not computable.
    - Add a decimal comparer that treats null as equal to null.
  - **Verification:** Unit tests confirm 29 unique keys and ids, non-empty names in both languages, units matching the field table, and correct comparer behavior.

- [x] **T148-01.2 — Entities and EF mappings** · DONE (2026-10-31)
  - **Objective:** Create the 11 tables in schema `cyclicalwaves`.
  - **Components:** `Infrastructure/Financial/Ingestion/Persistence/CyclicalWavesV2Rows.cs`, `CyclicalWavesV2Configurations.cs`, `FinancialIngestionDbContext.cs` (`DbSet`s only)
  - **Dependencies:** T148-01.1
  - **Actions:** Map the 11 tables per design §3:
    - `ToTable(name, "cyclicalwaves")` on every table.
    - `numeric(38,6)` for amounts.
    - Unique and partial-unique indexes, check constraints, and cascade or restrict FKs as specified.
    - The `Running` partial unique index on `SyncRuns`.
    - `HasData` seeding for `MetricDefinitions` from the Domain catalog.
    - No navigation or FK to any non-`cyclicalwaves` entity.
  - **Verification:** Build passes. Architecture and unit check: no configuration in the file references a type outside the v2 rows.

- [x] **T148-01.3 — Additive migration** · DONE (2026-10-31)
  - **Objective:** One migration that creates the schema and seed.
  - **Components:** `Infrastructure/Financial/Ingestion/Persistence/Migrations/<ts>_Feature148CyclicalWavesV2Foundation.cs`
  - **Dependencies:** T148-01.2
  - **Actions:**
    - Generate with `FINANCIAL_COPILOT_CONNECTION_STRING` pointing at disposable PG18.
    - Inspect the output: `EnsureSchema`, 11 tables, 29 seed rows. No `public` object is altered.
    - Implement `Down` to drop the tables and the schema.
  - **Verification:**
    - `Up`, `Down` and `Up` succeed on disposable PG18.
    - The model snapshot diff touches v2 entities only.
    - `information_schema` shows no FK crossing schemas.

- [x] **T148-01.4 — Financial stores** · DONE (2026-10-31)
  - **Objective:** Latest-state and period upserts.
  - **Components:** `Application/FinancialData/CyclicalWavesV2/ICyclicalWavesV2Store.cs` (records, port); `Infrastructure/Financial/Ingestion/CyclicalWavesV2/EfCoreCyclicalWavesV2Store.cs`
  - **Dependencies:** T148-01.2
  - **Actions:**
    - `UpsertCatalog`: insert new tickers, set missing ones inactive, reactivate returning ones, set `NormalizedTicker` via `PersianSymbolNormalizer`.
    - `ApplyScreener` per design §5: write only changed values, update dates, set `Enticker` (a conflict is logged and counted), one transaction, return whether anything changed.
    - `UpsertLineDefinitions`.
    - `ApplyIncomeStatements`: key on (`TickerId`, `PeriodEndingDate`, `FiscalMonth`); diff values by line key; latest `publishDate` wins within a response; unknown keys auto-create a definition; never delete.
  - **Verification:** Unit tests with SQLite or in-memory for the logic. The PostgreSQL behavior is covered in T148-08.2.

- [x] **T148-01.5 — Analysis store** · DONE (2026-10-31)
  - **Objective:** Independent article persistence.
  - **Components:** `ICyclicalWavesV2AnalysisStore`; `EfCoreCyclicalWavesV2AnalysisStore`
  - **Dependencies:** T148-01.2
  - **Actions:**
    - `ApplyAnalysesPage`: upsert by id; compute `ContentHash`; on change, update and replace categories and tags; `PlainTextSummary` through the existing `IHtmlTextStripper`; `NormalizedName` for tags; never delete.
    - `GetMaxPublishedAt` for incremental sync.
  - **Verification:** Unit tests for: a new article, an identical article (no write), a changed title or tag (update), and an empty summary.

- [x] **T148-01.6 — Run store with lease** · DONE (2026-10-31)
  - **Objective:** Run tracking and overlap prevention.
  - **Components:** `ICyclicalWavesV2SyncRunStore`; `EfCoreCyclicalWavesV2SyncRunStore`
  - **Dependencies:** T148-01.2
  - **Actions:**
    - `Request(scope, …)`: return `Conflict(existingId)` if any run is `Requested` or `Running`.
    - `TryStart`: on conflict, record `SkippedAlreadyRunning`.
    - `ClaimNextRequested`, `Heartbeat`, `Complete(status, counts, errorSummary ≤ 4 KB, sanitized)`, `ExpireStaleLeases`, `GetStatus(recent)`.
  - **Verification:** Unit tests for state transitions. The concurrency check against the real partial unique index is in T148-08.2.

## T148-02 — API Client and Authentication

- [x] **T148-02.1 — Options and registration** · DONE (2026-10-31)
  - **Objective:** Validated `CyclicalWavesV2` configuration.
  - **Components:** `Infrastructure/Financial/Providers/CyclicalWavesV2/CyclicalWavesV2Options.cs`; `ServiceCollectionExtensions.cs`; API and Worker `appsettings.json` (new section only, ingestion off)
  - **Dependencies:** —
  - **Actions:**
    - Add the options from design §9, with HTTPS base addresses and ranges: limit 1–40, page size 1–100, cron valid via `CyclicalWavesUtcCronSchedule.IsValid`.
    - Require credentials only when `IngestionEnabled`.
    - Use `ValidateOnStart`.
  - **Verification:** Unit tests reject invalid values and accept defaults with empty credentials while ingestion is off.

- [x] **T148-02.2 — Token service** · DONE (2026-10-31)
  - **Objective:** Process-local login and refresh with rotation.
  - **Components:** `CyclicalWavesV2TokenService.cs`
  - **Dependencies:** T148-02.1
  - **Actions:**
    - Login request: `x-client-id` header, body `{identifier,password}`, expect 201.
    - Refresh request: body `{refreshToken}`.
    - Expiry from the JWT `exp` claim, read without signature validation and used only for timing; 4-minute fallback; `RefreshSkewSeconds`.
    - Store the rotated refresh token before returning.
    - `SemaphoreSlim` single-flight.
    - If refresh returns 401, log in exactly once. A failed login raises `ProviderAuthenticationFailed`.
    - Never log tokens or credentials.
  - **Verification:** Unit tests with a stub handler cover:
    - `exp` parsing and the fallback
    - refresh before expiry
    - rotation persisted
    - 20 concurrent callers causing exactly one refresh
    - refresh 401 causing exactly one login
    - every auth request carries `x-client-id: default`

- [x] **T148-02.3 — Auth handler and typed clients** · DONE (2026-10-31)
  - **Objective:** The HTTP pipeline.
  - **Components:** `CyclicalWavesV2AuthHandler.cs`; typed `CyclicalWavesV2AuthClient` and `CyclicalWavesV2DataClient` registration
  - **Dependencies:** T148-02.2
  - **Actions:**
    - On a data-call 401: refresh once, replay once, then return the failure.
    - Pipeline order: auth handler, then `FinancialProviderResilienceHandler` (timeout, 5xx/408 retry with jitter, no 4xx retry).
    - `AllowAutoRedirect = false`.
    - Separate base addresses for auth and data.
  - **Verification:** Unit tests: 401 → exactly 1 refresh and 1 replay; a second 401 surfaces; a 400 or 404 is not retried; a 503 is retried up to `RetryCount`.

- [x] **T148-02.4 — Endpoint methods and DTO mapping** · DONE (2026-10-31)
  - **Objective:** Cover the 5 data endpoints and map them to Application records.
  - **Components:** `ICyclicalWavesV2Client` (Application port); `CyclicalWavesV2DataClient`; `CyclicalWavesV2Dtos.cs`
  - **Dependencies:** T148-02.3, T148-00.2
  - **Actions:** Implement these calls:
    - `GetTickers`
    - `GetScreener(ticker)`, with URL-encoded Persian; 404 → `NotFound`
    - `GetIncomeStatements(ticker, limit)`, with 404 → `NoStatements` and 400 → `InvalidLimit`
    - `GetIncomeStatementLines`, with a tolerant parser that accepts an array or a wrapped array of `{key,name,nameFa}`
    - `GetAnalysesPage(page, paginate, fromDate?)`, with bracket query keys encoded and `filter[type]=analysis` always present

    DTO rules:
    - Ignore unknown fields.
    - Parse `"YYYYMMDD"` strings, the `periodEndingDate` integer and UTC `published_at`.
    - Keep `null` as `null`.
    - Never let a DTO leave Infrastructure.
  - **Verification:** Unit tests parse every T148-00.2 fixture to the expected records, including all 29 screener fields, `null`, negative values, `pe = 0` and an empty summary.

## T148-03 — Data Synchronization

- [x] **T148-03.1 — Coordinator core** · DONE (2026-10-31)
  - **Objective:** One use case for the Worker and Admin.
  - **Components:** `Application/FinancialData/CyclicalWavesV2/CyclicalWavesV2SyncCoordinator.cs`
  - **Dependencies:** T148-01.4, T148-01.5, T148-01.6, T148-02.4
  - **Actions:**
    - `RunAsync(scope, options, trigger, requestedBy)` with the lease from `TryStart`.
    - Per-item try/catch with counts: total, processed, updated, unchanged, failed.
    - A sanitized error summary (ticker plus error category, no payloads or tokens).
    - Pacing via an injectable delay with `TimeProvider`.
    - Heartbeat every K items, and `Complete`.
    - A whole-run failure (authentication, or catalog unavailable) ends the run as `Failed`.
  - **Verification:** Unit tests with fakes cover counts, per-item isolation, `SkippedAlreadyRunning`, and the absence of secrets in the error summary.

- [x] **T148-03.2 — Catalog, Ticker, Screener and IncomeStatements scopes** · DONE (2026-10-31)
  - **Objective:** Financial data synchronization.
  - **Components:** the coordinator
  - **Dependencies:** T148-03.1
  - **Actions:**
    - **Catalog:** `GetTickers` → `UpsertCatalog`.
    - **Ticker:** requires a catalog row (else NotFound); `ApplyScreener` plus `ApplyIncomeStatements`.
    - **Screener:** all active tickers.
    - **IncomeStatements:** `GetIncomeStatementLines` → `UpsertLineDefinitions`, then all active tickers with the configured limit.
    - A screener 404 counts as a failure. A statements 404 counts as unchanged, with no statements.
  - **Verification:** Unit tests: an unchanged second run gives Updated = 0; one changed metric marks only that ticker Updated; 404 handling as above.

- [x] **T148-03.3 — Analysis scopes (initial and incremental)** · DONE (2026-10-31)
  - **Objective:** Comprehensive-analysis ingestion.
  - **Components:** the coordinator
  - **Dependencies:** T148-03.1
  - **Actions:**
    - **Full:** pages 1..`meta.last_page`, `paginate=AnalysisPageSize`, no date filter.
    - **Incremental:** `from_date` = Tehran date of `GetMaxPublishedAt` minus `AnalysisIncrementalOverlapDays`; when the store is empty, use Full.
    - One transaction per page. The loop stops on `last_page` or on an empty `data`.
  - **Verification:** Unit tests: an overlapping second incremental run inserts 0 rows; full mode walks all pages; an empty store switches to Full.

- [x] **T148-03.4 — Full scope** · DONE (2026-10-31)
  - **Objective:** The daily composite run.
  - **Components:** the coordinator
  - **Dependencies:** T148-03.2, T148-03.3
  - **Actions:**
    - Order: Catalog → lines → per ticker (screener, then statements) → Analysis (incremental, or full when empty).
    - Aggregate counts per phase into the run's error summary.
  - **Verification:** A unit test checks the call order and that one ticker's failure does not stop the other tickers or the analysis phase.

## T148-04 — Worker and Admin

- [x] **T148-04.1 — Daily worker** · DONE (2026-10-31)
  - **Objective:** Scheduled, catch-up and requested runs.
  - **Components:** `Worker/CyclicalWavesV2SyncWorker.cs`; `Worker/Program.cs`
  - **Dependencies:** T148-03.4
  - **Actions:**
    - A `BackgroundService` with a scope per cycle.
    - When `IngestionEnabled` and `DailySyncEnabled`:
      - At startup, run a Full catch-up if no Full run has completed since the previous cron occurrence.
      - Then run a Full at each `DailySyncCronUtc` occurrence.
    - Every `RequestedRunPollSeconds` while `IngestionEnabled`: run `ExpireStaleLeases`, then `ClaimNextRequested`, then execute.
    - Rethrow only when stopping.
    - Do not let the `ChatProvider` setting affect anything here.
  - **Verification:** Worker unit tests (the existing `CyclicalWavesDataAcquisitionWorkerTests` style, fake time):
    - the schedule fires at 23:30 UTC
    - catch-up runs once
    - a requested run is claimed
    - disabled flags cause no runs
    - behavior is identical for both `ChatProvider` values

- [x] **T148-04.2 — Admin API** · DONE (2026-10-31)
  - **Objective:** Manual operations and status.
  - **Components:** `API/Controllers/CyclicalWavesV2AdminController.cs`; `API/Contracts/CyclicalWavesV2AdminContracts.cs`
  - **Dependencies:** T148-03.4, T148-01.6
  - **Actions:**
    - Route `api/v1/admin/cyclicalwaves-v2`, with `[Authorize(Policy = AuthorizationPolicies.DataAdmin)]` and `[EnableRateLimiting(AuthenticatedActor)]`.
    - Inline endpoints: `POST catalog/sync` and `POST tickers/{ticker}/sync`.
    - Queued endpoints: `POST screener/sync`, `income-statements/sync`, `analysis/sync?mode=` and `sync` → `202 {runId}`, or `409 {existingRunId}`.
    - `GET status?recent=` returns flags, next UTC run, active run, last success per scope and recent runs with errors.
    - `RequestedBy` is the actor id.
  - **Verification:** Endpoint tests (T148-08.3).

- [x] **T148-04.3 — Admin panel** · DONE (2026-10-31)
  - **Objective:** A minimal UI on the existing Data Management console.
  - **Components:**
    - `src/frontend/src/routes/admin_.data.cyclicalwaves-v2.tsx`
    - `src/frontend/src/components/admin/cyclicalwaves-v2-page.tsx`
    - `data-management-shell.tsx` (nav item)
    - `data-admin-client.ts` (7 functions and types)
    - `data-management-overview-page.tsx` (relabel the legacy card «CyclicalWaves (قدیمی، متوقف‌شده)» and add a v2 card)
  - **Dependencies:** T148-04.2
  - **Actions:**
    - Six action buttons and a ticker input.
    - A status header (flags, next run).
    - A recent-runs table with counts and errors.
    - Polling while a run is active.
    - Wrap in `DataManagementGuard`.
    - Use existing components only.
  - **Verification:** The frontend build passes. A component test renders the status and disables the buttons while a run is active.

- [x] **T148-04.4 — Deployment configuration** · DONE (2026-10-31)
  - **Objective:** Document the v2 settings without secrets.
  - **Components:** `docker-compose.yml`, `.env.example`, root `README.md`
  - **Dependencies:** T148-02.1
  - **Actions:**
    - Map `CyclicalWavesV2__*` from `CYCLICALWAVES_V2_*` (credentials optional, ingestion default `false`).
    - Add `FinancialData__ChatProvider` (default `NoavaranAmin`). The C1 exclusion has no setting (C1-M).
    - Add a README configuration paragraph.
  - **Verification:** `docker compose config` resolves with v2 variables unset. A secret scan of the diff is clean.

## T148-05 — Legacy Retirement

- [x] **T148-05.1 — Legacy call guard** · DONE (2026-10-31)
  - **Objective:** Block all 11 legacy endpoints before any transmission.
  - **Components:** `Infrastructure/Financial/Providers/CyclicalWaves/LegacyCyclicalWavesCallBlockingHandler.cs`; `CyclicalWavesLegacyOptions` (`AllowCalls = false`); `ServiceCollectionExtensions.cs` (first handler on the 4 legacy typed clients)
  - **Dependencies:** —
  - **Actions:**
    - Throw `FinancialProviderException` (`ProviderRetired`) without calling the inner handler.
    - Log once per minute with the client name only.
  - **Verification:**
    - A unit test per client records zero inner sends.
    - An architecture test asserts that every legacy typed-client registration includes the guard.

- [x] **T148-05.2 — Unhost obsolete schedules and set flags** · DONE (2026-10-31)
  - **Objective:** No futile scheduled legacy work.
  - **Components:** `Worker/Program.cs`; API and Worker `appsettings.json`; `docker-compose.yml`
  - **Dependencies:** —
  - **Actions:**
    - Remove the `AddHostedService` lines for `ComprehensiveAnalysisDailySyncWorker`, `CyclicalWavesDataAcquisitionWorker` and `IndustryRelativeValuationCalculationWorker`. Keep the classes.
    - Set `IndustryRelativeValuation:Enabled=false`, including the compose default.
    - Set API `CyclicalWavesPsSync:AllowManualSyncWhenWorkerDisabled=false`.
    - Make `CYCLICAL_WAVES_USER_NAME` and `CYCLICAL_WAVES_PASSWORD` optional (`:-`).
  - **Verification:** Update `CleanArchitectureDependencyTests` so it asserts:
    - The three workers are not hosted.
    - The P/S sync and Feature 126 workers remain unhosted.
    - The v2 worker is hosted.
    - Also: compose resolves without the legacy variables.

- [x] **T148-05.3 — Retire legacy Admin ingestion triggers** · DONE (2026-10-31)
  - **Objective:** Manual legacy ingestion cannot run.
  - **Components:** `API/Controllers/AdminDataOperationsController.cs`; `API/Controllers/CyclicalWavesPsVisualizationAdminController.cs`
  - **Dependencies:** T148-05.1
  - **Actions:**
    - Return `410 Gone` (`ProviderRetired` problem body) before invoking any service for:
      - `cyclicalwaves/full-sync`
      - `cyclicalwaves/ps-visualization/sync`
      - any `ps-visualization` dry-run that calls the provider (verify in code)
      - `comprehensive-analysis/full-sync` and `comprehensive-analysis/daily-sync`
      - `data-sync` requests that explicitly name `CyclicalWaves`
    - `provider-health` returns the retired status without I/O.
    - Keep these read-only and local endpoints unchanged: `sync-runs`, `backfill-plain-text`, `backfill-company-id`, P/S `snapshot`/`history`/`companies`.
  - **Verification:** Endpoint tests (T148-08.3) assert `410` and that the service spies record zero calls.

- [x] **T148-05.4 — Disable legacy-only capabilities and consumers** · DONE (2026-10-31)
  - **Objective:** Turn off 115, 125 and the legacy analysis detector.
  - **Components:** `ServiceCollectionExtensions.cs` (unregister `ComprehensiveAnalysisPublishedDetector`); the retired list in `ChatCapabilityAvailability` (T148-06.1); `CyclicalWavesPsVisualization:*` stays false
  - **Dependencies:** T148-06.1
  - **Actions:**
    - Remove the detector registration only. Historical insight events and frontend localization stay.
    - Retire `ps_gauge_visualization` and `industry_relative_valuation_read` in both modes.
  - **Verification:** A unit test checks the `IInsightDetector` set no longer contains the detector. Chat tests for AC-21 are in T148-08.4.

- [x] **T148-05.5 — Deprecation documentation** · DONE (2026-10-31)
  - **Objective:** Mark superseded and disabled specs.
  - **Components:** `specs/020`, `065`, `066`, `067`, `070`, `071`, `073`, `084`, `114`, `115`, `125`, `126`, `127`; `specs/README.md`; `specs/implementation-checklist.md`
  - **Dependencies:** —
  - **Actions:**
    - Prepend the header lines exactly as in impact-analysis §4. Bodies are unchanged.
    - Add a Feature 148 index entry and checklist row.
  - **Verification:** `git diff` on those specs shows only header or note insertions.

- [x] **T148-05.6 — N1: identify legacy-dependent conditional alerts** · DONE (2026-10-31)
  - **Objective:** Find every alert data source that can carry legacy CyclicalWaves data, using provenance only.
  - **Components:** `Infrastructure/Financial/ConditionalTrackers/ConditionalTrackerEvaluationProcessor.cs` (read only); read-only census on the Development database
  - **Dependencies:** T148-06.6
  - **Actions:** Inspect every resolver of the evaluation pipeline; count providers of quotes, trades, feature snapshots and insight events; count active financial-metric rules whose latest `DerivedMetrics` row is legacy.
  - **Verification:** Only `FinancialMetric` rules read `DerivedMetrics`; quotes and trades are `TsetmcWebService`, `FeatureSnapshots` and the alert-relevant insight events carry no CyclicalWaves evidence. No legacy dependency is ambiguous.

## T148-06 — Chat Routing and Provider Isolation

- [x] **T148-06.1 — ChatProvider option and availability policy** · DONE (2026-10-31)
  - **Objective:** One owner of the capability matrix.
  - **Components:** `Application/AI/Orchestration/ChatCapabilityAvailability.cs` (`FinancialDataChatProvider` enum, `FinancialDataOptions`, `IChatCapabilityAvailability`); registration
  - **Dependencies:** —
  - **Actions:**
    - Bind `FinancialData:ChatProvider`, validated at startup, defaulting to `NoavaranAmin`.
    - Implement the design §10.1 matrix: retired in both modes; C2 unavailable in NoavaranAmin mode; the CyclicalWaves-supported set.
    - Return typed outcomes `Available`, `Retired`, `UnavailableForProvider`, together with their Persian messages.
  - **Verification:** A unit test covers every capability code in `CapabilityExecutionRoutes.All` × both modes.

- [x] **T148-06.2 — Enforcement points (including C2)** · DONE (2026-10-31)
  - **Objective:** No path bypasses the policy.
  - **Components:** `Application/AI/Orchestration/SemanticCapabilityExecutionContracts.cs` (dispatcher); `Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs`
  - **Dependencies:** T148-06.1
  - **Actions:**
    - Before invoking an executor, the dispatcher returns the existing typed unsupported or unavailable outcome with the policy message.
    - In the legacy workflow path, skip direct use-case branches and do not register agent tools for unavailable capabilities. This includes `query_comprehensive_analysis` in NoavaranAmin mode (C2), and in CyclicalWaves mode every tool except lookup, scanner and analysis.
    - Disable `TryFeature141FallbackAsync` in CyclicalWaves mode.
    - Inventory every Chat call site of `IComprehensiveAnalysisQueryUseCase` and `IQueryComprehensiveAnalysisUseCase` and confirm each is behind the policy. Record the list in the PR description.
  - **Verification:** Chat tests (T148-08.4): in NoavaranAmin mode an analysis question gets the C2 message and the legacy and v2 analysis repository spies record zero calls; retired capabilities get the retired message in both modes.

- [x] **T148-06.3 — CyclicalWaves entity resolver and conditional DI** · DONE (2026-10-31)
  - **Objective:** Ticker resolution from the v2 catalog only.
  - **Components:** `Infrastructure/Financial/Ingestion/CyclicalWavesV2/CyclicalWavesV2CanonicalEntityResolver.cs`; the CyclicalWaves-mode registration block in `ServiceCollectionExtensions.cs`
  - **Dependencies:** T148-01.4, T148-06.1
  - **Actions:**
    - Implement `ICanonicalQueryEntityResolver` and `ICanonicalQueryIndustryResolver` over `Tickers`:
      - match order: exact normalized ticker → `Enticker` → the existing fuzzy matcher restricted to v2 tickers
      - `ExternalCompanyId = "cw2:{Id}"`
      - industry resolution always NotFound
    - Register it only when `ChatProvider=CyclicalWaves`. `CompanyResolverService` is never swapped.
  - **Verification:** Unit tests cover resolved, ambiguous and not-found cases. A DI test proves NoavaranAmin mode resolves the original implementations.

- [x] **T148-06.4 — CyclicalWaves symbol metric lookup** · DONE (2026-10-31)
  - **Objective:** Metric retrieval from v2 values.
  - **Components:** `Application/Scanner/CyclicalWavesV2/ScreenerMetricMapping.cs` (shared with T148-07); `Infrastructure/…/CyclicalWavesV2SymbolMetricLookupService.cs`
  - **Dependencies:** T148-06.3
  - **Actions:**
    - Map `(MetricCode, PeriodSelector)` to a field per the design §10.3 table. EPS comes from statement key 85.
    - Build the existing `SymbolLookupTableResult`:
      - money in «میلیون ریال», computed as IRR ÷ 10^6 at presentation
      - EPS in IRR
      - margins in %
      - ratios as plain numbers
      - `pe = 0` shown as «قابل محاسبه نیست»
      - period labels from the metadata dates
    - An unmapped or null value returns a Missing cell with an unavailable warning.
    - Register in CyclicalWaves mode only.
  - **Verification:** Unit tests for every mapping row, `null`, `pe = 0` and the unit presentation.

- [x] **T148-06.5 — CyclicalWaves quarterly income statement** · DONE (2026-10-31)
  - **Objective:** Statement retrieval from v2 tables.
  - **Components:** `CyclicalWavesV2QuarterlyIncomeStatementUseCase.cs` (implements `IQuarterlyFinancialReportsQueryUseCase`)
  - **Dependencies:** T148-06.3
  - **Actions:**
    - Resolve the ticker through the v2 resolver.
    - Select the latest N statements.
    - Build `QuarterlyFinancialReportTableResult`:
      - main rows from keys 44, 48, 52, 54, 56, 57, 62, 63, 66, 85 and 86, using each provider line's Persian name and the response order
      - quarter values with million-IRR conversion at presentation
      - EPS in IRR
      - null cells unavailable
    - Lines specific to banks or insurers appear by their own keys, with no NoavaranAmin template mapping.
    - Register in CyclicalWaves mode only.
  - **Verification:** Unit tests with the فولاد fixture: rows, nulls, negative values and units.

- [x] **T148-06.6 — C1 provenance census (gate)** · DONE — gate (a)=(b) and (d)=0 met; (c)=4 130 mixed growth rows, see [c1-census.md](./c1-census.md)
  - **Objective:** Prove the discriminator is reliable before filtering.
  - **Components:** read-only SQL or a C# console script, outside production projects; evidence in `specs/148-…/c1-census.md`
  - **Dependencies:** —
  - **Actions:** On the Development database, read-only, count `DerivedMetrics` rows by:
    - (a) structured `SourceProvider = 'CyclicalWaves'` in source or dependency evidence
    - (b) the current substring marker
    - (c) mixed providers within one row
    - (d) the marker present only in `CalculationPolicyVersion`
    - (e) per `MetricCode`, the affected rows and how many companies would lose their latest value

    Record the JSON property casing actually used.
  - **Verification:**
    - The evidence file exists.
    - **Gate:** (a) and (b) match, and (c) and (d) are both 0. Otherwise stop T148-06.7 and report to the Product Owner.

- [x] **T148-06.7 — C1 filter in NoavaranAmin reads** · DONE — superseded in part by T148-06.10 (C1-M: no setting, mixed rows always excluded)
  - **Objective:** Exclude legacy CyclicalWaves-derived rows at the read boundary.
  - **Components:** `Infrastructure/Financial/Scanner/LegacyCyclicalWavesDerivedMetricFilter.cs`; call sites in `EfCoreSymbolMetricLookupService.cs`, `EfCoreScannerExecutionService.cs` (both `DerivedMetrics` queries) and `ProfessionalScanners/ProfessionalScannerUseCases.cs`
  - **Dependencies:** T148-06.6 gate passed
  - **Actions:**
    - Parse the evidence JSON for structured `SourceProvider`. Exclude a row when any source or dependency evidence item equals `ProviderSources.CyclicalWavesName`.
    - Apply the filter **before** the latest-per-company/metric selection.
    - Make no other change to those classes: formulas, precedence code and the CyclicalWaves layout code stay.
    - Do not touch `ConditionalTrackerEvaluationProcessor` (residual N1).
  - **Verification:** Unit tests for the filter. PostgreSQL tests in T148-08.2:
    - Legacy rows are never selected.
    - NoavaranAmin rows are selected.
    - With no valid row the result is Missing.
    - `DerivedMetrics` row counts are unchanged.

- [x] **T148-06.8 — CyclicalWaves comprehensive analysis retrieval** · DONE (2026-10-31)
  - **Objective:** Deterministic article search over v2 tables.
  - **Components:** `CyclicalWavesV2ComprehensiveAnalysisQuery.cs` (implements `IComprehensiveAnalysisQueryUseCase` and `IQueryComprehensiveAnalysisUseCase`); the tool-description variant in the workflow for CyclicalWaves mode
  - **Dependencies:** T148-01.5, T148-06.3, T148-00.3
  - **Actions:**
    - Ticker match: tag `NormalizedName` equals the normalized ticker, OR the normalized title's leading segment equals it.
    - Topic map: the 7 categories mapped to stored tag names, per the evidence from T148-00.3.
    - Optional `FromDate`; order by `PublishedAt` descending; limit 5 by default, 10 maximum.
    - Map to `ComprehensiveAnalysisSummaryItem`: Jalali date (Tehran), author from category titles, plain summary.
    - The tool and presentation text say only the title, published summary and date are available.
    - Register in CyclicalWaves mode only.
  - **Verification:** Unit tests: ticker by tag; ticker by title; topic filter; empty summary (listed by title and date); no "full article" wording in rendered output.

- [x] **T148-06.9 — N1: disable only legacy-dependent alerts** · DONE (2026-10-31)
  - **Objective:** A financial-metric alert whose current value is legacy CyclicalWaves-derived is not evaluated, triggered or notified; every other alert is unchanged.
  - **Components:** `ConditionalTrackerEvaluationProcessor.ResolveFinancialMetricAsync` (read boundary); `LegacyCyclicalWavesDerivedMetricFilter` (shared with C1)
  - **Dependencies:** T148-06.7, T148-05.6
  - **Actions:**
    - Classify the selected latest row by structured `SourceProvider` (same rule and switches as C1).
    - When legacy: record `LastDecision = LegacyDataRetired` with a Persian reason, keep the rule `Active`, create no trigger and no notification intent, and never substitute another row or source.
  - **Verification:** T148-08.9.

- [x] **T148-06.10 — C1-M: permanent exclusion of legacy and mixed provenance** · DONE (2026-10-31)
  - **Objective:** Product Owner decision C1-M: legacy and mixed-provenance metrics are never valid evidence; the `Include` option is removed.
  - **Components:** `LegacyCyclicalWavesDerivedMetricFilter.cs` (static rule, no options), `FinancialDataOptions` (only `ChatProvider` remains), call sites in `EfCoreSymbolMetricLookupService`, `EfCoreScannerExecutionService` (2 queries), `ProfessionalScannerUseCases` (availability and freshness), `ConditionalTrackerEvaluationProcessor` (N1); API/Worker `appsettings.json`, `docker-compose.yml`, `.env.example`, root `README.md`.
  - **Dependencies:** T148-06.7
  - **Actions:**
    - Remove `ExcludeLegacyCyclicalWavesDerivedMetrics` and `LegacyCyclicalWavesMixedProvenance` and every branch on them.
    - Exclude exclusively-legacy and mixed rows always; classify provider-less rows by the four registered legacy-only calculation policies; exclude unparseable evidence.
    - Apply the rule to the professional scanner's availability and freshness query, which previously counted legacy rows.
  - **Verification:** unit tests (classification incl. policy and undetermined cases, removed settings cannot reactivate, inputs unchanged, every listed policy is registered) and the NoavaranAmin Chat E2E tests (legacy P/E and monthly sales skipped, mixed growth row excluded from the scanner with a valid control row kept, historical rows still stored). Census in `c1-census.md`.

## T148-07 — Basic Financial Scanner (C3)

- [x] **T148-07.1 — Scanner metric mapping and growth** · DONE (2026-10-31)
  - **Objective:** Map scanner metric codes to v2 operands.
  - **Components:** `ScreenerMetricMapping.cs` (scanner entries); the existing Domain growth calculator (`Domain/Financial/Metrics/DeterministicMetricCalculators.cs`), reused and not modified
  - **Dependencies:** T148-06.4
  - **Actions:**
    - Verify and record the existing growth formula and the non-positive-base policy.
    - Add direct mappings for the 12 direct codes and growth mappings for the 10 growth codes, per design §10.5.
    - Every other code maps to `Unavailable(reason)`.
  - **Verification:** Unit tests compare each growth code with hand-computed fixtures, including a negative or zero base (unavailable, per policy) and sign changes.

- [x] **T148-07.2 — Monetary threshold resolver** · DONE (2026-10-31)
  - **Objective:** Convert user-entered amounts to IRR deterministically.
  - **Components:** `Application/Scanner/CyclicalWavesV2/MonetaryThresholdResolver.cs`; `Domain/Financial/CyclicalWavesV2/MonetaryAmountUnits.cs`
  - **Dependencies:** —
  - **Actions:**
    - Normalize Persian and Arabic digits and the separators `٬`, `,` and `٫`.
    - Detect `number [هزار|میلیون|میلیارد|هزار میلیارد|همت] [ریال|تومان]`. همت = 10^12 toman; toman = 10 IRR.
    - Match each monetary condition (unit `IRR` per the catalog) to an expression by equal numeric value.
    - Return one of: `Resolved(IRR)`, `ZeroNoUnitNeeded`, `MissingUnit` or `Ambiguous`.
    - Leave percent and ratio conditions untouched.
  - **Verification:** Unit tests:
    - «۱۰۰ میلیارد تومان» → 1e12 IRR
    - «۵۰۰ میلیون ریال» → 5e8
    - «۲ همت» → 2e13
    - «۱٬۵ میلیارد تومان» → 1.5e10
    - a missing unit → `MissingUnit`
    - two equal numbers with different units → `Ambiguous`

- [x] **T148-07.3 — CyclicalWaves scanner executor** · DONE (2026-10-31)
  - **Objective:** Basic screening over v2 data with the existing parser and ranker.
  - **Components:** `Infrastructure/Financial/Ingestion/CyclicalWavesV2/CyclicalWavesV2ScannerExecutionService.cs` (implements `IScannerExecutionService`); CyclicalWaves-mode DI
  - **Dependencies:** T148-07.1, T148-07.2, T148-06.1
  - **Actions:**
    - **Rejections, before any data read:**
      - `Universe.IndustryCode` → unavailable
      - any unavailable metric → unavailable answer naming the criterion
      - `MissingUnit` or `Ambiguous` → clarification «واحد مبلغ (ریال/تومان، میلیون/میلیارد) را مشخص کنید»
    - **Data:** load active tickers, values and metadata in one query from the `cyclicalwaves` tables only.
    - **Evaluation:**
      - evaluate the six operators, ANDed
      - `null` never matches and is counted as missing
      - `pe = 0` never matches `PE_TTM`
      - margin and growth thresholds are percentages
    - **Result:**
      - build `ScannerTableResult` through the existing `IScannerResultColumnPolicy` and `IScannerResultRanker`
      - per-row period labels and a mixed-period flag
      - monetary columns follow the existing display convention, converted at presentation
  - **Verification:** Scanner tests in T148-08.5.

- [x] **T148-07.4 — Feature 116 sales-growth plans, "between", unsupported criteria** · DONE with limitation — "between" verified as two bound conditions; the live LLM parser output for «بین ۵ و ۱۰» was not captured
  - **Objective:** Complete the C3 coverage.
  - **Components:** the scanner executor
  - **Dependencies:** T148-07.3
  - **Actions:**
    - Support `SalesGrowthScannerPlan` with previous-month and same-month-last-year baselines and the Percent, Positive and Multiple kinds. Other baselines are unavailable.
    - Verify with the real parser output (captured fixture) that «بین ۵ و ۱۰» produces two bound conditions. If not, record it as a limitation; the parser is not changed.
    - Confirm EPS growth, balance-sheet, production, quantity and EBIT criteria return the unavailable answer.
  - **Verification:** Tests in T148-08.5. The limitation, if any, is recorded in `ImplementationReport.md`.

## T148-08 — Testing and Verification

- [x] **T148-08.1 — Unit suite completeness** · DONE (2026-10-31)
  - **Objective:** Every new pure component is covered.
  - **Components:** `tests/FinancialCopilot.UnitTests/CyclicalWavesV2/*`
  - **Dependencies:** T148-01 through T148-07
  - **Actions:** Confirm that the unit tests listed in each task exist and pass. Add any missing tests for: the comparer, statement diff, analysis hash, coordinator, token and auth, DTOs, mapping, growth, the monetary resolver, the availability matrix, analysis matching, the guard and the C1 filter.
  - **Verification:** `dotnet test tests/FinancialCopilot.UnitTests --filter CyclicalWavesV2` is green.

- [x] **T148-08.2 — PostgreSQL integration tests** · DONE (2026-10-31)
  - **Objective:** Real constraints and isolation.
  - **Components:** `tests/FinancialCopilot.IntegrationTests/CyclicalWavesV2/*` (`PostgreSqlIntegrationFixture`, `[SkippableFact]`, disposable PG18 via `FINANCIAL_COPILOT_TEST_POSTGRES_CONNECTION_STRING`)
  - **Dependencies:** T148-01, T148-03, T148-06.7
  - **Actions:** Test the following:
    - Migration `Up`/`Down`.
    - AC-3: legacy row counts unchanged after migration and a fake-client Full run.
    - AC-4 to AC-10 against real unique and FK constraints, including duplicate-prevention attempts.
    - AC-11: concurrent `TryStart` against the partial unique index.
    - AC-23: no cross-schema FK in `information_schema.referential_constraints`.
    - C1: seeded mixed `DerivedMetrics` read through the lookup and scanner.
  - **Verification:** The suite is green on PG18. Skipped tests are reported as skipped, never as passed.

- [x] **T148-08.3 — Admin endpoint tests** · DONE (2026-10-31)
  - **Objective:** v2 Admin behavior and legacy trigger retirement.
  - **Components:** `tests/FinancialCopilot.IntegrationTests` (`WebApplicationFactory`, stub client)
  - **Dependencies:** T148-04.2, T148-05.3
  - **Actions:**
    - v2 endpoints: 403 for non-DataAdmin callers; inline results; `202` then Worker completion; `409` on conflict; status payload.
    - Legacy triggers: `410` with the service spy at zero calls.
    - `provider-health` returns the retired status.
  - **Verification:** Green.

- [~] **T148-08.4 — Chat routing and isolation tests** · PARTIAL (2026-10-31) — end-to-end Chat and Telegram suites added and green (28 tests); every provider requirement is proven, but 5 of the 10 acceptance questions (and Q10 partly) in their exact wording are stopped by pre-existing, provider-independent routing defects (see Verification)
  - **Objective:** AC-14 to AC-17, AC-19 and AC-21.
  - **Components:** unit and integration Chat tests (AI facade V2 path)
  - **Dependencies:** T148-06
  - **Actions:**
    - **CyclicalWaves mode:** P/E, monthly sales, quarterly table and analysis are answered from v2 tables. Spy NoavaranAmin ports (`Companies` reader, `DerivedMetrics` lookup, statement repository, legacy analysis repository) record zero calls. Unsupported questions get the unavailable answer.
    - **NoavaranAmin mode:** an analysis question gets the C2 answer, with zero reads of the legacy or v2 analysis tables.
    - **Both modes:** P/S gauge and relative valuation get the retired answer.
    - **Telegram:** the same rules apply through the shared orchestrator.
  - **Verification (2026-10-31):** `tests/FinancialCopilot.IntegrationTests/Feature148ChatProviderE2ETests.cs`, 28/28 passed. Real pipeline: `POST /api/ai/v1/query` (API key, conversation, MAF V2, semantic routing, provider selection, executors, presentation, billing, persistence), the V1 orchestrator, the Shadow routing mode, and Telegram `POST /api/v1/telegram/assistant/updates` (gateway key, account link, replay). Only the LLM is faked; v2 data is seeded through the real parser and store from recorded provider payloads. Each test asserts values, units, the source label, the entity types read (EF materialization recorder) and zero outbound HTTP (no legacy call, no live call).
    - Q1 monthly sales 639,418,160 million IRR from v2, persisted and replayed; Q3 quarterly statement (provider `CyclicalWavesV2`, sales 1,675,255,385, net profit 471,272,793); Q8 analysis 148001 from v2 only; Q9 answered as unsupported; NoavaranAmin mode returns 123,456,000 and 9.37 (newer legacy rows skipped), excludes the mixed growth row with a valid control kept, returns the C2 message with no analysis-table read; switching `ChatProvider` switches value and source in both directions.
    - Router-supported wordings prove the data paths of Q4–Q7: four stored quarters of net profit; P/E < 5 → شستا, فملی (`pe = 0` not computable); P/S < 1 → فملی, وبملت, کگل; MoM sales growth > 20% → فملی 33.33%, کگل 25%; net-profit growth 291.56% (v2) vs. mixed row excluded (NoavaranAmin).
    - **Not met for the exact wording (pre-existing, identical in NoavaranAmin mode, shared routing code unchanged by Feature 148):** Q2 and Q10 («سود»/«خالص» and product words become unresolvable entity mentions → clarification), Q4 (no statement type → "no data"), Q5/Q6 (`CapabilityRoutingPrecedence` ignores «کمتر از/بیشتر از» → symbol lookup), Q7 (classified as `product_sales_trend` → unavailable). Root amplifier: `LlmQueryInterpretationProposalProvider.Parse` rejects every proposal with `"periodCount": null`, so the LLM never corrects deterministic routing (confirmed empirically: `JsonElement.TryGetInt32()` throws `InvalidOperationException` on a `Null`-kind element; it does not return `false`). These are pinned as characterization tests.
    - **Product Owner decision (2026-10-31): closed with this limitation documented, not fixed here.** General Chat routing defects are repaired in a separate feature, not by expanding Feature 148's scope. See `specs/150-shared-chat-routing-intent-and-entity-resolution-fixes/spec.md` (R1–R6, one per defect above). Feature 148's implementation status is **IMPLEMENTED_WITH_KNOWN_CHAT_ROUTING_LIMITATIONS**; this task remains PARTIAL, not DONE, and is not retroactively marked passed.
    - Feature 148 defects found and fixed by these runs: Feature 116 baseline/growth/multiple columns were empty in the v2 scanner; a "latest" selector carried over by the conversation made v2 P/E "not published".

- [x] **T148-08.5 — Scanner tests** · DONE (2026-10-31)
  - **Objective:** AC-22.
  - **Components:** scanner unit and integration tests
  - **Dependencies:** T148-07
  - **Actions:** Cover each of these with fixtures:
    - «P/E کمتر از ۵» (excludes `pe = 0` and null)
    - «P/S کمتر از ۱»
    - «سود خالص فصل اخیر بیشتر از صفر»
    - «فروش ماه اخیر بیشتر از ۱۰۰ میلیارد تومان» (correct IRR)
    - «رشد فروش فصل اخیر نسبت به فصل قبل بیشتر از ۲۰ درصد»
    - «حاشیه سود ناخالص بیش از ۳۰ درصد»
    - a "between" range
    - a missing unit (clarification)
    - industry and EPS-growth questions (unavailable)
    - MoM and same-month growth
    - Feature 116 baselines
    - provider isolation (spies at zero)
  - **Verification:** Green, with hand-computed expected rows recorded in the fixtures.

- [x] **T148-08.6 — Legacy blocking and architecture tests** · DONE (2026-10-31)
  - **Objective:** AC-1 and AC-2.
  - **Components:** `tests/FinancialCopilot.ArchitectureTests`; legacy client tests
  - **Dependencies:** T148-05
  - **Actions:**
    - Guard present on all 4 legacy clients.
    - Zero sends for each of the 11 endpoint paths with default config.
    - Hosted-worker list asserted.
    - v2 Domain and Application layers have no Infrastructure references.
    - Legacy tests that need real client behavior set `AllowCalls=true` locally.
  - **Verification:** Green.

- [x] **T148-08.7 — Regression suites** · DONE — no new failures; pre-existing failures attributed against a clean HEAD worktree
  - **Objective:** AC-19, AC-20 and AC-25. Existing behavior is preserved.
  - **Components:** the full solution test run
  - **Dependencies:** all
  - **Actions:**
    - Run `dotnet test src/backend/FinancialCopilot.sln --configuration Release` and the frontend build and tests.
    - Update only the tests whose expectations change by approved decision:
      - C1 lookup and scanner winners
      - C2 analysis in NoavaranAmin mode
      - 115 and 125 retired answers
      - the `ComprehensiveAnalysisV2ProductionPath` test rebased to CyclicalWaves mode
      - the hosted-worker list
    - Classify every other failure as either a regression to fix or a pre-existing failure, with evidence (e.g. the 20 known pre-existing unit failures).
  - **Verification:** No unexplained failures. The classification is recorded in the report.

- [x] **T148-08.8 — Operational verification and report** · DONE (2026-10-31)
  - **Objective:** Produce honest delivery evidence.
  - **Components:** `specs/148-…/ImplementationReport.md`
  - **Dependencies:** T148-08.1 through T148-08.7
  - **Actions:**
    - **If v2 credentials are available**, run in a non-production environment:
      - Admin catalog sync, one ticker, analysis full, then Full
      - one manual scheduled-run trigger
      - the CyclicalWaves-mode Chat acceptance set, including the 6 scanner examples
      - record runs, counts and spot checks against the provider
    - **Otherwise**, mark the live items `NOT EXECUTED`.
    - Write the report: completed tasks, changed files, migration, test results, configuration changes, deployment steps (plan.md rollout), the C1 census result, residual items N1/N2 and limitations.
  - **Verification:** The report exists, and every live claim is backed by recorded evidence or marked not executed.

- [x] **T148-08.9 — N1 regression tests** · DONE (2026-10-31)
  - **Objective:** Prove the alert behavior.
  - **Components:** `tests/FinancialCopilot.IntegrationTests/ConditionalTrackerEndpointTests.cs`
  - **Dependencies:** T148-06.9
  - **Actions:** A legacy-dependent `PE_TTM` alert is skipped with its reason and kept; the same alert on a NoavaranAmin value triggers and creates one notification intent; the existing price, alias, limit and NL tests stay green.
  - **Verification:** 8/8 tests of the class passed.

---

**Task count:** 50 = 46 original + 3 N1 + T148-06.10 C1-M (00: 3 · 01: 6 · 02: 4 · 03: 4 · 04: 4 · 05: 6 · 06: 10 · 07: 4 · 08: 9). The earlier summary reported 41 and the per-group figures 42; the file actually contained 46 tasks before N1. Result: 49 done, 1 partial (T148-08.4), 0 blocked.

## Post-Closure Scope Extension — Gauge and Equilibrium APIs

**Extension status:** `FEATURE_148_GAUGE_EQUILIBRIUM_PARTIALLY_VERIFIED`.
**Original Feature 148 status:** unchanged; implemented and closed with known Chat-routing limitations.
PostgreSQL migration and exact-numeric checks, live API contracts, and two live Full syncs were run against disposable PostgreSQL 18 and authorized development provider credentials. The remaining acceptance limitation is real-model execution: the approved gauge queries are returned by a narrow deterministic CyclicalWaves route before the model client is resolved.

- [x] **T148-GE-00 — Explicit implementation authorization gate** — **DONE (2026-11-08)**
  - **Objective:** Record the Product Owner's separate authorization to begin implementation; the design decisions themselves are already approved.
  - **Components:** Feature 148 scope-extension approval record.
  - **Dependencies:** None.
  - **Verification:** User explicitly requested implementation on 2026-11-08; authorization recorded here before source changes.

- [x] **T148-GE-01 — Contract fixtures and v2 client** — **DONE; fixtures, live contracts, 404 handling and shared auth behavior verified**
  - Add fixtures from the authoritative six-page PDF for both catalogs and both detail endpoints,
    including null P/E/P/S objects, nullable close/status, all six day counts, negative equilibrium
    values, 401 and 404.
  - Add dedicated CyclicalWaves v2 DTOs/parser/client for the four documented routes. Do not reuse
    legacy clients, payloads, endpoints, or response mappings.
  - Add `GaugeBaseUrl` as an independent non-secret option; preserve existing `DataBaseAddress`.
  - Register a separate data HttpClient using the existing singleton token service and auth/resilience
    handlers. Prove concurrent 401s do not race refresh rotation and each request is replayed at most once.
  - Verify ticker path escaping exactly once for Persian symbols and direct ISIN path values.

- [x] **T148-GE-02 — Latest-state schema and migration** — **DONE; isolated PostgreSQL Apply/Rollback/Re-apply passed**
  - **Evidence:** Disposable PostgreSQL 18 verified 14 tables total (11 original plus 3 extension tables), 29 original metric definitions, 27 unconstrained `numeric` value chunks, three primary/unique logical keys, three same-schema ticker FKs, and no cross-schema FK. The seeded original ticker and financial-value rows survived rollback/re-apply.
  - Add exactly `GaugeAvailability`, `GaugeDetails`, and `EquilibriumRanges` under `cyclicalwaves`.
  - Reference existing `cyclicalwaves.Tickers`; no new symbol catalog and no FK outside the schema.
  - Use unconstrained PostgreSQL `numeric`, integer `A`–`F` counts, null-preserving columns and unique logical keys. Verify exact .NET/JSON precision in a spike because `decimal` is bounded and the PDF sets no range/scale maximum.
  - Add no historical snapshots. Verify migration up/down/up and query `information_schema` for FK isolation.

- [x] **T148-GE-03 — Ticker identity matching and upsert stores** — **DONE; PostgreSQL persistence, exact values, 404/unmatched behavior and repeated-run row stability verified**
  - **Evidence:** Full provider sync persisted 684 tickers, 684 availability rows, 1,276 gauge-detail rows, 684 equilibrium rows, and 17,293 analyses. The second Full pass preserved the same row counts. The first run had zero gauge/equilibrium detail-fetch failures; unmatched catalog identities were reported explicitly.
  - Match case-insensitive trimmed ISIN first, then Feature 148 normalized Persian/Arabic ticker.
  - Require one canonical ticker row. Do not insert unknown tickers or select an arbitrary duplicate.
  - Report unmatched/ambiguous catalog entries; preserve last-known values on per-symbol 404.
  - Prove nulls and negative values persist, repeated values do not rewrite metric columns, and no duplicate rows appear.
  - A completed catalog refresh updates availability for the complete matched set; catalog absence or
    detail 404 marks availability false but retains the last detail row and reports it.

- [x] **T148-GE-04 — Coordinator, lease and daily schedule** — **DONE; unit coverage and two live Full runs passed on the disposable development database**
  - Extend existing sync scopes, coordinator and `SyncRuns`; do not create another lock, run table or worker.
  - Add gauge catalog/details and equilibrium catalog/details phases to Full and the existing daily schedule.
  - Add single-ticker refresh; preserve global non-overlap, heartbeat, pacing and bounded retries.
  - Verify phase counts distinguish catalog size, matched, unmatched/ambiguous, processed, updated,
    unchanged, missing/404 and failed. Item failures continue; authentication failures fail the run.

- [x] **T148-GE-05 — DataAdmin API and console** — **DONE for implementation and endpoint tests (18/18)**
  - Add DataAdmin-protected catalog and full sync actions for gauge and equilibrium, plus queued
    single-ticker refresh. Keep long fan-out operations on the existing Worker queue.
  - Extend `/admin/data/cyclicalwaves-v2` with progress, status and phase counters/errors.
  - Verify authorization, inline/queued status contracts, `202`/`409` behavior, and accurate status.

- [x] **T148-GE-06 — CyclicalWaves-only Chat exposure** — **DONE for the Chat API route, scripted acceptance and provider-isolation checks; the narrow route intentionally bypasses the LLM**
  - Implement the approved P/E gauge, P/S gauge, and equilibrium range capabilities in Chat when `FinancialData:ChatProvider=CyclicalWaves`.
  - Register only in CyclicalWaves mode; NoavaranAmin never reads these tables. No fallback.
  - Keep Features 114, 125, 126 and 127 disabled. Restore Feature 115 only through the scoped standalone P/S gauge task; keep its historical/chart/industry behavior disabled.
  - Cover source labels, status/null interpretation, IRR presentation, no-data messages and provider isolation.

- [~] **T148-GE-07 — Verification and non-production rollout evidence** — **PARTIAL; database, live-provider, Admin, Chat and regression evidence complete; real-model execution not established**
  - **Evidence:** PostgreSQL, live endpoints, two Full syncs, unit, DataAdmin, Chat/provider-isolation and frontend checks ran. The real model configuration is present, but the gauge/equilibrium route returns deterministically before resolving the model client. No out-of-scope routing change was made to force an LLM call.
  - Run the contract, unit, PostgreSQL, Admin, Chat and regression suites after implementation.
  - Verify migration up/down/up, same-schema FKs, idempotency, unchanged writes, lease exclusion and
    exact provider/Chat source isolation.
  - Only with authorized credentials, run read-only contract checks and non-production Admin/full sync;
    report request, counts and spot-check evidence. Never claim a live run without evidence.

- [x] **T148-GE-08 — Exact numeric representation spike** — **DONE; JSON → DTO → Npgsql/PostgreSQL → retrieval → Chat text serialization passed exactly**
  - **Evidence:** PostgreSQL integration test round-tripped the maximum supported PostgreSQL numeric integer/scale range, values beyond `System.Decimal` precision, large fractions, negative values, very small values, and null close/status. The test also verified unchanged upserts do not add rows or rewrite equal details. No binary floating-point conversion is used.
  - **Objective:** Prove provider decimal precision survives DTO parsing, EF/Npgsql persistence, and API/Chat serialization without binary floating point.
  - **Components:** Gauge/equilibrium DTO parsing, domain value representation, EF numeric mapping, JSON converter, unit and PostgreSQL tests.
  - **Dependencies:** T148-GE-00 and T148-GE-01.
  - **Verification:** PostgreSQL integration tests proved exact large-magnitude and high-scale DTO-to-Npgsql-to-retrieval round trips, Chat answer serialization, null and negative preservation, and exact unchanged upserts. The implementation uses three base-10 `numeric` chunks plus sign and scale because PostgreSQL limits integer digits per numeric value.

- [x] **T148-GE-09 — Approved Chat semantics and provider isolation** — **DONE for scripted V2 semantics and NoavaranAmin table-read isolation**
  - **Objective:** Validate semantic routing and faithful presentation for all three approved Chat capabilities.
  - **Components:** V2 capability routing, Chat read services/presentation, integration tests for CyclicalWaves and NoavaranAmin modes.
  - **Dependencies:** T148-GE-03 and T148-GE-06.
  - **Verification:** Cover eight Persian acceptance examples and equivalent wording; status 1–4/null, boundary and null handling, below-average comparison excludes nulls, IRR units, explicit unavailable output, and zero table reads in NoavaranAmin mode. Do not repair pre-existing general Chat-routing limitations under this extension.

- [x] **T148-GE-10 — Scoped Feature 115 P/S gauge UX compatibility** — **DONE for the latest-state scripted regression; no history/TTM/Forward data returned**
  - **Objective:** Restore only the standalone P/S gauge display backed by the new latest-state rows.
  - **Components:** Existing Feature 115 gauge read/presentation and frontend compatibility tests.
  - **Dependencies:** T148-GE-03 and T148-GE-06.
  - **Verification:** New gauge close/status/bounds are displayed accurately; history, monthly-sales inset, LastPS/TTM and Forward P/S are absent. Features 114, 125, 126 and 127 remain disabled; no legacy API path is called.

**Extension task count:** 11 tasks (T148-GE-00 through T148-GE-10). T148-GE-00 is an authorization gate, not a design-decision gate. Contract/precision precedes storage; storage precedes sync, Admin and Chat; compatibility/regression precedes operational verification. Original Feature 148 task completion history remains unchanged.

### Extension verification record (2026-11-08)

- Unit selection `CyclicalWavesV2|ExactDecimalTests|Feature148`: **210 passed, 0 failed, 0 skipped**.
- CyclicalWaves and NoavaranAmin Chat/provider-isolation selection: **24 passed, 3 failed**. All gauge, Feature 115 and provider-isolation cases in this selection passed; the three failures are the established Q02/Q05/Q06 shared-routing cases.
- CyclicalWaves v2 DataAdmin endpoint checks: **18 passed**.
- PostgreSQL migration, schema, store, exact-numeric and coordinator integration checks: **6 passed, 0 failed, 0 skipped**.
- Live four-endpoint gauge/equilibrium contract and 404 check: **1 passed** (read-only GETs).
- Live Full sync and repeat: **1 passed**. First run reported 20,498 processed, 233 failed as catalog mismatches only (220 gauge, 13 equilibrium), 644 matched gauge details and 500 matched equilibrium details. Both Full runs left the same counts: 684 tickers, 684 availability rows, 1,276 gauge detail rows, 684 equilibrium rows and 17,293 analysis rows. Detail fetches had zero failures. Unmatched catalog entries appeared in `SyncRuns.ErrorSummary`.
- Gauge client fixture checks: **9 passed**; exact-decimal checks: **7 passed** (included in the unit total above).
- Frontend production build: **passed**; existing route-file warning and chunk-size warning were emitted.
- Architecture suite: **14 passed, 1 failed**. The failure is the existing `MigratedSemanticExecutors_DoNotReintroduceLegacySymbolExtraction` assertion on `FinancialStatementTableIntentRules`, already documented against the baseline and unrelated to the extension.
- Chat selection: **24 passed, 3 failed**. Q02 net-profit entity resolution and Q05/Q06 scanner routing match the documented pre-existing shared Chat-routing limitations under T148-08.4. They do not enter the new gauge route and were not changed.
- Real-model check: **not executed**. OpenAI configuration is available, but the approved explicit gauge/equilibrium requests are answered by the deterministic CyclicalWaves route before `ResolveModelClient`; the API E2E tests therefore verify the actual Chat route without invoking a live LLM. Do not treat model interpretation as proven.
- The PostgreSQL test server was isolated on port 55441, its per-test databases were dropped, and the server was stopped. The shared listener on port 5432 and production databases were untouched.
- `git diff --check` found no whitespace errors (Git reported an existing LF/CRLF normalization warning for `design.md`). No files were staged, committed, pushed or deployed. Feature 150 and NoavaranAmin integration files were not changed.
