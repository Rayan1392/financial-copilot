# Feature 125 End-to-End Verification Report

**Verification date:** 2026-08-12
**Scope:** read-only verification of worker, API, provider-client, and PostgreSQL state. No production code, configuration, or migrations were changed.

## 1. Executive result

Feature 125 is present in the deployed source and its migration is applied, but active source ingestion was **not verified**. The PostgreSQL Feature 125 tables contain zero rows, and the checked Worker configuration disables the feature and the scheduled coordinator.

| Area | Result |
|---|---|
| Worker process | Running (`FinancialCopilot.Worker`, started 2026-08-12 12:56:23 +03:30) |
| API process | Running; `GET http://localhost:5074/health` returned HTTP 200 / `Healthy` |
| Feature 125 migration | Applied: `20260812063122_Feature125Slice3Persistence`, EF Core 10.0.4 |
| Feature 125 source facts | 0 rows |
| Feature 125 calculations/snapshots | 0 rows in all checked tables |
| CyclicalWaves ingestion | Not evidenced in the database or retained logs |
| Overall | **Not receiving data** |

## 2. Worker configuration

Checked files:

- `src/backend/FinancialCopilot.Worker/appsettings.json`
- `src/backend/FinancialCopilot.Worker/appsettings.Development.json`
- copied settings under `src/backend/FinancialCopilot.Worker/bin/Debug/net10.0`

Relevant values found:

| Key | Current checked value | Status |
|---|---:|---|
| `IndustryRelativeValuation:Enabled` | `false` | Disabled |
| `IndustryRelativeValuation:DailyCadenceMinutes` | `1440` | Present |
| `IndustryRelativeValuation:SourceIngestion` | Not present | Uses option defaults; source ingestion defaults to disabled |
| `NadpcoScheduledSync:Enabled` | `false` | Disabled in checked settings |
| `CyclicalWaves:ProviderName` | `CyclicalWaves` | Present |
| `CyclicalWaves:BaseAddress` | `https://back1.cyclicalwaves.com/api/` | Present |
| `CyclicalWaves:TimeoutSeconds` | `30` | Present |
| `CyclicalWaves:RetryCount` | `2` | Present |
| `CyclicalWaves:UserName` / `Password` | Configured | Values intentionally omitted |
| `ConnectionStrings:FinancialCopilot` | Configured for local PostgreSQL | Credentials redacted |

The Worker registers `NadpcoScheduledSyncWorker`, and `Program.cs` registers `NadpcoScheduledSyncOptions`. The Feature 125 orchestration, source-ingestion service, and calculation writer are registered in `ServiceCollectionExtensions.cs`.

The effective environment variables of the running process were not exposed by the available process inspection. This is relevant because the database contains a currently `Running` NADPCO scheduled-sync row whose schedule snapshot says `Enabled:true`, while the checked Worker settings say `Enabled:false`. The runtime override responsible for this difference could not be identified read-only.

## 3. CyclicalWaves clients and mappings

`CyclicalWavesDataProviderClient` exists and is registered through the typed HTTP client with `CyclicalWavesAuthHandler` and the resilience handler. `ICyclicalWavesRelativeValuationProviderClient` resolves to this client.

Verified endpoint construction:

- P/S gauge: `ps/circle-chart-data/{ISIN}` (used by the existing P/S visualization path and consumed by Feature 125 from persisted valid P/S gauge snapshots).
- P/E gauge: `pe/circle-chart-data/{ISIN}`.
- Equilibrium gauge: `equilibrium/gauge/{ISIN}`.

Verified mappings:

- P/E: `close` → current PE; `avg` → historical average PE. The raw payload model also includes `min` and `max` boundaries.
- Equilibrium: `close` → current market price; `balance` → equilibrium price. Response identity is checked against `enticker`/`ticker`, and non-positive values are rejected.
- P/S: Feature 125 projects the latest valid persisted P/S gauge snapshot: `GaugeClose` → current PS; `GaugeAverage` → historical average PS; boundary start/min/average/max/end values remain available in the P/S snapshot/raw representation.

The client and registrations are present, but no successful Feature 125 provider observations were found in PostgreSQL.

## 4. Worker execution flow

The source path is:

`NadpcoScheduledSyncWorker` → `INadpcoScheduledSyncCoordinator.RunAsync` → `IIndustryRelativeValuationOrchestrationService.RunAsync` → `IIndustryRelativeValuationSourceIngestionService.RunAsync` → CyclicalWaves P/E and equilibrium clients plus persisted P/S snapshots → source facts → calculations/publication.

The source code confirms there is no dedicated Feature 125 worker. The existing coordinator invokes Feature 125 after its selected NADPCO datasets complete. Both the source-ingestion service and downstream orchestration have explicit disabled checks.

Database execution evidence:

