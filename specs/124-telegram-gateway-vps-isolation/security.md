# Security Implementation and Operations

## Application controls

- The Gateway uses a dedicated `PrimaryApiKey` for its API calls and a separate signed-request
  identity (`ServiceId`/`ServiceSecret`) for VPS 1 to Gateway operations. API-key clients support
  `AllowedPathPrefixes`; the Gateway credential should be restricted to the assistant-update and
  link-confirmation endpoints. These values must be injected by the deployment secret store, never
  committed to settings files.
- Gateway operation endpoints require HTTPS, HMAC-SHA256 signatures, a bounded timestamp skew, and
  a unique nonce. Reusing a nonce is rejected by the replay store.
- Gateway endpoints use a fixed-window rate limiter partitioned by the authenticated Gateway id
  (or source IP before authentication headers are available).
- Telegram payloads, Bot Tokens, API keys, callback data, and message text are not written to
  application logs. Polling failures log only a redacted exception type.

## Secret rotation

1. Create a new Gateway service secret and a new dedicated API key in the deployment secret store.
2. Add the new values to the API and Gateway environments while keeping the old values temporarily
   accepted during the short rotation window.
3. Restart the API and Gateway, verify signed requests and API calls, then revoke the old values.
4. Rotate the Telegram Bot Token in the Gateway secret store only; restart the Gateway and confirm
   that no VPS 1 process contains or requires the token.

## Network isolation verification

Before production cutover, verify from the Gateway host that PostgreSQL, Redis, and RabbitMQ ports
are unreachable, and verify from the primary host that Telegram access is not required. Enforce the
same rule with the VPS firewall/security group and container network policy. This is deployment
evidence and cannot be proven by the application build alone.
