# Feature 133 Telegram Boundary Audit

## 1. Executive Summary

The intended Feature 130/133 architecture is present for the primary Telegram path: `FinancialCopilot.TelegramGateway` owns Telegram polling and outbound Bot API calls, then calls `FinancialCopilot.API` over an authenticated internal HTTPS API. Feature 133 is handled inside the API and returns rendered messages to the gateway for delivery.

The repository is not boundary-clean. `FinancialCopilot.Worker` contains a separate direct Telegram polling/sending implementation (`TelegramDevPollingWorker`) and the Iran deployment compose file injects Telegram bot-token variables into the shared API/Worker environment. The direct worker is not registered as a hosted service in the current `Worker/Program.cs`, so this is a latent/deployment-risk path rather than evidence that it is currently running. The API source itself has no direct Telegram HTTP client or Bot API call.

This is a repository and deployment-configuration audit only; live Linode, Telegram, firewall, and secret-store state was not available for verification.

## 2. Current Architecture Diagram

Intended and implemented Feature 133 path:

```text
Telegram Bot API
        |
        | getUpdates / sendMessage / sendPhoto / membership calls
        v
FinancialCopilot.TelegramGateway  (Linode deployment)
        |
        | authenticated HTTPS, X-Api-Key
        v
FinancialCopilot.API  (Iran deployment)
        |
        | TelegramAssistantController
        v
TelegramChannelMonthlyReportHandler
        |
        v
Application / Domain / persisted financial data
```

Response delivery is `API -> gateway response -> gateway -> Telegram Bot API -> channel`.

Boundary risk found outside this path:

```text
FinancialCopilot.Worker (Iran deployment image)
        |
        | latent TelegramDevPollingWorker direct HTTPS calls
        v
api.telegram.org
```

## 3. FinancialCopilot.TelegramGateway Findings

- Project: `src/backend/FinancialCopilot.TelegramGateway`.
- Entry point: `Program.cs`; registers `TelegramGatewayPollingWorker` as a hosted service.
- Hosted service: `TelegramGatewayPollingWorker`.
- Telegram client: `TelegramApiClient`, using raw `HttpClient`; no Telegram NuGet SDK package is declared in the project file.
- Polling: `getUpdates` long polling with `message`, `callback_query`, and `channel_post`; `deleteWebhook` is optionally called on startup.
- Outbound operations: `sendMessage`, `sendPhoto`, `sendChatAction`, `answerCallbackQuery`, and `getChatMember`.
- API client: `PrimaryApiClient`, which posts updates to `/api/v1/telegram/assistant/updates` and link confirmations to `/api/v1/telegram/link/confirm` using the configured API key.
- Durable gateway state: persisted offset and idempotency paths.
- Configuration: `TelegramGateway:BotToken`, primary API URL/key, service identity, timeouts, polling settings, and durable paths.

These responsibilities and dependencies are correctly located in the gateway. The checked-in gateway compose file supplies the bot token only to the gateway container and keeps its published port bound to loopback.

## 4. FinancialCopilot.API Findings

### Direct Telegram calls

No direct API-source use was found for `api.telegram.org`, `Telegram.Bot`, `TelegramBotClient`, `getUpdates`, `sendMessage`, `sendPhoto`, or Telegram `HttpClient` construction under `FinancialCopilot.API`.

The API uses `ITelegramGatewayClient` / `TelegramGatewayClient`, whose endpoints are internal gateway routes such as `v1/gateway/telegram/send-message` and `v1/gateway/telegram/get-chat-member`. This is an internal gateway dependency, not a direct Telegram Bot API dependency.

### Feature 133

`TelegramAssistantController` accepts channel posts only through the authenticated API-client policy and dispatches them to `TelegramChannelMonthlyReportHandler`. The handler owns recognition, allowlisting, idempotency, backfill/readiness, AI orchestration, and rendering. It does not call Telegram directly.

### API configuration

The API has the expected `Telegram:Gateway` client configuration and `ChannelMonthlyReports` business-orchestration configuration. It also contains an empty `Telegram:Notifications` section and the infrastructure model `TelegramNotificationOptions` includes `BotToken` and `BaseUrl`; the active notification transport uses `ITelegramGatewayClient`, and no registration was found that binds `TelegramNotificationOptions` for direct delivery.

## 5. Configuration Audit

