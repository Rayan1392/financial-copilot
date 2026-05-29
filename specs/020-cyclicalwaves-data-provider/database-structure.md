# CyclicalWaves Data Provider — Database Structure

## Overview

The CyclicalWaves ingestion pipeline writes into **two separate EF Core DbContexts**:

| DbContext | Connection | Purpose |
|---|---|---|
| `FinancialProviderDbContext` | `FinancialProvider` connection string | Raw JSON payload storage before normalization |
| `FinancialIngestionDbContext` | `FinancialIngestion` connection string | Normalized domain tables consumed by the scanner |

Both use PostgreSQL with UUID primary keys.

---

## Database 1 — FinancialProviderDbContext

### Table: `ProviderRawPayloads`

Stores every raw JSON response verbatim before normalization. Deduplication is by `(ProviderName, Checksum)` so an identical payload fetched again is a no-op.

| Column | PG Type | Nullable | Constraint |
|---|---|---|---|
| `Id` | `uuid` | NOT NULL | PK |
| `ProviderName` | `varchar(128)` | NOT NULL | |
| `Dataset` | `varchar(64)` | NOT NULL | enum string: `Symbols`, `FinancialStatements`, `MonthlyProductionSales` |
| `Endpoint` | `varchar(512)` | NOT NULL | e.g. `/api/custom-filtering/ticker/%D8%B4%D9%BE%D9%86%D8%A7` |
| `ExternalReference` | `varchar(256)` | NOT NULL | Persian ticker string |
| `Payload` | `text` | NOT NULL | full JSON body |
| `Checksum` | `varchar(64)` | NOT NULL | SHA-256 hex of `Payload` |
| `ReceivedAt` | `timestamptz` | NOT NULL | UTC timestamp when HTTP response was received |

**Indexes:**
- `IX_ProviderRawPayloads_ProviderName_Checksum` — UNIQUE `(ProviderName, Checksum)`

**Entity / configuration:**
- [ProviderRawPayloadRow](src/backend/FinancialCopilot.Infrastructure/Financial/Providers/Persistence/ProviderRawPayloadPersistence.cs)
- [ProviderRawPayloadRowConfiguration](src/backend/FinancialCopilot.Infrastructure/Financial/Providers/Persistence/ProviderRawPayloadPersistence.cs)

**Migration:** `20260527045356_InitialFinancialProviderPayloads`

---

## Database 2 — FinancialIngestionDbContext

### Table: `Companies`

One row per company per provider. For CyclicalWaves, `ExternalCompanyId` is the Persian ticker (e.g. `شپنا`); `Name` is kept in sync with the same Persian ticker value.

| Column | PG Type | Nullable | Notes |
|---|---|---|---|
| `Id` | `uuid` | NOT NULL PK | |
| `ProviderName` | `text` | NOT NULL | `"CyclicalWaves"` |
| `ExternalCompanyId` | `text` | NOT NULL | Persian ticker (e.g. `شپنا`) — set by `CyclicalWavesSymbolNormalizer` |
| `Name` | `text` | NOT NULL | Display name; initially Persian ticker, updated to Persian ticker again by `CyclicalWavesFinancialStatementNormalizer` |
| `LastSynchronizedAt` | `timestamptz` | NOT NULL | |

**Indexes:**
- `IX_Companies_ProviderName_ExternalCompanyId` — UNIQUE `(ProviderName, ExternalCompanyId)`

---

### Table: `Symbols`

One row per tradeable symbol per provider. After the symbol sync, `SymbolCode` holds the Persian ticker. After the financial-statement sync it is overwritten with `enticker` (the ISIN-style code, e.g. `IRO7SHLP0001`).

| Column | PG Type | Nullable | Notes |
|---|---|---|---|
| `Id` | `uuid` | NOT NULL PK | |
| `CompanyId` | `uuid` | NOT NULL | FK → `Companies.Id` (logical; no DB constraint enforced) |
| `ProviderName` | `text` | NOT NULL | `"CyclicalWaves"` |
| `ExternalSymbolId` | `text` | NOT NULL | Persian ticker — matches `Companies.ExternalCompanyId` |
| `SymbolCode` | `text` | NOT NULL | Initially Persian ticker; replaced with `enticker` (e.g. `IRO7SHLP0001`) after financial-statement normalization |
| `LastSynchronizedAt` | `timestamptz` | NOT NULL | |

