# P/S gauge query is routed to SymbolLookup and fails structured parsing

Date: 2026-07-29  
Status: Fixed in the active V2 workflow

## Title

`گیج P/S شراز؟` enters the generic SymbolLookup parser and raises an invalid `SymbolLookupParseOutput` error instead of using the P/S visualization path.

## Observed behavior

```text
گیج P/S شراز؟
=> Structured output for 'SymbolLookupParseOutput' is invalid JSON.
```

Stack trace path:

```text
AiModelProviderServices.cs:192
LlmSymbolLookupParser.cs:124
SymbolLookupToolAdapter.cs:54
FinancialCopilotWorkflowDefinition.cs:500
```

## Expected behavior

The query must be recognized as an explicit P/S gauge visualization request, resolve `شراز`, invoke the persisted P/S visualization use case, and return the structured gauge/result contract. It must not call `LlmSymbolLookupParser`.

## Root cause

Feature 115's dedicated gauge route is absent from the active V2 workflow:

1. `FinancialCopilotWorkflowDefinition.cs:494-507` checks `IsDirectMetricLookupRequest` and calls `SymbolLookupToolAdapter.LookupAsync`.
2. `IsDirectMetricLookupRequest` delegates to the direct-metric registry at `FinancialCopilotWorkflowDefinition.cs:910-932`.
3. The direct metric terms include `p/s` and `ps` at `FinancialCopilotWorkflowDefinition.cs:973-982`.
4. `SymbolLookupToolAdapter.cs:46-56` invokes `LlmSymbolLookupParser`.
5. `LlmSymbolLookupParser.cs:114-125` requests and validates the `SymbolLookupParseOutput` structured response. The model output for a gauge command is not a valid lookup payload, so `AiModelProviderServices.cs:158-192` records the failure and rethrows it.

There is no `PsGaugeVisualization` value in `DetectedIntent` (`AiOrchestrationContracts.cs:8-22`), no gauge branch before direct metric lookup, and no active V2 caller for `IPsVisualizationExperienceUseCase`. The existing P/S visualization implementation is therefore unreachable from AI. This is a routing/precedence defect, not a malformed company symbol or provider-data error.

The fix adds the missing intent/routing branch before direct metric lookup, invokes the persisted visualization use case, carries the result through the V2 response and conversation payload, and leaves plain `P/S` point lookups on the existing SymbolLookup path.

## Why the error is misleading

The model-provider exception reports invalid JSON for `SymbolLookupParseOutput`, but the user did not ask for a generic metric lookup. The parser is being called because `p/s` is treated as a generic direct metric before a dedicated visualization intent exists.

## Acceptance criteria for the fix

- `گیج P/S شراز؟`, `گیج پی اس شراز`, `گیج نسبت قیمت به فروش شراز`, and English gauge variants route to `PsGaugeVisualization`.
- Explicit gauge/range/needle semantics take precedence over generic P/S point lookup.
- Plain `P/S شراز` remains the existing `PS_TTM` point lookup.
- P/S scanner thresholds and ComprehensiveAnalysis P/S topics remain unchanged.
- V1 and V2 use the same persisted visualization use case and produce equivalent structured facts.
- The gauge route performs zero CyclicalWaves HTTP calls.
- A malformed/empty visualization result is returned as a controlled unavailable/invalid state; it never reaches `SymbolLookupParseOutput`.
- Tests cover Persian spacing/ZWNJ, Arabic/Persian `ی` and `ک`, slash/space variants, punctuation, symbol-before/after metric, and feature-disabled behavior.

## Affected files

- `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs:494-507,910-982`
- `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Adapters/SymbolLookupToolAdapter.cs:46-64`
- `src/backend/FinancialCopilot.Application/Scanner/LlmSymbolLookupParser.cs:91-125`
- `src/backend/FinancialCopilot.Application/AI/ModelProviders/AiModelProviderServices.cs:158-192`
- `src/backend/FinancialCopilot.Application/AI/Orchestration/AiOrchestrationContracts.cs:8-22`
- `src/backend/FinancialCopilot.Application/FinancialData/Ingestion/PsVisualizationExperienceContracts.cs:63-65`
