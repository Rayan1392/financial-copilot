# Bug: Noavaran Amin Token Daily Limit Exhaustion

**Severity:** High — blocks all Noavaran Current API data ingestion until midnight server reset.

**Symptom:**
```
POST: https://data3.nadpco.com/api/v2/Token
Response 403: {"Status":403,"Response":{"Error":"Daily Token Allowance Exceeded"}}
```

## Vendor Constraint

Noavaran Amin limits token issuance to **10 tokens per day** (resets at midnight). Each token is valid for **24 hours**. If the system acquires more than 10 tokens in a calendar day, subsequent requests return HTTP 403 until midnight.

## Root Cause

`DefaultTokenLifetimeMinutes` was configured as `20` in both `API/appsettings.json` and `Worker/appsettings.json`, and also defaulted to `20` in `NadpcoApiProviderOptions`.

This means:

1. Both the API process and the Worker process independently cache the token for only 20 minutes.
2. After 20 minutes, `TryGetToken` considers the token expired and falls through to the distributed cache (Redis).
3. If Redis is also expired (its TTL was set to the same 20-minute window), both processes fetch a fresh token from the vendor.
4. With the Worker running scheduled sync jobs and the API serving requests, **3–6 tokens per hour** can be consumed — exhausting the 10-token daily quota in under 2 hours.

The vendor token actually remains valid for 24 hours, so no re-fetch was necessary at the 20-minute mark.

### Secondary contributor: `Invalidate()` on 401

`NadpcoApiAuthHandler` calls `tokenProvider.Invalidate()` followed by `GetTokenAsync(forceRefresh: true)` whenever a bearer-authenticated request returns HTTP 401. If the vendor sends a spurious 401 (e.g., during a brief service hiccup), this burns an additional token slot even though the existing token was still valid.

## Fix Applied

### `DefaultTokenLifetimeMinutes`: 20 → 1380 (23 hours)

Changed in three places:
- `src/backend/FinancialCopilot.API/appsettings.json`
- `src/backend/FinancialCopilot.Worker/appsettings.json`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Providers/NadpcoApi/NadpcoApiProviderOptions.cs` (default value)

23 hours gives a 1-hour safety margin before the vendor's 24-hour expiry, which is well within the vendor's window and ensures both processes can safely share a single token per day via Redis.

The `expiresAt.AddSeconds(-30)` safety trim in `NadpcoApiTokenProvider.SetToken` remains unchanged — it only subtracts 30 seconds from whatever lifetime is configured, so it is not a contributor.

## What Was NOT Changed

The `Invalidate`-on-401 behavior in `NadpcoApiAuthHandler` was left in place. With a 23-hour token lifetime it is a very rare path (only triggered by genuine 401 responses, not by token expiry). Removing it would risk holding a genuinely revoked token forever.

## Verification

After deployment, monitor Redis key `nadpco:auth:token` — it should persist across restarts and not be re-fetched more than once per day under normal operation.
