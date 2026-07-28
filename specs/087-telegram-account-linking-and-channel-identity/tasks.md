# Tasks — Telegram Account Linking and Channel Identity

## 1. Dependencies and Ownership

- [x] Reuse Feature 031 web authentication, the canonical `CurrentActor`/tenant context, and Feature 035 audit conventions; Telegram must not create an alternate user, tenant, wallet, or subscription.
- [x] Make this feature the sole owner of `TelegramAccountLink`, link-token lifecycle, Telegram identity lookup, unlink/relink rules, and link audit events.
- [x] Treat numeric `TelegramUserId` as the stable external identity and `TelegramChatId` as a delivery address; store username only as nullable display metadata and never resolve ownership by username.
- [x] Define service boundaries used by later features: `ITelegramIdentityLinkReader`, `ITelegramLinkService`, and authenticated Telegram-adapter-to-backend confirmation contract.

## 2. Domain Model and Invariants

- [x] Define `TelegramAccountLink` with canonical `ActorId`, optional `TenantId`, `TelegramUserId`, current private `TelegramChatId`, display metadata, `LinkedAtUtc`, `LastVerifiedAtUtc`, `RevokedAtUtc`, and row/concurrency version.
- [x] Define `TelegramLinkToken` as a purpose-bound, short-lived, single-use challenge with token hash, actor/tenant, creation/expiry/consumption/revocation timestamps, creator correlation id, and optional consuming Telegram identity.
- [x] Define states `Pending`, `Consumed`, `Expired`, and `Revoked`; only `Pending -> Consumed|Expired|Revoked` is valid and terminal states cannot return to pending.
- [x] Enforce one active Telegram identity per canonical actor and one active canonical actor per `TelegramUserId`; relinking requires explicit revocation/replacement rather than silent reassignment.
- [x] Define whether a new token revokes all older pending tokens for the same actor (recommended) and make issuance/consumption idempotent under retries.
- [x] Require unlink to revoke the active link and all pending tokens without deleting the audit trail; historical rows remain non-authoritative after `RevokedAtUtc`.

## 3. Persistence

- [x] Add additive persistence for `TelegramAccountLinks`, `TelegramLinkTokens`, and link audit records in the Identity-side infrastructure boundary.
- [x] Store only a cryptographic hash of the random token; never persist or log the bearer token, deep-link URL, bot token, or Telegram update payload containing it.
- [x] Add filtered unique constraints for active `(ActorId, TenantId)` and active `TelegramUserId`, plus unique token hash and indexes for expiry cleanup and Telegram identity lookup.
- [x] Use a transaction and optimistic concurrency/conditional update when consuming a token so two `/start link_<token>` updates cannot both succeed.
- [x] Retain revoked link/audit records according to Identity security retention; delete or redact expired token material after the documented security window.

## 4. Application Use Cases

- [x] Implement authenticated web `CreateTelegramLinkToken` returning expiry and deep link `https://t.me/{bot}?start=link_<token>`; validate bot configuration without exposing secrets.
- [x] Implement Telegram-first `/start` onboarding that explains sign-in, creates a web login/confirmation URL bound to the Telegram user/chat, and completes only after authenticated web confirmation.
- [x] Implement `ConfirmTelegramLink` for both flows with purpose, expiry, actor, tenant, and Telegram identity validation before atomic consumption.
- [x] Return explicit outcomes for invalid, expired, revoked, already-consumed, identity-conflict, actor-already-linked, and concurrent-consumption cases without revealing another actor's identity.
- [x] Implement authenticated web unlink and bot unlink; bot unlink must resolve the canonical actor through the active link and require an explicit callback confirmation.
- [x] Define relinking behavior for the same actor/same Telegram user as idempotent and for either side already linked elsewhere as a conflict requiring unlink first.

## 5. API and Telegram Contracts

- [x] Specify `POST /api/v1/telegram/link-token` as authenticated web initiation with response `{ deepLink, expiresAtUtc, correlationId }` and no token echo outside the deep link.
- [x] Specify `/start link_<token>` parsing with strict prefix, character set, maximum length, private-chat requirement, and localized success/failure messages.
- [x] Specify a web confirmation page after login that shows masked Telegram display metadata and requires a positive confirmation before linking.
- [x] Specify adapter confirmation as service-authenticated, replay-protected input containing token, `TelegramUserId`, `TelegramChatId`, Telegram update id, and correlation id.
- [x] Specify `DELETE /api/v1/telegram/link/me` and bot callback `unlink:confirm`; repeat unlink returns an idempotent already-unlinked result.
- [x] Persist processed Telegram update ids or equivalent idempotency keys so webhook retries cannot repeat link state changes.

## 6. Security and Operations

- [x] Use high-entropy tokens, a short configurable lifetime, constant-time hash comparison, purpose binding, HTTPS-only links, and server-side clock abstraction.
- [x] Authenticate the Telegram adapter with the repository's service-to-service mechanism; authorize web operations with current actor policies and isolate all queries by actor/tenant.
- [x] Rate-limit token issuance, invalid-token attempts, confirmation, and unlink callbacks; audit suspected enumeration or replay without logging raw identifiers unnecessarily.
- [x] Keep bot/service secrets in environment or secret storage and redact Telegram ids, usernames, tokens, and callback payloads from normal logs.
- [x] Emit structured metrics for issuance, successful links, expirations, conflicts, replays, unlinks, and adapter authentication failures, correlated by safe correlation id.

## 7. Tests and Acceptance Scenarios

- [x] Unit-test token hashing, expiry boundary, purpose binding, state transitions, deep-link parsing, and username-independence.
- [x] Integration-test web-first and Telegram-first flows, actor/tenant isolation, service authentication, pending-token revocation, unlink, and same-pair relink.
- [x] Concurrency-test two consumers of one token and two actors attempting the same Telegram identity; exactly one link may become active.
- [x] Replay-test repeated Telegram update ids, consumed tokens, revoked tokens, and expired confirmation URLs.
- [x] Given an authenticated actor, when a link token is consumed once by an unlinked Telegram user, then one active link exists and both channels show localized confirmation.
- [x] Given an expired/replayed token or an identity owned by another actor, when confirmation is attempted, then no link changes, no Billing entry is created, and a non-enumerating error is returned.
- [x] Given an active link, when unlink is confirmed from web or bot, then the link and pending tokens are revoked and later user-specific Telegram operations are denied.

## Completion Gate

- [x] Keep every task unchecked until implementation, migration review, security/concurrency tests, and checklist evidence are complete.
- [x] Confirm no Telegram-specific user, wallet, subscription, or username-based identity lookup was introduced.


## Implementation Notes

- Implemented canonical actor-to-Telegram linking in the existing Identity boundary with API-client-authenticated adapter operations and web-user self-service permission.
- Added web-first and Telegram-first purpose-bound challenges, SHA-256 token hashes, configurable expiry, pending-token revocation, strict private-chat/start-token validation, replay protection, and web/bot unlink.
- Added migration `20260712195627_AddTelegramAccountLinking`; filtered unique indexes and serializable confirmation enforce one active link per actor and Telegram user.
- Added Persian web initiation and masked post-login confirmation routes; username remains display-only and no Telegram-specific Identity or Billing model was introduced.
- Validation: API Release build; Feature 087 integration tests 6/6; Identity/authentication regressions 19/19; architecture tests 7/7; frontend production build; no pending Auth EF model changes.
