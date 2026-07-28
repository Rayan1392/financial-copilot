# Tasks

1. Extend conversation persistence with title and versioned structured assistant content.
2. Add repository methods for actor-scoped create, find, list, delete, and message retrieval.
3. Add `POST /api/ai/v1/conversations` and
   `DELETE /api/ai/v1/conversations/{conversationId}`.
4. Tighten existing history endpoints so tenant peers cannot read another actor's messages.
5. Return a stable DTO for both immediate query responses and reloaded assistant messages.
   Include top-level `confidenceScore` in both paths so structured symbol lookup messages reload
   with the same confidence shown at creation time.
6. Replace Supabase chat server functions with calls through the authenticated API client
   delivered by spec `031`.
7. Remove `generateMockReply` from the production chat path and stop client-side credit changes.
8. Adapt `MessageList` to backend query and persisted-message DTOs.
   Confidence rendering must prefer backend `confidenceScore` before falling back to
   `explainableAnswer.confidence`; missing scanner explainability must not turn a structured
   symbol lookup into `0%`.
9. Hide or disable controls whose backend behavior is not implemented.
10. Add authorization, persistence-reload, query, and frontend build verification.

## Implementation Status

Completed on 2026-06-01. The sidebar usage, watchlist, and market-summary prototype reads remain
isolated for spec `033`; they are not part of the production chat persistence path.
