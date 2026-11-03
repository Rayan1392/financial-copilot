# Feature 143 Design

## Architecture

TelegramGateway's existing `PrimaryApiClient` posts the gateway-authenticated update to the existing API endpoint. `TelegramAssistantController` offers the original channel update to the new deterministic financial-statement handler first; if it is not a financial-statement candidate, the controller delegates to the unchanged Feature 133 monthly handler. The feature handler receives the same authenticated API-client actor and reuses Feature 133's allowlist, authorized client, and tenant settings.

The parser and outcome contracts live in Application. Infrastructure owns the parser implementation, company/Noavaran eligibility lookup, durable claim, fiscal evidence check, structured logs, and the call to `IManualNoavaranFinancialStatementBackfillService`. This preserves Feature 142's authorization boundary and all provider/persistence logic.

## Persistence and duplicate behavior

Reuse `AuthDbContext.TelegramProcessedUpdates` and its unique `IdempotencyKey`; the key is `channel-financial-statement:{chatId}:{messageId}`. A first insert is the durable claim. Each category outcome is written to `ResponseJson` after its service call. Completed, disabled, and unresolved states are terminal. A duplicate racing the unique insert is suppressed. A row left in a claimed/processing state after a process interruption is ambiguous and fails closed for review. This does not claim distributed exactly-once execution: Feature 142's own ingestion idempotency and normalized upserts remain the second protection layer.

## Fiscal-year and period limitations

The announcement parser returns the reporting end date independently. The existing catalog has no fiscal-year metadata; source evidence on exact-period normalized Noavaran statements is the only currently available authoritative resolution used here. Feature 142's request selects year and `perTId`; the existing client has no exact date or request-level variant override. The implementation does not invent provider parameters or map amendment status onto `isRepresented`.

## Configuration

`ChannelFinancialStatements:AutoBackfillEnabled` is a separate boolean, false by default. Existing `ChannelMonthlyReports` settings continue to govern actor authorization; the configured channel allowlist must explicitly contain the CodalMirror signed chat ID (an empty list keeps Feature 143 closed). Production must explicitly enable the new flag after deployment review.

`ChannelFinancialStatements:InitialDelayMinutes` defaults to 5 and accepts 1–1440. The authorized Telegram delivery stores the recognized announcement as a durable `Feature143:Scheduled` checkpoint without calling Noavaran. The API worker scans every 30 seconds and atomically claims due checkpoints, then runs the existing four-category backfill and validation flow. The delay starts when the API accepts the channel post; worker scheduling can add up to about 30 seconds. The environment example exposes `CHANNEL_FINANCIAL_STATEMENTS_INITIAL_DELAY_MINUTES`, which Docker Compose maps to the application setting. Duplicate deliveries while scheduled use the existing message key and cannot trigger an early fetch. The manual admin backfill endpoint is unaffected.

## Operational visibility

Structured application logs include chat/message, ticker, external company id, announcement category, reported period end, resolved fiscal-year ending, months, explicit variant flags, per-category result/count, failure reason, and correlation id. Credentials and tokens are never logged.
