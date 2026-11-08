# Feature 148 — Design (rev 3, final)

**Bounded context:** CyclicalWaves v2 provider data. It is a supporting subdomain with latest-state and upsert ingestion. It owns the schema `cyclicalwaves`.

**NoavaranAmin boundary:** only the approved changes are made, and nothing else in NoavaranAmin changes.

- **C1:** a read-boundary filter in symbol lookup, the scanner and the professional scanner.
- **C2:** comprehensive analysis is disabled in NoavaranAmin mode.

Shared-infrastructure changes are limited to four things:

1. DI registrations that take effect only in CyclicalWaves mode.
2. One capability-availability check.
3. A guard on legacy clients.
4. Unhosting legacy workers.

## 1. Current architecture findings

Repository discovery was done 2026-10-30/31. Paths are relative to `src/backend/`.

| Area | Finding |
|---|---|
| Legacy HTTP | Four typed clients with base `back1.cyclicalwaves.com` (section `CyclicalWaves`): `CyclicalWavesDataProviderClient`, `CyclicalWavesDataAcquisitionClient`, the P/S operation client, and `ComprehensiveAnalysisBlogClient`. Auth is `{user_name,password}` with a Redis token cache. The 11-endpoint inventory is in impact-analysis §1. |
| Legacy schedules | Hosted in `Worker/Program.cs:124-126`: `ComprehensiveAnalysisDailySyncWorker`, `CyclicalWavesDataAcquisitionWorker`, and `IndustryRelativeValuationCalculationWorker` (the latter `Enabled=true`). The P/S sync and Feature 126 workers are already unhosted. `NadpcoScheduledSync` no longer triggers Feature 125 (`NadpcoScheduledSync.cs:279`). |
| Legacy Admin triggers | `cyclicalwaves/full-sync`, `cyclicalwaves/ps-visualization/sync` (live: `AllowManualSyncWhenWorkerDisabled=true`), `comprehensive-analysis/full-sync`, `comprehensive-analysis/daily-sync`. The frontend does not call any of them. |
| `DerivedMetrics` provenance (C1) | `SourceEvidenceJson`/`DependencyEvidenceJson` are serialized `FinancialSourceEvidence(SourceProvider, …)` collections (`Domain/Financial/Entities/FinancialEntities.cs:26`, `DerivedMetricResultStore.cs:86-87`). **`SourceProvider` is a structured discriminator.** Today `EfCoreSymbolMetricLookupService.cs:313-320` detects legacy CyclicalWaves rows by substring and *prefers* them. Readers of `DerivedMetrics` include symbol lookup, `EfCoreScannerExecutionService`, `ProfessionalScannerUseCases`, and `ConditionalTrackerEvaluationProcessor` (alerts, outside C1). |
| Comprehensive analysis | Chat reaches it through the `comprehensive_analysis` capability and the legacy `query_comprehensive_analysis` tool, via `IComprehensiveAnalysisQueryUseCase` and `IQueryComprehensiveAnalysisUseCase` (legacy tables). `ComprehensiveAnalysisPublishedDetector` (`MarketInsightDetectors.cs:367`) reads the legacy tables only. |
| Scanner | `LlmScannerQueryParser`, then `ScannerQueryPlan(Conditions: MetricReference + ConditionOperator{GT,GTE,LT,LTE,Equal,NotEqual} + decimal Threshold, OriginalUserQuery, SalesGrowth?)`, then `IScannerExecutionService`. The threshold has **no unit**: the existing executor compares the raw number with stored values. The plan has no "Between" operator; ranges are two conditions. |
| Chat | V2 workflow. `ICanonicalQueryEntityResolver` → `SemanticCapabilityDispatcher` → executor by capability code. There is also a legacy regex/tool path. Telegram shares the orchestrator. |
| Reuse | `CyclicalWavesUtcCronSchedule`, the Nadpco coordinator and run-lease pattern, `FinancialProviderResilienceHandler`, `PersianSymbolNormalizer`, `IHtmlTextStripper`, the `DataAdmin` policy, and the Data Management console. |
| Unrelated | Feature 139 `TahlilAppOptions` (`api.tahlilapp.com`, end-user identity) is separate and not touched. |

## 2. Architecture

```
Domain          Financial/CyclicalWavesV2/  ScreenerMetricCatalog (29 defs), ScreenerValueComparer, MonetaryAmountUnits
Application     FinancialData/CyclicalWavesV2/  ports + CyclicalWavesV2SyncCoordinator + records
                Scanner/CyclicalWavesV2/        ScreenerMetricMapping, MonetaryThresholdResolver (pure, deterministic)
                AI/Orchestration/ChatCapabilityAvailability.cs
Infrastructure  Financial/Providers/CyclicalWavesV2/   options, token service, auth handler, client, DTOs
                Financial/Ingestion/CyclicalWavesV2/   stores, run store, Chat read adapters, scanner executor
                Financial/Ingestion/Persistence/       CyclicalWavesV2 configurations (schema "cyclicalwaves") + migration
                Financial/Providers/CyclicalWaves/LegacyCyclicalWavesCallBlockingHandler.cs
                Financial/Scanner/LegacyCyclicalWavesDerivedMetricFilter.cs   (C1)
Worker          CyclicalWavesV2SyncWorker
API             CyclicalWavesV2AdminController (api/v1/admin/cyclicalwaves-v2)
Frontend        Data Management "CyclicalWaves v2" page
```

