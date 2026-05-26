# User Story — Natural Language Scanner Parser

## Story

As an investor,  
I want to ask screening questions through the AI chat experience,
so that I do not need to manually build complex financial filters or select a tool.

## Acceptance Criteria

- `POST /api/ai/v1/query` accepts Persian and English user Messages.
- Generic Conversation history is available through `/api/ai/v1/conversations` and Message retrieval endpoints.
- AI Query Orchestrator performs Intent Detection and selects the Scanner Tool for screening requests.
- Internal parser service returns a structured scanner query plan after Scanner Tool selection.
- Supported metrics are mapped from synonyms.
- Unsupported metrics are rejected or marked for clarification.
- Ambiguous periods return clarification suggestions.
- Generated plan contains no SQL.
- Plan is validated before execution.
- Parser can run with mock LLM in tests.
- The React UI does not call scanner parser services directly.

## Technical Notes

- LLM output must conform to strict JSON schema.
- Use backend validation after LLM output.
- Persist original question and interpreted plan.
- Associate the internal scanner plan with its Conversation Message execution.
