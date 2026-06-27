# Feature 080 — Acceptance Criteria

## Functional Acceptance Criteria

1. The system can calculate a monthly production/sales quality score for each eligible company in a selected report period.
2. The system can rank companies market-wide by quality score.
3. The system can rank companies within a selected industry/group.
4. The system can return top ranking and bottom ranking.
5. The system can select the latest available report period when period is not provided.
6. The response includes score, label, rank, explanation drivers, confidence, and data coverage.
7. The score is deterministic and does not require an LLM call.
8. Missing product line items do not crash the calculation.
9. Missing product mix data causes reweighting, not automatic zero score.
10. Companies with no valid sales amount are excluded or marked ineligible according to query settings.
11. Extreme outliers are capped/normalized and cannot produce scores outside 0..100.
12. Confidence score decreases when historical data, product line items, or industry peers are insufficient.
13. Persisted snapshots are idempotent per company/report period.
14. API query is performant using persisted snapshots.
15. AI can answer Persian natural-language ranking questions using this feature.
16. AI response clearly says this is production/sales quality ranking, not buy/sell advice.
17. AI routing does not conflict with:
    - latest monthly sales
    - product revenue mix
    - price
    - PE/PS/direct metrics

## Technical Acceptance Criteria

1. Application contracts are added in the appropriate Application layer.
2. Score calculator has unit tests.
3. Repository has tests for query and upsert behavior.
4. API endpoint has contract tests.
5. Intent detection has tests with Persian query examples.
6. Migration creates required table/indexes.
7. Recalculation use case logs eligible/skipped counts.
8. CancellationToken is used in async operations.
9. No provider-specific market price filter is introduced.
10. No raw SQL is used unless justified; EF Core preferred.

## Example Done Definition

Feature is done when this works:

User:
«۱۰ گزارش برتر تولید و فروش اردیبهشت ۱۴۰۵ را بگو»

System:
- detects `MonthlySalesQualityRanking`
- resolves report period 1405/02
- queries ranking snapshots or recalculates if explicitly triggered
- returns top 10 rows
- AI formats Persian table
- includes score, confidence, and explanation
- does not recommend buy/sell
