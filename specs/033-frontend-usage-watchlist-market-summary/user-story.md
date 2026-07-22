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

## Out Of Scope

- Portfolio valuation.
- User-facing watchlist editing controls in the first UI patch.
- Inventing money-flow or industry analytics without a normalized source.
