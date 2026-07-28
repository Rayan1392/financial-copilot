# Tasks

1. Add `StockMarketDbOptions`, a read-only connection factory, resilience wrapper, and query
   executor with bounded paging.
2. Add read-only query models for the authoritative source tables:
   - `Tse.Instrument` (instruments)
   - `tse.Trade` (intraday / in-day trades)
   - `TSE.TradeRefined` / `TradeOneDay` (daily trades — one price per trading day per
     instrument)
   - `tse.IndexB1LastDay` (intraday / in-day indices)
   - `Tse.IndexNew` (daily indices), filtered by the named-index `InstrumentRef` GUIDs
     (شاخص کل, شاخص کل فرابورس, شاخص بازده نقدی و قیمت, شاخص ۵۰ شرکت فعال‌تر,
     شاخص قیمت هم‌وزن, شاخص کل هم‌وزن — see user-story Scope for the exact GUIDs).
3. Add PostgreSQL rows, EF configurations, indexes, partitions/retention documentation, and a
   migration for trading instruments, intraday trades, daily trades, intraday indices, daily
   indices, latest quotes, and per-dataset watermarks.
4. Normalize instruments first; link nullable company ids using `InsCode -> InstrumentCode`.
5. Add idempotent normalizers and canonical raw-payload checksums for each time-series dataset.
6. Persist daily index closes from `Tse.IndexNew` per `InstrumentRef`, keyed by trading day.
7. Implement the latest-market-quote projection with intraday-first and daily fallback policy.
8. Wire `IMarketDataProvider` reads to the PostgreSQL latest-quote projection.
9. Verify and, if needed, correct the runtime quote resolver/provider algorithm so symbol/company
   lookup resolves `TradingInstrumentId` through
   `NoavaranEligibleCompanies.InstrumentCode -> TradingInstruments.InstrumentCode` before reading
   quote data.
10. Verify how `LatestMarketQuotes`, `IntradayTradeSnapshots`, and `DailyInstrumentTrades` are
    currently used by direct latest-price answers and valuation quote enrichment.
    - Confirm that **no query against these tables filters rows by `ProviderName`** in the
      runtime quote retrieval path (`PersistedMarketDataProvider`, `ProviderMarketQuoteResolver`,
      or any wrapper). The API runtime `MarketQuoteSourcePriorityOptions.PrimarySourceName`
      setting must not be passed as a `WHERE ProviderName = ...` predicate on canonical price
      tables. If such a filter exists, it must be removed.
    - Confirm that `LatestMarketQuotes` projection fallback also does not filter by `ProviderName`
      from the API config; if it does, remove that filter.
11. Ensure the quote lookup policy is:
    - current-date `IntradayTradeSnapshots` first (date-only comparison on trading date,
      **no `ProviderName` filter**)
    - latest `DailyInstrumentTrades` fallback when today intraday data is missing
      (**no `ProviderName` filter**)
    - `LatestMarketQuotes` projection fallback only if both canonical tables fail
      (**no `ProviderName` filter**)
    - unavailable only when all three sources fail
    - `ProviderName` is recorded for provenance/audit; it must not determine quote eligibility
12. Ensure price-change percentage is calculated consistently for both intraday and daily
    fallback paths:
    - `LATEST_PRICE = LastTradedPrice`
    - `DAILY_CHANGE_PCT = (LastTradedPrice / NULLIF(PriceYesterday, 0) - 1) * 100`
    - Do not derive `DAILY_CHANGE_PCT` from `ClosingPrice` in latest-price context.
      If closing-price change is needed later, define it as a separate metric
      (`CLOSING_PRICE_CHANGE_PCT`) rather than overloading `LATEST_PRICE`.
    - User-facing display of `DAILY_CHANGE_PCT` must use two decimal places.
13. Add or correct user-visible trading-date exposure and Persian/Jalali formatting for direct
    quote answers and quote-enriched valuation tables.
14. Update response contracts, deterministic prose, and table rendering only if required to carry
    trading date plus source/freshness labeling such as `IntradayToday` versus
    `LatestDailyFallback`.
