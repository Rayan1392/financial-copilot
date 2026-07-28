# Alert history and explainability

Order 99 owns the actor-visible alert-history projection for Telegram and API users.
It consumes terminal notification outcome handoffs from Feature 097 and creates one immutable
`UserAlertRecord` per actor plus notification decision sequence.

## Ownership boundary

- Detection remains upstream in `InsightEvent`, `AlertRule`, and `AlertRuleTrigger`.
- Delivery remains upstream in `NotificationIntent`, `NotificationDeliveryAttempt`, and Feature 097 dispatch.
- This feature owns the user-facing history record, deterministic explanation, feedback/dismiss actions,
  mute handoff to notification preferences, similar-event search metadata, and reaction snapshots.

The projection copies only reproducibility evidence. It does not rerun detectors, resend Telegram messages,
or overwrite source detection/transport records.

## Persistence

New Financial ingestion tables:

- `UserAlertRecords`: immutable alert facts/evidence snapshot, evidence hash, actor/tenant scope,
  detector/rule/preference/policy versions, terminal delivery status, correlation id, and retention markers.
- `UserAlertDeliveryTimeline`: append-only delivery attempts and terminal outcome entries.
- `UserAlertReactionSnapshots`: versioned post-alert reaction horizons (`H1`, `H24`, `D5`) stored separately
  from alert evidence.

Duplicate prevention is enforced by `NotificationIntentId + OutcomeSequence`, so retries or replayed handoffs
cannot create duplicate history records.

## API surface

- `GET /api/v1/alerts/me/history`
- `GET /api/v1/alerts/me/{alertId}`
- `GET /api/v1/alerts/me/{alertId}/why`
- `GET /api/v1/alerts/me/{alertId}/similar`
- `POST /api/v1/alerts/me/{alertId}/dismiss`
- `POST /api/v1/alerts/me/{alertId}/restore`
- `POST /api/v1/alerts/me/{alertId}/feedback`
- `POST /api/v1/alerts/me/{alertId}/mute`
- `POST /api/v1/alerts/me/{alertId}/reaction-refresh`
- `POST /api/ai/v1/query` accepts `context.alertId` and prepends the immutable evidence bundle to the AI request.

All alert ids are resolved through the current actor and tenant. Unauthorized or cross-actor ids return not found.

## Telegram surface

- `/alerts [symbol]`: latest alert-history records for the linked actor.
- `/alert ALERT_ID`: detail view with evidence hash, why text, reaction availability, and action buttons.
- Callback actions: detail, why, dismiss, mute symbol, and feedback.

Dismiss affects one record only. Mute changes future notification preferences and is handled through Feature 097
preference updates.

## Reaction analytics policy

Reaction snapshots are descriptive analytics, not recommendations or performance marketing. When canonical quote
horizons are incomplete or unavailable, the snapshot is marked `Unavailable` with a reason. The system must not
calculate reactions from guessed prices.

## Retention and privacy

Immutable evidence is retained for audit and explainability, with a redaction/tombstone path for privacy deletion.
Feedback and reaction snapshots are separate mutable projections, so correction or deletion does not rewrite the
source alert evidence.
