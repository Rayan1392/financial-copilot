# Telegram Account Linking

Feature 087 links a Telegram identity to an existing canonical Financial Copilot web user. Telegram remains a delivery channel; it does not create users, tenants, wallets, subscriptions, or Billing entries.

## Configuration

Configure non-secret link settings under `Telegram:AccountLinking`:

```json
{
  "BotUsername": "financial_copilot_bot",
  "WebConfirmationBaseUrl": "https://app.example.com/telegram/link/confirm",
  "TokenLifetimeMinutes": 10
}
```

The Telegram adapter authenticates with the existing `X-Api-Key` API-client mechanism. Bot tokens and API-key secrets must be supplied through deployment secret storage and must not be placed in these settings.

## Web-first flow

1. An authenticated web user calls `POST /api/v1/telegram/link-token`.
2. The API revokes older pending challenges for that actor and returns a deep link containing a short-lived token.
3. The bot receives `/start link_<token>` in a private chat.
4. The authenticated adapter calls `POST /api/v1/telegram/link/confirm` with the start parameter and numeric Telegram user/chat identity.
5. The backend atomically consumes the token and creates one active actor-to-Telegram link.

## Telegram-first flow

1. The adapter calls `POST /api/v1/telegram/link/telegram-start` with the numeric Telegram identity and update id.
2. The API returns a short-lived web confirmation URL.
3. After login, the canonical web user explicitly confirms through `POST /api/v1/telegram/link/web-confirm`.
4. The backend binds the challenge identity to that authenticated actor.

## Link management

- `GET /api/v1/telegram/link/me` returns the current actor's active link.
- `DELETE /api/v1/telegram/link/me` revokes it from the web application.
- `POST /api/v1/telegram/link/unlink-from-telegram` lets the authenticated adapter revoke it after an explicit bot confirmation.
- Unlink is idempotent and revokes pending actor/Telegram challenges while preserving audit records.

## Security and identity rules

- `TelegramUserId` is the stable Telegram identity. Username is display metadata only.
- Linking is supported only in a private chat where `TelegramChatId == TelegramUserId`.
- Only SHA-256 token hashes are persisted. Raw bearer tokens appear only in the one-time response.
- Tokens are purpose-bound, expire after the configured lifetime, and are single-use.
- Filtered unique indexes enforce one active Telegram identity per canonical actor and prevent one Telegram user from linking to multiple actors.
- Serializable confirmation plus unique constraints protect concurrent consumption.
- Link, unlink, conflict, replay, issue, and revocation actions are auditable without storing raw tokens.