| Configuration | Observed location | Classification |
|---|---|---|
| Telegram bot token for active gateway | `docker/telegram-gateway.compose.yml` -> `TelegramGateway__BotToken` from `TELEGRAM_BOT_TOKEN` | Correct location for the gateway; live secret value is not present in the repository |
| Telegram API URL | Gateway client constructs `https://api.telegram.org/bot{token}/` in `TelegramApiClient` | Correct gateway responsibility |
| Telegram chat/channel identifiers | API `Telegram:Membership:RequiredChannelId`; Feature 133 `ChannelMonthlyReports:AllowedChannelIds` | Correct as business/channel policy; not Bot API credentials |
| Gateway credentials | Gateway compose `TelegramGateway__PrimaryApiKey`, `ServiceId`, `ServiceSecret`; API `Telegram:Gateway` client settings | Correct split in principle; live secret alignment was not verified |
| `ChannelMonthlyReports` settings | `FinancialCopilot.API/appsettings.json`, bound in Infrastructure and consumed by `TelegramChannelMonthlyReportHandler` | Correct location; business orchestration settings |
| `TELEGRAM_BOT_TOKEN` in main compose API environment | `docker-compose.yml` line 98 | Architecture violation: bot secret is injected into the shared Iran-side API/Worker environment even though API code should not need it |
| `Telegram__DevPolling__BotToken` in main compose | `docker-compose.yml` line 176 | Architecture violation/risk: direct-poller secret is available to the Iran-side Worker environment |
| `Telegram__Notifications__BotToken` in main compose | `docker-compose.yml` line 177 | Architecture violation/risk: direct Telegram credential is offered to the shared Iran-side application environment; active transport should use the gateway |

Feature 133 settings found in API configuration:

```text
ChannelMonthlyReports:AutoBackfillEnabled
ChannelMonthlyReports:AutoPublishTrendEnabled
ChannelMonthlyReports:AllowedChannelIds
ChannelMonthlyReports:MaximumTextLength
ChannelMonthlyReports:ProcessingTimeoutSeconds
```

They contain no Telegram token, Telegram HTTP endpoint, or Telegram client settings. Their owner is `FinancialCopilot.API`, as required.

## 6. Deployment Audit

- `docker/telegram-gateway.compose.yml` defines a separate `telegram-gateway` container with the Bot API token, gateway image, persistent offset/idempotency state, restart policy, and health check.
- The gateway deployment documentation describes Linode/VPS 2 operation and outbound access to Telegram plus the Iran API.
- `docker-compose.yml` defines `api`, `worker`, and `frontend` for the Iran-side deployment. Its YAML anchor shares a broad environment block with API and Worker; that block includes Telegram token variables.
- `FinancialCopilot.Worker` is built into an Iran-side worker image. `TelegramDevPollingWorker.cs` directly creates a client with `https://api.telegram.org/bot{BotToken}/`, polls `getUpdates`, and sends Telegram messages/photos.
- `TelegramDevPollingWorker` is not registered by the current `FinancialCopilot.Worker/Program.cs`; only membership revalidation and other workers are registered. Therefore activation was not demonstrated, but the code and secret injection remain in the deployed service boundary.
- No Telegram SDK package was found in the gateway or API project files; Telegram access is implemented through raw HTTP in the gateway and the latent Worker poller.
- Live container environment, process list, firewall rules, and outbound network policy were not available, so runtime isolation cannot be conclusively proven from this repository alone.

## 7. Feature 133 Flow Verification

### Trigger path

Verified correct:

```text
Telegram channel post
  -> TelegramGatewayPollingWorker
  -> PrimaryApiClient.HandleUpdateAsync
  -> POST /api/v1/telegram/assistant/updates
  -> TelegramAssistantController
  -> TelegramChannelMonthlyReportHandler
```

The gateway normalizes `channel_post`, sends `TelegramUserId = 0` and `ChatType = channel`, and authenticates the API request with the configured API key. The API rejects malformed channel identity and applies the Feature 133 handler.

### Response path

Verified correct:

```text
TelegramChannelMonthlyReportHandler
  -> TelegramAssistantResult with rendered messages
  -> PrimaryApiClient response
  -> TelegramGatewayPollingWorker
  -> TelegramApiClient
  -> Telegram Bot API
  -> originating channel
```

The API handler does not call `api.telegram.org` directly.

