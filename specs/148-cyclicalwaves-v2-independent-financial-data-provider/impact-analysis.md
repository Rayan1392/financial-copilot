# Feature 148 — Impact Analysis (rev 3, final)

Repository state: `7c28937` (develop). Paths are relative to `src/backend/` unless prefixed with `src/`, `specs/` or `docs/`.

## 1. Legacy endpoint and consumer inventory

All endpoints in this section are on `https://back1.cyclicalwaves.com/api/` (config section `CyclicalWaves`). **All of them are retired by Feature 148.**

| # | Legacy endpoint | Client | Runtime callers | Data written |
|---|---|---|---|---|
| L1 | `POST auth/login` `{user_name,password}` | `CyclicalWavesAuthHandler` (Redis key `cyclicalwaves:auth:token:v1`) | every client below; `GET admin/provider-health` | — |
| L2 | `GET custom-filtering/tickers` | `CyclicalWavesDataProviderClient` | `POST admin/cyclicalwaves/full-sync`; `data-sync/*` with an empty or CyclicalWaves provider name (`DataSyncConsumerWorker` → `FinancialDataSyncProcessor`; the CyclicalWaves client is the DI default) | shared `FinancialStatements`, `MonthlyReports` and their line items (`ProviderName='CyclicalWaves'`), then `DerivedMetrics` |
| L3 | `GET custom-filtering/ticker/{ticker}` | same | same | same |
| L4 | `GET ps/circle-chart-data/{ISIN}` | Acquisition client; DataProvider client (P/S) | Feature 127 worker (hosted, flag off); Feature 114 Admin `ps-visualization/sync` (**live**); Feature 126 (not hosted) | `CyclicalWavesMetricSnapshots`/`AcquisitionChecks`; `CompanyPsGaugeSnapshots` |
| L5 | `GET ps-data/{ISIN}` | same | Feature 127; Feature 114 | same |
| L6 | `GET pe/circle-chart-data/{ISIN}` | Acquisition client; DataProvider client | Feature 127; Feature 126 | `CyclicalWavesMetricSnapshots`; `IndustryRelativeValuationSourceFacts` |
| L7 | `GET pe-data/{ISIN}` | Acquisition client | Feature 127 | `CyclicalWavesMetricSnapshots` |
| L8 | `GET equilibrium/gauge/{ISIN}` | Acquisition client; DataProvider client | Feature 127; Feature 126 | same as L6 |
| L9 | `GET futureprediction/{symbol}` | DataProvider client | Feature 114 sync | `CompanyPsGaugeSnapshots` |
| L10 | `GET ps/{isin}` | DataProvider client | Feature 114 sync | `CompanyPsHistoryPoints` |
| L11 | `GET blog/getComprehensiveAnalysis?page&paginate` | `ComprehensiveAnalysisBlogClient` | `ComprehensiveAnalysisDailySyncWorker` (hosted, flag off); Admin `comprehensive-analysis/full-sync`, `daily-sync` (ungated) | `ComprehensiveAnalyses`, `ComprehensiveAnalysisTags`, `ComprehensiveAnalysisCategories`, `ComprehensiveAnalysisSyncLogs` |

### Schedules and triggers retired

| Item | Current state | Feature 148 action |
|---|---|---|
| `ComprehensiveAnalysisDailySyncWorker` | Hosted (`Worker/Program.cs:124`), flag off | Unhost; class kept |
| `CyclicalWavesDataAcquisitionWorker` | Hosted (`Program.cs:125`), flag off | Unhost; class kept |
| `IndustryRelativeValuationCalculationWorker` | Hosted (`Program.cs:126`), **`Enabled=true`** | Unhost; set `Enabled=false` |
| `CyclicalWavesPsVisualizationSyncWorker`, `CyclicalWavesRelativeValuationWorker` | Already not hosted | Keep unhosted; the architecture test asserts it |
| `NadpcoScheduledSync` → Feature 125 | Already a no-op (`NadpcoScheduledSync.cs:279`) | No change (NoavaranAmin code untouched) |
| Admin `cyclicalwaves/full-sync`, `ps-visualization/sync`, `comprehensive-analysis/full-sync`, `daily-sync` | Callable | Return `410 Gone` before invoking any service |
| Explicit `data-sync` requests for the CyclicalWaves provider | Callable | `410` |
| `data-sync` requests with an empty provider name | Routed to the legacy default client | Fail fast at the guard |
| `GET admin/provider-health` | Calls L1 | Returns the retired status |

