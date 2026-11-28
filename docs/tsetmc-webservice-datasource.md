# TSETMC Web Service Data Source

## Overview

The TSETMC (Tehran Securities Exchange Technology Management Company) ASMX web service provides real-time and historical Iranian stock market data. This document records the service endpoint, authentication, and field mappings used by Phase 2 of spec `054`.

- **WSDL**: `http://service.tsetmc.com/WebService/TsePublicV2.asmx?WSDL`
- **Endpoint**: `http://service.tsetmc.com/WebService/TsePublicV2.asmx`
- **Protocol**: SOAP 1.1 over HTTP
- **Authentication**: Username / Password passed as XML body parameters in every request

## Operations Used

### `Instrument(UserName, Password, Flow)`
Fetches the instrument (symbol) dimension for a given market flow. The API and
scheduled worker sync flows 5, 6, 7, 1, 2, and 4 by default. The live Instrument
responses verified on 2026-11-24 contained active equities in flows 1, 2, and 4.

| Flow | Description |
|------|-------------|
| 1 | بورس (TSE equity) |
| 2 | فرابورس (OTC/IFX equity) |
| 4 | بازار پایه فرابورس (IFX base market) |
| 5, 6, 7 | Previously configured instrument flows retained for coverage |

**Response DataSet columns:**

| Column | Type | Description |
|--------|------|-------------|
| InsCode | long | TSETMC instrument code (primary key) |
| InstrumentID | string | 12-char ISIN-like code |
| CValMne | string | Latin mnemonic (for example `MKBT1`); not stored as the symbol |
| LVal18AFC | string | Persian trading symbol (for example «اخابر»); stored as `TradingInstruments.Symbol` (BUG-055) |
| LSoc30 | string | 30-char Persian company name |
| YMarNSC | string | Market code |
| CGdSVal | string | Instrument kind (single char) |
| CGrValCot | string | Instrument group code |
| Valid | string | "1" if valid/active |
| DInMar | int | Listing date (yyyyMMdd) |
| ZTitad | decimal | Total shares outstanding |

The sync groups observations by `InsCode`. When several flows agree on
`InstrumentID` and `Valid`, the sync applies one update and preserves the
existing instrument ID and price references. Missing validity or conflicting
identities are logged and excluded from the update.

An unsuccessful or empty flow is reported in `instrumentCoverage.failedFlows`.
Successful flows are still applied. The response marks incomplete coverage,
and the successful sync watermark advances only after a complete run. An
all-flow failure returns HTTP 503. The SOAP client retries transport failures
within its configured retry budget. Operators can retry the Instruments sync
from the existing admin API, and scheduled runs retry on the next market day.

After installing the change, record the current mappings and valuation coverage,
then run the Instruments sync once. Check the API coverage response or worker
logs, verify active ISINs for eligible companies, and confirm that trade rows
still reference their original instruments. Recheck publication by industry
after the valuation worker has run. Report missing prices or valuation inputs
separately from missing instrument identities.

---

### `TradeLastDay(UserName, Password, Flow)` — Intraday
Returns today's intraday trade snapshot for all instruments in the given market flow. Called repeatedly during the trading session.

**Flow values**: 0 (common/both), 1 (TSE), 2 (OTC), 3 (Futures), 4 (Paye Farabourse), 5 (Paye Farabourse unpublished)

**Response DataSet columns:**

| Column | Type | Description |
|--------|------|-------------|
| InsCode | long | TSETMC instrument code |
| DEven | int | Trade date (yyyyMMdd) |
| HEven | int | Trade time (HHmmss) |
| ZTotTran | decimal | Total transactions |
| QTotTran5J | decimal | Volume (shares) |
| QTotCap | decimal | Total capital (Rial) |
| PClosing | decimal | Closing price |
| PDrCotVal | decimal | Last traded price |
| PriceChange | decimal | Price change vs yesterday |
| PriceMin | decimal | Day low |
| PriceMax | decimal | Day high |
| PriceFirst | decimal | Opening price |
| PriceYesterday | decimal | Previous close |

---

### `TradeOneDay(UserName, Password, SelDate, Flow)` — Daily Historical
Returns all completed daily trade records for a specific date.

**SelDate**: `int` in yyyyMMdd format (e.g. `20260609`).  
**Flow values**: same as TradeLastDay (0–7, adds 6=Energy Exchange, 7=Commodity Exchange).

**Response DataSet columns**: same as TradeLastDay above.

---

### `IndexB2(UserName, Password, DEven)` — Daily Index History
Returns all index values for a specific date across all tracked indices.

**DEven**: `int` in yyyyMMdd format.

**Response DataSet columns:**