**Indexes:**
- `IX_Symbols_ProviderName_ExternalSymbolId` — UNIQUE `(ProviderName, ExternalSymbolId)`
- `IX_Symbols_SymbolCode` — non-unique, for scanner lookups by code

---

### Table: `FinancialStatements`

Three rows per ticker per sync: Q-0 (last quarter), Q-1 (penultimate quarter), Q-4 (last-year same quarter). The statement key is `ExternalStatementId` = `{_id}:Q0`, `{_id}:Q1`, or `{_id}:Q4`.

| Column | PG Type | Nullable | Notes |
|---|---|---|---|
| `Id` | `uuid` | NOT NULL PK | |
| `ProviderName` | `text` | NOT NULL | `"CyclicalWaves"` |
| `ExternalCompanyId` | `text` | NOT NULL | CyclicalWaves `_id` field |
| `ExternalStatementId` | `text` | NOT NULL | `{_id}:Q0`, `{_id}:Q1`, `{_id}:Q4` |
| `PeriodType` | `text` | NOT NULL | Always `"IncomeStatement"` |
| `PeriodStart` | `date` | NOT NULL | Estimated from Iranian fiscal calendar (see period resolution below) |
| `PeriodEnd` | `date` | NOT NULL | Estimated fiscal quarter end date |
| `SourcePayloadChecksum` | `text` | NOT NULL | Matches `ProviderRawPayloads.Checksum` that produced this row |
| `LastSynchronizedAt` | `timestamptz` | NOT NULL | |
| `WarningsJson` | `text` | NOT NULL | JSON array; always contains `StaleData` warning for CyclicalWaves |

**Indexes:**
- `IX_FinancialStatements_ProviderName_ExternalStatementId` — UNIQUE `(ProviderName, ExternalStatementId)`

**Migration added `WarningsJson`:** `20260528102634_AddWarningsJsonToNormalizedRows`

---

### Table: `FinancialStatementLineItems`

Metric values belonging to a `FinancialStatements` row. Unique per `(FinancialStatementId, MetricCode)`.

| Column | PG Type | Nullable | Notes |
|---|---|---|---|
| `Id` | `uuid` | NOT NULL PK | |
| `FinancialStatementId` | `uuid` | NOT NULL | FK → `FinancialStatements.Id` |
| `MetricCode` | `text` | NOT NULL | See metric codes table below |
| `Value` | `numeric` | NULL | Null when CyclicalWaves field is absent |

**Metric codes written per statement row:**

| MetricCode | Q-0 | Q-1 | Q-4 | Source field |
|---|---|---|---|---|
| `REVENUE` | ✓ | ✓ | ✓ | `last/penultimate/last_year_same_quarter_sale` |
| `NET_PROFIT` | ✓ | ✓ | ✓ | `*_net_profit` |
| `GROSS_PROFIT` | ✓ | ✓ | ✓ | `*_gross_profit` |
| `OPERATING_PROFIT` | ✓ | ✓ | ✓ | `*_operating_profit` |
| `NET_PROFIT_MARGIN` | ✓ | ✓ | ✓ | `*_net_profit_margin` |
| `GROSS_PROFIT_MARGIN` | ✓ | ✓ | ✓ | `*_gross_profit_margin` |
| `OPERATING_PROFIT_MARGIN` | ✓ | ✓ | ✓ | `*_operating_profit_margin` |
| `PE_RATIO` | ✓ | — | — | `pe` (Q-0 only) |
| `PS_RATIO` | ✓ | — | — | `ps` (Q-0 only) |

**Indexes:**
- `IX_FinancialStatementLineItems_FinancialStatementId_MetricCode` — UNIQUE `(FinancialStatementId, MetricCode)`

---

### Table: `MonthlyReports`

