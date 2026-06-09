# TahlilApp-AI Data & AI Flow — `financial_copilot`

## Scope

This document explains how TahlilApp-AI currently finds and uses data in the `financial_copilot` application database for:

1. AI chat questions such as `pe کگل چقدر است؟` or `آخرین قیمت شپنا چنده؟`
2. Fundamental data: financial statements, balance sheet, income statement, cash flow, fundamental indexes, monthly activity, and derived metrics
3. Trading statistics: price, daily/intraday trades, index daily/intraday data

## Current implementation status from specs

| Area | Status | Main specs |
|---|---|---|
| Core AI facade, scanner, explainability, billing | Implemented | 007, 008, 009, 010 |
| Financial semantic layer and metric aliases | Implemented | 015 |
| Derived metrics engine | Implemented | 006, 016 |
| Symbol metric point lookup | Implemented | 045 |
| Microsoft Agent Framework orchestration V2 | Implemented | 047 |
| OpenAI / DeepSeek provider switching | Implemented | 049 |
| Noavaran archive SQL import | Implemented / frozen-source model | 051, 052 |
| Noavaran current API ingestion from 1403 onward | Not implemented yet | 053 |
| NADPCO / Noavaran all fundamental-index catch-up | Not implemented yet | 050 |
| StockMarketDB bridge sync for trading statistics | Implemented | 030 |
| Direct TSETMC feed migration | Not implemented yet | 054 |
| Frontend data management console | Not implemented yet | 055 |
| Dynamic alias learning | Not implemented yet | 046 |

---

# 1. AI flow: how the assistant finds data

## Public entry point

All user chat questions go through one public endpoint:

```http
POST /api/ai/v1/query
```

The frontend should not call separate scanner, lookup, provider, SQL, or calculation endpoints for user questions. The AI facade is the orchestration boundary.

## High-level AI execution flow

```text
User message
  -> POST /api/ai/v1/query
  -> Authenticate actor / tenant / API client
  -> Reserve AI usage credit
  -> Detect intent
      -> Scanner intent
      -> SymbolLookup intent
      -> Clarification / unsupported intent
  -> Parse requested symbols, metrics, filters, periods
  -> Resolve metric aliases through semantic layer
  -> Resolve symbols/companies through normalized Companies/Symbols
  -> Execute deterministic backend service
      -> ScannerExecutionEngine
      -> SymbolMetricLookupService
  -> Read normalized data from financial_copilot
      -> DerivedMetrics
      -> LatestMarketQuotes
      -> FinancialStatements / MonthlyReports when recalculation is needed
  -> Build explainable result
      -> table rows
      -> citations
      -> freshness
      -> confidence
      -> usage metadata
  -> Finalize billing
  -> Persist conversation/message/result
  -> Return response to frontend
```

## Important rule

The LLM does **not** calculate financial values and does **not** run SQL. It only helps with intent detection, natural-language parsing, metric/symbol extraction, and final language generation. Financial calculation and data retrieval are deterministic backend responsibilities.

## Example: `pe کگل چقدر است؟`

```text
User: pe کگل چقدر است؟
  -> IntentDetector: SymbolLookup
  -> SymbolLookupParser extracts:
       raw symbol = کگل
       metric term = pe
  -> IMetricAliasResolver maps `pe` to canonical MetricCode, usually PE_TTM
  -> ISymbolNameResolver resolves کگل against Companies/Symbols
  -> ISymbolMetricLookupService queries:
       DerivedMetrics for PE_TTM
       all Symbols linked to the resolved CompanyId
  -> Result table uses:
       Companies.TseSymbol as display symbol
       Companies.Name as company name
       PE_TTM from latest available DerivedMetrics row
  -> If missing, cell freshness = Missing and feedback is logged
```

## Example: `آخرین قیمت شپنا چقدر است؟`

