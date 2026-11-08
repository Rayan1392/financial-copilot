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

1. **Deploy.** Use the defaults: `IngestionEnabled=false`, `ChatProvider=NoavaranAmin`, and `AllowCalls=false`. The C1/C1-M exclusion is unconditional (census passed; no setting). Legacy calls, schedules and triggers stop; C1 and C2 take effect.
2. **Load data.** Set the credentials and enable ingestion. From Admin, run catalog, one ticker, analysis full, then Full. Spot-check the results.
3. **Enable the schedule.** Turn on `DailySyncEnabled` and observe two runs.
4. **Trial CyclicalWaves mode.** Run `ChatProvider=CyclicalWaves` in non-production with the acceptance set, including the scanner examples. Promote only by explicit decision.
5. **Rollback options:** `ChatProvider=NoavaranAmin`, `IngestionEnabled=false`. Re-including legacy CyclicalWaves-derived metrics is not an option (C1-M).

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

## Post-Closure Scope Extension — Gauge and Equilibrium APIs

**Design status:** `FEATURE_148_GAUGE_EQUILIBRIUM_EXTENSION_DESIGN_APPROVED_FOR_IMPLEMENTATION`.
This is a new approval-gated plan after the original Feature 148 closure. It does not modify the
original 18–19 day estimate or original completion status.

### Proposed implementation slices (after approval)

| Slice | Deliverable | Estimate |
|---|---|---:|
| G1 | New v2 contracts, DTO/parser fixtures, distinct configurable data client using the existing singleton auth/token pipeline | 1–1.5 d |
| G2 | Four `cyclicalwaves` latest-state entities/mappings and additive migration; upsert stores and schema/FK verification | 1–1.5 d |
| G3 | Extend sync scopes/coordinator/full schedule, per-phase counts, unmatched-symbol policy, retry/404 handling | 1–1.5 d |
| G4 | DataAdmin endpoints/UI actions, status counters, unit/PostgreSQL/Admin/regression coverage | 1–1.5 d |
| **Total** | **Full approved scope including Chat and scoped Feature 115 UX** | **14–20 d** |

The initial 4–6 day estimate above covered ingestion/storage only and is superseded by the final
14–20 day estimate below, which includes approved Chat and scoped Feature 115 UX work.

### Expected repository impact

Implementation should reuse rather than duplicate these existing components:

- `FinancialCopilot.Infrastructure/Financial/Providers/CyclicalWavesV2/CyclicalWavesV2TokenService.cs`,
  `CyclicalWavesV2Handlers.cs`, and `CyclicalWavesV2Options.cs`.
- `FinancialCopilot.Infrastructure/CyclicalWavesV2ServiceCollectionExtensions.cs` for singleton
  token ownership and typed-client registration.
- `FinancialCopilot.Domain/Financial/CyclicalWavesV2/*` and
  `FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/CyclicalWavesV2Rows.cs`,
  `CyclicalWavesV2Configurations.cs`, and `FinancialIngestionDbContext.cs` for same-schema models.
- `FinancialCopilot.Application/FinancialData/CyclicalWavesV2/CyclicalWavesV2SyncCoordinator.cs`,
  `CyclicalWavesV2SyncRunContracts.cs`, the EF sync-run store, and
  `FinancialCopilot.Worker/CyclicalWavesV2SyncWorker.cs` for scheduling/leases.
- `FinancialCopilot.API/Controllers/CyclicalWavesV2AdminController.cs`, its contracts, and
  `src/frontend/src/components/admin/cyclicalwaves-v2-page.tsx` plus its data-admin client.
- Existing tests under `tests/FinancialCopilot.UnitTests/CyclicalWavesV2/`, PostgreSQL/Admin
  integration tests, and the existing frontend Admin page tests.

Expected new files are a dedicated v2 gauge/equilibrium client and contracts/parser, four latest
state rows/configurations/stores, an additive migration, admin contract/action additions, and
fixtures/tests. Exact file splits are for implementation review; no source files are changed in
this design phase.

### Capacity and operational check

