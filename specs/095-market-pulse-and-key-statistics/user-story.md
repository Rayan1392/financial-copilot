# User Story — Market Pulse and Key Statistics

## Status
`[ ]` Proposed

## Feature
Publish a canonical real-time market pulse containing turnover, money flow, queue values, breadth, and leading industries.

## Story

As a TahlilApp-AI user,

I want to view a concise and current pulse of the entire market,

so that I can understand liquidity, breadth, queues, and capital flow at a glance.

## Business Context

Definitions must be explicit about included instruments, units, cutoff time, and provider freshness.

## Dependencies

- Features 030
- 054
- 064
- and 092.

## In Scope

- Market-wide key statistics snapshot.
- Small-trade value and configurable market-segment scope.
- Real-money flow for equities and fixed-income funds.
- Buy/sell queue count and value.
- Market breadth and leading/lagging industries.
- Weekly/monthly baseline comparisons.

## Out of Scope

- AI narrative generation.
- Personalized followed-symbol digest.
- Unsupported statistics without canonical data definitions.

## Acceptance Criteria

1. Market-wide key statistics snapshot.
2. Small-trade value and configurable market-segment scope.
3. Real-money flow for equities and fixed-income funds.
4. Buy/sell queue count and value.
5. Market breadth and leading/lagging industries.
6. Weekly/monthly baseline comparisons.
7. All user-specific data is isolated by canonical actor and tenant context where applicable.
8. All responses and notifications expose source freshness and evidence when financial facts are shown.
9. Failure of this feature must not silently consume credits or create duplicate ledger entries.
10. The capability is protected by explicit plan/entitlement checks and auditable authorization.

## API / Integration Proposal

```text
GET /api/v1/market-pulse/latest
GET /api/v1/market-pulse/history?from=&to=
```

## Data Model Proposal

```csharp
MarketPulseSnapshot { Id; TradingDate; CapturedAtUtc; RetailTradeValue; EquityRealMoneyFlow; FixedIncomeFundRealMoneyFlow; BuyQueueCount; BuyQueueValue; SellQueueCount; SellQueueValue; BreadthJson; IndustryDriversJson; EvidenceJson; }
```

## Security, Billing, and Compliance Rules

- Reuse ASP.NET Core Identity actor context and existing authorization policies.
- Reuse Billing reservation/finalization and immutable UsageLedger semantics.
- Never place secrets, bot tokens, payment credentials, or provider credentials in source-controlled configuration.
- Treat Telegram usernames as display metadata, never as stable identity.
- Avoid financial-advice wording; state that outputs are informational and evidence-based.
