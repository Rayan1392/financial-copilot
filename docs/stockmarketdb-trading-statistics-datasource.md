# StockMarketDB Trading Statistics Datasource

## Purpose

This document records the live, read-only SQL Server schema inspection completed on
`2026-06-01` for the local `StockMarketDB` database. It defines the recommended PostgreSQL
normalization strategy for market instruments, intraday quotes, daily trades, and indices.

Credentials are operational secrets. Configure them outside source control under a dedicated
`StockMarketDb` configuration section. Do not reuse or hardcode the supplied local `sa`
password.

## Source Tables

| Table | Rows observed | Role | Recommended cadence |
|---|---:|---|---|
| `Tse.Instrument` | 76,810 | Registered TSE instruments and index identities | Daily plus on-demand refresh |
| `Tse.Trade` | ~450,000 | Intraday instrument quote/trade snapshots | Every minute |
| `Tse.InstTrade` | 2,172,303 | One summarized instrument row per trading day | Nightly after market close |
| `Tse.IndexB1LastDay` | 46,206 | Current intraday index snapshots | Every five minutes |
| `Tse.IndexNew` | 598 | Legacy daily index history for one index | Historical reference only |
| `Tse.IndexNew2` | 227,436 | Broader historical daily index history for 70 indices | Historical backfill only |

The row counts for live tables changed during inspection because ingestion was active.

## Identity And Linkage

`Tse.Instrument.Id` is the source FK used by `Tse.Trade`, `Tse.InstTrade`, and
`Tse.IndexB1LastDay` through their `InstrumentRef` columns.

`Tse.Instrument.InsCode` is unique and is the stable TSETMC identifier used for cross-source
alignment. Translate `InstrumentRef -> Tse.Instrument.Id -> Tse.Instrument.InsCode`, then match
the existing normalized company `InstrumentCode`.

Live verification:

- All `859` CodalDB companies carrying `Companies.InstCode` matched `Tse.Instrument.InsCode`.
- `806` of those matched instruments were active (`Valid = 1`, not deleted).
- The source contains many non-company instruments such as options, debt instruments, and
  indices. A trading instrument dimension must exist separately from normalized companies.

Do not use CodalDB `Companies.InstrumentRef`: it is a constant placeholder and is unrelated to
`StockMarketDB.Tse.Instrument.Id`.

## Current And Historical Index Sources

`Tse.IndexB1LastDay` is the current source. During inspection it contained index snapshots
through `2026-06-01 12:35`.

`Tse.IndexNew` stopped updating on `2024-06-06`. `Tse.IndexNew2` has wider historical coverage
but stopped updating on `2024-06-08`. Use `IndexNew2` for historical backfill only. For current
daily index closes, derive one row per `(InstrumentRef, IndexDate)` from the last
`IndexB1LastDay` snapshot of the trading day.

## PostgreSQL Normalized Model

Add provider-scoped tables:

| PostgreSQL table | Source | Unique key |
|---|---|---|
| `TradingInstruments` | `Tse.Instrument` | `(ProviderName, ExternalInstrumentId)` and `(ProviderName, InstrumentCode)` |
| `IntradayTradeSnapshots` | `Tse.Trade` | `(ProviderName, ExternalSnapshotId)` |
| `DailyInstrumentTrades` | `Tse.InstTrade` | `(ProviderName, ExternalTradeId)` |
| `IntradayIndexSnapshots` | `Tse.IndexB1LastDay` | `(ProviderName, ExternalSnapshotId)` |
| `DailyIndexSnapshots` | derived current close plus optional `Tse.IndexNew2` backfill | `(ProviderName, TradingInstrumentId, TradingDate)` |
| `LatestMarketQuotes` | projection from latest intraday snapshot, daily fallback | `(ProviderName, TradingInstrumentId)` |

`TradingInstruments.NormalizedCompanyId` is nullable. Company linkage uses `InstrumentCode`;
non-company instruments remain queryable without inventing company records.

Keep `LatestMarketQuotes` as a small upsert projection for scanner and market-summary reads.

The initial PostgreSQL migration keeps the append-heavy intraday tables unpartitioned because
their global `(ProviderName, ExternalSnapshotId)` unique keys guarantee idempotency across the
entire history. PostgreSQL declarative partitions require partition keys in parent-level unique
constraints, so converting immediately would weaken that guarantee or require a different key
design. The polling worker runs daily retention cleanup and keeps 30 days of intraday trade and
index snapshots by default. Before retained volume becomes operationally significant, introduce
a dedicated partition migration with date-aware idempotency keys and detach/drop old partitions
instead of row deletes.

## Incremental Watermarks

| Dataset | Source watermark | Notes |
|---|---|---|
| Instruments | `ChangeTime` plus `Id` overlap | Refresh changed instruments and retain source `Id` |
| Intraday trades | `ReceiveDate` plus `Id` overlap | Re-read a short overlap window and upsert by source `Id` for late arrivals |
| Daily trades | `Id` primary key plus bounded historical backfill | Retain `TradeDateTime`; source has one duplicate natural `(InstrumentRef, TradeDate)` key |
| Intraday indices | `ChangeTime` plus `Id` overlap | Upsert by source `Id` |
| Historical daily indices | bounded date-range paging from `IndexNew2` | One-time/backfill workflow |

The overlap strategy is required because source rows arrive late in some periods. While a
bounded page is full, persist a timestamp plus source-id continuation cursor so dense timestamps
drain deterministically. After a short page completes the cycle, clear the continuation cursor
and start the next cycle from the timestamp overlap window.

## Source Index Recommendations

The inspected source tables only expose primary keys, except for unique
`Tse.Instrument.InsCode`. If the source database owner permits non-invasive read-performance
indexes, add:

```sql
CREATE INDEX IX_Trade_ReceiveDate_Id
ON Tse.Trade (ReceiveDate, Id);

CREATE INDEX IX_IndexB1LastDay_ChangeTime_Id
ON Tse.IndexB1LastDay (ChangeTime, Id);

CREATE INDEX IX_Instrument_ChangeTime_Id
ON Tse.Instrument (ChangeTime, Id);

CREATE INDEX IX_InstTrade_TradeDateTime_Id
ON Tse.InstTrade (TradeDateTime, Id);
```

These indexes are recommendations only. The FinancialCopilot adapter remains read-only and
must not create or alter objects in `StockMarketDB`.

## Operational Shape

Implement a separate `StockMarketDb` provider adapter. It may share SQL resilience patterns
with CodalDB but must use its own options, connection factory, query executor, payload
normalizers, and watermarks.

Recommended flow:

```text
StockMarketDb polling worker
-> read bounded source pages with overlap watermark
-> serialize canonical ProviderRawPayload + SHA-256
-> normalize/upsert PostgreSQL rows
-> advance dataset watermark after successful page
-> refresh LatestMarketQuotes projection
-> invalidate scanner/market-summary cache
```

Do not enqueue every one-minute poll through the existing per-company CodalDB full-sync
fan-out. Trading statistics are time-series ingestion workloads with different cadence,
retention, partitioning, and failure-recovery requirements.

## Initial Warm-Up

Before enabling the polling worker, call the instrument sync endpoint incrementally until a
page returns fewer rows than `StockMarketDb:PageSize`. The source instrument dimension is larger
than one bounded page. Time-series sync rejects a page containing unresolved instrument
references and leaves its watermark unchanged, so it can be retried after dimension warm-up
without losing early observations.
