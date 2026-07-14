# Tasks — Conditional Symbol Tracker

## 1. Dependencies and Ownership

- [x] Reuse canonical company identity, Feature 015 metric definitions/units, Feature 084/092 evidence and event identity, and Feature 097 notification delivery/preferences.
- [x] Make this feature the owner of user-authored `AlertRule`, natural-language-to-governed-rule parsing, rule lifecycle, evaluation state, crossing/reset semantics, and trigger records.
- [x] Keep followed symbols (085), radar preferences (093), detected market events (084/092), delivered notifications (097), and alert history (099) distinct.

## 2. Rule Domain Model

- [x] Define `AlertRule` aggregate with actor/tenant, canonical company, governed `RuleType`, metric/event code, operator, decimal threshold, unit, baseline window, recurrence, cooldown, reset policy, session policy, state, version, and timestamps.
- [x] Govern price, percentage change, volume, trading value, buyer power, real-money flow, buy/sell queue, Codal publication/type, and financial metric conditions; reject unsupported type/operator/unit combinations.
- [x] Define lifecycle `Draft -> Active <-> Paused -> Removed` and one-shot `Active -> Triggered/Completed`; removed rules are not evaluated and remain auditable.
- [x] Define threshold crossing from prior eligible observation to current observation, equality behavior, missing prior data, gap/opening values, and stale/out-of-order observation rejection.
- [x] Define recurring reset/re-arm conditions (cross back, next market session, or explicit hysteresis) and ensure cooldown suppresses delivery without corrupting evaluation state.
- [x] Define natural-language parsing as a proposal producing the normalized rule plus Persian confirmation; no rule becomes active until validation and explicit user confirmation succeed.

## 3. Persistence and Integrity

- [x] Persist normalized rule fields rather than executable text/SQL; retain original text and parser version only as audit/display metadata.
- [x] Persist `AlertRuleEvaluationState` with last observation/value/time, armed state, last trigger, cooldown end, rule version, and concurrency token.
- [x] Persist immutable trigger evidence or create a canonical `InsightEvent` reference containing source metric/event, value, threshold, baseline, period, freshness, provider, and rule version.
- [x] Add actor/company/state and due-evaluation indexes, soft-remove audit fields, and optional semantic duplicate constraint/idempotency key for repeated create requests.
- [x] Use conditional updates/transactions so concurrent evaluators cannot trigger the same rule/evidence crossing twice.

## 4. Application Use Cases and Evaluation

- [x] Implement create-from-structured-input, parse-natural-language, confirm, list, get, update, pause/resume, and remove use cases with actor ownership and plan rule-count limits.
- [x] Resolve user symbols to canonical `ExternalCompanyId` and aliases/metrics through governed resolvers; return ambiguity for multiple matches.
- [x] Evaluate rules only against canonical persisted observations/events with compatible unit, source freshness, market-session state, and observation ordering.
- [x] Support one-shot versus recurring behavior, cooldown, hysteresis/reset, trading-session-only rules, closing-session rules, and report-driven rules that are not session-bound.
- [x] Publish a notification intent through Feature 097 with stable `(RuleId, RuleVersion, EvidenceIdentity, TriggerSequence)` deduplication key.
- [x] Return explicit invalid, ambiguous, unsupported, stale, paused, expired-entitlement, missing-data, and provider-unavailable outcomes.

## 5. API and Telegram Interaction

- [x] Specify `POST/GET/PATCH/DELETE /api/v1/trackers/me` contracts including normalized rule preview, confirmation token/version, lifecycle state, last evaluation, last trigger, and next eligibility.
- [x] Define Telegram creation flow with natural-language prompt, parsed rule card, edit/cancel/confirm callbacks, symbol/metric disambiguation, and paginated rule management.
- [x] Version callback payloads and validate actor, rule version, expiry, and replay; stale confirmation cannot activate a newer/changed rule.
- [x] Explain each trigger using exact observed value, threshold, crossing direction, baseline/window, source time, freshness, and recurring/reset status.

## 6. Background Processing, Security, and Observability

- [x] Define event-driven evaluation where canonical observations/events exist and bounded polling only for projections without events; document cadence per rule family and source freshness.
- [x] Partition work by rule/company, use distributed leases or queue ownership, bounded concurrency, retry/backoff, poison handling, and idempotent consumers.
- [x] Enforce actor/tenant isolation, entitlement and rule limits, input length/rate limits, and prohibit arbitrary expressions, SQL, scripts, or LLM-created executable logic.
- [x] Emit metrics for active rules by type, evaluation lag, stale skips, crossings, resets, cooldown suppressions, duplicates, failures, and notification handoff.
- [x] Trace observation/event through rule version, trigger evidence, notification intent, delivery, and Feature 099 history.

## 7. Tests and Acceptance Scenarios

- [x] Unit-test every rule/operator/unit family, crossing equality, reset/hysteresis, cooldown, one-shot completion, recurring re-arm, session boundaries, and out-of-order observations.
- [x] Test natural-language Persian normalization, ambiguity, unsupported expressions, confirmation expiry/version mismatch, and deterministic normalized representation.
- [x] Integration-test actor isolation, entitlement/rule limits, persistence indexes, event and polling evaluation, Feature 097 handoff, and Feature 099 evidence linkage.
- [x] Concurrency/replay-test simultaneous evaluators and duplicate market observations; a crossing creates one trigger and one notification intent.
- [x] Given a price-below rule with a prior value above the threshold, when a fresh in-session value crosses below, then one evidence-backed trigger is recorded.
- [x] Given a recurring rule remains true during cooldown, when more observations arrive, then no duplicate alert occurs until the reset/re-arm condition is met.
- [x] Given stale/missing/provider-failed data, when evaluation runs, then no trigger fires and the state/metrics explain why evaluation was skipped.

## Completion Gate

- [x] Keep tasks unchecked until all rule families, concurrency/session/expiry/provider-failure scenarios, and notification integration pass.
- [x] Confirm no executable user expression, duplicate detector, or parallel delivery mechanism was added.