```text
User: آخرین قیمت شپنا چقدر است؟
  -> IntentDetector: SymbolLookup
  -> Metric alias maps آخرین قیمت to LATEST_PRICE
  -> Symbol resolver maps شپنا to company/security
  -> SymbolMetricLookupService reads LatestMarketQuotes
  -> LatestMarketQuotes is populated by StockMarketDB bridge ingestion
  -> Result includes symbol, company, latest price, freshness, citation/provenance
```

## Example: scanner query

```text
User: سهم‌هایی با P/E کمتر از 6 و رشد سود خالص بالای 50٪
  -> IntentDetector: Scanner
  -> NaturalLanguageScannerParser creates a validated query plan
  -> Metric aliases resolve to canonical MetricCode values
  -> ScannerExecutionEngine reads DerivedMetrics / LatestMarketQuotes
  -> Result table returns matching companies with explainability and confidence
```

---

# 2. Application database: `financial_copilot`

`financial_copilot` is the normalized operational PostgreSQL database used by TahlilApp-AI. It is not supposed to be a thin proxy over provider databases. The intended model is:

```text
External / provider sources
  -> raw payload capture
  -> normalization
  -> canonical PostgreSQL tables in financial_copilot
  -> deterministic derived metrics / projections
  -> AI facade reads only canonical read models
```

## Main normalized tables / read models

| Purpose | Tables / models |
|---|---|
| Company/security identity | Companies, Symbols, Industries, TradingInstruments |
| Raw auditability | ProviderRawPayloads, ProviderSyncRuns, ProviderSyncErrors |
| Financial statements | FinancialStatements, FinancialStatementLineItems |
| Monthly activity | MonthlyReports, MonthlyReportLineItems |
| Derived/precomputed metrics | DerivedMetrics |
| Semantic catalog | FinancialMetricDefinitions, MetricAliases, MetricCalculationPolicies, MetricDependencies |
| Trading statistics | IntradayTradeSnapshots, DailyInstrumentTrades, IntradayIndexSnapshots, DailyIndexSnapshots |
| Latest quote projection | LatestMarketQuotes |
| AI execution evidence | Conversations, Messages, AiQueryExecutions, AiToolExecutions, ScannerQueries, ScannerQueryPlans, ScannerExecutions, ScannerResultItems |
| Billing | UsageReservations, UsageLedgerEntries, WalletBalanceProjections |
| Missing answers | MissingAnswerFeedback |

---

# 3. Fundamental data flow

## Source boundary

The corrected source model is:

| Logical vendor | Physical source | Source mode | Role |
|---|---|---|---|
| NoavaranAmin | NoavaranArchiveSql | ArchiveOneTime | Historical/archive SQL snapshot, imported once and frozen |
| NoavaranAmin | NoavaranCurrentApi | CurrentIncremental | Current data from Shamsi 1403 onward; not implemented yet |
| CyclicalWaves | CyclicalWavesApi | ExternalSnapshot | External snapshot provider, not company-catalog authority |
| Tsetmc | StockMarketDb | MigrationBridge | Current trading-statistics bridge source |
| Tsetmc | TsetmcWebService | Direct feed target; not implemented yet |

## Company catalog

The authoritative company/security catalog is intended to come from Noavaran/NADPCO company catalog data, not from CyclicalWaves. The canonical identity should prefer stable identifiers such as:

- `CoID`
- ISIN codes
- `InstCode`
- `CompanySymbol`
- normalized TSE symbol

## Financial statements

### Sources

- Current implemented historical/archive path: `NoavaranArchiveSql`, originally CodalDB SQL Server.
- Current API path from 1403 onward: planned in spec 053.

### Normalized target

```text
FinancialStatements
FinancialStatementLineItems
```

### Statement categories

`FinancialStatements` separates:

| Column | Meaning |
|---|---|
| StatementType | IncomeStatement, BalanceSheet, CashFlow |
| PeriodType | ThreeMonths, SixMonths, NineMonths, TwelveMonths, Monthly, TrailingTwelveMonths |

CashFlow is supported by the schema but is currently mostly reserved/deferred unless a normalizer maps cash-flow line items.

