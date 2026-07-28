# Adding Credit to a User

This runbook explains how to add (top up) billing credit for a user. The supported,
audited path is the **Admin Billing credit-adjustment endpoint**, which writes an immutable
`UsageLedger` entry and updates the wallet projection in one transaction.

> Credit is owned by the `FinancialCopilot.Billing` bounded context. The AI orchestrator and
> any other caller must never mutate a wallet balance directly — always go through this
> endpoint so the ledger remains the single source of accounting truth. See
> [billing-and-credits-domain.md](billing-and-credits-domain.md).

## TL;DR

```http
POST /api/v1/admin/billing/customers/{customerAccountId}/adjustments
Authorization: Bearer <BillingAdmin JWT>
Content-Type: application/json

{
  "credits": 10000,
  "reason": "Manual top-up for <who/why>",
  "idempotencyKey": "topup-2026-06-06-001"
}
```

You need two things: the user's **`customerAccountId` GUID** (see below) and a **BillingAdmin**
JWT.

## Step 1 — Find the user's `customerAccountId` GUID

A user does not pay with their `UserId` directly; every billable user owns a **`CustomerAccount`**
identified by a GUID (`CustomerAccountId`). For an individual user the account is the unique
`(TenantId, UserId)` row in `billing_customer_accounts`.

Resolve the GUID from the user's email against the PostgreSQL database:

```sql
SELECT ca."Id"        AS customer_account_id,
       ca."TenantId"  AS tenant_id,
       ca."UserId"    AS user_id,
       ca."AccountType",
       ca."BillingMode",
       wp."Balance"   AS current_balance
FROM   billing_customer_accounts ca
JOIN   auth_users u   ON u."Id" = ca."UserId"
LEFT  JOIN billing_wallet_projections wp ON wp."CustomerAccountId" = ca."Id"
WHERE  lower(u."Email") = lower('user@example.com');
```

- `customer_account_id` is the GUID you pass in the endpoint path.
- If no row comes back, the user has not been provisioned a billing account yet. Accounts are
  provisioned on first owned-identity sign-in via `OwnedIdentityBillingProvisioner`
  (Free plan → `IncludedCredits` initial balance). Have the user sign in once, then re-run the query.
- For **organization / API-client** consumers there is one account per tenant (no `UserId`); find
  it with `WHERE ca."TenantId" = '<tenant-guid>' AND ca."AccountType" = 'Organization'`.
"user_id" = `86ad7abb-206c-4e43-b6d2-06c5c3f3884d`
"customer_account_id" = `97899f00-29a6-4ae0-bec0-a2f6cd026f03`
tenant_id = `11111111-1111-1111-1111-111111111111`

> **Record the GUID** for the user you are working with in
> [`user-account-ids.md`](user-account-ids.md) so it is easy to reuse. Treat that file as an
> operational note, not a credential.

## Step 2 — Obtain a BillingAdmin token

The endpoint is guarded by the `BillingAdmin` authorization policy, which requires:

- a **WebApp user** JWT (the `X-Api-Key` credential is **not** accepted for this route), and
- the `Billing.Manage` permission, granted through the `BillingAdmin` role.

Grant the role to your operator account (one-time) and sign in to obtain the JWT.

## Step 3 — Apply the credit

```bash
curl -X POST \
  "http://localhost:5074/api/v1/admin/billing/customers/<customerAccountId>/adjustments" \
  -H "Authorization: Bearer <BillingAdmin JWT>" \
  -H "Content-Type: application/json" \
  -d '{
        "credits": 10000,
        "reason": "Manual top-up for QA testing",
        "idempotencyKey": "topup-2026-06-06-001"
      }'
```

Request body (`AdminCreditAdjustmentRequest`):

| Field | Type | Notes |
| --- | --- | --- |
| `credits` | decimal | Amount to add. Must be `> 0`. Stored at precision `(18,4)`. |
| `reason` | string | Audit description (≤ 500 chars). Required. |
| `idempotencyKey` | string | Unique per logical top-up (≤ 160 chars). Replaying the **same** key is a no-op and returns `alreadyApplied: true`. |

Response (`AdminCreditAdjustmentResponse`):

```json
{
  "ledgerEntryId": "…",
  "credits": 10000,
  "updatedBalance": 20000,
  "availableSpendingCapacity": 20000,
  "alreadyApplied": false
}
```

The adjustment is recorded as a `UsageLedgerEntry` of type `Adjustment`
(operation code `Billing.ManualAdjustment`) and emits a `Billing.CreditAdjusted` outbox event.

## Step 4 — Verify the new balance

```http
GET /api/v1/admin/billing/customers/{customerAccountId}/wallet
Authorization: Bearer <BillingAdmin JWT>
```

Returns the current `balance`, `reservedCredits`, and `availableSpendingCapacity`
(`balance + creditLineApprovedLimit − reservedCredits`).

## Notes

- **Idempotency matters.** Use a fresh `idempotencyKey` for each *intended* top-up. Reusing a key
  intentionally protects against double-charging on retries; reusing it accidentally silently
  skips the top-up.
- **Units are abstract "credits"** (decimal, not currency). What a credit buys is governed by the
  pricing policy, not by this endpoint.
- **Tenant isolation.** The endpoint only operates on accounts within the caller's tenant; a
  `customerAccountId` from another tenant is rejected.

## Reference

| Purpose | Path |
| --- | --- |
| Endpoint | `src/backend/FinancialCopilot.API/Controllers/AdminBillingController.cs` |
| Request/response DTOs | `src/backend/FinancialCopilot.API/Contracts/BillingAdminResponse.cs` |
| Credit logic | `src/backend/FinancialCopilot.Infrastructure/Billing/Persistence/CreditAdjustmentService.cs` |
| Auth policy | `src/backend/FinancialCopilot.API/Security/ServiceCollectionExtensions.cs` (`BillingAdmin`) |
| Account provisioning | `src/backend/FinancialCopilot.Infrastructure/Authentication/OwnedIdentityBillingProvisioner.cs` |
| Domain overview | [billing-and-credits-domain.md](billing-and-credits-domain.md) |
