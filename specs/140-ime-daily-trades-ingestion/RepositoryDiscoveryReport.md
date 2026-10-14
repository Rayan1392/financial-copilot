# Feature 140 — Repository Discovery Report (T140-01)

**Scope:** repository audit only. No Feature 140 implementation, schema, test, configuration, or specification changes were made. The working tree already contained unrelated edits in `.env.example`, `docker-compose.yml`, `FinancialCopilot.Infrastructure/ServiceCollectionExtensions.cs`, `ComprehensiveAnalysisDailySyncWorker.cs`, and Worker `appsettings.json`; these were left untouched.

## 1. Executive verdict

**Material design correction is required before implementation.** The specification treats the physical ingestion table `Companies` as a cross-provider canonical company identity. Repository evidence shows that table is `FinancialIngestionDbContext.Companies` (`NormalizedCompanyRow`) and identifies a provider-specific row by the unique pair `(ProviderName, ExternalCompanyId)`. Its `Id` is a row GUID, not a guaranteed legal-company identity across providers. Provider copies of the same issuer can therefore have distinct `Companies.Id` values.

The repository also has a domain `Company` (`FinancialCopilot.Domain.Financial.Entities.FinancialEntities.Company`) with a `Guid Id` and `ProviderExternalReference` collection, which is semantically closer to a canonical company. However, the ingestion persistence context does not persist that type; no company entity configuration or `DbSet<Company>` was found there. A Feature 140 FK to that domain type is therefore not currently implementable as a database FK without an established persistence target. Do not treat the two `Company` concepts as interchangeable.

**Canonical company identity decision: `Companies.Id requires adjustment`.** Revise the specification to pick a concrete persisted identity: either scope mapping to an explicitly named provider’s `NormalizedCompanyRow.Id` and document that it is not cross-provider canonical, or establish/use a persisted canonical company entity before calling the mapping company-level. The current repo has no cross-provider canonical persisted company FK target to recommend as a drop-in replacement. `TradingInstrumentRow.Id` is a persisted security/instrument identity, but is not a company identity and is not an appropriate replacement for producer-to-company mapping.

The ingestion, EF, Admin, and Worker patterns are reusable. The repository has no IME-specific provider client or import pattern, no general raw HTTP response ledger, and no observed IME trade rows proving the symbol-prefix examples. Implementation should remain blocked at the spec/design gate until the canonical FK target is settled.

## 2. Companies entity findings

There are two distinct models named for company identity:

| Concern | Repository type | Persistence / meaning |
|---|---|---|
| Normalized provider company row | `FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence.NormalizedCompanyRow`, in `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/FinancialIngestionRows.cs` | Persisted by `FinancialIngestionDbContext.Companies` to unqualified PostgreSQL table `Companies` (default schema; migration does not specify one). Provider identity is `(ProviderName, ExternalCompanyId)`. |
| Domain company aggregate | `FinancialCopilot.Domain.Financial.Entities.Company`, in `src/backend/FinancialCopilot.Domain/Financial/Entities/FinancialEntities.cs` | Immutable domain model with `Guid Id`, `Name`, optional `IndustryId`, and provider external references. It is not mapped by the financial ingestion EF context. |

The physical `Companies` table is owned by `FinancialIngestionDbContext` in `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/FinancialIngestionDbContext.cs`. `NormalizedCompanyRowConfiguration` in `FinancialIngestionConfigurations.cs` maps it with `ToTable("Companies")`, primary key `Id`, and unique index `(ProviderName, ExternalCompanyId)`. Migration `20260527045316_InitialFinancialIngestion` creates `Companies.Id` as PostgreSQL `uuid` / C# `Guid`; `ProviderName`, `ExternalCompanyId`, `Name`, and `LastSynchronizedAt` are non-nullable.

`NormalizedCompanyRow` has non-null `Id`, `ProviderName`, `ExternalCompanyId`, `Name`, and `LastSynchronizedAt`. The spec-relevant descriptive fields are nullable: `CompanySymbol`, `TseSymbol`, `InstrumentCode`; similarly `CompanySymbolEnglish`, `CompanySymbolPinglish`, `CompanyIsin`, `SymbolIsin`, and `Ticker`/`EnTicker`. `SymbolsIn` is not a property on this row. The property set also has `CompanyCode`, `NationalId`, optional industry/group/market GUIDs, and source provenance (`LogicalVendor`, `SourceMode`).

