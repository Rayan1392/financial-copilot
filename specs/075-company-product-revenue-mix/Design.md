# Feature 075 — Deterministic Product-Revenue-Mix Follow-ups

## Scope decision

Feature 075 owns `product_revenue_mix`, `ProductRevenueMixResponse`, its ranked product rows, and
the displayed mix table. Feature 128 owns semantic routing and is not expanded. Features 137 and
138 supply the reusable `SuggestedAction`, registry-versioned stable-ID, in-memory round-trip, and
`DeterministicSuggestionsApplied` persistence conventions.

This extension adds navigation to existing capabilities; it does not add multi-month mix analytics
or change Feature 075 calculations, so the existing Feature 075 specification remains the owner.

## Applicability and ownership

Apply only to a successful V2 response with a non-null typed `ProductRevenueMixResponse` and an
`Answered` or `PartialAnswer` dialogue outcome. When applied, the deterministic collection is
authoritative even when empty. Do not apply to clarification, not-found/no-data, ambiguity,
unsupported, or failure paths. V1 remains unchanged.

## Product eligibility and ranking reuse

- Traverse `ProductRevenueMixResponse.Products` in its existing displayed order. The repository
  already orders this collection by `ProductRank`; the selector introduces no second ranking.
- Use the returned canonical `ProductName`, `SalesAmount`, share, and rank. Do not perform another
  read to rediscover products.
- The current typed row has no stable vendor product code. Its normalized canonical product title
  is therefore the smallest safe in-result identity. All rows sharing one normalized title are
  ambiguous and are excluded rather than guessed.
- Reject empty, over-length, generic “other”, aggregate/total, and synthetic placeholder titles.
- A parser-unsafe row is skipped and traversal continues until two valid product actions are found
  or the bounded result is exhausted.
- Service/non-product results yield no product actions unless they contain actual eligible product
  rows.

## In-memory round-trip validation

For each product candidate, construct `روند فروش {title} {symbol}` and require:

- `MonthlyProductTrendIntentRules` accepts the query;
- `BuildQuery` returns the same normalized company symbol and product title;
- the capability interpreter selects the existing product-sales trend route with no missing slots;
- the interpreter's company entity equals the canonical symbol.

The action itself uses capability code `monthly_product_trend`, matching Features 136/137 and the
existing typed result discriminator. No downstream use case is executed.

For the company candidate, construct `روند فروش ماهانه {symbol}` and require the capability to be
enabled, `MonthlyActivityTrendIntentRules` to accept/extract the same symbol, and the interpreter to
select `monthly_activity_trend` with no missing slots.

## Action construction

Product actions use `RunRelatedCapability`, identical label/message, preset slots `company`,
`symbol`, and `product`, relevance reason `product_revenue_mix_follow_up`, and the current registry
version. The company action uses `symbol` and `company` slots with the same relevance reason.

IDs use the existing SHA-256 convention over the feature/policy identifier, target capability,
registry version, canonical symbol, and normalized canonical product identity (when present). Raw
user text is never included. The maximum is two product actions followed by one company action.
The mix capability itself and semantic aliases of it are never suggested.

## Persistence and transport

The selector feeds the existing workflow `SuggestedActions` collection. The generalized
`DeterministicSuggestionsApplied` marker from Feature 138 causes `MessagePersistenceFunction` to
persist the exact non-empty or empty collection and skip `CapabilityGuidanceService`. Existing API,
web, and Telegram mappings/renderers are reused without a new DTO or persistence path.

## Tests

Focused tests cover four products, one product, parser-unsafe leading rows, duplicate titles, no
queryable products, unavailable company trend, stable order/IDs, registry-version ID variation,
exact slots, displayed-rank parity, self-loop exclusion, and product/company parser/interpreter
round trips. Persistence, Features 136/137/138, routing, architecture, Telegram, frontend action
rendering/clicking, and an API round-trip fixture remain regression gates.
