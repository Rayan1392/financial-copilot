# Tasks — Followed Symbols Foundation

## 1. Domain and Contracts

- [x] Add `FollowedSymbol` entity/read model.
- [x] Add `FollowedSymbolDto`.
- [x] Add `GetMyFollowedSymbolsQuery`.
- [x] Add `FollowSymbolCommand`.
- [x] Add `UnfollowSymbolCommand`.
- [x] Add `ReplaceMyFollowedSymbolsCommand`.
- [x] Add `IFollowedSymbolRepository` abstraction.

## 2. Persistence

- [x] Create migration for `FollowedSymbols`.
- [x] Add unique index on `(ActorId, ExternalCompanyId)`.
- [x] Add index on `ActorId` for fast retrieval.
- [x] Store canonical `ExternalCompanyId` and display symbol.
- [x] Preserve `FollowedAtUtc`.

## 3. Company Resolution

- [x] Reuse existing company/symbol resolution path.
- [x] Reject unknown symbols with a clear validation response.
- [x] Ensure followed symbols reference canonical company identity.
- [x] Avoid persisting raw unresolved user-entered symbol text.

## 4. Application Use Cases

- [x] Implement `GetMyFollowedSymbolsUseCase`.
- [x] Implement `FollowSymbolUseCase`.
- [x] Implement `UnfollowSymbolUseCase`.
- [x] Implement `ReplaceMyFollowedSymbolsUseCase`.
- [x] Ensure idempotent follow behavior where appropriate.

## 5. API

- [x] Add `GET /api/v1/followed-symbols/me`.
- [x] Add `POST /api/v1/followed-symbols/me/{externalCompanyId}`.
- [x] Add `DELETE /api/v1/followed-symbols/me/{externalCompanyId}`.
- [x] Add `PUT /api/v1/followed-symbols/me` for bulk replacement.
- [x] Return stable response shape for frontend use.

## 6. Frontend UX

- [x] Add "Follow symbol" / "Unfollow" action on symbol pages.
- [x] Add follow action on AI symbol answer cards.
- [x] Add follow action on market insight cards.
- [x] Add simple followed-symbols management view.
- [x] Avoid portfolio wording in UI.

## 7. Tests

- [x] Unit-test duplicate prevention.
- [x] Unit-test follow/unfollow idempotency.
- [x] Integration-test actor isolation.
- [x] Integration-test unknown symbol validation.
- [x] Integration-test list ordering and response shape.

## Implementation Notes

- Implemented the followed-symbols bounded capability with a `FollowedSymbol` domain model, application use-case ports, EF-backed repository, and canonical company resolver over normalized `Companies`.
- Added migration `20260709214214_AddFollowedSymbols` with tenant/actor/actor-type scoping, canonical `ExternalCompanyId`, display symbol/name snapshots, `FollowedAtUtc`, and a unique actor/company index.
- Added `GET`, `POST`, `DELETE`, and `PUT` endpoints under `/api/v1/followed-symbols/me`, reusing watchlist read/write-self authorization policies.
- Extended symbol metadata with `externalCompanyId` so frontend follow actions resolve canonical company identity instead of persisting raw symbol text.
- Added frontend server functions, `/followed-symbols` management view, sidebar entry point, AI table follow action, and market-mover follow action. UI copy uses "followed symbols" and explicitly avoids portfolio/holding semantics.
- Validation passed: API Release build, `FollowedSymbols085Tests` 3/3, `FollowedSymbolsEndpointTests` 6/6, frontend `npm run build`, and architecture tests 7/7.