Neither `CompanySymbol`, `TseSymbol`, nor `InstrumentCode` has a unique index in the model configuration. `Ticker` and `EnTicker` have unique filtered indexes for non-null values (`WHERE "Ticker" IS NOT NULL`, `WHERE "EnTicker" IS NOT NULL`), added for Spec 067. Optional `IndustryId`, `GroupId`, and `MarketId` references are indexed and configured `DeleteBehavior.SetNull`. Provider-normalized company FKs do exist: `NormalizedFinancialStatementRow.CompanyId`, `NormalizedMonthlyReportRow.CompanyId`, and `TradingInstrumentRow.NormalizedCompanyId` target `NormalizedCompanyRow` with `SetNull` delete behavior. These FKs therefore retain provider-row semantics and do not establish cross-provider canonical identity.

## 3. Canonical company identity and provider duplicate semantics

The only database uniqueness for normalized company identity is `(ProviderName, ExternalCompanyId)`. This supports coexisting provider rows and does not enforce cross-provider consolidation. `CompanySymbol`, `TseSymbol`, and `InstrumentCode` can repeat at the database level. The schema consequently permits multiple company rows for the same legal issuer and multiple aliases/symbol attributes across provider records; it does not provide a single row whose `Id` is guaranteed stable across providers.

The migration/configuration prove that provider duplicate rows are allowed. No connected production database was queried, so actual duplicate counts or current duplicate values cannot be asserted. The repository includes company alias/resolution behavior and the provider references in the domain model, but no persisted canonical company table mapping usable by the Feature 140 migration was found.

## 4. Existing mapping patterns

The strongest relevant pattern is Spec 067’s `CyclicalWavesCompanyMappingService` in `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/CyclicalWaves/CyclicalWavesCompanyMappingService.cs`. It fills nullable `Ticker`/`EnTicker` on normalized company rows from company/market data and relies on the filtered unique indexes above. This is a provider-to-normalized-row enrichment, not a manually reviewed mapping ledger; it has no mapping status, disabled state, mapping audit, or conflict-review lifecycle.

Other reusable concepts include:

- `ProviderExternalReference` on the domain `Company` in `FinancialEntities.cs`: provider name plus provider-local external identity, but no persisted link to `NormalizedCompanyRow` in the ingestion context.
- `TradingInstrumentRow` in `FinancialIngestionRows.cs`: persistent instrument identity, with nullable `NormalizedCompanyId`; it is appropriate for security/instrument-specific joins, not a company-level producer mapping.
- `CanonicalQueryEntityResolver` and governed symbol aliases in the Application AI orchestration layer: ambiguity is retained instead of auto-selecting a company, but this is query interpretation rather than provider mapping persistence.
- Archive import’s company mapping evaluation (`ArchiveImportCoordinator.EvaluateCompanyMappingAsync` in `Financial/Ingestion/ArchiveImport/ArchiveImport.cs`): reports company mapping coverage for import validation; it is not a reusable mapping registry.

No existing entity/service provides reviewed external-producer-to-company mappings with unresolved/conflict states. Feature 140 will need its own mapping semantics after the canonical persistence target is resolved; it should reuse the normalized lookup/ambiguity conventions rather than clone unrelated query alias behavior.

## 5. EF Core / PostgreSQL conventions

