# User Story — AI Market Report and Personal Digest

## Status
`[ ]` Proposed

## Feature
Generate evidence-bound market narratives and a personalized end-of-day digest for each user’s followed symbols.

## Story

As a TahlilApp-AI user,

I want to receive an AI-written market report and a digest about my followed symbols,

so that I can understand what mattered today and why without reading many separate screens.

## Business Context

The LLM is a renderer over persisted pulse and insight evidence. Causal language must be qualified unless causality is directly supported.

## Dependencies

- Features 084-086
- 090
- 092
- 095
- and 097.

## In Scope

- Intraday and end-of-day market report from Feature 095 snapshots.
- Personal digest from followed symbols and insight events.
- Drivers, anomalies, Codal events, and next-day watch items.
- All numeric statements bound to persisted evidence.
- Public report and subscriber-only personalized report.

## Out of Scope

- Price targets.
- Trade instructions.
- Fabricated causal explanations.
- Portfolio P/L without holdings.

## Acceptance Criteria

1. Intraday and end-of-day market report from Feature 095 snapshots.
2. Personal digest from followed symbols and insight events.
3. Drivers, anomalies, Codal events, and next-day watch items.
4. All numeric statements bound to persisted evidence.
5. Public report and subscriber-only personalized report.
6. All user-specific data is isolated by canonical actor and tenant context where applicable.
7. All responses and notifications expose source freshness and evidence when financial facts are shown.
8. Failure of this feature must not silently consume credits or create duplicate ledger entries.
9. The capability is protected by explicit plan/entitlement checks and auditable authorization.

## API / Integration Proposal

```text
GET /api/v1/market-reports/latest
GET /api/v1/digests/me/latest
POST /api/v1/digests/me/generate
```

## Data Model Proposal

```csharp
MarketReport { Id; Scope; ActorId?; TradingDate; SnapshotIdsJson; InsightEventIdsJson; Narrative; GeneratedAtUtc; ModelMetadataJson; EvidenceHash; }
```

## Security, Billing, and Compliance Rules

- Reuse ASP.NET Core Identity actor context and existing authorization policies.
- Reuse Billing reservation/finalization and immutable UsageLedger semantics.
- Never place secrets, bot tokens, payment credentials, or provider credentials in source-controlled configuration.
- Treat Telegram usernames as display metadata, never as stable identity.
- Avoid financial-advice wording; state that outputs are informational and evidence-based.
