# Feature 125 Ingestion Flow Analysis

**Investigation date:** 2026-08-12
**Scope:** read-only architecture tracing. No code, configuration, or migrations were modified.

## Executive conclusion

There are two related but separate ingestion paths:

1. **P/S visualization ingestion** has an independent hosted `BackgroundService` and a DataAdmin API endpoint. It calls CyclicalWaves directly and writes `CompanyPsGaugeSnapshots` / related P/S tables.
2. **Feature 125 source ingestion** has no independent hosted worker, scheduler, or Feature 125-specific API endpoint. Its P/S input is read from the already-persisted P/S snapshot table. Its P/E and equilibrium inputs call CyclicalWaves directly. The Feature 125 source service is reached through `IndustryRelativeValuationOrchestrationService`, which production code invokes from `NadpcoScheduledSyncCoordinator`.

The exact Feature 125 source path is:

```text
NadpcoScheduledSyncWorker                         (automatic trigger)
        or
AdminDataOperationsController                     (forced manual trigger)
        ↓
INadpcoScheduledSyncCoordinator.RunAsync
        ↓
IIndustryRelativeValuationOrchestrationService.RunAsync
        ↓
IIndustryRelativeValuationSourceIngestionService.RunAsync
        ├─ reads persisted P/S gauge snapshot
        ├─ ICyclicalWavesRelativeValuationProviderClient.GetPeGaugeAsync
        ├─ ICyclicalWavesRelativeValuationProviderClient.GetEquilibriumGaugeAsync
        ↓
IndustryRelativeValuationSourceFacts
        ↓
IndustryRelativeValuationCalculationSnapshotWriter
        ↓
IndustryRelativeValuationCalculations
IndustryRelativeValuationMetrics
CompanyIndustryRelativeValuations
IndustryRelativeValuationOutbox / watch-state tables as applicable
```

The separate P/S feed path is:

```text
CyclicalWavesPsVisualizationSyncWorker
        or
CyclicalWavesPsVisualizationAdminController
        ↓
ICyclicalWavesPsVisualizationSyncService.SyncAsync
        ↓
ICyclicalWavesPsProviderClient.GetGaugeAsync
        ↓
CyclicalWavesDataProviderClient
        ↓
/api/ps/circle-chart-data/{ISIN}
        ↓
CompanyPsGaugeSnapshots
CompanyPsSeriesSyncStates
CompanyPsVisualizationLeases
```

## 1. Services, classes, interfaces, and callers

### Feature 125 source ingestion

| Class/service | Interface | Project | Entry point | Verified callers |
|---|---|---|---|---|
| `IndustryRelativeValuationSourceIngestionService` | `IIndustryRelativeValuationSourceIngestionService` | `FinancialCopilot.Infrastructure` | `RunAsync(IndustryRelativeValuationSourceRunRequest, CancellationToken)` | `IndustryRelativeValuationOrchestrationService.RunAsync`; direct construction in Feature 125/unit/integration tests |
| `IndustryRelativeValuationOrchestrationService` | `IIndustryRelativeValuationOrchestrationService` | `FinancialCopilot.Infrastructure` | `RunAsync(string correlationId, CancellationToken)` | `NadpcoScheduledSyncCoordinator.RunAsync` at `NadpcoScheduledSync.cs:342-344`; direct construction in Feature 125 tests |
| `NadpcoScheduledSyncCoordinator` | `INadpcoScheduledSyncCoordinator` | `FinancialCopilot.Infrastructure` | `RunAsync(NadpcoScheduledSyncRunRequest, CancellationToken)` | `NadpcoScheduledSyncWorker.ExecuteAsync`; `AdminDataOperationsController.RunNadpcoScheduledSync` |
| `NadpcoScheduledSyncWorker` | `BackgroundService` / hosted service | `FinancialCopilot.Worker` | `ExecuteAsync(CancellationToken)` | .NET host; registered at `Worker/Program.cs:119` |

`IndustryRelativeValuationSourceIngestionService` is registered as scoped at `ServiceCollectionExtensions.cs:1299-1300`. It has no `BackgroundService` base class and no timer. Its `RunAsync` first checks `IndustryRelativeValuation:SourceIngestion:Enabled`, acquires the `IndustryRelativeValuationSourceIngestion` lease, enumerates eligible companies, and processes each company.

`IndustryRelativeValuationOrchestrationService` is registered as scoped at `ServiceCollectionExtensions.cs:1301-1302`. It first checks `IndustryRelativeValuation:Enabled`; only then does it call the source-ingestion service and the calculation/publishing pipeline.