### Configuration dependencies

- **Sections:**
  - `CyclicalWaves`
  - `CyclicalWavesDataAcquisition`
  - `CyclicalWavesPsSync` (API `AllowManualSyncWhenWorkerDisabled=true` → `false`)
  - `CyclicalWavesPsVisualization`
  - `ComprehensiveAnalysisBlog`
  - `ComprehensiveAnalysisDailySync`
  - `Feature126:*`
  - `IndustryRelativeValuation` (`true` → `false`)
  - `FinancialSourcePriority`. This is inert and left unchanged.
- **Compose:** `CYCLICAL_WAVES_USER_NAME` and `CYCLICAL_WAVES_PASSWORD` change from required to optional.
- **Not affected:** Feature 139 TahlilApp session exchange (separate identity client). Chat and recalculation make no legacy calls.

## 2. Capability disposition

| Capability / feature | Legacy endpoint needed? | v2 replacement | Disposition |
|---|---|---|---|
| **020** statement and monthly sync into shared tables | L2, L3 | v2 screener and statements in the **independent** schema | Retired. Shared rows are kept. |
| **065** comprehensive-analysis sync | L11 | `/blogs?filter[type]=analysis` | Retired. Replaced by v2 analysis ingestion. |
| **066** comprehensive-analysis Chat | No (storage) | v2 `Analyses` | CyclicalWaves mode reads the v2 tables. **NoavaranAmin mode: disabled (C2).** |
| `ComprehensiveAnalysisPublishedDetector` (084) | Reads legacy storage only | — (not repointed, per C2) | **Disabled** (unregistered). Historical insight events are kept. |
| **067/068** company mapping and backfill | No | — | Unchanged. 067 is marked superseded. |
| **070/071/073** CyclicalWaves metrics in `DerivedMetrics` | Producer L2/L3 | — | The producer is retired and the rows are kept. **NoavaranAmin lookup and scanner reads exclude them (C1).** |
| **073** lookup contract | — | v2 screener fields | Reused by the CyclicalWaves-mode lookup. |
| **114** P/S visualization sync | L4, L5, L9, L10 | None | Disabled (`410`, manual flag off, guard). |
| **115** P/S gauge (Chat and the monthly-trend gauge) | Data from 114 | None | Disabled. `ps_gauge_visualization` is retired in both modes; `CyclicalWavesPsVisualization:*` stays false. |
| **125** industry relative valuation | Inputs from 127 (L4–L8) | None | Disabled. The worker is unhosted, `Enabled=false`, and `industry_relative_valuation_read` is retired in both modes. Stored calculations, industry-watch and outbox rows are kept. |
| **126** relative-valuation ingestion | L4, L6, L8 | None | Disabled (already dormant) and guarded. |
| **127** data acquisition | L4–L8 | None | Disabled (worker unhosted) and guarded. |
| Scanner (`screen_stocks` / `scanner`) | No | v2 screener values | NoavaranAmin mode: existing path with the C1 filter. **CyclicalWaves mode: basic v2 scanner (C3).** |
| Professional scanners | No | — | NoavaranAmin mode: C1 filter. CyclicalWaves mode: unavailable. |
| Symbol lookup | No | v2 tables | NoavaranAmin mode: C1 filter. CyclicalWaves mode: v2 lookup. |
| Quarterly statement table (144), entity resolution | No | v2 tables | NoavaranAmin mode unchanged. CyclicalWaves mode: v2 implementations. |
| Monthly trend, product trend/value/comparison, statement value search, period analysis, sales-quality ranking, disclosure listing, personalized insight, Feature 141 | No | None in v2 | NoavaranAmin mode unchanged. CyclicalWaves mode: unavailable. |

## 3. Direct and indirect consumers

