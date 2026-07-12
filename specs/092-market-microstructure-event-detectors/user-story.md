# User Story — Market Microstructure Event Detectors

## Status
`[ ]` Proposed

## Feature
Extend proactive insight detection with large trades, queue events, buyer-power anomalies, money flow, and volume anomalies.

## Story

As a TahlilApp-AI user,

I want the platform to detect important intraday market-structure events consistently,

so that radar, filters, and reports can reuse one trusted event stream.

## Business Context

This feature expands Feature 084 detectors and must not create Telegram-specific detection logic.

## Dependencies

- Features 030
- 054
- 064
- and 084.

## In Scope

- Large-trade detection.
- Buy/sell queue formation, release, and collection events.
- Real buyer/seller power.
- Real-money inflow/outflow.
- Volume and trading-value anomalies.
- Historical rarity, evidence, confidence, freshness, and deduplication.

## Out of Scope

- High-frequency exchange co-location.
- Price prediction.
- Unverifiable smart-money claims.
- Telegram delivery itself.

## Acceptance Criteria

1. Large-trade detection.
2. Buy/sell queue formation, release, and collection events.
3. Real buyer/seller power.
4. Real-money inflow/outflow.
5. Volume and trading-value anomalies.
6. Historical rarity, evidence, confidence, freshness, and deduplication.
7. All user-specific data is isolated by canonical actor and tenant context where applicable.
8. All responses and notifications expose source freshness and evidence when financial facts are shown.
9. Failure of this feature must not silently consume credits or create duplicate ledger entries.
10. The capability is protected by explicit plan/entitlement checks and auditable authorization.

## API / Integration Proposal

```text
Internal detector execution + GET /api/v1/insights/market filters
```

## Data Model Proposal

```csharp
InsightEvent extensions / detector-specific EvidenceJson schemas; no parallel event table unless justified.
```

## Security, Billing, and Compliance Rules

- Reuse ASP.NET Core Identity actor context and existing authorization policies.
- Reuse Billing reservation/finalization and immutable UsageLedger semantics.
- Never place secrets, bot tokens, payment credentials, or provider credentials in source-controlled configuration.
- Treat Telegram usernames as display metadata, never as stable identity.
- Avoid financial-advice wording; state that outputs are informational and evidence-based.
