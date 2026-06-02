# Tasks

1. Add actor-scoped watchlist contracts, persistence rows, EF configuration, and migration.
2. Implement watchlist query/update services with symbol validation and batched quote resolution.
3. Add `GET /api/v1/watchlists/me` and `PUT /api/v1/watchlists/me`.
4. Add a market-summary application service over normalized index and latest-quote projections.
5. Add `GET /api/v1/market/summary` with explicit nullable unsupported fields and `asOf`.
6. Connect sidebar usage to `GET /api/v1/usage/me`.
7. Replace `STOCK_DB` sidebar quote reads and `MARKET_SNAPSHOT` context-panel reads.
8. Add loading, empty, stale, and error presentation states.
9. Add migration, authorization, fallback, cache, and frontend lint/build verification.

## Implementation Status

Completed on 2026-06-02. The sidebar and context panel now use backend-owned usage,
watchlist, quote, and market-summary reads. Unsupported analytics remain explicitly
unavailable until normalized sources are added.

