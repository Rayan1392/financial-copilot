# API Design

## Public AI Facade Decision

The React chat UI must call one backend facade endpoint for every user message:

```http
POST /api/ai/v1/query
```

The frontend must not infer intent or choose a scanner, portfolio, market summary, single stock, or deep search service. The backend performs Intent Detection, Tool Routing, execution, answer generation, Usage Accounting, and Conversation persistence behind this endpoint.

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
    "results": [
      {
        "symbol": "ABC",
        "companyName": "Example Company",
        "industry": "Chemicals",
        "score": 87.5,
        "matchedConditions": [
          {
            "metric": "NetProfitGrowth",
            "actualValue": 72.4,
            "threshold": 50,
            "unit": "percent",
            "period": "LatestQuarter",
            "comparison": "YoY"
          },
          {
            "metric": "PE",
            "actualValue": 4.2,
            "threshold": 5,
            "period": "TTM"
          }
        ],
        "explanation": "Latest quarter net profit growth was 72.4% YoY and TTM P/E was 4.2.",
        "citations": [
          {
            "type": "FinancialStatement",
            "period": "LatestQuarter",
            "reportDate": "2026-05-01",
            "sourceProvider": "ThirdPartyProvider",
            "lastSyncAt": "2026-05-02T08:15:00Z"
          }
        ],
        "confidenceScore": 0.91,
        "warnings": []
      }
    ]
  },
  "usage": {
    "operation": "AiQuery.Scanner",
    "creditsCharged": 1,
    "quotaRemaining": 99
  }
}
```

The returned `detectedIntent` and `toolUsed` are informational output. They do not create a frontend routing responsibility.

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

Every invocation of `POST /api/ai/v1/query` creates Usage Accounting records after authentication and according to the validation/charging policy. The response includes charged credits and remaining quota where permitted.

```http
GET  /api/v1/usage/me
GET  /api/v1/usage/api-client/{clientId}
POST /api/v1/credits/top-up
GET  /api/v1/subscriptions/plans
POST /api/v1/subscriptions/subscribe
```

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
