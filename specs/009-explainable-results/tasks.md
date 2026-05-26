# Tasks

- Define explanation DTOs.
- Implement explanation builder.
- Add applied-filter DTOs with display label, normalized condition, and condition origin.
- Add screener table response DTO mapping with ordered columns, rows, source timestamps, live/fallback status, and omitted-column warnings for the current React result component.
- Add source metadata mapping.
- Add semantic metric definition/calculation-policy version evidence to displayed metric explanation DTOs.
- Define `IConfidenceScoreCalculator`, confidence policy inputs, factor breakdown DTO, and policy/version metadata.
- Implement deterministic confidence score policy and unit tests for freshness, missing evidence, ambiguity, and warning penalties.
- Add a Microsoft Agent Framework workflow function/executor adapter that invokes `IConfidenceScoreCalculator` after scanner execution and supplies its immutable result to answer assembly.
- Implement contextual suggested-question generation for completed scanner answers.
- Integrate optional prose/suggested-question generation through provider-neutral AI model interfaces and deterministic response evidence.
- Map Explainable Answer output into the AI facade assistant Message contract.
- Map Billing-provided usage metadata beside the Explainable Answer without adding charging logic to explanation services.
- Add tests ensuring explanation numbers, displayed filter chips, suggested questions, and table columns/values match the executed plan and returned result context.
- Add integration tests proving the facade returns the backend-computed Confidence Score and that generated prose cannot alter it.
- Add tests proving generated explanations use the resolved semantic metric version instead of provider field names or prompt-invented definitions.
