# Tasks — Followed Symbols Foundation

## 1. Domain and Contracts

- [ ] Add `FollowedSymbol` entity/read model.
- [ ] Add `FollowedSymbolDto`.
- [ ] Add `GetMyFollowedSymbolsQuery`.
- [ ] Add `FollowSymbolCommand`.
- [ ] Add `UnfollowSymbolCommand`.
- [ ] Add `ReplaceMyFollowedSymbolsCommand`.
- [ ] Add `IFollowedSymbolRepository` abstraction.

## 2. Persistence

- [ ] Create migration for `FollowedSymbols`.
- [ ] Add unique index on `(ActorId, ExternalCompanyId)`.
- [ ] Add index on `ActorId` for fast retrieval.
- [ ] Store canonical `ExternalCompanyId` and display symbol.
- [ ] Preserve `FollowedAtUtc`.

## 3. Company Resolution

- [ ] Reuse existing company/symbol resolution path.
- [ ] Reject unknown symbols with a clear validation response.
- [ ] Ensure followed symbols reference canonical company identity.
- [ ] Avoid persisting raw unresolved user-entered symbol text.

## 4. Application Use Cases

- [ ] Implement `GetMyFollowedSymbolsUseCase`.
- [ ] Implement `FollowSymbolUseCase`.
- [ ] Implement `UnfollowSymbolUseCase`.
- [ ] Implement `ReplaceMyFollowedSymbolsUseCase`.
- [ ] Ensure idempotent follow behavior where appropriate.

## 5. API

- [ ] Add `GET /api/v1/followed-symbols/me`.
- [ ] Add `POST /api/v1/followed-symbols/me/{externalCompanyId}`.
- [ ] Add `DELETE /api/v1/followed-symbols/me/{externalCompanyId}`.
- [ ] Add `PUT /api/v1/followed-symbols/me` for bulk replacement.
- [ ] Return stable response shape for frontend use.

## 6. Frontend UX

- [ ] Add "Follow symbol" / "Unfollow" action on symbol pages.
- [ ] Add follow action on AI symbol answer cards.
- [ ] Add follow action on market insight cards.
- [ ] Add simple followed-symbols management view.
- [ ] Avoid portfolio wording in UI.

## 7. Tests

- [ ] Unit-test duplicate prevention.
- [ ] Unit-test follow/unfollow idempotency.
- [ ] Integration-test actor isolation.
- [ ] Integration-test unknown symbol validation.
- [ ] Integration-test list ordering and response shape.
