# Tasks

## Planning and Compatibility Boundary

- Audit the current `AiQueryOrchestrationService`, `LlmAiIntentDetector`, `LlmScannerQueryParser`, `LlmSymbolLookupParser`, `LlmScannerExplanationGenerator`, Billing hook, memory integration, missing-answer feedback integration, and AI facade response mapping.
- Identify every externally observable behavior of `POST /api/ai/v1/query` that must remain backward compatible.
- Add a configuration model such as `AiOrchestrationOptions` with mode values `V1` and `MicrosoftAgentFrameworkV2`; default to `V1` unless explicitly enabled.
- Register orchestration implementations through DI so `IAiQueryOrchestrationService` can resolve V1 or V2 without controller changes.
- Keep V1 implementation available and covered by existing tests.

## Microsoft Agent Framework Infrastructure

- Add the approved Microsoft Agent Framework NuGet packages to the appropriate project only; do not reference them from Domain, Billing, semantic metric, scanner deterministic execution, or public API contract assemblies.
- Create an orchestration boundary for Microsoft Agent Framework integration, for example:
  - `FinancialCopilotAgentWorkflowRunner`
  - `FinancialCopilotAgentFactory`
  - `FinancialCopilotWorkflowFactory`
  - `FinancialCopilotAgentToolRegistry`
  - `MicrosoftAgentFrameworkMiddlewareRegistration`
- Wire Microsoft Agent Framework model/client usage through the provider-neutral AI contracts from `014`; do not hardcode vendor SDK clients into the workflow.
- Add a model-client adapter/resolver bridge only if required by the selected Microsoft Agent Framework API surface; keep it internal to the orchestration infrastructure.
- Ensure deterministic fake AI provider can drive the V2 workflow in unit/integration tests without network calls.

## Agent and Workflow Design

- Define the V2 workflow as an explicit ordered pipeline where mandatory backend steps cannot be bypassed by the LLM.
- Use an Agent step for conversational interpretation and permitted tool selection.
- Use Workflow/function steps for:
  - context and conversation preparation,
  - memory retrieval and provider-safe prompt context,
  - billing reservation,
  - scanner plan parsing and validation,
  - symbol metric lookup parsing and validation,
  - scanner execution,
  - symbol lookup execution,
  - explainable answer assembly,
  - deterministic confidence scoring,
  - missing-answer feedback collection,
  - usage finalization/release,
  - message persistence.
- Ensure every workflow execution has correlation identifiers for request id, tenant id, actor id, conversation id, message id, billing reservation id, provider attempt id, and workflow version.
- Version the workflow, for example `maf-orchestration-v2.0`, and include the version in telemetry/evaluation metadata.

## Tool / Function Adapters

- Implement narrow tool/function adapters over existing Application services; suggested adapters:
  - `ScannerPlanToolAdapter`
  - `ScannerExecutionToolAdapter`
  - `SymbolLookupToolAdapter`
  - `ExplainableAnswerToolAdapter`
  - `MemoryContextToolAdapter`
  - `BillingReservationFunction`
  - `BillingFinalizationFunction`
  - `MessagePersistenceFunction`
  - `MissingAnswerFeedbackFunction`
- Tool adapters must accept typed, minimal input DTOs and return typed result DTOs.
- Tool adapters must not expose DbContext, SQL, arbitrary repository access, billing ledger mutation, wallet mutation, or unvalidated provider calls to the LLM.
- Tool adapters must validate all LLM-originated fields before calling deterministic services.
- Tool adapters must preserve existing parser validation, semantic alias resolution, ambiguity handling, and clarification behavior.
- Add guardrails so LLM-proposed tool names or arguments cannot invoke unregistered methods.

## Provider-Neutral AI Integration

- Ensure all LLM calls inside the V2 agent/workflow request capabilities rather than vendor names.
- Use existing `IAiModelProviderResolver`, `IAiProviderCapabilityRegistry`, `IAiModelClient`, and normalized usage facts from `014`.
- Preserve structured-output schema validation and fallback behavior for intent detection, scanner parsing, symbol lookup parsing, explanation generation, and suggested follow-up generation.
- Preserve provider-selection and fallback audit evidence in the V2 workflow telemetry.
- Ensure hosted/local provider tenant restrictions and data-residency policies continue to apply.

## Billing and Usage Accounting

