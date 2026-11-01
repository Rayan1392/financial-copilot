# Feature 151 — TahlilApp → SapioAI top-up contract

New inbound contract. Feature 139's outbound Basic Auth authenticates SapioAI to TahlilApp and cannot authenticate the reverse direction. The existing `POST /api/v1/billing/payment-callback/{provider}` returns `NotConfigured`; it is not a grant endpoint.

## Pre-checkout and delivery

`GET /api/v1/billing/partners/tahlilapp/top-ups/eligibility?userId=<encoded-id>` uses partner service authentication. A linked active Plus/Pro user receives `200 {"linked":true,"eligible":true,"plan":"Plus","subscription":{"slug":"sapio_plus","startsAtUtc":"2026-10-20T00:00:00Z","expiresAtUtc":"2027-01-18T00:00:00Z"},"checkedAtUtc":"2026-11-01T08:25:00Z"}`. If multiple paid windows are active, return eligible Plus/Pro windows explicitly and let TahlilApp snapshot the chosen window with the purchase; the chosen source cannot be inferred later. Free, active Max, expired, revoked or suspended accounts return `eligible:false`; unresolved ID returns 404; unavailable authoritative status returns 503. This check does not authorize a later grant. Implement the Max exclusion in the shared eligibility policy without requiring Feature 152's full Unlimited accounting rollout.

`POST /api/v1/billing/partners/tahlilapp/top-ups` uses HTTPS JSON and `X-Contract-Version: 1`. TahlilApp's backend sends it only after server-side payment verification. `userId` is Feature 139's official root `user_id`; resolve `(TahlilApp, userId)` in `auth_external_identities` and then the individual Billing account. No user is provisioned from a webhook. The original-window triple uses the **existing** official `subscription.plans[]` `slug`, `starts_at`, `expires_at`; there is no official subscription ID. TahlilApp must persist this triple at checkout. If the three fields cannot distinguish distinct windows in TahlilApp, the partner must add a stable period ID before enabling top-ups; this is a narrow contract contingency, not an assumed existing field.

```http
POST /api/v1/billing/partners/tahlilapp/top-ups
X-Contract-Version: 1
Authorization: Bearer <partner-service-token>
Content-Type: application/json

{
  "eventId": "evt-20261101-001",
  "purchaseId": "ta-purchase-001",
  "paymentId": "ta-verified-payment-001",
  "userId": "d647420371eb1270418f59c20d6c7d02",
  "creditsPurchased": 500,
  "packageId": "TA-TOPUP-500",
  "packageVersion": "v1",
  "subscription": {
    "slug": "sapio_plus",
    "startsAtUtc": "2026-10-20T00:00:00Z",
    "expiresAtUtc": "2027-01-18T00:00:00Z"
  },
  "paidAtUtc": "2026-11-01T08:30:00Z"
}
```

`eventId`, `purchaseId`, `paymentId` are stable, bounded opaque identifiers. `paidAtUtc` is the verified UTC payment timestamp, not delivery time; it must be within the claimed original window and cannot be in the future. `creditsPurchased` is a positive integer and must match an active agreed quantity: **100, 500 or 1000**. Package ID/version are optional but, if sent, must map to the same quantity in the active versioned offer allowlist. Unknown quantity, inactive package, mismatched package/quantity or malformed subscription window is rejected. TahlilApp retains payment audit; SapioAI validates the purchased credits.

## Authentication and responses

Use a dedicated short-lived partner JWT with validated issuer, audience `sapio-tahlilapp-topup`, `topup.deliver` scope and signing-key ID. TahlilApp signs only server-side. User JWTs and ordinary API keys cannot grant. Store signing/verifier material in deployment secrets, rotate with overlapping key IDs, require TLS, bounded skew, request size/rate limits and redacted logs.

```http
HTTP/1.1 200 OK
Content-Type: application/json

{"status":"granted","purchaseId":"ta-purchase-001","grantId":"b4b28ce1-7492-4a0d-a8ea-8e2f767a72ac","creditsGranted":500,"expiresAtUtc":"2027-01-18T00:00:00Z","grantedAtUtc":"2026-11-01T08:30:03Z"}
```

Look up persisted event, purchase and payment identities **before** checking current eligibility for a new grant. Identical retries, including a new event ID for the same purchase/payment, return the original persisted result after later expiry or a plan change; conflicting payloads return 409. A verified purchase delivered after its original window expired returns `200 {"status":"expired-unfulfilled","purchaseId":"ta-purchase-001","creditsGranted":0,"expiresAtUtc":"2027-01-18T00:00:00Z"}` after persisting that outcome; this acknowledges receipt **without claiming a grant**. If Plus/Pro payment preceded a transition to Max, an active/unrevoked original window may still receive the grant with its original expiry; a new Max checkout remains forbidden. Financial refund/reconciliation remains TahlilApp's decision. The self-scoped `GET /api/v1/billing/top-ups/{purchaseId}` returns the persisted status to the signed-in owner; before delivery and for another user it returns 404. SapioAI displays 404 as processing, not verified payment. Only `granted` triggers balance refresh via `GET /api/v1/usage/me`.

Errors use ProblemDetails with stable code/correlation ID and no sensitive payload: 400 `invalid-topup-event`; 401/403 `partner-auth-failed`; 404 `topup-user-unresolved` (retry after linkage, then reconcile); 409 `topup-identity-conflict`, `topup-ineligible` or `topup-reversed` (stop and investigate/refund); 422 `topup-quantity-invalid` (fix offer agreement or refund); 429/5xx `topup-temporary-failure` (retry same IDs/body with backoff). A verified `topup-ineligible` payment persists `ineligible-unfulfilled` for reconciliation before responding 409. Authoritative-status outage returns 503, never an inferred grant from stale grace. Failed validation and a received event are not `granted`.