### Flow

```text
NoavaranArchiveSql / NoavaranCurrentApi
  -> provider query / API call
  -> ProviderRawPayload saved with checksum and provenance
  -> normalizer maps source item ids / API fields to canonical MetricCode
  -> FinancialStatements row upserted
  -> FinancialStatementLineItems row upserted
  -> MetricRecalculationRequest published
  -> MetricRecalculationProcessor computes DerivedMetrics
  -> Scanner / SymbolLookup reads DerivedMetrics
```

## Fundamental indexes

### Current implemented path

Reviewed vendor indexes are persisted into `DerivedMetrics` as source-marked observations with distinct calculation policy versions. They are not recalculated by the internal engine when they are vendor-precomputed ratios/indexes.

### Open catch-up path

Spec 050 is still open. It should fetch all company fundamental indexes from Shamsi 1403 through 1405 using:

```http
POST /api/v2/CompanyFundamentalIndex/Values?fromYear=1403&toYear=1405
```

with:

```json
{
  "companyIds": [...],
  "companyIndexIds": []
}
```

Important: this catch-up must persist all vendor observations for coverage, but only reviewed/index-mapped metrics should be promoted into governed scanner metrics.

## Monthly activity / production and sales

### Normalized target

```text
MonthlyReports
MonthlyReportLineItems
```

### Flow

```text
Noavaran source
  -> monthly product/service activity query
  -> raw payload saved
  -> Jalali year/month converted to Gregorian period window
  -> MonthlyReports / MonthlyReportLineItems upserted
  -> MetricRecalculationRequest published
  -> Derived metrics recalculated:
       MONTHLY_SALES
       MONTHLY_SALES_GROWTH_YOY
       MONTHLY_SALES_GROWTH_MOM
       TTM_SALES
```

## Derived metrics

DerivedMetrics is the primary read path for AI/scanner answers about financial metrics.

Examples:

- `PE_TTM`
- `EPS_TTM`
- `NET_PROFIT`
- `NET_PROFIT_GROWTH_YOY`
- `REVENUE`
- `REVENUE_GROWTH_YOY`
- `MONTHLY_SALES`
- `TTM_SALES`
- reviewed financial ratios and indexes

Flow:

```text
FinancialStatements / MonthlyReports / vendor ratio/index observations
  -> MetricRecalculationRequests outbox
  -> MetricRecalculationProcessor worker
  -> DerivedMetricRecalculationCommand
  -> IFinancialMetricCalculator strategies
  -> DerivedMetrics upserted with metric code, period, policy version, source evidence
```

---

# 4. Trading statistics flow

## Current implemented source

`StockMarketDB` is an MS SQL Server bridge database currently updated by another service using TSETMC ASMX web services. TahlilApp-AI currently reads it as a read-only bridge source.

## Source tables

| StockMarketDB table | Role | Cadence |
|---|---|---|
| Tse.Instrument | TSE instrument/security dimension | Daily + on-demand |
| Tse.Trade | Intraday instrument quote/trade snapshots | Every minute |
| Tse.InstTrade | One daily instrument summary row | Nightly after market close |
| Tse.IndexB1LastDay | Current intraday index snapshots | Every five minutes |
| Tse.IndexNew2 | Historical daily index backfill | Backfill / historical only |

## Normalized target tables

| Target table | Meaning |
|---|---|
| TradingInstruments | Provider-scoped TSE instruments; nullable company link |
| IntradayTradeSnapshots | Intraday price/trade snapshots |
| DailyInstrumentTrades | Daily instrument trading rows |
| IntradayIndexSnapshots | Intraday index values |
| DailyIndexSnapshots | Daily index close/history |
| LatestMarketQuotes | Small projection used by scanner, symbol lookup, watchlist, market summary |

## Linkage

