# Tasks — Personalized Symbol Intelligence Feed

## 1. Dependencies

- [ ] Confirm Feature 084 `InsightEvent` persistence exists.
- [ ] Confirm Feature 085 followed-symbols API exists.
- [ ] Confirm actor/user identity is available in the API context.

## 2. Contracts

- [ ] Add `GetMyFollowedSymbolInsightsQuery`.
- [ ] Add `FollowedSymbolInsightFeedResponse`.
- [ ] Add `InsightActionDto`.
- [ ] Add optional `UserInsightState` entity for seen/dismissed state.
- [ ] Add `IUserInsightStateRepository` if seen/dismissed state is implemented.

## 3. Use Cases

- [ ] Implement `GetMyFollowedSymbolInsightsUseCase`.
- [ ] Retrieve followed symbols for the current actor.
- [ ] Query `InsightEvents` by followed external company ids.
- [ ] Apply ranking by severity, importance score, freshness, and confidence.
- [ ] Return empty-state response if no followed symbols exist.
- [ ] Return empty-state response if followed symbols exist but no current insights exist.

## 4. API

- [ ] Add `GET /api/v1/insights/followed-symbols/me`.
- [ ] Add `POST /api/v1/insights/{insightEventId}/seen` if state is implemented.
- [ ] Add `POST /api/v1/insights/{insightEventId}/dismiss` if state is implemented.
- [ ] Add filters: type, severity, date range, includeDismissed.
- [ ] Add pagination.

## 5. AI Explanation Bridge

- [ ] Add support for structured `insightEventId` context in AI query flow.
- [ ] Load persisted insight evidence before generating the explanation.
- [ ] Ensure the LLM cannot modify numeric evidence, confidence, source, or period.
- [ ] Add guardrail wording to prevent buy/sell recommendations.
- [ ] Add suggested follow-up questions based on insight type.

## 6. Frontend UX

- [ ] Add personalized intelligence feed panel.
- [ ] Add card actions: open symbol, open source report, ask AI, dismiss.
- [ ] Add empty state for no followed symbols.
- [ ] Add empty state for no current insights.
- [ ] Show source, period, freshness, and confidence on every card.

## 7. Tests

- [ ] Integration-test actor isolation.
- [ ] Integration-test feed contains only followed-symbol events.
- [ ] Integration-test ranking order.
- [ ] Integration-test empty states.
- [ ] Regression-test that detector logic is not duplicated in this feature.
- [ ] Regression-test AI explanation preserves evidence and avoids advice wording.