### CyclicalWaves provider client

| Class/service | Interface | Project | Entry point | Verified callers |
|---|---|---|---|---|
| `CyclicalWavesDataProviderClient` | `ICyclicalWavesRelativeValuationProviderClient`, `ICyclicalWavesPsProviderClient`, plus the general provider interfaces | `FinancialCopilot.Infrastructure` | `GetPeGaugeAsync`, `GetEquilibriumGaugeAsync`, `GetGaugeAsync`, `GetCurrentValuesAsync`, `GetForwardValuesAsync`, `GetHistoryAsync` | Feature 125 source service for PE/equilibrium; P/S visualization sync service for P/S; other general ingestion paths for its general provider interfaces |
| `CyclicalWavesAuthHandler` | Delegating HTTP handler | `FinancialCopilot.Infrastructure` | HTTP pipeline handler | Typed `CyclicalWavesDataProviderClient` HTTP client |

The typed HTTP client is registered at `ServiceCollectionExtensions.cs:979-986`, with `CyclicalWavesAuthHandler` and the provider resilience handler. The Feature 125 interface is explicitly mapped to the same client at `ServiceCollectionExtensions.cs:998-999`.

### P/S snapshot ingestion

| Class/service | Interface | Project | Entry point | Verified callers |
|---|---|---|---|---|
| `CyclicalWavesPsVisualizationSyncWorker` | `BackgroundService` / hosted service | `FinancialCopilot.Worker` | `ExecuteAsync(CancellationToken)` | .NET host; registered at `Worker/Program.cs:123` |
| `CyclicalWavesPsVisualizationSyncService` | `ICyclicalWavesPsVisualizationSyncService`, `ICompanyPsVisualizationReader` | `FinancialCopilot.Infrastructure` | `SyncAsync(PsVisualizationSyncRequest, CancellationToken)`; `GetAsync` is read-only | P/S hosted worker; `CyclicalWavesPsVisualizationAdminController.ExecuteAsync` |
| `NoavaranEligibleCompanyPsScopeReader` | `IPsEligibleCompanyScopeReader` | `FinancialCopilot.Infrastructure` | `ReadAsync` | P/S sync service; P/S admin controller dry-run |

The P/S service is registered at `ServiceCollectionExtensions.cs:1288-1290`. Its `SyncSnapshotAsync` calls `ICyclicalWavesPsProviderClient.GetGaugeAsync`, `GetCurrentValuesAsync`, and `GetForwardValuesAsync`; the service persists the resulting snapshot and state. Feature 125 later reads the latest valid P/S row from `CompanyPsGaugeSnapshots` rather than calling the P/S provider through `ICyclicalWavesRelativeValuationProviderClient`.

## 2. Provider calls and persistence mapping

### P/S

```text
CyclicalWavesPsVisualizationSyncWorker.ExecuteAsync
  → CyclicalWavesPsVisualizationSyncService.SyncAsync
  → SyncSnapshotAsync
  → ICyclicalWavesPsProviderClient.GetGaugeAsync
  → CyclicalWavesDataProviderClient.GetGaugeAsync
  → GET /api/ps/circle-chart-data/{ISIN}
  → CompanyPsGaugeSnapshots
```

The same P/S service also calls `ps-data/{ISIN}` for current values and `futureprediction/{symbol}` for forward values, then persists the complete P/S visualization snapshot. History uses the separate `ps/{ISIN}` endpoint and writes `CompanyPsHistoryPoints`.

Feature 125’s source service then reads `CompanyPsGaugeSnapshots` where the provider is CyclicalWaves, renderability is `Renderable`, quality is `Valid`, and `GaugeClose`/`GaugeAverage` are positive. It projects:

- `GaugeClose` → Feature 125 `CurrentPS`;
- `GaugeAverage` → Feature 125 `HistoricalAveragePS`;
- persisted boundary values → the Feature 125 P/S evidence/projection.

The Feature 125 destination row is `IndustryRelativeValuationSourceFacts` with `SourceKind = PSGauge`.

### P/E

```text
IndustryRelativeValuationSourceIngestionService.ProcessCompanyAsync
  → ICyclicalWavesRelativeValuationProviderClient.GetPeGaugeAsync
  → CyclicalWavesDataProviderClient.GetPeGaugeAsync
  → GET /api/pe/circle-chart-data/{ISIN}
  → PersistAsync
  → IndustryRelativeValuationSourceFacts
```

