# CyclicalWaves v2 (TahlilApp API v2) — authoritative provider contracts

These three documents are the versioned source of truth for Feature 148
([spec](../../../specs/148-cyclicalwaves-v2-independent-financial-data-provider/spec.md)).

| File | Title | Version / date | SHA-256 |
|---|---|---|---|
| `tahlilapp-api-client.pdf` | مستندات API — سرویس داده (auth, financial screener, comprehensive analysis) | 2.0 | `77b6a2b2bc623797887c7dba94b5b797c10be8078a38328c9e99cbf927f55caa` |
| `income-statement-api-client.pdf` | مستندات سرویس صورت سود و زیان فصلی | ۲ آبان ۱۴۰۵ | `9d6cc5f2d9427aefdbe3d00ac3462482b75fb523729fa892881f8646df144d90` |
| `gauge-equilibrium-api-client.pdf` | مستندات سرویس گیج P/E و P/S و بازه قیمت تعادلی | ۱۳ آبان ۱۴۰۵ | `E0FD0EAC5B1A69B6EC696794061D9E3450497D6FAF6431FC4DEFF84B54B2CDA4` |

**Provenance:** the first two PDFs were supplied by the Product Owner as chat attachments on 2026-10-30
(Feature 148 phase 1) and committed unchanged on 2026-10-31. The gauge/equilibrium PDF was supplied from
the Product Owner's local file on 2026-11-07 and copied unchanged for the post-closure scope extension.

**Endpoints:** auth server `https://auth.cycfx.com/api` (`POST /auth/login`, `POST /auth/refresh`, header
`x-client-id: default`), data server `https://api.tahlilapp.com/api` (`GET /custom-filtering/tickers`,
`GET /custom-filtering/ticker/{ticker}`, `GET /income-statements/ticker/{ticker}?limit=N`,
`GET /income-statements/lines`, `GET /blogs?filter[type]=analysis`). Details the documents leave open were
verified against the live provider; see
[contract-verification.md](../../../specs/148-cyclicalwaves-v2-independent-financial-data-provider/contract-verification.md).

The gauge/equilibrium PDF documents `GET /pe-ps-gauges/tickers`, `GET /pe-ps-gauges/ticker/{ticker}`,
`GET /equilibrium-ranges/tickers`, and `GET /equilibrium-ranges/ticker/{ticker}`. Its transcribed
contract is [gauge-equilibrium-api-contract.md](./gauge-equilibrium-api-contract.md). It is part of
the Feature 148 post-closure extension, authorized for implementation on 2026-11-08 and currently in
progress. The routes are implemented in application code but have not been live-verified. The configuration is
`CyclicalWavesV2:GaugeBaseUrl=https://api.tahlilapp.com/api`, separate from the unchanged
`DataBaseAddress`, using the existing shared account and token lifecycle. The approved scope includes
all three Chat capabilities only in CyclicalWaves mode and a scoped Feature 115 standalone P/S gauge
restoration. Feature 115 historical/forward/monthly sub-capabilities and Features 114, 125, 126 and 127
remain disabled. NoavaranAmin never reads these tables and remains unchanged.

**Legacy (retired by Feature 148, kept for history only):** `docs/cw_api.txt` and
`docs/Cyclical Waves_api_document_2026_05_26.pdf` describe the retired `back1.cyclicalwaves.com` integration.
No code calls those endpoints any more.

## Synchronization schedule and announcement refresh (Feature 148 change `intraday-gauge-sync-and-codal-trigger`)

`CyclicalWavesV2SyncWorker` (Worker process) runs everything below one after another, so these runs never compete with
each other for the run lease (one `Running` row at a time). `IngestionEnabled` gates all of it.

| Run | Switch | When | Content |
|---|---|---|---|
| Full | `DailySyncEnabled` | `DailySyncCronUtc`, default `30 2,15 * * *`: 06:00 and 19:00 Tehran, every day | catalog, statement lines, screener and income statements, gauge and equilibrium catalogs and details, analyses |
| Market hours | `IntradaySyncEnabled` | Saturday to Wednesday, `IntradayWindowStartTehran` (09:00) to `IntradayWindowEndTehran` (12:30) inclusive, every `IntradayIntervalMinutes` (30) | `GaugeDetails`, then `EquilibriumDetails` (two rows in the run history, trigger `Scheduled`) |
| Announcement | `CodalGaugeRefreshEnabled` | first delivery of a CodalMirror monthly report (Feature 133) or financial statement (Feature 143) post | `TickerGaugeEquilibrium` of the announced symbol, trigger `CodalAnnouncement`: once at once and once more after `CodalGaugeRecheckMinutes` (30; 0 turns the second fetch off) |
| Queued | — | every `RequestedRunPollSeconds` | Admin requests and announcement runs that are due, oldest first, up to 50 per poll |