| Column | Type | Description |
|--------|------|-------------|
| InsCode | long | Index instrument code |
| Deven | int | Date (yyyyMMdd) |
| Heven | int | Time (HHmmss) |
| xNivInuClMresIbs | decimal | Closing index value |
| xNivInuPhMresIbs | decimal | Day high value |
| xNivInuPbMresIbs | decimal | Day low value |
| XVarDrInuClV | decimal | Change vs previous day |

---

### `IndexB1LastDayLastData(UserName, Password, Flow)` — Intraday Index
Returns today's intraday index snapshots.

**Flow values**: 0 (normal), 1 (Bourse), 2 (Farabourse), 3 (ATI), 4 (Paye Farabourse)

**Response DataSet columns:**

| Column | Type | Description |
|--------|------|-------------|
| insCode | long | Index instrument code (lowercase!) |
| DEven | int | Date (yyyyMMdd) |
| HEven | int | Time (HHmmss) |
| XDrNivJIdx004 | decimal | Current index value |
| XVarIdxJRfV | decimal | Close-to-close change percent |

---

### `Option(UserName, Password)` — Option contract terms and open interest (Feature 167)

Returns every option contract the exchange lists, about 1,900 rows and 0.9 MB.

| Column | Type | Description | Stored as |
|--------|------|-------------|-----------|
| InsCode | long | Instrument code of the contract | link to `TradingInstruments` |
| InstrumentID | string | ISIN; its prefix gives the side | `OptionContracts.Side` |
| BuyOP, SellOP | long | Open positions; always equal | `OpenInterest` (from `BuyOP`) |
| YesterdayOP | long | Open positions of the previous day | `PreviousOpenInterest` |
| ContractSize | int | Shares per contract | `ContractSize` |
| StrikePrice | decimal | Strike price | `StrikePrice` |
| UAInsCode | long | Instrument code of the underlying | `UnderlyingInstrumentId` |
| BeginDate, EndDate | int | `yyyyMMdd` | `BeginDate`, `ExpiryDate` |
| AFactor, BFactor, CFactor, Delta, MarketSameRiskOP | — | Margin factors and risk figures | not stored |

Side from the ISIN prefix: `IRO9` and `IROA` are calls (symbols start with ض), `IROF` and `IROB`
are puts (symbols start with ط). Any other row (embedded puts and similar, ISIN `IRS…`, symbols
starting with ه or ظ) is not a listed call or put and is ignored.

### `BestLimitsAllIns(UserName, Password, Flow)` — Order book (Feature 167)

Returns up to five order-book levels per instrument. Flow 3 is about 8,900 rows and 2.6 MB and
answers in under a second. The response is read as a stream and its raw payload is not stored.

| Column | Type | Description |
|--------|------|-------------|
| number | byte | Level, 1 (best) to 5 |
| InsCode | long | Instrument code |
| PMeDem, QTitMeDem, ZOrdMeDem | int, long, int | Bid price, volume, order count |
| PMeOf, QTitMeOf, ZOrdMeOf | int, long, int | Ask price, volume, order count |
| HEven | int | Time of the last change (`HHmmss`, market time) |

A zero price or volume means there is no order on that side; it is stored as null. Only
instruments that are known option contracts are kept. Each sync replaces the whole stored book:
a level the provider no longer reports is deleted.

### `Instrument` with a derivative flow (flow 3)

Flow 3 returns every derivative contract ever listed: about 65,000 rows and 66 MB, of which
about 3,100 are valid. For the flows in `DerivativeInstrumentFlows` (default `[3]`) the client
reads the response as a stream, keeps only valid listed option contracts (by ISIN prefix), uses
the Persian trading symbol (`LVal18AFC`) as the symbol and the contract description (`LVal30`)
as the name, and the sync does not store the raw payload.

### Live verification, 2026-11-28 (one call per operation)

| Check | Result |
|---|---|
| `Option` | 1,867 contracts, 89 underlyings, no expired contract, expiries 2026-11-28 to 2028-05-10. `BuyOP` equals `SellOP` on every row. One row has a non-positive strike. Contract size 1,000 on 1,762 rows. |
| Side | 897 calls and 897 puts by ISIN prefix; the symbol prefix agrees on every one. 73 other rows. The listed calls and puts are written on 31 underlyings (20 shares with `IRO…` ISINs, 11 tradable funds with `IRT…`); the other 58 underlyings belong to the 73 unlisted kinds. |
| `Instrument(Flow = 3)` | 64,693 rows, 3,130 valid, 2,044 valid listed options. Contains all `Option` contracts; 1,768 of the 1,794 listed ones are valid. Underlyings are not in flow 3. |
| `BestLimitsAllIns(Flow = 3)` | 8,877 rows for 1,899 instruments. 1,557 option contracts have at least one level, 923 have a level-1 ask. 342 instruments are not option contracts. No level-1 ask below its bid. |
| `TradeLastDay(Flow = 3)` | 644 rows; 633 are option contracts. |
| Whole pipeline on a test database with these responses | Instruments 5.1 s; contracts 1.2 s first run, 0.6 s unchanged; order book 1.6 s first run, 0.7 s unchanged. 1,768 contracts and 4,919 levels stored. |