Three rows per ticker per sync: M-0 (last month), M-1 (penultimate month), M-12 (last-year same month). The report key is `ExternalReportId` = `{_id}:M0`, `{_id}:M1`, or `{_id}:M12`.

| Column | PG Type | Nullable | Notes |
|---|---|---|---|
| `Id` | `uuid` | NOT NULL PK | |
| `ProviderName` | `text` | NOT NULL | `"CyclicalWaves"` |
| `ExternalCompanyId` | `text` | NOT NULL | CyclicalWaves `_id` field |
| `ExternalReportId` | `text` | NOT NULL | `{_id}:M0`, `{_id}:M1`, `{_id}:M12` |
| `PeriodStart` | `date` | NOT NULL | First day of the Gregorian calendar month |
| `PeriodEnd` | `date` | NOT NULL | Last day of the Gregorian calendar month |
| `SourcePayloadChecksum` | `text` | NOT NULL | |
| `LastSynchronizedAt` | `timestamptz` | NOT NULL | |
| `WarningsJson` | `text` | NOT NULL | JSON array; always contains `StaleData` warning |

**Indexes:**
- `IX_MonthlyReports_ProviderName_ExternalReportId` — UNIQUE `(ProviderName, ExternalReportId)`

**Migration added `WarningsJson`:** `20260528102634_AddWarningsJsonToNormalizedRows`

---

### Table: `MonthlyReportLineItems`

| Column | PG Type | Nullable | Notes |
|---|---|---|---|
| `Id` | `uuid` | NOT NULL PK | |
| `MonthlyReportId` | `uuid` | NOT NULL | FK → `MonthlyReports.Id` |
| `ProductCode` | `text` | NOT NULL | Always `"REVENUE"` for CyclicalWaves |
| `ProductionQuantity` | `numeric` | NULL | Not populated by CyclicalWaves (null) |
| `SalesQuantity` | `numeric` | NULL | Not populated by CyclicalWaves (null) |
| `SalesAmount` | `numeric` | NULL | `last/penultimate/last_year_same_month_sale` |

**Indexes:**
- `IX_MonthlyReportLineItems_MonthlyReportId_ProductCode` — UNIQUE `(MonthlyReportId, ProductCode)`

---

### Table: `ProviderSyncRuns`

Tracks each `DataSyncRequest` execution. Idempotency key prevents re-processing the same logical sync window.

| Column | PG Type | Nullable | Notes |
|---|---|---|---|
| `Id` | `uuid` | NOT NULL PK | |
| `IdempotencyKey` | `text` | NOT NULL | e.g. `cw-symbols:{guid}`, `cw-fs:{ticker}:{yyyyMMddHH}` |
| `Dataset` | `text` | NOT NULL | `Symbols`, `FinancialStatements`, `MonthlyProductionSales` |
| `ExternalReference` | `text` | NULL | Persian ticker (null for symbol list sync) |
| `Status` | `text` | NOT NULL | `Pending`, `Running`, `Completed`, `Failed` |
| `RequestedAt` | `timestamptz` | NOT NULL | |
| `StartedAt` | `timestamptz` | NULL | |
| `CompletedAt` | `timestamptz` | NULL | |
| `ProcessedRecords` | `integer` | NOT NULL | Count of rows written |
| `ErrorCount` | `integer` | NOT NULL | |
| `ErrorMessage` | `varchar(1000)` | NULL | |
| `SourcePayloadChecksum` | `text` | NULL | Set after payload is fetched |

**Indexes:**
- `IX_ProviderSyncRuns_IdempotencyKey` — UNIQUE `IdempotencyKey`

---

### Table: `MetricRecalculationRequests`

Outbox for `DerivedMetricRecalculationRequested` events. Unique per `(SourceDataset, SourcePayloadChecksum)` to avoid duplicate triggers.

| Column | PG Type | Nullable | Notes |
|---|---|---|---|
| `Id` | `uuid` | NOT NULL PK | |
| `SourceDataset` | `text` | NOT NULL | Dataset that triggered the recalculation |
| `ExternalReference` | `text` | NULL | Ticker or null |
| `SourcePayloadChecksum` | `text` | NOT NULL | |
| `RequestedAt` | `timestamptz` | NOT NULL | |

