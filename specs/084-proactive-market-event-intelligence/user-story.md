# User Story — Proactive Market Event Intelligence

## Status
`[ ]` Proposed

## Feature
Proactive market-wide insight event detection and ranked feed for TahlilApp-AI.

## Story

As a TahlilApp-AI user,

I want the system to proactively surface the most important market and company events,

so that I can understand what changed today without manually asking separate questions for each symbol.

## Business Context

The current assistant is primarily reactive: users ask about metrics, symbols, financial statements, monthly reports, or scanner filters. This feature adds a proactive product layer that detects important events from existing persisted data and exposes them as evidence-backed insights.

Because the current product does not have a practical watchlist, this feature must be market-wide first. It must not depend on watchlist or portfolio data.

## In Scope

- Persisted `InsightEvent` read model.
- Deterministic insight detectors over existing data projections.
- Insight scoring, severity, confidence, evidence, freshness, and deduplication.
- Market-wide feed endpoint.
- Symbol-level feed endpoint.
- Industry-level feed endpoint if industry metadata is available.
- No buy/sell recommendation wording.

## Out of Scope

- Full portfolio intelligence.
- Brokerage integration.
- Push notifications.
- ML price prediction.
- Watchlist-only personalization.
- Investment advice or trading signals.

## Suggested Insight Types

- `MonthlyReportPublished`
- `MonthlySalesAnomaly`
- `MonthlyQualityRankingChange`
- `PriceMovement`
- `ComprehensiveAnalysisPublished`
- `FinancialStatementPublished`
- `DataFreshnessWarning`

## Acceptance Criteria

1. The system persists market insight events with source provider, source period, detected time, severity, importance score, confidence score, evidence, and deduplication key.
2. The feed can return the latest ranked market-wide insights without requiring a user watchlist.
3. The system generates no duplicate insight for the same company, period, source, and detector rule.
4. Every insight includes deterministic evidence and freshness metadata.
5. Every insight includes a concise reason explaining why the event is important.
6. The feed excludes expired insights by default.
7. The feed supports filtering by symbol.
8. The feed supports filtering by insight type and severity.
9. The AI text renderer must not invent values, alter evidence, or convert insights into buy/sell recommendations.
10. The feature integrates with existing explainability and confidence-score principles.

## Example Output

```text
Today, 8 important market events were detected:
1. KCHAD — Latest monthly sales were materially above the 12-month average.
2. FMLI — Large daily price move with high trading value.
3. SHGHDIR — New comprehensive analysis was published.
```

## API Proposal

```http
GET /api/v1/insights/market
GET /api/v1/insights/symbol/{symbol}
GET /api/v1/insights/industries/{industryCode}
```

## Data Model Proposal

```csharp
InsightEvent
{
    Guid Id;
    string ExternalCompanyId;
    string Symbol;
    string? IndustryCode;
    InsightType InsightType;
    InsightSeverity Severity;
    decimal ImportanceScore;
    decimal ConfidenceScore;
    string Title;
    string Summary;
    string Reason;
    string EvidenceJson;
    string SourceProviderName;
    string SourceEntityType;
    string? SourceEntityId;
    string? SourcePeriod;
    DateTime DetectedAtUtc;
    DateTime? ExpiresAtUtc;
    string DeduplicationKey;
}
```
