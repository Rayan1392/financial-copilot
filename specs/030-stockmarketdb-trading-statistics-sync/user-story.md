# StockMarketDB Trading Statistics Synchronization

## User Story

As a FinancialCopilot operator, I want registered instruments, stock trading statistics, and
market indices synchronized from the read-only `StockMarketDB` SQL Server source into
PostgreSQL so scanner answers and market summaries can use fresh market values with auditable
fallbacks.

## Scope

- Add a dedicated `StockMarketDb` read-only provider adapter.
- Normalize `Tse.Instrument`, `Tse.Trade`, `Tse.InstTrade`, and `Tse.IndexB1LastDay`.
- Backfill historical daily indices from `Tse.IndexNew2` only; do not treat stale
  `Tse.IndexNew` or `Tse.IndexNew2` as current feeds.
- Link instruments to existing normalized companies through `InsCode -> InstrumentCode`.
- Persist append-oriented history and a compact latest-quote projection.
- Add bounded, overlap-watermark polling workers with dataset-specific cadence.

## Acceptance Criteria

1. Secrets remain external to source control and SQL access is read-only.
2. Trading instruments persist independently from companies and optionally link to an existing
   normalized company by TSETMC instrument code.
3. Intraday trades, daily trades, and intraday indices are idempotent on source identifiers.
4. Current daily index rows derive from the last intraday index snapshot per trading day.
5. Historical daily index backfill uses `Tse.IndexNew2` and is explicitly separated from the
   current feed.
6. Polling uses bounded pages, overlap watermarks, retry/failure isolation, and telemetry.
7. `LatestMarketQuotes` exposes latest price, price-change percentage, source kind, and as-of
   timestamp with daily fallback when an intraday observation is unavailable.
8. Scanner and market-summary caches invalidate after successful projection updates.
9. Unit, integration, architecture, and migration tests pass.

## Out Of Scope

- Direct writes or DDL against `StockMarketDB`.
- Order-book depth, individual transaction tape, or portfolio valuation.
- Treating every registered instrument as a company.
- Replacing normalized PostgreSQL reads with query-time SQL Server access.