The existing global lease, 30-second queued-run poll, 300 ms configurable provider-call delay,
30-minute default lease and 30-second per-request timeout are the starting policy. A Full run will
add two catalogs plus up to one gauge-detail and one equilibrium-detail request per matched symbol.
At the current 300 ms delay, 1,366 additional detail calls alone add about 6.8 minutes of deliberate
delay, before provider latency and existing Full work. Measure a complete dry-run-like integration
against fixtures and the deployed non-production provider before deciding whether lease duration,
heartbeat frequency or pacing need adjustment. This is a sizing estimate, not a claim about catalog
size or API rate limits.

### Approval and rollout gates

1. Product Owner approves schema, host option, decimal precision policy and no-history default.
2. The Product Owner has approved Chat exposure for the three new capabilities in CyclicalWaves mode and a scoped Feature 115 P/S gauge restoration. Implementation remains separately gated.
3. Only after approval: implement fixture-first, then additive migration, store/coordinator/Admin,
   automated tests and non-production live verification if authorized credentials are available.
4. Run migration up/down/up on disposable PostgreSQL; verify no cross-schema FKs and no changes to
   existing legacy or NoavaranAmin rows.
5. Run catalog and single-symbol refresh, then full gauge/equilibrium sync in non-production; check
   counts, unmatched identities, 404s and unchanged-row behavior before enabling daily sync.

## Approved post-closure Gauge and Equilibrium implementation sequence

The Product Owner has approved the five design decisions. This ordered sequence supersedes the earlier
optional-Chat and no-legacy recommendations above. Product Owner implementation authorization was
recorded on 2026-11-08 before source changes. Preserve the
original Feature 148 closure and its known Chat-routing limitations.

1. Record explicit implementation authorization. Define the separate `CyclicalWavesV2:GaugeBaseUrl`
   default (`https://api.tahlilapp.com/api`); keep existing `DataBaseAddress` unchanged and use the
   same account/token service.
2. Add four endpoint DTO/client contracts and fixtures, then verify exact base-10 decimal behavior.
   The .NET `decimal` range/scale limitation must be resolved before using it for provider values.
3. Add three latest-state tables (`GaugeAvailability`, `GaugeDetails`, `EquilibriumRanges`), EF mappings
   and additive migration. Use unconstrained PostgreSQL `numeric`, same-schema ticker FKs, nulls and
   negative values; no history tables.
4. Implement catalog resolution and idempotent stores; then gauge and equilibrium catalog/detail
   coordinator phases. Reuse existing global lease, sync run tracking, one worker, daily schedule and
   bounded pacing/retry policy.
5. Add DataAdmin actions/status to the existing API and console; queue long operations.
6. Add CyclicalWaves-only Chat read services, semantic Persian routing, status/value presentation and
   isolation tests. NoavaranAmin never reads the new tables and has no fallback.
7. Restore only Feature 115 standalone P/S gauge from new rows. Do not restore Feature 114 ingestion,
   Feature 115 history/monthly inset/LastPS/Forward P/S, or Features 125/126/127.
8. Run API, mapping/migration, sync/Worker, Admin, Chat in both provider modes, legacy UX and full
   regression suites; then perform authorized non-production operational verification.

### Final extension estimate

| Work slice | Estimate |
|---|---:|
| Contract, host/auth and exact numeric spike | 2–3 engineering days |
| Schema, migration, matching and latest-state stores | 2–3 days |
| Coordinator, Worker schedule, Admin API/UI | 3–4 days |
| Chat data access, semantic routing and provider-mode tests | 3–4 days |
| Scoped Feature 115 P/S gauge restoration and compatibility | 2–3 days |
| Regression and operational verification | 2–3 days |
| **Total** | **14–20 engineering days** |

Estimates exclude provider changes or a newly discovered need for additional data. Risks to close during
implementation: exact numeric representation with .NET/EF/JSON, provider catalog mismatches and 404s,
full-run duration under current pacing, and UI compatibility of the standalone Feature 115 gauge.

No implementation, live request, migration, or database action was performed while preparing this
proposal.
