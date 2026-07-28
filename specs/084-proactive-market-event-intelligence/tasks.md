# Tasks — Proactive Market Event Intelligence

## 1. Domain and Contracts

- [x] Add `InsightEvent` domain/read-model entity.
- [x] Add enums: `InsightType`, `InsightSeverity`, `InsightSourceEntityType`.
- [x] Add `InsightEvidenceItem` contract for source-bound evidence.
- [x] Add `InsightFeedQuery`, `InsightFeedResponse`, and `InsightFeedItem` DTOs.
- [x] Add `IInsightEventRepository` abstraction.
- [x] Add `IInsightDetector` abstraction.
- [x] Add `IInsightScoringService` abstraction.
- [x] Add `IInsightDeduplicationPolicy` abstraction.

## 2. Persistence

- [x] Create migration for `InsightEvents`.
- [x] Enforce unique index on `DeduplicationKey`.
- [x] Add indexes for `DetectedAtUtc`, `ExternalCompanyId`, `Symbol`, `InsightType`, and `Severity`.
- [x] Store evidence as JSON with deterministic numeric values and source metadata.
- [x] Store source provider and source period explicitly.

## 3. Detectors

- [x] Implement `MonthlyReportPublishedDetector`.
- [x] Implement `MonthlySalesAnomalyDetector` using latest sales versus 12-month average or configured baseline.
- [x] Implement `MonthlyQualityRankingChangeDetector` using Feature 080 outputs.
- [x] Implement `PriceMovementDetector` using canonical latest quote / daily trade projections.
- [x] Implement `ComprehensiveAnalysisPublishedDetector` using CyclicalWaves analysis data.
- [x] Implement `FinancialStatementPublishedDetector` using Noavaran financial-statement persistence.
- [x] Implement `DataFreshnessDetector` using sync metadata and stale-data policies.

## 4. Scoring and Ranking

- [x] Define severity thresholds per detector.
- [x] Define importance score formula using magnitude, freshness, source confidence, and rarity.
- [x] Define confidence score factors using evidence completeness, source freshness, and detector reliability.
- [x] Ensure scoring is deterministic and unit-testable.

## 5. Application Use Cases

- [x] Add `GenerateMarketInsightsUseCase` for scheduled or admin-triggered detection.
- [x] Add `GetMarketInsightFeedUseCase`.
- [x] Add `GetSymbolInsightFeedUseCase`.
- [x] Add optional `GetIndustryInsightFeedUseCase`.
- [x] Add pagination and filters: type, severity, symbol, industry, date range.

## 6. API

- [x] Add `GET /api/v1/insights/market`.
- [x] Add `GET /api/v1/insights/symbol/{symbol}`.
- [x] Add `GET /api/v1/insights/industries/{industryCode}` if industry metadata is available.
- [x] Add admin/manual trigger endpoint only if consistent with existing data-sync admin patterns.

## 7. AI and Explainability

- [x] Ensure AI renderer can summarize persisted insight events without changing numbers.
- [x] Ensure each item exposes source provider, source period, freshness, confidence, and evidence.
- [x] Add guardrail: no buy/sell recommendation or trading signal language.
- [x] Add suggested follow-up actions: `OpenSymbol`, `OpenSourceReport`, `AskAiAboutThis`.

## 8. Tests

- [x] Unit-test each detector.
- [x] Unit-test deduplication keys.
- [x] Unit-test scoring policies.
- [x] Integration-test feed query ordering and filtering.
- [x] Regression-test that stale/missing data creates warnings rather than hallucinated insights.
- [x] Regression-test that the AI answer cannot overwrite evidence or confidence.

## Implementation Notes

- Implemented in `Domain.Financial.Insights`, `Application.FinancialData.Insights`, `Infrastructure.Financial.Insights`, and API `MarketInsightsController`.
- Added EF migration `20260709133527_AddInsightEvents` for the `InsightEvents` table and required indexes.
- Added deterministic feed rendering through persisted `Title`, `Summary`, `Reason`, `Evidence`, `ConfidenceScore`, and `SuggestedActions`; LLM explanation over `insightEventId` remains owned by feature `086`.
- Validation passed:
  - `dotnet build src\backend\FinancialCopilot.API\FinancialCopilot.API.csproj --configuration Release -m:1`
  - `dotnet test tests\FinancialCopilot.UnitTests\FinancialCopilot.UnitTests.csproj --configuration Release -m:1 --filter FullyQualifiedName~MarketInsight084Tests`
  - `dotnet test tests\FinancialCopilot.IntegrationTests\FinancialCopilot.IntegrationTests.csproj --configuration Release -m:1 --filter FullyQualifiedName~MarketInsightEndpointTests`
  - `dotnet test tests\FinancialCopilot.ArchitectureTests\FinancialCopilot.ArchitectureTests.csproj --configuration Release -m:1`
