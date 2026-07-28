# User Story — Market Pulse and Key Statistics

## Status
`[x]` Implemented

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

## Implemented Canonical Definitions

- Scope is `all` active instruments from Feature 054's configured primary market source, or an explicitly configured `MarketCode` from `MarketViews:PulseSegments`; unknown segments are rejected.
- Transaction value is the sum of canonical TSETMC `QTotCap` (`TotalCapital`) at one capture cutoff. Intraday snapshots use the latest fresh observation per instrument; final snapshots use the completed daily row.
- Small-trade value, equity/fixed-income real-money flow, and queue count/value remain explicit `Unavailable` facts because normalized storage does not contain per-trade classification, client-type, or order-book evidence. They are never returned as zero.
- Breadth uses fresh canonical quote change percentages: positive is advancing, negative is declining, and zero is unchanged. Missing/stale instruments are reported as excluded.
- Industry scores are equal-weight averages of fresh constituent change percentages. Ties are stable by industry code.
- Weekly and monthly baselines use the preceding 5 and 20 completed trading sessions, with minimum samples of 3 and 10 respectively.
- Iran session states are `PreOpen` before 09:00, `Open` from 09:00 through 12:30, `Intermission` until the established end-of-day ingest cutoff, `Closed` afterward, and `Holiday` on Thursday/Friday. Unknown data is never inferred as a session fact.
- Snapshots are definition-versioned and revision-linked. A changed input hash creates a new immutable revision and designates it current; identical input in the same cadence slot is idempotent.
- Generation is scheduled at a configurable cadence, serialized per slot with a PostgreSQL transaction advisory lock, retried with bounded backoff, and never reserves or consumes credits.
