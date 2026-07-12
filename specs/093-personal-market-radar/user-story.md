# User Story — Personal Market Radar

## Status
`[ ]` Proposed

## Feature
Continuously monitor followed symbols and deliver only material market events ranked by importance.

## Story

As a TahlilApp-AI user,

I want to enable a radar for the symbols I follow,

so that I receive only material events relevant to my selected companies.

## Business Context

The radar is personalization over canonical followed symbols and insight events; it is not a holdings or portfolio model.

## Dependencies

- Features 085
- 086
- 092
- and 097.

## In Scope

- Reuse followed symbols from Feature 085.
- Reuse insight events from Features 084 and 092.
- Per-user radar preferences and event categories.
- Sub-minute-capable evaluation when source freshness supports it.
- Importance scoring and severity thresholds.
- Notification handoff without detector duplication.

## Out of Scope

- Holdings, quantity, average cost, or P/L.
- Duplicate detector logic.
- Brokerage integration.

## Acceptance Criteria

1. Reuse followed symbols from Feature 085.
2. Reuse insight events from Features 084 and 092.
3. Per-user radar preferences and event categories.
4. Sub-minute-capable evaluation when source freshness supports it.
5. Importance scoring and severity thresholds.
6. Notification handoff without detector duplication.
7. All user-specific data is isolated by canonical actor and tenant context where applicable.
8. All responses and notifications expose source freshness and evidence when financial facts are shown.
9. Failure of this feature must not silently consume credits or create duplicate ledger entries.
10. The capability is protected by explicit plan/entitlement checks and auditable authorization.

## API / Integration Proposal

```text
GET /api/v1/radar/me
PUT /api/v1/radar/me/preferences
POST /api/v1/radar/me/test-notification
```

## Data Model Proposal

```csharp
RadarPreference { ActorId; EventTypesJson; MinimumSeverity; MinimumImportance; DigestMode; IsEnabled; }
```

## Security, Billing, and Compliance Rules

- Reuse ASP.NET Core Identity actor context and existing authorization policies.
- Reuse Billing reservation/finalization and immutable UsageLedger semantics.
- Never place secrets, bot tokens, payment credentials, or provider credentials in source-controlled configuration.
- Treat Telegram usernames as display metadata, never as stable identity.
- Avoid financial-advice wording; state that outputs are informational and evidence-based.
