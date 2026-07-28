# Persian Chat Direction And Message Order

## Date

2026-06-02

## Status

Resolved.

## User-Visible Failure

The owned web chat displayed Persian conversations incorrectly:

1. Persian questions could receive an English fallback response.
2. User questions appeared on the left instead of the right.
3. AI responses appeared on the right instead of the left.
4. On conversation reload, an AI response could appear before the user question that triggered it.

## Root Cause

The chat page uses an RTL document layout. The user bubble relied on:

```text
flex justify-end
```

In an RTL flex container, the end edge is the visual left edge, so the user bubble was placed on
the wrong side. The assistant block also inherited RTL flex direction and appeared on the right.

The conversation persistence layer stored each user question and its assistant response with the
same timestamp. History reads sorted only by `CreatedAt`, which did not guarantee that the user
message would be returned before the assistant message.

The orchestration fallback strings were hard-coded in English. The scanner explanation prompt also
did not require generated explanations and suggested questions to use the user's original language.

## Resolution

Updated:

```text
src/frontend/src/components/app/message-list.tsx
src/backend/FinancialCopilot.Infrastructure/Conversations/Persistence/ConversationRepositories.cs
src/backend/FinancialCopilot.Application/AI/Orchestration/AiQueryOrchestrationService.cs
src/backend/FinancialCopilot.Application/Scanner/LlmScannerExplanationGenerator.cs
```

The implementation now:

1. Uses LTR flex positioning wrappers so user bubbles stay on the right and AI blocks stay on the
   left independently of the document RTL direction.
2. Uses `dir="auto"` for message content so Persian and English text retain natural reading
   direction.
3. Adds a deterministic secondary sort that returns `User` before `Assistant` when message
   timestamps match.
4. Returns Persian clarification and unknown-intent fallback text when the user question contains
   Persian characters.
5. Returns Persian deterministic scanner summaries when the parsed scanner language starts with
   `fa`.
6. Instructs generated scanner explanations to respond in the same language as the original query.

## Verification

```powershell
npm run build
dotnet test src/backend/FinancialCopilot.sln --configuration Release --no-restore
curl.exe --silent --show-error --head --max-time 5 http://localhost:8080
```

Passed:

- Frontend production build.
- Unit tests: `303`.
- Integration tests: `210`.
- Architecture tests: `3`.
- Local frontend returned HTTP `200`.

