# Tasks

- Define ScannerQueryPlan DTO/model.
- Define AI Query Orchestrator and Intent Detection contracts needed to select the Scanner Tool.
- Define Conversation and Message API contracts.
- Define JSON schema for LLM output.
- Implement IScannerQueryParser.
- Implement LLM parser adapter against the provider-neutral contracts defined by `014-ai-model-provider-abstraction`.
- Implement rule-based fallback for common queries.
- Integrate `IMetricAliasResolver`/semantic catalog resolution for Persian and English terms such as `high growth`, retaining canonical `MetricCode`, alias, policy, and metric-version evidence.
- Add filter-origin metadata (`explicit`, `inferred-default`, `clarified`) to the scanner plan contract.
- Add requested result-column metadata and 10-column validation to the scanner plan contract.
- Implement plan validator.
- Integrate parser invocation behind `POST /api/ai/v1/query`; do not expose a public scanner parse endpoint.
- Add generic Conversation and Message history endpoints for AI chat history.
- Define orchestration integration points for mandatory Billing reservation/finalization without implementing Billing policy inside parser services.
- Add tests running parser scenarios through the deterministic fake AI model provider and provider-capability fallback behavior.
- Add tests for example questions, including the Persian `high growth and P/E below 6` scenario, rejection of unapproved extra filters, and explicit column overrides.
- Add tests proving aliases resolve to semantic metric identifiers and unknown/ambiguous terminology never falls through to hardcoded property guesses.
- Add tests proving the LLM may propose candidates but only backend alias/policy validation establishes the executable canonical metric code.
