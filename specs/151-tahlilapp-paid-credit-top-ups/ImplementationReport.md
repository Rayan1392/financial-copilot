# Feature 151 implementation report

**Status (2026-11-02):** SapioAI implementation and focused verification are complete in the local repository. TahlilApp coordination, external user-ID entropy confirmation, and joint staging remain pending. Delivery stays disabled by default (`Billing:TahlilAppTopUps:Enabled=false`). No production migration, deployment, or production data change was performed.

The four-field webhook, anonymous balance read, and their documentation were first committed locally in `2d499d6`. This revision closes the gaps found when that commit was re-inspected. Its changes are uncommitted.

## Contracts

- `GET /api/v1/billing/partners/tahlilapp/credit-balance?userId=<URL-encoded-id>`:
  - Anonymous and read-only. Any Authorization header is ignored, and no billing, audit, outbox, or token rows are written.
  - Resolved through the existing TahlilApp identity mapping. Responses are `no-store`.
  - Returns `plan`, `subscriptionStartsAtUtc`, `subscriptionExpiresAtUtc`, `quotaMode` (`finite` or `unlimited`), `availableCredits` (`null` only for Unlimited), and `asOfUtc`.
  - Has its own per-IP limit (`RateLimiting:TahlilAppCreditBalance`, default 60 per 60 seconds), separate from the login/registration budget.
  - It does not refresh entitlements or evaluate checkout eligibility.
- `POST /api/v1/billing/partners/tahlilapp/top-ups`:
  - Requires the partner Bearer JWT (issuer, audience, `topup.deliver` scope, `kid`, one-time `jti`).
  - Accepts exactly `invoiceId`, `userId`, `creditsPurchased`, and `paidAtUtc`. Any other field, including `subscription`, returns 400 `invalid-topup-event`.
  - The invoice fingerprint covers exactly those four values.
  - Eligibility at `paidAtUtc`: Free/no paid window and active Max (even overlapping Plus/Pro) are ineligible. Plus, Pro, and overlapping Plus/Pro are eligible. For an overlap, the grant uses the window chosen by the shared overlap rule (latest expiry, Pro on an exact tie). Delivery after that window expired persists `expired-unfulfilled`.
- There is no top-up eligibility endpoint, and none is called from TahlilApp checkout.

See [integration-contract.md](integration-contract.md) for examples, error codes, and the security assessment.

## Changes in this revision

- **Dedicated balance rate limit.** The balance read previously shared `RateLimiting:PublicAuthentication` (10/minute per IP) with login, registration, and password reset. A TahlilApp server calling for all customers would have hit 10 reads/minute, competing with sign-in traffic from that IP. The new `TahlilAppCreditBalance` policy has its own configurable budget, in `RateLimitPolicies.cs`, `Program.cs`, `appsettings.json`, and `docker-compose.yml`.
- **Documented problem code on binding failures.** Unknown body fields, missing fields, malformed JSON, and a missing `userId` previously returned ASP.NET's generic validation problem without `code`. Both partner endpoints now return the documented 400 `invalid-topup-event`.
- **Dead code.** Removed the unused `SameContractInstant` helper from `TahlilAppTopUpService`.
- **Tests:**
  - New balance tests: latest-expiring finite plan, Pro on exact tie, Free window, expired top-up source and reservation exclusion, read-only footprint, ignored partner/garbage JWT, missing/blank/over-length/unknown IDs, and the dedicated rate limit.
  - New webhook tests: rejection of each removed body field, invoice conflicts on changed user and paid time, and unknown user.
  - Unit tests: the four-field serialization test replaces the stale five-field test; added a test for a payment outside every stored window (`ineligible-unfulfilled`).
- **Test compile fixes.** The unit-test project did not compile at `2d499d6`:
  - `CompanyDisclosureFeed112Tests` omitted the repository's required `cache` argument.
  - `TahlilAppTopUpTests` referenced the removed `TopUpWindowResponse` type.

  Both are fixed in test code only.
- **Docs.** Updated the integration contract (rate limit, exact error table, concrete `userId` assessment, removed fields), spec, user stories, tasks, `docs/api-design.md`, the Persian guide `docs/integrations/tahlilapp-feature151-fa.html`, and one cross-reference paragraph in the Feature 152 spec about Unlimited reporting.

## Eligibility-rule revision (2026-11-02)

