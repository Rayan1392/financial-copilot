# Feature 148 — Implementation Plan (rev 3, final)

The task-level breakdown is in [tasks.md](./tasks.md). Effort is in focused engineering days (d).

## Slices

| # | Slice (task group) | Delivers | Depends on | Effort |
|---|---|---|---|---|
| 00 | Preparation | Contract PDFs committed, fixtures, live contract verification (B2/B3), C1 census preparation | — | 0.5 d |
| 01 | Persistence | 11 entities in `cyclicalwaves`, 29-row seed, one additive migration, stores, run store | 00 | 2.5 d |
| 02 | Client and authentication | Options, token service, auth handler, 7-endpoint client, DTO mapping | 00 | 1.5 d |
| 03 | Synchronization | Coordinator: Catalog / Ticker / Screener / IncomeStatements / Analysis (full + incremental) / Full | 01, 02 | 1.5 d |
| 04 | Worker and Admin | Daily worker, catch-up, requested-run poller, Admin API, Data Management page, config/compose entries | 03 | 2.5 d |
| 05 | Legacy retirement | Guard, unhosted legacy workers, `410` legacy triggers, flags, detector unregistered, spec headers | — | 1 d |
| 06 | Chat routing and isolation | Availability policy, CyclicalWaves-mode DI swaps (resolver, lookup, quarterly table, analysis), C2, C1 filter and census | 01 | 4 d |
| 07 | Basic scanner | CyclicalWaves scanner executor, shared mapping, monetary threshold resolver, growth, Feature 116 baselines | 06 | 3 d |
| 08 | Verification and report | Cross-cutting tests, regressions, migration up/down, live smoke (if credentials exist), `ImplementationReport.md` | all | 2 d |

**Total:** about **18–19 d**.

**Critical path:** 00 → 01 → 03 → 04 → 08, with 06 → 07 running in parallel after 01. Slice 05 is independent; deploy it first operationally.

## Files likely to change

**New**