Mapping in `CyclicalWavesDataProviderClient`:

- response `close` → `CurrentValue` / current PE;
- response `avg` → `ReferenceValue` / historical average PE;
- `min` and `max` are deserialized in the payload model but are not written as separate Feature 125 source-fact columns by this provider result.

The source fact uses `SourceKind = PEGauge`.

### Equilibrium price

```text
IndustryRelativeValuationSourceIngestionService.ProcessCompanyAsync
  → ICyclicalWavesRelativeValuationProviderClient.GetEquilibriumGaugeAsync
  → CyclicalWavesDataProviderClient.GetEquilibriumGaugeAsync
  → GET /api/equilibrium/gauge/{ISIN}
  → PersistAsync
  → IndustryRelativeValuationSourceFacts
```

Mapping in `CyclicalWavesDataProviderClient`:

- response `close` → `CurrentValue` / current market price;
- response `balance` → `ReferenceValue` / equilibrium price;
- `enticker`/`ticker` must match the requested ISIN identity;
- non-positive `close` or `balance` is rejected as `InvalidNonPositiveInput`.

The source fact uses `SourceKind = EquilibriumGauge`.

## 3. All verified trigger types

### Automatic Worker trigger for Feature 125

```text
FinancialCopilot.Worker host
  → NadpcoScheduledSyncWorker.ExecuteAsync
  → INadpcoScheduledSyncCoordinator.GetStatusAsync / RunAsync
  → NadpcoScheduledSyncCoordinator.RunAsync
  → ExecuteSelectedDatasetsAsync
  → IIndustryRelativeValuationOrchestrationService.RunAsync
```

`NadpcoScheduledSyncWorker` is registered as a hosted service at `Worker/Program.cs:119`. It only calls the coordinator automatically when `NadpcoScheduledSync:Enabled` is true and the schedule is due (or missed recovery applies).

### Manual API trigger that indirectly reaches Feature 125

`POST /api/v1/admin/data-sync/nadpcoapi/scheduled-sync/run` is implemented by `AdminDataOperationsController.RunNadpcoScheduledSync` at `AdminDataOperationsController.cs:741` area. It calls `INadpcoScheduledSyncCoordinator.RunAsync` with `TriggerSource.Manual` and `Force: true`.

The coordinator’s disabled check applies only to automatic/missed-recovery triggers when `!settings.Enabled && !request.Force`. Therefore this manual endpoint can reach Feature 125 even when `NadpcoScheduledSync:Enabled=false`, provided the Feature 125 gates are enabled and the run is not otherwise blocked.

This is an indirect NADPCO coordinator endpoint, not a dedicated Feature 125 endpoint.

### Independent P/S triggers

The P/S visualization worker is independently registered at `Worker/Program.cs:123`. It starts a periodic timer using `CyclicalWavesPsSync:SnapshotCadenceMinutes` and calls `ICyclicalWavesPsVisualizationSyncService.SyncAsync`.

The DataAdmin P/S controller exposes:

- `POST /api/v1/admin/cyclicalwaves/ps-visualization/scope/dry-run` — scope inspection only;
- `POST /api/v1/admin/cyclicalwaves/ps-visualization/sync` — snapshot and due-history sync;
- `POST /api/v1/admin/cyclicalwaves/ps-visualization/snapshot` — snapshot-only sync;
- `POST /api/v1/admin/cyclicalwaves/ps-visualization/history` — history-only sync;
- `GET /api/v1/admin/cyclicalwaves/ps-visualization/companies/{companyId}` — persisted read only.

The controller allows manual P/S sync while the worker is disabled when `CyclicalWavesPsSync:AllowManualSyncWhenWorkerDisabled=true`.

No equivalent PE or equilibrium API endpoint was found. The only production call sites for `GetPeGaugeAsync` and `GetEquilibriumGaugeAsync` are inside Feature 125 source ingestion (apart from tests).

## 4. Direct answers

### A) Can Feature 125 receive CyclicalWaves data when `NadpcoScheduledSync:Enabled=false`?

**Yes, but only through the forced manual NADPCO scheduled-sync API path, and only if the Feature 125 gates are enabled.**

