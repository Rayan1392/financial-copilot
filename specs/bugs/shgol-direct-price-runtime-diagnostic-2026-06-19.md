# `آخرین قیمت شگل` Runtime Diagnostic

Date: 2026-06-19
Status: Diagnostic only
Scope: current production branch, no production code changes

## Request

`آخرین قیمت شگل`

## Expected SQL Reference Outcome

- Symbol/company resolves to `شگل` / `گلتاش`
- `InstrumentCode = 44153164692325703`
- `TradingInstrumentId = 92990a92-e853-47e3-a682-bb8794b22999`
- No intraday row for `2026-06-19`
- Latest daily fallback row exists:
  - `TradingDate = 2026-06-17`
  - `LastTradedPrice = 3934`
  - `PriceChangePercentage = 2.9842931937172800`
- User-facing lookup should return:
  - `LATEST_PRICE = 3934`
  - `DAILY_CHANGE_PCT = 2.98`
  - `TradingDate = 2026-06-17`
  - `SourceLabel = LatestDailyFallback`

## Evidence Table

| Step | Method / Class | Input | Actual output / evidence | Matches expected SQL flow? |
|---|---|---|---|---|
| 1. Parsed symbols | `LlmSymbolLookupParser.TryParseDirectPriceLookup(...)` via `TryParseDirectLookup(...)` | `آخرین قیمت شگل` | Deterministic direct-price branch strips `آخرین قیمت` and preserves `RawSymbolName = شگل` | Yes |
| 2. Parsed metrics | `LlmSymbolLookupParser.TryParseDirectPriceLookup(...)` | `آخرین قیمت شگل` | `ResolvedMetricCode = LATEST_PRICE`, `OriginalMetricTerm = آخرین قیمت` | Yes |
| 3. Resolved company / symbol | `CompanyResolverService.ResolveBySymbolAsync("شگل")` | `شگل` | `ExternalCompanyId = 167`, `Ticker = null`, `TseSymbol = شگل`, `CompanySymbol = شگل`, `InstrumentCode = 44153164692325703` | Yes |
| 4. Resolved `InstrumentCode` | `PersistedMarketDataProvider.ResolveEligibleCompanyInstrumentIdsAsync(...)` and company row evidence | `شگل` | `InstrumentCode = 44153164692325703` | Yes |
| 5. Resolved `TradingInstrumentId` | `PersistedMarketDataProvider.ResolveInstrumentIdsByCodeAsync(...)` | `44153164692325703` | `TradingInstrumentId = 92990a92-e853-47e3-a682-bb8794b22999` | Yes |
| 6. Provider config values | `MarketQuoteSourcePriorityOptions.PrimarySourceName` default + API appsettings | API runtime | API appsettings do **not** override `MarketQuoteSourcePriority:PrimarySourceName`; code default is `StockMarketDb`. Worker appsettings override `TsetmcWebService`, but API does not. | No |
| 7. Intraday query result count | `PersistedMarketDataProvider.ResolveFromCanonicalTradeTablesAsync(...)` intraday branch | `TradingInstrumentId = 92990a92-e853-47e3-a682-bb8794b22999`, `TradingDate = 2026-06-19`, `ProviderName = StockMarketDb` | `0` rows | Yes |
| 8. Daily fallback query result count | `PersistedMarketDataProvider.ResolveFromCanonicalTradeTablesAsync(...)` daily branch | `TradingInstrumentId = 92990a92-e853-47e3-a682-bb8794b22999`, `ProviderName = StockMarketDb` | `0` rows through production code path. Live DB evidence shows `1` row exists under `ProviderName = TsetmcWebService`: `2026-06-17`, `ClosingPrice=3911`, `LastTradedPrice=3934`, `PriceChange=91`, `PriceYesterday=3820`, `PriceChangePercentage=2.9842931937172800` | No |
| 9. Selected quote object | `PersistedMarketDataProvider.GetLatestQuotesAsync(...)` → `ProviderMarketQuoteResolver.ResolveAsync(...)` | `SymbolCode = شگل` | `MarketQuoteObservation = null`. Projection fallback also misses because `LatestMarketQuotes` row exists only under `ProviderName = TsetmcWebService`, while code filters `ProviderName = StockMarketDb`. | No |
| 10. Symbol lookup cells before API mapping | `EfCoreSymbolMetricLookupService.LookupAsync(...)` → `BuildCells(...)` | `Pairs = [(شگل, LATEST_PRICE)]`, `QueryText = آخرین قیمت شگل` | Row exists: `SymbolCode = شگل`, `CompanyName = گلتاش`. Cells: `SYMBOL -> formattedValue=شگل, freshness=Persisted`; `COMPANY_NAME -> formattedValue=گلتاش, freshness=Persisted`; `LATEST_PRICE -> value=null, formattedValue=null, freshness=Missing`; `DAILY_CHANGE_PCT -> value=null, formattedValue=null, freshness=Missing` | No |
| 11. API response cells after mapping | `AiFacadeController.MapSymbolLookupTable(...)` → `MapCells(...)` | same lookup table | Mapping preserves the same cell values because row count is `1`: `LATEST_PRICE -> { value: null, formattedValue: null, freshnessStatus: Missing, sourceTimestamp: null, tradingDate: null, tradingDatePersian: null, sourceLabel: null }`; `DAILY_CHANGE_PCT` identical missing shape | No |

