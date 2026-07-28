# Tasks — Notification Orchestration and Noise Control

## 1. Ownership and Domain

- [x] Make this feature the sole owner of notification intents/outbox, preference evaluation, suppression, batching/digests, Telegram delivery attempts/retries, and delivery audit for Features 090–096.
- [x] Keep event detection in producers and immutable user alert/explanation history in Feature 099; Feature 097 records transport outcomes and suppression decisions.
- [x] Define event categories shared by all producers, priority/severity, channel, delivery mode, and stable event/entity identity conventions.
- [x] Define `NotificationIntent` lifecycle `Pending`, `Deferred`, `Batched`, `Sending`, `Delivered`, `Suppressed`, `Expired`, `FailedRetryable`, `DeadLettered`, `Cancelled` with valid transitions.

## 2. Preferences and Precedence

- [x] Define actor/tenant `NotificationPreference` with timezone, immediate/digest mode, quiet-hours window (including overnight), sensitivity/minimum priority, daily cap, and digest schedule.
- [x] Define category preferences and per-symbol overrides/mutes using canonical company identity; no second watchlist is created.
- [x] Apply precedence: legal/system critical policy, explicit per-symbol override, event-category preference, actor global preference, then product default; document which priority may bypass quiet hours/caps.
- [x] Define cooldown and duplicate suppression separately: dedup blocks the same event/delivery identity; cooldown suppresses similar events within a policy window.
- [x] Define batching/digest rules, maximum items/age, ordering, overflow, expiration, and behavior when preferences change while intents are queued.

## 3. Persistence and Idempotency

- [x] Persist preferences, immutable `NotificationIntent` payload/evidence reference, suppression decision/reason, batch membership, and one-to-many delivery attempts.
- [x] Enforce unique producer deduplication key plus channel/actor and unique delivery-part idempotency key; never rely only on in-memory locks.
- [x] Add indexes for due/status/priority, actor history, batch window, expiry, and dead letter; use concurrency token/lease fields for workers.
- [x] Snapshot effective preference/policy version at decision time while retaining source event id and correlation id.
- [x] Define retention: durable delivery/suppression audit long enough for Feature 099 and operations, shorter retention/redaction for raw transport errors/payloads.

## 4. Dispatcher and Workers

- [x] Define one producer contract to enqueue an intent atomically/idempotently with source event/rule/report evidence; producers never call Telegram directly.
- [x] Evaluate entitlement and effective preferences, quiet hours, sensitivity, cooldown, daily cap, priority bypass, expiry, and batching before delivery.
- [x] Schedule deferred quiet-hour release and digest assembly using actor timezone and Tehran defaults without daylight/date-boundary duplication.
- [x] Claim due work with distributed lease/skip-locked equivalent, bounded global/per-chat concurrency, and ordered multipart delivery.
- [x] Implement Telegram retry classification: honor `retry_after`, exponential backoff/jitter for transient failures, no retry for blocked/invalid chat, capped attempts, and dead-letter operations.
- [x] Persist provider message ids and part statuses so retries resume unsent parts without duplicating delivered parts; callbacks/replays are idempotent.

## 5. API and User Interaction

- [x] Specify get/update preferences, category/per-symbol override, mute/unmute, digest schedule, and paginated notification history contracts with optimistic versioning.
- [x] Provide Telegram settings menus for immediate/digest, quiet hours/timezone, sensitivity, categories, per-symbol mute, daily cap visibility, and reset defaults.
- [x] Return effective preference explanation and suppression/delivery status; localize invalid timezone/window, stale callback, rate limit, and transport failures.
- [x] Hand successful/suppressed/failed terminal outcomes to Feature 099 without duplicating its evidence/explanation projection.

## 6. Security and Observability

- [x] Enforce actor/tenant isolation, callback ownership, service authentication for producers, rate/abuse limits, and secret storage for Telegram transport.
- [x] Minimize/redact message content, Telegram ids, tokens, and provider payloads; audit preference changes, priority bypass, manual retry, and dead-letter actions.
- [x] Emit queue depth/age, decision/suppression reason, delivery latency/success, retry/dead-letter, duplicate prevention, quiet-hour deferral, cap, batch size, and provider latency.
- [x] Trace producer event through preference snapshot, intent, batch, delivery attempts/provider ids, Billing correlation where applicable, and Feature 099 record.

## 7. Tests and Acceptance Scenarios

- [x] Unit-test precedence, overnight quiet hours/timezones, digest/immediate, dedup versus cooldown, caps, priority bypass, batching, expiry, and state transitions.
- [x] Integration-test transactional enqueue, actor isolation, worker leasing, provider retry classifications, multipart resume, blocked bot, dead letter, and Feature 099 outcome handoff.
- [x] Concurrency/replay-test duplicate producers/workers/callbacks; one intent and at most one successful delivery per part are recorded.
- [x] Given a duplicate event intent, when producers retry, then one intent survives and the original outcome is returned.
- [x] Given quiet hours/digest preference, when a noncritical event arrives, then it is deferred/batched and later delivered once in the correct timezone.
- [x] Given repeated transient Telegram failures, when attempts exhaust, then the intent is dead-lettered with audit/metrics and no producer or Billing work is repeated.

## Completion Gate

- [x] Every producer uses the dispatcher, and precedence, concurrency/replay, retry/dead-letter, Persian callback, actor-isolated history, and Feature 099 handoff tests pass. Validation: API and Worker Release builds; 981/981 unit tests; 27/27 notification and adjacent-producer integration tests; 7/7 architecture tests; both EF model-current checks; and `git diff --check`.
