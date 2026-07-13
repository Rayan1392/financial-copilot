# Tasks — Telegram Alert History and Explainability

## 1. Ownership and Immutable Record

- [ ] Make this feature owner of the actor-visible immutable alert record, explanation projection, feedback/dismiss/mute actions, search, and post-alert reaction analytics.
- [ ] Reference source `InsightEvent`, `AlertRule` trigger, report/announcement, Feature 097 intent/delivery attempts, and policy versions; do not duplicate detection or transport execution.
- [ ] Define `UserAlertRecord` at notification-decision time (including suppressed where product-visible) with actor/tenant, canonical symbol, source ids, delivery status, timestamps, evidence snapshot/hash, thresholds/baselines, detector/rule/preference versions, and correlation id.
- [ ] Distinguish detected event, rule trigger, notification intent, delivery attempt, delivered alert, dismissed alert, and muted future alerts in contracts and UI.

## 2. Persistence and Reproducibility

- [ ] Persist alert facts/evidence immutably; mutable delivery status is an append-only attempt/status timeline or separate projection, never an overwrite of source evidence.
- [ ] Enforce unique actor plus source-intent/decision identity so Feature 097 retries cannot create duplicate history records.
- [ ] Snapshot exact source metrics, units, observation time/freshness, threshold/operator, comparison baseline/window/sample, importance/confidence, detector/rule version, and why-text inputs.
- [ ] Add indexes for actor/time, symbol, category/type, delivery status, dismissed/muted/feedback state, source event, and cursor pagination.
- [ ] Define retention and legal/privacy deletion behavior: evidence/audit retention, reaction/feedback retention, and redaction/tombstone without cross-actor leakage.

## 3. Explainability and Reaction Analytics

- [ ] Build deterministic “why this alert” from persisted evidence and policy: what changed, observed value, threshold/baseline, source/freshness, matched user preference/rule, and delivery/suppression reason.
- [ ] If AI follow-up is offered, call the existing AI facade with actor-owned `alertId`, immutable evidence bundle, citations, and Billing reservation; forbid changed numbers or unsupported advice.
- [ ] Define similar-event search methodology by detector/version, symbol/industry, magnitude band, and historical window; disclose sample and avoid success-rate claims without methodology.
- [ ] Define post-alert price reaction horizons from canonical quotes/trades, anchor price/time, session/calendar adjustment, corporate-action handling, missing data, units, and calculation version.
- [ ] Store reaction snapshots/version separately and allow later completion/correction without mutating original alert evidence; label them descriptive, not recommendation/performance marketing.

## 4. Use Cases, API, and Telegram UX

- [ ] Implement paginated/searchable history and detail, deterministic explanation, feedback, dismiss/restore, mute symbol/category handoff to Feature 097, similar events, and reaction refresh use cases.
- [ ] Enforce actor/tenant ownership for every id and source reference; unauthorized/not-found responses must not reveal another actor's alert.
- [ ] Specify cursor pagination with symbol/type/status/date/dismissed/delivery filters, stable ordering/tie-breaker, bounded date range/page size, and retention-aware results.
- [ ] Specify alert detail with immutable evidence, source link/citations, detector/rule/preference versions, delivery timeline, why explanation, similar events, reaction availability, and correlation id.
- [ ] Provide Telegram `/alerts`, filters/pagination, detail, why, source, dismiss, mute, feedback, and AI follow-up callbacks with version/replay/ownership checks.
- [ ] Distinguish dismissing one record from muting future notifications and require confirmation for broader mute changes.

## 5. Background Processing, Security, and Observability

- [ ] Consume Feature 097 terminal/decision events idempotently; backfill reaction horizons on due schedules with distributed lease, bounded concurrency, retry/backoff, and poison handling.
- [ ] Protect evidence/source links, actor feedback, and Telegram identifiers with actor isolation, authorization, rate limits, minimized logs, and retention controls.
- [ ] Emit history creation lag, duplicate prevention, delivery outcome, explanation availability, reaction completion/missing/correction, search latency, feedback, dismiss, and mute metrics.
- [ ] Trace detection/rule through notification decision/attempt to alert record, explanation, AI reservation, reaction version, and feedback.

## 6. Tests and Acceptance Scenarios

- [ ] Unit-test why-text for every source type, threshold/baseline accuracy, delivery timeline, pagination ordering, similar-event criteria, and reaction horizons/session adjustment.
- [ ] Integration-test Feature 097 outcome consumption, actor isolation, immutable evidence, search/filter/cursor pagination, dismiss versus mute, feedback, AI evidence faithfulness, and retention behavior.
- [ ] Replay/concurrency-test duplicate delivery outcomes and reaction workers; one alert record and one version per reaction horizon/input revision result.
- [ ] Given a delivered alert, when detail/why is requested, then exact source metrics, threshold, baseline, freshness, versions, and delivery status are returned reproducibly.
- [ ] Given an unauthorized actor or expired/removed source link, when detail is requested, then no cross-actor evidence is disclosed and retained snapshots remain explainable per policy.
- [ ] Given a reaction horizon is incomplete or market data is missing, when shown, then it is pending/unavailable with reason rather than calculated from guessed prices.

## Completion Gate

- [ ] Keep tasks unchecked until immutability, ownership, search/pagination, reaction methodology, replay, retention, Persian callbacks, and AI faithfulness tests pass.