- **Direct callers of legacy endpoints:** the 4 legacy clients, reached from workers, Admin, data-sync messaging, health and dormant Feature 126. Every path is covered by §1.
- **Indirect readers of legacy-derived storage:**

  | Reader | What it reads | Change |
  |---|---|---|
  | `EfCoreSymbolMetricLookupService`, `EfCoreScannerExecutionService`, `ProfessionalScannerUseCases` | `DerivedMetrics` | C1 filter |
  | `ConditionalTrackerEvaluationProcessor` | `DerivedMetrics` | **Not changed** (§6) |
  | `MetricRecalculationProcessor` | Shared line items | No new CyclicalWaves inputs arrive |
  | `IndustryRelativeValuationCalculationInputBuilder` | — | Disabled |
  | `PsVisualizationExperienceUseCase` | — | Disabled |
  | `ComprehensiveAnalysis*` query use cases | — | Disabled in NoavaranAmin mode; swapped in CyclicalWaves mode |
  | `ComprehensiveAnalysisPublishedDetector` | — | Disabled |

- **Shared entry point:** Telegram uses the same orchestrator, so the same Chat rules apply.
- **Frontend:**
  - No legacy Admin endpoint is called from the UI.
  - The overview card in `data-management-overview-page.tsx` is relabeled.
  - The `ComprehensiveAnalysisPublished` localization in `insight-localization.ts` and the followed-symbols filter stay, so historical events still display.
- **Tests:**
  - About 22 unit and about 12 integration files touch legacy code. They are kept; tests of real legacy client behavior set `CyclicalWavesLegacy:AllowCalls=true`.
  - Feature 115/125 Chat tests now expect the retired answer.
  - Comprehensive-analysis NoavaranAmin-mode tests now expect the C2 unavailable answer.
  - `ComprehensiveAnalysisV2ProductionPath` is rebased onto CyclicalWaves mode.
  - Lookup and scanner tests that seeded CyclicalWaves-evidenced `DerivedMetrics` as expected winners are updated to the C1 behavior.
  - `CleanArchitectureDependencyTests.cs:131-166` gets the new hosted list.

## 4. Documentation changes

| Document | Change |
|---|---|
| `specs/020`, `065`, `067`, `070`, `071`, `073`, `114`, `126`, `127` (top of the primary story/design) | Add: `> DEPRECATED / SUPERSEDED by Feature 148 (2026-10-31): legacy CyclicalWaves endpoints are retired at runtime; stored data retained.` |
| `specs/115`, `specs/125` | Add: `> DISABLED by Feature 148: depends on discontinued CyclicalWaves endpoints; stored data retained.` |
| `specs/066`, `specs/084` | Note: comprehensive analysis is disabled in NoavaranAmin mode (C2) and served from `cyclicalwaves.Analyses` in CyclicalWaves mode; the published-analysis detector is disabled. |
| `specs/073` | Note: in NoavaranAmin mode, reads exclude legacy CyclicalWaves-derived rows (C1). |
| `specs/README.md`, `specs/implementation-checklist.md` | Add a Feature 148 entry; annotate the superseded and disabled rows. |
| Root `README.md` | Document `FinancialData:ChatProvider`, `ExcludeLegacyCyclicalWavesDerivedMetrics`, `CyclicalWavesV2__*` and `CyclicalWavesLegacy:AllowCalls`. |
| `docs/providers/cyclicalwaves-v2/` | Add the two v2 PDFs plus a provenance note. Label `docs/cw_api.txt` and `docs/Cyclical Waves_api_document_2026_05_26.pdf` as legacy. |

## 5. Behavior that must remain unchanged

- NoavaranAmin ingestion (scheduled sync, monthly backfill, Feature 142, archive), persistence, records, API integrations, repositories, catalogs, formulas and `CompanyResolverService`.
- NoavaranAmin Chat responses for every capability except three:
  - C1: lookup and scanner no longer select legacy CyclicalWaves-derived rows.
  - C2: comprehensive analysis returns the unavailable answer.
  - Retired: P/S gauge and relative valuation.
- Every legacy CyclicalWaves row and every `DerivedMetrics` row.
- Admin policies and rate limits, Feature 139, billing, Telegram transport, and Feature 141 under NoavaranAmin.

## 6. Residual findings (not addressed by Feature 148)

- **N1.** `ConditionalTrackerEvaluationProcessor.cs:387` reads `DerivedMetrics` without the C1 filter. User alerts on metrics that only legacy CyclicalWaves supplied may evaluate against frozen values. This is outside the C1 authorization ("lookups and scanner queries") and needs a separate Product Owner decision.
- **N2.** `src/backend/FinancialCopilot.API/appsettings.json` (`NoavaranCurrentApi`, around lines 238–239) contains committed credentials. They should be rotated and moved to secrets in a separate change.
