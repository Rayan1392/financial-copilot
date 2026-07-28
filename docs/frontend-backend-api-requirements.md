# Frontend Backend API Requirements

## Purpose

This document derives the backend API requirements from the current React and TypeScript frontend in `src/frontend`. It defines the public endpoints needed to replace the current Supabase-backed server functions and mock financial responses.

The central constraint is unchanged: the React chat UI sends every user message to one AI facade endpoint. It does not detect intent or call scanner, portfolio, market summary, or research services directly.

## Current Implementation Audit

Audit completed on `2026-06-01`:

| Area | Current state | Delivery spec |
| --- | --- | --- |
| Frontend identity | Supabase signs users in today. The target is backend-owned ASP.NET Core Identity with JWT access tokens, rotating refresh tokens, tenant membership, and permission claims. | `031-frontend-authenticated-api-bridge` |
| AI query | `.NET` already exposes `POST /api/ai/v1/query`; the frontend still calls `generateMockReply`. | `032-frontend-chat-conversation-cutover` |
| Conversation history | `.NET` exposes list/detail/message reads, but lacks empty create, delete, titles, reloadable structured assistant content, and complete actor-scoped reads. | `032-frontend-chat-conversation-cutover` |
| Usage | `.NET` already exposes `GET /api/v1/usage/me`; the frontend still reads Supabase mock subscription credits. | `033-frontend-usage-watchlist-market-summary` |
| Watchlist | Prototype Supabase table and `STOCK_DB` quotes only; no .NET API exists. | `033-frontend-usage-watchlist-market-summary` |
| Market panel | Prototype imports `MARKET_SNAPSHOT`; StockMarketDB projections exist but no web summary endpoint exists. | `033-frontend-usage-watchlist-market-summary` |
| Metadata | `.NET` exposes metric metadata only; periods, symbols, and industries remain missing. | `034-frontend-assisted-query-metadata` |

Spec `031` replaces Supabase authentication rather than adding a bridge. The backend owns user
registration, login, refresh, logout, revoke, tenant membership, roles, permissions, and JWT
issuance. This avoids accepting arbitrary tenant identifiers from the browser and keeps the
FinancialCopilot API's existing tenant-scoped authorization contract intact.

## Frontend Evidence Reviewed

| Frontend area | Current dependency | Backend implication |
| --- | --- | --- |
| `src/lib/chat.functions.ts` | Supabase `threads`, `messages`, `user_subscriptions`, and `watchlists` operations | Conversation, usage, and watchlist APIs are required. |
| `src/routes/_app.chat.tsx` | Creates a thread, then sends the first message | Query may create a conversation atomically; empty conversation creation remains required for the sidebar. |
| `src/routes/_app.c.$threadId.tsx` | Loads and sends conversation messages | Conversation message retrieval and AI query execution are required. |
| `src/components/app/sidebar.tsx` | Lists, creates, deletes threads; displays credits and watchlist quotes | Conversation lifecycle, usage summary, and enriched watchlist endpoints are required. |
| `src/components/app/context-panel.tsx` | Displays a mocked market snapshot | A market summary endpoint is required to replace mocked context data. |
| `src/components/app/message-list.tsx` | Renders structured assistant results | The AI response DTO must carry structured explainable-answer content. |
| `src/lib/mock/data.ts` | Generates scanner, analysis, market, portfolio, and deep research answers | All mock intent handling must move behind the AI Query Orchestrator. |
| `src/routes/auth.tsx` and `src/integrations/supabase/auth-attacher.ts` | Supabase authentication and bearer-token attachment | Replace with backend Identity register/login, JWT access-token handling, refresh rotation, and logout. |

## Migration Rule

The current frontend mock reply generator chooses between scanner, stock analysis, comparison, market summary, portfolio, and deep research results. That behavior must not remain in the product UI.

All chat prompts must use:

```http
POST /api/ai/v1/query
```

The backend owns:

```text
User Message
-> AI Query Orchestrator
-> Intent Detection
-> Tool Routing
-> Scanner Tool / Single Stock Analysis / Market Summary / Portfolio / Deep Search
-> Data Fetching / Cached Data / Third-Party APIs
-> Answer Generation
-> Data Citation / Confidence Score / Usage Accounting
-> Conversation Persistence
```

## Scanner Result Scenario From the Current UI

The current UI example submits the Persian prompt meaning "stocks with high growth and P/E below 6" and renders an answer containing interpreted filter chips, a ranked results table, confidence, usage, and follow-up suggestions.

The screenshot also exposes an important backend rule: "high growth" is ambiguous. If the backend interprets it as `SalesGrowth > 20%`, or adds a market-cap threshold, those choices must come from a documented default policy and be labelled as inferred filters. Otherwise, the AI Query Orchestrator must request clarification. The response must never present a newly added filter as though the user explicitly requested it.

### Required API and Application Services

| Responsibility | Public API or internal service | Output needed by the UI |
| --- | --- | --- |
| Receive the Persian prompt, persist messages, route the request, and return the answer | `POST /api/ai/v1/query` backed by `IAiQueryOrchestrator` | Conversation/message ids, answer payload, and usage result. |
| Detect that the message is a screening query | `IIntentDetectionService` and `IToolRouter` | Informational `detectedIntent: scanner` and selected tool metadata. |
| Extract `P/E < 6` and resolve or clarify "high growth" | `IScannerQueryParser`, `IMetricAliasResolver`, and query-plan validation | Applied filters with canonical metric codes, definition/policy version, explicit versus inferred/default origin, and clarification state. |
| Calculate growth, P/E, and market capitalization from financial data | `IFinancialMetricRegistry`, registered `IFinancialMetricCalculator` strategies, and normalized market-data repositories | Deterministic versioned values and observation timestamps; no LLM-defined formulas or LLM-calculated numbers. |
| Resolve latest price and price movement | `IMarketQuoteResolver` with batch provider/repository access | Live quote values when available; otherwise previous completed trading-day values with source metadata. |
| Execute filtering, rank matching securities, and select rendered fields | `IScannerExecutionService`, `IScannerResultRanker`, and `IScannerResultColumnPolicy` | Ranked table with default or user-requested columns, capped at 10 displayed data columns. |
| Produce the statement above the table and evidence for filter chips/results | `IExplainableAnswerBuilder` | Summary, Confidence Score, Data Citations, warnings, and suggested questions. |
| Calculate the displayed Confidence Score from result evidence | `IConfidenceScoreCalculator` invoked as a required Microsoft Agent Framework workflow function/executor | Score, factor breakdown, and policy version; the AI model cannot supply or alter this number. |
| Charge the displayed credits and return the remaining allowance | `IUsageChargeCalculator` and `IUsageAccountingService` invoked by required workflow steps | Credits charged and credits remaining in the query response; the AI model cannot supply or alter these numbers. |
| Preserve the displayed response for reload/history | Conversation and Message persistence services | Stored assistant content with filters, rows, citations, confidence, and usage outcome. |

### Required Screener Answer Shape

Whenever the assistant answer contains a list of stocks, the `assistantMessage.content.screener` object returned through the AI facade must support the table and filter chips rendered in the existing UI. Unless the user explicitly requests otherwise, its displayed columns are:

```text
Symbol
Latest Price
Price Change Percentage
Market Capitalization
Metrics Relevant To The User's Question
```

For example, a P/E and profitability-growth filter includes P/E and profitability growth columns in addition to the default market columns. A sales-growth filter includes sales growth. The backend limits the final table to 10 displayed data columns for UI clarity and retrieval performance.