## 3. Database: schema `cyclicalwaves` in `FinancialIngestionDbContext`

- Tables are mapped with `ToTable(name, "cyclicalwaves")`.
- The migration starts with `EnsureSchema`. `__EFMigrationsHistory` stays in `public`.
- No foreign key leaves the schema.
- All amounts are `numeric(38,6)`.

| Table | Columns | Keys / constraints |
|---|---|---|
| `Tickers` | `Id uuid`, `Ticker varchar(64)`, `NormalizedTicker`, `Enticker varchar(16) null`, `IsActive`, `CreatedAt`, `UpdatedAt`, `LastSeenInCatalogAt` | unique `Ticker`; index `NormalizedTicker`; partial unique `Enticker` |
| `MetricDefinitions` | `Id smallint` (seeded 1–29), `FieldKey`, `NameEn`, `NameFa`, `Category`, `Unit`, `SortOrder` | unique `FieldKey`; check on `Unit`; `HasData` seed |
| `FinancialValues` | `TickerId`, `MetricDefinitionId`, `NumericValue null`, `UpdatedAt` | PK (`TickerId`,`MetricDefinitionId`); FK → `Tickers`, `MetricDefinitions` |
| `ScreenerMetadata` | `TickerId`, `LastMonthSaleDate date null`, `LastQuarterDate date null`, `ProviderUpdatedAt null`, `LastChangedAt`, `LastSyncedAt` | PK/FK `TickerId` |
| `IncomeStatementLineDefinitions` | `Id int` (provider `key`), `NameEn`, `NameFa`, `UpdatedAt` | PK `Id` |
| `IncomeStatements` | `Id uuid`, `TickerId`, `PeriodEndingDate date`, `FiscalYear`, `FiscalMonth`, `Audited null`, `PublishDate null`, `CodalUrl null`, `CreatedAt`, `UpdatedAt` | unique (`TickerId`,`PeriodEndingDate`,`FiscalMonth`); check `FiscalMonth IN (3,6,9,12)` |
| `IncomeStatementValues` | `StatementId`, `LineDefinitionId`, `SortOrder`, `Quarter`, `YearToDate`, `TrailingYear` (null allowed) | PK (`StatementId`,`LineDefinitionId`); FK cascade to statement; FK to definition |
| `Analyses` | `Id bigint` (provider id), `Title varchar(500)`, `Summary text null`, `PlainTextSummary text null`, `PublishedAt timestamptz`, `ContentHash char(64)`, `FirstSyncedAt`, `LastSyncedAt`, `UpdatedAt` | PK `Id`; index `PublishedAt` |
| `AnalysisCategories` | `AnalysisId`, `ProviderCategoryId`, `Title` | PK pair; FK cascade |
| `AnalysisTags` | `AnalysisId`, `ProviderTagId`, `Name`, `NormalizedName` | PK pair; index `NormalizedName`; FK cascade |
| `SyncRuns` | `Id`, `Scope`, `TickerFilter`, `AnalysisMode`, `TriggerSource`, `Status`, `RequestedAt`, `StartedAt`, `CompletedAt`, `TotalItems`, `ProcessedItems`, `UpdatedItems`, `UnchangedItems`, `FailedItems`, `ErrorSummary` (≤ 4 KB, sanitized), `LockOwner`, `LockLeaseExpiresAt`, `RequestedBy` | partial unique index where `Status='Running'` (one running sync) |

**Upsert rules:**

- **Statements:** the key is (`TickerId`, `PeriodEndingDate`, `FiscalMonth`), and restatements update in place. If the same key appears twice in one response, the row with the latest `publishDate` wins and a warning is logged.
- **Line definitions:** an unknown line key creates its own definition from the statement line.
- **Analyses:** `ContentHash` is SHA-256 over the title, summary, `published_at`, and the sorted ids and names of categories and tags. It is used only for change detection.

## 4. Provider contract (v2)

| # | Call | Notes |
|---|---|---|
| 1 | `POST https://auth.cycfx.com/api/auth/login` | Headers `Content-Type: application/json` and `x-client-id: default`; body `{identifier,password}`. Returns `201 {access_token, refresh_token, used_secondary_password}`. Rate limit: 10 requests per minute. |
| 2 | `POST .../auth/refresh` | Body `{refreshToken}`; returns `201` with a new token pair. Old refresh tokens are valid for 2 more minutes; reusing one after that invalidates the login. |
| 3 | `GET https://api.tahlilapp.com/api/custom-filtering/tickers` | `string[]` of Persian tickers. |
| 4 | `GET .../custom-filtering/ticker/{ticker}` | `ticker`, `enticker`, 29 metrics, `lastMonthSaleDate`, `lastQuarterDate` (`"YYYYMMDD"`), and system fields. `id` is ignored. `404` means the ticker is unknown. |
| 5 | `GET .../income-statements/ticker/{ticker}?limit=N` | `{ticker, enticker, total, statements:[{periodEndingDate, fiscalYear, fiscalMonth, audited?, publishDate, codalUrl, lines:[{key,name,nameFa,quarter?,yearToDate?,trailingYear?}]}]}`. `404` and `400` as documented. |
| 6 | `GET .../income-statements/lines` | 82 lines. **The response shape is undocumented**; the parser is tolerant, and the shape is verified in T148-00.3. |
| 7 | `GET .../blogs?filter[type]=analysis&page&paginate≤100[&filter[from_date]&filter[to_date]&filter[tags][]&filter[search]]` | `{data:[{id,title,summary,published_at,categories[{id,title}],tags[{id,name}]}], links, meta}`. Dates use `YYYY-MM-DD` (as in the examples); tag names contain underscores. Tag-to-ticker linkage and date format are verified in T148-00.3. |

