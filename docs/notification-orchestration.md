# Notification Orchestration and Noise Control

Feature 097 owns durable proactive Telegram delivery for notification intents emitted by Features 090–096. Producers detect or generate facts and call `INotificationIntentPublisher`; they never call Telegram directly. Feature 099 consumes terminal outcome handoffs and remains the owner of the user-facing immutable explanation/history projection.

## Boundaries

- Domain policy owns preference invariants, precedence, quiet-hour behavior, critical-priority bypass, and valid intent lifecycle transitions.
- Application contracts own producer, preference, dispatcher, recipient, entitlement, transport, and operator ports.
- Infrastructure translates Billing plan capabilities, Telegram account links, EF persistence, actor timezones, and Telegram Bot API outcomes.
- API and Telegram settings commands are thin adapters over `INotificationUseCases`.
- The worker only schedules bounded dispatcher iterations.

The lifecycle is `Pending`, `Deferred`, `Batched`, `Sending`, `Delivered`, `Suppressed`, `Expired`, `FailedRetryable`, `DeadLettered`, or `Cancelled`. Only a DataAdmin manual-retry operation may reopen a dead-lettered intent.

## Preference precedence

The effective order is:

1. Billing capability `Notifications.Telegram`.
2. Explicit canonical-company mute.
3. Event-category enablement and category severity/cooldown overrides.
4. Actor global minimum severity and cooldown.
5. Actor-local daily cap.
6. Actor-local quiet hours.
7. Immediate or digest delivery mode.

`Critical` events may bypass the daily cap, cooldown, and quiet hours. They never bypass entitlement or explicit category/symbol mutes. Overnight quiet windows are supported. `Asia/Tehran` is the product default, and every decision snapshots the preference version and policy version used.

Queued intents are evaluated against the latest preference. The effective version is then persisted with the decision. Digest intents join a unique actor/channel/schedule batch, are ordered by priority and creation time, and overflow remains in the same durable window for a subsequent bounded dispatch. Expiry remains authoritative.

## Idempotency and retries

- Producer uniqueness: tenant + actor + actor type + channel + producer deduplication key.
- Worker ownership: persisted lease token and expiry with bounded claims.
- Delivery uniqueness: stable intent/batch part key with a database-enforced single successful attempt.
- Telegram `429` honors `retry_after`; `5xx`, timeout, and network failures use capped exponential backoff with jitter.
- Blocked/invalid chats and other permanent `4xx` responses are dead-lettered without retry.
- Provider message ids and every attempt are persisted. Multipart retries skip already delivered parts.
- No credit reservation or ledger write occurs during delivery. Producer-side billable work is never repeated by a notification retry.

Terminal results create sequenced `NotificationOutcomeHandoffs` for Feature 099. A manual retry can therefore produce a later terminal outcome without overwriting the original failed outcome.

## User and operator APIs

Authenticated actor APIs:

```text
GET /api/v1/notifications/me/preferences
PUT /api/v1/notifications/me/preferences
GET /api/v1/notifications/me/history?offset=0&pageSize=25
```

Telegram supports `/notifications` (or `/settings`) plus versioned callback buttons. Text commands manage mode, timezone, quiet hours, severity, daily cap, category mute, followed-symbol mute, and reset. Symbol overrides accept canonical companies already present in the actor’s followed-symbol list; no notification-specific watchlist exists.

DataAdmin operations:

```text
GET  /api/v1/admin/notifications/dead-letters
POST /api/v1/admin/notifications/dead-letters/{notificationIntentId}/retry
```

Every preference change and manual retry is audited with actor, tenant, correlation, time, and a bounded snapshot/detail.

## Configuration and rollout

The dispatcher is deliberately disabled in source-controlled defaults. Apply both migrations, provide the bot token through secrets/environment, and then enable it:

```powershell
$env:Telegram__Notifications__BotToken = "<secret>"
$env:Notifications__Dispatcher__Enabled = "true"
```

Relevant settings:

```json
{
  "Notifications": {
    "Dispatcher": {
      "IntervalSeconds": 10,
      "BatchSize": 100,
      "LeaseSeconds": 90,
      "MaximumAttempts": 5,
      "InitialBackoffSeconds": 10,
      "MaximumBackoffSeconds": 900,
      "DigestMaximumItems": 25,
      "MessagePartLength": 3800,
      "TransportErrorRetentionDays": 30,
      "DeliveryAuditRetentionDays": 730
    }
  }
}
```

Apply migrations `ImplementNotificationOrchestration` and `SeedTelegramNotificationCapability` before enabling dispatch. Durable delivery/suppression/outcome audit is retained for the configured long window by operations; redacted transport-error detail is automatically cleared after the shorter retention window. Raw Telegram provider responses and tokens are never persisted.

## Observability

Metrics cover queue depth/age, policy action and suppression reason, digest size, duplicate-part prevention, delivery/provider latency, success, retry, and dead letters. Correlation, source-event identity, evidence reference, preference version, policy version, intent, batch, attempt, provider message id, and outcome handoff provide the trace chain without logging message content, Telegram ids, or tokens.