9. Implement full-sync and incremental sync per dataset, sharing the idempotent normalizers:
   - **Full-sync (bounded historical backfill):** instruments, daily trades
     (`TSE.TradeRefined`), intraday trades (`tse.Trade`), daily indices (`Tse.IndexNew`),
     intraday indices (`tse.IndexB1LastDay`). Paged by source key/date range with progress
     watermarks so a run is restartable and bounded.
   - **Incremental sync (overlap-watermark forward sync):** advance each dataset's watermark
     with overlap to absorb late-arriving rows; idempotent on source identifiers.
15. Add polling workers driving incremental sync: instruments daily, intraday trades every
    minute, intraday indices every five minutes, daily trades and daily indices after market
    close. Full-sync runs as an explicit, bounded admin operation per dataset.
16. Add DataAdmin endpoints for bounded manual full-sync and incremental sync per dataset, and
    operational state.
17. Add unit tests for linkage, idempotency, overlap watermarks, full-sync paging/restart, and
    quote fallback, including:
    - intraday-today hit
    - latest-daily fallback hit
    - missing only when all sources (intraday, daily, projection) fail
    - `DAILY_CHANGE_PCT = (LastTradedPrice / PriceYesterday - 1) * 100` on both intraday and
      daily fallback paths; verify `ClosingPrice` is not used for this calculation
    - `TradingInstrumentId` resolution through eligible-company `InstrumentCode`
    - **provider-name mismatch regression:** given API `PrimarySourceName = StockMarketDb` and
      `DailyInstrumentTrades.ProviderName = TsetmcWebService` for the resolved
      `TradingInstrumentId`, the quote must still be found; `LATEST_PRICE` must equal
      `LastTradedPrice` (e.g. `3934`) and `DAILY_CHANGE_PCT` must equal the correct two-decimal
      value (e.g. `2.98`); no `Missing` cells
    - **formatting regression:** raw `PriceChangePercentage = 2.9842931937172800` must display
      as `2.98` (two decimal places) in user-facing output
    - **freshness-label regression:** a quote resolved from latest `DailyInstrumentTrades`
      must carry source label `LatestDailyFallback` (or `PreviousTradingDay`), not `IntradayToday`
18. Add integration tests for persistence, migrations, admin authorization, cache
    invalidation, and scanner latest-price reads.
19. Add regression tests proving valuation lookups such as `PE` / `PS` reuse the same quote
    fallback behavior for `LATEST_PRICE` and `DAILY_CHANGE_PCT`:
    - for `پی به ای کگل چقدر است؟`, `PE_TTM`, `LATEST_PRICE`, and `DAILY_CHANGE_PCT` must all
      be populated when any valid canonical quote row exists for the resolved instrument,
      regardless of `ProviderName`
    - seed test data with `DailyInstrumentTrades.ProviderName = TsetmcWebService` while API
      `PrimarySourceName = StockMarketDb` to verify provider-independence
20. Add regression tests proving monthly production/sales lookups still suppress quote columns
    after the quote fallback behavior is corrected:
    - `آخرین فروش کگل؟`, `فروش ماهانه کچاد؟`, `متوسط فروش 12 ماهه کچاد؟` must not include
      `LATEST_PRICE` or `DAILY_CHANGE_PCT`; this rule must remain in force even when those
      columns would be non-null for the same symbol
21. Add an endpoint-level regression test for the confirmed `شگل` provider-name mismatch bug:
    - seed: `شگل` company, `TradingInstrumentId = 92990a92-e853-47e3-a682-bb8794b22999`,
      `DailyInstrumentTrades` and `LatestMarketQuotes` rows under `ProviderName = TsetmcWebService`,
      no API override for `MarketQuoteSourcePriority:PrimarySourceName`
    - assert: `LATEST_PRICE = 3,934`, `DAILY_CHANGE_PCT = +2.98%`, `SourceLabel = LatestDailyFallback`
    - assert: no `Missing` freshness on `LATEST_PRICE` or `DAILY_CHANGE_PCT`
21. Update operator documentation and record completion evidence in the checklist.
