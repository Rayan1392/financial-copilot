# Technical Proposal — Three Improvements for the AI Capital Market Assistant

## Context

The current TahlilApp-AI assistant is primarily reactive: the user asks about a specific symbol, financial metric, monthly report, statement period, full financial-statement table, or scanner filter, and the system routes the request to deterministic services.

The next product upgrade should make the assistant proactive. The assistant should detect meaningful market/company events before the user asks, explain why each event matters, and provide evidence, freshness, and confidence.

Because the current product does not have a practical watchlist, the first step should not depend on watchlist or portfolio data. The correct sequence is:

1. **084 — Proactive Market Event Intelligence**
2. **085 — Followed Symbols Foundation**
3. **086 — Personalized Symbol Intelligence Feed**

---

## Recommendation 1 — Proactive Market Event Intelligence

### Product goal

Build a market-wide insight feed that detects important events across all covered companies and ranks them by importance.

The user should be able to open the app and see:

```text
Today, 8 important market events were detected:
1. KCHAD — Latest monthly sales were materially above the 12-month average.
2. FMLI — Large daily price move with high trading value.
3. SHGHDIR — New comprehensive analysis was published.
4. SHPDIS — Latest monthly report quality deteriorated versus the prior trend.
```

### Why this matters

This turns the product from a passive Q&A assistant into an active market monitoring assistant. It increases daily engagement and reuses existing data sources without requiring portfolio integration.

### Core architecture

Introduce a reusable `Market Insight Event Engine`.

```text
Ingestion / Existing Projections
        ↓
Insight Detectors
        ↓
Insight Scoring + Deduplication
        ↓
Persisted InsightEvents
        ↓
Market Feed API / Symbol Feed API / AI Explanation Bridge
```

### Suggested detectors for v1

| Detector | Source | Signal |
|---|---|---|
| MonthlyReportPublishedDetector | Noavaran monthly activity | New monthly production/sales report is available |
| MonthlySalesAnomalyDetector | Monthly sales + 12-month average | Latest sales materially above/below baseline |
| PriceMoveDetector | LatestMarketQuotes / daily trades | Large daily change or unusual trading value |
| ComprehensiveAnalysisPublishedDetector | CyclicalWaves | New comprehensive/technical/fundamental analysis |
| FinancialStatementPublishedDetector | Noavaran financial statements | New 3/6/9/12-month statement available |
| MonthlyQualityRankingDetector | Feature 080 | Report quality improved/deteriorated |
| DataFreshnessDetector | Existing sync metadata | Market/fundamental data is stale or missing |

### Suggested data model

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

### Suggested endpoints

```http
GET /api/v1/insights/market
GET /api/v1/insights/symbol/{symbol}
GET /api/v1/insights/industries/{industryCode}
```

### Important product rule

The feature must not provide buy/sell recommendations. The language should be event-based:

- "Important event detected"
- "Sales changed materially"
- "New statement was published"
- "Data is stale"

Not:

- "Buy this stock"
- "Sell this stock"
- "This is a trading signal"

---

## Recommendation 2 — Followed Symbols Foundation

### Product goal

Add a lightweight personalization primitive before building watchlist AI or portfolio intelligence.

The user can follow/unfollow symbols. This is intentionally simpler than a full portfolio and does not require holdings, cost basis, position weight, P/L, or brokerage integration.

### Why this matters

The product currently does not have a real watchlist. Proactive personalization needs a user-specific universe of symbols. `FollowedSymbols` creates that foundation with low risk and low complexity.

### Data model

```csharp
FollowedSymbol
{
    Guid Id;
    Guid ActorId;
    string ExternalCompanyId;
    string Symbol;
    DateTime FollowedAtUtc;
    string? Source;
}
```

### Suggested endpoints

```http
GET    /api/v1/followed-symbols/me
POST   /api/v1/followed-symbols/me/{externalCompanyId}
DELETE /api/v1/followed-symbols/me/{externalCompanyId}
PUT    /api/v1/followed-symbols/me
```

### UX entry points

- Symbol page: "Follow symbol"
- AI answer card: "Follow this symbol"
- Market insight card: "Follow symbol"
- Followed symbols management page

### Acceptance principles

- One actor cannot follow the same company twice.
- Followed symbols are not portfolio holdings.
- The system must not infer financial exposure from followed symbols.
- Followed symbols can later power personalized insights, notifications, and home dashboard cards.

---

## Recommendation 3 — Evidence-first Insight and Answer Format

### Product goal

Every AI answer and every proactive insight should show deterministic evidence, source freshness, confidence, and next action.

The assistant should answer:

1. What happened?
2. Why does it matter?
3. What is the source?
4. How fresh is the data?
5. How confident is the system?
6. What should the user inspect next?

### Suggested output contract

```json
{
  "title": "KCHAD monthly sales were materially above baseline",
  "summary": "Latest monthly sales were 38% above the 12-month average.",
  "reason": "The latest monthly sales amount exceeded the configured anomaly threshold.",
  "severity": "Important",
  "importanceScore": 82,
  "confidenceScore": 91,
  "freshness": {
    "sourcePeriod": "1405/03",
    "lastSyncedAtUtc": "..."
  },
  "evidence": [
    {
      "label": "Latest monthly sales",
      "value": "...",
      "sourceProvider": "NoavaranCurrentApi"
    },
    {
      "label": "12-month average",
      "value": "...",
      "sourceProvider": "CyclicalWaves / Derived Snapshot"
    }
  ],
  "actions": [
    "OpenSymbol",
    "AskAiAboutThis",
    "OpenSourceReport"
  ]
}
```

### AI explanation bridge

When the user clicks "Ask AI about this insight", the system should pass the persisted `InsightEvent` as structured context. The LLM may explain and summarize, but it must not change numeric values, source dates, confidence score, or evidence.

### Recommended endpoint

```http
POST /api/ai/v1/query
```

With an optional structured context field:

```json
{
  "message": "Explain this insight",
  "context": {
    "insightEventId": "..."
  }
}
```

---

## Delivery sequence

| Feature | Name | Scope | Priority |
|---|---|---:|---:|
| 084 | Proactive Market Event Intelligence | Medium | 1 |
| 085 | Followed Symbols Foundation | Small/Medium | 2 |
| 086 | Personalized Symbol Intelligence Feed | Medium | 3 |

## Explicitly deferred

- Full portfolio intelligence
- Buy/sell recommendations
- Push notifications
- ML-based prediction
- Autonomous opportunity discovery
- Brokerage integration

These are valuable later, but they should not block the next product value layer.
