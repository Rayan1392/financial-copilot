# Trading Instrument Unification

## User Story

As a data platform owner, I want `TradingInstruments` to be a single provider-neutral dimension
table owned exclusively by the TSETMC feed, so that intraday trades, daily trades, and index
snapshots never fail to persist because the instrument was ingested by a different provider, and
Noavaran Amin company data is linked to market instruments by their shared TSETMC codes rather
than by provider name.

## Business Context

`TradingInstruments` is a **dimension** — it describes *what* instrument is being traded.
Dimension tables should not be partitioned by data provider. Currently:

- `StockMarketDbSyncService` inserts `TradingInstrumentRow` rows with `ProviderName = "StockMarketDb"`.
- `TsetmcDirectFeedSyncService` inserts `TradingInstrumentRow` rows with `ProviderName = "TsetmcWebService"`.
- Both `PersistIntradayTradesAsync` and `PersistDailyTradesAsync` in `TsetmcDirectFeedSyncService`
  look up instruments filtered by `ProviderName == "TsetmcWebService"` via `InstrumentMapByInsCodeAsync`.
  If the instruments sync has not yet run for the day, this map is empty and **every trade record
  is silently skipped**, resulting in zero rows in `IntradayTradeSnapshots`.

The TSETMC `InsCode` (field `InsCode` / `insCode` in every ASMX response) is the canonical, stable,
globally unique identifier for a listed instrument at the Tehran exchange. It is the only identifier
that:
- is present in every TSETMC response (trades, indices, instruments),
- is also exposed by Noavaran Amin company data as `tseCode` / `tseCIsinCode` / `tseSIsinCode`,
- and therefore can serve as the single join key across all sources.

There is no valid reason to have two `TradingInstrumentRow` rows for the same `InsCode`.

## Acceptance Criteria

1. `TradingInstruments` is a single provider-neutral table — `ProviderName` column is either
   removed or kept only as an audit/origin stamp with no functional filter applied when resolving
   instrument ids for trade or index persistence.
2. `TsetmcDirectFeedSyncService` is the only service that creates and updates `TradingInstrumentRow`
   records. `StockMarketDbSyncService` must not upsert instrument rows; it resolves instruments
   by `InstrumentCode` from the shared table.
3. Intraday trade persistence never skips records because an instrument sync has not yet run.
   `SynchronizeIntradayTradesAsync` auto-creates a minimal instrument stub row (InsCode, Symbol)
   for any unseen `InsCode` so trades are not lost.
4. Noavaran Amin company catalog records are linked to `TradingInstrumentRow` records via
   `tseCode` (maps to `InsCode`), `tseCIsinCode`, or `tseSIsinCode` — whichever is populated —
   without depending on provider name matching.
5. `PersistedMarketDataProvider` and `TsetmcValidationService` both resolve the correct instrument
   regardless of which provider originally inserted the row.
6. No duplicate `TradingInstrumentRow` records exist for the same `InsCode`. A unique constraint
   on `InstrumentCode` (after removing the provider partition) is enforced at the database level.
7. Architecture test: no production code outside `TsetmcDirectFeedSyncService` writes to the
   `TradingInstruments` table directly.

## Out of Scope

- Changing the index snapshot or market quote projection logic beyond the instrument-resolution fix.
- Migrating historical duplicate rows (a one-off cleanup script is acceptable as a deployment note,
  not a code requirement).
- Adding new Noavaran Amin fields to `TradingInstruments`.