- Market-hours times are Tehran local time, a fixed UTC+03:30. Public holidays are not known; a run on a closed day
  finds unchanged data. A slot that is a whole interval late is dropped and logged; slots are not caught up after a
  restart. A startup catch-up exists only for the Full run.
- The announcement refresh is requested in the API process by the two Telegram channel handlers and only queued there;
  the Worker calls the provider. It does not depend on the Noavaran Amin switches or outcome, and a failure to queue is
  logged and never changes the handling of the post. A symbol that is not in the v2 ticker catalog is skipped. One
  pending run per ticker and kind (immediate, deferred); at 500 pending announcement runs further requests are dropped
  with a warning.
- `cyclicalwaves."SyncRuns"."NotBeforeUtc"` is the due time of a queued run (null: due at once). A run that is not yet
  due is not claimed, does not block an Admin request and is not reported as the active run.
- An announcement run waits behind a running Full or market-hours run (about 19 and 12 minutes on production figures of
  2026-11-28). An Admin inline run that holds the lease at a slot makes that slot's run `SkippedAlreadyRunning`.
- Production setting (OQ-184, answered 2026-11-28): TahlilApp updates P/E, P/S and the equilibrium range once a day and
  when a new report is published, so the market-hours run stays off. Production uses the two Full runs and the
  announcement refresh; its second fetch is set with `CYCLICALWAVES_V2_CODAL_GAUGE_RECHECK_MINUTES` in the server `.env`.
  A market-hours slot would be about 1,156 provider calls (656 gauge and 500 equilibrium tickers on 2026-11-28).

## Comprehensive analysis in NoavaranAmin mode (Feature 159)

`FinancialData:CapabilitySources:comprehensive_analysis=CyclicalWaves` lets Chat answer comprehensive analysis from
`cyclicalwaves."Analyses"` while `FinancialData:ChatProvider` stays `NoavaranAmin` (ADR
[0001](../../adr/0001-per-capability-chat-data-source.md)). Nothing else reads the v2 schema in that mode. The answer
shows the latest analysis whose title contains the asked ticker as a whole word: title, publish date, a partial preview
and the fixed link `ComprehensiveAnalysis:TahlilAppAnalysesUrl`. **The provider field `summary` is the whole paid
analysis text, not a summary** (owner, 2026-11-10); only the preview (at most `TeaserPercent`=30% and
`TeaserMaxCharacters`=300, none under `TeaserMinimumSourceCharacters`=150) leaves the query
(default `https://tahlilapp.com/app/analysis`). Exception: for a user whose plan grants
`ComprehensiveAnalysis.FullText` (Max; `billing_plan_capabilities`) the query returns the whole text, bounded at 20,000
characters (change `max-plan-full-analysis-text`, 2026-11-11). It needs the `Analysis` sync to run (`CyclicalWavesV2:IngestionEnabled`
on the Worker).

Read-only production check before setting the map (run by the owner; no credentials in this file):

```sql
SELECT count(*) AS analyses, max("PublishedAt") AS newest_published, max("LastSyncedAt") AS last_synced
FROM cyclicalwaves."Analyses";

SELECT "Scope", "AnalysisMode", "Status", "StartedAt", "CompletedAt", "UpdatedItems", "FailedItems", "ErrorSummary"
FROM cyclicalwaves."SyncRuns"
WHERE "Scope" IN ('Analysis', 'Full')
ORDER BY "RequestedAt" DESC
LIMIT 5;
```

## Standalone valuation gauge cards (Feature 159 change `standalone-valuation-gauge-cards`)

