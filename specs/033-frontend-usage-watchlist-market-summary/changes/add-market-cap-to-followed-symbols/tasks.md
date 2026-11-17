# Implementation Plan: Market Capitalization in Followed Symbols

Status: Gate B: Approved by owner on 2026-11-16 — implementation paused; approved source assumption failed, renewed Gate A required

## Scope

Add each followed symbol's latest available market capitalization to its card on the followed-symbols management page, formatted as billion tomans. The backend watchlist response will expose the converted value and observation time; unavailable or unconvertible values remain nullable and render as «—».

## Discovery Findings

- The watchlist already resolves the actor's symbols and quotes in one backend service (`WatchlistService.BuildViewAsync`).
- The current `WatchlistQuote` contract has no market-cap field. Its quote provider returns latest price/change, not market capitalization.
- The scanner reads `MARKET_CAP` from persisted `DerivedMetrics`; `DerivedMetricRow` carries `Value`, `Unit`, `ObservedAt`, and provenance. This is the candidate batched source for the watchlist.
- The semantic catalog describes the metric unit only as `amount`, and scanner fixtures use the generic unit `Amount`. Neither proves whether values are Rial or Toman. The application also has no evident producer that guarantees this unit for every stored row.
- Therefore, source availability and an explicit currency mapping are a mandatory precondition. If the persisted rows cannot be reliably identified as Rial or Toman, stop before implementation and return to Gate A with a revised design. Do not guess a conversion or fall back to `MarketSnapshot.MarketCapitalization`, which the watchlist path does not use.

## Planned Files and Changes

1. `src/backend/FinancialCopilot.Application/FinancialData/MarketViews/MarketViewContracts.cs`
   - Add nullable `MarketCapBillionToman` and `MarketCapAsOf` to `WatchlistQuote`.
2. `src/backend/FinancialCopilot.Infrastructure/Financial/MarketViews/MarketViewServices.cs`
   - Extend `WatchlistService.BuildViewAsync` to resolve the latest eligible `MARKET_CAP` row for all followed companies in one query, keyed to the same company/ticker resolution used by the watchlist.
   - Accept only rows with a verified source unit and valid value; convert Rial by dividing by 10,000,000,000 or Toman by dividing by 1,000,000,000. Preserve `ObservedAt` in the response.
   - Keep missing/unrecognized-unit values null. Add no migration or new ingestion path in this change.
3. `src/frontend/src/lib/market-view.functions.ts`
   - Add the nullable response fields to the `WatchlistQuote` type.
4. `src/frontend/src/routes/_app/followed-symbols.tsx`
   - Render «ارزش بازار» with Persian-formatted digits and «میلیارد تومان» in each followed-symbol card; render «—» when the value is null.
5. `tests/FinancialCopilot.IntegrationTests/MarketViewEndpointTests.cs`
   - Extend the existing watchlist read coverage for populated, missing, unrecognized-unit, and stale market-cap observations while retaining actor scoping and price/change behavior.
6. Add focused backend and frontend tests for the unit conversion and the nullable display state in their existing test projects.

No database migration, new endpoint, new ingestion job, or changes to the context-panel presentation are planned.

## Ordered Tasks

1. Before changing production code, inspect representative persisted `MARKET_CAP` rows and their ingestion/provenance mapping to prove the source unit and whether the rows are current enough for this card. If no reliable source/unit mapping exists, stop and request revised design approval.
2. Add backend response fields and a batched latest-metric lookup scoped to followed company IDs, preserving the metric observation timestamp.
3. Implement the verified Rial/Toman conversion and null behavior for missing, invalid, or unsupported units.
4. Add the frontend response type and render the value and unavailable marker in followed-symbol cards.
5. Add the planned focused endpoint, unit-conversion, and UI-state tests; verify the backend and frontend changes using the repository's relevant checks.

## Acceptance Criteria

- A symbol with a fresh `MARKET_CAP` observation in a verified Rial or Toman unit shows the correct value in billion tomans.
- Missing, stale, invalid, or unsupported-unit observations do not show a fabricated or misconverted value.
- Market-cap reads are batched for the watchlist; tenant/actor scoping and existing quote resolution remain intact.
- The management page labels the value in Persian and uses Persian digits; existing price and change display remains intact.
- No migration or new ingestion path is added unless a revised design is approved.

## Risks and Blockers

- The source unit and production availability of `MARKET_CAP` rows are not established by the checked-in metric definition or current quote-provider contract. This must be resolved in task 1 before production changes.
- Market-cap freshness may differ from quote freshness. Keep its own `MarketCapAsOf`; do not imply it shares the quote timestamp.
- Existing scanner behavior proves the application can read persisted metric rows but does not prove that current production data has a complete or correctly unit-tagged market-cap feed.

## Implementation Status

Paused before production changes on 2026-11-16. A read-only query of the local development database found no `MARKET_CAP` rows, and the repository has no active producer that guarantees them. The approved plan excludes new ingestion and migrations. See `design-proposal-revision.md`; renewed Gate A approval and a replacement Gate B plan are required before implementation.
