# User Story — Personalized Symbol Intelligence Feed

## Status
`[x]` Implemented 2026-07-10

## Feature
Personalized insight feed for the user's followed symbols, powered by the reusable market insight event engine.

## Story

As a TahlilApp-AI user with followed symbols,

I want the assistant to show important events for the symbols I follow,

so that I can quickly understand what changed in the companies I care about without asking separate questions.

## Business Context

Feature 084 creates market-wide `InsightEvent` records. Feature 085 creates the user's followed-symbol list. This feature combines both capabilities and exposes a personalized feed.

The feed is still not portfolio intelligence. It must not infer position size, profit/loss, exposure, or risk from followed symbols.

## In Scope

- Personalized feed filtered by followed symbols.
- Ranking by event importance, freshness, severity, and user relevance.
- Insight card actions: open symbol, open source report, ask AI about this insight, dismiss.
- Seen/dismissed state if needed for stable UX.
- AI explanation bridge using persisted insight evidence.

## Out of Scope

- Full portfolio analysis.
- Push notifications.
- Investment recommendations.
- ML prediction.
- User risk profiling.

## Acceptance Criteria

1. Authenticated users can retrieve a feed containing only events related to their followed symbols.
2. If the user follows no symbols, the API returns an empty-state response with suggested actions.
3. The feed preserves all evidence, source, freshness, and confidence fields from the underlying `InsightEvent`.
4. The feed ranks high-severity and fresh events above low-severity or stale events.
5. The feed supports pagination and filters by insight type and severity.
6. Users can mark an insight as seen or dismissed if `UserInsightState` is implemented.
7. The AI explanation bridge can explain an insight using the persisted evidence without changing numeric values.
8. The renderer must not present insights as buy/sell recommendations.
9. The feed response includes actionable next steps.
10. The implementation reuses Feature 084 insight events and must not duplicate detector logic.

## API Proposal

```http
GET /api/v1/insights/followed-symbols/me
POST /api/v1/insights/{insightEventId}/seen
POST /api/v1/insights/{insightEventId}/dismiss
```

## AI Bridge Proposal

```http
POST /api/ai/v1/query
```

```json
{
  "message": "Explain this insight",
  "context": {
    "insightEventId": "..."
  }
}
```

## Empty State Example

```text
You are not following any symbols yet. Follow symbols from a company page or an AI answer card to receive a personalized intelligence feed.
```
