# Tasks

1. Add `NadpcoApiProviderOptions` for base URL, credentials, timeout, retry, circuit-breaker,
   batch-size, and concurrency settings.
2. Add a thread-safe token cache and authentication handler for Basic token acquisition,
   Bearer injection, expiry refresh, and one bounded `401` retry.
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
- Raw payloads are stored with `ProviderName = "NadpcoApi"` and the existing checksum dedupe path.
- Credentials remain configuration-only and should be supplied from secrets/environment variables.
- The client accepts common token response shapes (`access_token`, `accessToken`, `token` plus
  optional `expires_in`, `expiresIn`, `expiresAt`, or `expiration`). The exact vendor token lifetime
  is still documented as requiring vendor confirmation or a controlled live smoke before scheduled
  NADPCO reads are enabled.
