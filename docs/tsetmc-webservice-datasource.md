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
| CValMne | string | 5-letter symbol |
| LVal18AFC | string | 18-char Persian full symbol |
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
    "InstrumentFlows": [5, 6, 7, 1, 2, 4],
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

## DataAdmin Endpoints

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/v1/admin/tsetmc/status` | Returns IsOperational + notes |
| POST | `/api/v1/admin/tsetmc/{dataset}/sync` | Triggers sync for a dataset |
| POST | `/api/v1/admin/tsetmc/validate` | Phase 3: runs bridge vs direct comparison, persists mismatches |
| GET | `/api/v1/admin/tsetmc/mismatches?recentDays=7` | Phase 3: per-field mismatch summary |
| GET | `/api/v1/admin/tsetmc/source-priority` | Phase 4: shows active quote source and cutover state |

Valid `dataset` values: `instruments`, `intradaytrades`, `dailytrades`, `dailyindices`, `intradayindices`.

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
