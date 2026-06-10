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
9. Implement full-sync and incremental sync per dataset, sharing the idempotent normalizers:
   - **Full-sync (bounded historical backfill):** instruments, daily trades
     (`TSE.TradeRefined`), intraday trades (`tse.Trade`), daily indices (`Tse.IndexNew`),
     intraday indices (`tse.IndexB1LastDay`). Paged by source key/date range with progress
     watermarks so a run is restartable and bounded.
   - **Incremental sync (overlap-watermark forward sync):** advance each dataset's watermark
     with overlap to absorb late-arriving rows; idempotent on source identifiers.
10. Add polling workers driving incremental sync: instruments daily, intraday trades every
    minute, intraday indices every five minutes, daily trades and daily indices after market
    close. Full-sync runs as an explicit, bounded admin operation per dataset.
11. Add DataAdmin endpoints for bounded manual full-sync and incremental sync per dataset, and
    operational state.
12. Add unit tests for linkage, idempotency, overlap watermarks, full-sync paging/restart, and
    quote fallback.
13. Add integration tests for persistence, migrations, admin authorization, cache
    invalidation, and scanner latest-price reads.
14. Update operator documentation and record completion evidence in the checklist.

