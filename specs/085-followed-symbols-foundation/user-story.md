# User Story — Followed Symbols Foundation

## Status
`[ ]` Proposed

## Feature
A lightweight user-specific followed-symbols capability that enables future personalization without requiring full portfolio modeling.

## Story

As a TahlilApp-AI user,

I want to follow and unfollow symbols I care about,

so that future AI feeds and product surfaces can prioritize events related to those symbols.

## Business Context

The product currently does not have a practical watchlist. Before implementing Watchlist AI or portfolio intelligence, the system needs a minimal user-specific symbol universe.

This feature intentionally does not model holdings, quantity, average cost, realized/unrealized profit, or portfolio risk. It is not a portfolio feature.

## In Scope

- Follow a company/symbol.
- Unfollow a company/symbol.
- List my followed symbols.
- Replace my followed-symbol set.
- Prevent duplicates.
- Attach followed symbols to authenticated actor/user context.
- Add UI entry points from symbol pages, AI answer cards, and insight cards.

## Out of Scope

- Portfolio holdings.
- Transaction history.
- Position weight.
- Cost basis.
- Profit/loss.
- Risk analytics.
- Push notifications.

## Acceptance Criteria

1. Authenticated users can follow a symbol by resolved company identifier.
2. Authenticated users can unfollow a symbol.
3. Authenticated users can retrieve their followed-symbol list.
4. The same actor cannot follow the same company twice.
5. Followed symbols are stored against actor/user identity and tenant context where applicable.
6. Followed symbols must reference canonical company identity, not raw user-entered text.
7. The system must not infer financial exposure from followed symbols.
8. The API returns symbol, company name, external company id, and followed timestamp.
9. Future insight feed features can query followed symbols efficiently.
10. UI labels must distinguish "followed symbols" from "portfolio".

## API Proposal

```http
GET    /api/v1/followed-symbols/me
POST   /api/v1/followed-symbols/me/{externalCompanyId}
DELETE /api/v1/followed-symbols/me/{externalCompanyId}
PUT    /api/v1/followed-symbols/me
```

## Data Model Proposal

```csharp
FollowedSymbol
{
    Guid Id;
    Guid ActorId;
    string ExternalCompanyId;
    string Symbol;
    DateTime FollowedAtUtc;
    string? Source;
}
```
