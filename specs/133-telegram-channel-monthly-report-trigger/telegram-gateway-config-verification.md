# Telegram Gateway Configuration Verification

## 1. Current Configuration Status

The repository contains a dedicated `FinancialCopilot.TelegramGateway` ASP.NET Core process. Its checked-in defaults are in `src/backend/FinancialCopilot.TelegramGateway/appsettings.json` under `TelegramGateway`.

The supplied snippet is the core configuration, but it is not the complete enabled-production contract. `TelegramGatewaySettings` also defines `MaximumClockSkewSeconds`, rate-limit settings, `RequestTimeoutSeconds`, and `Limit`. When `Enabled=true`, startup validation additionally requires:

- a non-empty `BotToken`;
- an HTTPS `PrimaryApiBaseUrl`;
- a non-empty `PrimaryApiKey`;
- valid polling, timeout, and update-limit values; and
- fully qualified (absolute) writable offset/idempotency paths.

`ServiceId` and `ServiceSecret` are optional for polling, but must either both be present or both be absent. The checked-in relative state paths are therefore development defaults; the compose deployment replaces them with container paths.

## 2. Bot Token Consumption

**BotToken consumed: YES.**

The gateway binds `TelegramGatewaySettings` in `FinancialCopilot.TelegramGateway/Program.cs` with:

```csharp
AddOptions<TelegramGatewaySettings>()
    .BindConfiguration(TelegramGatewaySettings.SectionName)
```

`TelegramGatewaySettings.SectionName` is `TelegramGateway`.

`TelegramApiClient` receives `IOptions<TelegramGatewaySettings>`, reads `settings.BotToken`, and constructs the Telegram Bot API base URL as:

```text
https://api.telegram.org/bot{BotToken}/
```

Therefore the real bot token belongs in the `FinancialCopilot.TelegramGateway` container/process configuration, not in the API or Worker configuration.

## 3. Polling/Webhook Mechanism

Polling is already implemented.

- Hosted service: `TelegramGatewayPollingWorker`, registered with `AddHostedService`.
- Telegram client: `TelegramApiClient`.
- Mechanism: raw `HttpClient` long polling against Telegram `getUpdates`; no Telegram SDK or webhook receiver is used.
- `LongPollTimeoutSeconds` is passed as the `getUpdates` timeout.
- `PollIntervalSeconds` is used between empty successful polling cycles.
- `DeleteWebhookOnStart=true` causes `deleteWebhook` to be called before polling.
- Updates include `message`, `callback_query`, and `channel_post`.
- The next offset and delivery idempotency state are persisted using the configured files.

The gateway then calls `PrimaryApiClient`, which sends the normalized update to the API over HTTPS using `PrimaryApiKey`. The gateway is the only component that constructs a direct `api.telegram.org` URL.

## 4. Enabled Flag Behavior

`TelegramGateway:Enabled` is consumed by both startup validation and `TelegramGatewayPollingWorker`.

When false, the hosted worker logs that polling is disabled and returns without contacting Telegram. When true, the worker loads the persisted offset, optionally deletes the webhook, and enters the long-poll loop.

`Enabled` in the API is a separate setting: `Telegram:Gateway:Enabled`. It controls whether the API-side `TelegramGatewayClient` is willing to call the gateway’s internal routes. It does not enable direct Telegram polling.

## 5. Production Configuration Source

The documented Linode flow deploys the gateway separately from the Iran-side API/Worker. The deployment directory is:

```text
/opt/financial-copilot-telegram-gateway
```

The production `.env` in that directory is secret-managed and is not copied from source control. The deployment instructions explicitly require retaining `TELEGRAM_BOT_TOKEN` and `TELEGRAM_PRIMARY_API_KEY` there. The gateway compose file translates those variables into .NET configuration keys.

The production flow is therefore:

```text
Linode secret .env
  -> docker/telegram-gateway.compose.yml environment mapping
  -> TelegramGateway__* environment variables
  -> .NET configuration section TelegramGateway
  -> TelegramGatewaySettings
```

The repository cannot verify the live Linode `.env` values. No populated production secret was found in the repository.

## 6. Docker Compose / Environment Mapping

