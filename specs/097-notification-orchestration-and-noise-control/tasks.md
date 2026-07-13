# Tasks — Notification Orchestration and Noise Control

## 1. Ownership and Domain

- [ ] Make this feature the sole owner of notification intents/outbox, preference evaluation, suppression, batching/digests, Telegram delivery attempts/retries, and delivery audit for Features 090–096.
- [ ] Keep event detection in producers and immutable user alert/explanation history in Feature 099; Feature 097 records transport outcomes and suppression decisions.
- [ ] Define event categories shared by all producers, priority/severity, channel, delivery mode, and stable event/entity identity conventions.
- [ ] Define `NotificationIntent` lifecycle `Pending`, `Deferred`, `Batched`, `Sending`, `Delivered`, `Suppressed`, `Expired`, `FailedRetryable`, `DeadLettered`, `Cancelled` with valid transitions.

## 2. Preferences and Precedence

- [ ] Define actor/tenant `NotificationPreference` with timezone, immediate/digest mode, quiet-hours window (including overnight), sensitivity/minimum priority, daily cap, and digest schedule.
- [ ] Define category preferences and per-symbol overrides/mutes using canonical company identity; no second watchlist is created.
- [ ] Apply precedence: legal/system critical policy, explicit per-symbol override, event-category preference, actor global preference, then product default; document which priority may bypass quiet hours/caps.
- [ ] Define cooldown and duplicate suppression separately: dedup blocks the same event/delivery identity; cooldown suppresses similar events within a policy window.
- [ ] Define batching/digest rules, maximum items/age, ordering, overflow, expiration, and behavior when preferences change while intents are queued.

## 3. Persistence and Idempotency

- [ ] Persist preferences, immutable `NotificationIntent` payload/evidence reference, suppression decision/reason, batch membership, and one-to-many delivery attempts.
- [ ] Enforce unique producer deduplication key plus channel/actor and unique delivery-part idempotency key; never rely only on in-memory locks.
- [ ] Add indexes for due/status/priority, actor history, batch window, expiry, and dead letter; use concurrency token/lease fields for workers.
- [ ] Snapshot effective preference/policy version at decision time while retaining source event id and correlation id.
- [ ] Define retention: durable delivery/suppression audit long enough for Feature 099 and operations, shorter retention/redaction for raw transport errors/payloads.

## 4. Dispatcher and Workers

- [ ] Define one producer contract to enqueue an intent atomically/idempotently with source event/rule/report evidence; producers never call Telegram directly.
- [ ] Evaluate entitlement and effective preferences, quiet hours, sensitivity, cooldown, daily cap, priority bypass, expiry, and batching before delivery.
- [ ] Schedule deferred quiet-hour release and digest assembly using actor timezone and Tehran defaults without daylight/date-boundary duplication.
- [ ] Claim due work with distributed lease/skip-locked equivalent, bounded global/per-chat concurrency, and ordered multipart delivery.
- [ ] Implement Telegram retry classification: honor `retry_after`, exponential backoff/jitter for transient failures, no retry for blocked/invalid chat, capped attempts, and dead-letter operations.
- [ ] Persist provider message ids and part statuses so retries resume unsent parts without duplicating delivered parts; callbacks/replays are idempotent.

## 5. API and User Interaction

- [ ] Specify get/update preferences, category/per-symbol override, mute/unmute, digest schedule, and paginated notification history contracts with optimistic versioning.
- [ ] Provide Telegram settings menus for immediate/digest, quiet hours/timezone, sensitivity, categories, per-symbol mute, daily cap visibility, and reset defaults.
- [ ] Return effective preference explanation and suppression/delivery status; localize invalid timezone/window, stale callback, rate limit, and transport failures.
- [ ] Hand successful/suppressed/failed terminal outcomes to Feature 099 without duplicating its evidence/explanation projection.

## 6. Security and Observability

- [ ] Enforce actor/tenant isolation, callback ownership, service authentication for producers, rate/abuse limits, and secret storage for Telegram transport.
- [ ] Minimize/redact message content, Telegram ids, tokens, and provider payloads; audit preference changes, priority bypass, manual retry, and dead-letter actions.
- [ ] Emit queue depth/age, decision/suppression reason, delivery latency/success, retry/dead-letter, duplicate prevention, quiet-hour deferral, cap, batch size, and provider latency.
- [ ] Trace producer event through preference snapshot, intent, batch, delivery attempts/provider ids, Billing correlation where applicable, and Feature 099 record.

## 7. Tests and Acceptance Scenarios

- [ ] Unit-test precedence, overnight quiet hours/timezones, digest/immediate, dedup versus cooldown, caps, priority bypass, batching, expiry, and state transitions.
- [ ] Integration-test transactional enqueue, actor isolation, worker leasing, provider retry classifications, multipart resume, blocked bot, dead letter, and Feature 099 outcome handoff.
- [ ] Concurrency/replay-test duplicate producers/workers/callbacks; one intent and at most one successful delivery per part are recorded.
- [ ] Given a duplicate event intent, when producers retry, then one intent survives and the original outcome is returned.
- [ ] Given quiet hours/digest preference, when a noncritical event arrives, then it is deferred/batched and later delivered once in the correct timezone.
- [ ] Given repeated transient Telegram failures, when attempts exhaust, then the intent is dead-lettered with audit/metrics and no producer or Billing work is repeated.

## Completion Gate

- [ ] Keep tasks unchecked until every producer uses the dispatcher and precedence, concurrency, retry, poison, Persian callback, and history-handoff tests pass.
