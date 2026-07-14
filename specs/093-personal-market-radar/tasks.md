# Tasks — Personal Market Radar

## 1. Ownership and Domain

- [x] Reuse Feature 085 `FollowedSymbol` as the only watchlist model and Features 084/092 as the only event producers; radar must not infer holdings or duplicate detectors.
- [x] Make this feature owner of radar enablement, selection/sensitivity policy, per-symbol overrides, composite-event relevance, and event-to-notification-intent matching.
- [x] Define `RadarPreference` lifecycle `Active`, `Paused`, `Removed` and clarify that removing a followed symbol stops future monitoring without deleting historical events/alerts.
- [x] Define plan-governed followed-symbol/radar limits through Billing capabilities; do not add local plan-name checks.

## 2. Persistence and Preference Model

- [x] Persist one actor/tenant radar profile with enabled event categories, minimum severity/importance, sensitivity, delivery mode reference, and active state.
- [x] Persist optional per-symbol overrides keyed to canonical `ExternalCompanyId`, with event-category/sensitivity overrides and inheritance from the profile.
- [x] Define precedence: per-symbol override, radar profile, then Feature 097 global notification preference; Feature 097 quiet hours/caps/mutes always govern delivery.
- [x] Store event matching checkpoints/idempotency state, not copied detector evidence; reference immutable `InsightEvent` ids.
- [x] Index actor/state and company/event matching; use optimistic concurrency for preference updates and audit every change.

## 3. Application and Evaluation

- [x] Implement get/update/enable/pause radar and per-symbol override use cases with actor isolation, canonical company resolution, entitlement, and plan limits.
- [x] Match new persisted events to active followed symbols, allowed types, source freshness, severity, importance, and effective sensitivity.
- [x] Define sensitivity as governed threshold profiles, not opaque AI judgment, and persist the applied profile/version in match evidence.
- [x] Define composite-event logic over a bounded time window with required component event identities, deterministic score, and prevention of both component and composite spam.
- [x] Compare current event magnitude/rarity to historical events for the symbol and include comparison evidence without changing the source event.
- [x] Publish eligible intents only through Feature 097 with stable actor/event/radar-policy key; paused/removed/over-limit items never enqueue.

## 4. API, Telegram, and Scheduling

- [x] Specify `GET /api/v1/radar/me`, `PUT /api/v1/radar/me/preferences`, per-symbol override operations, and test-notification contract that is clearly synthetic and non-billable.
- [x] Provide Telegram inline controls for enable/pause, event categories, sensitivity, followed-symbol overrides, and pagination with actor/version/replay validation.
- [x] Process events event-driven where possible; define fallback polling cadence from source freshness and never promise sub-minute alerts when upstream data is slower.
- [x] Use durable consumer checkpoints, bounded concurrency, per-actor/event idempotency, retry/backoff, poison handling, and distributed ownership.

## 5. Security and Observability

- [x] Enforce actor/tenant isolation and feature limits on reads/writes and evaluation; redact preference/user identifiers from market-event logs.
- [x] Emit event-to-radar match latency, matched/suppressed counts by reason, sensitivity profile, composite formation, notification handoff, and plan-limit denial.
- [x] Correlate followed symbol, insight event, radar policy/version, notification intent, and Feature 099 alert record.

## 6. Tests and Acceptance Scenarios

- [x] Unit-test preference inheritance, lifecycle, sensitivity profiles, composite windows, historical comparison, and plan limits.
- [x] Integration-test actor isolation, only-followed-symbol matching, no detector duplication, Feature 097 precedence/handoff, and removal/pause behavior.
- [x] Concurrency/replay-test duplicate events and workers create one intent per actor/event/policy.
- [x] Given an active followed symbol and qualifying event, when radar evaluates it, then one intent is queued with the applied sensitivity and evidence reference.
- [x] Given a paused/removed watch item or global mute, when an event arrives, then no delivery intent is created and the suppression reason is auditable.
- [x] Given upstream freshness cannot support sub-minute evaluation, when status is shown, then actual cadence/freshness is disclosed rather than overstated.

## Completion Gate

- [x] Keep tasks unchecked until plan-limit, precedence, composite, cadence, retry, and cross-feature tests pass.
