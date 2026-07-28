# User Story — Professional Scanners and Ready Filters

## Status
`[x]` Implemented

## Feature
Provide governed ready-made filters and professional-market menus with explainable, reproducible results.

## Story

As a TahlilApp-AI user,

I want to run ready-made professional filters with transparent definitions,

so that I can discover unusual market behavior without building complex filters manually.

## Business Context

Every filter is a governed, versioned definition over canonical metrics/events and returns exact match reasons.

## Dependencies

- Features 007
- 008
- 009
- 015
- 072
- 074
- and 092.

## In Scope

- Ready filters for buyer power, queue events, volume anomalies, large trades, technical thresholds, and combined conditions.
- Today and historical professional-activity views.
- Industry chart/table filters.
- Result ranking and exact reason per matched symbol.
- Unlimited versus metered access controlled by plan entitlement.

## Out of Scope

- Opaque proprietary signals without formula disclosure.
- Automatic buy/sell recommendations.
- Ad-hoc SQL from user text.

## Acceptance Criteria

1. Ready filters for buyer power, queue events, volume anomalies, large trades, technical thresholds, and combined conditions.
2. Today and historical professional-activity views.
3. Industry chart/table filters.
4. Result ranking and exact reason per matched symbol.
5. Unlimited versus metered access controlled by plan entitlement.
6. All user-specific data is isolated by canonical actor and tenant context where applicable.
7. All responses and notifications expose source freshness and evidence when financial facts are shown.
8. Failure of this feature must not silently consume credits or create duplicate ledger entries.
9. The capability is protected by explicit plan/entitlement checks and auditable authorization.

## API / Integration Proposal

```text
GET /api/v1/scanners/catalog
POST /api/ai/v1/query (natural-language scanner)
Internal scanner execution contracts
```

## Data Model Proposal

```csharp
ScannerDefinition { Code; Version; TitleFa; DescriptionFa; ConditionsJson; RequiredDatasetsJson; EntitlementCode; IsActive; }
```

## Security, Billing, and Compliance Rules

- Reuse ASP.NET Core Identity actor context and existing authorization policies.
- Reuse Billing reservation/finalization and immutable UsageLedger semantics.
- Never place secrets, bot tokens, payment credentials, or provider credentials in source-controlled configuration.
- Treat Telegram usernames as display metadata, never as stable identity.
- Avoid financial-advice wording; state that outputs are informational and evidence-based.