- `NadpcoScheduledSyncRuns` contains 85 rows.
- Latest row: `Running`, started `2026-08-12 12:56:45.806934 +03:30`, with 0 processed and 0 failed batches at verification time.
- Previous row: `HungRecovered`, started `2026-08-11 11:42:10.053304 +03:30`, recovered at `2026-08-12 12:56:36.955285 +03:30`.
- The latest run’s dataset snapshot is the existing NADPCO dataset set; it does not prove that Feature 125 completed.

No retained Worker log file was available in the workspace, so Feature 125 “started”, provider request/response, persistence, correlation ID, and completion log events could not be independently verified. The checked source contains the expected structured log messages, including correlation IDs.

## 5. Database verification

Connected read-only to PostgreSQL database `financial_copilot` on `localhost:5432`.

The following tables exist:

- `IndustryRelativeValuationSourceFacts`
- `IndustryRelativeValuationSourceLeases`
- `IndustryRelativeValuationOutbox`
- `IndustryRelativeValuationCalculations`
- `CompanyIndustryRelativeValuations`
- `IndustryRelativeValuationMetrics`
- `IndustryWatchStates`

Counts at verification time:

| Table | Rows |
|---|---:|
| `IndustryRelativeValuationSourceFacts` | 0 |
| `IndustryRelativeValuationSourceLeases` | 0 |
| `IndustryRelativeValuationOutbox` | 0 |
| `IndustryRelativeValuationCalculations` | 0 |
| `CompanyIndustryRelativeValuations` | 0 |
| `IndustryRelativeValuationMetrics` | 0 |
| `IndustryWatchStates` | 0 |

Source-fact metrics present: none.

Latest valid observation timestamps:

| Metric | Latest `PersistedAtUtc` | Companies |
|---|---|---:|
| PS | None | 0 |
| PE | None | 0 |
| Equilibrium | None | 0 |

There are 4,494 rows in `Companies`, of which 2,112 meet the source-ingestion eligibility predicates (`ProviderName=NoavaranCurrentApi`, non-null industry, non-null `SymbolIsin`). Feature 125 source-fact coverage is 0 / 2,112 eligible companies.

Missing-data checks:

- Companies with PS but no PE: no PS facts exist; therefore none returned.
- Companies with PE but no Equilibrium: no PE facts exist; therefore none returned.
- Companies with no valid metrics: all 2,112 eligible companies currently have no Feature 125 valid source facts.

No Feature 125 endpoint payloads were present in `ProviderRawPayloads` (`circle-chart-data` / `equilibrium/gauge` search: 0 rows).

## 6. Provider data quality

No Feature 125 samples could be drawn because no source facts were persisted. Consequently, positive-value checks for PS, PE, and equilibrium cannot pass on live persisted data.

The source code does implement the required guards: non-positive P/E and equilibrium values are marked invalid, equilibrium identity mismatches are rejected, missing values are retained as non-ready results rather than treated as valid observations, and P/S projection requires positive close and average values.

## 7. Logs and observability

The Worker uses Serilog console output at Information level, with Microsoft EF database commands at Information level. No retained log file was found in the repository or Worker output directory.

Source-level observability includes messages for:

- Feature 125 disabled/skipped;
- source-ingestion start/completion/failure and lease contention;
- provider failures;
- downstream completion/failure with correlation ID, company count, persisted facts, failures, published snapshots, and inconclusive snapshots.

No runtime Feature 125 correlation ID or successful persisted-fact log could be recovered from the available evidence.

## 8. API verification

`GET /health` succeeded. No standalone Feature 125 HTTP controller route was found in the API route inventory.

The Feature 125 semantic read path is registered through `IndustryRelativeValuationReadRepository` and `IndustryRelativeValuationSemanticAdapter`, both backed by `FinancialIngestionDbContext`. The read repository logs “Feature 125 read started” and reads persisted snapshots; it does not inject or call the CyclicalWaves HTTP client.

Therefore, source inspection confirms that the semantic API path is designed to read published snapshots and does not call CyclicalWaves during user queries. With zero published snapshots, no successful end-to-end API result can currently be verified.

## 9. Errors, warnings, and blockers

1. Feature 125 is disabled in the checked Worker settings.
2. The `IndustryRelativeValuation:SourceIngestion` section is absent, so source-ingestion options use their disabled default.
3. The checked `NadpcoScheduledSync:Enabled` value is false, although the database records a currently running scheduled-sync row with an older/effective enabled snapshot. The runtime configuration source/override is unresolved by read-only inspection.
4. All Feature 125 persistence tables are empty; no PS, PE, or equilibrium observations have been received or persisted.
5. No retained runtime logs were available to prove provider requests, responses, correlation IDs, or Feature 125 completion.

## 10. Recommended next action

Do not treat Feature 125 as operationally verified. First reconcile the running Worker’s effective configuration with the checked settings, then enable and execute the intended scheduled path under the deployment’s normal change-control process. After one completed run, repeat the database freshness, coverage, positive-value, missing-data, log-correlation, and API read-path checks.

