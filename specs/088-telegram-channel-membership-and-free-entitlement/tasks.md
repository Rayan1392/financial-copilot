# Tasks — Telegram Channel Membership and Free Entitlement

## 1. Dependencies and Ownership

- [x] Depend on Feature 087 for canonical actor-to-Telegram identity and Features 010/013 for reservation, wallet projection, and immutable `UsageLedger` accounting.
- [x] Make this feature the owner of membership verification/cache and free-daily-allowance eligibility policy; do not create a Telegram wallet or redefine paid subscription/purchased-credit balances.
- [x] Document the consumption order as free daily allowance first, then active subscription allowance, then purchased credits, implemented through Billing allocation policy and exposed in ledger evidence.

## 2. Domain and Policy

- [x] Define governed membership states covering `Creator`, `Administrator`, `Member`, `RestrictedMember` with membership, `Left`, `Kicked/Banned`, `NotFound`, and `UnknownProviderFailure`; explicitly map which states qualify.
- [x] Define `ChannelMembershipVerification` with actor, Telegram user, configured channel identity, normalized status, provider observation time, cache expiry, failure category, and correlation id.
- [x] Define `DailyFreeAllowancePolicy` with configurable amount (initially five), Tehran business timezone/day key, eligibility requirement, non-rollover rule, and policy version.
- [x] Model a daily grant/allocation as an idempotent Billing operation keyed by `(ActorId, AllowanceDate, PolicyVersion)`; unused units expire at the next Tehran-day boundary and never become purchased balance.
- [x] Define membership-loss behavior: no new free reservation after a confirmed invalid state; already committed usage is not reversed; subscription and purchased credits remain usable.
- [x] Define fail-closed behavior for expired/absent verification when Telegram is unavailable, while a still-valid cached eligible result remains usable until its expiry.

## 3. Persistence and Billing Integration

- [x] Persist the latest verification and bounded history/audit data with unique actor/channel cache key and indexes on expiry, status, and revalidation time.
- [x] Record free allowance issuance/consumption/expiry through existing Billing ledger and reservation contracts with allocation-source metadata; do not mutate wallet totals directly.
- [x] Enforce daily-grant uniqueness in the database and transactionally handle concurrent first requests at the day boundary.
- [x] Store configured channel id/username as configuration metadata, but compare membership using Telegram numeric channel/user identifiers returned by the provider.
- [x] Retain provider failure category and timestamps without retaining unnecessary raw Telegram responses.

## 4. Application Use Cases

- [x] Implement `VerifyRequiredChannelMembership` using the active Feature 087 link, provider adapter, cache policy, and explicit eligible/ineligible/unavailable result.
- [x] Implement `GetMyTelegramEntitlement` returning membership status/freshness, daily allowance total/used/remaining/expiry, paid entitlement summary, and next action.
- [x] Implement idempotent `EnsureDailyFreeAllowance` within the Billing transaction boundary and never grant before a qualifying membership result exists.
- [x] Revalidate on explicit user request, cache expiry, membership-sensitive entitlement refresh, and a bounded background schedule; avoid a Bot API call per AI message.
- [x] Validate allowance reservations against the Tehran date key and membership cache atomically enough that concurrent messages cannot exceed the allowance.
- [x] Add abuse controls for repeated link/unlink, account swapping, verification storms, and multiple requests at midnight without penalizing legitimate retries. Link-token/update idempotency, endpoint rate limits, `auth_telegram_link_audits`, verification history, and serialized reservation handling now provide the expected audit/control surface.

## 5. API, Telegram, and Background Contracts

- [x] Specify `POST /api/v1/telegram/membership/verify` as actor-scoped and rate-limited, returning normalized state, `verifiedAtUtc`, `validUntilUtc`, and join/retry action.
- [x] Specify `GET /api/v1/telegram/entitlement/me` with distinct free, subscription, and purchased-credit buckets and an explanation of consumption order.
- [x] Provide localized inline actions to join the configured channel and re-check membership; callback data must be short, versioned, actor-resolved, and replay-safe.
- [x] Schedule due revalidation with bounded concurrency, distributed lease where multiple workers run, exponential backoff for Telegram failures, and dead-letter/operations visibility after exhaustion.
- [x] Do not downgrade a confirmed eligible cache entry solely because a transient provider request failed before cache expiry; surface verification freshness honestly.

## 6. Security and Observability

- [x] Keep bot credentials and channel secrets outside source control; redact Telegram ids and raw provider payloads while retaining safe provider status/error codes.
- [x] Enforce actor/tenant isolation and require service authentication for provider-adapter callbacks or internal verification commands.
- [x] Rate-limit verification by actor, Telegram identity, and IP/update source; audit suspicious account cycling and repeated invalid membership claims.
- [x] Emit metrics for eligible/ineligible states, cache hit rate, provider latency/failures, daily grants, duplicate-grant prevention, bucket consumption, and denied reservations.
- [x] Trace verification, allowance allocation, reservation, commit/release, and final ledger entry with one correlation id.

## 7. Tests and Acceptance Scenarios

- [x] Unit-test every Telegram member state, cache freshness, Tehran midnight boundary, non-rollover, consumption ordering, and membership-loss policy.
- [x] Integration-test Billing ledger idempotency, free/subscription/purchased fallback order, reservation rollback, actor isolation, and provider-unavailable behavior.
- [x] Concurrency-test simultaneous first use and simultaneous reservations at the daily limit; one allowance grant and no overspend are permitted.
- [x] Expiry-test stale eligible and stale ineligible cache entries, provider recovery, and allowance expiry across daylight-independent Tehran day boundaries.
- [x] Given a linked qualifying member, when the first metered request occurs that day, then exactly five free units are available through Billing and unused units do not roll over.
- [x] Given membership loss, when free credit is requested after revalidation, then free access is denied but valid subscription/purchased credit may still be reserved.
- [x] Given Telegram is unavailable, when a valid eligible cache exists it is honored until expiry; otherwise no grant occurs and a localized retry response is returned.

## Completion Gate

- [x] Keep tasks unchecked until Billing integration, timezone/concurrency tests, provider contract tests, and checklist evidence pass.
- [x] Confirm no Telegram-specific wallet, balance mutation, or per-message provider check was introduced.

## Implementation Notes

- Core implementation landed 2026-07-13 and was closed on 2026-07-13 with localized inline actions, bounded worker-driven revalidation, dead-letter/backoff handling, cache/provider/grant/reservation metrics, and a per-account reservation serialization guard for the free-daily-allowance path.
- Validation passed: backend Release build; frontend production build; Feature 088 focused unit suite 14/14; Feature 088 focused integration suite 4/4; architecture tests 7/7; `git diff --check`.
- Auth EF migration `20260713122727_AddTelegramMembershipRevalidation` was added so the revalidation table and snapshot stay aligned with the implementation.
