# Feature 130 — Telegram Gateway Interactive Channel

## 1. Goal

A linked FinancialCopilot user can send a Persian financial question to the Telegram bot and receive the answer produced by the existing FinancialCopilot AI/query pipeline. `FinancialCopilot.TelegramGateway` runs on Linode only as the Telegram channel adapter; it neither interprets financial intent nor performs analysis.

## 2. Current State

`FinancialCopilot.TelegramGateway` is an existing .NET 10 ASP.NET Core project in `FinancialCopilot.sln`, not an empty shell. It combines a web host with `TelegramGatewayPollingWorker`. When `TelegramGatewaySettings.Enabled` is true, the worker optionally removes a webhook, long-polls Telegram through `TelegramApiClient.GetUpdatesAsync`, processes updates in `UpdateId` order, and persists the next offset in `OffsetFilePath`. It accepts text messages and callback queries, handles the existing `/start link_...` account-link confirmation, sends Telegram typing actions, and sends rendered text or PNG responses. `GatewayIdempotencyStore` persists completed response-part delivery outcomes in a local JSON file.

`PrimaryApiClient` currently sends updates over HTTP(S) to `POST api/v1/telegram/assistant/updates`, adding `X-Api-Key`. The same client calls the existing link-confirmation endpoint. There is no configured retry handler; failures from `EnsureSuccessStatusCode` escape to the polling loop, which retains the last persisted offset and retries the polling cycle after five seconds.

The primary API already exposes `TelegramAssistantController` at `POST /api/v1/telegram/assistant/updates`. It requires `AuthorizationPolicies.ApiClientOnly` and the authenticated-actor rate-limit policy. Its `TelegramAiAssistantAdapter` validates the update, resolves the Telegram identity through the existing account link, creates or reuses a `TelegramConversationBinding`, and—for ordinary free text—calls `IAiQueryOrchestrationService.ExecuteAsync` with the linked actor, tenant, conversation, and `ExternalUserId`. It therefore enters the same V1/V2 FinancialCopilot orchestration and tools used by the API facade. `TelegramAssistantResponseRenderer` converts the resulting `AiQueryResponse` into Telegram-sized rendered messages. `TelegramProcessedUpdates` stores processed results so a duplicate update replays the response without a second AI execution. Existing unit tests prove linked free-text routing, conversation binding, unlinked handling, and duplicate replay.

The general facade `POST /api/ai/v1/query` also exists, accepts `AiQueryHttpRequest`, returns `AiQueryHttpResponse`, and uses `CorrelationIdMiddleware`, `GlobalExceptionMiddleware`, conversation repositories, and `IAiQueryOrchestrationService`. Direct use by the gateway would, however, bypass the already-implemented Telegram identity, conversation binding, idempotency, and response-rendering behavior.

The gateway is disabled in its checked-in settings. Its local base URL is HTTP while enabled settings require an HTTPS primary URL. Both gateway and API settings currently contain credential-shaped configuration values; production must not rely on these values, and they must be removed/rotated as part of deployment preparation.

## 3. Scope

- Long-poll Telegram for ordinary text messages on the Linode gateway.
- Extract only update, user, chat, thread/message, locale, timestamp, and text identifiers already represented by `TelegramAssistantUpdateRequest`.
- Call the existing authenticated Telegram assistant endpoint at `https://api.sapioai.ir`.
- Let the backend resolve identity, retain conversation context, run its existing query pipeline, and render the response.
- Deliver every rendered response part to the originating Telegram chat.
- Preserve practical at-least-once handling across API, process, and Telegram failures.
- Add the minimum logging, correlation propagation, tests, configuration, and deployment checks required for this flow.

## 4. Non-Goals

- Proactive Telegram alerts, notification outbox, background monitoring, scheduled messages, portfolio alerts, or report-publication alerts.
- Telegram Mini Apps, Telegram Serverless, `.gram` domains, payments, subscriptions, or payment flows.
- Replication of Iran-hosted data to Linode.
- A separate Telegram AI agent or Telegram-specific financial analysis.
- Replacement or redesign of the existing FinancialCopilot query pipeline or its wider conversation model.
- Redis, RabbitMQ, a new event bus, distributed infrastructure, or a generic omnichannel/provider framework for this interactive flow.
- Speculative support for WhatsApp, Discord, Slack, mobile channels, or hypothetical scale.
- Redesign of existing account linking, commands, callbacks, proactive delivery endpoints, or billing capabilities.

## 5. Runtime Architecture