- `src/backend/FinancialCopilot.Domain/Financial/CyclicalWavesV2/*`
- `src/backend/FinancialCopilot.Application/FinancialData/CyclicalWavesV2/*`
- `src/backend/FinancialCopilot.Application/Scanner/CyclicalWavesV2/*`
- `src/backend/FinancialCopilot.Application/AI/Orchestration/ChatCapabilityAvailability.cs`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Providers/CyclicalWavesV2/*`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/CyclicalWavesV2/*`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/CyclicalWavesV2Configurations.cs`, `CyclicalWavesV2Rows.cs`, and the migration
- `src/backend/FinancialCopilot.Infrastructure/Financial/Providers/CyclicalWaves/LegacyCyclicalWavesCallBlockingHandler.cs`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Scanner/LegacyCyclicalWavesDerivedMetricFilter.cs`
- `src/backend/FinancialCopilot.Worker/CyclicalWavesV2SyncWorker.cs`
- `src/backend/FinancialCopilot.API/Controllers/CyclicalWavesV2AdminController.cs` and its contracts
- `src/frontend/src/routes/admin_.data.cyclicalwaves-v2.tsx`
- `src/frontend/src/components/admin/cyclicalwaves-v2-page.tsx`
- Tests under `tests/*/CyclicalWavesV2/` and fixtures
- `docs/providers/cyclicalwaves-v2/*`

**Modified (narrow)**

| File | Change |
|---|---|
| `src/backend/FinancialCopilot.Infrastructure/ServiceCollectionExtensions.cs` | v2 registrations, CyclicalWaves-mode swaps, legacy guard, detector unregistration |
| `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/FinancialIngestionDbContext.cs` | `DbSet`s |
| `src/backend/FinancialCopilot.Worker/Program.cs` | Add the v2 worker; remove the 3 legacy hosted services |
| `src/backend/FinancialCopilot.Application/AI/Orchestration/SemanticCapabilityExecutionContracts.cs` | Dispatcher availability check |
| `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs` | Guards on legacy branches, tools and the Feature 141 fallback |
| `src/backend/FinancialCopilot.Infrastructure/Financial/Scanner/EfCoreSymbolMetricLookupService.cs`, `EfCoreScannerExecutionService.cs`, and `src/backend/FinancialCopilot.Infrastructure/Financial/ProfessionalScanners/ProfessionalScannerUseCases.cs` | C1 filter call only |
| `src/backend/FinancialCopilot.API/Controllers/AdminDataOperationsController.cs`, `CyclicalWavesPsVisualizationAdminController.cs` | Legacy triggers return `410` |
| `src/backend/FinancialCopilot.API/appsettings.json`, `src/backend/FinancialCopilot.Worker/appsettings.json`, `docker-compose.yml`, `.env.example` | New sections; legacy flags; optional legacy credentials |
| `src/frontend/src/integrations/financial-copilot/data-admin-client.ts`, `data-management-shell.tsx`, `data-management-overview-page.tsx` | v2 page, navigation, overview card relabel |
| `tests/FinancialCopilot.ArchitectureTests/CleanArchitectureDependencyTests.cs` | Hosted list; guard rule; v2 layering |
| Spec headers, `specs/README.md`, `specs/implementation-checklist.md`, root `README.md` | Documentation updates |

**Not modified:**

- NoavaranAmin ingestion, persistence, repositories, catalogs, formulas, and `CompanyResolverService`
- `ConditionalTrackerEvaluationProcessor`
- every legacy row

## Database migration strategy

- One **additive** migration, `Feature148CyclicalWavesV2Foundation`, in `FinancialIngestionDbContext`: `EnsureSchema("cyclicalwaves")`, 11 tables, and the 29 seed rows.
- `Down` drops the 11 tables and the schema.
- No `public` object is altered and no legacy row is touched.
- It is applied by the existing API-startup migrator.
- It is verified first on disposable PostgreSQL 18; Docker is unavailable locally.
- There is no backfill migration; the first Full run populates the data.

## Verification strategy

- **Unit tests:** pure logic.
  - Comparer and diff
  - Mapping, growth and monetary threshold resolver
  - Availability matrix
  - Token service
  - Blocking handler
  - C1 filter
  - Analysis matching
- **PostgreSQL integration tests:** stores, constraints, FK isolation, migration up/down, and the C1 filter over seeded mixed rows.
- **Endpoint tests:** v2 Admin flows (`403`/`202`/`409`) and the legacy `410` triggers.
- **Chat and scanner tests:**
  - Spy ports prove zero NoavaranAmin or legacy reads in CyclicalWaves mode.
  - NoavaranAmin mode: C2 unavailable, retired capabilities, C1 behavior.
- **Regression suites (NoavaranAmin mode):** the existing symbol-lookup, scanner, professional-scanner, quarterly-table, AI-facade, NoavaranAmin-ingestion and Telegram suites.
- **Live smoke runs:** catalog, one ticker, analysis incremental, and Full. These run only with v2 credentials and are reported verbatim, or reported as not executed.

## Rollout

1. **Deploy.** Use the defaults: `IngestionEnabled=false`, `ChatProvider=NoavaranAmin`, `AllowCalls=false`, and `ExcludeLegacyCyclicalWavesDerivedMetrics=true` (census already passed). Legacy calls, schedules and triggers stop; C1 and C2 take effect.
2. **Load data.** Set the credentials and enable ingestion. From Admin, run catalog, one ticker, analysis full, then Full. Spot-check the results.
3. **Enable the schedule.** Turn on `DailySyncEnabled` and observe two runs.
4. **Trial CyclicalWaves mode.** Run `ChatProvider=CyclicalWaves` in non-production with the acceptance set, including the scanner examples. Promote only by explicit decision.
5. **Rollback options:** `ChatProvider=NoavaranAmin`, `IngestionEnabled=false`, `ExcludeLegacyCyclicalWavesDerivedMetrics=false`.

## Blockers and verifications

| ID | Item | Handling |
|---|---|---|
| B1 | The two PDFs exist only as chat attachments | T148-00.1 needs file copies from the Product Owner. |
| B2 | `/income-statements/lines` response shape is undocumented | Tolerant parser now; live check in T148-00.3. |
| B3 | `/blogs` tag naming, ticker linkage and the `from_date` format need live confirmation | Live check in T148-00.3; fixtures are adjusted from the evidence. |
| B4 | v2 credentials are needed for live verification | Without them, live items are reported as not executed. |
| B5 | The C1 census may find mixed-provider or policy-only rows | T148-06.6 pauses for Product Owner review. |
| N1 | `ConditionalTrackerEvaluationProcessor` reads `DerivedMetrics`, including legacy rows | Outside the C1 authorization and not changed. Reported for a separate decision. |

## Risks

| Risk | Mitigation |
|---|---|
| v2 has no company names or industries (L1) | Questions by company name or industry return NotFound or unavailable in CyclicalWaves mode; there is no enrichment from NoavaranAmin. |
| The monetary unit cannot be resolved from the user's text | Ask a clarification question; never assume a unit. |
| The parser may not emit both bounds for "between" | Covered by test T148-07.4. If the parser fails, record it as a limitation; the shared parser is not changed. |
| Refresh-token reuse invalidates the login | Single-flight token service, with the new refresh token stored immediately. |
| The large workflow legacy path | Three choke-point guards, plus spy tests. |
| Docker unavailable locally | Use the disposable PG18 connection string. |