- Financial ingestion persistence uses `FinancialIngestionDbContext` and `IEntityTypeConfiguration<T>` classes in `Financial/Ingestion/Persistence/*Configurations.cs`; `OnModelCreating` applies configurations from the context namespace. Additive mappings should follow that pattern and use EF migrations under `Financial/Ingestion/Persistence/Migrations`.
- Keys are generally `Guid` / PostgreSQL `uuid`. UTC instants use `DateTimeOffset` / `timestamp with time zone`; dates use `DateOnly` / `date`. The initial migration spells table and index names in PascalCase and does not specify a schema.
- Unique indexes are expressed with `.HasIndex(...).IsUnique()`. Filtered/partial indexes are already used with `.HasFilter(...)`, e.g. the Spec 067 ticker indexes and other Financial Ingestion configuration. Therefore the requested active-only uniqueness can be expressed as a PostgreSQL partial unique index, for example unique `ImeProducerCode` with filter `IsActive = true` (and status predicate if the chosen model requires it), once mapping semantics are finalized.
- Nullable company links can be expressed as nullable `Guid?` FKs. Existing nullable dimensions use `SetNull`; mapping history/observation linkage should select delete behavior explicitly rather than rely on convention.
- No row-version concurrency token is configured on `NormalizedCompanyRow`. For admin mapping writes, enforce uniqueness in PostgreSQL and handle constraint violations; do not assume optimistic concurrency is already standardized for these rows.
- `FinancialIngestionDbContext` includes `DataSyncRunRow`/`ProviderSyncRuns` operational run records. The migration uses explicit timestamps/counters, not a common `CreatedAtUtc`/`UpdatedAtUtc` base class.
- Migration evidence: `20260527045316_InitialFinancialIngestion.cs`, plus the current `FinancialIngestionDbContextModelSnapshot.cs`. Feature 140 should add one isolated EF configuration and migration using the established context only after resolving the FK target.

## 6. Existing ingestion architecture

### StockMarketDB / daily trades

`StockMarketDbSyncService` in `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/StockMarketDb/StockMarketDbSyncService.cs` orchestrates the source query executor and persistence. `PersistDailyTradesAsync` resolves instruments, loads existing rows by provider plus external trade ID, upserts daily trade values, updates latest quotes, and saves once. SQL source reads are in `Financial/Providers/StockMarketDb/SqlStockMarketDbQueryExecutor.cs`; application sync contracts are in `FinancialCopilot.Application/FinancialData/Ingestion/StockMarketDbSyncContracts.cs`. The provider source is a read-only SQL Server adapter, not an HTTP IME client.

StockMarketDB rows are normalized into `DailyInstrumentTrades`, associated with a `TradingInstrumentRow`, and use the source trade ID as provider-local identity. This is a useful application-service/DTO/repository/result pattern, but it does not preserve an arbitrary full HTTP response body for every page.

### CodalDB

`CodalDbScheduledSyncService.ExecuteAsync` in `Financial/Ingestion/CodalDb/CodalDbScheduledSyncService.cs` is an orchestrator: records start/watermark, reads changed company IDs, publishes a tenant-wide Symbols request, then bounded-parallel company dataset requests. Watermark advancement occurs only when no enqueue failures occurred. It returns counts/failures/duration and logs structured events. `DataSyncRequest` and `DataSyncMessaging` carry work to the Worker; `DataSyncRunRow` records operational runs.

### CyclicalWaves

`CyclicalWavesDataAcquisitionService.ExecuteAsync` in `Financial/Ingestion/CyclicalWaves/CyclicalWavesDataAcquisitionService.cs` calls a typed provider client and persistence repository, checks prior successful observations, validates stale provider dates, retains full raw JSON and a canonical JSON SHA-256 response hash, and persists accepted or failed acquisition facts including requested/completed times, endpoint, HTTP status and attempt count. It reports changed/unchanged/failed/skipped counts and structured logs. This is the closest HTTP/raw-evidence model, but it is specific to acquisition check/snapshot tables rather than a generic import ledger.

### Novaran and other scheduled imports

The repository includes `NadpcoScheduledSyncCoordinator`/`NadpcoApi` sync and the generic data-sync messaging/publisher architecture. Its worker and provider options show configured bounded retry/delay behavior. No separate generic scheduled-job framework is evident from the inspected Worker paths. Feature 140 should use the shared importer from both trigger paths and the existing Worker/Admin data-sync pattern where it fits.

## 7. HTTP and resilience conventions

Provider HTTP code uses typed clients and explicit provider adapters. `CyclicalWavesDataAcquisitionClient` plus `CyclicalWavesDataAcquisitionResilienceHandler` live under `src/backend/FinancialCopilot.Infrastructure/Financial/Providers/CyclicalWaves/`; the client captures request/response facts and the acquisition layer retains results. NADPCO has `NadpcoApiResilienceHandler` and options with explicit `RetryCount`; its handler retries transient failures with bounded attempt behavior. TSETMC’s `TsetmcWebServiceClient` has explicit envelope parsing and field conversion, but it is not evidence of a shared HTTP resilience policy.

