# TSETMC Web Service Data Source

## Overview

The TSETMC (Tehran Securities Exchange Technology Management Company) ASMX web service provides real-time and historical Iranian stock market data. This document records the service endpoint, authentication, and field mappings used by Phase 2 of spec `054`.

- **WSDL**: `http://service.tsetmc.com/WebService/TsePublicV2.asmx?WSDL`
- **Endpoint**: `http://service.tsetmc.com/WebService/TsePublicV2.asmx`
- **Protocol**: SOAP 1.1 over HTTP
- **Authentication**: Username / Password passed as XML body parameters in every request

## Operations Used

### `Instrument(UserName, Password, Flow)`
Fetches instrument (symbol) dimension for a given market flow.

| Flow | Description |
|------|-------------|
| 5 | بورس (TSE equity) |
| 6 | فرابورس (OTC/IFX equity) |
| 7 | حق‌تقدم (Rights) |

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
| XVarIdxJ | decimal | Change percent |

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
    "InstrumentFlows": [5, 6, 7],
    "DailyTradeFromDate": "20200101",
    "DailyTradeToDate": null,
    "DailyIndexFromDate": "20200101",
    "DailyIndexToDate": null,
    "Enabled": false
  }
}
```

Set `Enabled: true` and provide credentials to activate the Phase 2 direct feed. Until then, the system falls back to StockMarketDB (bridge source).

## DataAdmin Endpoints

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/api/v1/admin/tsetmc/status` | Returns IsOperational + notes |
| POST | `/api/v1/admin/tsetmc/{dataset}/sync` | Triggers sync for a dataset |

Valid `dataset` values: `instruments`, `intradaytrades`, `dailytrades`, `dailyindices`, `intradayindices`.

## Migration Phases (spec 054)

| Phase | Status | Description |
|-------|--------|-------------|
| 1 — Bridge Stabilization | ✅ Complete | StockMarketDb = MigrationBridge, provenance columns, ITsetmcDirectFeedSyncService stub |
| 2 — Direct TSETMC Provider | ✅ Complete | TsetmcWebServiceClient SOAP adapter, full normalizers, DataAdmin endpoints |
| 3 — Parallel Validation | ⏳ Deferred | Shadow-mode comparison, mismatch reporting |
| 4 — Cutover | ⏳ Deferred | Switch MarketQuoteSourcePriority to TsetmcWebService, disable StockMarketDB polling |
