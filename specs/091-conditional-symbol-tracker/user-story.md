# User Story — Conditional Symbol Tracker

## Status
`[ ]` Proposed

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
