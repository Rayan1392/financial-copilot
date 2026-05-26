# API Design

## Public AI Facade Decision

The React chat UI must call one backend facade endpoint for every user message:

```http
POST /api/ai/v1/query
```

The frontend must not infer intent or choose a scanner, portfolio, market summary, single stock, or deep search service. The backend performs Intent Detection, Tool Routing, execution, answer generation, Usage Accounting, and Conversation persistence behind this endpoint.

The public API contract is also independent of the selected LLM runtime. The backend may execute an AI workflow through configured hosted model providers or local models, but it must not expose vendor-specific request/response formats to the React UI.

## Authentication Models

### Owned Web Application

Use JWT bearer authentication for the React application.

### SaaS/API Consumers

Use API keys initially. Design authentication so OAuth2 client credentials can be added later. An external client using the AI conversation experience uses the same AI facade contract.

Headers:

```text
Authorization: Bearer <jwt>
X-Api-Key: <api-key>
X-Correlation-Id: <uuid>
```

## AI Query API

### Submit User Message

```http
POST /api/ai/v1/query
```

Request:

```json
{
  "conversationId": "uuid-or-null",
  "message": "List symbols with latest quarter net profit growth above 50% and P/E below 5",
  "language": "fa-IR",
  "responseMode": "sync"
}
```

`conversationId` is omitted or null when starting a Conversation. The API creates a Conversation, persists the user Message, routes the request to the appropriate backend tool/use case, persists the assistant Message, and returns the answer.

Example response when the backend selects the Scanner Tool:

```json
{
  "conversationId": "uuid",
  "messageId": "uuid",
  "status": "completed",
  "detectedIntent": "scanner",
  "toolUsed": "Scanner",
  "needsClarification": false,
  "answer": {
    "text": "Two symbols matched your screening conditions.",
    "table": {
      "columns": [
        { "key": "symbol", "label": "Symbol" },
        { "key": "latestPrice", "label": "Latest Price" },
        { "key": "priceChangePercent", "label": "Price Change Percentage" },
        { "key": "marketCapitalization", "label": "Market Capitalization" },
        { "key": "netProfitGrowth", "label": "Net Profit Growth" },
        { "key": "pe", "label": "P/E" }
      ],
      "omittedColumns": [],
      "rows": [
        {
          "symbol": "ABC",
          "companyName": "Example Company",
          "industry": "Chemicals",
          "latestPrice": 23450,
          "priceChangePercent": 2.4,
          "priceSource": "LiveQuote",
          "priceAsOf": "2026-05-26T09:30:00Z",
          "marketCapitalization": 42000000000000,
          "netProfitGrowth": 72.4,
          "pe": 4.2,
          "score": 87.5,
          "matchedConditions": [
            {
              "metric": "NET_PROFIT_GROWTH_YOY",
              "metricVersion": "v1",
              "calculationPolicyVersion": "yoy-quarterly-v1",
              "actualValue": 72.4,
              "threshold": 50,
              "unit": "percent",
              "period": "LatestQuarter",
              "comparison": "YoY"
            },
            {
              "metric": "PE_TTM",
              "metricVersion": "v1",
              "calculationPolicyVersion": "ttm-valuation-v1",
              "actualValue": 4.2,
              "threshold": 5,
              "period": "TTM"
            }
          ],
          "citations": [
            {
              "type": "FinancialStatement",
              "period": "LatestQuarter",
              "reportDate": "2026-05-01",
              "sourceProvider": "ThirdPartyProvider",
              "lastSyncAt": "2026-05-02T08:15:00Z"
            }
          ]
        }
      ]
    },
    "explanation": "Latest quarter net profit growth was 72.4% YoY and TTM P/E was 4.2.",
    "confidenceScore": 0.91,
    "warnings": []
  },
  "usage": {
    "operation": "AiQuery.Scanner",
    "creditsCharged": 1.0,
    "remainingBalance": 99.0,
    "pricingPolicyVersion": "v1",
    "cached": false
  }
}
```

The returned `detectedIntent` and `toolUsed` are informational output. They do not create a frontend routing responsibility.

When an answer contains a list of stocks, the response uses a table schema. Default columns are symbol, latest price, price change percentage, market capitalization, and metrics relevant to the user's query; user-requested column changes are accepted after validation. The backend enforces a maximum of 10 displayed data columns. Price values prefer available live/low-latency quote data and fall back to the latest completed trading-day statistics with explicit `priceSource` and `priceAsOf` metadata.

## Conversation History API

Conversation history is generic AI chat history, not scanner-specific history.

