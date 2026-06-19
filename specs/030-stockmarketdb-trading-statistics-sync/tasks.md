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
11. Ensure the quote lookup policy is:
    - current-date `IntradayTradeSnapshots` first (date-only comparison on trading date)
    - latest `DailyInstrumentTrades` fallback when today intraday data is missing
    - unavailable only when both sources fail
12. Ensure price-change percentage is calculated/returned consistently for both intraday and
    daily fallback paths.
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
    - missing only when both sources fail
    - `PriceChangePercentage` behavior on both paths
    - `TradingInstrumentId` resolution through eligible-company `InstrumentCode`
18. Add integration tests for persistence, migrations, admin authorization, cache
    invalidation, and scanner latest-price reads.
19. Add regression tests proving valuation lookups such as `PE` / `PS` reuse the same quote
    fallback behavior for `LATEST_PRICE` and `DAILY_CHANGE_PCT`.
20. Add regression tests proving monthly production/sales lookups still suppress quote columns
    after the quote fallback behavior is corrected.
21. Update operator documentation and record completion evidence in the checklist.
