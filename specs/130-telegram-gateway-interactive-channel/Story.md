# Story — Interactive Telegram FinancialCopilot Query

## 1. User Outcome

A linked FinancialCopilot user can send a Persian financial question to the Telegram bot and receive, in the same Telegram chat, the response generated and rendered by the existing FinancialCopilot AI/query pipeline.

## 2. Preconditions

- The Telegram bot is configured and polling is enabled.
- `FinancialCopilot.TelegramGateway` is continuously supervised and running on Linode.
- The Telegram user is already linked to a FinancialCopilot account.
- The gateway has outbound access to the Telegram Bot API and `https://api.sapioai.ir`.
- The gateway has a valid dedicated API key authorized for the Telegram assistant and account-link confirmation paths.
- Writable persistent storage is available for the polling offset and gateway idempotency files.

## 3. Main Flow

1. The linked user sends an ordinary Persian financial question to the Telegram bot.
2. `TelegramGatewayPollingWorker` receives the text update through Telegram long polling and maps its Telegram identifiers, text, locale, timestamp, and correlation value.
3. `PrimaryApiClient` sends the mapped update over authenticated HTTPS to `POST /api/v1/telegram/assistant/updates` on `api.sapioai.ir`.
4. `TelegramAssistantController` passes the update to `TelegramAiAssistantAdapter`, which resolves the linked user and obtains the existing chat/thread conversation binding.
5. `TelegramAiAssistantAdapter` invokes the existing `IAiQueryOrchestrationService`, and the FinancialCopilot backend produces and renders the answer.
6. The gateway receives the ordered rendered message parts and sends them without analytical rewriting to the originating Telegram chat.
7. After all parts are successfully delivered, the gateway persists confirmed part outcomes and advances the polling offset.

## 4. Acceptance Criteria

