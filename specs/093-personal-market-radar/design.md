# Design — Personal Market Radar

## Status

Implemented by checklist order 93.

## Architectural Boundary

Personal Market Radar is a personalization and matching capability. It owns radar preferences, deterministic selection policy, per-symbol overrides, composite relevance, durable checkpoints, and creation of notification intents. It does not own watchlists, event detection, notification delivery, or financial positions.

- Feature 085 `FollowedSymbol` is the only monitored-symbol source.
- Features 084 and 092 `InsightEvent` records are the only event source and immutable evidence boundary.
- Billing capability `Radar.Symbols` governs the maximum followed-symbol count.
- `IRadarNotificationPolicyGate` is the Feature 097 handoff boundary. Its eventual global mute, quiet-hour, cap, and channel decisions take precedence over radar matches.
- Feature 099 can correlate its alert record through `RadarEventMatch.NotificationIntentId`, the referenced insight event, and the stable radar deduplication key.

## Domain Model

`RadarProfile` is actor/tenant scoped and follows `Active`, `Paused`, and `Removed` lifecycle states. It stores enabled insight categories, minimum severity and importance, a governed sensitivity profile, delivery mode, version, and concurrency token.

`RadarSymbolOverride` is keyed by profile and canonical `ExternalCompanyId`. Nullable category, severity, importance, and sensitivity fields inherit from the profile. An override's effective settings precede profile settings; the Feature 097 policy gate still governs delivery.

Sensitivity is deterministic and versioned as `radar-sensitivity-v1`:

| Profile | Minimum severity | Minimum importance | Maximum source age |
| --- | --- | ---: | ---: |
| Broad | Informational | 30 | 60 minutes |
| Balanced | Notice | 50 | 30 minutes |
| Focused | Important | 70 | 15 minutes |

The effective threshold is the stricter of explicit profile/override settings and the selected sensitivity policy.

## Evaluation and Idempotency

The worker evaluates active profiles every 30 seconds by default, but API and Telegram status always disclose that delivery cannot be faster than upstream evidence freshness. Each batch:

1. acquires a database-backed profile lease with an owner token;
2. resolves the actor's current followed symbols;
3. validates the `Radar.Symbols` entitlement;
4. reads new, unexpired persisted insight events;
5. applies override inheritance, category, freshness, severity, importance, historical percentile, and sensitivity rules;
6. forms composites only from distinct event types within a bounded 30-minute window;
7. persists a match or suppression checkpoint referencing immutable insight-event ids;
8. passes eligible matches to `IRadarNotificationPolicyGate` and then publishes one `NotificationIntent`.

The unique radar deduplication key contains profile identity, preference version, insight identity, and component-set identity. Profile leases, unique indexes, and publisher idempotency prevent concurrent workers or replayed events from creating duplicate intents. Failure state is stored on the profile with exponential backoff; the configured retry threshold marks poison failures and moves them to a one-hour retry interval.

## Persistence

- `RadarProfiles`: actor-scoped preference, checkpoint, lease, retry, and source-freshness state.
- `RadarSymbolOverrides`: canonical per-symbol inherited settings.
- `RadarEventMatches`: match/suppression decision, applied policy version, comparison score, component ids, evidence reference, and notification intent link.
- `RadarPreferenceAudits`: versioned preference and override change snapshots.

No detector evidence is copied. A match stores the `InsightEvent` foreign key and a bounded evidence identity reference.

## Interfaces

HTTP:

- `GET /api/v1/radar/me`
- `PUT /api/v1/radar/me/preferences`
- `POST /api/v1/radar/me/enable`
- `POST /api/v1/radar/me/pause`
- `DELETE /api/v1/radar/me`
- `PUT|DELETE /api/v1/radar/me/symbols/{externalCompanyId}`
- `POST /api/v1/radar/me/test-notification`

The test notification is explicitly informational and non-billable.

Telegram provides `/radar [page]` and `/radar_override <company-id> <broad|balanced|focused|paused|inherit>`, plus versioned inline callbacks for lifecycle, categories, sensitivity, and override pause/resume/inheritance. Existing Telegram processed-update replay protection and linked-actor resolution apply to all radar callbacks.

## Observability and Security

Metrics cover match latency, matches, suppressions by reason, sensitivity, composite formation, notification handoff, and failures. Logs intentionally omit actor and preference identifiers. HTTP access requires actor-scoped `radar.read.self` or `radar.write.self` permissions, and repositories always key profiles by tenant, actor, and actor type.

## Validation Evidence

- Personal radar and Telegram focused unit tests: 16 passed.
- Radar endpoint integration tests: 3 passed.
- Architecture dependency tests: 7 passed.
- API and Worker builds: succeeded with zero warnings.
