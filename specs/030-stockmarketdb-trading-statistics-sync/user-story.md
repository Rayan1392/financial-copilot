# StockMarketDB Trading Statistics Synchronization

## User Story

As a FinancialCopilot operator, I want registered instruments, stock trading statistics, and
market indices synchronized from the read-only `StockMarketDB` SQL Server source into
PostgreSQL so scanner answers and market summaries can use fresh market values with auditable
fallbacks.

## Scope

- Add a dedicated `StockMarketDb` read-only provider adapter.
- Normalize the following source datasets (authoritative mapping):
  - Daily indices (شاخص روزانه): `Tse.IndexNew`, scoped by named-index `InstrumentRef`.
  - Intraday / in-day indices (شاخص لحظه‌ای - بین‌روز): `tse.IndexB1LastDay`.
  - Daily trades (آمار معاملات روزانه): `TSE.TradeRefined` (`TradeOneDay`) — one price per
    trading day per instrument.
  - Intraday / in-day trades (آمار معاملات لحظه‌ای - بین‌روز): `tse.Trade`.
  - Instruments: `Tse.Instrument`.
- Resolve named market indices in `Tse.IndexNew` by `InstrumentRef`:
  - شاخص کل — `36423CB8-D33B-47AD-89D4-06FA49592CBA`
  - شاخص کل فرابورس — `1B32B991-F48A-4F7E-9C0C-328D0B093EA5`
  - شاخص بازده نقدی و قیمت — `B27FA320-194F-4710-8D12-277E245D33C5`
  - شاخص ۵۰ شرکت فعال‌تر — `47CE7543-C052-4C44-BF0D-29281818FCA5`
  - شاخص قیمت (هم‌وزن) — `42FCE63E-6CEB-405B-9179-78606C210D86`
  - شاخص کل (هم‌وزن) — `D01F9D84-A1C8-46F3-A959-800DEF9E112F`
- Link instruments to existing normalized companies through `InsCode -> InstrumentCode`.
- Persist append-oriented history and a compact latest-quote projection.
- Add bounded, overlap-watermark polling workers with dataset-specific cadence.
- Support both **full-sync** (bounded historical backfill) and **incremental sync**
  (overlap-watermark forward sync) for every dataset above.

## Acceptance Criteria

1. Secrets remain external to source control and SQL access is read-only.
2. Trading instruments persist independently from companies and optionally link to an existing
   normalized company by TSETMC instrument code.
3. Intraday trades (`tse.Trade`), daily trades (`TSE.TradeRefined`), and intraday indices
   (`tse.IndexB1LastDay`) are idempotent on source identifiers.
4. Daily index rows persist from `Tse.IndexNew`, scoped by the named-index `InstrumentRef`
   values listed in Scope.
5. Every dataset supports a bounded full-sync (historical backfill) and an incremental
   overlap-watermark forward sync; the two paths share the same idempotent normalizers.
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

