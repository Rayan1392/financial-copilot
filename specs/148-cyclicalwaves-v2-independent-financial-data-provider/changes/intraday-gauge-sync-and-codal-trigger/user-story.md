# User Story: gauge and equilibrium sync during market hours, and on CodalMirror announcements

## Feature Folder

`148-cyclicalwaves-v2-independent-financial-data-provider/changes/intraday-gauge-sync-and-codal-trigger`

## Layers / Area

Layers: backend | database | frontend | ops
Area: FinancialData | Telegram | Administration

## Priority

P1

## Complexity

M

## Depends On

148-cyclicalwaves-v2-independent-financial-data-provider, 133 (Telegram channel monthly report trigger),
143-telegram-financial-statement-report-trigger

## References

- `design-proposal.md` in this folder (approved at Gate A on 2026-11-28)
- `docs/providers/cyclicalwaves-v2/README.md`, `docs/providers/cyclicalwaves-v2/gauge-equilibrium-api-contract.md`
- Data model: `FinancialIngestionDbContext`, `CyclicalWavesV2SyncRunRow`, `CyclicalWavesV2SyncRunRowConfiguration`
  (table `cyclicalwaves."SyncRuns"`)
- Code: `CyclicalWavesV2SyncWorker`, `CyclicalWavesV2SyncCoordinator`, `EfCoreCyclicalWavesV2SyncRunStore`,
  `CyclicalWavesV2Options`, `CyclicalWavesV2AdminController`, `TelegramChannelMonthlyReportHandler`,
  `TelegramChannelFinancialStatementHandler`, `TsetmcPollingWorker` (trading-day rule)
- `specs/open-questions.md` OQ-180 to OQ-184

## Standards Source Of Truth

Obey `.claude/rules/*`. Do not copy rule text into this spec.

## User Story

As a Sapio user who looks at the P/E and P/S gauges and the equilibrium price of a symbol,
I want those values refreshed before the market opens, every 30 minutes while it is open, and right after the company
publishes a monthly report or financial statement,
so that the cards show the provider's current values instead of last night's.

## Business Context

The gauge and equilibrium cards (Feature 159) and the v2 industry comparison (Feature 125) read
`cyclicalwaves."GaugeDetails"` and `cyclicalwaves."EquilibriumRanges"`, which today change once a day at 03:00 Tehran.
The provider's values move with the price and with new filings.

## Scope

- Default times of the daily `Full` runs: 06:00 and 19:00 Tehran, every day (the 19:00 run was added by the owner with
  Gate B on 2026-11-28).
- Market-hours runs: `GaugeDetails` then `EquilibriumDetails`, Saturday to Wednesday, 09:00 to 12:30 Tehran, every
  `IntradayIntervalMinutes` (30).
- Gauge refresh of the announced symbol on CodalMirror monthly report and financial statement posts: immediately and
  again after `CodalGaugeRecheckMinutes` (30).
- Queue changes that make the above reliable: requests accepted while a run is active, due time, several queued runs per
  poll.
- Admin status endpoint and page show the market-hours schedule, the next market-hours run and the new trigger.
- Settings in `appsettings.json` (API and Worker), `docker-compose.yml`, `.env.example`.

## Out Of Scope

- Screener, income statements, catalogs and analyses during market hours.
- The TSETMC price poll.
- A public-holiday calendar.
- Restoring the «مدیریت داده» entry of the Admin panel (BUG-056).
- Refreshing the v2 screener or income statement of the announced symbol (→ `specs/backlog.md`).

## Functional Requirements

1. With `DailySyncEnabled`, a `Full` run starts at each `DailySyncCronUtc` occurrence; the default is `30 2,15 * * *`
   (06:00 and 19:00 Tehran).
2. With `IngestionEnabled` and `IntradaySyncEnabled`, the Worker starts a `GaugeDetails` run and then an
   `EquilibriumDetails` run (trigger `Scheduled`) at every slot. Slots are `IntradayWindowStartTehran` plus a whole
   number of `IntradayIntervalMinutes`, up to and including `IntradayWindowEndTehran`, on Saturday to Wednesday in Tehran
   time (UTC+03:30).
3. A slot whose start is delayed by `IntradayIntervalMinutes` or more is dropped and logged. After a Worker start the
   next slot is the first one after the start time.
4. The second run of a slot starts even when the first one failed or was skipped.
5. On the first delivery of an authorized, recognized CodalMirror monthly report post or financial statement post, and
   with `IngestionEnabled` and `CodalGaugeRefreshEnabled`, the system queues a `TickerGaugeEquilibrium` run for the
   announced symbol with trigger `CodalAnnouncement`, due immediately, and, when `CodalGaugeRecheckMinutes` is above
   zero, a second one due that many minutes later.
