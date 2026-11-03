# Feature 130 — Implementation Report

## 1. Outcome

Repository implementation is complete for T130-01 through T130-09. The Telegram gateway now sends correlated, API-key-authenticated requests through the existing Telegram assistant endpoint; advances offsets according to explicit terminal/retry outcomes; preserves ordered, replay-safe response delivery; reports unhealthy readiness after backend 401/403; and supports polling-only startup without inbound HMAC credentials. T130-10 remains ready for operational verification because this environment has no authorized Linode or real Telegram bot access.

## 2. Production Changes

- `PrimaryApiClient` now sends `X-Correlation-Id`, preserves HTTP failure status for worker classification, and implements the registered primary-API authentication health check.
- `TelegramGatewayPollingWorker` now maps ordinary text updates through the existing endpoint and advances offsets only after successful or approved terminal outcomes.
- `TelegramApiClient` now classifies Telegram 429/timeouts/5xx as transient and permanent rejections as terminal using the existing operation-result contract.
- `GatewayIdempotencyStore` now persists confirmed sends before exposing them for replay skipping and ignores legacy/non-success entries.
- `TelegramGatewaySettings` and `Program.cs` validate HTTPS, polling/timeouts, absolute durable paths, and paired optional HMAC credentials; inbound controllers are mapped only when HMAC is configured.
- Gateway/API configuration no longer contains the tracked bot token, raw primary API key, or HMAC secret; the dedicated API client uses `TELEGRAM_GATEWAY_API_KEY` and remains limited to the two approved paths.
- No backend AI, rendering, account-linking, conversation, billing, or orchestration production code was changed.

## 3. Tests Added/Changed

- Added `tests/FinancialCopilot.UnitTests/TelegramGateway130Tests.cs` with 21 tests covering request headers/path, Persian mapping, configuration, health, offset rules, Telegram classification, multipart ordering, durable idempotency, and replay behavior.
- Added `tests/FinancialCopilot.IntegrationTests/TelegramAssistant130IntegrationTests.cs` with 2 tests covering the linked-user controller/adapter/orchestrator path, conversation binding, rendered response, backend duplicate replay, and API-key authorization/path scope.
- Added the TelegramGateway project reference and internal visibility required by the focused unit tests; no production test-only interface was introduced.

## 4. Acceptance Criteria Verification

| AC | Status | Evidence |
|---|---|---|
| AC-01 | VERIFIED | `Persian_text_update_maps_fields_and_advances_after_delivery` exercises ordinary polled text processing. |
| AC-02 | VERIFIED | The same unit test asserts all Telegram request fields and `telegram:{UpdateId}`. |
| AC-03 | VERIFIED | `Malformed_update_is_terminal_and_advances_offset` proves safe terminal advancement. |
| AC-04 | VERIFIED | `PrimaryApiClient_posts_existing_endpoint_with_api_key_and_correlation` asserts the existing endpoint; no `/api/ai/v1/query` gateway call exists. |
| AC-05 | VERIFIED | Startup validation test rejects HTTP when polling is enabled. |
| AC-06 | VERIFIED | Primary client test asserts `X-Api-Key` and `X-Correlation-Id`. |
| AC-07 | VERIFIED | Integration test proves controller → adapter → recording `IAiQueryOrchestrationService`. |
| AC-08 | VERIFIED | Integration test links the Telegram identity before orchestration; existing unlinked behavior remains unchanged. |
| AC-09 | VERIFIED | Integration test asserts the persisted `TelegramConversationBinding` and orchestration conversation ID. |
| AC-10 | VERIFIED | Integration test asserts `TelegramProcessedUpdates` replay with one orchestration call. |
| AC-11 | VERIFIED | Multipart test passes backend-rendered text directly to Telegram in backend part order. |
| AC-12 | PENDING OPERATIONAL SMOKE | Automated delivery path is verified; the real Linode/Telegram round trip requires T130-10 access. |
| AC-13 | VERIFIED | Multipart replay and store reload tests prove confirmed-part persistence and skipping. |
| AC-14 | VERIFIED | Status and transport theories prove timeout/network/429/5xx retain the offset. |
| AC-15 | VERIFIED | `Backend_2xx_TransientError_is_delivered_and_advances_offset` proves the approved terminal backend behavior. |
| AC-16 | VERIFIED | 401/403 tests prove generic terminal messaging and unhealthy-to-healthy readiness transitions; integration tests prove API rejection. |
| AC-17 | VERIFIED | Transient Telegram multipart test proves retained offset and retry with confirmed-part skipping. |
| AC-18 | VERIFIED | Permanent Telegram rejection test proves terminal offset advancement. |
| AC-19 | VERIFIED | Store persistence/replay tests retain practical at-least-once behavior without an exactly-once mechanism. |
| AC-20 | VERIFIED | Configuration validation covers credentials, timeouts, polling bounds, and absolute persistent paths. |
| AC-21 | VERIFIED | Configuration and integration tests prove the dedicated path-scoped API client and absence of tracked raw Feature 130 secrets. |
| AC-22 | VERIFIED (REPOSITORY) | Gateway startup/configuration preserves the Linode outbound-only boundary; live deployment confirmation remains in T130-10. |