```json
{
  "summary": "I found four symbols matching the applied filters and ranked them by signal quality.",
  "appliedFilters": [
    {
      "metric": "PE_TTM",
      "metricVersion": "v1",
      "calculationPolicyVersion": "ttm-valuation-v1",
      "operator": "<",
      "value": 6,
      "displayLabel": "P/E below 6",
      "origin": "explicit"
    },
    {
      "metric": "SALES_GROWTH_YOY",
      "metricVersion": "v1",
      "calculationPolicyVersion": "sales-growth-yoy-v1",
      "operator": ">",
      "value": 20,
      "period": "LatestAvailable",
      "comparison": "YoY",
      "displayLabel": "Sales growth above 20%",
      "origin": "inferred-default",
      "reason": "The phrase 'high growth' uses the configured scanner default."
    }
  ],
  "table": {
    "columns": [
      { "key": "symbol", "label": "Symbol" },
      { "key": "latestPrice", "label": "Latest Price" },
      { "key": "priceChangePercent", "label": "Price Change Percentage" },
      { "key": "marketCapitalization", "label": "Market Capitalization" },
      { "key": "pe", "label": "P/E" },
      { "key": "salesGrowth", "label": "Sales Growth" }
    ],
    "omittedColumns": [],
    "rows": [
    {
      "symbol": "TOSN",
      "companyName": "Example Company",
      "latestPrice": 23450,
      "priceChangePercent": 2.4,
      "priceSource": "LiveQuote",
      "priceAsOf": "2026-05-26T09:30:00Z",
      "pe": 5.8,
      "salesGrowth": 24.1,
      "marketCapitalization": 42000000000000,
      "matchedConditions": [],
      "citations": []
    }
    ]
  },
  "confidenceScore": 0.78,
  "warnings": []
}
```

If live price data is not available, `priceSource` must identify `PreviousCompletedTradingDay` and `priceAsOf` must record the trading-day observation. The result table schema, row values, applied filters, citations, and confidence inputs must be generated by deterministic backend services. The LLM may generate localized prose only after those facts are established.

Financial terminology is extensible through versioned semantic definitions and registered calculator strategies, not hardcoded parser or orchestration branches. Persian and English user wording resolves into canonical metric codes before execution; when a phrase changes meaning depending on its comparison basis, the backend must request clarification or apply an explicitly documented policy.

### Confidence and Credit Calculation Boundary

The confidence and consumed-credit values shown beneath an assistant answer are backend calculation results, not generated content. The Microsoft Agent Framework workflow behind `IAiQueryOrchestrator` must call deterministic Application-layer services as required steps:

```text
Execute Scanner Tool
-> IConfidenceScoreCalculator.Calculate(...)
-> IExplainableAnswerBuilder.Build(...)
-> IUsageChargeCalculator.Calculate(...)
-> IUsageAccountingService.Finalize(...)
-> Persist Assistant Message
```

These services follow SOLID boundaries and are testable without an LLM or agent runtime. Microsoft Agent Framework adapters expose them to the orchestration workflow; framework middleware captures invocation telemetry and correlation, but does not decide the score or debit credits.

## Required Public API Surface

