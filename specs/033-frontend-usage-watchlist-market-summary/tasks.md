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
10. Replace the context panel's market-wide top-movers list with the user's
    followed-symbol/watchlist quote list, with vertical scrolling after six visible symbols.

11. Add a sidebar refresh icon next to the AI-credit label that refetches `GET /api/v1/usage/me`,
    with in-flight disabled/spinner state and an accessible label.

12. Extend `GET /api/v1/usage/me` with nullable `planCode`, `planName`, `planIncludedCredits` via
    `ISubscriptionPlanRepository`, with an integration test.
13. Add a pure, unit-tested credit-progress helper (percentage clamp, colour thresholds, unknown-plan
    handling) and use it in the sidebar card (`X از Y` label, plan-relative bar, semantic colours).

## Implementation Status

Completed on 2026-06-02. The sidebar and context panel now use backend-owned usage,
watchlist, quote, and market-summary reads. Unsupported analytics remain explicitly
unavailable until normalized sources are added.

2026-07-22 update: The chat context panel no longer displays market-wide top movers.
It displays the actor's followed-symbol watchlist with enriched latest price/change data.
`GET /api/v1/watchlists/me` prefers current `FollowedSymbols` rows when present and falls
back to legacy `WatchlistSymbols` rows for backward compatibility. The context-panel
watchlist list is vertically scrollable after six visible symbols.

2026-10-07 update: the sidebar AI-credit card gained a manual refresh icon that refetches
`/api/v1/usage/me` so `availableSpendingCapacity` can be refreshed on demand (task 11).

2026-10-07 update: the sidebar credit bar is plan-relative (tasks 12-13); plan limits come from the
Billing plan catalog, not frontend constants.