```text
StockMarketDB.Tse.Instrument.Id
  -> used by Trade / InstTrade / IndexB1LastDay as InstrumentRef
  -> resolve to Tse.Instrument.InsCode
  -> match financial_copilot.Companies.InstrumentCode
  -> optional TradingInstruments.NormalizedCompanyId
```

Do not use the old CodalDB `Companies.InstrumentRef` as a TSETMC linkage key; it is a placeholder and not the StockMarketDB instrument id.

## Price flow

```text
StockMarketDB polling worker
  -> read bounded pages using timestamp/source-id watermarks
  -> save raw payload
  -> normalize intraday/daily rows
  -> update LatestMarketQuotes projection
  -> invalidate scanner / market-summary cache
  -> SymbolLookup and Scanner read LatestMarketQuotes
```

## Index flow

```text
Tse.IndexB1LastDay
  -> IntradayIndexSnapshots
  -> derive current daily close from last snapshot of trading day
  -> DailyIndexSnapshots
```

`Tse.IndexNew2` is used for historical daily index backfill only, because the older current index-history tables stopped updating in 2024.

## Future target

Spec 054 is open. Final architecture should replace the bridge dependency with direct TSETMC web-service ingestion:

```text
Short term: StockMarketDB bridge
Transition: StockMarketDB and TsetmcWebService in parallel validation
Final: TsetmcWebService owns trading-statistics updates directly
```

The scanner and AI facade should continue reading canonical projections such as `LatestMarketQuotes`; they should not care whether the projection came from StockMarketDB or direct TSETMC.

---

# 5. Data freshness, provenance, and confidence

Every answer should carry evidence:

- source provider / physical source
- source mode: archive, current incremental, bridge, external snapshot
- period end or quote timestamp
- last sync time
- freshness state: live, previous trading day, persisted, missing
- metric calculation policy version
- raw payload checksum / evidence where applicable
- confidence score
- usage accounting metadata

For archive data, absence of recent sync should not be treated as stale after the archive is frozen. For current API and market data, freshness should be evaluated against sync cadence.

---

# 6. What is still missing / recommended next work

## Highest priority

1. Implement spec 053: Noavaran Current API Ingestion.
   - This is required for financial/fundamental data from Shamsi 1403 onward.
   - Scheduled ingestion should target this source, not the frozen archive.

2. Implement spec 050: all fundamental-index catch-up.
   - Fetch all index observations for all local Noavaran/NADPCO companies from 1403 to 1405.
   - Store all vendor observations as coverage data.
   - Promote only reviewed indexes to governed metrics.

3. Implement spec 054: StockMarketDB to direct TSETMC migration.
   - Keep StockMarketDB as bridge.
   - Add direct TSETMC source in parallel.
   - Make `LatestMarketQuotes` source-priority driven.

## Medium priority

4. Implement spec 055: Frontend Data Management Console.
   - Show source freshness, sync history, archive freeze state, current API status, StockMarketDB status, and future TSETMC status.

5. Implement spec 046: Dynamic Metric Alias Learning.
   - Useful for questions where users use new Persian/English metric names.
   - Must only add aliases after validation; it must not create formulas or SQL from LLM output.

---

# 7. Practical mental model

## For `PE کگل`

```text
Question -> AI facade -> SymbolLookup -> Metric alias PE_TTM -> Company/Symbol کگل
-> DerivedMetrics -> answer
```

## For `آخرین قیمت شپنا`

```text
Question -> AI facade -> SymbolLookup -> Metric alias LATEST_PRICE -> Company/Symbol شپنا
-> LatestMarketQuotes -> answer
```

## For financial statement metrics

```text
Noavaran archive/current source -> FinancialStatements/LineItems
-> DerivedMetrics -> AI answer
```

## For fundamental indexes

```text
Noavaran current API / catch-up -> vendor index observations
-> reviewed indexes promoted to DerivedMetrics
-> AI answer
```

## For trading price and index data

```text
StockMarketDB bridge now / direct TSETMC later
-> trading time-series tables
-> LatestMarketQuotes / DailyIndexSnapshots
-> AI answer
```