**Value semantics:**

- All money is IRR, including EPS. Expenses are negative.
- Margins are percentages.
- `pe = 0` means "not computable", and `null` means unknown.
- The trailing-year value for key 86 is always null.
- The documents disagree on token lifetime (5 vs 15 minutes); §6 covers this.

## 5. Ingestion

`CyclicalWavesV2SyncCoordinator` is shared by the Worker and Admin. It runs these scopes:

| Scope | Steps |
|---|---|
| Catalog | Fetch tickers; insert new, mark missing inactive, reactivate returning. |
| Ticker | Requires an existing catalog row; runs `ApplyScreener` and `ApplyIncomeStatements` for that ticker. |
| Screener | `ApplyScreener` for each active ticker. |
| IncomeStatements | Fetch line definitions, upsert them, then `ApplyIncomeStatements` for each active ticker. |
| Analysis | Incremental or Full page walk, then `ApplyAnalysesPage` per page. |
| Full | Catalog, then line definitions, then screener and statements per ticker, then Analysis (Full when the store is empty, otherwise Incremental). |

**Run-level rules:**

- A global lease is taken first. If another run holds it, the run is recorded as `SkippedAlreadyRunning`.
- A failed item is counted and the run continues.
- The lease is renewed periodically.
- Provider calls are sequential, with a delay between them (`RequestDelayMilliseconds`, default 300). Timeouts are per attempt. 5xx, 408 and timeouts are retried with jittered backoff, using the existing resilience handler. 4xx responses are never retried.

**`ApplyScreener`** runs in one transaction per ticker:

- Compare all 29 values by decimal equality (`null` equals `null`). Insert missing rows and update only changed ones.
- Update the date fields only when they change. Always stamp `LastSyncedAt`.
- Set `Enticker` when it is null. A conflicting value is logged, counted as a failure, and the stored value is kept.

**`ApplyIncomeStatements`** runs in one transaction per ticker:

- Insert new periods.
- For existing periods, update the header and diff values by line key.
- Never delete a period that is absent from the response.

**`ApplyAnalysesPage`** runs in one transaction per page:

- Upsert by provider id. When the hash changes, update the article and replace its categories and tags.
- Never delete an article.
- Full mode reads all pages with no date filter.
- Incremental mode uses `from_date` = Tehran date of the highest stored `PublishedAt` minus `AnalysisIncrementalOverlapDays` (2), in `YYYY-MM-DD`.
- Both modes use `paginate=100` and stop at `meta.last_page`.

## 6. Authentication

`CyclicalWavesV2TokenService` is one singleton per process, with single-flight access.

1. Expiry comes from the JWT `exp` claim, with a 4-minute fallback.
2. Within `RefreshSkewSeconds` (60) of expiry, the service refreshes and stores the new refresh token immediately.
3. If refresh returns 401, the service logs in once. A failed login raises `ProviderAuthenticationFailed`, with no retry loop.

`CyclicalWavesV2AuthHandler` handles a 401 from any call with one refresh and one replay. Tokens are not shared through Redis.

Credentials come from `CyclicalWavesV2__Identifier` and `CyclicalWavesV2__Password` (compose: `CYCLICALWAVES_V2_IDENTIFIER` and `CYCLICALWAVES_V2_PASSWORD`). They are required only when ingestion is enabled. Credentials and tokens are never logged.

## 7. Worker

`CyclicalWavesV2SyncWorker` is always hosted and is gated by `IngestionEnabled` and `DailySyncEnabled`.

- **Schedule:** a Full run on `DailySyncCronUtc` (default `30 23 * * *`), using `CyclicalWavesUtcCronSchedule`.
- **Startup catch-up:** a Full run if none has completed since the last scheduled occurrence.
- **Requested runs:** every 30 seconds it polls for `Requested` runs.
- **Expired leases:** a run whose lease expired is marked `Failed (LeaseExpired)`.

## 8. Admin

Routes live under `api/v1/admin/cyclicalwaves-v2`, with the `DataAdmin` policy and the `AuthenticatedActor` rate limit.

| Method and path | Mode |
|---|---|
| `POST catalog/sync` | inline |
| `POST tickers/{ticker}/sync` | inline |
| `POST screener/sync` | queued, returns `202` |
| `POST income-statements/sync` | queued, returns `202` |
| `POST analysis/sync?mode=incremental\|full` | queued, returns `202` |
| `POST sync` | queued, returns `202` |
| `GET status?recent=10` | read |