- **AC-01:** When polling is enabled and Telegram returns an ordinary text-message update, `TelegramGatewayPollingWorker` processes that update through the interactive assistant flow.
- **AC-02:** A gateway mapping test proves that the existing `TelegramAssistantUpdateRequest` receives the update ID, message kind, Telegram user ID, chat ID, optional thread ID, message ID, text, locale, received timestamp, and `telegram:{UpdateId}` correlation value from a representative Persian text update.
- **AC-03:** A malformed or unsupported update is logged by update ID and redacted reason without message text, does not crash the worker, and is treated as terminal so the offset advances past it.
- **AC-04:** The gateway sends interactive updates only to the existing `POST /api/v1/telegram/assistant/updates` endpoint and does not call `/api/ai/v1/query` directly or introduce another interactive endpoint.
- **AC-05:** Enabling the gateway with production configuration requires `PrimaryApiBaseUrl` to use HTTPS and target `https://api.sapioai.ir`.
- **AC-06:** Each assistant request sends the configured raw API key in `X-Api-Key` and sends the request correlation value in `X-Correlation-Id` as `telegram:{UpdateId}`.
- **AC-07:** An authenticated API integration test proves that a request using the dedicated API client follows `TelegramAssistantController` → `TelegramAiAssistantAdapter` → the existing `IAiQueryOrchestrationService` and returns a conversation ID and rendered messages.
- **AC-08:** The backend resolves the Telegram identity through the existing account link and does not add guest Telegram access or linked-user resolution to the gateway.
- **AC-09:** Ordinary linked-user text reuses or creates the existing per-chat/thread `TelegramConversationBinding`, and subsequent updates preserve that conversation context through the backend.
- **AC-10:** For a duplicate Telegram update, the existing `TelegramProcessedUpdates` record replays the persisted backend result without a second AI execution, as proven by an automated duplicate-replay test.
- **AC-11:** Outbound response text, parse mode, actions, and optional media come from the backend-rendered `TelegramAssistantResult`; the gateway neither performs financial reasoning nor rewrites the analytical result.
- **AC-12:** Rendered response parts are sent in `PartNumber` order to the originating Telegram chat, and a bounded real-bot staging smoke test proves a Persian question completes the Linode → `api.sapioai.ir` → Telegram round trip.
- **AC-13:** After Telegram confirms a response part, `GatewayIdempotencyStore` durably records its update/part outcome and a replay skips that confirmed part while continuing any incomplete parts.
- **AC-14:** On a primary API timeout, network failure, HTTP 429, or HTTP 5xx response, the update is not completed, the next offset is not persisted, and the polling loop retries it with bounded backoff; automated tests cover this retained-offset behavior.
- **AC-15:** A primary API HTTP 2xx response is treated as the backend's completed, persisted outcome even when `TelegramAssistantResult.Status` is `TransientError`; its rendered parts are delivered, and the offset advances only after their terminal delivery outcome rather than retrying the backend result.
- **AC-16:** On HTTP 401 or 403 from the primary API, the gateway logs a redacted configuration/security error, exposes unhealthy readiness, sends a generic temporary-unavailability message when the chat is known, and treats the update as terminal; API integration coverage verifies missing or invalid credentials are rejected without exposing backend details.
- **AC-17:** On a Telegram send timeout, HTTP 429, or HTTP 5xx response, the response part and update remain incomplete, the offset does not advance, and the update is retried; automated tests verify both retained offset and skipping of parts already confirmed before the failure.
- **AC-18:** On a permanent Telegram rejection, the gateway logs only the redacted Telegram error plus update/chat identifiers, treats the update as terminal, and advances the offset so subsequent updates continue.
- **AC-19:** Delivery remains practical at-least-once across the existing backend and gateway idempotency layers; no exactly-once guarantee is made, and the accepted crash window after Telegram accepts a send but before local confirmation persistence may produce a duplicate.
- **AC-20:** Enabled production configuration supplies `BotToken`, `PrimaryApiBaseUrl`, `PrimaryApiKey`, request and long-poll timeouts, polling interval/limit, and durable absolute `OffsetFilePath` and `IdempotencyFilePath` values.
- **AC-21:** The primary API uses one active dedicated TelegramGateway API client with stable client/tenant IDs and allowed path prefixes limited to `/api/v1/telegram/assistant/updates` and `/api/v1/telegram/link/confirm`; raw secrets are supplied through deployment environment/secret configuration, are not committed, and are not logged.
- **AC-22:** `FinancialCopilot.TelegramGateway` runs as a single continuously supervised Linode process with persistent local state and outbound access to Telegram and `api.sapioai.ir`, while the FinancialCopilot API, orchestration, identity, conversations, PostgreSQL data, and financial services remain in Iran; the interactive path requires no Iran-to-Linode connection or webhook.

### Excluded From This Story

- Proactive alerts.
- Scheduled messages.
- Notification outbox.
- Background monitoring.
- Portfolio or report alerts.
- Telegram Mini Apps.
- Payments or subscriptions.
- Data replication.
- Guest Telegram access.
- A second Telegram AI agent.
- Telegram-specific financial logic.
- Omnichannel abstraction.
- Redis, RabbitMQ, or an event bus.
- Webhook migration.
- Redesign of the FinancialCopilot AI pipeline.
- Redesign of account linking or conversations.

## 5. Definition of Done

- All acceptance criteria pass and existing tests remain green.
- All required bounded unit, integration, and staging tests pass without adding a new large end-to-end framework.
- No secrets are committed to source control.
- `FinancialCopilot.TelegramGateway` runs on Linode with production configuration and durable local state.
- A linked Telegram user can send a real Persian financial question and receive the FinancialCopilot response.
- No out-of-scope architecture is introduced.

## 6. Design Traceability

| Story Area | Design Section |
|---|---|
| User outcome and scope | Sections 1, 3, and 4 |
| Runtime and deployment flow | Sections 5 and 11 |
| API and message contract | Section 6 |
| Gateway/backend responsibility boundary | Section 7 |
| Authentication and production configuration | Sections 8 and 10 |
| Completion, retry, and idempotency behavior | Section 9 |
| Bounded verification | Section 12 |
| Implementation boundaries | Sections 13 and 15 |

STORY_APPROVED_FOR_TASKS
