# Revised Design Proposal: Market Capitalization in Followed Symbols

Status: Proposed — renewed Gate A approval required

## Why the Approved Design Must Change

The approved design depended on an existing, persisted `MARKET_CAP` source. A read-only query of the local development database found no `DerivedMetrics` rows with `MetricCode = MARKET_CAP`. The application code also has no active producer that guarantees this metric. The watchlist quote provider supplies price and change only.

The existing TSETMC instrument feed already parses `TotalShares` from its instrument payload, but the sync service does not persist or use that value. Persisting share count and calculating market capitalization would therefore add data-ingestion behavior and a database migration, which were explicitly outside the approved design and Gate B plan.

## Recommended Revised Design

1. Persist TSETMC `TotalShares` on the existing trading-instrument record during instrument synchronization, with the synchronization timestamp available as freshness evidence.
2. Resolve the followed symbol's current share count alongside its latest quote in a batched backend read.
3. Calculate market capitalization from the latest price and total shares. Convert the result to billion tomans at one documented backend boundary. For a Rial-denominated price and a share count, the conversion is `latestPrice × totalShares ÷ 10,000,000,000`.
4. Return the nullable calculated value and its price/share observation times from the watchlist API.
5. Render «ارزش بازار» in «میلیارد تومان» with Persian digits on each followed-symbol card. Show «—» when either input is missing or fails the approved freshness rule.

This reuses the existing TSETMC instrument sync and latest quote path; it does not add a new external provider. It does add a nullable database column and migration, plus logic to keep the stored share count synchronized.

## Acceptance Criteria

- Instrument synchronization persists the TSETMC total share count and records when that value was synchronized.
- Market capitalization is computed only when both latest price and total shares are present and eligible under the approved freshness policy.
- A known Rial price and share count produce the expected billion-toman value without rounding the underlying calculation prematurely.
- Missing or stale inputs produce an unavailable marker, never a guessed value.
- The watchlist API and page expose the value while preserving existing quote, actor-scoping, and batching behavior.

## Decision Needed

Approve this revised source and calculation: TSETMC total shares × the latest Rial quote, converted to billion tomans. If the owner requires an exchange-provided market-cap value instead, the design needs a provider/source that supplies and timestamps that value.

## Risks and Dependencies

- The TSETMC share count must represent total issued shares, and the quote unit must be verified at the sync boundary before applying the conversion.
- Share count may update less often than price; the response must retain separate observation times and apply a freshness rule agreed at Gate A.
- The revised scope adds a database migration and changes to TSETMC ingestion, so a new implementation plan and Gate B approval will be required after renewed Gate A approval.

## Out of Scope

- Portfolio value, profit/loss, or investment analysis.
- A new market-data provider or a database table dedicated to market-cap history.
