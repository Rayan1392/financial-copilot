# API Design

## API Versioning

Base path:

```text
/api/v1
```

## Authentication Models

### Owned Web App

Use JWT bearer authentication.

### SaaS/API Consumers

Use API keys initially. Design so OAuth2 client credentials can be added later.

Headers:

```text
Authorization: Bearer <jwt>
X-Api-Key: <api-key>
X-Correlation-Id: <uuid>
```

## Scanner Endpoints

### Create Scanner Query

```http
POST /api/v1/scanner/query
```

Request:

```json
{
  "question": "List symbols with latest quarter net profit growth above 50% and P/E below 5",
  "language": "fa-IR",
  "limit": 50,
  "executionMode": "sync"
}
```

Response:

```json
{
  "queryId": "uuid",
  "status": "completed",
  "needsClarification": false,
  "interpretedPlan": {},
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
      "explanation": "Matched because latest quarter net profit growth was 72.4% YoY and TTM P/E was 4.2.",
      "sources": [
        {
          "type": "FinancialStatement",
          "period": "LatestQuarter",
          "reportDate": "2026-05-01",
          "provider": "ThirdPartyProvider"
        }
      ],
      "confidence": 0.91,
      "warnings": []
    }
  ],
  "usage": {
    "creditsCharged": 1,
    "quotaRemaining": 99
  }
}
```

### Parse Scanner Query Only

Useful for UI preview and debugging.

```http
POST /api/v1/scanner/parse
```

### Execute Structured Scanner Plan

Useful for SaaS clients that build their own filter UI.

```http
POST /api/v1/scanner/execute
```

### Get Query History

```http
GET /api/v1/scanner/history
```

### Get Supported Metrics

```http
GET /api/v1/scanner/metadata/metrics
```

### Get Supported Periods

```http
GET /api/v1/scanner/metadata/periods
```

## Admin/Data Endpoints

```http
POST /api/v1/admin/data-sync/symbols
POST /api/v1/admin/data-sync/financial-statements
POST /api/v1/admin/data-sync/monthly-reports
GET  /api/v1/admin/data-sync/runs
GET  /api/v1/admin/provider-health
```

## Billing/Usage Endpoints

```http
GET  /api/v1/usage/me
GET  /api/v1/usage/api-client/{clientId}
POST /api/v1/credits/top-up
GET  /api/v1/subscriptions/plans
POST /api/v1/subscriptions/subscribe
```

## Error Response

Use a consistent problem-details format:

```json
{
  "type": "https://financialcopilot/errors/validation-error",
  "title": "Validation error",
  "status": 400,
  "detail": "Unsupported metric: EV/EBITDA",
  "traceId": "00-...",
  "errors": {
    "metric": ["EV/EBITDA is not supported in Phase 1."]
  }
}
```
