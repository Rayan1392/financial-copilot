# Telegram channel membership and free entitlement

Feature 088 adds channel-membership eligibility for the Telegram free allowance without creating a Telegram wallet.

## Runtime configuration

```json
"Telegram": {
  "Membership": {
    "RequiredChannelId": "@financial_copilot_channel",
    "BotTokenEnvironmentVariable": "TELEGRAM_BOT_TOKEN",
    "VerificationCacheMinutes": 60,
    "ProviderFailureCacheMinutes": 5,
    "DailyFreeCredits": 5,
    "PolicyVersion": "telegram-free-daily-v1"
  }
}
```

The bot token is read from the configured environment variable. It must not be stored in source-controlled settings.

## Behavior

- `POST /api/v1/telegram/membership/verify` reads the active Feature 087 Telegram link, calls Telegram `getChatMember`, normalizes the provider status, and stores the latest verification plus history.
- Eligible statuses are creator, administrator, member, and restricted member with active membership.
- Unknown provider failures are fail-closed when no valid eligible cache exists. A valid cached eligible verification remains usable until its expiry.
- `GET /api/v1/telegram/entitlement/me` returns link state, membership freshness, the current Tehran-day free allowance bucket, paid spending capacity, and consumption order.
- The AI billing hook calls Billing before reservation. If the linked actor has a current eligible membership, Billing grants the Tehran-day allowance idempotently.

## Billing model

Billing remains the source of truth:

- Daily grants are recorded in `billing_daily_free_allowance_grants`.
- The grant also writes an immutable `billing_usage_ledger_entries` adjustment with `AllocationSource = TelegramDailyFreeAllowance`.
- AI charge ledger rows can carry `AllocationSource` and `AllowanceDateKey` evidence.
- The idempotency key is based on actor, Tehran date key, and policy version, so concurrent first use cannot create duplicate grants.
- Unused daily allowance is non-rollover and expires at the next Tehran-day boundary.
