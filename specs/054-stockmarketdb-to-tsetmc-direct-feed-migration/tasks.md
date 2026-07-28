# Tasks - StockMarketDB to Direct TSETMC Feed Migration

## Phase 1 - Bridge Stabilization

- Keep existing `030-stockmarketdb-trading-statistics-sync` as the current bridge implementation.
- Mark `StockMarketDB` as `MigrationBridge`, not archive.
- Persist provenance for all market statistics loaded from StockMarketDB.
- Add DataAdmin health and run history for StockMarketDB polling.

## Phase 2 - Direct TSETMC Provider Foundation

- Add `TsetmcWebService` provider adapter for ASMX/web-service calls.
- Externalize credentials/endpoints/timeouts.
- Normalize TSETMC instruments, trades, daily market stats, and index data into existing canonical projections.
- Add bounded polling, overlap windows, retry, timeout, and telemetry.

## Phase 3 - Parallel Validation

- Run direct TSETMC ingestion in shadow mode.
- Compare latest price, close price, price change percent, volume, value, trade count, and index values against StockMarketDB-derived values.
- Persist mismatch reports.
- Expose mismatch summary in DataAdmin.

## Phase 4 - Cutover

- Add configuration to switch market quote source priority from `StockMarketDB` to `TsetmcWebService`.
  **Important:** `PrimarySourceName` controls which provider writes to the projection and which sync
  workers are active. It must not be used as a `WHERE ProviderName = ...` filter in the runtime
  quote resolver. The resolver must read the best available row for the resolved `TradingInstrumentId`
  regardless of which provider populated it. If a filter exists in `PersistedMarketDataProvider` or
  any quote resolver, it must be removed before or during cutover.
- Keep rollback to StockMarketDB until direct feed proves stable.
- Disable StockMarketDB polling after cutover.
- Update docs and operational runbooks.
- Verify that after cutover, quote rows previously written under `ProviderName = StockMarketDb`
  are still resolved correctly by the runtime quote path (no `Missing` cells due to provider mismatch).

## Known Architectural Debt (tracked in spec 064)

The following issues were discovered post-cutover and are tracked in
[spec 064 - Trading Instrument Unification](../064-trading-instrument-unification/user-story.md):

### TradingInstruments is not provider-neutral

`TradingInstruments` is a dimension table but is currently partitioned by `ProviderName`.
Both `StockMarketDbSyncService` and `TsetmcDirectFeedSyncService` insert rows with their own
`ProviderName`, producing duplicate rows for the same physical instrument.

`InstrumentMapByInsCodeAsync` in `TsetmcDirectFeedSyncService` filters by
`ProviderName == "TsetmcWebService"`, so on any day before `SynchronizeInstrumentsAsync`
(EOD only) has run, the map is **empty** and every call to `PersistIntradayTradesAsync` silently
skips all records, resulting in zero rows in `IntradayTradeSnapshots`.

**Required fix (Phase 1 of spec 064):**
1. Remove the `ProviderName` filter from `InstrumentMapByInsCodeAsync`.
2. Auto-create minimal stub instrument rows for unseen InsCodes during intraday persistence.
3. Remove instrument-write logic from `StockMarketDbSyncService`.

### Noavaran Amin cross-source linkage via tseCode

Noavaran Amin company records expose `tseCode` (= TSETMC `InsCode`), `tseCIsinCode`, and
`tseSIsinCode`. These are not stored on `NormalizedCompanyRow` and are not used when linking
companies to instruments. Linkage currently relies on `InstrumentCode` string matching.

**Required fix (Phase 2 of spec 064):**
1. Add `TseCode`, `TseCIsinCode`, `TseSIsinCode` columns to `NormalizedCompanyRow`.
2. Populate from Noavaran Amin company catalog in `NadpcoApiCompanyNormalizer`.
3. Use `TseCode` as the primary join key in `TsetmcDirectFeedSyncService.PersistInstrumentsAsync`.