```text
Telegram user
  → Telegram Bot API
  ← long poll from FinancialCopilot.TelegramGateway on Linode
  → TelegramGatewayPollingWorker
  → HTTPS POST https://api.sapioai.ir/api/v1/telegram/assistant/updates
  → TelegramAssistantController
  → TelegramAiAssistantAdapter
  → existing IAiQueryOrchestrationService and FinancialCopilot tools/data
  → rendered TelegramAssistantResult
  → TelegramGatewayPollingWorker
  → Telegram Bot API sendMessage/sendPhoto
  → Telegram user
```

All application traffic is initiated from Linode: outbound to Telegram and outbound over HTTPS to `api.sapioai.ir`. The Iran deployment only receives the HTTPS API request and returns its response on that connection. Feature 130 requires no Iran-to-Linode connection and no public Telegram webhook ingress on Linode; polling remains the hosting model.

## 6. Integration Contract

Choose option B, but reuse the minimal channel-facing endpoint that already exists; do not create another endpoint. `POST /api/v1/telegram/assistant/updates` is necessary because it maps Telegram identity to a FinancialCopilot actor, maintains per-chat/thread conversation binding, deduplicates updates, and produces Telegram-ready message parts. Calling `/api/ai/v1/query` directly would duplicate or discard those concerns.

The request remains the existing `TelegramAssistantUpdateRequest`: Telegram update ID and kind; user/chat/thread/message identifiers; callback identifiers when applicable; text; locale; received timestamp; and correlation ID. For the scoped text flow, the gateway fills only the text-message fields. The response remains `TelegramAssistantResult`, particularly `Status`, `ConversationId`, ordered `Messages`, and `CorrelationId`. `Messages` already carry part number, text, parse mode, actions, and optional media. No FinancialCopilot analytical contract is copied into the gateway.

`PrimaryApiClient` should also send the request correlation value as `X-Correlation-Id`, not only in the JSON body, so HTTP logs, middleware, backend processing, and Telegram update logs share `telegram:{UpdateId}`.

## 7. Telegram Gateway Responsibilities

The gateway owns Telegram polling, Telegram DTO deserialization, extraction of channel identifiers, the typing indicator, calls to the primary API, ordered delivery of rendered parts, offset and send-part persistence, Telegram/API timeout handling, and channel-safe operational logs. Existing `TelegramApiClient`, `PrimaryApiClient`, `TelegramGatewayPollingWorker`, and `GatewayIdempotencyStore` remain the concrete components; no provider interfaces or channel framework are needed.

It must not classify intent, resolve companies, select tools/models, calculate metrics, query financial storage, create financial prose, or alter backend conclusions. It must not inspect `AiResponse` to make business decisions. Identity resolution, authorization as the linked actor, conversation creation, idempotent AI execution, orchestration, billing/usage effects, and Telegram rendering remain in the Iran-hosted backend.

The existing HMAC-authenticated `TelegramGatewayController` supports backend-to-gateway operations used by other features. It is not on the interactive path and must not be expanded by Feature 130.

## 8. Authentication

Reuse the existing API-key scheme. Provision one dedicated Telegram gateway API client with stable client/tenant GUIDs and `AllowedPathPrefixes` limited to `/api/v1/telegram/assistant/updates` and, while account linking remains enabled, `/api/v1/telegram/link/confirm`. Linode sends the raw key in `X-Api-Key` only over HTTPS. In API configuration, `KeyEnvironmentVariable` must contain the name of the environment variable that holds the raw key, not the raw key itself; alternatively configure only `KeySha256`. The gateway loads `TelegramGateway:PrimaryApiKey` from a Linode secret/environment variable.

The checked-in credential-shaped values must not be used. Rotate them before production and remove secrets from tracked configuration, retaining only empty/example settings. Never log the bot token, API key, authorization headers, or full Telegram payload. HMAC `ServiceId`/`ServiceSecret` protect the separate inbound gateway controller and are not required by the interactive network direction; current startup validation couples them to polling and should be separated so polling-only deployment does not imply Iran-to-Linode connectivity.

## 9. Error Handling