| Priority | UI requirement | Endpoint | Notes |
| --- | --- | --- | --- |
| P0 | Register an owned web-app user | `POST /api/auth/v1/register` | Uses ASP.NET Core Identity and server-side tenant membership rules. |
| P0 | Sign in and receive tokens | `POST /api/auth/v1/login` | Returns short-lived JWT access token plus opaque rotating refresh token. |
| P0 | Rotate an authenticated session | `POST /api/auth/v1/refresh` | Rotates the hashed refresh-token session and rejects replay. |
| P0 | End or explicitly revoke a session | `POST /api/auth/v1/logout`, `POST /api/auth/v1/revoke` | Revokes persisted refresh-token state. |
| P0 | Read the current actor profile | `GET /api/auth/v1/me` | Returns backend-owned identity, tenant, roles, and effective permissions. |
| P0 | Submit a chat prompt and receive an AI answer | `POST /api/ai/v1/query` | The only public frontend-facing chat query endpoint. |
| P0 | List chat history in the sidebar | `GET /api/ai/v1/conversations` | Replaces the current thread list. |
| P0 | Create an empty chat from the sidebar | `POST /api/ai/v1/conversations` | Required because the current New Chat action navigates before a first prompt exists. |
| P0 | Retrieve a conversation | `GET /api/ai/v1/conversations/{conversationId}` | Supplies title and conversation metadata. |
| P0 | Retrieve chat messages | `GET /api/ai/v1/conversations/{conversationId}/messages` | Replaces message table reads. |
| P0 | Delete a chat | `DELETE /api/ai/v1/conversations/{conversationId}` | Replaces sidebar thread deletion. |
| P0 | Display plan and remaining AI credits | `GET /api/v1/usage/me` | Replaces subscription reads; query execution also returns consumed usage. |
| P1 | Display saved watchlist symbols and quote changes | `GET /api/v1/watchlists/me` | Should include quote data needed by the sidebar instead of using local mock data. |
| P1 | Modify a saved watchlist | `PUT /api/v1/watchlists/me` | The database already models a watchlist; required when editing is exposed in the UI. |
| P1 | Populate the context panel | `GET /api/v1/market/summary` | Replaces the mocked total index, money flow, gainers, losers, and industries. |
| P2 | Populate assisted scanner/filter controls | `GET /api/ai/v1/metadata/metrics` | Supporting metadata only; selected filters are still submitted through `/query`. |
| P2 | Populate period choices | `GET /api/ai/v1/metadata/periods` | Supporting metadata only. |
| P2 | Populate symbol search/autocomplete | `GET /api/ai/v1/metadata/symbols` | Supporting metadata only. |
| P2 | Populate industry search/autocomplete | `GET /api/ai/v1/metadata/industries` | Supporting metadata only. |

## Backend-Owned Authentication

The target frontend signs in through FinancialCopilot backend APIs implemented with ASP.NET
Core Identity:

```http
POST /api/auth/v1/register
POST /api/auth/v1/login
POST /api/auth/v1/refresh
POST /api/auth/v1/logout
POST /api/auth/v1/revoke
GET  /api/auth/v1/me
```

Login returns a short-lived JWT access token and an opaque refresh token. Refresh tokens rotate
on use and are persisted only as hashes. The backend resolves tenant membership, roles, and
effective permissions server-side. The frontend must not supply an arbitrary tenant identifier
to gain access to another tenant's conversations, usage, or watchlist.

Authorization is permission-based:

```text
Role -> Permissions -> JWT permission claims -> ASP.NET Core authorization requirement
```

Product access then applies subscription and Billing checks:

```text
permission claim
-> active plan capability and quota
-> AI credit reservation for billable execution
```

See [authorization-and-plan-entitlements.md](./authorization-and-plan-entitlements.md) for the
catalog and initial configurable plan matrix.

Required persistence:

| Table | Responsibility |
| --- | --- |
| `Users` | ASP.NET Core Identity web users and credential state. |
| `Roles` | Named permission groups used for administration. |
| `Permissions` | Stable authorization capability codes. |
| `UserRoles` | User-to-role assignments. |
| `RolePermissions` | Role-to-permission assignments. |
| `RefreshTokens` | Hashed rotating refresh-token sessions and revocation metadata. |
| `Tenants` | Tenant records required by backend actor context. |
| `UserTenants` | Server-owned user-to-tenant membership. |

## AI Query Contract

### Request

```http
POST /api/ai/v1/query
Authorization: Bearer <financial-copilot-jwt>
Content-Type: application/json
```

```json
{
  "conversationId": "f6fc58b7-8ea8-47c4-939e-40d5a141bc83",
  "message": "Find undervalued steel stocks with improving profit growth.",
  "language": "fa-IR",
  "options": {
    "deepResearch": false
  }
}
```

`conversationId` may be `null` when a user submits a first message from the home chat page. In that case, the backend should create the conversation, persist the user message, execute orchestration, account for usage, persist the assistant message, and return the created conversation in one operation.

### Response

The current message renderer expects assistant content with optional stock cards, tables, scanner results, research progress, portfolio analysis, suggested questions, confidence, and consumed credits. A transitional response compatible with that UI is:

