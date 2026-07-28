# User Story — LLM Provider Switching & DeepSeek Support

## Story

As a platform administrator,

I want the AI platform to support both OpenAI and DeepSeek behind the existing provider abstraction,

so that I can switch between providers using configuration only and without changing business logic, orchestration workflows, billing behavior, or frontend integrations.

---

## Business Value

* Reduce dependency on a single AI vendor.
* Improve operational resilience.
* Allow cost optimization between providers.
* Enable model quality comparison.
* Prepare the platform for future providers such as Claude and Azure OpenAI.
* Align with the long-term Hybrid AI strategy.

---

## Acceptance Criteria

### Provider Selection

* Active provider is selected from configuration.
* No code changes are required.
* Application restart is sufficient.

Example:

```json
{
  "AiProvider": {
    "DefaultProvider": "OpenAI"
  }
}
```

or

```json
{
  "AiProvider": {
    "DefaultProvider": "DeepSeek"
  }
}
```

---

### DeepSeek Integration

* DeepSeek Chat models are supported.
* DeepSeek Reasoning models are supported.
* Structured output continues to work.
* Tool calling continues to work.
* Streaming responses continue to work.

---

### Backward Compatibility

The following must continue to function:

* POST /api/ai/v1/query
* Billing reservations/finalization
* Usage Metering
* Conversation Memory
* Explainable Results
* Missing Answer Feedback
* Scanner Execution
* Symbol Lookup
* Microsoft Agent Framework V1
* Microsoft Agent Framework V2

---

### Usage Tracking

Usage records must contain:

* Provider Name
* Model Name
* Prompt Tokens
* Completion Tokens
* Total Tokens
* Estimated Cost

---

### Diagnostics

Admin APIs must expose:

* Active Provider
* Active Model

for troubleshooting purposes.

---

### Security

* Provider API keys are loaded from configuration/secrets.
* Provider API keys never appear in logs.
* Sensitive provider payloads remain redacted.

---

## Out Of Scope

* Automatic provider failover.
* Multi-provider routing.
* Load balancing between providers.
* Provider A/B testing.
* Local LLM routing.

These may be introduced in future stories.