### Provider host

`service.tsetmc.com` resolves to two addresses. On 2026-11-28 `94.182.113.112` answered HTTP 403
with an HTML page to every request, including the WSDL, and `46.102.143.212` answered 200, with
and without a VPN. The client treats the 403 page as a provider failure and retries within
`RetryCount`; it does not pick an address, by the owner's decision of 2026-11-28 (OQ-171).
Whether production sees the failing address is not known.

## Configuration (`appsettings.json`)

```json
{
  "TsetmcWebService": {
    "ProviderName": "TsetmcWebService",
    "ServiceUrl": "http://service.tsetmc.com/WebService/TsePublicV2.asmx",
    "UserName": "<set via secret>",
    "Password": "<set via secret>",
    "TimeoutSeconds": 60,
    "RetryCount": 3,
    "IntradayTradeFlows": [0, 1, 2, 3, 4, 5],
    "InstrumentFlows": [5, 6, 7, 1, 2, 4, 3],
    "DerivativeInstrumentFlows": [3],
    "OptionOrderBookFlow": 3,
    "DailyTradeFromDate": "20200101",
    "DailyTradeToDate": null,
    "DailyIndexFromDate": "20200101",
    "DailyIndexToDate": null,
    "Enabled": false
  }
}
```

Set `Enabled: true` and provide credentials to activate the Phase 2 direct feed. Until then, the system falls back to StockMarketDB (bridge source).

## Worker Polling Schedule

The `TsetmcPollingWorker` hosted service drives all TSETMC data collection automatically on the Iranian equity market calendar. All times are **IRST (UTC+03:30)** — Iran does not observe DST.

### Intraday data (trades + indices)

| Days | Window | Cadence |
|------|--------|---------|
| Saturday – Wednesday | 09:00 – 12:30 | Every 60 seconds (configurable) |

Calls `SynchronizeIntradayTradesAsync` and `SynchronizeIntradayIndicesAsync` (TSETMC methods: `TradeLastDay`, `IndexB1LastDayLastData`).

### End-of-day data (instruments + daily trades + daily indices)

| Days | Fire-once after |
|------|----------------|
| Saturday – Tuesday | 17:00 IRST |
| Wednesday | 20:00 IRST |

Wednesday has a later cut-off because the exchange session ends later that day. Calls `SynchronizeInstrumentsAsync`, `SynchronizeDailyTradesAsync`, and `SynchronizeDailyIndicesAsync` (TSETMC methods: `Instrument`, `TradeOneDay`, `IndexB2`). Thursday and Friday are market holidays; no calls are made.

### Worker configuration (`TsetmcPolling` section)

```json
{
  "TsetmcPolling": {
    "Enabled": true,
    "IntradayTickSeconds": 30,
    "IntradayIntervalSeconds": 60
  }
}
```

| Key | Default | Description |
|-----|---------|-------------|
| `Enabled` | `false` | Master switch for the worker |
| `IntradayTickSeconds` | `30` | How often the loop wakes to check the schedule window |
| `IntradayIntervalSeconds` | `60` | Minimum gap between consecutive intraday sync calls |

### Option market data (Feature 167)

| Dataset | Window | Cadence | Switch (default off) |
|------|--------|---------|------|
| Option contracts and open interest (`Option`) | intraday window, and once after the end-of-day instruments sync | `OptionContractsIntervalSeconds` (300) | `OptionContractsEnabled` |
| Option order book (`BestLimitsAllIns`, flow 3) | intraday window | `OptionOrderBookIntervalSeconds` (60) | `OptionOrderBookEnabled` |

Both keys are in the `TsetmcPolling` section. Intervals below 10 seconds fail start-up
validation. Each dataset runs in its own scope after trades and indices; a failure is logged and
does not delay the others. The contract sync does not create instruments: a contract whose own
instrument or underlying is not in `TradingInstruments` yet is counted as unresolved and stored
by a later run, after the instruments sync has seen it.

Sync-state rows `Tsetmc_OptionContracts` and `Tsetmc_OptionOrderBook` are stamped on every run.
The watermark advances only after a successful run, and the option chain reports the order-book
watermark as the observation time of the book.