```json
{
  "conversation": {
    "id": "f6fc58b7-8ea8-47c4-939e-40d5a141bc83",
    "title": "Undervalued steel stocks",
    "createdAt": "2026-05-26T10:00:00Z",
    "updatedAt": "2026-05-26T10:00:04Z"
  },
  "userMessage": {
    "id": "73d12152-f7cd-401f-b564-2ee1a50c81bc",
    "role": "user",
    "content": {
      "message": "Find undervalued steel stocks with improving profit growth."
    },
    "createdAt": "2026-05-26T10:00:00Z"
  },
  "assistantMessage": {
    "id": "e8babf22-ae2b-4272-9d43-3df28a75db55",
    "role": "assistant",
    "content": {
      "message": "I found the following candidates based on your criteria.",
      "confidenceScore": 0.86,
      "creditsUsed": 1.0,
      "suggestedQuestions": [
        "Compare the two strongest candidates."
      ],
      "cards": [],
      "tables": [],
      "screener": null,
      "research": null,
      "portfolio": null,
      "citations": [
        {
          "source": "Normalized market data",
          "asOf": "2026-05-26T09:30:00Z"
        }
      ]
    },
    "createdAt": "2026-05-26T10:00:04Z"
  },
  "usage": {
    "creditsCharged": 1.0,
    "remainingBalance": 99.0,
    "pricingPolicyVersion": "v1",
    "cached": false,
    "plan": "free"
  }
}
```

The backend can later version assistant content into a discriminated `blocks` contract, but the first replacement API must cover every result type currently rendered by the React application.

## Conversation Contracts

### List Conversations

```http
GET /api/ai/v1/conversations
```

```json
[
  {
    "id": "f6fc58b7-8ea8-47c4-939e-40d5a141bc83",
    "title": "Undervalued steel stocks",
    "createdAt": "2026-05-26T10:00:00Z",
    "updatedAt": "2026-05-26T10:00:04Z"
  }
]
```

### Create Empty Conversation

```http
POST /api/ai/v1/conversations
```

```json
{
  "title": "New conversation"
}
```

The backend should generate or update the final title after the first successful query, matching the current sidebar behavior.

### Retrieve Messages

```http
GET /api/ai/v1/conversations/{conversationId}/messages
```

```json
[
  {
    "id": "73d12152-f7cd-401f-b564-2ee1a50c81bc",
    "role": "user",
    "content": {
      "message": "Find undervalued steel stocks."
    },
    "createdAt": "2026-05-26T10:00:00Z"
  }
]
```

### Delete Conversation

```http
DELETE /api/ai/v1/conversations/{conversationId}
```

Expected success status: `204 No Content`.

## Usage Accounting Contract

The sidebar currently reads the user's plan and remaining credits independently of chat execution.

```http
GET /api/v1/usage/me
```

```json
{
  "plan": "free",
  "creditsRemaining": 99,
  "creditsTotal": 100,
  "updatedAt": "2026-05-26T10:00:04Z"
}
```

Every `POST /api/ai/v1/query` must resolve the billed customer, enforce/reserve available spending capacity before billable work, and commit or release operation-based usage on outcome. The backend returns the updated usage summary. The frontend must not calculate or decrement credits locally.

## Watchlist Contract

The sidebar currently combines persisted watchlist symbols with mocked quote changes. The replacement endpoint should supply both in one read to avoid one request per symbol.

```http
GET /api/v1/watchlists/me
```

```json
{
  "symbols": ["FOLD", "MOBN"],
  "items": [
    {
      "symbol": "FOLD",
      "name": "Foulad",
      "price": 5120,
      "changePercent": 1.4
    }
  ],
  "updatedAt": "2026-05-26T09:30:00Z"
}
```

When watchlist editing is connected:

```http
PUT /api/v1/watchlists/me
```

```json
{
  "symbols": ["FOLD", "MOBN", "SHEP"]
}
```

## Market Summary Contract

The context panel renders market-level information separately from an individual conversation. This is a read-only market endpoint, not a chat routing endpoint.

```http
GET /api/v1/market/summary
```

