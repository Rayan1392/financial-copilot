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
