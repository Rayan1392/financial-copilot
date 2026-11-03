# Feature 130 — Implementation Tasks

## Slice 1 — Authenticated Gateway → FinancialCopilot Request Path

### T130-01 — Secure and validate the polling-only gateway configuration

**Status:** DONE

**Purpose**
Make the existing gateway start safely on Linode with production HTTPS/API-key settings and without requiring unrelated inbound HMAC connectivity.

**Changes**

* Update `src/backend/FinancialCopilot.TelegramGateway/appsettings.json` to remove the tracked bot token, primary API key, and HMAC secret while retaining disabled, non-secret example values.
* Correct the dedicated TelegramGateway client in `src/backend/FinancialCopilot.API/appsettings.json` so `KeyEnvironmentVariable` names the deployment environment variable instead of containing a credential-shaped value, while preserving the two approved path prefixes.
* Update `TelegramGatewaySettings` and `FinancialCopilot.TelegramGateway/Program.cs` validation so enabled polling requires `BotToken`, an HTTPS `PrimaryApiBaseUrl`, `PrimaryApiKey`, valid timeout/polling values, and non-empty offset/idempotency paths.
* Decouple polling validation from `ServiceId`/`ServiceSecret`; require those two values as a pair and map the existing `TelegramGatewayController` only when the inbound HMAC capability is configured.

**Acceptance Criteria**

* AC-05
* AC-20
* AC-21

**Verification**

* Add configuration tests proving enabled polling rejects HTTP or missing interactive credentials, accepts polling-only configuration without HMAC credentials, and rejects a partially configured HMAC pair.
* Search tracked gateway/API configuration for the removed raw credential values and verify none remain.

### T130-02 — Complete the existing PrimaryApiClient request and readiness contract

**Status:** DONE

**Purpose**
Send each Telegram assistant update through the approved authenticated endpoint with end-to-end correlation and observable authentication readiness.

**Changes**

* Update `PrimaryApiClient.HandleUpdateAsync` to construct the existing `POST api/v1/telegram/assistant/updates` request explicitly and add the request correlation value as `X-Correlation-Id` while retaining `X-Api-Key` and the configured request timeout.
* Preserve HTTP status information from failed calls so `TelegramGatewayPollingWorker` can distinguish 401/403 from retryable 429/5xx responses; do not add a second backend client or call `/api/ai/v1/query`.
* Make the existing singleton `PrimaryApiClient` report an unhealthy `IHealthCheck` result after 401/403 and recover after a later authenticated success, then register that check on the existing gateway `/health` pipeline in `Program.cs`.

**Acceptance Criteria**

* AC-04
* AC-06
* AC-14
* AC-16

**Verification**

* Add focused `PrimaryApiClient` tests with a recording `HttpMessageHandler` for the exact path, method, JSON contract, `X-Api-Key`, `X-Correlation-Id`, timeout, retryable status propagation, and authentication health transitions.

### T130-03 — Prove the existing authenticated backend assistant path

**Status:** DONE

**Purpose**
Lock the approved controller-to-orchestrator behavior with integration coverage instead of changing backend responsibilities.

**Changes**

* Add `tests/FinancialCopilot.IntegrationTests/TelegramAssistant130IntegrationTests.cs` using the existing `WebApplicationFactory` patterns and a test-only path-scoped TelegramGateway API client.
* Seed a linked Telegram identity and replace only `IAiQueryOrchestrationService` with a recording test double so the test can assert actor, tenant, external user, and conversation inputs.
* Exercise `POST /api/v1/telegram/assistant/updates` through `TelegramAssistantController` and `TelegramAiAssistantAdapter`, asserting existing conversation binding, backend rendering, and `TelegramProcessedUpdates` duplicate replay.
* Verify missing, invalid, and path-disallowed API keys are rejected by the existing API-key authentication infrastructure.

**Acceptance Criteria**

* AC-07
* AC-08
* AC-09
* AC-10
* AC-16
* AC-21

**Verification**

* Run the new integration test class and assert a duplicate update returns the persisted rendered result without a second orchestration call.
* Assert unauthenticated and invalid/path-disallowed clients receive 401/403 as applicable.

## Slice 2 — Interactive Telegram Flow and Delivery Semantics

### T130-04 — Make worker completion outcomes control offset advancement

**Status:** DONE

**Purpose**
Ensure the existing long-polling worker advances its durable offset only for successful or approved terminal outcomes.

**Changes**

