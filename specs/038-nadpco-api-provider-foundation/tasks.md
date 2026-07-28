# Tasks

1. Add `NadpcoApiProviderOptions` for base URL, credentials, timeout, retry, circuit-breaker,
   batch-size, and concurrency settings.
2. Add a thread-safe Redis-compatible token cache and authentication handler for Basic token
   acquisition, Bearer injection, Tehran-day cache reuse until `23:59:59`, and one bounded retry
   after token rejection (`401`, `403`, or vendor message `توکن صحیح نیست`).
3. Capture and document the verified successful token response contract and lifetime.
4. Add typed HTTP client registration with resilience and redacted structured telemetry.
5. Add provider-local DTO boundaries without leaking vendor payload types into Application.
6. Reuse `ProviderRawPayload` storage with `ProviderName = "NadpcoApi"` and deterministic
   checksum behavior.
7. Add a provider health adapter that reports authentication, transport, and remote-service
   state without leaking secrets.
8. Register the provider alongside existing providers using provider-name routing.
9. Add tests for token cache reuse, refresh, `401` retry, failed authentication, timeout,
   circuit breaker, raw-payload checksum deduplication, secret redaction, and coexistence with
   `CodalDb`.

## Implementation Status

Implemented on 2026-06-03.

Notes:

- Added `NadpcoApiProviderOptions`, token cache/provider, Bearer auth handler, local resilience
  handler, typed HTTP data client, provider health adapter, and provider-name routing.
- Updated on 2026-07-22: the token cache now stores the first successful Noavaran Amin token of
  each Tehran calendar day in Redis-compatible `IDistributedCache` until `23:59:59` Tehran time.
  Data requests reuse that token for the day and only force a new token after Tehran-day expiry,
  `401`, `403`, or the vendor invalid-token message `توکن صحیح نیست`.
- Raw payloads are stored with `ProviderName = "NadpcoApi"` and the existing checksum dedupe path.
- Credentials remain configuration-only and should be supplied from secrets/environment variables.
- The client accepts common token response shapes (`access_token`, `accessToken`, `token` plus
  optional `expires_in`, `expiresIn`, `expiresAt`, or `expiration`). The exact vendor token lifetime
  is still documented as requiring vendor confirmation or a controlled live smoke before scheduled
  NADPCO reads are enabled.
