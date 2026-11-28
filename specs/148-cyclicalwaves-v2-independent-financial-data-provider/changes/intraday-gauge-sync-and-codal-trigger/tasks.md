# Tasks: gauge and equilibrium sync during market hours, and on CodalMirror announcements

Status: Gate B: Approved by the owner on 2026-11-28 («یک اجرای فول روزانه هم در ساعت 19:00 اضافه کن علاوه بر 6 صبح با بقیه موافقم گیت B مورد تایید است»; Gate A the same day)

## Feature Folder

`148-cyclicalwaves-v2-independent-financial-data-provider/changes/intraday-gauge-sync-and-codal-trigger`

## Before Starting

- [x] This file's `Status:` line records Gate B approval by the owner.
- [x] Read `design-proposal.md` and `user-story.md` in this folder.
- [x] No open question in `specs/open-questions.md` blocks this change (OQ-184 is open and not blocking).
- [ ] The working tree holds uncommitted Feature 168 work, including `.env.example`, `docker-compose.yml` and both
  `appsettings.json` files. Edit only this change's lines there and stage only this change's hunks when asked to commit.

## Implementation Tasks

### 1. Settings and schedule

- [x] `CyclicalWavesV2Options`: default `DailySyncCronUtc = "30 2,15 * * *"` (06:00 and 19:00 Tehran; the 19:00 run was added by the owner with Gate B); new `IntradaySyncEnabled`,
  `IntradayWindowStartTehran` ("09:00"), `IntradayWindowEndTehran` ("12:30"), `IntradayIntervalMinutes` (30),
  `CodalGaugeRefreshEnabled`, `CodalGaugeRecheckMinutes` (30); validation and message per the user story.
- [x] `CyclicalWavesV2IntradaySchedule` in `FinancialCopilot.Application/FinancialData/CyclicalWavesV2/` (pure, no
  framework types): built from window start, window end and interval; `GetNextOccurrence(afterUtc)` returns the next slot
  in UTC for Saturday to Wednesday in UTC+03:30.
- [x] `appsettings.json` of the API and the Worker, `docker-compose.yml` (API and Worker services) and `.env.example`:
  new keys, new cron default. `docker/appsettings.Production.json` has no `CyclicalWavesV2` section and stays untouched.

### 2. Run queue

- [x] `CyclicalWavesV2SyncTrigger.CodalAnnouncement`.
- [x] `CyclicalWavesV2SyncRunRow.NotBeforeUtc`, configuration, hand-written additive migration
  `AddCyclicalWavesV2SyncRunNotBefore` and the model snapshot.
- [x] `ICyclicalWavesV2SyncRunStore.EnqueueAnnouncementRefreshAsync(ticker, requestedBy, now, notBeforeUtc, maximumPending)`
  returning queued, duplicate or queue-full; it does not look at active runs.
- [x] `RequestAsync` and `ClaimNextRequestedAsync` take the due time into account (requirements 11 and 12);
  `CyclicalWavesV2SyncRunSummary` carries `NotBeforeUtc`.

### 3. Worker

- [x] `CyclicalWavesV2SyncWorker`: second schedule beside the daily one; at a due slot run `GaugeDetails` then
  `EquilibriumDetails` through the coordinator; drop a slot that is one interval late; log start and drop.
- [x] `ProcessRequestedRunsAsync` drains up to 50 due queued runs per poll and stops on the first empty claim.
- [x] `RunAsync` takes the scope as a parameter; the daily and catch-up paths keep `Full`.

### 4. CodalMirror trigger

- [x] Port `ICodalAnnouncementGaugeRefreshTrigger` and application service `CodalAnnouncementGaugeRefresh` in
  `FinancialCopilot.Application/FinancialData/CyclicalWavesV2/`: switch check, catalog lookup
  (`ICyclicalWavesV2Store.FindCatalogTickerAsync`), immediate and deferred enqueue, outcomes logged, no exception leaves
  it except cancellation.
- [x] Infrastructure adapter that runs the service in its own DI scope, so the run store's `ChangeTracker.Clear()` and
  `SaveChanges` never touch the handler's `FinancialIngestionDbContext`; registration in
  `CyclicalWavesV2ServiceCollectionExtensions`, settings mapped from `CyclicalWavesV2Options`.
