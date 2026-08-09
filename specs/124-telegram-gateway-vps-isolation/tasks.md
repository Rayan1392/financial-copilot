# Tasks — Telegram Gateway and VPS Isolation

## 1. Discovery and Boundary Contract

- [x] Inventory every direct Telegram call in API, Worker, and Infrastructure.
- [x] Document current polling, assistant, linking, membership, callback, and notification flows.
- [x] Define the Gateway boundary and the canonical API-owned responsibilities.
- [x] Define normalized request/response contracts and correlation/idempotency fields.
- [x] Decide long polling versus webhook for the first production release; prefer long polling for
  the initial VPS isolation because it requires no inbound Telegram webhook route.

## 2. Gateway Service

- [x] Create a separately deployable Telegram Gateway process/container with no database dependency.
- [x] Store and load the Bot Token only from VPS 2 secret storage.
- [x] Implement `getUpdates` or webhook intake with bounded polling, backoff, timeout, and offset
  persistence.
- [x] Implement outbound `sendMessage`, `answerCallbackQuery`, typing actions, and media handling.
- [x] Implement `getChatMember` as a normalized membership operation.
- [ ] Add bounded retries, Telegram rate-limit handling, dead-letter/replay controls, and graceful
  shutdown.
- [x] Ensure duplicate updates and duplicate delivery requests are idempotent.

## 3. Primary API Integration

- [x] Add authenticated Gateway-to-API endpoints for assistant updates and account-link confirmation.
- [x] Add a Gateway client abstraction for membership verification.
- [x] Add a Gateway client abstraction for notification delivery while retaining the existing outbox
  lease, retry, dead-letter, and audit ownership on VPS 1.
- [ ] Remove direct Telegram HTTP calls from primary API/Worker paths after the Gateway is verified.
- [x] Preserve existing authorization, actor/tenant isolation, Billing, and conversation semantics.
- [x] Add configuration for Gateway base URL, service identity, timeouts, retry policy, and rollout
  mode without placing secrets in source-controlled settings.

## 4. Security

- [x] Create a least-privilege API client dedicated to the Gateway.
- [x] Enforce HTTPS, request authentication, timestamp/replay validation, and rate limits.
- [ ] Restrict Gateway access at the reverse proxy/firewall to the VPS 2 public IP where possible.
- [x] Add secret rotation procedures for Bot Token and Gateway credentials.
- [x] Redact tokens, Telegram payloads, callback data, and sensitive message content from logs.
- [ ] Verify that VPS 2 cannot reach PostgreSQL, Redis, or RabbitMQ.

## 5. Deployment and Cutover

- [ ] Provision VPS 2 with Docker, TLS, monitoring, firewall, and restart policy.
- [ ] Deploy the Gateway with a dedicated domain or private HTTPS endpoint.
- [x] Configure VPS 1 to use the Gateway in shadow/diagnostic mode before production cutover.
- [x] Disable the old polling/transport path only after end-to-end verification.
- [ ] Test message, callback, `/start` linking, membership, notification, retry, rate-limit, and
  restart scenarios.
- [ ] Rotate the Bot Token after confirming no old instance is polling.
- [x] Document rollback and emergency duplicate-poller shutdown procedures.

## 6. Observability and Tests

- [ ] Add metrics for inbound updates, forwarding latency, Telegram errors, retries, rate limits,
  duplicate updates, delivery outcomes, and membership failures.
- [ ] Add structured correlation ids across Telegram, Gateway, API, and notification outbox logs.
- [ ] Unit-test normalization, authentication, replay checks, offset handling, and retry policy.
- [ ] Integration-test Gateway-to-API contracts with fake Telegram responses.
- [ ] Run end-to-end tests against a test bot for chat, linking, callbacks, membership, and alerts.
- [ ] Verify that API/Worker function correctly when direct Telegram access from VPS 1 is blocked.

## Completion Gate

- [ ] All production Telegram traffic originates from VPS 2.
- [x] No Bot Token or Telegram transport secret remains required on VPS 1.
- [x] Persistent business state remains exclusively on VPS 1.
- [ ] Cutover, rollback, rotation, and incident procedures are documented and tested.
- [ ] Security, integration, and end-to-end evidence is linked before marking the feature complete.
