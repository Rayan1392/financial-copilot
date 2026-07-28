# Tasks

## Task 1 — Design Provider-Agnostic LLM Contracts

Create provider-neutral contracts:

* ILLMProvider
* ILLMProviderFactory
* LLMRequest
* LLMResponse
* LLMUsage
* LLMToolCall

Acceptance:

* No provider-specific DTO leaks into Application layer.

---

## Task 2 — Refactor Existing OpenAI Provider

Move current OpenAI implementation behind the provider abstraction.

Acceptance:

* Existing functionality unchanged.

---

## Task 3 — Implement DeepSeek Provider

Create:

* DeepSeekProvider

Support:

* Chat Completion
* Tool Calling
* Structured Output
* Streaming

Documentation:

https://api-docs.deepseek.com

https://api-docs.deepseek.com/api/create-chat-completion

---

## Task 4 — Provider Factory

Implement configuration-driven provider resolution.

Supported values:

* OpenAI
* DeepSeek

Invalid values:

* ConfigurationException

---

## Task 5 — Configuration Support

Add:

```json
{
  "AiProvider": {
    "DefaultProvider": "OpenAI",

    "OpenAI": {
      "ApiKey": "",
      "Model": "gpt-5"
    },

    "DeepSeek": {
      "ApiKey": "",
      "Model": "deepseek-chat",
      "BaseUrl": "https://api.deepseek.com"
    }
  }
}
```

Create:

* AiProviderOptions
* OpenAiOptions
* DeepSeekOptions

---

## Task 6 — Dependency Injection

Register providers through DI.

Acceptance:

* Active provider selected by factory.

---

## Task 7 — Usage Metering Compatibility

Extend usage persistence:

* ProviderName
* ModelName
* PromptTokens
* CompletionTokens
* TotalTokens
* EstimatedCost

Ensure compatibility with story 010.

---

## Task 8 — Logging & Telemetry

Add diagnostics:

* Provider Name
* Model Name
* Duration
* Token Usage

Metrics:

* ai_requests_total
* ai_provider_requests_total
* ai_provider_failures_total

Ensure compatibility with story 018.

---

## Task 9 — Admin Diagnostics API

Add endpoint:

GET /api/admin/ai/provider

Response:

```json
{
  "provider":"DeepSeek",
  "model":"deepseek-chat"
}
```

---

## Task 10 — Integration Testing

Verify:

### OpenAI

* Query execution
* Tool calling
* Usage persistence

### DeepSeek

* Query execution
* Tool calling
* Usage persistence

### Provider Switching

* OpenAI → DeepSeek
* DeepSeek → OpenAI

without code changes.

---

## Task 11 — Microsoft Agent Framework Validation

Verify compatibility with:

* V1 Orchestrator
* Microsoft Agent Framework V2

Acceptance:

* Existing workflows unchanged.

---

## Task 12 — Documentation

Create:

docs/ai-provider-switching.md

Include:

* Architecture
* Configuration
* DeepSeek setup
* Troubleshooting
* Adding future providers
