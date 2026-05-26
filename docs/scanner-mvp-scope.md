# Phase 1 Scanner MVP Scope

## MVP Goal

Build a backend scanner that receives natural language financial screening questions, converts them into a validated scanner plan, executes the plan on normalized financial and market data, and returns ranked explainable results.

## In Scope

### Natural Language Scanner

Supported query examples:

- latest quarter net profit growth greater than 50% and P/E below 5
- latest month sales growth greater than 100%
- P/S below 1
- companies with sales growth and improving gross margin
- symbols with net profit growth and low valuation

### Metric Coverage

Phase 1 metrics:

- Net profit
- Net profit growth
- Revenue/sales
- Sales growth
- Production volume growth
- Gross profit
- Operating profit
- EPS
- P/E
- P/S
- Gross margin
- Operating margin
- Net margin

### Period Coverage

- Latest month
- Latest quarter
- 3-month, 6-month, 9-month, 12-month financial periods
- YoY comparison
- MoM comparison for monthly reports
- TTM for ratios where available

### Result Features

- List of matching symbols.
- Metric values.
- Ranking score.
- Explanation per symbol.
- Data source and report date.
- Warnings for missing/stale data.
- Export-ready API response.

## Out of Scope for Phase 1

- Autonomous research agent.
- Portfolio analysis.
- Watchlist AI.
- Real-time trading signal generation.
- Direct buy/sell recommendation.
- Deep research reports.
- Complex technical analysis.
- Backtesting.
- Full Elasticsearch deployment unless PostgreSQL search is insufficient.

## Scanner Query Plan

The LLM should output a structured plan such as:

```json
{
  "intent": "scanner",
  "universe": {
    "market": "TSE",
    "industries": [],
    "symbols": []
  },
  "conditions": [
    {
      "metric": "NetProfitGrowth",
      "operator": ">",
      "value": 50,
      "unit": "percent",
      "period": "LatestQuarter",
      "comparison": "YoY"
    },
    {
      "metric": "PE",
      "operator": "<",
      "value": 5,
      "period": "TTM"
    }
  ],
  "sort": [
    {
      "metric": "NetProfitGrowth",
      "direction": "desc"
    }
  ],
  "limit": 50
}
```

## Validation Rules

Reject or clarify when:

- metric is unsupported,
- period is unsupported,
- operator is invalid,
- query would require missing data,
- ambiguity changes financial meaning,
- LLM output contains executable SQL,
- limit exceeds allowed plan/user quota.

## Ranking Policy

Initial ranking score:

```text
score =
  conditionMatchScore
  + dataFreshnessScore
  + profitabilityQualityScore
  + valuationAttractivenessScore
  - missingDataPenalty
```

Keep scoring deterministic and documented.

## Explainability Contract

Each result item must explain:

- why it matched,
- actual values vs thresholds,
- period used,
- comparison basis,
- provider/source,
- last update,
- confidence.

## Non-Functional Requirements

- P95 response under 3 seconds for cached/normalized scanner queries.
- Async execution for heavy queries.
- All calculations covered by unit tests.
- All AI-generated plans validated before execution.
- Query logs stored for product analytics.
- Rate limiting per user and API client.
