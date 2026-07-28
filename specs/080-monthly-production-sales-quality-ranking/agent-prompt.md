# Prompt for Coding Agent — Feature 080

Use codebase-memory-mcp first.

project_name: D-Source-TahlilApp-AI
repo_path: D:\Source\TahlilApp-AI
The repository is already indexed.

Implement Feature 080: Monthly Production & Sales Quality Ranking.

Read these files first:
- `user-story.md`
- `tasks.md`
- `acceptance-criteria.md`
- `scoring-model.md`

## Objective

Create a deterministic, explainable ranking feature that scores and ranks companies based on the quality of their monthly production and sales reports.

The ranking must answer questions like:
- «بهترین گزارش‌های ماهانه بازار کدامند؟»
- «۱۰ گزارش برتر تولید و فروش این ماه را بگو»
- «کدام نمادها رشد فروش باکیفیت داشتند؟»
- «گزارش‌های ماهانه ضعیف این ماه کدامند؟»
- «در صنعت فلزات اساسی کدام شرکت‌ها گزارش ماهانه بهتری داشتند؟»

## Important Constraints

1. Do not use LLM for numeric scoring.
2. Do not make this feature depend on latest market price or price provider.
3. Do not present results as investment advice.
4. Reuse existing monthly production/sales data model.
5. Reuse Feature 075 product revenue mix data if available.
6. Missing dimensions must be reweighted, not scored as zero.
7. Confidence must be separate from quality score.
8. Ranking must be persisted as snapshots for fast reads.
9. AI routing must not break:
   - latest monthly sales lookup
   - product revenue mix lookup
   - price lookup
   - PE/PS/direct metric lookup

## Implementation Order

1. Inspect current data model.
2. Add contracts.
3. Implement deterministic score calculator.
4. Add persistence snapshot entity and migration.
5. Implement repository.
6. Implement recalculation use case.
7. Add API endpoint.
8. Integrate recalculation after monthly ingestion.
9. Add AI intent/routing.
10. Add response composer.
11. Add tests.
12. Add documentation.

## Deliverable

Make code changes and add/adjust tests. At the end, report:
- files changed
- migrations created
- tests added
- how to run recalculation
- example API call
- example Persian AI query