Feature 140 should use an isolated typed IME client and an explicit bounded transient retry policy aligned with the provider-specific handler style. Preserve requested date and raw body before parsing; do not infer that every provider uses one global Polly policy. HTTP status, attempts, and endpoint are available in CyclicalWaves acquisition persistence as the best precedent.

## 8. Raw payload retention

`ProviderSyncRuns` (`DataSyncRunRow`, configured in Financial Ingestion persistence) retains an import/sync run envelope: idempotency key, dataset, optional external reference, status, requested/started/completed timestamps, processed-record and error counts, error message, and optional source payload checksum. It does **not** retain raw provider response bodies, per-request HTTP status/attempt history, or parsed-row detail.

The reusable raw-body precedent is CyclicalWaves: accepted acquisition rows retain complete `RawResponseJson`, response hash, HTTP status, attempt count, endpoint, and request/acquisition/completion timestamps; failed rows retain failure code/message and request outcome metadata. Raw preservation is provider-specific rather than a shared generic response store. For IME, use a dedicated raw response evidence record (or a clearly identified IME acquisition table) while also writing the ordinary run summary; do not mistake `SourcePayloadChecksum` for retained payload evidence.

## 9. Idempotency / fingerprint

The repository favors database natural uniqueness plus upsert/unique-conflict handling:

- `Companies`: unique `(ProviderName, ExternalCompanyId)`;
- financial statements/monthly reports: provider-scoped external identity unique indexes;
- financial statement line items: parent plus metric code unique;
- StockMarketDB daily trades: provider plus source external trade ID is used to find/update observations;
- derived acquisition: canonical JSON SHA-256 checksum and successful-check lookup;
- `FinancialDataSyncProcessor.TryGetUniqueConstraintViolation` handles PostgreSQL unique violations.

No generic `SourceFingerprint` field/helper was found for arbitrary IME trade observations. Most consistent implementation is a deterministic SHA-256 fingerprint over a canonical serialization of the 34 provider-owned source fields, backed by a unique database index over the appropriate source/date scope. The fingerprint must not include `ImeProducerCode`, `ResolvedCompanyId`, `ProducerMappingId`, mapping status, or resolution timestamps; those are derived mutable metadata, not provider-observation identity. Re-resolution can then update linkage without causing a new observation identity.

## 10. Scheduler and timezone

The Worker is a .NET `BackgroundService` host (see `src/backend/FinancialCopilot.Worker/Worker.cs` and registered workers in Worker startup). `StockMarketDbPollingWorker` polls every 30 seconds and dispatches configured dataset cadences; options are under `StockMarketDbPolling`. Other Worker classes implement recurring work directly with timers/delay loops and per-worker configuration. No Quartz/Hangfire cron scheduler was found in the inspected Worker pattern.

Tehran timezone helpers are repeated where local-calendar semantics matter, using `Asia/Tehran` with platform-specific Windows fallback (`ResolveTehranTimeZone` examples in `ShamsiMonthCalculator`, `IndustryRelativeValuationOrchestrationService`, and `NadpcoApiTokenProvider`). Existing generic data-sync jobs are RabbitMQ-triggered rather than cron jobs. The repository-native approach for Saturday–Wednesday at 17:00 is a configured Worker `BackgroundService` that computes the next run using the Tehran timezone and invokes the same import service, or an existing worker cadence abstraction if one is introduced in current code; avoid assuming UTC 17:00 or a cron package.

The recurring worker implementation is process-local; no distributed scheduler lease is established by `StockMarketDbPollingWorker`. RabbitMQ-backed Admin sync uses the existing broker path and provides the better multi-instance work dispatch precedent. A schedule must have a database-backed lease/idempotency guard or use an existing shared coordination facility if multiple Worker replicas are deployed; no general single-instance scheduling guarantee was found.

## 11. Admin authorization and API

Data operations use `AdminDataOperationsController` (`src/backend/FinancialCopilot.API/Controllers/AdminDataOperationsController.cs`), route prefix `[Route("api/v1/admin")]`, and `AuthorizationPolicies.DataAdmin` (`src/backend/FinancialCopilot.API/Security/AuthorizationPolicies.cs`). For example, it has `POST data-sync/symbols`, `GET data-sync/runs`, `POST stockmarketdb/{dataset}/sync`, and `GET stockmarketdb/sync-state`. Trigger operations validate requests and publish `DataSyncRequest` to RabbitMQ; they require `DataSyncMessaging:Enabled=true` and a configured broker in API and Worker. Integration coverage is in `tests/FinancialCopilot.IntegrationTests/AdminDataOperationsEndpointTests.cs`.

