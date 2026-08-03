# AI Provider Switching

Financial Copilot routes LLM calls through the provider-neutral `IAiModelClient` abstraction. Application workflows request capabilities such as chat completion, structured output, tool calling, streaming, and usage reporting; they do not call OpenAI or DeepSeek APIs directly.

## Configuration

Use `AiProvider:DefaultProvider` to select the active hosted LLM provider. Restart the API after changing the value.

```json
{
  "AiProvider": {
    "DefaultProvider": "OpenAI",
    "OpenAI": {
      "ApiKey": "",
      "Model": "gpt-5.4"
    },
    "DeepSeek": {
      "ApiKey": "",
      "Model": "deepseek-chat",
      "BaseUrl": "https://api.deepseek.com",
      "ThinkingEnabled": false,
      "ReasoningEffort": null
    }
  }
}
```

The existing `AiModelProviders` list is still supported for advanced registrations. When `AiProvider:DefaultProvider` is set, provider resolution is filtered to that provider key.

## Secrets

Prefer environment variables or secret stores over checked-in settings:

```powershell
$env:AiProvider__OpenAI__ApiKey = "<openai-key>"
$env:AiProvider__DeepSeek__ApiKey = "<deepseek-key>"
```

For Docker Compose, copy `.env.example` to the ignored `.env` file and set:

```dotenv
AI_PROVIDER_DEFAULT_PROVIDER=OpenAI
OPENAI_API_KEY=<openai-key>
ABRAVRAN_ENABLED=false
```

For `AiModelProviders`, set `CredentialSecretReference` to the environment variable name, such as `OPENAI_API_KEY` or `DEEPSEEK_API_KEY`. For the `AiProvider` section, leave `ApiKey` empty in committed files and override it locally or in deployment configuration.

## DeepSeek

The DeepSeek adapter uses the Chat Completions API at:

```text
POST https://api.deepseek.com/chat/completions
```

Supported normalized capabilities:

- Chat completion
- JSON structured output through `response_format: { "type": "json_object" }`
- Function tool calling
- Streaming through the existing single-completion streaming adapter contract
- Usage reporting from prompt and completion token fields

Set `ThinkingEnabled` for reasoning-capable DeepSeek deployments. `ReasoningEffort` is optional and provider-specific.

## Diagnostics

Data admins can inspect the selected provider:

```http
GET /api/admin/ai/provider
GET /api/v1/admin/ai/provider
```

The response includes configured provider, resolved provider, model, capabilities, and availability. It never returns API keys or prompt payloads.

## Usage Accounting

AI query finalization stores provider usage facts on `billing_usage_ledger_entries`:

- `ProviderName`
- `ModelName`
- `PromptTokens`
- `CompletionTokens`
- `TotalTokens`
- `EstimatedCost`

Billing credits remain calculated by Financial Copilot pricing policy. Provider-reported cost is persisted for diagnostics and reconciliation, not as the source of billing truth.

## Troubleshooting

- `hosted_provider_credentials_missing`: configure the provider API key through secrets or environment variables.
- `compatible_provider_not_configured`: ensure `AiProvider:DefaultProvider` matches an enabled provider with the requested capabilities.
- `hosted_provider_authentication_failed`: verify the provider key and account status.
- `hosted_provider_rate_limited`: retry later or review provider-side rate limits.

## Adding Future Providers

Add a new `IHostedAiModelTransport` or `IAiModelClient` implementation, map provider DTOs into `AiModelRequest`, `AiModelResult`, `AiToolCall`, and `AiExecutionUsageFacts`, then register it through DI with an `AiModelProviderDescriptor`. Do not introduce provider SDK types into Application, Billing, Scanner, or orchestration contracts.
