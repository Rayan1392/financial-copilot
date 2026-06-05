# User Story - Microsoft Agent Framework Orchestration V2

## Story

As a platform owner,  
I want the existing AI query orchestration to be migrated to a Microsoft Agent Framework based V2 workflow,  
so that FinancialCopilot can use a production-grade agent/workflow model while preserving the existing public API, billing behavior, deterministic scanner execution, explainability, telemetry, and backward compatibility.

## Business Context

The current backend already has provider-neutral AI execution from `014-ai-model-provider-abstraction`, AI facade delivery from `007-natural-language-scanner-parser`, scanner execution from `008`, explainability from `009`, billing readiness from `010`, telemetry from `018`, conversation memory from `019`, and symbol point lookup from `045`.

This story must not replace those working services. It introduces a V2 orchestration layer that uses Microsoft Agent Framework for agent/workflow coordination while keeping existing Application services as the source of truth for deterministic business behavior.

## Acceptance Criteria

- `POST /api/ai/v1/query` remains the only public chat-query endpoint and remains backward compatible for existing frontend and external clients.
- Existing V1 orchestration can still run behind a feature flag or configuration switch while the V2 Microsoft Agent Framework orchestration is introduced.
- A new V2 orchestration implementation is added, for example `MicrosoftAgentFrameworkAiQueryOrchestrationService` or `FinancialCopilotAgentWorkflowRunner`, without deleting the existing V1 implementation in the same story.
- Microsoft Agent Framework is used for orchestration concepts: Agent for conversational interpretation/tool selection and Workflow for mandatory ordered backend steps.
- Microsoft Agent Framework dependencies are isolated to API/Infrastructure/composition/adapters or an AI orchestration infrastructure boundary; Domain, Billing, Scanner deterministic services, semantic layer, and public API DTOs must not depend on Microsoft Agent Framework types.
- Provider-neutral AI contracts from `014` remain authoritative for model execution. The V2 workflow must resolve model capability through `IAiModelProviderResolver` or equivalent provider-neutral services rather than hardcoding OpenAI, Ollama, Claude, Abravran, or any vendor SDK.
- Existing Application services remain the source of truth:
  - `IScannerQueryParser` / scanner plan validation
  - `IScannerExecutionService`
  - `ISymbolLookupParser`
  - `ISymbolMetricLookupService`
  - `IExplainableAnswerBuilder`
  - `IConfidenceScoreCalculator`
  - `IBillingFacadeHook` / Billing usage reservation and finalization
  - conversation/message persistence
  - memory context and disclosure policy
  - missing-answer feedback collection
- The V2 workflow must preserve the same response contract shape returned by the AI facade, including scanner table, symbol lookup table, explainable answer, usage metadata, memory disclosures, warnings, citations, confidence, and conversation id.
- Billing reservation must occur before billable model/tool work, and billing finalization/release must occur exactly once for success, clarification, validation failure, provider failure, cancellation, and unexpected exceptions.
- LLM output may suggest intent, tool, metric wording, explanation text, or follow-up questions, but it must never perform deterministic financial calculations, SQL execution, credit calculation, wallet mutation, confidence scoring, or final authorization.
- Scanner and SymbolLookup tool/function adapters must expose only narrow, validated input contracts and must call existing backend services; they must not expose raw database access, arbitrary SQL, or billing mutation to the LLM.
- V2 workflow telemetry must emit OpenTelemetry-compatible workflow, agent, tool/function, provider-attempt, billing, and persistence spans compatible with `018-ai-observability-and-telemetry`.
- V2 workflow must preserve provider-attempt metadata and normalized usage facts from `014` for billing, evaluation, and audit.
- Conversation memory from `019`, when enabled and permitted, must be injected through an approved context provider/prompt context step and returned as material-use disclosure when it affects the response.
- Existing AI evaluation/regression framework from `017` can run the same golden datasets against V1 and V2 orchestration configurations and compare results.
- A rollback path exists through configuration: operators can switch back to V1 orchestration without changing frontend code or public API routes.
- The implementation includes unit, integration, architecture, and regression tests proving backward compatibility and V2 behavior.

## Backward Compatibility Requirements

- Do not rename or remove `POST /api/ai/v1/query`.
- Do not introduce a public `/parse`, `/execute`, `/tool`, or provider-specific AI endpoint for frontend use.
- Do not change existing response DTO fields in a breaking way; new fields must be nullable/additive.
- Do not remove V1 orchestration until V2 has parity and explicit removal is approved in a future story.
- Existing deterministic fake provider tests must continue to pass without network calls.
- Existing billing ledger semantics and idempotency guarantees must remain unchanged.
- Existing frontend chat, conversation reload, usage display, scanner table, and symbol lookup rendering must continue to work.

## Technical Notes

- Preferred shape:
  - `IAiQueryOrchestrationService` remains the stable Application contract.
  - A configuration option such as `AiOrchestration:Mode = V1 | MicrosoftAgentFrameworkV2` selects the implementation.
  - `FinancialCopilotAgentWorkflowRunner` coordinates the Microsoft Agent Framework Agent/Workflow.
  - Tool adapters wrap existing Application services through narrow contracts.
  - Middleware handles correlation, telemetry, authorization context propagation, safety checks, and audit capture.
- Use workflows for mandatory ordered steps that must not be skipped:
  1. Resolve actor/tenant/conversation context.
  2. Retrieve permitted memory context.
  3. Reserve usage through Billing.
  4. Run agent intent/tool-selection step.
  5. Run validated parse/tool execution.
  6. Build explainable answer and deterministic confidence.
  7. Collect missing-answer feedback if applicable.
  8. Finalize/release billing.
  9. Persist messages and response evidence.
- Use an agent only where LLM reasoning is appropriate: intent/tool routing, natural-language interpretation, clarification wording, explanation prose, and follow-up suggestions.
- Use deterministic backend services for everything financial, billable, persistent, or security-sensitive.
- Keep prompts, schema validation, tool registration, workflow policy, and business rules outside provider adapters.
- A **package selection and proof-of-concept spike** must precede all production code. The official Microsoft Agent Framework NuGet packages available at implementation time are the required primary choice. `Microsoft.Extensions.AI` and `Microsoft.SemanticKernel` may be evaluated only as bridge or fallback options and must not substitute the official packages unless they are unavailable, unsupported, or demonstrably unsuitable; any fallback must be justified and documented. Document the chosen package name, exact version, and API surface assumptions in the tasks implementation notes before any infrastructure or adapter code is written. Gate the full implementation on spike validation against the deterministic fake provider.
- If the chosen Microsoft Agent Framework package API differs from the patterns described above, adapt the implementation to the official package version while preserving all architectural constraints.