If a run is already active, the call returns `409` with that run's id.

On the frontend:

- A new page and navigation item in the Data Management shell, with six actions, a ticker input, and a recent-runs table showing errors.
- The legacy "CyclicalWaves" overview card is relabeled "legacy (retired)".

## 9. Configuration

```jsonc
"FinancialData": {
  "ChatProvider": "NoavaranAmin"                           // NoavaranAmin | CyclicalWaves; C1/C1-M exclusion has no setting
},
"CyclicalWavesV2": {
  "AuthBaseAddress": "https://auth.cycfx.com/api/", "DataBaseAddress": "https://api.tahlilapp.com/api/",
  "ClientId": "default", "Identifier": "", "Password": "",
  "IngestionEnabled": false, "DailySyncEnabled": false, "DailySyncCronUtc": "30 23 * * *",
  "IncomeStatementLimit": 12, "AnalysisPageSize": 100, "AnalysisIncrementalOverlapDays": 2,
  "RequestDelayMilliseconds": 300, "TimeoutSeconds": 30, "RetryCount": 2,
  "RefreshSkewSeconds": 60, "RequestedRunPollSeconds": 30, "LeaseMinutes": 30
},
"CyclicalWavesLegacy": { "AllowCalls": false }
```

## 10. Chat

### 10.1 Capability availability (single owner)

`IChatCapabilityAvailability` is a singleton in the Application layer, built from configuration:

| Capability | NoavaranAmin mode | CyclicalWaves mode |
|---|---|---|
| `symbol_lookup` | existing path, with the C1 filter | v2 lookup |
| `financial_statement_table` (quarterly) | existing | v2 statements |
| `scanner` | existing path, with the C1 filter | v2 scanner (§10.5) |
| `comprehensive_analysis` | **unavailable (C2)** | v2 analyses |
| `ps_gauge_visualization`, `industry_relative_valuation_read` | **retired** | **retired** |
| all other capability codes | existing | unavailable |

These are the three answers the policy returns:

| Outcome | Persian message |
|---|---|
| Retired | «این قابلیت به‌دلیل توقف منبع دادهٔ قبلی غیرفعال شده است.» |
| Unavailable (CyclicalWaves mode) | «این پرسش با دادهٔ منبع فعال (CyclicalWaves) قابل پاسخ نیست.» |
| C2 (NoavaranAmin mode) | «تحلیل جامع در منبع دادهٔ فعال در دسترس نیست.» |

The policy is enforced in three places:

1. `SemanticCapabilityDispatcher`, before the executor runs.
2. The legacy workflow path: direct use-case branches are skipped and agent tools are not registered. In NoavaranAmin mode this also covers `query_comprehensive_analysis`.
3. The Feature 141 fallback, which is off in CyclicalWaves mode.

There is no fallback between providers.

### 10.2 DI (CyclicalWaves mode only)

These Chat-only ports are swapped. NoavaranAmin registrations are untouched, and `CompanyResolverService` is never swapped.

| Port | Implementation | Reads |
|---|---|---|
| `ICanonicalQueryEntityResolver` (+ industry) | `CyclicalWavesV2CanonicalEntityResolver` | `Tickers`: exact normalized ticker, then ISIN, then the existing fuzzy match. Industry resolution always returns NotFound. |
| `ISymbolMetricLookupService` | `CyclicalWavesV2SymbolMetricLookupService` | `FinancialValues`, `ScreenerMetadata`, and the latest statement for EPS |
| `IQuarterlyFinancialReportsQueryUseCase` | `CyclicalWavesV2QuarterlyIncomeStatementUseCase` | `IncomeStatements`, `IncomeStatementValues` |
| `IComprehensiveAnalysisQueryUseCase`, `IQueryComprehensiveAnalysisUseCase` | `CyclicalWavesV2ComprehensiveAnalysisQuery` | `Analyses`, tags, categories |
| `IScannerExecutionService` | `CyclicalWavesV2ScannerExecutionService` | `Tickers`, `FinancialValues`, `ScreenerMetadata` |

Entity identity in CyclicalWaves mode:

- `CanonicalId` is `Tickers.Id`.
- `ExternalCompanyId` is `"cw2:{Id}"`.
- `CompanyName` is the ticker, because the API has no company names (L1).

### 10.3 Lookup mapping

The lookup uses spec 073's `MetricCode` and `SymbolLookupPeriodSelector`.

| MetricCode | Latest | Previous | Same period last year | Other |
|---|---|---|---|---|
| `REVENUE` | `lastQuarterSale` | `penultimateQuarterSale` | `lastYearSameQuarterSale` | `AVG_4Q_REVENUE` → `average4QuarterSale` |
| `NET_PROFIT` / `GROSS_PROFIT` / `OPERATING_PROFIT` | `lastQuarter…` | `penultimateQuarter…` | `lastYearSameQuarter…` | |
| `NET_/GROSS_/OPERATING_PROFIT_MARGIN` | `lastQuarter…Margin` | `penultimateQuarter…Margin` | `lastYearSameQuarter…Margin` | |
| `MONTHLY_SALES` | `lastMonthSale` | `penultimateMonthSale` | `lastYearSameMonthSale` | |
| `AVG_12M_MONTHLY_SALES` | `average12MonthSale` | | | `LastYearAverage12Month` → `lastYearAverage12MonthSale` |
| `PE_TTM` / `PS_TTM` | `pe` / `ps` | | | |
| `EPS` | statement key 85 of the latest period: `quarter`, or `trailingYear` for TTM | | | |

