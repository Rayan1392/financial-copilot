# Missing Owned-User Billing Account After Registration

## Date

2026-06-02

## Status

Resolved.

## User-Visible Failure

After a new web user registered and logged in successfully, the frontend requested:

```http
GET /api/v1/usage/me
```

The API returned an internal server error. The stack trace reached:

```text
FinancialCopilot.Billing.Services.BillableAccountResolver.ResolveAsync(...)
FinancialCopilot.API.Controllers.UsageController.GetMyUsage(...)
```

`BillableAccountResolver` could not find an individual Billing customer account for the
authenticated owned web user and threw:

```text
No billable customer account is configured for this actor.
```

## Root Cause

Owned Identity registration provisioned:

- The ASP.NET Core Identity user.
- The `User` role.
- The default tenant membership.
- The access token and refresh-token session.

It did not provision the Billing-side records required by usage reads and billable AI
operations:

- An individual prepaid `billing_customer_accounts` row.
- A `Free` subscription assignment.
- A `billing_wallet_projections` row initialized from the plan's included credits.

Existing Billing endpoint tests used pre-seeded customer accounts, so they did not cover the
real owned-user registration workflow.

## Resolution

Added `OwnedIdentityBillingProvisioner` in:

```text
src/backend/FinancialCopilot.Infrastructure/Authentication/OwnedIdentityBillingProvisioner.cs
```

Before any owned-user session is issued, it now idempotently ensures:

1. The `Free` subscription plan exists.
2. The user has an individual prepaid Billing account for the resolved tenant.
3. The account has the `Free` plan assigned when no plan is present.
4. The account has a wallet projection initialized with the plan's included credits.

The provisioner runs from `OwnedIdentityService.CreateSessionAsync`, so it applies to:

- Registration.
- Login.
- Refresh-token rotation.

This also repairs users created before the fix on their next login or refresh.

The implementation handles concurrent provisioning attempts by accepting the database
uniqueness race only when the expected fully provisioned account and wallet already exist.

## Tests Added

Extended `OwnedIdentityEndpointTests` with:

- `Register_ProvisionsFreeBillingAccountForUsage`
  - Registers a user.
  - Calls `GET /api/v1/usage/me`.
  - Verifies an individual prepaid wallet with `10` free credits.
- `Login_RepairsMissingBillingAccountForExistingUser`
  - Registers a user.
  - Removes the Billing account and wallet to simulate a pre-fix user.
  - Logs in again.
  - Verifies `GET /api/v1/usage/me` succeeds.

## Verification

```powershell
dotnet test src/backend/FinancialCopilot.sln --configuration Release --no-restore
```

Passed:

- Unit tests: `303`
- Integration tests: `206`
- Architecture tests: `3`