- Automatic Worker execution: **No**. `NadpcoScheduledSyncWorker` does not call the coordinator when the scheduled-sync setting is disabled.
- Forced manual `POST .../nadpcoapi/scheduled-sync/run`: **Yes in principle**. The controller sends `Manual` plus `Force:true`, and the coordinator’s disabled guard does not skip that request.
- Feature 125 source service itself: still requires `IndustryRelativeValuation:SourceIngestion:Enabled=true`.
- Feature 125 orchestration itself: still requires `IndustryRelativeValuation:Enabled=true`.

The independent P/S visualization worker can receive P/S data when `NadpcoScheduledSync:Enabled=false`, because it is a separate hosted service. That P/S data is written to the P/S visualization tables; it does not by itself execute Feature 125 source ingestion or create Feature 125 PE/equilibrium facts.

### B) Is there an independent scheduler/job for Feature 125 source ingestion?

**No.** There is no `Feature125` hosted service, timer, scheduler, or standalone job. `IndustryRelativeValuationSourceIngestionService` is a scoped service with a callable `RunAsync`; scheduling ownership remains with the existing coordinator, as also stated in its source comments.

There is an independent P/S visualization scheduler, but that is the P/S snapshot feed and not the Feature 125 source-ingestion scheduler.

### C) Is there an API endpoint that manually triggers PS, PE, or equilibrium ingestion?

- **P/S:** Yes. The DataAdmin P/S visualization endpoints listed above directly invoke `ICyclicalWavesPsVisualizationSyncService.SyncAsync`.
- **P/E:** No dedicated endpoint found. It can be reached indirectly by the forced manual NADPCO scheduled-sync endpoint, subject to both Feature 125 flags.
- **Equilibrium:** No dedicated endpoint found. It can be reached indirectly by the same forced manual NADPCO scheduled-sync endpoint, subject to both Feature 125 flags.
- **Feature 125 source ingestion as a whole:** No dedicated Feature 125 endpoint found.

### D) What configuration flags control only Feature 125 ingestion?

The Feature 125-specific activation gates are:

| Configuration key | Scope |
|---|---|
| `IndustryRelativeValuation:Enabled` | Gates the downstream Feature 125 orchestration, including calling source ingestion and calculation/publication. |
| `IndustryRelativeValuation:SourceIngestion:Enabled` | Gates `IndustryRelativeValuationSourceIngestionService` itself. The nested section is absent from the checked appsettings, so its `Enabled` default is false. |

Other Feature 125 source options control limits/behavior rather than activation, including `CanonicalProviderName`, `MaximumCompaniesPerRun`, `MaximumConcurrency`, and `LeaseMinutes`.

Related but not Feature 125-only trigger flags:

- `NadpcoScheduledSync:Enabled` gates the automatic coordinator trigger;
- `CyclicalWavesPsSync:Enabled` gates the independent P/S visualization worker;
- `CyclicalWavesPsSync:AllowManualSyncWhenWorkerDisabled` controls manual P/S endpoint behavior;
- `CyclicalWaves` configures the provider client, authentication, URL, timeout, retry, and resilience behavior.

## 5. Configuration and activation dependencies

For automatic Feature 125 P/E/equilibrium ingestion, all of the following are required:

1. The Worker process is running.
2. `NadpcoScheduledSync:Enabled=true` so the scheduled Worker invokes the coordinator.
3. `IndustryRelativeValuation:Enabled=true` so orchestration does not return before source ingestion.
4. `IndustryRelativeValuation:SourceIngestion:Enabled=true` so source ingestion does not return immediately.
5. CyclicalWaves provider base address and authentication configuration are valid.
6. Eligible company rows exist with the required provider identity, industry, and `SymbolIsin`.
7. The Feature 125 source lease is available.

For P/S visualization ingestion, the required path is instead:

1. The Worker process is running and `CyclicalWavesPsSync:Enabled=true`, or a DataAdmin invokes the P/S endpoint with manual sync allowed.
2. Eligible P/S companies exist in the P/S scope.
3. CyclicalWaves provider configuration is valid.
4. The P/S visualization lease is available.

## 6. Missing activation steps observed in the checked repository state

The checked Worker configuration has `IndustryRelativeValuation:Enabled=false`. It also has no explicit `IndustryRelativeValuation:SourceIngestion` section, whose `Enabled` property defaults to false. Therefore, source ingestion cannot run through the normal Feature 125 orchestration path under those checked settings.

The checked Worker configuration also has `NadpcoScheduledSync:Enabled=false`, so the automatic coordinator trigger is disabled. This does not disable the separate P/S visualization Worker, which is configured independently.

No implementation fix was applied. The exact operational activation decision and any configuration changes remain for the deployment owner.

