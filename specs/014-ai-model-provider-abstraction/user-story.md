# User Story - AI Model Provider Abstraction

## Story

As a platform owner,  
I want LLM and embedding execution behind provider-neutral interfaces,  
so that AI workflows can use cloud providers or local models without coupling application behavior to a vendor SDK.

## Acceptance Criteria

- AI model access is a separate capability from third-party financial/market data access defined in `004-third-party-data-provider-abstraction`.
- Application and AI orchestration layers depend on provider-neutral contracts, not on OpenAI, Anthropic/Claude, Abravran, Ollama, or any other vendor SDK or HTTP schema.
- The design supports hosted providers such as OpenAI and Anthropic/Claude, a future Abravran hosted-provider adapter after its contract is obtained, and local model execution such as Ollama.
- A provider adapter can expose capabilities including chat completion, structured output, tool/function calling, streaming, embeddings, token/usage reporting, model availability, and health status.
- Each workflow requests required capabilities rather than selecting a provider by vendor name. Provider routing selects only a configured adapter that satisfies those capabilities and tenant/policy restrictions.
- The Scanner parser requires structured-output support or a validated fallback strategy so every generated `ScannerQueryPlan` remains schema-validated regardless of model provider.
- Microsoft Agent Framework agents/workflows are constructed over provider-neutral AI clients and tools. Provider SDK objects do not leak into scanner, explainability, billing, or public API DTOs.
- Hosted provider secrets, endpoints, models, tenant allow-lists, data-residency restrictions, and fallback policy are supplied through secured configuration.
- Local provider configuration supports endpoint/model selection and operational safeguards without assuming public-cloud billing data is available.
- AI provider execution emits normalized usage facts such as provider key, model key, input/output usage when supplied, duration, cache/tool/embedding indicators, status, and correlation identifiers.
- `FinancialCopilot.Billing` consumes normalized provider-cost and operation facts through its own pricing policies; provider adapters never debit wallets or calculate displayed credits.
- Provider failures, timeouts, unavailable capabilities, fallback attempts, and local-runtime unavailability are handled through explicit policy and are auditable.
- Automated tests can use a deterministic fake AI model provider without network calls or a running local model.

## Technical Notes

- Use SOLID boundaries with interfaces such as `IAiModelClient`, `IAiModelProviderResolver`, `IAiProviderCapabilityRegistry`, and `IAiExecutionTelemetrySink`.
- Keep prompt construction, schema validation, agent tool registration, and business workflow policy outside vendor adapters.
- Provider adapters translate normalized requests/results to vendor-specific protocols; they do not interpret financial meaning.
- Name the Abravran adapter as a contract-pending integration point only; do not implement vendor assumptions until official API/authentication/model documentation is available.
- Microsoft Agent Framework remains the workflow/agent orchestration model; AI-provider adapters supply the model client used by that orchestration.

