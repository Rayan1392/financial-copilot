# User Story — Explainable Scanner Results

## Story

As an investor or SaaS API consumer,  
I want each AI answer produced by the Scanner Tool to explain why symbols matched,
so that I can trust and audit the output in my Conversation.

## Acceptance Criteria

- Each result includes matched condition details.
- Each condition includes actual value, threshold, period, and comparison basis.
- Each result includes Data Citations with source provider, report date, and last sync timestamp.
- Displayed financial metric evidence includes its resolved semantic definition and calculation-policy version where applicable.
- Each completed scanner answer includes an answer-level Confidence Score; per-result confidence is optional only when a documented policy calculates it separately.
- Each result includes warnings if data is stale or incomplete.
- Scanner answers include applied filter chips whose labels and values match the executed plan and identify inferred defaults.
- Stock-list answers expose a renderable table schema and rows, using default columns unless the user requested validated alternatives.
- Every table value that can vary by observation time includes enough Data Citation/freshness metadata to indicate whether latest price information is live or from the previous completed trading day.
- When the 10-column maximum omits requested or potentially useful metrics, the answer identifies the omission or prompts the user to narrow the requested table fields.
- Each completed scanner answer includes contextually relevant suggested follow-up questions derived from the user's previous question and the returned result set.
- Suggested follow-up questions are returned in the assistant Message payload so the React UI can render selectable suggestion chips below the answer.
- Scanner answers include the answer-level Confidence Score and display backend-produced usage metadata from Billing required by the current chat UI; explainability does not calculate charges or balances.
- The answer-level Confidence Score is calculated by a backend `IConfidenceScoreCalculator` from validated interpretation certainty, evidence completeness, source freshness, and execution warnings.
- Confidence output includes a policy/version identifier and a factor breakdown sufficient for audit and testing.
- The AI model must not invent, estimate, or overwrite the displayed Confidence Score.
- Explanation text is concise and not a buy/sell recommendation.
- The Explainable Answer is returned through `POST /api/ai/v1/query` for both our own UI and conversational external clients.

## Technical Notes

- Numeric explanations should be generated from deterministic result data.
- LLM may polish text but must not change numbers.
- Text generation and suggested-question generation use provider-neutral model interfaces from `014-ai-model-provider-abstraction`; model-provider changes must not alter deterministic evidence, confidence, table data, or Billing metadata.
- AI explanations consume semantic metric labels/definitions from `015-financial-semantic-layer`; they must not invent or silently redefine a financial metric.
- Implement confidence calculation behind SOLID Application-layer abstractions; policy implementations must be independently replaceable and unit testable.
- In the Microsoft Agent Framework orchestration, confidence calculation is a required deterministic workflow function/executor after result evidence exists and before the assistant Message is finalized. It is not an optional tool call selected by the LLM.
- A Microsoft Agent Framework adapter may expose the function result to answer generation, but the calculation service remains independent of the agent framework and AI provider.
- The displayed filter summary must not state that an inferred/default condition was explicitly provided by the user.
- The answer generator may write the table introduction but must render table schema and numeric row values from the backend table projection without modifying columns or values.
- Suggested follow-up questions must stay within supported AI facade capabilities and must not imply unavailable data or unsupported analysis.
- The assistant Message persists answer evidence needed for later Conversation retrieval.
- Usage metadata is attached to the response from `010`/`013` Billing integration and is not an output of `IExplainableAnswerBuilder`.
