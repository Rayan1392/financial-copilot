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

These entries are the initial supported semantic metric definitions, not a hardcoded closed catalog. EPS, P/E, margins, growth measures, and cash-flow measures are examples of an extensible domain that can grow substantially. The scanner resolves Persian or English user terminology into canonical metric codes through the versioned Financial Semantic Layer and retains metric/calculation-policy versions for explanation and audit. Future metric additions should be registered through extensible calculation strategies without modifying core orchestration logic.

### Period Coverage

- Latest month
- Latest quarter
- 3-month, 6-month, 9-month, 12-month financial periods
- YoY comparison
- MoM comparison for monthly reports
- TTM for ratios where available

### Result Features

- Matching stock lists represented as structured result tables.
- Mandatory identity columns for every scanner table: `نماد` (symbol) first, `شرکت` (company name) second. These are always present and cannot be removed or reordered.
- Metric columns include only the metrics explicitly requested, filtered, sorted, or named by the user. No automatic quote enrichment (`LATEST_PRICE`, `DAILY_CHANGE_PCT`, `MARKET_CAP`) is added unless the user asked for it or it is part of a filter/sort condition.
  - Example: `لیست نمادهای با پی به ای زیر 4 و پی به اس زیر 1` → columns are `نماد`, `شرکت`, `PE_TTM`, `PS_TTM` only.
  - Example: same query with `همراه با آخرین قیمت` → columns are `نماد`, `شرکت`, `PE_TTM`, `PS_TTM`, `LATEST_PRICE`.
- Internal/debug columns (e.g., `symbols`) must never appear in user-facing scanner output.
- User-requested table-column overrides, validated to a maximum of 10 displayed data columns.
- Valuation ratio zero-value exclusion: rows with `PE_TTM = 0`, `PS_TTM = 0`, or `PB = 0` must not satisfy `<` or `<=` filter conditions for those metrics. Zero values for valuation ratios are treated as missing/invalid.
- Live/low-latency price values when available, otherwise latest completed trading-day price statistics with visible source/freshness metadata. (Applies only when price columns are included per the rules above.)
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
- Advanced derived-feature scoring, ML feature-store infrastructure, AI evaluation dashboards, and personalized long-term memory.

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
      "metric": "NET_PROFIT_GROWTH_YOY",
      "metricVersion": "v1",
      "operator": ">",
      "value": 50,
      "unit": "percent",
      "period": "LatestQuarter",
      "comparison": "YoY"
    },
    {
      "metric": "PE_TTM",
      "metricVersion": "v1",
      "operator": "<",
      "value": 5,
      "period": "TTM"
    }
  ],
  "sort": [
    {
      "metric": "NET_PROFIT_GROWTH_YOY",
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
- terminology cannot be resolved to an allowed semantic metric definition/version.

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

For stock-list answers, table schema and row values are assembled by deterministic Application-layer services. Use a result-column policy (`IScannerResultColumnPolicy`) that always emits `نماد` and `شرکت` as the first two columns, then emits only the metrics the user explicitly requested or used as filter/sort conditions. Quote columns are included only when the user explicitly asked for them or when they are part of a filter/sort condition. A market quote resolver retrieves prices in batches with live-to-previous-trading-day fallback when quote columns are included. The AI may describe the table but must not choose unvalidated columns, add unrequested columns, or generate numerical data.

## Internal Application Services

The MVP scanner is implemented behind the facade through services such as:

- `IAiQueryOrchestrator`
- `IIntentDetectionService`
- `IScannerQueryParser`
- `IScannerExecutionService`
- `IScannerResultRanker`
- `IExplainableAnswerBuilder`

No scanner-specific parse or execute endpoint is part of the React UI contract.

## Future Platform Extensions

The Scanner MVP sits on architecture that can later add versioned derived features, AI evaluation/regression datasets, OpenTelemetry-compatible AI workflow observability, and consent-aware memory. Those capabilities remain internal extensions behind the same AI facade and do not expand the Phase 1 public scanner API.

## Non-Functional Requirements

- P95 response under 3 seconds for cached/normalized scanner queries.
- Async execution for heavy queries.
- All calculations covered by unit tests.
- All AI-generated plans validated before execution.
- Conversation Messages, routed tool activity, and query evidence stored for product analytics and audit.
- Rate limiting per user and API client.