`docker/telegram-gateway.compose.yml` is the active dedicated gateway compose definition. It maps:

```text
TELEGRAM_GATEWAY_ENABLED       -> TelegramGateway__Enabled
TELEGRAM_BOT_TOKEN             -> TelegramGateway__BotToken
TELEGRAM_PRIMARY_API_BASE_URL  -> TelegramGateway__PrimaryApiBaseUrl
TELEGRAM_PRIMARY_API_KEY       -> TelegramGateway__PrimaryApiKey
TELEGRAM_GATEWAY_SERVICE_ID    -> TelegramGateway__ServiceId
TELEGRAM_GATEWAY_SERVICE_SECRET-> TelegramGateway__ServiceSecret
```

It also forces `ASPNETCORE_ENVIRONMENT=Production`, sets absolute container paths for offset/idempotency state, mounts persistent gateway state, and publishes the gateway only on loopback (`127.0.0.1`).

The dedicated compose file does not explicitly override `PollIntervalSeconds`, `LongPollTimeoutSeconds`, `RequestTimeoutSeconds`, `Limit`, or `DeleteWebhookOnStart`; those values come from the application configuration defaults unless separately supplied as environment variables. The checked-in defaults are 1 second, 25 seconds, 40 seconds, 50 updates, and webhook deletion enabled.

The main `docker-compose.yml` is for the API/Worker stack and does not configure `TelegramGateway:BotToken`. Historical `.bak` compose files contain older Telegram settings, but they are not the active compose definition and were not treated as production configuration.

## 7. Feature 133 Compatibility

Feature 133 is compatible with the existing gateway flow:

```text
Telegram channel post
  -> TelegramGatewayPollingWorker
  -> PrimaryApiClient
  -> POST /api/v1/telegram/assistant/updates
  -> TelegramAssistantController
  -> TelegramChannelMonthlyReportHandler
```

The gateway maps channel posts and forwards them through the same authenticated assistant-update endpoint. The API controller dispatches `ChannelPost` updates to `TelegramChannelMonthlyReportHandler`. The handler owns Feature 133’s separate `ChannelMonthlyReports` gates:

- `AutoBackfillEnabled` controls refresh/backfill execution;
- `AutoPublishTrendEnabled` controls publication after readiness;
- `AllowedChannelIds` and authorized API-client/tenant settings constrain execution.

These Feature 133 settings are API-side configuration. They are not replacements for `TelegramGateway:BotToken`, and they do not require moving the token into the API or Worker.

Feature 133 therefore does not require a new Telegram gateway configuration mechanism. It requires the existing gateway/API credentials and routing to be correctly provisioned, plus the desired API-side `ChannelMonthlyReports` values.

## 8. Required Changes

No code or configuration-file change is required to consume the bot token or implement polling; both already exist.

For a production activation, the operational configuration must be verified/provisioned outside source control:

1. Set `TELEGRAM_GATEWAY_ENABLED=true` in the Linode gateway secret environment.
2. Set the real `TELEGRAM_BOT_TOKEN` there.
3. Set the HTTPS primary API URL and matching `TELEGRAM_PRIMARY_API_KEY`.
4. Ensure the API’s configured gateway client accepts the matching key and the assistant-update path.
5. Ensure the gateway state directory is persistent and writable.
6. Set API-side `ChannelMonthlyReports` gates according to the intended Feature 133 rollout; these are independent of the gateway token.

The repository’s `docker/telegram-gateway.env.example` and root `.env.example` are templates only. They do not prove that Linode has populated values.

## 9. Final Recommendation

Keep the real bot token exclusively in the dedicated `FinancialCopilot.TelegramGateway` Linode/container secret configuration, exposed to the process as `TelegramGateway__BotToken` (normally through `TELEGRAM_BOT_TOKEN`). Do not place the token in `FinancialCopilot.API` or `FinancialCopilot.Worker` configuration.

The requested hypothesis is confirmed in substance: the existing gateway already consumes the token and polls Telegram, and Feature 133 uses that path. Before production activation, verify the live Linode secret `.env`, the API key/client alignment, persistent state mount, and the API-side Feature 133 gates. No new gateway configuration schema is needed.