## 5. Test Results

- `dotnet test tests/FinancialCopilot.UnitTests/FinancialCopilot.UnitTests.csproj --configuration Release --filter TelegramGateway130Tests --no-restore` — PASS: 21 passed, 0 failed, 0 skipped.
- `dotnet test tests/FinancialCopilot.IntegrationTests/FinancialCopilot.IntegrationTests.csproj --configuration Release --filter TelegramAssistant130IntegrationTests --no-restore` — PASS: 2 passed, 0 failed, 0 skipped.
- `dotnet test src/backend/FinancialCopilot.sln --configuration Release` — FAIL: 2,094 passed, 40 failed, 0 skipped (unit: 1,650 passed/2 failed; integration: 432 passed/38 failed; architecture: 12 passed/0 failed). All Feature 130 focused tests passed. The failures are outside the changed gateway implementation and include existing financial-statement, scanner, valuation, market-insight, monthly-trend, and older Telegram fixture expectations; they were not altered to force a green suite.
- Isolated Telegram run including Feature 087, 088, and 130 suites — 4 passed and 8 older Feature 087/088 fixture failures; both Feature 130 tests passed. The older fixtures use an API-client tenant that does not match their linked web-user tenant.
- `dotnet build src/backend/FinancialCopilot.TelegramGateway/FinancialCopilot.TelegramGateway.csproj --configuration Release` — PASS: 0 warnings, 0 errors.
- `git diff --check` — PASS, with only repository line-ending conversion warnings.
- Raw Feature 130 credential scan — PASS: removed bot token, primary API key, and HMAC secret were not found outside build outputs.

## 6. Remaining Operational Verification

T130-10 requires an operator with Linode and real bot access to:

1. Set `TelegramGateway__Enabled=true`, `TelegramGateway__BotToken`, `TelegramGateway__PrimaryApiBaseUrl=https://api.sapioai.ir`, `TelegramGateway__PrimaryApiKey`, timeout/polling values, and absolute writable `TelegramGateway__OffsetFilePath` and `TelegramGateway__IdempotencyFilePath` values in the supervisor's secret/environment configuration.
2. Set `TELEGRAM_GATEWAY_API_KEY` to the matching raw key in the Iran API deployment and restart/reload the API and supervised Linode gateway processes.
3. From Linode, verify DNS/TLS access with `curl --fail --silent --show-error https://api.telegram.org/ > /dev/null` and `curl --fail --silent --show-error https://api.sapioai.ir/health`.
4. Verify the gateway's local readiness endpoint with `curl --fail --silent --show-error http://127.0.0.1:<gateway-port>/health`.
5. Send one Persian financial question from an already-linked Telegram user and confirm the rendered answer returns to the originating chat; correlate logs using `telegram:<UpdateId>` without recording the message text or secrets.
6. In staging, make the primary assistant request return 503 for one update, confirm the offset does not advance, restore the API, and confirm retry/replay completes without repeating already confirmed Telegram parts.
7. Restart the supervised gateway and confirm it reuses both persistent state files and remains healthy.

## 7. Scope Confirmation

No webhook, queue, Redis, RabbitMQ, event bus, distributed lock, exactly-once claim, generic channel abstraction, second AI agent, guest access, data replication, financial logic, or redesign of authentication, account linking, conversations, billing, or AI orchestration was introduced.