Other codes return Missing with an unavailable warning. No value is ever substituted from another period or provider.

Presentation:

- Money is shown in «میلیون ریال», the current lookup convention: IRR ÷ 10^6.
- EPS is shown in IRR.
- `pe = 0` is shown as «قابل محاسبه نیست».

### 10.4 Comprehensive analysis (CyclicalWaves mode)

Retrieval is deterministic:

1. Symbols are resolved against `Tickers`.
2. An article matches a ticker when one of its `AnalysisTags.NormalizedName` values equals the normalized ticker, or when the first segment of its normalized title (before ` - ` or ` – `) equals it.
3. The 7 documented categories map to stored tag names. Spaces become `_`, and `نمودار P/E` becomes `P-E` and `نمودار P/S` becomes `P-S`; both are verified in T148-00.3.
4. Results are ordered by `PublishedAt` descending, with a default limit of 5 and a maximum of 10.

Each result fills the existing `ComprehensiveAnalysisSummaryItem`:

| Field | Value |
|---|---|
| `AnalysisId` | provider id |
| `PersianCreatedAt` | Jalali form of `PublishedAt` in Tehran time |
| `AuthorName` | category titles |
| `PlainTextSummary` | the stripped summary |
| `TagNames` | tag names |

Presentation and the tool description state that only the title, published summary and date are available. An article with an empty summary is listed by title and date.

### 10.5 Basic scanner (C3)

The parser, validator, ranker, column policy and capability/tool wiring are reused unchanged. The executor `CyclicalWavesV2ScannerExecutionService` takes over in CyclicalWaves mode.

1. **Universe.** All active tickers with screener metadata. A non-null `Universe.IndustryCode` makes the query unavailable.
2. **Metric map.** `ScreenerMetricMapping` is shared with lookup, so there is one mapping table.

   | Kind | Codes | Source |
   |---|---|---|
   | Direct | `PE_TTM`, `PS_TTM`, `REVENUE`, `NET_PROFIT`, `GROSS_PROFIT`, `OPERATING_PROFIT`, the 3 margins, `MONTHLY_SALES`, `AVG_12M_MONTHLY_SALES`, `AVG_4Q_REVENUE` | latest field |
   | Growth | `REVENUE`, `NET_PROFIT`, `GROSS_PROFIT`, `OPERATING_PROFIT` `_GROWTH_QOQ` | `lastQuarter…` vs `penultimateQuarter…` |
   | Growth | the same four `_GROWTH_YOY` codes | `lastQuarter…` vs `lastYearSameQuarter…` |
   | Growth | `MONTHLY_SALES_GROWTH_MOM` | `lastMonthSale` vs `penultimateMonthSale` |
   | Growth | `MONTHLY_SALES_GROWTH_YOY` | `lastMonthSale` vs `lastYearSameMonthSale` |

   Growth uses the **existing Domain growth calculator and non-positive-base policy**, verified in T148-07.1.

   Feature 116 `SalesGrowth` plans work for previous-month and same-month-last-year baselines, with Percent, Positive and Multiple kinds. Other baselines are unavailable.

   Anything else is unavailable: industry, balance sheet, production, quantity, `EBIT_*`, `EPS_GROWTH_*`, and multi-period trends.
3. **Operators.** All six `ConditionOperator` values are supported, and conditions are ANDed. "Between" means two bound conditions on the same metric; T148-07.4 verifies that the parser emits them.
4. **Monetary thresholds.**
   - The parser threshold has no unit, so a deterministic `MonetaryThresholdResolver` (Application layer, used only in CyclicalWaves mode) reads `plan.OriginalUserQuery`.
   - It normalizes Persian and Arabic digits and separators, then finds `number + scale + currency` expressions. Scales are هزار, میلیون, میلیارد, هزار میلیارد and همت (= 10^12 toman). Currencies are ریال and تومان (toman = 10 IRR).
   - It matches each expression to a monetary condition by numeric value and converts the threshold to IRR.
   - If a non-zero monetary condition has no matching unit, or the match is ambiguous, the answer is a clarification asking for the unit. No unit is ever assumed. A threshold of 0 needs no unit.
5. **Percent and ratio thresholds.** Margins and growth are compared as percent numbers. P/E and P/S are compared as ratios.
6. **Exclusions.** A `null` operand never matches and is counted as missing. `pe = 0` never matches any `PE_TTM` condition and is displayed as «قابل محاسبه نیست».
7. **Output.** The existing `ScannerTableResult` shape, with ticker rows and the requested columns.
   - Per-row period labels come from `lastQuarterDate` and `lastMonthSaleDate`.
   - Mixed periods are flagged in execution facts.
   - Monetary columns follow the existing scanner display convention, converted at presentation only.
   - Ranking reuses `IScannerResultRanker`.

