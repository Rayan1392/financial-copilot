# Features 087–099 Task Refinement Review

## Review Scope

Reviewed all `user-story.md` and `tasks.md` files for Features 087 through 099, `specs/implementation-checklist.md`, `specs/README.md`, neighboring implemented Features 084–086, and the existing Identity, Billing, AI orchestration, followed-symbol, insight-event, market-data, and outbox terminology.

## Files Changed

- `specs/087-telegram-account-linking-and-channel-identity/tasks.md`
- `specs/088-telegram-channel-membership-and-free-entitlement/tasks.md`
- `specs/089-telegram-ai-assistant-adapter/tasks.md`
- `specs/090-smart-codal-announcement-alerts/tasks.md`
- `specs/091-conditional-symbol-tracker/tasks.md`
- `specs/092-market-microstructure-event-detectors/tasks.md`
- `specs/093-personal-market-radar/tasks.md`
- `specs/094-professional-scanners-and-ready-filters/tasks.md`
- `specs/095-market-pulse-and-key-statistics/tasks.md`
- `specs/096-ai-market-report-and-personal-digest/tasks.md`
- `specs/097-notification-orchestration-and-noise-control/tasks.md`
- `specs/098-telegram-subscriptions-credit-purchases-and-entitlements/tasks.md`
- `specs/099-telegram-alert-history-and-explainability/tasks.md`
- `specs/reviews/features-087-099-task-refinement-report.md`

No `user-story.md` required modification: the stories state the intended scope sufficiently, while their generic task templates were the material ambiguity. The implementation checklist was not changed because Features 087–099 are still proposed, their statuses must remain unchanged, and the package-level dependency sequence already exists in `specs/README.md`.

## Major Ambiguities Found

- All thirteen task files repeated one generic template and did not define feature-specific aggregates, lifecycle states, constraints, APIs, worker behavior, failure semantics, or acceptance scenarios.
- The request's feature-specific labels swapped Features 092 and 093 relative to the repository: the repository owns microstructure/large-trade detection in 092 and personal radar in 093. The refinement preserves repository numbering and applies each requested concern to its actual owning folder.
- The documents did not consistently distinguish Telegram identity from canonical actor identity or Telegram chat from Telegram user.
- Free, subscription, and purchased-credit allocation order and timezone/non-rollover behavior were unstated.
- Alert-producing features could each be read as owning delivery, deduplication, preferences, retries, and history.
- “Watchlist”, tracked symbol, alert rule, market event, notification, and alert history were not consistently separated.
- Microstructure formulas, ready-filter contracts, pulse snapshot correction, and post-alert reaction methodology lacked reproducibility requirements.
- AI reports/summaries did not fully specify evidence-first validation, fallback, version history, or Billing rollback behavior.

## Ownership Decisions

- Feature 087 exclusively owns Telegram account links, link tokens, unlink/relink, and Telegram identity resolution. Numeric `TelegramUserId`, never username, is stable identity.
- Feature 088 owns channel-membership verification/cache and free-daily-allowance eligibility; Feature 013 remains the ledger, wallet, reservation, subscription, and purchased-credit owner.
- Feature 089 is a thin Telegram adapter over the existing AI facade, conversation, symbol resolution, telemetry, and Billing paths.
- Feature 085 remains the only followed-symbol/watchlist model. Feature 093 applies radar policy to it and does not model holdings.
- Features 084 and 092 own canonical detected market events and evidence identity; 090 and 091 add subscription/rule-specific trigger orchestration without parallel event stores.
- Feature 097 exclusively owns notification preferences, deduplication, cooldown, quiet hours, batching, outbox, Telegram delivery, retry, dead letter, and delivery audit.
- Feature 099 owns the immutable actor-visible alert-history/explanation projection and post-alert reaction analytics, referencing Feature 097 delivery outcomes.
- Feature 095 owns deterministic immutable market-pulse facts; Feature 096 owns evidence-bound narrative/report versions.
- Feature 094 owns governed ready-filter catalog/saved references while existing scanner execution and metric semantics remain authoritative.

## Cross-Feature Dependencies and Recommended Order

1. `087 -> 088 -> 089` establishes identity, free entitlement, and Telegram AI transport.
2. `092` establishes shared microstructure event formulas and canonical evidence.
3. `097` must precede production delivery from `090`, `091`, `093`, or reports from `096`.
4. `090` consumes existing announcement ingestion; `091` consumes governed metrics/events; `093` consumes `085` followed symbols plus `084/092` events.
5. `094` consumes existing scanner/semantic layers plus `092`; `095` consumes canonical market data plus `092` definitions.
6. `096` consumes `095` facts and `084/090/092` events, then hands delivery to `097`.
7. `098` reuses `013/035` Billing/admin ownership and may follow the basic Telegram journey.
8. `099` consumes stable event/rule and `097` outcome contracts and should follow the notification foundation.

## Assumptions Retained

- Canonical ownership is represented by the repository's actor/tenant context; the exact user-versus-API-client eligibility for Telegram remains an implementation validation.
- Tehran timezone is used for the free daily allowance and trading calendar unless product configuration explicitly selects another supported timezone.
- Existing Billing capabilities can be extended with allocation-source metadata and product/payment workflows rather than replaced.
- Existing `InsightEvent`, `FollowedSymbol`, Conversation/Message, provider source-priority, and AI orchestration abstractions remain authoritative.
- Deterministic notifications are not charged unless a later Billing policy explicitly makes them metered; AI summary/report operations follow existing reservation/finalization policy.

## Product Decisions Still Required

- Exact link-token lifetime and whether a canonical actor may ever link more than one Telegram account in a future plan.
- Exact free/subscription/purchased allocation order confirmation, daily allowance expiry time, and cached-membership grace duration.
- Approved microstructure formulas, market/instrument scope, baseline windows, thresholds, and “smart money” terminology.
- Plan limits for followed symbols, active alert rules, radar, filter runs, notifications, and manual report generation.
- Which notification priorities may bypass quiet hours/daily caps and the default digest schedule/timezone behavior.
- Manual receipt-review MVP operating policy, accepted receipt types, renewal/refund/grace rules, and future payment provider.
- Alert-history/evidence retention and approved post-alert reaction horizons/methodology.

## Intentionally Out of Scope

- Source code, migrations, controllers, services, DTOs, entities, tests, configuration, or runtime changes.
- Telegram shadow users, Telegram-specific wallets/subscriptions/ledgers, alternate AI pipelines, direct provider access from bot handlers, or parallel notification/event stores.
- Portfolio holdings, brokerage integration, order execution, investment advice, guaranteed signals, or unsupported causal/performance claims.

## Verification Statement

This refinement changes specification/planning files only. No feature was marked implemented and no task checkbox was completed. No source code or runtime file was changed by this review.