V2 Chat answers «گیج P/E ‹نماد›», «گیج P/S ‹نماد›», «بازهٔ قیمت تعادلی ‹نماد›» (or «گیج قیمت تعادلی») and «گیج‌های
ارزش‌گذاری ‹نماد›» with typed cards built from the latest-state `GaugeAvailability`, `GaugeDetails` and
`EquilibriumRanges` rows. A «گیج» or «بازه» word is required (OQ-130): the bare «قیمت تعادلی ‹نماد›» keeps its current
route. The equilibrium needle uses the TSETMC trade tables through `PersistedMarketDataProvider`, shown with its own
source and time (change `equilibrium-intraday-price`): before `DailyPriceReadyTehran` (19:00 Tehran) today's latest
`IntradayTradeSnapshots` row however old (the intraday poll stops at the close); from then today's
`DailyInstrumentTrades` row when it exists, else still today's intraday row (the daily row is late on some Wednesdays);
with no intraday row today (Thursday, Friday, holiday, no trade) the latest daily row. The relative valuation inputs
(Feature 125) keep the shared 15-minute intraday freshness (`MarketViews:StaleAfterMinutes`). Cards make no LLM, vendor HTTP or cross-provider fallback call.
Each usable card (Available or Partial, including a range-only equilibrium card) costs one credit, so the three-card
answer costs three (change `valuation-gauge-per-card-credits`, OQ-138, OQ-139; it supersedes OQ-131). The answer
settles with `ValuationGaugesAnswered.<n>` at that fixed amount in every `UsageBasedCredits:Mode`
(`ValuationGaugeBillingPolicy`). Before any read, the hold is raised to the requested cards; a balance below it gets
the existing insufficient-credit answer and no card (OQ-140). Clarification, no-data, stale-only and
temporarily-unavailable answers are free. No table, migration or tariff seed was added.

**Activation** (API service; off by default):

| Setting | Default | Meaning |
|---|---|---|
| `ValuationGaugeCards:Enabled` (`VALUATION_GAUGE_CARDS_ENABLED`) | `false` | Master switch for the route and the symbol-menu shortcut. |
| `ValuationGaugeCards:MaxFetchAgeHours` (`VALUATION_GAUGE_CARDS_MAX_FETCH_AGE_HOURS`) | `48` | Older snapshots (or `FetchedAtUtc` more than 5 minutes in the future) are reported stale and not shown. 1–720. |
| `ValuationGaugeCards:ReadTimeoutMilliseconds` / `QuoteTimeoutMilliseconds` | `2000` / `1500` | Budgets for the snapshot read and the quote read; 100–30000. |
| `ValuationGaugeCards:DailyPriceReadyTehran` | `19:00` | Tehran time (`HH:mm`) from which the equilibrium price is today's daily trade row when it exists; before it, today's intraday trade. |
| `ValuationGaugeCards:PercentageDecimals` | `1` | Largest-remainder precision of the six day-share percentages; 0–3. |
| `FinancialData:CapabilitySources:valuation_gauges` (`FINANCIAL_DATA_SOURCE_VALUATION_GAUGES`) | empty | Needed only when `ChatProvider` is `NoavaranAmin`; may map only to `CyclicalWaves`. Base CyclicalWaves needs no entry. |

All values are validated at start-up. Steps: confirm the gauge and equilibrium syncs are recent (query below), set the
source map if the base provider is NoavaranAmin, set `VALUATION_GAUGE_CARDS_ENABLED=true`, restart the API, then check
`GET /api/ai/v1/capabilities/availability` returns `{"valuationGaugeCards":true}` for a signed-in user and ask
«گیج P/E شاراک» once.

**Rollback:** set `VALUATION_GAUGE_CARDS_ENABLED=false` (and clear the source entry if it was added) and restart the API.
New requests return to their previous routes and the availability endpoint hides the menu shortcut; cards already stored
in conversations stay readable because history maps the stored payload without consulting the flag.

**Health signals** (meter `FinancialCopilot.ValuationGauges`): `valuation_gauge.requests` (tags `match`, `usable`),
`valuation_gauge.cards` (tags `kind`, `state`, `reason`), `valuation_gauge.snapshot_age` (hours, tag `kind`) and
`valuation_gauge.latency` (ms, tag `match`). `match` is `matched`, `missing`, `ambiguous` or `timeout`. Watch for a rising share of `match=missing`/`ambiguous`
(identity drift),
`state=Stale` (sync lag beyond 48 h), `reason=quote_timed_out` and `reason=read_timed_out`.