**Indexes:**
- `IX_MetricRecalculationRequests_SourceDataset_SourcePayloadChecksum` — UNIQUE

---

### Table: `DerivedMetrics`

Computed growth metrics produced by the derived-metric engine after ingestion.

| Column | PG Type | Nullable | Notes |
|---|---|---|---|
| `Id` | `uuid` | NOT NULL PK | |
| `SymbolId` | `uuid` | NOT NULL | FK → `Symbols.Id` |
| `MetricCode` | `varchar(128)` | NOT NULL | e.g. `REVENUE_GROWTH_QOQ`, `REVENUE_GROWTH_YOY` |
| `MetricVersion` | `varchar(64)` | NOT NULL | |
| `CalculationPolicyVersion` | `varchar(64)` | NOT NULL | |
| `PeriodType` | `varchar(32)` | NOT NULL | |
| `PeriodStart` | `date` | NOT NULL | |
| `PeriodEnd` | `date` | NOT NULL | |
| `Value` | `numeric` | NULL | |
| `Unit` | `varchar(32)` | NOT NULL | |
| `ObservedAt` | `timestamptz` | NOT NULL | |
| `LastSynchronizedAt` | `timestamptz` | NOT NULL | |
| `WarningsJson` | `text` | NOT NULL | `"[]"` default |
| `SourceEvidenceJson` | `text` | NOT NULL | Input metric rows used in calculation |
| `DependencyEvidenceJson` | `text` | NOT NULL | Dependency chain |

**Indexes:**
- UNIQUE `(SymbolId, MetricCode, MetricVersion, CalculationPolicyVersion, PeriodEnd)`

**Migration:** `20260527050845_AddDerivedMetricResults`

---

### Tables: `FeatureDefinitions`, `FeatureSnapshots`, `FeatureComputationJobs`

ML feature layer on top of derived metrics. Not directly written by CyclicalWaves normalizers, but populated by the feature engine triggered downstream.

| Table | PG Name | Key Unique Index |
|---|---|---|
| `FeatureDefinitionRow` | `FeatureDefinitions` | `(FeatureCode, FeatureVersion)` |
| `FeatureSnapshotRow` | `FeatureSnapshots` | `(SymbolId, FeatureCode, FeatureVersion, PolicyVersion, PeriodEnd, InputFingerprint)` |
| `FeatureComputationJobRow` | `FeatureComputationJobs` | `IdempotencyKey` |

**Migration:** `20260527151735_AddDerivedFeatureFoundation`

---

## CyclicalWaves API Payload → DB Column Mapping

### Auth Response (`POST /api/auth/login`)

```json
{ "access_token": "...", "expires_in": 864000 }
```

Only used by `CyclicalWavesAuthHandler` for JWT caching. Not persisted.

---

### Ticker List Response (`GET /api/custom-filtering/tickers`)

```json
["شپنا", "فولاد", "خودرو", ...]
```

Stored verbatim in `ProviderRawPayloads` (Dataset = `Symbols`). `CyclicalWavesSymbolNormalizer` filters to Persian-script strings only (Unicode U+0600–U+06FF).

| API value | `Companies` column | `Symbols` column |
|---|---|---|
| Persian ticker (e.g. `شپنا`) | `ExternalCompanyId`, `Name` | `ExternalSymbolId`, `SymbolCode` (provisional) |

---

### Ticker Detail Response (`GET /api/custom-filtering/ticker/{ticker}`)

```json
{
  "success": true,
  "data": {
    "_id": "6421abc...",
    "ticker": "شپنا",
    "enticker": "IRO7SHLP0001",
    "last_quarter_sale": 120000,
    "penultimate_quarter_sale": 110000,
    "last_year_same_quarter_sale": 95000,
    "last_quarter_net_profit": 18000,
    ...
    "last_month_sale": 40000,
    "penultimate_month_sale": 38000,
    "last_year_same_month_sale": 32000,
    "pe": 12.5,
    "ps": 1.8
  }
}
```

