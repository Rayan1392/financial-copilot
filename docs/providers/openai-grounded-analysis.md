# OpenAI — grounded analysis (`gpt-6.2-sol`, web search)

Provider note for Features 162 and 163, change `sol-deep-analysis-web-search`
(`specs/162-context-aware-financial-report-follow-up/changes/sol-deep-analysis-web-search/`). The general provider setup
(keys, registrations, switching) is in `docs/ai-provider-switching.md`.

## Model

- Grounded analyses (Report Discussion and the confirmed Symbol-Grounded Fallback) call `gpt-6.2-sol` through
  `AiModelRequest.ModelKeyOverride`, only when `GroundedAnalysis:Enabled` is true. Every other workload keeps the
  OpenAI registration's model (`gpt-6-luna`).
- `GroundedAnalysis:ProviderKey` must name a registration with the `WebSearch` capability (only OpenAI has it). The
  DeepSeek, Abravran, Ollama and fake transports answer a web request with `CapabilityUnavailable`.
- Calls use the Responses API with a `json_schema` output (`ReportDiscussionAnalysis_v1`,
  `SymbolGroundedFallbackAnalysis_v1`). The 162 disposition step (answer / hand back / missing data) is a separate
  small call with no tools; `GroundedAnalysis:Disposition:Model` null means the workload's own model.

## Web search

Sent only when the user's web switch is on:

```json
"tools": [{ "type": "web_search", "search_context_size": "low", "user_location": { "type": "approximate", "country": "IR" } }],
"max_tool_calls": 1,
"include": ["web_search_call.action.sources"]
```

- `tool_choice` is not sent, so it stays at its default (auto): the model may search but need not.
- `max_tool_calls` is the profile's `MaxWebSearchCalls` (Simple 1, Deep 2), a hard per-response cap.
- `GroundedAnalysis:WebSearch:AllowedDomains` is empty (no domain filter).
- Usage records the number of `web_search_call` items, and the consulted source URLs are collected. The answer may cite
  only those URLs (`ExternalEvidenceValidator`); a number from an unconsulted, undated or mismatched source fails
  grounding.
- Search-content tokens have no exact cap. `search_context_size: low` limits them, and the cost alert watches them.

## Budgets (technical, independent of the price)

| `GroundedAnalysis:Profiles` | Simple | Deep |
|---|---|---|
| `ReasoningEffort` | low | medium |
| `MaxOutputTokens` (reasoning included) | 3,000 | 8,000 |
| `InputTokenTarget` | 6,000 | 10,000 |
| `MaxWebSearchCalls` | 1 | 2 |
| `TimeoutSeconds` | 45 | 90 |

- The OpenAI HTTP client timeout (`AiModelProviders:Providers[OpenAI]:TimeoutSeconds`, 120) stays above the profile
  timeouts.
- An answer cut at `MaxOutputTokens` counts as a failure and costs the user 0 credits.
- Credits come from `GroundedAnalysisPricing` and never from these values (see `docs/grounded-analysis-operations.md`).

## Cost monitoring

- `ModelPrices` has a `gpt-6.2-sol` row (input $2, cached input $0.10, output $10 per 1M tokens), and
  `GroundedAnalysis:Telemetry:WebSearchCallUsd` is $0.01 per search.
- The estimated USD per analysis is used only for monitoring; no billing path reads it.
- Above `GroundedAnalysis:Telemetry:CostAlertUsd` (0.20), a warning is logged and `grounded_analysis.cost_alerts` is
  incremented.

## Not yet verified

The provider spike (T00, `spike-notes.md` in the change folder) is partial. Its results so far, on 2026-11-20:

- Confirmed: `gpt-6.2-sol` is reachable with the development key.
- Confirmed: the lowest accepted `reasoning.effort` is `low` (`none` is rejected).
- Not yet verified, because every generating call returned 429 `credit_balance_exhausted`:
  - whether `web_search` works together with a strict `json_schema` output;
  - how `max_tool_calls` behaves, including the `open_page` and `find_in_page` actions;
  - the `reasoning_tokens` count and the sources field.

Complete these checks before enabling the flag in any environment that uses a real key.