- **Confirmed rule applied.** `TahlilAppTopUpService.DeliverAsync` no longer persists `ambiguous-unfulfilled` when Plus and Pro overlap at `paidAtUtc`. It selects the window that `TahlilAppSubscriptionPolicy.ResolveActivePlanSet` chose (latest expiry, Pro on an exact tie). Same-plan windows with an identical expiry fall back to the earliest start for a deterministic receipt.
- **Unchanged behavior.** Free and active-Max rejection, outside-window rejection, `expired-unfulfilled` after the selected window ends, invoice idempotency, JWT behavior, and source-aware expiry are unchanged.
- **Tests.**
  - Unit tests now cover Pro only, overlapping Plus/Pro (Pro later, Plus later, exact tie; each checks the receipt window, source expiry, and replay), Max overlapping Plus/Pro, Free without a window, and an overlap delivered after the selected window expired.
  - The endpoint overlap test now expects a grant from the later-expiring Pro window.
  - The endpoint Max test now includes an overlapping Pro window.
- **Not changed.** The frontend still recognizes a stored `ambiguous-unfulfilled` status (`_app.top-up.tsx`, `market-view.functions.ts`); the service no longer produces it. The nullable source-window migration is still needed for ineligible receipts that have no window.

| Check (eligibility revision) | Result |
|---|---|
| Release build of the integration-test project (API, Infrastructure) and the unit-test project | Both succeeded, 0 warnings, 0 errors. |
| `TahlilAppTopUpTests` (unit) | 16 passed, 0 failed. |
| `TahlilAppTopUpEndpointTests` + `TahlilAppTopUpPostgreSqlTests` + `TahlilAppMaxUsagePostgreSqlTests` on disposable PostgreSQL 18.1 | 29 passed, 0 failed, 0 skipped. |

## Verification performed in the previous revision

| Check | Result |
|---|---|
| `dotnet build tests/FinancialCopilot.IntegrationTests -c Release` (builds API, Infrastructure, and dependencies) | Succeeded, 0 warnings, 0 errors. |
| `TahlilAppTopUpEndpointTests` (Release, in-memory EF) | 15 passed, 0 failed. |
| `TahlilAppTopUpPostgreSqlTests` + `TahlilAppMaxUsagePostgreSqlTests` on a disposable local PostgreSQL 18.1 (127.0.0.1:55441), migrations applied by the tests | 14 passed, 0 failed, 0 skipped. Covers concurrent duplicate delivery, concurrent invoice conflict, rollback and safe retry, replay after expiry, and grant racing reservation/settlement/refund/expiry. |
| `dotnet build tests/FinancialCopilot.UnitTests -c Release` | Succeeded, 0 warnings, 0 errors. |
| Unit tests filtered to `TahlilApp` and `CompanyDisclosureFeed112Tests` | 147 passed, 0 failed. |
| `dotnet ef migrations has-pending-model-changes --context BillingDbContext` | No changes since the last migration. |
| Frontend build | Not run; no frontend file changed in this revision. |

**Not part of Feature 151, but it breaks other API tests.** The tracked `src/backend/FinancialCopilot.API/appsettings.json` in `2d499d6` sets `Authentication:Email:Enabled=true` with a Gmail `UserName` and `SenderAddress`, but no password and an empty `FrontendBaseUrl`. Startup options validation therefore fails ("Authentication SMTP settings are invalid", "email token settings and trusted frontend URL are invalid") in any host that does not override those settings. For example, `BillingEndpointTests` and `UsageBasedCreditBillingEndpointTests` fail at host start (17 of 33 in that run). The Feature 151 test factory now disables email explicitly. The tracked setting was left unchanged because it belongs to the Feature 145 SMTP work.

## External work and remaining constraints

- TahlilApp must send only the four webhook properties.
- It must keep one immutable invoice per verified payment and keep provider payment IDs on its side.
- It must issue a fresh short-lived JWT with a unique random `jti` for every attempt.
- Before the balance endpoint is exposed, TahlilApp must confirm in writing that production `user_id` values come from a CSPRNG with at least 128 bits of entropy and are not derived from sequential or personal data. The repository cannot verify this, and the Feature 139 format accepts low-entropy IDs.
- TahlilApp must state its peak balance-read rate and egress IPs so the limit and an optional edge allowlist can be set.
- A separate authenticated invoice-keyed reversal event is still needed for automated refund/chargeback handling.
- Joint staging must verify the balance endpoint, ID entropy, rate limits, JWT claims/key rotation, retry/idempotency, conflicts, source expiry/overlap, and reconciliation before delivery is enabled.