Stored in `ProviderRawPayloads` twice — once under `Dataset = FinancialStatements`, once under `Dataset = MonthlyProductionSales`. Checksum deduplication prevents double storage of the identical body.

**Full field mapping:**

| JSON field | C# property | Written to |
|---|---|---|
| `_id` | `Id` | `Companies.ExternalCompanyId`, `FinancialStatements.ExternalCompanyId`, `MonthlyReports.ExternalCompanyId`, statement key prefix |
| `ticker` | `Ticker` | `Symbols.ExternalSymbolId` (lookup key), `Companies.Name` |
| `enticker` | `Enticker` | `Symbols.SymbolCode` (overwrites Persian ticker) |
| `last_quarter_sale` | `LastQuarterSale` | `FinancialStatementLineItems` REVENUE on Q-0 row |
| `penultimate_quarter_sale` | `PenultimateQuarterSale` | REVENUE on Q-1 row |
| `last_year_same_quarter_sale` | `LastYearSameQuarterSale` | REVENUE on Q-4 row |
| `last_quarter_net_profit` | `LastQuarterNetProfit` | NET_PROFIT on Q-0 |
| `penultimate_quarter_net_profit` | `PenultimateQuarterNetProfit` | NET_PROFIT on Q-1 |
| `last_year_same_quarter_net_profit` | `LastYearSameQuarterNetProfit` | NET_PROFIT on Q-4 |
| `last_quarter_gross_profit` | `LastQuarterGrossProfit` | GROSS_PROFIT on Q-0 |
| `penultimate_quarter_gross_profit` | `PenultimateQuarterGrossProfit` | GROSS_PROFIT on Q-1 |
| `last_year_same_quarter_gross_profit` | `LastYearSameQuarterGrossProfit` | GROSS_PROFIT on Q-4 |
| `last_quarter_operating_profit` | `LastQuarterOperatingProfit` | OPERATING_PROFIT on Q-0 |
| `penultimate_quarter_operating_profit` | `PenultimateQuarterOperatingProfit` | OPERATING_PROFIT on Q-1 |
| `last_year_same_quarter_operating_profit` | `LastYearSameQuarterOperatingProfit` | OPERATING_PROFIT on Q-4 |
| `last_quarter_net_profit_margin` | `LastQuarterNetProfitMargin` | NET_PROFIT_MARGIN on Q-0 |
| `penultimate_quarter_net_profit_margin` | `PenultimateQuarterNetProfitMargin` | NET_PROFIT_MARGIN on Q-1 |
| `last_year_same_quarter_net_profit_margin` | `LastYearSameQuarterNetProfitMargin` | NET_PROFIT_MARGIN on Q-4 |
| `last_quarter_gross_profit_margin` | `LastQuarterGrossProfitMargin` | GROSS_PROFIT_MARGIN on Q-0 |
| `penultimate_quarter_gross_profit_margin` | `PenultimateQuarterGrossProfitMargin` | GROSS_PROFIT_MARGIN on Q-1 |
| `last_year_same_quarter_gross_profit_margin` | `LastYearSameQuarterGrossProfitMargin` | GROSS_PROFIT_MARGIN on Q-4 |
| `last_quarter_operating_profit_margin` | `LastQuarterOperatingProfitMargin` | OPERATING_PROFIT_MARGIN on Q-0 |
| `penultimate_quarter_operating_profit_margin` | `PenultimateQuarterOperatingProfitMargin` | OPERATING_PROFIT_MARGIN on Q-1 |
| `last_year_same_quarter_operating_profit_margin` | `LastYearSameQuarterOperatingProfitMargin` | OPERATING_PROFIT_MARGIN on Q-4 |
| `last_month_sale` | `LastMonthSale` | `MonthlyReportLineItems` REVENUE on M-0 row |
| `penultimate_month_sale` | `PenultimateMonthSale` | REVENUE on M-1 row |
| `last_year_same_month_sale` | `LastYearSameMonthSale` | REVENUE on M-12 row |
| `pe` | `Pe` | PE_RATIO on Q-0 row only |
| `ps` | `Ps` | PS_RATIO on Q-0 row only |