Mapping endpoints should use the same `/api/v1/admin/...` route namespace, DataAdmin authorization policy, request validation, explicit count/result DTOs, and `ProblemDetails`/existing API error conventions. Use existing pagination/filtering conventions from adjacent Admin list APIs. The conceptual `/api/admin/ime/...` routes in `Design.md` are not repository-native and should be revised. The existing management API has permission-code and audit conventions documented in `docs/admin-management-api.md`, but for data import/mapping operations `DataAdmin` is the closest existing authorization policy.

## 12. Historical re-resolution execution

Repository backfills use application coordinators and bounded batches, with Admin trigger/status/progress endpoints (for example monthly activity backfill contracts and `MonthlyActivityBackfillCoordinator`). Generic dataset sync uses RabbitMQ `DataSyncRequest` dispatch and durable `DataSyncRunRow` tracking. There is no dedicated queue for entity re-resolution.

For a small bounded affected set, a shared application service may execute a transactionally bounded batch. For unbounded history, expose a DataAdmin request that dispatches through the existing data-sync messaging/Worker infrastructure and records a run/progress result, following the existing backfill coordinator pattern. Do not add a new queue. Mapping changes should commit first, then enqueue/re-run affected rows idempotently; use keyset/batch paging and ensure the operation can be resumed. The exact route should report queued/completed counts rather than running a large full-history update synchronously in an HTTP request.

## 13. IME source-field compatibility

The supplied `source-fields.json` lists 34 provider fields, including `Symbol`, `ProducerName`, `ArzehKonandeh`, price/quantity/date/warehouse/broker/settlement/category/lot fields. There is no existing IME DTO or EF row in production code (`ImeDailyTrades` was not found). Existing financial rows use typed nullable properties for provider fields and separate derived/link columns; CyclicalWaves stores provider response JSON separately from normalized metrics. This supports preserving all 34 fields as provider-owned source data and storing `ImeProducerCode`, `ResolvedCompanyId`, `ProducerMappingId`, and `MappingResolvedAtUtc` separately.

Do not flatten provider and mapping values into one mutable object without an explicit immutable raw payload boundary. The source contract is compatible in principle, but one actual IME response fixture has not been found in repository code/data to confirm JSON types, optionality, or field casing.

## 14. Producer-code prefix assumption

The spec package contains examples `KRPT-AMMONIAB-00`, `SHPC-PBR1220WWP-00`, and `SHPC-OAVAMB-00` and expects the segment before the first hyphen to be a deterministic lookup key. Repository source/test search found no matching IME records or fixtures, and `source-fields.json` defines field names only, with no example rows. Thus the extraction rule is deterministic for those examples but repository evidence does not validate it against observed provider data. No malformed or exceptional IME symbols could be identified from the repository.

Keep the prefix as a lookup key only. Require a reviewed explicit mapping; do not infer global issuer identity from `KRPT` or `SHPC`.

## 15. Exact reusable repository classes / paths

