# Phase 1 Scanner MVP Scope

## MVP Goal

Build a Scanner Tool behind the AI facade. The React chat UI submits every user Message through `POST /api/ai/v1/query`; the AI Query Orchestrator detects scanner intent, invokes the Scanner Use Case, and returns an Explainable Answer with Data Citations and a Confidence Score.

## Public Entry Point

```http
POST /api/ai/v1/query
```

The frontend does not call scanner parse or execute APIs. Scanner-specific behavior is internal to the Application layer.

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
- Explainable Answer content usable in the generic Conversation response.
- Usage Accounting associated with the facade request.

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

After Tool Routing selects the Scanner Tool, `IScannerQueryParser` should produce a structured plan such as:

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

When the Scanner Tool answers a Message, each result item in the Explainable Answer must explain:

- why it matched,
- actual values vs thresholds,
- period used,
- comparison basis,
- provider/source,
- last update,
- Confidence Score.

The answer is persisted as an assistant Message in the Conversation, along with traceable Data Citations and usage outcome.

## Internal Application Services

The MVP scanner is implemented behind the facade through services such as:

- `IAiQueryOrchestrator`
- `IIntentDetectionService`
- `IScannerQueryParser`
- `IScannerExecutionService`
- `IScannerResultRanker`
- `IExplainableAnswerBuilder`

No scanner-specific parse or execute endpoint is part of the React UI contract.

## Non-Functional Requirements

- P95 response under 3 seconds for cached/normalized scanner queries.
- Async execution for heavy queries.
- All calculations covered by unit tests.
- All AI-generated plans validated before execution.
- Conversation Messages, routed tool activity, and query evidence stored for product analytics and audit.
- Rate limiting per user and API client.