## 8. Direct Telegram Dependency Findings

### Finding 1

Severity: Major

Location: `src/backend/FinancialCopilot.Worker/TelegramDevPollingWorker.cs` (direct source dependency); `src/backend/FinancialCopilot.Worker/TelegramDevPollingOptions.cs`; `docker-compose.yml` Telegram DevPolling environment entries.

Evidence: the worker constructs `https://api.telegram.org/bot{settings.BotToken}/`, calls `getUpdates`, `deleteWebhook`, `sendMessage`, and `sendPhoto`. The Worker project is built and deployed by `docker/worker.Dockerfile`, while the current Worker program does not register this class.

Impact: the Iran-side Worker image contains a second Telegram integration and can be activated with a configuration change, violating the single Linode gateway boundary and risking duplicate polling/delivery.

Required correction: remove or relocate the direct development poller from the Iran-deployed Worker boundary; if retained for local development, place it in a separately scoped development-only project/profile that cannot receive production Telegram secrets. Remove its production compose secret injection.

Estimated effort: medium refactor; approximately 1–3 engineering days including deployment/configuration and regression verification.

### Finding 2

Severity: Major

Location: `docker-compose.yml` shared `application-environment`, especially `TELEGRAM_BOT_TOKEN`, `Telegram__DevPolling__BotToken`, and `Telegram__Notifications__BotToken`.

Evidence: the API/Worker deployment environment exposes Telegram credential variables even though the active API notification transport calls `ITelegramGatewayClient`, and the active API membership provider calls the gateway for Telegram membership.

Impact: Telegram secrets are unnecessarily present on Iran-side services and can be consumed by latent or future direct clients; this weakens secret isolation and obscures the intended boundary.

Required correction: inject the bot token only into the Linode gateway deployment. Remove direct Telegram token variables from the API/Worker compose environment and remove/retire unused direct-notification configuration after confirming no operational consumer.

Estimated effort: configuration-only change if no live consumer exists; otherwise small refactor. Allow 0.5–1 day for deployment and secret-rotation verification.

### Finding 3

Severity: Minor

Location: `src/backend/FinancialCopilot.Infrastructure/Notifications/NotificationBoundaries.cs` (`TelegramNotificationOptions`) and the empty API `Telegram:Notifications` configuration section.

Evidence: the model still advertises a direct `BotToken` and `https://api.telegram.org` base URL, while `TelegramNotificationTransport` delegates to `ITelegramGatewayClient`.

Impact: stale configuration contracts can reintroduce a direct Telegram implementation or mislead operators about the owning service.

Required correction: remove the unused direct options contract/section, or explicitly document and enforce that only gateway client settings are supported.

Estimated effort: small refactor, approximately 0.5–1 day plus tests.

## 9. Security Assessment

- The active gateway Bot API token is correctly modeled as gateway-owned and supplied through gateway deployment configuration.
- The API does not need the Bot API token for Feature 133 or membership checks; it calls the gateway instead.
- The gateway-to-API path uses an API key and HTTPS configuration. The checked-in values are placeholders/empty; live credential correctness and rotation were not verified.
- The API does not impersonate Telegram in the Feature 133 handler.
- The main compose environment nevertheless passes Telegram credential variables to Iran-side API/Worker services. This is a secret-isolation violation even where the current API source does not consume them.
- No live secret values were found in the inspected source/configuration files.

## 10. Final Verdict

`ARCHITECTURE_BOUNDARY_VIOLATION`

The API’s active Feature 133 implementation respects the boundary, but the overall repository/deployment architecture does not: a direct Telegram poller remains in the Iran-side Worker image and Telegram secrets are injected into the shared Iran-side deployment environment.

## 11. Recommended Next Steps

1. Remove production Telegram secret injection from the API/Worker environment and rotate any credentials that may have been exposed to those services.
2. Retire or isolate `TelegramDevPollingWorker` from the Iran-deployed Worker artifact; verify the Worker image contains no direct Bot API path.
3. Remove or clearly deprecate `TelegramNotificationOptions` and the empty direct-notification configuration after confirming deployment consumers.
4. Run a repository check that only `FinancialCopilot.TelegramGateway` contains Bot API URLs, polling, and outbound Telegram operations.
5. Perform an authorized staging verification from Linode and inspect live container environment/firewall rules before declaring runtime isolation complete.

