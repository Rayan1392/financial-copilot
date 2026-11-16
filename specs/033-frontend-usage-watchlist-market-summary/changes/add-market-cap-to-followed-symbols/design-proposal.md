# Add Market Capitalization to Followed Symbols

Status: Gate A: Approved by owner on 2026-11-16

## Goal

Show each followed symbol's market capitalization in the symbol card on the followed-symbols page, formatted in billion tomans.

## Discovery

- The followed-symbols page reads the shared watchlist quote query and currently renders latest price and daily change.
- The current backend `WatchlistQuote` response contains price, change percentage, observation time, source, and stale state; it has no market-capitalization field.
- The persisted latest-quote row and quote observation also contain price and change only.
- A domain `MarketSnapshot` has an optional `MarketCapitalization` property, but the watchlist quote path does not currently read it.
- The governed `MARKET_CAP` metric exists, but its semantic catalog entry labels its unit only as `amount`; it does not establish the source currency or a ready-to-use watchlist read path.
- The existing watchlist feature specification explicitly limits its enriched quote data to latest price and change. This is a scoped change under feature 033.

## Proposed Design

1. Extend the backend watchlist read model with a nullable market-capitalization value for each symbol, resolved in the same batched read as the watchlist data.
2. Use an authoritative persisted market-capitalization source. Confirm its currency and freshness semantics before conversion; do not calculate or infer a value from unrelated fields.
3. Convert to billion tomans in the backend response (or at a single explicitly documented boundary) and expose a clearly named nullable field such as `MarketCapBillionToman`.
4. Render the value in the existing symbol card with Persian digits and the label «میلیارد تومان». If no valid value is available, show a neutral unavailable marker rather than a fabricated value.
5. Preserve existing price/change behavior and watchlist batching. No schema migration is expected if a suitable persisted source already exists; if it does not, stop and revise the design before adding ingestion or new storage.

## Acceptance Criteria

- Each followed-symbol card shows the market capitalization in billion tomans when a valid source value is available.
- Missing or stale values follow the agreed availability policy and are never presented as current without evidence.
- Currency conversion is verified against the source unit and covered by focused tests.
- The watchlist remains a batched read and existing quote values and UI states continue to work.

## Open Question

- Which existing persisted market-capitalization source is authoritative for the watchlist, and what currency/unit does it store? Resolve this during the implementation-plan investigation; if no suitable source exists, return with a revised design rather than silently introducing a new ingestion path.

## Dependencies and Risks

- Backend watchlist contract/service and frontend watchlist type/rendering must change together.
- The main risk is a unit mismatch between Rial and Toman; conversion must not be implemented until the source unit is verified.
- The displayed market cap may have a different observation time from the latest price. Its timestamp/freshness must remain available or be reflected consistently in the watchlist response.

## Out of Scope

- Portfolio value or profit/loss calculations.
- New market-data ingestion, schema changes, or market-cap estimation unless a revised design is approved.