- [x] `TelegramChannelMonthlyReportHandler.HandleAsync`: call the trigger after a new claim, before `ProcessClaimAsync`.
- [x] `TelegramChannelFinancialStatementHandler.HandleRecognizedAsync`: call the trigger after the first claim is saved
  (`existing is null`), before the `AutoBackfillEnabled` branch; update the handler factory in
  `ServiceCollectionExtensions`.

### 5. Admin status

- [x] `CyclicalWavesV2StatusResponse` and `CyclicalWavesV2RunResponse`: new members; the active run ignores runs that are
  not yet due.
- [x] `data-admin-client.ts` types and `cyclicalwaves-v2-page.tsx`: the two new status blocks.

## Tests

- [x] `CyclicalWavesV2SyncTests` (unit): schedule slots, weekend, week wrap, window edges, interval that does not divide
  the window; options validation; Worker runs both scopes at a slot, neither when off, drops a late slot, drains the
  queue.
- [x] Unit tests of `CodalAnnouncementGaugeRefresh`: queued twice, recheck 0 queues once, switch off, ingestion off, not
  in catalog, duplicate, queue full, store exception swallowed and logged.
- [x] `TelegramChannelMonthlyReportHandlerTests` and `TelegramChannelFinancialStatementHandler143Tests`: trigger called
  once for a new post, not for a replay, not for an unrecognized or unauthorized post; a throwing trigger does not change
  the result.
- [x] `CyclicalWavesV2PostgreSqlIntegrationTests`: enqueue while a run is `Running`; a deferred run is not claimed before
  its due time and is claimed after; duplicate and cap; Admin `RequestAsync` with only a deferred run pending; migration
  up, down, up.
- [x] `CyclicalWavesV2CompositionTests`: the trigger resolves in the API host; architecture tests stay green.
- [x] `cyclicalwaves-v2-page.test.tsx`: the new blocks render from the status response.

## Records

- [x] `docs/providers/cyclicalwaves-v2/README.md`: schedule, settings, announcement trigger, queue rules.
- [x] `specs/README.md` index entry; `specs/backlog.md`: v2 screener and income statement of the announced symbol.
- [x] `CHANGELOG.md` `[Unreleased]`: a Persian line that the gauges and the equilibrium price are refreshed during market
  hours and after a new filing.

## Definition Of Done

- [~] `dotnet test src/backend/FinancialCopilot.sln --configuration Release`: no failure outside `specs/test-baseline.md`
  and the open bugs BUG-050 and BUG-053. 2026-11-28: architecture 24/24; unit 29 failures, all in the baseline, BUG-050
  or `Availability_CoversEveryRouteInBothModes` (`option_comparison`, Feature 168 work in the tree); integration not
  conclusive: Docker Desktop was not running, so the PostgreSQL tests failed to start. Rerun with Docker up: the
  `CyclicalWavesV2AdminEndpointTests` and `CyclicalWavesV2PostgreSqlIntegrationTests` pass (62 of 81 in the filtered run;
  the 19 failures are the gauge E2E tests of BUG-053). The rest of the integration suite was not rerun.
- [x] `dotnet ef migrations has-pending-model-changes --context FinancialIngestionDbContext` reports none.
- [~] In `src/frontend`: `npm run build` green; `npm run typecheck` 37 errors, the baseline; `npm run test` 4 failures and
  3 browser suites, none in files this change touched (BUG-040, BUG-051 and a `user-analytics-panel` case that fails
  differently on each run); `npm run lint` errors only on pre-existing lines.
- [ ] Acceptance criteria AC-01 to AC-11 checked.
- [x] No live TahlilApp or OpenAI call is made by the agent.

## Owner steps (not done by the agent)

- [x] Commit (`acda444d`, pushed to `develop` 2026-11-28).
- [ ] Release and the production migration.
- [ ] Server `.env`: remove or change `CYCLICALWAVES_V2_DAILY_SYNC_CRON_UTC` to `30 2,15 * * *`; set
  `CYCLICALWAVES_V2_CODAL_GAUGE_REFRESH_ENABLED=true` and, if another delay than 30 minutes is wanted,
  `CYCLICALWAVES_V2_CODAL_GAUGE_RECHECK_MINUTES`. Leave `CYCLICALWAVES_V2_INTRADAY_SYNC_ENABLED` off (OQ-184 answer). The first Worker
  start runs one catch-up `Full` when no `Full` run has completed since the latest of the two new times.
- [~] AC-12 not needed while the market-hours run stays off (OQ-184 answer, 2026-11-28).
- [x] OQ-184: asked TahlilApp; answered 2026-11-28.