All arithmetic is deterministic C#; the LLM never computes numbers.

### 10.6 C1 filter (NoavaranAmin reads)

- `LegacyCyclicalWavesDerivedMetricFilter` is provider-neutral Infrastructure code. It parses `SourceEvidenceJson` and `DependencyEvidenceJson`.
- A row counts as legacy when any evidence item has `SourceProvider == ProviderSources.CyclicalWavesName`, including mixed CyclicalWaves + NoavaranAmin rows (C1-M, permanent).
- A row with no recorded provider counts as legacy when its `CalculationPolicyVersion` is one of the four registered policies fed only by legacy CyclicalWaves line items; unparseable evidence is excluded (provenance undetermined).
- The filter is a static rule with no configuration; the professional scanner's availability and freshness check also uses it.
- The filter applies to rows loaded by `EfCoreSymbolMetricLookupService`, `EfCoreScannerExecutionService` and `ProfessionalScannerUseCases`. It runs **before** the latest-per-company/metric selection, so an older valid NoavaranAmin row can still be chosen and is labeled with its own period.
- When no valid row remains, the existing Missing or unavailable result is returned.
- There is no flag: the former `ExcludeLegacyCyclicalWavesDerivedMetrics` and `LegacyCyclicalWavesMixedProvenance` settings were removed by C1-M, and binding them has no effect.
- No `DerivedMetrics` row, formula or ingestion path changes. The existing substring-based CyclicalWaves layout code becomes unreachable and is left in place.
- **Gate (T148-06.6).** A read-only census compares the structured classification with the current substring marker, and counts mixed-provider rows and rows marked only through `CalculationPolicyVersion`. Any disagreement or mixed row pauses the task for Product Owner review.

## 11. Legacy retirement

1. **Guard.** `LegacyCyclicalWavesCallBlockingHandler` is the first handler on all 4 legacy clients. Unless `CyclicalWavesLegacy:AllowCalls=true`, it throws `ProviderRetired` before any I/O. An architecture test asserts the guard is present.
2. **Schedules.** The `AddHostedService` lines for `ComprehensiveAnalysisDailySyncWorker`, `CyclicalWavesDataAcquisitionWorker` and `IndustryRelativeValuationCalculationWorker` are removed. The classes stay, and the architecture test's hosted list is updated. `IndustryRelativeValuation:Enabled` and `CyclicalWavesPsSync:AllowManualSyncWhenWorkerDisabled` are set to `false`.
3. **Admin triggers.** These return `410 Gone` (`ProviderRetired`) before calling any service:
   - `cyclicalwaves/full-sync`
   - `cyclicalwaves/ps-visualization/sync` (and its provider-calling dry-run, if any)
   - `comprehensive-analysis/full-sync`
   - `comprehensive-analysis/daily-sync`

   Read-only and local endpoints are kept: `sync-runs`, `backfill-plain-text`, `backfill-company-id`, and the P/S snapshot and history reads. `provider-health` returns the retired status. Explicit `data-sync` requests for the CyclicalWaves provider return `410`. Data-sync requests with an empty provider name still resolve to the legacy default client and fail fast at the guard.
4. **Capabilities.** Retired in both modes per §10.1. `ComprehensiveAnalysisPublishedDetector` is unregistered from DI; its historical insight events stay.
5. **Data and specs.** No legacy row is touched. Superseded headers are added as listed in impact-analysis §4.

## 12. Testing approach

- **Unit:**
  - catalog, comparer and diff
  - coordinator
  - token and auth handler
  - DTO parsing from PDF fixtures
  - lookup and scanner mapping
  - monetary threshold resolver
  - growth via the Domain calculator
  - availability matrix for both modes
  - analysis matching
  - blocking handler
  - C1 filter
- **PostgreSQL integration** (disposable PG18): migration up and down, store behavior, the FK-isolation check, and the C1 filter against seeded mixed `DerivedMetrics`.
- **Endpoint:** v2 Admin flows, and `410` on the legacy triggers.
- **Chat and scanner:** CyclicalWaves mode with spy ports that must record zero reads; NoavaranAmin mode with C2 unavailable and retired capabilities; plus the NoavaranAmin regression suites.

## 13. Product Owner decisions (final)

| ID | Final decision |
|---|---|
| D1 | `CyclicalWavesV2` configuration section |
| D2 | `cyclicalwaves` schema |
| D3 | Superseded by C1 = B |
| D4 | Superseded by C3 = A |
| D5 | Retire all legacy endpoints, keep all records, disable legacy-only capabilities |
| D6 | Process-local tokens |
| D7 | Admin panel included |
| D8 | Configurable cron, default 23:30 UTC |
| D9 | Comprehensive analysis in scope |
| **C1** | **B**: exclude legacy CyclicalWaves-derived rows from NoavaranAmin lookup and scanner reads, using the structured discriminator and a census gate |
| **C2** | **C**: comprehensive analysis disabled in NoavaranAmin mode; legacy detector disabled |
| **C3** | **A**: basic CyclicalWaves scanner, as in §10.5 |