* Update `TelegramGatewayPollingWorker` directly so update handling returns a small private/internal completion outcome used by `ExecuteAsync`; do not introduce a polling service or channel interface.
* Retain the existing ordinary text mapping to `TelegramAssistantUpdateRequest`, including IDs, locale, timestamp, and `telegram:{UpdateId}` correlation.
* Treat malformed/unsupported updates as redacted terminal outcomes that advance the offset without stopping later updates.
* Retain the offset for primary API timeout/network/429/5xx failures and transient Telegram delivery failures, and apply the existing bounded polling-loop delay before retry.
* Treat primary API 401/403 and permanent Telegram rejection as approved terminal outcomes, including the generic chat message for known chats on authentication failure.
* Treat every primary API 2xx `TelegramAssistantResult`, including `Status = TransientError`, as a completed backend outcome whose offset advances only after its rendered parts reach a successful or approved terminal delivery outcome.

**Acceptance Criteria**

* AC-01
* AC-02
* AC-03
* AC-14
* AC-15
* AC-16
* AC-17
* AC-18

**Verification**

* Exercise update processing with temporary offset storage and assert the exact persisted offset after successful, malformed, backend-transient, backend-authentication, 2xx `TransientError`, Telegram-transient, and Telegram-permanent outcomes.

### T130-05 — Classify Telegram send failures without redesigning TelegramApiClient

**Status:** DONE

**Purpose**
Give the polling worker the approved transient-versus-permanent delivery signal using the existing Telegram operation result contract.

**Changes**

* Update `TelegramApiClient.SendOperationAsync` to classify HTTP 429, request timeout, and HTTP 5xx as retryable `RateLimited`, `Timeout`, or `GatewayUnavailable` outcomes.
* Classify non-retryable Telegram rejection as the existing redacted permanent error outcome and retain only provider message IDs or safe error metadata.
* Keep the current `sendMessage`/`sendPhoto`, long-polling, and local DTO implementations; do not introduce a Telegram provider abstraction or retry library.

**Acceptance Criteria**

* AC-17
* AC-18

**Verification**

* Add HTTP-handler tests for successful sends, 429, timeout, representative 5xx, and permanent 4xx responses, asserting only the approved redacted outcome data is returned.

### T130-06 — Complete ordered multipart delivery and durable replay skipping

**Status:** DONE

**Purpose**
Deliver the backend-rendered response in order while preserving practical at-least-once behavior across retries.

**Changes**

* Update `TelegramGatewayPollingWorker.SendMessagesAsync` to return success/transient/permanent delivery outcome, send `TelegramAssistantResult.Messages` in `PartNumber` order to the source chat, and stop at the first incomplete transient part.
* Pass backend-provided text, parse mode, actions, and optional PNG media unchanged to `TelegramApiClient`; do not inspect `AiResponse` or add financial logic.
* Update `GatewayIdempotencyStore` only as needed so a response-part outcome is considered replayable after its local JSON persistence succeeds; transient failures must never be stored as completed.
* On replay, skip durably confirmed parts and continue remaining parts; preserve the accepted crash window between Telegram acceptance and local persistence without claiming exactly-once delivery.

**Acceptance Criteria**

* AC-11
* AC-12
* AC-13
* AC-15
* AC-19

**Verification**

* Test ordered text/photo delivery, unchanged rendered content, stopping on a transient middle part, replay skipping of confirmed earlier parts, and retry after idempotency-file persistence failure.
* Verify the offset is saved only after the complete multipart outcome is successful or permanently terminal.

## Slice 3 — Bounded Verification and Linode Readiness

### T130-07 — Add polling and Primary API failure regression tests

**Status:** DONE

**Purpose**
Provide bounded automated coverage for inbound mapping and backend completion rules at the gateway boundary.

**Changes**

* Add a project reference from `tests/FinancialCopilot.UnitTests/FinancialCopilot.UnitTests.csproj` to `FinancialCopilot.TelegramGateway` and, if needed, expose only internal existing-worker methods to `FinancialCopilot.UnitTests` through the gateway project file.
* Add `tests/FinancialCopilot.UnitTests/TelegramGatewayPollingWorker130Tests.cs` using recording HTTP handlers and temporary files rather than new production interfaces.
* Cover Persian text mapping, malformed/unsupported advancement, timeout/network/429/5xx offset retention, 2xx `TransientError` completion, and 401/403 terminal behavior with the generic user message and unhealthy readiness.

**Acceptance Criteria**

* AC-01
* AC-02
* AC-03
* AC-14
* AC-15
* AC-16

**Verification**

* Run `dotnet test tests/FinancialCopilot.UnitTests/FinancialCopilot.UnitTests.csproj --configuration Release --filter TelegramGatewayPollingWorker130Tests`.

### T130-08 — Add delivery and idempotency regression tests

**Status:** DONE

**Purpose**
Prove ordered delivery, transient/permanent Telegram behavior, and replay semantics using the existing clients and local store.

**Changes**

