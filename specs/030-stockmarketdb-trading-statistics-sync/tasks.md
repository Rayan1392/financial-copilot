# Tasks

1. Add `StockMarketDbOptions`, a read-only connection factory, resilience wrapper, and query
   executor with bounded paging.
2. Add query models for `Tse.Instrument`, `Tse.Trade`, `Tse.InstTrade`,
   `Tse.IndexB1LastDay`, and historical `Tse.IndexNew2`.
3. Add PostgreSQL rows, EF configurations, indexes, partitions/retention documentation, and a
   migration for trading instruments, intraday trades, daily trades, intraday indices, daily
   indices, latest quotes, and per-dataset watermarks.
4. Normalize instruments first; link nullable company ids using `InsCode -> InstrumentCode`.
5. Add idempotent normalizers and canonical raw-payload checksums for each time-series dataset.
6. Derive current daily index closes from the last intraday index snapshot per trading day.
7. Implement the latest-market-quote projection with intraday-first and daily fallback policy.
8. Wire `IMarketDataProvider` reads to the PostgreSQL latest-quote projection.
9. Add polling workers: instruments daily, intraday trades every minute, indices every five
   minutes, daily trades after market close, and historical index backfill as an explicit admin
   operation.
10. Add DataAdmin endpoints for bounded manual full/incremental sync and operational state.
11. Add unit tests for linkage, idempotency, overlap watermarks, daily-close derivation, and
    quote fallback.
12. Add integration tests for persistence, migrations, admin authorization, cache
    invalidation, and scanner latest-price reads.
13. Update operator documentation and record completion evidence in the checklist.