```json
{
  "totalIndex": 2419500,
  "totalIndexChangePercent": 0.72,
  "weightedIndex": 756220,
  "weightedIndexChangePercent": 0.35,
  "realMoneyFlow": 128000000000,
  "tradingVolume": 6400000000,
  "topGainers": [
    {
      "symbol": "FOLD",
      "changePercent": 4.1
    }
  ],
  "topLosers": [],
  "trendingIndustries": [
    {
      "name": "Metals",
      "changePercent": 2.2
    }
  ],
  "insight": "Money flow improved in metal-related symbols.",
  "asOf": "2026-05-26T09:30:00Z"
}
```

## Frontend Replacement Map

| Current frontend operation or mock | Replacement backend API |
| --- | --- |
| `listThreads` | `GET /api/ai/v1/conversations` |
| `createThread` from the sidebar | `POST /api/ai/v1/conversations` |
| `createThread` followed by first `sendChatMessage` | Prefer one `POST /api/ai/v1/query` with `conversationId: null`. |
| `getThreadMessages` | `GET /api/ai/v1/conversations/{conversationId}/messages` |
| `deleteThread` | `DELETE /api/ai/v1/conversations/{conversationId}` |
| `sendChatMessage`, `generateMockReply`, title update, and local credit decrement | `POST /api/ai/v1/query` |
| `getSubscription` | `GET /api/v1/usage/me` |
| `getWatchlist` plus mocked sidebar price data | `GET /api/v1/watchlists/me` |
| `MARKET_SNAPSHOT` in the context panel | `GET /api/v1/market/summary` |
| Supabase authentication and bearer attachment | Replace with backend register/login, JWT access-token forwarding, refresh rotation, and logout. |
| Future scanner/filter selector UI | Fetch metadata endpoints; submit the final user request through `POST /api/ai/v1/query`. |

## Internal Services, Not Frontend APIs

The following concerns belong in the Application layer behind the AI Query Orchestrator:

```text
IAiQueryOrchestrator
IIntentDetectionService
IToolRouter
IScannerQueryParser
IScannerExecutionService
IScannerResultRanker
IScannerResultColumnPolicy
IMarketQuoteResolver
IMetricAliasResolver
IFinancialMetricRegistry
IMetricCalculationPolicyProvider
IFinancialMetricCalculator
IConfidenceScoreCalculator
ISingleStockAnalysisService
IMarketSummaryService
IPortfolioAnalysisService
IDeepSearchService
IUsageChargeCalculator
IUsageAccountingService
```

The React UI must not call these scanner-specific endpoints:

```http
POST /api/v1/scanner/query
POST /api/v1/scanner/parse
POST /api/v1/scanner/execute
```

If scanner diagnostics are later exposed for administrators, they must use internal-only routes such as:

```http
POST /api/internal/scanner/parse
POST /api/internal/scanner/execute
```

They are not part of the public frontend API.

## Security and Behavior Requirements

| Area | Requirement |
| --- | --- |
| Authorization | Every conversation, usage, and watchlist operation is scoped to the authenticated user and resolved tenant. |
| Persistence | The backend persists both user and assistant messages and owns conversation title updates. |
| Usage | The backend checks allowance and records consumed credits during AI query execution. |
| Explainability | Assistant answers return a Confidence Score and Data Citations when market data influences the answer. |
| Errors | Public APIs use the shared problem-details error contract and return a correlation identifier. |
| Localization | Request language may be `fa-IR`, while API schema names remain English. |

## Suggested Implementation Order

1. Implement ASP.NET Core Identity, JWT access tokens, refresh-token rotation, permission
   policies, and the server-only TanStack API client.
2. Complete conversation lifecycle and reloadable structured assistant persistence.
3. Replace thread and message server functions, `generateMockReply`, and local credit changes.
4. Connect `GET /api/v1/usage/me` and render backend explainability DTOs.
5. Expose watchlist quotes and market summary data to remove sidebar and context-panel mocks.
6. Add metadata endpoints when assisted query/filter controls are implemented.
