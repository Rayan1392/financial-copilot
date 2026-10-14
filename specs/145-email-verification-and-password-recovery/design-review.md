# Feature 145 — Pre-Implementation Design Review

Review performed after discovery/specification and before production changes. This is an independent pass over the security and compatibility properties of `design.md`.

## Findings

### BLOCKER — None

No unresolved design blocker identified.

### MAJOR — Resolved

1. The checked-in API configuration sets owned-user access JWT lifetime to seven days. Revoking refresh tokens alone would leave established access sessions usable for a week after recovery. **Revision:** newly issued owned-user JWTs carry the Identity security stamp; JWT validation checks it against the current user record. A reset rotates the stamp, immediately invalidating those JWTs. Existing pre-feature tokens lack the claim and retain their original expiry. This is now explicitly documented.

Token replay is controlled by a conditional consume update in a shared database transaction; legacy users are not silently marked verified or locked out; reset revokes persisted refresh sessions; enumeration-sensitive operations share a generic response.

### MINOR

1. A database lookup is required for each newly issued owned-user JWT validation to enforce security-stamp revocation; deployments should monitor auth database load.
2. SMTP send occurs after persistence and is not durably queued. A process crash or provider failure can delay delivery; the persisted cooldown/token state plus generic resend flow is recoverable, but delivery is not exactly-once.
3. API rate limiting is currently in-process. Multi-instance production must apply equivalent IP throttling at a shared gateway or configure a distributed limiter.

### NOTE

Registration still exposes duplicate-email status as the existing endpoint does; full registration enumeration protection was not a required compatibility change. Consider it separately.

## Review decision

## Verification review after focused revision

The revised JWT policy gives newly issued owned-user access sessions a server-side revocation mechanism and keeps external/API-client token behavior outside that path. The remaining legacy-token window is bounded by the currently configured lifetime and is called out for deployment. No security-critical finding remains unresolved.

**PASS — proceed to implementation.**
