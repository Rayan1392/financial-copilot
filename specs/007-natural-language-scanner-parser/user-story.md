# User Story — Natural Language Scanner Parser

## Story

As an investor,  
I want to ask screening questions through the AI chat experience,
so that I do not need to manually build complex financial filters or select a tool.

## Acceptance Criteria

- `POST /api/ai/v1/query` accepts Persian and English user Messages.
- Generic Conversation history is available through `/api/ai/v1/conversations` and Message retrieval endpoints as part of the AI facade capability, not as scanner-specific history.
- AI Query Orchestrator performs Intent Detection and selects the Scanner Tool for screening requests.
- Internal parser service returns a structured scanner query plan after Scanner Tool selection.
- Supported metrics are mapped from synonyms.
- Unsupported metrics are rejected or marked for clarification.
- Ambiguous periods return clarification suggestions.
- Ambiguous Persian or English phrases such as "high growth" either use a documented configurable default or return a clarification request.
- The parsed plan distinguishes conditions explicitly requested by the user from conditions inferred through a documented default policy.
- The parser must not silently add screening conditions, such as a market capitalization threshold, that were not explicitly requested or resolved through policy.
- The parser extracts explicit user requests to add, remove, or reorder table columns separately from screening conditions.
- A user column request that exceeds the supported maximum of 10 displayed data columns is validated and either reduced with an explicit warning or returned for clarification.
- Generated plan contains no SQL.
- Plan is validated before execution.
- Parser can run with mock LLM in tests.
- Parser invokes LLM execution through provider-neutral interfaces defined by `014-ai-model-provider-abstraction`, supporting configured hosted or local models without vendor-specific parser logic.
- The React UI does not call scanner parser services directly.

## Technical Notes

- LLM output must conform to strict JSON schema.
- Structured-output and tool/function-calling capabilities are requested from the selected AI model provider; if a configured provider lacks required capability, the orchestrator uses an approved fallback or fails safely.
- Use backend validation after LLM output.
- Persist original question and interpreted plan.
- Persist the origin and explanation of every inferred/default filter so the UI can display it accurately.
- Keep requested presentation columns separate from executable financial filters so display selection cannot silently change the result universe.
- Associate the internal scanner plan with its Conversation Message execution.
- The AI Query Orchestrator integrates with Billing reservation/finalization workflows from `010`/`013`; the parser does not calculate charges.
- The parser and orchestrator never access OpenAI, Anthropic/Claude, Abravran, Ollama, or another provider SDK directly; provider-specific translation remains in Infrastructure/AI adapters.
