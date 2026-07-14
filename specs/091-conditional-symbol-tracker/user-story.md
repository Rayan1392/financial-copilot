# User Story — Conditional Symbol Tracker

## Status
`[x]` Implemented

## Feature
Let users define price, volume, money-flow, fundamental, and Codal conditions and receive a notification when the condition becomes true.

## Story

As a TahlilApp-AI user,

I want to define a condition for a symbol and be notified when it becomes true,

so that I can delegate continuous monitoring instead of repeatedly checking prices and reports.

## Business Context

Rules are governed data with typed operators and validated units. Natural language may map to a rule, but cannot produce executable code or SQL.

## Dependencies

- Features 015
- 030
- 054
- 064
- 084-086
- 089
- 092
- and 097.

## In Scope

- Price threshold and crossing rules.
- Volume and value versus historical baseline.
- Buyer power and real-money-flow rules.
- Fundamental and report-publication conditions.
- Natural-language rule definition mapped to governed rule types.
- One-shot and recurring rules with cooldown.

## Out of Scope

- Arbitrary executable user expressions.
- LLM-generated SQL.
- Broker order execution.
- Guaranteed signal claims.

## Acceptance Criteria

1. Price threshold and crossing rules.
2. Volume and value versus historical baseline.
3. Buyer power and real-money-flow rules.
4. Fundamental and report-publication conditions.
5. Natural-language rule definition mapped to governed rule types.
6. One-shot and recurring rules with cooldown.
7. All user-specific data is isolated by canonical actor and tenant context where applicable.
8. All responses and notifications expose source freshness and evidence when financial facts are shown.
9. Failure of this feature must not silently consume credits or create duplicate ledger entries.
10. The capability is protected by explicit plan/entitlement checks and auditable authorization.

## API / Integration Proposal

```text
POST /api/v1/trackers/me
GET /api/v1/trackers/me
PATCH /api/v1/trackers/me/{id}
DELETE /api/v1/trackers/me/{id}
```

## Data Model Proposal

```csharp
AlertRule { Id; ActorId; ExternalCompanyId; RuleType; Operator; Threshold; Unit; BaselineWindow?; Recurrence; Cooldown; IsEnabled; LastTriggeredAtUtc?; }
```

## Security, Billing, and Compliance Rules

- Reuse ASP.NET Core Identity actor context and existing authorization policies.
- Reuse Billing reservation/finalization and immutable UsageLedger semantics.
- Never place secrets, bot tokens, payment credentials, or provider credentials in source-controlled configuration.
- Treat Telegram usernames as display metadata, never as stable identity.
- Avoid financial-advice wording; state that outputs are informational and evidence-based.

## Implementation Notes

- The domain owns a typed `AlertRule` aggregate and `AlertRuleEvaluationState`; user text is retained only as audit metadata and is parsed into governed fields before persistence.
- REST delivery uses actor-scoped `/api/v1/trackers/me` endpoints with dedicated read/write permissions, authenticated-actor rate limiting, plan capability `Tracker.Rules`, canonical company/alias resolution, optimistic versions, and expiring confirmation tokens.
- Telegram reuses the same application use cases. `/track`, `/track_edit`, and paginated `/trackers` commands expose compact versioned confirm/edit/cancel/pause/resume/remove callbacks with update replay protection.
- Evaluation reads canonical persisted quotes, trades, Feature 092 snapshots, derived financial metrics, and Feature 084 Codal events. The worker polls every 60 seconds in bounded batches of 100; freshness limits are 20 minutes for quotes, 24 hours for trade/Feature snapshots, 45 days for financial metrics, and 7 days for Codal events.
- Per-rule transactions, evaluation-state concurrency tokens, immutable trigger evidence, and unique deduplication keys suppress concurrent/replayed crossings. Notification delivery is delegated to Feature 097 through `NotificationIntent`; the trigger retains the notification-intent link for Feature 099 history.
- Observability covers active rules by type, evaluation lag, skip reason (including stale/missing/entitlement), crossings, resets, cooldown suppression, duplicates, failures, and notification handoff.

## Validation Evidence

- Release solution build: succeeded with zero warnings.
- Tracker/Telegram unit tests: 30 passed.
- Tracker integration tests: 6 passed, including actor isolation, plan limits, alias resolution, concurrent evaluation, replay suppression, and notification handoff.
- Architecture tests: 7 passed.
- Financial-ingestion and billing EF models: no pending model changes.