```http
POST /api/ai/v1/query
GET  /api/ai/v1/conversations
GET  /api/ai/v1/conversations/{conversationId}
GET  /api/ai/v1/conversations/{conversationId}/messages
```

Each Conversation may contain Messages answered through different tools over time. For example, one conversation can include a market summary question followed by a scanner question.

## Supporting Reference APIs

These endpoints support UI controls, discovery, and integrations. They do not replace `POST /api/ai/v1/query` for a user chat message.

```http
GET /api/ai/v1/metadata/metrics
GET /api/ai/v1/metadata/periods
GET /api/ai/v1/metadata/symbols
GET /api/ai/v1/metadata/industries
```

The metrics metadata response is backed by the versioned Financial Semantic Layer. It may expose stable semantic metric identifiers, localized display aliases, unit/category, supported period/comparison options, and current public definition/policy version. It must not expose an ungoverned hardcoded frontend formula list.

`GET /api/ai/v1/metadata/metrics` is implemented as an authenticated reference endpoint backed by the registered semantic catalog. It exposes metric codes, definition versions, localized aliases, supported periods, units/categories, and registered public calculation-policy versions; it does not expose executable formula expressions.

## Internal Scanner Services

Scanner parsing and execution are Application-layer responsibilities invoked by `IAiQueryOrchestrator` after Tool Routing selects the Scanner Tool:

```csharp
public interface IAiQueryOrchestrator
public interface IIntentDetectionService
public interface IScannerQueryParser
public interface IScannerExecutionService
public interface IScannerResultRanker
public interface IExplainableAnswerBuilder
```

They are not frontend-facing public APIs. If operational diagnostics eventually require HTTP access, it must be admin-authorized, disabled or protected outside intended environments, and explicitly documented as internal-only:

```http
POST /api/internal/scanner/parse
POST /api/internal/scanner/execute
```

The React UI must never call internal scanner diagnostic endpoints.

## Admin/Data Endpoints

```http
POST /api/v1/admin/data-sync/symbols
POST /api/v1/admin/data-sync/financial-statements
POST /api/v1/admin/data-sync/monthly-reports
GET  /api/v1/admin/data-sync/runs
GET  /api/v1/admin/provider-health
```

## Billing/Usage Endpoints

Every invocation of `POST /api/ai/v1/query` resolves a billable `CustomerAccount`, validates entitlement, reserves spending capacity before expensive work, and commits or releases usage after execution according to a versioned operation-based pricing policy. The immutable Usage Ledger is the source of accounting truth; wallet balance is a read projection.

Billing persistence applies reservation creation and wallet-capacity hold atomically. It also applies successful reservation commit, usage-ledger charge, and wallet debit atomically; a zero-charge failure release records the reservation reason and restores reserved capacity without creating a charge entry. AI facade orchestration will call these Billing contracts in the metering integration story.

For SaaS organization accounts, API usage is charged to the organization and may be attributed to an optional partner-scoped `externalUserId`. Organization accounts may be prepaid, postpaid, or hybrid with an explicitly approved credit line. For direct consumer accounts, the product manages subscriptions/top-ups and rejects billable execution without allowance or balance by default.

```http
GET  /api/v1/usage/me
GET  /api/v1/usage/api-client/{clientId}
GET  /api/v1/billing/wallet
GET  /api/v1/billing/transactions
POST /api/v1/credits/top-up
GET  /api/v1/subscriptions/plans
POST /api/v1/subscriptions/subscribe
GET  /api/v1/admin/billing/customers/{customerAccountId}/usage
GET  /api/v1/admin/billing/customers/{customerAccountId}/invoices
POST /api/v1/admin/billing/customers/{customerAccountId}/adjustments
```

Billing administration endpoints require a billing-administrator role and remain tenant-scoped. The implemented manual adjustment operation records a positive usage-credit adjustment with an audit reason and idempotency key, and updates the wallet projection as part of the same persistence operation. It does not convert payment currency to credits or serve as a payment gateway.

Payment gateway callbacks and partner invoice settlement endpoints must be provider-specific authenticated/internal integration contracts when implemented, with idempotency enforced. Full payment gateway and automatic invoice delivery are not required for the initial scanner milestone.

## Error Response

Use a consistent problem-details format for the AI facade and supporting APIs:

```json
{
  "type": "https://financialcopilot/errors/validation-error",
  "title": "Validation error",
  "status": 400,
  "detail": "Unsupported metric: EV/EBITDA",
  "traceId": "00-...",
  "correlationId": "uuid",
  "errors": {
    "metric": ["EV/EBITDA is not supported in Phase 1."]
  }
}
```