Rollout: deploy with both switches off; run the instruments sync (adds flow 3); run the stub
backfill; run `optioncontracts` once and check the counts; turn on `OptionContractsEnabled`, then
`OptionOrderBookEnabled`, and watch one session. Rollback: turn both switches off; the tables can
stay.

## DataAdmin Endpoints

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/v1/admin/tsetmc/status` | Returns IsOperational + notes |
| POST | `/api/v1/admin/tsetmc/{dataset}/sync` | Triggers sync for a dataset |
| POST | `/api/v1/admin/tsetmc/validate` | Phase 3: runs bridge vs direct comparison, persists mismatches |
| GET | `/api/v1/admin/tsetmc/mismatches?recentDays=7` | Phase 3: per-field mismatch summary |
| GET | `/api/v1/admin/tsetmc/source-priority` | Phase 4: shows active quote source and cutover state |

Valid `dataset` values: `instruments`, `intradaytrades`, `dailytrades`, `dailyindices`, `intradayindices`.

Feature 167 adds, with their own response (fetched, persisted, unchanged, rejected, unresolved,
removed, ignored, duration; HTTP 503 when the provider fails, stored data untouched):

| Method | Route | Description |
|--------|-------|-------------|
| POST | `/api/v1/admin/tsetmc/optioncontracts/sync` | Option contract terms and open interest |
| POST | `/api/v1/admin/tsetmc/optionorderbook/sync` | Option order book |
| POST | `/api/v1/admin/tsetmc/instrument-stubs/backfill` | Renames instruments still stored under their numeric code, from stored daily trade payloads; safe to repeat |

## Option Market Read API (Feature 167)

Authenticated users (`MarketSummaryRead` policy, per-actor rate limit).

| Method | Route | Query | Result |
|--------|-------|-------|--------|
| GET | `/api/v1/market/options/chain` | `underlying` (symbol), `expiry` (optional) | Expiry dates, underlying price, and per strike the call and put with last and closing price, best bid and ask with volumes, open interest, contract size; observation times of the order book and open interest. 404 when the symbol has no unexpired listed option or the expiry is not listed. |
| GET | `/api/v1/market/options/call-exercise-ranking` | `underlying`, `expiry` (optional), `quantity` (contracts, default 1) | Calls ordered by effective purchase cost (strike + best ask), with premium over the underlying price; calls that cannot be ranked are listed with the reason. 409 when no price is stored for the underlying. |

## Option Market Tables (Feature 167)

`FinancialIngestionDbContext`, migration `Feature167OptionMarket`.

| Table | Key | Content |
|---|---|---|
| `OptionContracts` | `Id`; unique `TradingInstrumentId` | Side, underlying instrument, strike, expiry, begin date, contract size, open interest, previous open interest, observation times. Check constraints on side and terms. |
| `OptionOrderBookLevels` | (`TradingInstrumentId`, `Level`) | Latest bid and ask price, volume and order count per level; provider time; time of last change. No history. |

Market pulse leaves known option contracts out of its instrument set.

## Migration Phases (spec 054)

| Phase | Status | Description |
|-------|--------|-------------|
| 1 — Bridge Stabilization | ✅ Complete | StockMarketDb = MigrationBridge, provenance columns, ITsetmcDirectFeedSyncService stub |
| 2 — Direct TSETMC Provider | ✅ Complete | TsetmcWebServiceClient SOAP adapter, full normalizers, DataAdmin endpoints |
| 2b — Scheduled Worker | ✅ Complete | TsetmcPollingWorker with IRST market-hours schedule |
| 3 — Parallel Validation | ✅ Complete | MarketQuoteMismatches table, TsetmcValidationService, mismatch DataAdmin endpoints |
| 4 — Cutover | ✅ Complete | PersistedMarketDataProvider uses IMarketQuoteSourcePriority; set `MarketQuoteSourcePriority:PrimarySourceName` = `TsetmcWebService` to cut over, `StockMarketDb` to roll back |

### Phase 4 Cutover Runbook

1. Verify Phase 3 mismatch rate is acceptable via `GET /api/v1/admin/tsetmc/mismatches`.
2. Confirm `POST /api/v1/admin/tsetmc/status` shows `IsOperational=true`.
3. Set `MarketQuoteSourcePriority:PrimarySourceName` = `"TsetmcWebService"` in config (already set in `appsettings.json`).
4. Optionally set `StockMarketDbPolling:Enabled` = `false` to stop the bridge polling.
5. To roll back: set `PrimarySourceName` = `"StockMarketDb"` and re-enable polling. No code change required.
