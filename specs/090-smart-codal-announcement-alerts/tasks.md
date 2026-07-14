# Tasks - Smart Codal Announcement Alerts

## 1. Dependencies and Ownership

- [x] Reuse existing Codal/Noavaran announcement ingestion and canonical company identity; this feature must not poll Codal or persist a competing announcement model.
- [x] Reuse Feature 085 followed symbols, Feature 084 event identity/evidence conventions, Feature 097 notification dispatcher/preferences, and Feature 013 Billing for optional AI work.
- [x] Make this feature owner of Codal subscription/filter policy and announcement-to-alert-intent orchestration; Feature 097 owns delivery status/retries and Feature 099 owns user-visible immutable alert history.

## 2. Domain and Persistence

- [x] Define `CodalAlertSubscription` with actor/tenant, canonical `ExternalCompanyId`, governed announcement types, minimum importance, raw-alert flag, AI-summary flag, active/paused state, and audit timestamps.
- [x] Define normalized announcement identity from authoritative provider plus immutable external announcement id; never deduplicate by mutable title alone.
- [x] Define deterministic evidence schema for announcement number/type, published/period dates, source URL/source identity, extracted monetary values/units, comparison periods, extraction version, freshness, and source checksum.
- [x] Enforce one effective subscription per actor/company (or documented filter-set uniqueness), actor ownership, canonical company FK/reference, and indexed active subscription lookup.
- [x] Persist subscription lifecycle/audit changes; event/delivery/history persistence remains in Features 084/097/099 rather than a parallel alert table.

## 3. Detection and Deterministic Extraction

- [x] Consume completion events from the existing announcement ingestion boundary and create/reuse a canonical `InsightEvent` with stable event identity and detector version.
- [x] Map governed announcement categories and significance using documented deterministic rules; preserve unknown categories rather than guessing.
- [x] Extract dates, periods, amounts, units, ratios, and prior-period comparisons deterministically with evidence offsets/field paths and explicit missing/ambiguous states.
- [x] Match active subscriptions by canonical company, allowed types, and importance; one announcement/subscription combination produces at most one raw notification intent.
- [x] Keep raw alert generation independent of AI summary availability, latency, credit state, or provider failure.

## 4. AI Summary Flow

- [x] Build an immutable evidence bundle from the persisted announcement and deterministic extraction; never send unsupported inferred facts as evidence.
- [x] Reserve/commit Billing credits for an actor-requested or policy-enabled AI summary and release on generation failure according to Feature 013.
- [x] Persist summary version/model/prompt-policy metadata and evidence hash; prohibit invented sentiment, causality, valuation, or recommendation language.
- [x] If the raw alert was already delivered, publish a separate correlated `SummaryReady` notification intent when the summary completes; do not resend the raw alert.
- [x] Isolate failures: extraction gaps may degrade the raw message, while AI failure produces an explicit unavailable state and cannot retract or block the source-link alert.

## 5. API and Telegram UX

- [x] Specify create/list/update/pause/delete subscription contracts with canonical company id, announcement categories, importance, and AI-summary preference; validate plan limits and actor ownership.
- [x] Provide Telegram commands/inline keyboards to select followed symbol, categories, importance, raw-only versus AI summary, pause, and remove with pagination and callback replay protection.
- [x] Render raw alerts with title, symbol, published time, type, deterministic facts, source link/source identity, freshness, and correlation id; label delayed summaries clearly.
- [x] Return localized invalid-symbol, unsupported-type, entitlement, duplicate-subscription, stale-source, and provider/AI-unavailable outcomes.

## 6. Background Processing, Security, and Observability

- [x] Consume announcement events through durable outbox/queue with idempotent consumer, bounded concurrency, retry/backoff, poison handling, and distributed ownership/lease as needed.
- [x] Publish all outbound intents through Feature 097 so quiet hours, preferences, deduplication, caps, retry, and Telegram delivery are applied consistently.
- [x] Enforce actor/tenant isolation and entitlement at subscription mutation and execution; redact Telegram/user identifiers and announcement payload content where unnecessary.
- [x] Measure ingestion-to-detection, detection-to-raw-intent, summary latency, extraction failures, duplicate suppression, Billing outcomes, and delivery handoff failures.
- [x] Correlate announcement identity, insight event, subscription, usage reservation, summary version, notification intent, and alert history record.

## 7. Tests and Acceptance Scenarios

- [x] Unit-test category mapping, significance, period comparison, financial-number/unit extraction, event/delivery keys, and source-link validation.
- [x] Integration-test subscription ownership/limits, ingestion-event consumption, exact-once raw intent, Feature 097 preference handoff, and Feature 099 evidence linkage.
- [x] Retry/concurrency-test duplicate ingestion events and simultaneous consumers; one event and one intent per eligible subscription must result.
- [x] Billing/provider tests must prove AI failure or insufficient credits never suppresses the raw alert and never leaves a committed duplicate charge.
- [x] Given a new matching announcement, when ingestion completes, then a raw evidence-backed intent is created once and includes the authoritative source link/source identity.
- [x] Given AI summary is enabled and succeeds later, when it becomes ready, then a correlated follow-up is queued without duplicating the raw alert.
- [x] Given an unknown/ambiguous value or provider failure, when the alert is rendered, then it is marked unavailable/ambiguous rather than guessed.

## Completion Gate

- [x] Keep tasks unchecked until ingestion contract, extraction, deduplication, Billing isolation, notification handoff, and Persian Telegram tests pass.
- [x] Confirm no parallel Codal polling, notification dispatcher, or alert-history store was introduced.

## Implementation Notes

- 2026-07-13: Implemented `CodalAlertSubscription` domain policy, application use cases, EF persistence/migrations, `/api/v1/codal-alerts/me/subscriptions` create/list/update/delete endpoints, followed-symbol ownership validation, and `SubscribedCodalAnnouncementDetector`.
- 2026-07-13: Detector emits `CodalAnnouncementMatched` insight events from existing monthly-report and financial-statement ingestion facts only when an active subscription matches. It publishes idempotent raw `NotificationIntent` rows with evidence, source provider/entity/period, freshness, and correlation data; no parallel Codal polling, notification dispatcher, alert-history store, or separate announcement table was introduced.
- 2026-07-13: Added Billing-backed `POST /api/v1/codal-alerts/me/insights/{insightEventId}/summary`, persistent `CodalAlertSummaries`, evidence hashing, provider-neutral summarization, reservation commit/release behavior, and correlated `CodalAlertSummaryReady` notification intents.
- 2026-07-13: Added Telegram `/codal_alerts` guidance and `calert.summary.v1:{insightEventId}` callback handling through the existing Telegram assistant adapter/idempotency path.
- 2026-07-13: Validation passed: API Release build, `MarketInsight084Tests` unit slice 4/4, `MarketInsightEndpointTests` integration slice 10/10, architecture tests 7/7.
