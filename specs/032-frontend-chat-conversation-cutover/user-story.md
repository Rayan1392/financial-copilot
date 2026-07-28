# Frontend Chat And Conversation API Cutover

## User Story

As a web user, I want chat messages and conversation history to use the FinancialCopilot AI
facade so the visible answers, persistence, citations, confidence, and consumed credits come
from backend services instead of frontend mocks and Supabase tables.

## Current Gap

`src/frontend/src/lib/chat.functions.ts` writes Supabase `threads` and `messages`, calls
`generateMockReply`, and decrements mock credits locally. The backend already exposes
`POST /api/ai/v1/query` and conversation reads, but it lacks empty-conversation creation,
deletion, titles, actor-level ownership checks for every history read, and reloadable structured
assistant-message content.

## Scope

- Complete the backend conversation lifecycle required by the existing sidebar.
- Persist a versioned structured assistant payload that can be rendered after reload.
- Replace Supabase chat persistence and mock reply generation with .NET API calls.
- Adapt the current React renderer to backend scanner table, explainable answer, citations,
  clarification, top-level confidence, and usage contracts.
- Keep all user prompts routed only through `POST /api/ai/v1/query`.

## Acceptance Criteria

1. `POST /api/ai/v1/query` with `conversationId=null` atomically creates a conversation and
   persists user and assistant messages.
2. `POST /api/ai/v1/conversations` creates an empty conversation for the sidebar New Chat action.
3. `GET /api/ai/v1/conversations`, conversation detail, and message reads are tenant- and
   actor-scoped.
4. `DELETE /api/ai/v1/conversations/{conversationId}` deletes only the current actor's
   conversation and returns `204`.
5. Conversation summaries include a display title; the backend owns default and first-message
   title generation.
6. Reloaded assistant messages preserve structured answer content needed by the renderer.
7. The frontend no longer calls `generateMockReply`, writes Supabase chat tables, or decrements
   credits.
8. Structured financial answers render backend text, tables, freshness, citations, top-level
   confidence, follow-up questions when present, and usage metadata. Scanner answers additionally
   render filter chips and ranked rows.
9. The frontend prefers backend `confidenceScore` over `explainableAnswer.confidence`; a symbol
   lookup response with a structured table must not display `0%` merely because no scanner
   explainable answer exists.
10. Unsupported prototype actions such as deep research and portfolio analysis are hidden or
   visibly disabled until backend capabilities are specified and implemented.
11. Backend integration tests and frontend lint/build checks pass.

## Out Of Scope

- New scanner-specific public endpoints.
- Portfolio-management implementation.
- Streaming transport; the first cutover may remain request/response.
- Migrating old Supabase prototype threads into PostgreSQL.
