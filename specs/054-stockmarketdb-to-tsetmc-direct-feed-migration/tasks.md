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
- Keep rollback to StockMarketDB until direct feed proves stable.
- Disable StockMarketDB polling after cutover.
- Update docs and operational runbooks.