- **Malformed/unsupported update:** validate required text-message identifiers, log the `UpdateId` and reason without message text, skip safely, and advance past an update that cannot be answered. A malformed update must not crash or permanently poison polling.
- **Primary API timeout, network error, 429, or 5xx:** do not persist the next Telegram offset. Log correlation and status, then retry through the existing polling loop with a small bounded backoff. If the backend completed but its response was lost, `TelegramProcessedUpdates` replays the stored result instead of executing AI again.
- **Primary API 2xx response:** treat the returned `TelegramAssistantResult` as the backend's completed, persisted outcome and deliver its rendered parts. This includes a result whose application status is `TransientError`; retrying that update would only replay the same persisted response.
- **Unauthorized/forbidden primary request:** log as a configuration/security error and expose unhealthy readiness. If the chat is known, send a short generic temporary-unavailability message; treat the update as terminal so one bad credential does not create an infinite poison loop. Do not echo backend details.
- **Telegram send timeout, 429, or 5xx:** a response part is incomplete. Do not advance the update offset; retry the update. Backend replay plus `GatewayIdempotencyStore` avoids repeat AI work and skips parts already confirmed as sent.
- **Permanent Telegram rejection:** log the redacted Telegram error and update/chat identifiers, then advance to avoid blocking all subsequent updates.
- **Duplicate/crash window:** retain at-least-once semantics. Backend database idempotency protects AI execution and gateway part keys protect confirmed sends, but a crash after Telegram accepts a message and before local persistence can produce a duplicate. Eliminating that narrow window requires distributed delivery machinery and is outside this MVP.

The current worker advances the offset even when `SendMessagesAsync` receives a transient unsuccessful result. The implementation must make response delivery report success/transient/permanent outcome and advance the offset only under the rules above.

## 10. Configuration

Required production settings are `Enabled`, `BotToken`, `PrimaryApiBaseUrl=https://api.sapioai.ir`, `PrimaryApiKey`, request and long-poll timeouts, polling interval/limit, and durable absolute paths for `OffsetFilePath` and `IdempotencyFilePath`. The API requires the matching dedicated API-client identity, tenant, secret source/hash, active flag, and narrow path prefixes. Secrets come from the Linode/API deployment secret mechanism, never source control. Existing rate-limit and health settings may be retained; no broker/cache/database is added on Linode.

## 11. Deployment Boundary

Deploy `FinancialCopilot.TelegramGateway` as a single continuously supervised process on Linode with outbound HTTPS/DNS access to Telegram and `api.sapioai.ir`, a writable persistent directory for offset/idempotency files, automatic restart, and `/health` available to local monitoring. Deploy the FinancialCopilot API, orchestration, PostgreSQL data, identity links, conversations, and financial services in the existing Iran environment. No Iran-to-Linode route is required. Restrict or disable external access to the gateway’s unrelated `/v1/gateway/telegram/*` endpoints unless another deployed feature explicitly depends on them.

## 12. Testing Strategy

- Unit-test gateway mapping from a representative Persian Telegram text update to the existing `TelegramAssistantUpdateRequest`, including chat/update identifiers, locale, and correlation header; also test malformed input.
- Unit-test polling completion rules: API timeout/5xx and transient Telegram send failure retain the offset; successful ordered multipart delivery advances it; confirmed parts are skipped on replay; permanent failures do not poison later updates.
- Extend API integration coverage to call `/api/v1/telegram/assistant/updates` with the dedicated API key and a linked Telegram actor, assert the orchestration path is invoked and rendered messages/conversation ID return, assert duplicate update replay, and assert missing/invalid credentials return 401/403.
- Run one bounded staging smoke test using a real bot: Persian question → Linode gateway → `api.sapioai.ir` → Telegram response, then repeat with a simulated API outage. Do not build a new E2E framework.

## 13. Implementation Slices

1. **Authenticated backend path:** production-safe secret configuration, dedicated path-scoped API key, HTTPS base URL, correlation-header propagation, and a linked-user API integration test.
2. **Interactive Telegram path:** retain long polling and existing clients; verify minimal inbound mapping, ordered rendered-message delivery, durable offset/part state, and a real Persian-message staging smoke test.
3. **Failure-safe deployment:** correct offset advancement for transient/permanent outcomes, add bounded tests and health/logging signals, separate polling validation from unrelated inbound-controller credentials, and verify supervised single-instance Linode deployment.

## 14. Open Questions

None for Feature 130 implementation.

## 15. Design Decisions

| Decision | Choice | Reason |
|---|---|---|
| Backend integration | Reuse `POST /api/v1/telegram/assistant/updates` | It already supplies identity, conversation, deduplication, orchestration, and Telegram rendering. |
| AI ownership | Existing `IAiQueryOrchestrationService` in the primary API | Prevents a second agent and preserves all financial logic in FinancialCopilot. |
| Telegram ingress | Existing `getUpdates` long polling from Linode | Requires outbound connectivity only and no Telegram access from Iran. |
| Service authentication | Dedicated, path-scoped `X-Api-Key` over HTTPS | Reuses current security with the smallest privilege boundary. |
| Delivery semantics | Single-instance, persistent offset/part state, at-least-once retry | Meets MVP reliability without a broker or distributed subsystem. |
| New production components | None | Existing gateway clients, worker, API controller, adapter, renderer, and stores cover the flow. |

DESIGN_APPROVED_FOR_STORY