- Keep Billing as the authoritative owner of pricing, reservation, finalization, ledger entries, wallet projection, entitlements, and user-facing credit values.
- Reserve usage before running billable agent/model/tool work.
- Finalize or release exactly once in all branches:
  - success,
  - clarification,
  - validation failure,
  - no answer / missing data,
  - provider failure,
  - cancellation,
  - unhandled exception.
- Pass normalized provider/operation facts to the existing billing integration without letting Microsoft Agent Framework middleware or tool adapters calculate displayed credits.
- Add idempotency/correlation safeguards so retrying an orchestration step cannot double-charge.

## Memory, Conversation, and Persistence

- Retrieve memory through existing `IMemoryContextProvider` and `IMemoryProtectionPolicy` only.
- Include only provider-prompt-eligible memory in the Agent prompt/context.
- Return memory material-use disclosures as before.
- Persist user and assistant messages with the same conversation ownership and reload semantics as V1.
- Store enough V2 workflow metadata in assistant message evidence/audit fields to support debugging and evaluation without breaking current response reloads.
- Ensure API clients and web users preserve current actor/tenant isolation.

## Observability, Audit, and Evaluation

- Emit workflow-level telemetry compatible with `IAiWorkflowTelemetrySink`.
- Emit tool/function execution telemetry with status, duration, input classification, output classification, and error category, but avoid sensitive prompt/response leakage.
- Emit provider-attempt telemetry from the existing provider-neutral AI execution path.
- Include `AiOrchestrationMode`, workflow version, selected agent/tool path, provider attempts, fallback status, billing reservation id, and response type in audit metadata.
- Extend `017-ai-evaluation-and-regression` configuration so golden datasets can run against both V1 and MicrosoftAgentFrameworkV2 modes.
- Add a comparison report or metadata fields sufficient to detect behavior drift between V1 and V2.

## Security and Guardrails

- Add architecture tests preventing Microsoft Agent Framework package references from leaking into:
  - `FinancialCopilot.Domain`,
  - `FinancialCopilot.Billing`,
  - deterministic scanner execution services,
  - semantic metric catalog/calculator services,
  - public API DTO assemblies/contracts.
- Ensure tool adapters enforce the existing authorization/tenant context; do not let an agent select tenant, actor, or billing account.
- Ensure no LLM output can produce SQL, raw expression trees, dynamic LINQ, direct EF predicates, billing ledger commands, or confidence score values.
- Preserve prompt/response privacy and redaction policy from `018`.
- Add cancellation and timeout behavior around workflow execution and provider calls.

## Testing

- Add unit tests for orchestration mode selection and DI registration.
- Add unit tests for each tool/function adapter validating allowed and rejected inputs.
- Add unit tests proving Billing finalization/release happens exactly once for success, clarification, validation failure, provider failure, cancellation, and unexpected exception.
- Add integration tests for `POST /api/ai/v1/query` in V2 mode using the deterministic fake provider:
  - scanner query,
  - symbol metric lookup query,
  - clarification query,
  - unknown/unsupported query,
  - provider fallback,
  - missing-answer feedback,
  - memory disclosure when enabled,
  - conversation reload.
- Add backward compatibility tests comparing V1 and V2 response contract shape for representative scanner and symbol lookup queries.
- Add architecture tests for Microsoft Agent Framework dependency isolation.
- Add evaluation/regression tests that run a small golden dataset against V2 mode and assert no deterministic financial/billing/confidence fields are generated by the LLM.
- Ensure existing V1 tests still pass.

## Documentation

- Update `architecture.md` to make Microsoft Agent Framework the required orchestration technology for V2 while preserving provider-neutral model execution and deterministic backend rules.
- Update README or backend operations docs with:
  - `AiOrchestration:Mode`,
  - rollback procedure from V2 to V1,
  - required package/configuration notes,
  - telemetry fields,
  - limitations and deferred removal of V1.
- Update implementation checklist completion evidence after verification.
- Document any Microsoft Agent Framework API-version assumptions in the story implementation notes.

## Out of Scope

- Removing V1 orchestration.
- Changing the public AI facade endpoint.
- Rewriting scanner execution, symbol lookup, semantic metric calculation, Billing, or data ingestion.
- Introducing public tool endpoints.
- Letting the LLM execute SQL or calculate financial/billing/confidence values.
- Implementing new autonomous research, portfolio agent, watchlist agent, or deep research features unless already covered by existing specs.