* Add `tests/FinancialCopilot.UnitTests/TelegramGatewayDelivery130Tests.cs` with temporary offset/idempotency paths and deterministic Telegram HTTP responses.
* Cover ordered multipart delivery to the originating chat, backend-rendered content preservation, confirmed-part persistence and replay skipping, transient send offset retention, permanent rejection advancement, and the accepted post-send/pre-persist duplicate window.
* Reuse the existing `TelegramGatewayOperationResult`, `TelegramApiClient`, and `GatewayIdempotencyStore`; add no test-only production interface.

**Acceptance Criteria**

* AC-11
* AC-12
* AC-13
* AC-17
* AC-18
* AC-19

**Verification**

* Run `dotnet test tests/FinancialCopilot.UnitTests/FinancialCopilot.UnitTests.csproj --configuration Release --filter TelegramGatewayDelivery130Tests`.

### T130-09 — Verify configuration, security, health, and full regression

**Status:** DONE

**Purpose**
Confirm production-safe startup and security behavior without adding an observability or secret-management subsystem.

**Changes**

* Add `tests/FinancialCopilot.UnitTests/TelegramGatewayConfiguration130Tests.cs` for polling-only startup validation, HTTPS enforcement, durable path requirements, paired HMAC validation, and `PrimaryApiClient` health transitions.
* Validate that the dedicated API client remains active and restricted to `/api/v1/telegram/assistant/updates` and `/api/v1/telegram/link/confirm`, with its raw key supplied only through the named deployment environment variable or a hash.
* Run the existing unit, integration, and architecture test projects after the focused Feature 130 tests pass; fix only regressions caused by Feature 130 work.

**Acceptance Criteria**

* AC-05
* AC-16
* AC-20
* AC-21

**Verification**

* Run `dotnet test src/backend/FinancialCopilot.sln --configuration Release`.
* Confirm tracked configuration contains no bot token, raw TelegramGateway API key, or HMAC secret and that logs/tests never emit those values or full Telegram message payloads.

### T130-10 — Deploy and run the bounded Linode smoke test

**Status:** READY_FOR_OPERATIONAL_VERIFICATION

The current environment has no authorized access to the Linode service host or real Telegram bot, so the real supervised deployment and chat round trip could not be executed here.

**Purpose**
Prove the completed vertical path with one linked user under the approved single-instance deployment boundary.

**Changes**

* Configure the existing `FinancialCopilot.TelegramGateway` process on Linode with supervision/automatic restart, the bot token, `https://api.sapioai.ir`, the dedicated API key, request/polling timeouts, and writable absolute offset/idempotency paths.
* Verify outbound DNS/HTTPS access from Linode to Telegram and `api.sapioai.ir`; do not add an Iran-to-Linode route, webhook, broker, database, or infrastructure-as-code layer.
* Use one already-linked Telegram test user to execute the approved real Persian financial question round trip, then simulate a bounded primary API outage and confirm the update is retried after service recovery without a second backend AI execution when `TelegramProcessedUpdates` already contains the result.

**Acceptance Criteria**

* AC-12
* AC-14
* AC-20
* AC-21
* AC-22

**Verification**

* Record the smoke-test correlation ID and verify the response returns to the originating chat through `TelegramAssistantController` → `TelegramAiAssistantAdapter` → `IAiQueryOrchestrationService`.
* Restart the supervised process and verify the persisted offset/idempotency files are reused and `/health` is healthy with valid service credentials.

## AC Coverage Matrix

| Acceptance Criterion | Task(s) |
| -------------------- | ------- |
| AC-01 | T130-04, T130-07 |
| AC-02 | T130-04, T130-07 |
| AC-03 | T130-04, T130-07 |
| AC-04 | T130-02 |
| AC-05 | T130-01, T130-09 |
| AC-06 | T130-02 |
| AC-07 | T130-03 |
| AC-08 | T130-03 |
| AC-09 | T130-03 |
| AC-10 | T130-03 |
| AC-11 | T130-06, T130-08 |
| AC-12 | T130-06, T130-08, T130-10 |
| AC-13 | T130-06, T130-08 |
| AC-14 | T130-02, T130-04, T130-07, T130-10 |
| AC-15 | T130-04, T130-06, T130-07 |
| AC-16 | T130-02, T130-03, T130-04, T130-07, T130-09 |
| AC-17 | T130-04, T130-05, T130-08 |
| AC-18 | T130-04, T130-05, T130-08 |
| AC-19 | T130-06, T130-08 |
| AC-20 | T130-01, T130-09, T130-10 |
| AC-21 | T130-01, T130-03, T130-09, T130-10 |
| AC-22 | T130-10 |

TASKS_APPROVED_FOR_IMPLEMENTATION