---

## Fiscal Period Resolution

CyclicalWaves returns only relative labels (last quarter / penultimate / last-year same), not absolute dates. `CyclicalWavesRelativePeriodResolver` converts them to `(DateOnly Start, DateOnly End)` pairs using Iranian fiscal-year calendar approximations in Gregorian terms.

### Quarterly resolution

| Iranian Fiscal Quarter | Gregorian approximation |
|---|---|
| Q1 | Mar 21 – Jun 21 |
| Q2 | Jun 22 – Sep 22 |
| Q3 | Sep 23 – Dec 22 |
| Q4 | Dec 23 – Mar 20 (next calendar year) |

The resolver finds the fiscal quarter containing `asOf` UTC date, then steps back:

| Offset | Meaning | Resolution |
|---|---|---|
| `Q0` | Last completed quarter | One quarter before current |
| `Q1` | Penultimate quarter | Two quarters before current |
| `Q4` | Last-year same quarter | Q-0 dates shifted back 1 year |

### Monthly resolution

Months use the Gregorian calendar (not Iranian). Resolution from `asOf`:

| Offset | Meaning | Resolution |
|---|---|---|
| `M0` | Last completed month | `asOf` month − 1 |
| `M1` | Penultimate month | `asOf` month − 2 |
| `M12` | Last-year same month | `asOf` month − 13 |

All resolved dates are **estimates**. Every `FinancialStatements` and `MonthlyReports` row written by CyclicalWaves normalizers carries a `WarningsJson` with:

```json
[{ "Code": "StaleData", "Message": "Fiscal period dates are estimated from the request timestamp using Iranian fiscal-year calendar approximations." }]
```

---

## Migration History

### FinancialIngestionDbContext

| Migration | Added |
|---|---|
| `20260527045316_InitialFinancialIngestion` | `Companies`, `Symbols`, `FinancialStatements`, `FinancialStatementLineItems`, `MonthlyReports`, `MonthlyReportLineItems`, `ProviderSyncRuns`, `MetricRecalculationRequests` |
| `20260527050845_AddDerivedMetricResults` | `DerivedMetrics` |
| `20260527151735_AddDerivedFeatureFoundation` | `FeatureDefinitions`, `FeatureSnapshots`, `FeatureComputationJobs` |
| `20260528102634_AddWarningsJsonToNormalizedRows` | `WarningsJson` column on `FinancialStatements` and `MonthlyReports` |

### FinancialProviderDbContext

| Migration | Added |
|---|---|
| `20260527045356_InitialFinancialProviderPayloads` | `ProviderRawPayloads` |

---

## Data Flow Summary

```
CyclicalWaves API
       │
       ▼
CyclicalWavesDataProviderClient
       │  (HTTP GET, stores raw JSON)
       ▼
ProviderRawPayloadStore.StoreAsync()
       │  dedup by (ProviderName, SHA-256 checksum)
       ▼
ProviderRawPayloads table  ──── FinancialProviderDbContext
       │
       ▼
FinancialDataSyncProcessor  (selects normalizer by ProviderName + Dataset)
       │
       ├─ CyclicalWavesSymbolNormalizer
       │       → Companies (upsert by ExternalCompanyId)
       │       → Symbols   (upsert by ExternalSymbolId, SymbolCode = Persian ticker)
       │
       ├─ CyclicalWavesFinancialStatementNormalizer
       │       → Symbols.SymbolCode ← enticker
       │       → FinancialStatements   × 3 (Q0, Q1, Q4)
       │       → FinancialStatementLineItems × 7–9 per statement
       │
       └─ CyclicalWavesMonthlyReportNormalizer
               → MonthlyReports        × 3 (M0, M1, M12)
               → MonthlyReportLineItems × 1 (REVENUE) per report
       │
       ▼
MetricRecalculationRequests  (outbox)
       │
       ▼
DerivedMetricEngine
       │
       ▼
DerivedMetrics (REVENUE_GROWTH_QOQ, REVENUE_GROWTH_YOY)
```
