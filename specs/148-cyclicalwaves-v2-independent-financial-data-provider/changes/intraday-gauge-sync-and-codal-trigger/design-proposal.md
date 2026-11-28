# Design Proposal: gauge and equilibrium sync during market hours, and on CodalMirror announcements

Status: Gate A: Approved by the owner on 2026-11-28 («با گیت A موافقم با شروط بالا»; conditions in section 6)

## Feature Folder

`148-cyclicalwaves-v2-independent-financial-data-provider/changes/intraday-gauge-sync-and-codal-trigger`

Layers: backend, database, frontend (Admin status page), ops · Area: FinancialData, Telegram, Administration

## 1. Request (owner, 2026-11-28)

1. CyclicalWaves v2 data is fetched once a day. Fetch it once before the market opens (for example 06:00 Tehran) and
   every 30 minutes from 09:00 to 12:30 Tehran, Saturday to Wednesday.
2. A related post in the CodalMirror Telegram channel already triggers the Noavaran Amin monthly report and financial
   statement fetch. The TahlilApp gauge services must be called at the same moments.
3. The interval during market hours must be a setting. "Price" in the request meant the equilibrium price
   (قیمت تعادلی), which item 1 covers. The TSETMC price poll (every 60 seconds) stays as it is.

## 2. Findings

- `CyclicalWavesV2SyncWorker` runs one `Full` sync per `CyclicalWavesV2:DailySyncCronUtc` occurrence
  (`30 23 * * *` = 03:00 Tehran, every day), a startup catch-up, and queued Admin runs.
- Production measurements (status endpoint, 2026-11-28): scheduled `Full` took 19 min 7 s for about 2,550 provider
  calls; `GaugeDetails` alone took 7 min 3 s for 656 tickers. Equilibrium targets: 500.
- One run at a time: a partial unique index on `Status = 'Running'` is the lease.
  `ICyclicalWavesV2SyncRunStore.RequestAsync` refuses a queued request while any run is `Requested` or `Running`.
  The production report shows three `TickerGaugeEquilibrium` requests skipped while `GaugeDetails` was running.
- The Worker claims one queued run per poll (`RequestedRunPollSeconds`, 30 s).
- CodalMirror posts are handled in the API process: `TelegramChannelMonthlyReportHandler` (Feature 133) and
  `TelegramChannelFinancialStatementHandler` (Feature 143). Neither calls CyclicalWaves v2.
- 09:00–12:30 Tehran is 05:30–09:00 UTC; one five-field UTC cron cannot express it without extra occurrences.

## 3. Design

### 3.1 Schedule (Worker)

| Run | When (Tehran) | Content |
|---|---|---|
| Pre-market | 06:00, every day | `Full`, unchanged content |
| Evening | 19:00, every day | `Full` (added by the owner with Gate B on 2026-11-28) |
| Market hours | Saturday to Wednesday, 09:00 to 12:30 inclusive, every 30 minutes (8 slots) | `GaugeDetails`, then `EquilibriumDetails` |

- Pre-market and evening: the default of `DailySyncCronUtc` becomes `30 2,15 * * *`. No new mechanism.
- Market hours: two consecutive runs with the existing scopes, so no new scope and no change to the scope check
  constraint; "last success by scope" on the Admin page stays meaningful.
- New settings, in Tehran local time (fixed UTC+03:30): `IntradaySyncEnabled`, `IntradayWindowStartTehran`,
  `IntradayWindowEndTehran`, `IntradayIntervalMinutes`. Trading days Saturday to Wednesday are a constant, as in
  `TsetmcPollingWorker`.
- A slot that could not start within one interval of its time is dropped; slots are never caught up after a restart.
- Public holidays are not known to the system; the runs happen and find unchanged data.

### 3.2 CodalMirror trigger

- On the first delivery of an authorized, recognized monthly report or financial statement post, the handler asks for a
  gauge refresh of that symbol. The request does not depend on the Noavaran Amin outcome or switches.
- Two queued runs of scope `TickerGaugeEquilibrium` with the new trigger `CodalAnnouncement`: one due immediately, one
  due after `CodalGaugeRecheckMinutes` (30), because it is not known how fast TahlilApp recalculates after a filing.
- The queue accepts these requests while another run is active. A deferred request needs a due time on the run row
  (`NotBeforeUtc`, additive nullable column).
- A symbol that is not in the v2 catalog is skipped and logged. A pending request for the same symbol is not duplicated.
  The number of pending announcement requests is capped.
- A failure to queue is logged and never fails the Noavaran Amin handling.
- The Worker drains several queued runs per poll, so a burst of announcements is not limited to one symbol per 30 s.

## 4. Alternatives considered

- `Full` every 30 minutes: fits today (19 min at night) but with little margin, and statements, catalogs and analyses do
  not move with the price. Rejected by the owner.
- Screener in the market-hours run: not needed now (owner).
- A new composite scope: needs a change to the scope check constraint and gives one history row instead of two; no gain.
- Calling the provider inline from the Telegram handler: would hold the webhook request on a slow dependency and collide
  with the run lease. Rejected.

## 5. Risks

- Provider load: about 9,250 extra calls per trading day (8 × 1,156). TahlilApp's rate limits are not known (OQ-184).
- A market-hours cycle is estimated at 12 to 15 minutes from the measurements above; it has not been measured during
  market hours.
- A long run (`Full`, a market-hours cycle) delays an announcement refresh by up to its length.
- A market-hours slot is recorded as `SkippedAlreadyRunning` when an Admin inline run holds the lease at that moment.

## 6. Owner decisions (2026-11-28)

- OQ-180: market-hours content is gauges and equilibrium ranges only; no screener.
- OQ-181: no change to the TSETMC price poll.
- OQ-182: the 06:00 run stays daily, because filings are also published on Thursday and Friday.
- OQ-183: announcement refresh is gauge and equilibrium of the announced symbol, immediately and once more after
  30 minutes.
