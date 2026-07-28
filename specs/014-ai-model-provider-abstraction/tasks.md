# Tasks

## Contracts

- Define provider-neutral AI request/result models for conversation messages, structured outputs, tool/function calls, streaming chunks, embeddings, and normalized execution usage facts.
- Define `IAiModelClient`, `IAiModelProviderResolver`, `IAiProviderCapabilityRegistry`, `IAiExecutionTelemetrySink`, and provider health contracts.
- Define required-capability requests for scanner parsing, explanation generation, suggested questions, summarization, embeddings, and future research tools.
- Define configuration models for provider aliases, hosted/local mode, endpoints, model aliases, secrets references, allowed capabilities, tenant routing policy, and fallback policy.

## Adapters

- Implement a deterministic fake AI model provider for tests.
- Implement hosted-provider adapters incrementally for selected contracted providers, beginning with the provider chosen for MVP deployment.
- Reserve an `Abravran` hosted-provider adapter/configuration type behind the common interfaces; implement it only after its official API contract is available.
- Implement an Ollama/local-model adapter behind the same interfaces where local execution is required.
- Implement health and capability discovery checks appropriate to configured hosted and local providers.

## Orchestration Integration

- Add an AI model provider resolver to the Microsoft Agent Framework setup so agents/workflows request capabilities and receive a compatible model client.
- Update `IScannerQueryParser` and answer-generation adapters to depend on normalized AI provider contracts, not vendor SDK types.
- Enforce strict schema validation and deterministic fallback when the selected model/provider cannot reliably produce required structured output.
- Preserve provider-selection, model-selection, fallback, and normalized execution facts as internal audit evidence correlated to the AI query execution.
- Emit provider-attempt telemetry compatible with `018-ai-observability-and-telemetry` without making model adapters responsible for end-to-end workflow tracing.

## Billing and Security

- Pass normalized operation/provider usage facts to `IUsageChargeCalculator` without allowing AI adapters to modify billing ledgers or user-facing credit values.
- Protect hosted-provider secrets and restrict logging of prompts/responses according to configured privacy policy.
- Add tenant/policy tests preventing a request from using a model provider or local runtime not enabled for that account/environment.

## Verification

- Add unit tests for capability resolution, adapter mapping, schema validation behavior, fallback routing, and normalized usage facts.
- Add integration tests for the AI facade using the deterministic fake provider.
- Add adapter contract tests for each implemented hosted/local provider.
- Add architecture tests preventing vendor SDK dependencies from leaking into Domain, Scanner, Billing, or public API contract assemblies.
- Provide execution metadata needed by `017-ai-evaluation-and-regression` to compare approved provider/workflow configurations in controlled evaluation runs.

## Implementation Status - 2026-05-27

Implemented in this story:

- Added provider-neutral Application request/result contracts for chat completion, structured output, tool calls, streaming, embeddings, health, routing requirements, and normalized execution usage facts.
- Added capability-based provider resolution enforcing enabled state, required capabilities, tenant allow-lists, optional data-residency matching, and local-runtime restrictions.
- Added execution coordination that validates tenant/workload/correlation evidence, schema-validates structured output, falls back to another compatible provider after invalid output or provider failure, and records per-attempt metadata.
- Added secured configuration models for hosted/local/fake registration, model keys, endpoints, credential secret references, capabilities, tenant policy, data residency, and prompt-logging restrictions.
- Added a deterministic fake model client for automated tests, an Ollama local adapter using documented chat/embedding/health APIs, a contracted hosted-transport adapter boundary, a disabled contract-pending Abravran boundary, and a metadata-only telemetry sink.
- Added unit and integration tests for capability routing, tenant/local policy enforcement, structured-output fallback, correlation safety, normalized usage facts, fake-provider DI execution, hosted-transport mapping, Ollama response mapping, and architecture isolation of vendor names from business/public-contract assemblies.

Explicitly deferred to dependent scope:

- `007-natural-language-scanner-parser` constructs Microsoft Agent Framework workflow/adapters over these provider-neutral clients and delivers public AI facade execution; the facade remains a protected placeholder in this story.
- `010-usage-metering-and-billing-readiness` converts normalized provider/operation facts into Billing charge requests and ledger outcomes.
- `018-ai-observability-and-telemetry` persists expanded workflow/provider/tool traces beyond the attempt-level sink established here.
- A concrete hosted OpenAI or Anthropic/Claude transport is activated only after the MVP hosted provider, authentication method, data policy, and approved contract are selected. Abravran remains contract-pending by requirement.