6. A redelivered or replayed post queues nothing.
7. A symbol that is not in the v2 ticker catalog queues nothing; the skip is logged with the symbol.
8. A symbol that already has a pending announcement run for the same due kind (immediate, deferred) gets no second one.
9. When 500 announcement runs are pending, further requests are dropped and logged as a warning.
10. Queuing never changes the result of the Feature 133 or Feature 143 handling; a queuing error is logged.
11. Announcement requests are accepted while another run is `Requested` or `Running`. Admin queue requests keep today's
    rule, except that a run whose due time is in the future does not count as active.
12. The Worker claims only runs that are due, oldest request first, and runs up to 50 queued runs per poll.
13. The Admin status endpoint returns the market-hours settings, the next market-hours slot and each run's due time; a
    run that is not yet due is not reported as the active run.

## Data / Schema

`FinancialIngestionDbContext`, table `cyclicalwaves."SyncRuns"`: new nullable column `NotBeforeUtc`
(`timestamp with time zone`). Null means due immediately. Additive migration, no backfill, no new index (the table holds
only run history and the queue is filtered by `Status`). `TriggerSource` gets the new value `CodalAnnouncement`; the
column has no check constraint.

## API (if any)

| Method | Route | Request | Response | Notes |
|---|---|---|---|---|
| GET | `/api/v1/admin/cyclicalwaves-v2/status` | unchanged | adds `intradaySyncEnabled`, `intradayWindowStartTehran`, `intradayWindowEndTehran`, `intradayIntervalMinutes`, `nextIntradayRunUtc`, `codalGaugeRefreshEnabled`, `codalGaugeRecheckMinutes`; each run adds `notBeforeUtc` | Additive |

## AI Query Path (if any)

None. Answers read the same tables.

## UI (if any)

`src/frontend/src/components/admin/cyclicalwaves-v2-page.tsx`: under the daily schedule, «همگام‌سازی حین بازار» (on/off,
window and interval in Tehran time, next run) and «به‌روزرسانی با اطلاعیهٔ کدال» (on/off, recheck minutes). The run table
shows the trigger value as it does today.

## Non-Functional Requirements

- Settings are validated at start: window start before end, both `HH:mm`; interval 5 to 240 minutes; recheck 0 to 1,440
  minutes. An invalid value stops the host, as for the other `CyclicalWavesV2` settings.
- Provider calls keep the existing pacing, timeout, retry and lease settings.
- Logs: slot started, slot dropped, announcement request queued, skipped (not in catalog, duplicate, cap) and failed,
  each with the symbol and the correlation id of the Telegram update; no post text.
- New switches default to off in code, `docker-compose.yml` and `.env.example`.

## Acceptance Criteria

- [ ] AC-01: with default settings the daily schedule has exactly two occurrences a day, 02:30 UTC and 15:30 UTC.
- [ ] AC-02: for a Saturday in Tehran the market-hours schedule yields exactly 09:00, 09:30, …, 12:30 (8 slots,
  05:30 to 09:00 UTC); it yields none on Thursday and Friday; after Wednesday 12:30 the next slot is Saturday 09:00.
- [ ] AC-03: at a slot the Worker runs `GaugeDetails` and then `EquilibriumDetails`; with `IntradaySyncEnabled` off it
  runs neither.
- [ ] AC-04: a slot delayed by one interval or more is not run.
- [ ] AC-05: a first monthly report post for a symbol in the v2 catalog creates two `Requested` runs
  (`TickerGaugeEquilibrium`, `CodalAnnouncement`), one without due time and one due 30 minutes later; the same holds for
  a financial statement post.
- [ ] AC-06: a replayed post, a symbol outside the catalog, and `CodalGaugeRefreshEnabled = false` create none.
- [ ] AC-07: the two runs are created while another run is `Running`; the Worker runs the immediate one after that run
  ends and the deferred one only after its due time.
- [ ] AC-08: an exception while queuing leaves the Feature 133 and Feature 143 results as they are today.
- [ ] AC-09: an Admin queue request is accepted while only a not-yet-due run is pending, and refused as today while a run
  is `Running`.
- [ ] AC-10: the migration adds only `NotBeforeUtc` and rolls back cleanly; existing rows read as due immediately.
- [ ] AC-11: the status endpoint and the Admin page show the market-hours schedule and the next slot.
- [ ] AC-12 (owner, production): after one trading day with the switches on, the run history shows 8 `GaugeDetails` and 8
  `EquilibriumDetails` scheduled runs between 05:30 and 09:30 UTC, none `SkippedAlreadyRunning` because of the previous
  slot, and announcement runs for the day's CodalMirror posts.

## Assumptions

- TahlilApp accepts about 9,250 more calls per trading day (OQ-184, open, not blocking: the switches allow turning it
  off).
- A market-hours cycle ends within 30 minutes (estimate 12 to 15 minutes from the production figures of 2026-11-28).
- The API and the Worker are deployed together, so neither reads a `CodalAnnouncement` row with the old enum.
- Trading days are Saturday to Wednesday, as `TsetmcPollingWorker` already assumes.
