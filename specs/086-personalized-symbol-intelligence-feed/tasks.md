# Tasks — Personalized Symbol Intelligence Feed

## 1. Dependencies

- [x] Confirm Feature 084 `InsightEvent` persistence exists.
- [x] Confirm Feature 085 followed-symbols API exists.
- [x] Confirm actor/user identity is available in the API context.

## 2. Contracts

- [x] Add `GetMyFollowedSymbolInsightsQuery`.
- [x] Add `FollowedSymbolInsightFeedResponse`.
- [x] Add `InsightActionDto`.
- [x] Add optional `UserInsightState` entity for seen/dismissed state.
- [x] Add `IUserInsightStateRepository` if seen/dismissed state is implemented.

## 3. Use Cases

- [x] Implement `GetMyFollowedSymbolInsightsUseCase`.
- [x] Retrieve followed symbols for the current actor.
- [x] Query `InsightEvents` by followed external company ids.
- [x] Apply ranking by severity, importance score, freshness, and confidence.
- [x] Return empty-state response if no followed symbols exist.
- [x] Return empty-state response if followed symbols exist but no current insights exist.

## 4. API

- [x] Add `GET /api/v1/insights/followed-symbols/me`.
- [x] Add `POST /api/v1/insights/{insightEventId}/seen` if state is implemented.
- [x] Add `POST /api/v1/insights/{insightEventId}/dismiss` if state is implemented.
- [x] Add filters: type, severity, date range, includeDismissed.
- [x] Add pagination.

## 5. AI Explanation Bridge

- [x] Add support for structured `insightEventId` context in AI query flow.
- [x] Load persisted insight evidence before generating the explanation.
- [x] Ensure the LLM cannot modify numeric evidence, confidence, source, or period.
- [x] Add guardrail wording to prevent buy/sell recommendations.
- [x] Add suggested follow-up questions based on insight type.

## 6. Frontend UX

- [x] Add personalized intelligence feed panel.
- [x] Add card actions: open symbol, open source report, ask AI, dismiss.
- [x] Add empty state for no followed symbols.
- [x] Add empty state for no current insights.
- [x] Show source, period, freshness, and confidence on every card.

## 7. Tests

- [x] Integration-test actor isolation.
- [x] Integration-test feed contains only followed-symbol events.
- [x] Integration-test ranking order.
- [x] Integration-test empty states.
- [x] Regression-test that detector logic is not duplicated in this feature.
- [x] Regression-test AI explanation preserves evidence and avoids advice wording.

## Implementation Notes

- Implemented in `Domain.Financial.Insights`, `Application.FinancialData.Insights`, `Infrastructure.Financial.Insights`, API `MarketInsightsController`, and the followed-symbols frontend route.
- Added EF migration `20260710063425_AddUserInsightStates` for actor-scoped seen/dismissed state.
- Reused persisted `InsightEvents` from Feature 084 and followed `ExternalCompanyId` values from Feature 085; no detector logic was added or duplicated.
- AI explanation uses `POST /api/ai/v1/query` with `context.insightEventId` and returns deterministic evidence-preserving text, including the no buy/sell recommendation guardrail.
- Validation:
  - `dotnet build src/backend/FinancialCopilot.API/FinancialCopilot.API.csproj --configuration Release --no-restore -m:1`
  - `npm run build` in `src/frontend`
  - `dotnet test tests/FinancialCopilot.IntegrationTests/FinancialCopilot.IntegrationTests.csproj --configuration Release -m:1 --filter FullyQualifiedName~MarketInsightEndpointTests`
  - `dotnet test tests/FinancialCopilot.UnitTests/FinancialCopilot.UnitTests.csproj --configuration Release -m:1 --filter "FullyQualifiedName~MarketInsight084Tests|FullyQualifiedName~FollowedSymbols085Tests"`
  - `dotnet test tests/FinancialCopilot.ArchitectureTests/FinancialCopilot.ArchitectureTests.csproj --configuration Release -m:1`