## Exact Failing Step

The first failing step is **Step 6 / Step 8** at the market-quote source selection boundary:

- the API runtime uses `PrimarySourceName = StockMarketDb`
- the real `شگل` quote rows are stored under `ProviderName = TsetmcWebService`
- therefore both quote retrieval branches filter out valid rows before `EfCoreSymbolMetricLookupService` builds the `LATEST_PRICE` cell

## Direct Evidence Captured

- `Companies` match for `شگل`:
  - `ExternalCompanyId = 167`
  - `Name = گلتاش`
  - `TseSymbol = شگل`
  - `CompanySymbol = شگل`
  - `InstrumentCode = 44153164692325703`
- `NoavaranEligibleCompanies` match:
  - `ProviderName = NoavaranCurrentApi`
  - `TseSymbol = شگل`
  - `InstrumentCode = 44153164692325703`
- `DailyInstrumentTrades` fallback row:
  - `ProviderName = TsetmcWebService`
  - `TradingDate = 2026-06-17`
  - `ClosingPrice = 3911.00`
  - `LastTradedPrice = 3934.00`
  - `PriceChange = 91.00`
  - `PriceYesterday = 3820.00`
  - `PriceChangePercentage = 2.9842931937172800`
- `LatestMarketQuotes` row:
  - `ProviderName = TsetmcWebService`
  - `LatestPrice = 3934.00`
  - `PriceChangePercentage = 2.9842931937172774869109947600`
  - `TradingDate = 2026-06-17`
  - `SourceKind = Intraday`

## Regression Test Added

- Added failing endpoint-level regression test:
  - `V2ShgolDirectPriceRegressionEndpointTests.V2AiQuery_DirectPriceQuestion_Shgol_ShouldReturnLatestDailyFallbackQuote`
  - File: [AiFacadeV2EndpointTests.cs](/d:/Source/TahlilApp-AI/tests/FinancialCopilot.IntegrationTests/AiFacadeV2EndpointTests.cs:1)
- Test seeds:
  - `شگل` company
  - `TradingInstrumentId = 92990a92-e853-47e3-a682-bb8794b22999`
  - `DailyInstrumentTrades` and `LatestMarketQuotes` under `ProviderName = TsetmcWebService`
  - no API override for `MarketQuoteSourcePriority:PrimarySourceName`
- Expected assertion:
  - `LATEST_PRICE = 3,934`
  - `DAILY_CHANGE_PCT = +2.98%`
  - `SourceLabel = LatestDailyFallback`
- Current branch behavior:
  - test fails because `LATEST_PRICE` remains `Missing`