| Use | Reusable class / artifact |
|---|---|
| Company and ingestion persistence | `NormalizedCompanyRow`, `FinancialIngestionDbContext`, `NormalizedCompanyRowConfiguration` — `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/FinancialIngestionRows.cs`, `FinancialIngestionDbContext.cs`, `FinancialIngestionConfigurations.cs` |
| Domain company references | `Company`, `ProviderExternalReference` — `src/backend/FinancialCopilot.Domain/Financial/Entities/FinancialEntities.cs` |
| Company alias enrichment | `CyclicalWavesCompanyMappingService` — `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/CyclicalWaves/CyclicalWavesCompanyMappingService.cs` |
| Instrument/company relationship | `TradingInstrumentRow` and its mapping/configuration — `FinancialIngestionRows.cs`, `FinancialIngestionConfigurations.cs` |
| Daily trade importer/persistence | `StockMarketDbSyncService.PersistDailyTradesAsync`, `SqlStockMarketDbQueryExecutor`, `IStockMarketDbQueryExecutor` — `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/StockMarketDb/` and `Financial/Providers/StockMarketDb/` |
| Scheduled provider orchestration | `CodalDbScheduledSyncService`, `DataSyncRequest`, `DataSyncMessaging`, `DataSyncRunRow` — `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/CodalDb/`, `Financial/Ingestion/`, and persistence rows |
| Raw HTTP response acquisition | `CyclicalWavesDataAcquisitionClient`, `CyclicalWavesDataAcquisitionResilienceHandler`, `CyclicalWavesDataAcquisitionService`, accepted/failed acquisition repository and configuration — `Financial/Providers/CyclicalWaves/`, `Financial/Ingestion/CyclicalWaves/`, `Financial/Ingestion/Persistence/CyclicalWavesDataAcquisitionConfigurations.cs` |
| Provider retry example | `NadpcoApiResilienceHandler`, `NadpcoApiProviderOptions` — `src/backend/FinancialCopilot.Infrastructure/Financial/Providers/NadpcoApi/` |
| Worker cadence | `StockMarketDbPollingWorker`, `StockMarketDbPollingOptions` — `src/backend/FinancialCopilot.Worker/StockMarketDbPollingWorker.cs` |
| DataAdmin API pattern | `AuthorizationPolicies.DataAdmin`, `AdminDataOperationsController`, `AdminDataOperationsEndpointTests` — `src/backend/FinancialCopilot.API/Security/AuthorizationPolicies.cs`, `src/backend/FinancialCopilot.API/Controllers/AdminDataOperationsController.cs`, `tests/FinancialCopilot.IntegrationTests/AdminDataOperationsEndpointTests.cs` |
| Existing re-resolution/backfill shape | `MonthlyActivityBackfillCoordinator` and contracts; CyclicalWaves company mapping/backfill services — `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/` and Application `FinancialData/Ingestion/` |
| Schema evidence | `20260527045316_InitialFinancialIngestion.cs`, later migrations, `FinancialIngestionDbContextModelSnapshot.cs` — `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/Migrations/` |

## 16. Required Feature 140 design changes

Revise only the identity-target portions before implementation:

1. **UserStory.md / Design.md / SpecReview.md / README.md:** distinguish provider-scoped `NormalizedCompanyRow` from the domain `Company`; do not call physical `Companies.Id` canonical across providers.
2. **Design.md sections 4–5, 8–9 and Tasks.md T140-07, T140-09, T140-12, T140-15:** define the persisted mapping FK target and its provider scope, or add a prerequisite for persisted canonical-company identity. Remove `SymbolsIn` as a presumed field; it does not exist on `NormalizedCompanyRow`.
3. **T140-08 / FR-05:** filtered unique indexes are supported and can enforce active producer-code uniqueness once the meaning of a producer code and any provider scope are finalized.
4. **Design.md source examples and T140-02/05/19:** retain fixture-based contract checks as a prerequisite; examples are in specs only, not repository fixtures/data.
5. **Design.md / Tasks.md schedule and Admin routes:** use `/api/v1/admin/...` and `DataAdmin`; schedule through the Worker host with Tehran timezone logic and shared idempotency/coordination rather than introducing a scheduler framework.

No changes to the 34-field provider contract are indicated by the repository audit. Keep mapping metadata separate from those provider fields and from observation fingerprint inputs.

## 17. Implementation blockers

1. Choose a persisted canonical mapping target. Current `FinancialIngestionDbContext.Companies.Id` is provider-row identity; the domain `Company.Id` has no mapping in that context.
2. Confirm with a captured IME fixture the real field types/optionality and the three sample Symbol/ProducerName pairs before finalizing DTO/parser assumptions.
3. Decide IME raw evidence granularity (full response per requested day/page plus HTTP status/attempt details) and fingerprint scope (date plus canonicalized source facts) in the design before adding indexes.
4. Confirm Worker replica topology or add the repository-approved shared lease/idempotency behavior for a timed schedule; the existing timer worker is process-local.

## 18. Recommendation

Do not start T140-02 or implementation slices until the mapping target is corrected and recorded in the Feature 140 spec. The remaining repository patterns are concrete and reusable; they do not require a new scheduler, queue, or resilience framework.

FEATURE_140_REPOSITORY_DISCOVERY_NEEDS_CHANGES
