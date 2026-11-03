# Frontend Usage, Watchlist, And Market Summary Integration

## User Story

As a web user, I want the sidebar and market context panel to display backend-owned usage,
watchlist quotes, and market summary data so the UI no longer presents canned financial values
or locally maintained credits.

## Current Gap

The backend exposes `GET /api/v1/usage/me`, but the frontend still reads a Supabase
`user_subscriptions` row. Watchlist symbols and context-panel values are entirely Supabase/mock
driven. StockMarketDB ingestion now persists `LatestMarketQuotes` and daily/intraday index data,
but no web-facing watchlist or market-summary API exists.

## Scope

- Connect the existing usage endpoint to the sidebar.
- Add authoritative watchlist persistence and enriched quote reads.
- Add a market-summary read model and endpoint backed by normalized PostgreSQL projections.
- Replace `STOCK_DB` sidebar lookups and `MARKET_SNAPSHOT` context-panel imports.
- Show the user's followed-symbol watchlist, not market-wide top movers, in the main chat
  context panel next to the chatbot.
- Return explicit unavailable fields when current normalized data does not support a widget.

## Acceptance Criteria

1. Sidebar credits come from `GET /api/v1/usage/me`; the frontend never mutates balances.
2. `GET /api/v1/watchlists/me` returns actor-scoped symbols plus batched latest quote metadata.
3. `PUT /api/v1/watchlists/me` validates symbol limits and persists actor-scoped watchlist
   changes for future editing UI.
4. `GET /api/v1/market/summary` returns available index observations, top movers, and `asOf`
   timestamps from normalized PostgreSQL reads.
5. Unsupported market fields such as real-money flow or industry trends are nullable or omitted
   until a governed source is ingested; no fabricated values are returned.
6. Sidebar and context panel show loading, empty, stale, and error states.
7. Cache invalidation follows StockMarketDB projection updates.
8. The main chat context panel renders watchlist/followed symbols with latest price and
   change percentage; when more than six symbols are present, the symbol list scrolls
   vertically instead of expanding the whole panel.
9. Tenant isolation, quote fallback, unavailable fields, and frontend lint/build checks pass.
10. The sidebar "اعتبار هوش مصنوعی" label has an adjacent, proportionally sized refresh icon button.
    Activating it re-reads `GET /api/v1/usage/me` so `availableSpendingCapacity` reflects the
    current database state. The control is read-only (never mutates balances), is disabled with a
    visible spinning state while a refresh is in flight (preventing duplicate requests), keeps the
    last known value visible during refresh, and has an accessible Persian label.
11. `GET /api/v1/usage/me` also returns the account's current plan (`planCode`, `planName`) and the
    plan's `planIncludedCredits`, read from Billing's `SubscriptionPlans` through the existing
    `ISubscriptionPlanRepository`. These are nullable when the account has no resolvable plan. The
    frontend never hard-codes plan allowances; the admin-managed plan catalog is the single source.
12. The sidebar AI-credit card shows `<remaining> از <planIncludedCredits>` (Persian digits) and a
    progress bar whose width is `availableSpendingCapacity / planIncludedCredits * 100`, clamped to
    0-100 (e.g. 18/25 = 72%, 18/300 = 6%, 18/1000 = 1.8%). Bar colour follows the remaining
    percentage: above 50% emerald, 20-50% amber, below 20% rose, using existing theme tokens.
13. Edge cases never crash or show invalid progress: negative remaining shows 0%, remaining above
    the limit shows 100%, and a missing/zero/unknown plan limit shows only the remaining value with
    an empty neutral bar (no percentage).
14. The wallet is a single balance (initial balance = plan included credits; purchases add to the
    same balance). No separate "extra credit" concept exists, so none is displayed. The refresh icon
    from criterion 10 is retained because balances change after each AI request and the query does
    not refetch automatically; it is a read-only refresh, not a purchase/recharge action.

## Out Of Scope

- Portfolio valuation.
- User-facing watchlist editing controls in the first UI patch.
- Inventing money-flow or industry analytics without a normalized source.