`GaugeDetails.FetchedAtUtc` and `EquilibriumRanges.DetailsFetchedAtUtc` are the time of the last successful fetch of
that row, also when the vendor returned the same values (closed market days). The gauge cards (`MaxFetchAgeHours`) and
the Feature 125 comparison on v2 (`IndustryRelativeValuation:SourceFreshnessHours`) age the data by these columns, so
they measure sync lag, not the time since the values last changed (BUG-052).

```sql
SELECT "Scope", "Status", "StartedAt", "CompletedAt", "UpdatedItems", "FailedItems", "ErrorSummary"
FROM cyclicalwaves."SyncRuns"
WHERE "Scope" IN ('GaugeCatalog', 'GaugeDetails', 'EquilibriumCatalog', 'EquilibriumDetails', 'Full')
ORDER BY "RequestedAt" DESC
LIMIT 10;
```

**Source-coverage baseline** (local development database, 2026-11-22 sync; staging and production not measured):
654 P/E and 645 P/S gauge rows, of which 83 and 38 have no current value (card without needle) and none has a zero day
total; 4 rows have a non-increasing axis (text fallback); every row has an asymmetric average; active tickers have no
duplicate normalized ticker or ISIN and none lacks an ISIN; 9 equilibrium ranges have a negative lower bound. Evidence:
`specs/159-capability-source-map-comprehensive-analysis/changes/standalone-valuation-gauge-cards/evidence/`.

**Deviations from the plan:** a small read endpoint `GET /api/ai/v1/capabilities/availability` was added because the
web client had no existing availability boundary for the menu shortcut (unknown or failed → hidden); and
`DetectedIntent.ValuationGauges` was appended to the persisted intent enum (integer, added last) so history records the
route.

### Display rule and derived figures (change `valuation-gauge-cards-visual-refresh`)

- **Rounding is display-only.** The payload keeps the exact provider strings. Web cards, Telegram and history text use
  the same rule, rounding half away from zero from the decimal string with no precision limit:
  - P/E and P/S: exactly two decimals («۷٫۰۰»);
  - share price and equilibrium bounds: whole rials («۴٬۱۲۰»);
  - derived percentages: one decimal.
- **Source label:** users see the provider as «تحلیل‌اپ» with the fetch date only («تحلیل‌اپ · دریافت ۱۴۰۵/۰۹/۰۵»). The
  stored `source` code stays `CyclicalWavesV2`. The quote line keeps its own source and time.
- **Derived figures** are computed once in `ValuationGaugeProjector` and shared by web and Telegram. They go in the
  optional `insights` member, additive in payload version 1:
  - `distanceToAveragePercent` = (current − average) / average, only when the average is > 0;
  - `currentBand` (1–6), only for a needle inside the axis with a non-zero day total;
  - `daysBelowBandPercent` and `daysInBandPercent`, the sums of the displayed largest-remainder shares;
  - for the equilibrium range, only with a quote and a positive price: `lowerDistancePercent`,
    `midpointDistancePercent` and `upperDistancePercent`, each as (level − price) / price: how far today's price must
    move to reach the level, positive when the level is above the price (change `equilibrium-distance-from-price`).
    The card and the text show them without a sign, as «تا میانه ۳۳٫۲٪ رشد» / «… افت» / «هم‌سطح», with a note that they
    are distances and not a forecast;
  - `priceVsMidpointPercent` and `priceVsLowerPercent`, each as (price − x) / |x|, omitted when x = 0: the figures
    released before that change. They are still written for older clients and shown only for a stored card that has
    no distances.
- **Sector ranges:** each ratio sector carries its exact `from`/`to` edges (start, min, the midpoint of min and average,
  average, the midpoint of average and max, max, end).
- **Stored cards:** cards stored before this change lack `insights` and `from`/`to`. They still render, without those
  figures.
- **Forward P/E and P/S from the provider:** each gauge object carries `forward` (live contract of 2026-11-28). It is
  ingested and stored in `cyclicalwaves."GaugeDetails"` (`Forward*` columns, null when the provider has none) by change
  `148-…/changes/gauge-forward-field`.
