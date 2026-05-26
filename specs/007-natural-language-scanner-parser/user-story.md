# User Story — Natural Language Scanner Parser

## Story

As an investor,  
I want to ask scanner questions in natural language,  
so that I do not need to manually build complex financial filters.

## Acceptance Criteria

- API accepts Persian and English questions.
- Parser returns a structured scanner query plan.
- Supported metrics are mapped from synonyms.
- Unsupported metrics are rejected or marked for clarification.
- Ambiguous periods return clarification suggestions.
- Generated plan contains no SQL.
- Plan is validated before execution.
- Parser can run with mock LLM in tests.

## Technical Notes

- LLM output must conform to strict JSON schema.
- Use backend validation after LLM output.
- Persist original question and interpreted plan.
