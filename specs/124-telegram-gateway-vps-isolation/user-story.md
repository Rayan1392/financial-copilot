# Feature 124 — Telegram Gateway and VPS Isolation

## Status

`[ ] Planned`

## Feature

Move all Telegram network traffic to a dedicated Gateway hosted on a Telegram-accessible VPS,
while keeping Financial Copilot API, database, Redis, RabbitMQ, and business processing on the
primary VPS.

## Story

As a Financial Copilot operator,

I want Telegram connectivity to be handled by a separate Gateway VPS,

so that Telegram filtering or network failure on the primary VPS does not interrupt Telegram
chat, account linking, membership verification, or notifications.

## Business Context

The current solution has Telegram responsibilities in both the Worker and Infrastructure layers:
polling, message delivery, callback responses, membership checks, and notification delivery. A
second Worker alone does not isolate all Telegram traffic and may duplicate unrelated background
jobs. The Gateway must be a transport boundary, not a second business-data or identity system.

## Dependencies

- Features `031`, `087`, `088`, `089`, `097`, `098`, and `099`.
- Existing API-client authentication and canonical actor/tenant context.
- Existing notification outbox, retry, idempotency, and audit records.

## Goals

- Keep the Telegram Bot Token only on the Gateway VPS.
- Ensure every request to `api.telegram.org` originates from the Gateway VPS.
- Keep canonical users, tenants, billing, conversations, financial data, and notification state on
  the primary VPS.
- Preserve Telegram update idempotency, account linking, callback handling, membership checks, and
  notification retry semantics.
- Allow the Gateway to be restarted or redeployed without losing durable business state.

## In Scope

- Dedicated Telegram Gateway process/container on VPS 2.
- Long polling initially; webhook mode may be added after the boundary is stable.
- Forwarding inbound updates to the primary API over authenticated HTTPS.
- Gateway-owned Telegram operations: `getUpdates`, `sendMessage`, `answerCallbackQuery`, typing
  actions, webhook management, and `getChatMember`.
- API contracts for assistant updates, account-link confirmation, membership verification, and
  notification delivery.
- Secure deployment, observability, retries, replay protection, cutover, and rollback.

## Out of Scope

- A Telegram-specific user, tenant, wallet, subscription, or billing model.
- A second PostgreSQL, Redis, or RabbitMQ data plane.
- Direct database access from the Gateway.
- Moving financial calculations or AI orchestration to the Gateway.
- Storing raw Bot Tokens or Telegram payloads in normal logs.

## Target Architecture

```text
Telegram API
     |
     | HTTPS / Bot Token (VPS 2 only)
     v
Telegram Gateway (VPS 2)
     |
     | HTTPS + service authentication
     v
Financial Copilot API (VPS 1)
     |
     +-- PostgreSQL
     +-- Redis
     +-- RabbitMQ / Worker
```

## Required Scenarios

1. When Telegram sends a new message, the Gateway receives it and forwards a normalized update to
   the primary API exactly once or with a replay-safe idempotency key.
2. When the API returns rendered assistant messages, the Gateway sends them to the originating
   Telegram chat and records the delivery result without exposing the Bot Token to the API.
3. When a user starts an account-link flow, the Gateway forwards the numeric Telegram user/chat
   identity and update id; the API remains the owner of token consumption and account linking.
4. When the web application verifies channel membership, the API requests membership through the
   Gateway; the Gateway calls `getChatMember` and returns a normalized, bounded result.
5. When the notification dispatcher has a Telegram delivery, the Gateway performs the Telegram
   send operation while the primary system remains the owner of outbox state, leases, retries,
   dead-letter handling, and audit history.
6. When the Gateway is unavailable, the API returns a bounded provider-unavailable result and
   existing retry/expiry rules apply; no credit, link, or notification state is silently lost.
7. When the Gateway restarts or receives a duplicate Telegram update, processing is idempotent and
   does not duplicate account links, AI charges, messages, or notification deliveries.
8. When VPS 1 cannot reach Telegram directly, all supported Telegram features continue through VPS
   2.

## Acceptance Criteria

1. No production component on VPS 1 calls `api.telegram.org` directly.
2. The Bot Token exists only in the Gateway secret store/environment.
3. The Gateway has no database credentials and no direct access to PostgreSQL, Redis, or RabbitMQ.
4. Gateway-to-API calls use HTTPS, a dedicated service credential, rotation, rate limiting, and
   replay protection.
5. Assistant updates, `/start` linking, callbacks, membership verification, and notifications are
   verified end-to-end through VPS 2.
6. Existing actor/tenant authorization and Billing reservation/finalization semantics remain
   unchanged.
7. Telegram update, delivery, membership, and gateway failures are observable with correlation
   ids and redacted error categories.
8. Cutover and rollback can be completed without deleting persistent data or changing Telegram
   identity records.

## Security and Operations Rules

- Use a dedicated API client identity and least-privilege permission set for the Gateway.
- Allowlist the Gateway IP at the public API or reverse proxy where possible.
- Prefer mTLS or signed requests in addition to the service API key for production.
- Rotate the Bot Token and Gateway credential during the cutover.
- Persist Gateway offset/delivery metadata on VPS 2 or use Telegram webhook delivery with replay
  protection; in-memory-only offsets are insufficient for production reliability.
- Redact Bot Tokens, API keys, callback payloads, message text, and raw Telegram responses from
  ordinary logs.

## Rollback

Disable Gateway traffic, restore the previous single-host Telegram adapter, and keep all primary
database/outbox/link records intact. Rollback must not reuse an old Telegram update offset without
idempotency verification.
