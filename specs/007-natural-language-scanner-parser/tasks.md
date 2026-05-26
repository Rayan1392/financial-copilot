# Tasks

- Define ScannerQueryPlan DTO/model.
- Define AI Query Orchestrator and Intent Detection contracts needed to select the Scanner Tool.
- Define Conversation and Message API contracts.
- Define JSON schema for LLM output.
- Implement IScannerQueryParser.
- Implement LLM parser adapter.
- Implement rule-based fallback for common queries.
- Implement plan validator.
- Integrate parser invocation behind `POST /api/ai/v1/query`; do not expose a public scanner parse endpoint.
- Add generic Conversation and Message history endpoints for AI chat history.
- Add tests for example questions.
