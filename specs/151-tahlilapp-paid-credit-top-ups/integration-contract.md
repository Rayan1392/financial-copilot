# Feature 151 — TahlilApp and SapioAI integration contract

This contract covers the informational balance read and server-to-server paid top-up delivery. Feature 139's outbound Basic Auth is unrelated to webhook authentication. Delivery remains disabled until TahlilApp adopts this contract and staging verification is complete.

## Informational balance/status

`GET /api/v1/billing/partners/tahlilapp/credit-balance?userId=<URL-encoded-stable-user-id>` is unauthenticated by agreement. SapioAI URL-decodes the query value and resolves it through the existing `(Provider = TahlilApp, ExternalUserId)` identity mapping. The endpoint is read-only: it does not refresh/synchronize entitlements, grant credits, or run checkout eligibility. It is rate limited by client IP using the existing public-authentication policy (10 requests/minute by default), and sends `Cache-Control: no-store`.

Finite response example:

```json
{
  "plan": "Pro",
  "subscriptionStartsAtUtc": "2026-10-20T00:00:00Z",
  "subscriptionExpiresAtUtc": "2027-01-18T00:00:00Z",
  "quotaMode": "finite",
  "availableCredits": 725,
  "asOfUtc": "2026-11-02T12:00:00Z"
}
```

For Max, the response uses `"quotaMode":"unlimited"` and `"availableCredits":null`; it never encodes Unlimited as a numeric sentinel. Max's own authoritative expiry is reported. Active Max takes precedence over other active windows; after Max expires, the latest-expiring active non-Max window is selected, preserving Pro on an exact expiry tie. For finite plans, `availableCredits` is the expiry-aware spendable wallet balance less reserved credits. Expired source credits are excluded, including top-up sources. Free uses the account's persisted Free subscription start/end fields. A missing or inactive mapping returns 404. Unknown or malformed IDs return 400/404 without personal or payment details.

The repository stores the stable external ID but does not generate it or establish its entropy. The sample 32-character hexadecimal value is not proof that production IDs are random. Without authentication, a guessable/enumerable ID exposes that customer's remaining credits, plan, and subscription dates. The compatible mitigation is to have TahlilApp guarantee cryptographically random, non-enumerable IDs with at least 128 bits of entropy and retain the IP rate limit at the edge and in SapioAI. Confirm this guarantee externally before exposing the endpoint; JWT is not added by this contract.

## Top-up delivery

Send HTTPS `POST /api/v1/billing/partners/tahlilapp/top-ups` with `Content-Type: application/json` and `Authorization: Bearer <partner JWT>`. The JSON body has exactly four fields:

```json
{
  "invoiceId": "ta-invoice-001",
  "userId": "d647420371eb1270418f59c20d6c7d02",
  "creditsPurchased": 500,
  "paidAtUtc": "2026-11-01T08:30:00Z"
}
```

Do not send subscription data, payment amount/currency, package ID/version, event ID, payment ID, a JWT in the body, or extra request fields. Unknown body properties are rejected. Query/identity values are capped at the repository's external identity size (128 characters); invoice IDs are non-empty and bounded. `paidAtUtc` must be valid and not in the future.

SapioAI resolves the active TahlilApp identity and authoritative subscription windows from its synced Feature 139 data. Credit quantity must match an active database-controlled offer (100, 500, or 1000). Free accounts cannot receive a new top-up grant. A payment timestamp that falls during an active Max window is ineligible. If a Plus/Pro invoice was paid before Max began, it may still be delivered during the Plus/Pro-to-Max transition only while its original Plus/Pro source remains active and unrevoked. Grants expire at that source's authoritative end. Expired or revoked sources persist `expired-unfulfilled`; ineligible current plans persist `ineligible-unfulfilled`.

Because the body intentionally omits the source subscription, SapioAI matches `paidAtUtc` against stored Plus/Pro windows. If more than one Plus/Pro window contains that timestamp, the invoice is persisted as `ambiguous-unfulfilled` with zero credits and no source assignment. The original source cannot be proven from the four fields, so SapioAI does not choose by effective plan or expiry ordering. TahlilApp should route this outcome to reconciliation/manual resolution; retrying the same invoice returns the same stored outcome. This is the exact overlap case where the simplified contract cannot identify the original purchase source.

## Invoice identity and outcomes

`invoiceId` is the immutable business idempotency key. Its persisted fingerprint covers exactly `invoiceId`, `userId`, `creditsPurchased`, and `paidAtUtc`. A retry with the same invoice and same four values returns the original outcome without a second grant, even after expiry or plan changes. Reusing an invoice with a different user, quantity, or paid timestamp returns 409 `topup-identity-conflict`; it cannot modify the original outcome or grant.

TahlilApp must enforce that one verified payment maps to at most one immutable invoice ID and retain provider/payment IDs internally. SapioAI cannot correlate provider refunds/chargebacks for new receipts using payment IDs. A separate authenticated invoice-keyed reversal event remains out of scope and is needed if SapioAI should automate those reversals.

Successful delivery returns the persisted result, for example:

```json
{"status":"granted","invoiceId":"ta-invoice-001","grantId":"b4b28ce1-7492-4a0d-a8ea-8e2f767a72ac","creditsGranted":500,"expiresAtUtc":"2027-01-18T00:00:00Z","grantedAtUtc":"2026-11-01T08:30:03Z"}
```

The receipt, source grant, ledger adjustment, wallet projection, audit, outbox, and outcome are saved atomically. Active offer quantities remain DB-controlled. Self-scoped `GET /api/v1/billing/top-ups/{invoiceId}` returns persisted customer status; the browser return never authorizes a grant.

## JWT validation and replay

The Authorization JWT continues to use the configured issuer (`Billing:TahlilAppTopUps:Authentication:Issuer`), audience (default `sapio-tahlilapp-topup`), `topup.deliver` scope, configured `kid` key selection, signature, and token lifetime with 30 seconds clock skew. Required claims are `iss`, `aud`, `scope`, `exp`, and `jti`; signing keys require at least 32 UTF-8 bytes. SapioAI atomically consumes `(iss,jti)` for each POST. TahlilApp must issue a fresh short-lived token and random unique `jti` for every delivery attempt while preserving the invoice and body. Balance GET does not require or reuse this JWT.

## Errors, reconciliation, and rollout

Malformed requests return 400 `invalid-topup-event`; unknown quantities return 422 `topup-quantity-invalid`; missing/replayed partner auth returns 401 `partner-auth-failed`; unresolved users return 404 `topup-user-unresolved`; ineligible persisted outcomes return 409 `topup-ineligible`; invoice conflicts return 409 `topup-identity-conflict`; temporary authoritative-data or storage failures return 503/5xx for retry with a fresh JWT and unchanged body. Ambiguity is returned as the persisted `ambiguous-unfulfilled` outcome for reconciliation.

Reconciliation identifies new receipts by invoice, outcome, owner, optional resolved source window, payment/received time, and grant. Historical payment IDs remain on legacy receipts only, with their existing uniqueness constraint. No production migration or production data change is part of this work. Coordinate the four-field request and balance read with TahlilApp, verify high-entropy IDs and all JWT claims in staging, and keep `Billing:TahlilAppTopUps:Enabled=false` until joint verification.