- **Forward value on a ratio card, source order** (change `valuation-gauge-provider-forward`, OQ-136):
  1. the provider value, when the stored gauge has a positive one and `ValuationGaugeCards:ProviderForward:Enabled`
     (default true) is on. It comes with the gauge snapshot (same freshness, no extra read), needs a displayable card
     with a dial but no current value, and is named «P/E آینده‌نگر» / «P/S آینده‌نگر» without a qualifier; «جزئیات» says
     it is تحلیل‌اپ's calculation and neither a certain forecast nor advice;
  2. otherwise the Sapio estimate below, when its flag is on and its guards pass, always labelled «(برآورد ساپیو)»;
  3. otherwise no forward needle.
  The estimate is not computed for a card that has a provider value. The card's `forward.source` is `provider` or
  `sapio`; a stored `forward` without it is a Sapio estimate. Reasons for no provider value:
  `provider_forward_missing`, `provider_forward_not_positive`, `provider_forward_out_of_range`,
  `card_not_displayable`, `axis_not_renderable`.

### Sapio forward P/E and P/S estimate (change `valuation-gauge-forward-ratio`)

Behind a flag, Sapio adds **its own** estimate to a P/E or P/S card that has no provider forward value. It is
always labelled «(برآورد ساپیو)», never attributed to تحلیل‌اپ, and never changes the provider `status`, the position,
the insights, the card state or the credits.

- **Method `sapio-1`** (`ForwardRatioEstimator`, pure; reference: `estimate()` in
  `tools/research/forward-pe-reverse-engineering/export_estimator_fixtures.py`):
  - forecast sales = Σ last 12 monthly sales × (Σ last 4 months ÷ Σ the same 4 months one year earlier);
  - forward margin = median of the last 8 quarterly net margins (net profit ÷ revenue, discrete quarters);
  - forward P/S = current P/S × Σ revenue of the last 4 quarters ÷ forecast sales;
  - forward P/E = current P/E × Σ net profit of the last 4 quarters ÷ (forecast sales × forward margin).
  Each ratio uses its own gauge `close`.
- **Inputs** (`ForwardRatioInputsReader`, read-only, bounded to 16 months and 8 statements):
  - monthly sales: the accepted monthly activity reports of the company whose `SymbolIsin` is the vendor ticker's ISIN
    (exactly one match), the month itself (output type 0, or none for service reports), summed over line items and
    converted from million rials to rials;
  - quarters: the stored income statements' discrete-quarter values; revenue is the first of keys 44, 46, 124, 412, 599
    with a value, net profit is key 66 (`CyclicalWavesV2IncomeStatementLines`). A missing discrete quarter is never
    filled from year-to-date figures.
- **No estimate** (the card stays exactly as before) when: the flag is off; the card is not displayable, has no current
  value or no dial; the company is not matched uniquely; the industry is excluded (default external id `31`, OQ-148);
  fewer than 16 consecutive months (ends at most 35 days apart) or the latest month is more than
  `MaxMonthlyReportAgeDays` old; the comparison months sum to zero or less; the growth factor is outside
  `MinGrowthFactor`–`MaxGrowthFactor`; fewer than 8 consecutive usable quarters (ends at most 100 days apart) ending at
  the latest statement; the input read fails or exceeds its budget. The forward P/E alone is hidden when the margin is
  ≤ 0, or the current P/E or the 4-quarter net profit is ≤ 0; the forward P/S can still be shown.
- **Payload:** the optional `forward` member of a ratio card (additive in payload version 1, omitted when absent):
  `value` (two decimals), `angle` and `side` on the card's own axis with the same pin rule as the current needle,
  `methodVersion`, `salesGrowthFactor`, `marginPercent` (P/E card only), `latestSalesMonthEnd`, `latestQuarterEnd`.
- **Display:** a finely dashed needle with a ring and a chip «P/E آینده‌نگر (برآورد ساپیو) ‹مقدار›» on the web card, the
  method sentence in «جزئیات», and the line «P/E آینده‌نگر (برآورد ساپیو): ‹مقدار›» in the text answer and Telegram.
- **Configuration** `ValuationGaugeCards:ForwardEstimate`: `Enabled` (false), `MinGrowthFactor` (0.5),
  `MaxGrowthFactor` (4), `MaxMonthlyReportAgeDays` (75), `ExcludedIndustryExternalIds` (`["31"]`, default in code: the
  configuration binder appends to it), `ReadTimeoutMilliseconds` (1000). Invalid values fail startup. The input reader
  is registered only when `Enabled` is true; turning it off restores today's cards and is the rollback path.
- **Telemetry:** `valuation_gauge.forward_estimates` (kind, shown, reason, source), `valuation_gauge.forward_read_latency`, and
  one warning log per request when the input read times out or fails.
