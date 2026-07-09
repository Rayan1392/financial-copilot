# Tasks — Proactive Market Event Intelligence

## 1. Domain and Contracts

- [ ] Add `InsightEvent` domain/read-model entity.
- [ ] Add enums: `InsightType`, `InsightSeverity`, `InsightSourceEntityType`.
- [ ] Add `InsightEvidenceItem` contract for source-bound evidence.
- [ ] Add `InsightFeedQuery`, `InsightFeedResponse`, and `InsightFeedItem` DTOs.
- [ ] Add `IInsightEventRepository` abstraction.
- [ ] Add `IInsightDetector` abstraction.
- [ ] Add `IInsightScoringService` abstraction.
- [ ] Add `IInsightDeduplicationPolicy` abstraction.

## 2. Persistence

- [ ] Create migration for `InsightEvents`.
- [ ] Enforce unique index on `DeduplicationKey`.
- [ ] Add indexes for `DetectedAtUtc`, `ExternalCompanyId`, `Symbol`, `InsightType`, and `Severity`.
- [ ] Store evidence as JSON with deterministic numeric values and source metadata.
- [ ] Store source provider and source period explicitly.

## 3. Detectors

- [ ] Implement `MonthlyReportPublishedDetector`.
- [ ] Implement `MonthlySalesAnomalyDetector` using latest sales versus 12-month average or configured baseline.
- [ ] Implement `MonthlyQualityRankingChangeDetector` using Feature 080 outputs.
- [ ] Implement `PriceMovementDetector` using canonical latest quote / daily trade projections.
- [ ] Implement `ComprehensiveAnalysisPublishedDetector` using CyclicalWaves analysis data.
- [ ] Implement `FinancialStatementPublishedDetector` using Noavaran financial-statement persistence.
- [ ] Implement `DataFreshnessDetector` using sync metadata and stale-data policies.

## 4. Scoring and Ranking

- [ ] Define severity thresholds per detector.
- [ ] Define importance score formula using magnitude, freshness, source confidence, and rarity.
- [ ] Define confidence score factors using evidence completeness, source freshness, and detector reliability.
- [ ] Ensure scoring is deterministic and unit-testable.

## 5. Application Use Cases

- [ ] Add `GenerateMarketInsightsUseCase` for scheduled or admin-triggered detection.
- [ ] Add `GetMarketInsightFeedUseCase`.
- [ ] Add `GetSymbolInsightFeedUseCase`.
- [ ] Add optional `GetIndustryInsightFeedUseCase`.
- [ ] Add pagination and filters: type, severity, symbol, industry, date range.

## 6. API

- [ ] Add `GET /api/v1/insights/market`.
- [ ] Add `GET /api/v1/insights/symbol/{symbol}`.
- [ ] Add `GET /api/v1/insights/industries/{industryCode}` if industry metadata is available.
- [ ] Add admin/manual trigger endpoint only if consistent with existing data-sync admin patterns.

## 7. AI and Explainability

- [ ] Ensure AI renderer can summarize persisted insight events without changing numbers.
- [ ] Ensure each item exposes source provider, source period, freshness, confidence, and evidence.
- [ ] Add guardrail: no buy/sell recommendation or trading signal language.
- [ ] Add suggested follow-up actions: `OpenSymbol`, `OpenSourceReport`, `AskAiAboutThis`.

## 8. Tests

- [ ] Unit-test each detector.
- [ ] Unit-test deduplication keys.
- [ ] Unit-test scoring policies.
- [ ] Integration-test feed query ordering and filtering.
- [ ] Regression-test that stale/missing data creates warnings rather than hallucinated insights.
- [ ] Regression-test that the AI answer cannot overwrite evidence or confidence.
