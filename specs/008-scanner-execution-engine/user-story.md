# User Story — Scanner Execution Engine

## Story

As an investor,  
I want screening questions submitted in my Conversation to return matching symbols,
so that I can quickly find fundamental and valuation candidates without selecting backend services.

## Acceptance Criteria

- Scanner Use Case executes a validated scanner plan after Tool Routing selects it.
- Support AND conditions for Phase 1.
- Support operators: <, <=, >, >=, =, between.
- Support sorting and limit.
- Return symbol, company, industry, score, matched conditions, and source metadata.
- Handle missing data with warnings.
- Never execute raw SQL produced by AI.
- Query performance is acceptable for normalized datasets.
- Scanner results are returned inside the Explainable Answer produced by `POST /api/ai/v1/query`.
- Scanner execution is not exposed as a frontend-facing public endpoint.

## Technical Notes

- Start with EF Core expression building or predefined query handlers.
- Prefer deterministic query model over arbitrary dynamic SQL.
- `IScannerExecutionService` and `IScannerResultRanker` belong in the Application layer behind `IAiQueryOrchestrator`.
