# User Story — Scanner Execution Engine

## Story

As an investor,  
I want scanner results returned from validated financial conditions,  
so that I can quickly find symbols matching fundamental and valuation criteria.

## Acceptance Criteria

- Execute a validated scanner plan.
- Support AND conditions for Phase 1.
- Support operators: <, <=, >, >=, =, between.
- Support sorting and limit.
- Return symbol, company, industry, score, matched conditions, and source metadata.
- Handle missing data with warnings.
- Never execute raw SQL produced by AI.
- Query performance is acceptable for normalized datasets.

## Technical Notes

- Start with EF Core expression building or predefined query handlers.
- Prefer deterministic query model over arbitrary dynamic SQL.