No decisions remain open. Open technical verifications are listed in plan.md (Blockers).

## Post-Closure Scope Extension — Gauge and Equilibrium APIs

**Status:** `FEATURE_148_GAUGE_EQUILIBRIUM_EXTENSION_DESIGN_APPROVED_FOR_IMPLEMENTATION`.
This design is an additive proposal only. It does not alter the closed Feature 148 implementation or
authorize production code, migrations, configuration edits, database activity, live imports, Chat
routing changes, commits, pushes, or deployment.

### Authoritative API contract

The six-page provider PDF is copied unchanged to
[`docs/providers/cyclicalwaves-v2/gauge-equilibrium-api-client.pdf`](../../../docs/providers/cyclicalwaves-v2/gauge-equilibrium-api-client.pdf).
The source contract is summarized in
[`gauge-equilibrium-api-contract.md`](../../../docs/providers/cyclicalwaves-v2/gauge-equilibrium-api-contract.md).
The PDF identifies four GET routes below `https://api.tahlilapp.com/api/` and uses the current
`https://auth.cycfx.com/api/` login/refresh service, `x-client-id: default`, and bearer auth for every
data call. Its access token is valid for 15 minutes. Each refresh rotates `refresh_token`; the prior
refresh token is only accepted for two additional minutes. The PDF says one read per day is sufficient.

### Client and token lifecycle

Add a focused v2 gauge/equilibrium data client and DTO/parser types; do not route these requests
through `CyclicalWavesDataProviderClient`, `CyclicalWavesRelativeValuationPayloadModels`, or any old
response mapper. Keep the current `CyclicalWavesV2Options:DataBaseAddress` unchanged and add the
non-secret `GaugeBaseUrl` option with the PDF data host as its default. Register a distinct
named/typed `HttpClient` using that base address and the existing `CyclicalWavesV2AuthHandler` and
`CyclicalWavesV2ResilienceHandler`.

`CyclicalWavesV2TokenService` is already registered singleton per process and gates both token reads
and renewals with one `SemaphoreSlim`. Both data clients must use this exact singleton. A simultaneous
401 with the same rejected access token is serialized: the first caller renews and stores the rotated
refresh token before returning; later callers compare their rejected access token with the new one and
reuse it rather than submitting the old refresh token. Proactive renewal uses the JWT expiry with the
existing 60-second skew, leaving margin before the provider's two-minute old-token window. The handler
replays each data request at most once. The Worker owns background data calls and the database run
lease serializes scheduled/admin sync across Worker replicas. Do not create a second token service or
credentials. Chat reads persisted data and does not call the provider directly.

### Schema and identity

Add three latest-state entities only in `FinancialIngestionDbContext` and schema `cyclicalwaves`:

| Entity | Candidate columns and key | Query/index intent |
|---|---|---|
| `GaugeAvailability` | `TickerId` PK/FK, `HasPe`, `HasPs`, `LastFetchedAtUtc` | One row per existing symbol; catalog and Admin status reads. |

| `GaugeDetails` | `TickerId`, `GaugeType` (`PE`/`PS`), `A`–`F` integer counts, nullable `Close`, decimal `Start`, `End`, `Min`, `Avg`, `Max`, nullable `Status`, `FetchedAtUtc`; unique `(TickerId, GaugeType)` | One current gauge per ticker/type; symbol display lookup. |
| `EquilibriumRanges` | `TickerId` PK/FK, `HasRange`, nullable IRR `Min`, `Avg`, `Max`, `FetchedAtUtc` | One current range per ticker; catalog presence and last-known values share this row. |

All foreign keys reference `cyclicalwaves.Tickers` only. Existing ticker identity is canonical: match a
nonempty ISIN first using ordinal case-insensitive comparison; if absent, use the existing Persian
symbol normalizer (including Arabic/Persian Yeh and Kaf folding) and match `NormalizedTicker`. Require
one match. Do not insert/merge/update the master ticker catalog from these secondary catalogs. Record
unmatched and ambiguous rows in sanitized phase summaries and skip them. Exact duplicate ISIN or
normalized names in the current master are a data-quality issue to report, not an excuse to pick one.

Each completed catalog refresh updates availability for the entire matched set. A symbol that
disappears from a later catalog is marked unavailable while its last detail rows are retained;
current reads must respect availability and must not present retained values as current. If a stale
catalog entry returns 404 on its detail call, mark that specific gauge/range unavailable and report
the catalog/detail mismatch without deleting the last detail values.

For detail requests, prefer the matched row's ISIN, otherwise its stored canonical Persian ticker.
Pass the raw canonical identifier to the URI builder once and call `Uri.EscapeDataString` exactly
once. Do not pre-encode in the coordinator or encode an already encoded value.

Store `A`–`F` as integer day counts, never as ratios. Store gauge ratios and IRR amounts as PostgreSQL
`numeric`, never `real`, `double precision`, or C# `float`/`double`. The contract demonstrates four
fractional digits for gauges and two for IRR examples, but gives no upper bound or rounding contract;
choose a precision/scale after Product Owner/provider confirmation, or use unconstrained PostgreSQL
`numeric` to preserve arbitrary JSON decimal precision. Keep only documented nullable values nullable:
the `pe`/`ps` detail objects can be null; `close` and `status` can be null. Missing required values
are invalid provider payloads, not zeros. Do not add a strict midpoint check that could reject
provider rounding; application validation may flag a range whose `avg` is outside `[min,max]`.

Do not add history or snapshots. Unchanged upserts compare all provider fields and update only fetch
timestamps according to an explicit freshness policy; avoid rewriting financial values solely because
a sync ran. Retaining every daily value is a separate decision with storage-growth and time-series
query costs, and is not part of this proposal.

### Sync and Admin extension

Extend the existing `CyclicalWavesV2SyncScope`, `CyclicalWavesV2SyncCoordinator`, `SyncRuns` table,
EF sync-run store, and single hosted `CyclicalWavesV2SyncWorker`. Proposed independent scopes are
`GaugeCatalog`, `Gauge`, `EquilibriumCatalog`, `Equilibrium`, and `TickerGaugeEquilibrium`; extend the
existing `Full` scope to include both catalogs/details. Preserve the one global lease and existing
request pacing, bounded retry, heartbeat, startup catch-up and daily schedule. Do not create another
worker, run table, lock or migration schema.

For each catalog, persist resolved availability after matching. For each flagged gauge, fetch details
using the resolved identity; persist one row for every non-null nested gauge object. The details
response's explicit null object is no gauge detail and is not a numeric zero. For equilibrium, fetch
only catalog members and persist the full three-value response. A per-symbol 404 means “not found/no
data”: retain the last known row, count a failed/missing item, and report it; never write zeros or
delete prior data. Authentication/configuration failures fail the run; other item failures continue
and produce `CompletedWithErrors`. No undocumented request-rate quota is assumed; reuse the current
300 ms configurable delay and bounded resilience policy unless provider evidence justifies a change.

Extend the existing `/admin/data/cyclicalwaves-v2` UI and DataAdmin controller. Catalog-only actions
may follow the existing inline catalog convention if measured short; full catalog/detail sync and
single-ticker refresh should be queued because they fan out across symbols. Status uses existing
`SyncRuns` counters with phase-specific sanitized details and counts for catalog size, matched,
unmatched/ambiguous, processed, updated, unchanged, missing/404 and failed. The single-ticker action
must queue the combined gauge/equilibrium refresh under the same global lease.

### Chat boundary and capability matrix

| Capability | New API support | Approved Chat behavior |
|---|---|---|
| P/E gauge | Current gauge distribution, status and historical day counts | Available only in CyclicalWaves mode, reading new tables. |
| P/S gauge | Same | Available only in CyclicalWaves mode, reading new tables; old chart/history remains unsupported. |
| Equilibrium range | Current IRR lower bound, midpoint and upper bound | Available only in CyclicalWaves mode, reading new tables. |
| Industry-relative valuation | No cohort/peer comparisons are documented | Keep retired. |
| Features 114, 115, 125, 126, 127 beyond those exact data fields | Not established | Keep disabled; assess each feature's remaining inputs and UX separately. |

NoavaranAmin remains unchanged and never reads these tables. There is no provider fallback or merge.
Chat preserves nulls, distinguishes close/min/avg/max/start/end/status, treats a-f as counts, states
equilibrium in IRR, and does not imply a guaranteed future price or recommendation. Unsupported data
returns an explicit unavailable response. Historical charts, industry rankings and predictions remain
disabled; the only scoped legacy UX approved here is Feature 115 standalone P/S gauge.

### Product Owner decisions (approved; implementation authorization remains separate)

1. Approved this scope and the latest-state tables/migration.
2. Approved separate `GaugeBaseUrl` configuration even though the current PDF and
   current v2 default resolve to the same host.
3. Approved unconstrained PostgreSQL `numeric`; exact .NET/JSON precision remains an implementation spike.
4. Approved latest-state only with no new snapshots/history.
5. Approved all three capabilities in CyclicalWaves-mode Chat only.
6. Approved scoped Feature 115 standalone P/S gauge only. Its history, monthly-sales inset, LastPS/TTM
   and Forward P/S remain unsupported; Features 114, 125, 126 and 127 remain disabled.
7. NoavaranAmin remains unchanged; unmatched/ambiguous ticker reporting and
   last-known-value retention on a 404.


### Final Product Owner decisions

The five decisions are approved: unconstrained PostgreSQL numeric with exact base-10 representation verification; latest-state only; separate GaugeBaseUrl with existing auth/token lifecycle; all three Chat capabilities in CyclicalWaves mode only; and scoped Feature 115 standalone P/S gauge restoration. The exact base-10 representation is a technical spike because .NET decimal is bounded and the PDF gives no maximum magnitude or scale. Chat must preserve nulls, distinguish close/min/avg/max/start/end/status, treat a-f as counts, report equilibrium in IRR, and return explicit unavailable answers without fallback. Feature 115 history, monthly-sales inset, LastPS/TTM and Forward P/S remain unsupported; Features 114, 125, 126 and 127 stay disabled. The Product Owner explicitly authorized implementation on 2026-11-08; progress and remaining evidence are in tasks.md and ImplementationReport.md.
