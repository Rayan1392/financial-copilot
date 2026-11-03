# Feature 133 — Telegram Channel Monthly Report Trigger

## 1. Objective

Recognize a bounded monthly-report publication in an existing Telegram channel, refresh that company/month through existing ingestion, execute the existing FinancialCopilot trend query, and publish its rendered result to the originating channel. Design only; no implementation is included.

## 2. Scope

### In Scope

- Original channel posts containing text or a textual media caption; deterministic monthly-report recognition and canonical symbol resolution.
- Existing gateway polling, primary API integration, monthly direct ingestion, AI orchestration, rendering, durable deduplication, and basic failure handling.
- Independent global controls for automatic backfill and automatic trend publication; publication always requires successful fresh acquisition for the triggering post.
- A narrow correction to an observed existing routing conflict required by the requested query.

### Out of Scope

Channel discovery/administration UI, edited-post reprocessing, report corrections through edits, OCR, generic NLP, new agents, financial calculations, subscriptions/watchlists, schedulers, brokers, databases, services, and redesign of Features 129/130.

## 3. Existing System Findings

Paths below are repository-relative. Findings describe inspected source, not deployed-server verification. The existing graph index omitted some current gateway/controller types; those files were inspected directly after graph discovery returned insufficient results.

### 3.1 Feature 130 Telegram Gateway

`specs/130-telegram-gateway-interactive-channel/{Design.md,ImplementationReport.md,Deployment-Runbook.md}` describes one supervised Linode process making outbound HTTPS calls to Telegram and the Iran-hosted API. Current `src/backend/FinancialCopilot.TelegramGateway/TelegramGatewayPollingWorker.cs` implements that architecture. Preserve its ordinary-message, account-linking, callback, offset, and multipart delivery paths.

Configuration inspection: gateway `Program.cs` uses `AddOptions<TelegramGatewaySettings>().BindConfiguration(TelegramGatewaySettings.SectionName).Validate(...).ValidateOnStart()`. `TelegramGatewaySettings.cs` defines global PascalCase properties under `TelegramGateway`; the worker/clients consume `IOptions<T>.Value`, not live monitored options. No per-channel automation settings mechanism exists in the inspected gateway. Reuse this binding convention for the already-proposed backend `TelegramChannelMonthlyReportOptions`, with the `ChannelMonthlyReports` section detailed in section 16. The channel authorization allowlist does not become a per-channel settings subsystem.

### 3.2 Telegram inbound update flow

`TelegramApiClient.cs` in the gateway defines `TelegramGatewayUpdate` with only `message` and `callback_query`. `GetUpdatesAsync` passes timeout, limit, and offset, but no explicit `allowed_updates`. `ProcessUpdateAsync` requires `Message.From` and `Message.Chat` for ordinary messages. There is no channel-post branch, caption field, or channel identity handling. Unsupported shapes are skipped and offsets advance.

Telegram exposes `channel_post` separately from `message`, and `edited_channel_post` separately from original posts. A channel message need not have `from`; its `chat.id` is the destination and `message_id` identifies the post within that chat. Explicitly request `message`, `callback_query`, and `channel_post` so a previous polling configuration cannot silently exclude channel posts. See the official [Update/Message definitions](https://core.telegram.org/bots/api#update) and [getUpdates contract](https://core.telegram.org/bots/api#getupdates).

### 3.3 Telegram outbound response flow

`TelegramGatewayPollingWorker.SendMessagesAsync` sends ordered `TelegramAssistantRenderedMessage` parts through `TelegramApiClient.SendMessageAsync` / `SendPhotoAsync`. Confirmed sends are stored under `update:{updateId}:part:{partNumber}`. Transient delivery returns `Retry`; permanent rejection completes the update. Markdown text has a plain-text fallback. PNG type, size, and hash are validated.

`src/backend/FinancialCopilot.Infrastructure/Authentication/TelegramAssistantResponseRenderer.cs`, `Render` / `RenderMonthlyTrend`, already renders `MonthlyActivityTrendResult`, including chart media through `ITelegramMonthlyTrendChartRenderer`. Reuse it without copying financial prose or chart logic.

### 3.4 Monthly backfill implementation

`src/backend/FinancialCopilot.API/Controllers/NoavaranMonthlyBackfillController.cs`, `RunSingleCompanyMonth`, owns `POST /api/v1/admin/noavaran-current/monthly-backfill/single-company-month`. Symbol lookup is an exact trimmed `NoavaranEligibleCompanies.TseSymbol` comparison, resolving an integer `ExternalCompanyId`. Unknown symbols return 404; invalid company/year/month return validation 400; a noninteger resolved ID produces a problem response. Year range is 1404–1500 and month range is 1–12.

The controller awaits `ISingleCompanyMonthlyIngestionService.ExecuteDirectAsync` and returns `AdminMonthlyActivitySingleCompanyMonthDirectResponse`: run ID, company ID, year/month, status, already-processed flag, processed-record count, error count/message, and completion time. HTTP 200 alone is not success: the returned run can be `Failed`.

`src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/SingleCompanyMonthlyIngestionService.cs` fetches all ProductSales output types through `INadpcoMonthlyProductSalesDirectProvider`, then awaits `IFinancialDataSyncProcessor.ProcessPayloadAsync`. Its direct idempotency key includes a newly generated GUID; repeated calls are fresh runs. Its separate `EnqueueAsync` range operation uses the existing queue and is not the requested operation.

`FinancialDataSyncProcessor.cs` persists raw data, invokes normalization, checks requested company/month report existence, and can return `Failed` with `NoDataYet`. It publishes subsequent derived-metric recalculation and invalidates scanner cache. In `NadpcoApi/NadpcoApiMonthlyActivityNormalizer.cs`, normalized rows are saved and single-month ProductSales trend snapshots are recalculated inline before returning. `CompanyMonthlyActivityTrendSnapshotCalculator.RecalculateAsync` can return without a snapshot when OutputType=0 reports or line items are absent. Therefore a completed run does not unconditionally guarantee fresh trend output. Later general derived-metric work is asynchronous but is not a prerequisite for this trend.

### 3.5 Production and sales trend capability

`src/backend/FinancialCopilot.Application/AI/Orchestration/MonthlyActivityTrendIntentRules.cs` explicitly defines `روند تولید و فروش` as an alias for the persisted monthly sales trend, not a new production series. `ConversationalCapabilityContracts.cs` and `SemanticCapabilityExecutors.cs` expose code `monthly_activity_trend` and `MonthlyActivityTrendCapabilityExecutor`.

`src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/MonthlyActivityTrendQueryUseCase.cs` resolves the company and reads `CompanyMonthlyActivityTrendSnapshots` through `ICompanyMonthlyActivityTrendSnapshotRepository`; it returns `MonthlyActivityTrendResponse`. It supports an explicit latest report year/month at use-case level, but the current semantic executor and ordinary orchestration calls do not forward that selection. Ordinary queries use the latest snapshot. `AiQueryResponse.MonthlyActivityTrendResult` carries the structured result; the public `AiFacadeController` exposes `/api/ai/v1/query` with `AiQueryHttpRequest` / `AiQueryHttpResponse` contracts.

**Observed routing conflict:** `MonthlyProductComparisonIntentRules.cs` also matches `تولید و فروش`. Both `AiQueryOrchestrationService.cs` and `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs` give this Feature 129 comparison path precedence. Its `ExtractCompanyText` does not exclude `روند`, so the requested literal can incorrectly resolve that word as the company. Do not claim the literal currently reliably returns a trend.

Feature 129's design was inspected at `specs/129-monthly-product-production-sales-intelligence/Design.md`. Actual `MonthlyProductComparisonUseCase.cs` / `EfCoreMonthlyProductComparisonRepository.cs` read persisted monthly report items and call `MonthlyProductComparisonCalculator`; this is different from the broader snapshot architecture envisioned in that spec. Feature 133 needs neither a second comparison calculator nor implementation of that broader design.

Recommended bounded prerequisite: in `MonthlyProductComparisonIntentRules.LooksLikeMonthlyProductComparisonQuery`, exclude the canonical company trend phrase when no explicit product-comparison qualifier is present. Preserve explicit product comparisons. Verify the exact requested phrase through V1, V2, and semantic routing. This restores the existing trend alias, without a new capability or Feature 129 redesign.

### 3.6 Authentication

Gateway `PrimaryApiClient.HandleUpdateAsync` and `ConfirmLinkAsync` share `CreateClient` and `TelegramGateway:PrimaryApiKey`; link confirmation uses `/api/v1/telegram/link/confirm`. `TelegramLinkService.ConfirmFromTelegramAsync` passes the authenticated adapter tenant to `ConfirmAsync`, which rejects a challenge with a different tenant. Preserve this validation. Checked-in API `Authentication:ApiKeys:Clients` assigns `telegram-gateway-vps2` (client `22222222-2222-2222-2222-222222222222`) to the existing/default tenant `11111111-1111-1111-1111-111111111111`, also used by `Authentication:OwnedIdentity:DefaultTenantId`. These are repository configuration findings, not verification of deployment secrets or live accounts. Feature 133 must retain the existing credential/client/tenant assignment.

Gateway `PrimaryApiClient.HandleUpdateAsync` sends `X-Api-Key` and `X-Correlation-Id` to `/api/v1/telegram/assistant/updates`. HTTPS is required when enabled. `src/backend/FinancialCopilot.API/Security/ApiKeyAuthenticationHandler.cs` validates active configured clients, tenant/client GUIDs, secret/hash, and allowed path prefixes; it produces an `ApiClient` identity.

`TelegramAssistantController` requires `ApiClientOnly` and authenticated-actor rate limiting. In `Security/ServiceCollectionExtensions.cs`, `NoavaranMonthlyBackfill` instead requires valid actor context, **WebAppUser** authentication mode, and `NoavaranMonthlyBackfillExecute` or `DataSyncManage` permission. A gateway API key cannot invoke the admin operation merely by adding its URL to allowed paths. The separate gateway HMAC controller is not involved in this outbound path.

### 3.7 Billing and conversation behavior

`TelegramAiAssistantAdapter.HandleCoreAsync` requires a positive Telegram user ID, resolves a linked actor, creates/reuses a chat/thread conversation binding, and invokes `IAiQueryOrchestrationService.ExecuteAsync`. A channel must not be impersonated as a Telegram user.

`Infrastructure/ServiceCollectionExtensions.cs` selects V1 or `MicrosoftAgentFrameworkAiQueryOrchestrationService` through the same interface. V1 and V2 reserve/finalize usage; semantic execution also has billing in `Application/AI/Orchestration/SemanticCapabilityExecutionContracts.cs`. Deterministic data does not imply free execution. `Infrastructure/Billing/AiFacadeBillingHook.cs` uses correlation-derived reservation/charge keys. `FinancialCopilot.Billing/Services/BillableAccountResolver.cs` maps API clients to the tenant organization account, users to individual accounts, and rejects actors with neither identity or no configured account.

The orchestration path persists conversation/messages and may load authorized memory. There is no inspected automatic, anonymous, free channel-query mode. Use the existing gateway API-client actor in its unchanged tenant and a fresh conversation per post; never reuse a subscriber's conversation or billing identity. `Infrastructure/Billing/Persistence/BillingRepositories.cs`, `CustomerAccountRepository.FindOrganizationByTenantAsync`, selects the single `CustomerAccounts` row with the authenticated tenant and `AccountType == Organization`. Thus Option A already supports automated billing ownership without another credential, tenant, or execution-identity abstraction; account provisioning/funding remains an operational gate (section 14).

### 3.8 Existing reusable idempotency/persistence

Gateway `GatewayIdempotencyStore.cs` stores confirmed send results in an atomically replaced JSON file; it has no backfill/query checkpoint support. The polling offset is separately persisted.

`TelegramAiAssistantAdapter` replays completed `TelegramProcessedUpdates` for seven days using update-based keys. It inserts the result **after** execution, so its unique index does not prevent concurrent pre-insert execution. `Infrastructure/Authentication/Persistence/AuthPersistenceModels.cs` and `AuthDbContext.cs` define `TelegramProcessedUpdateRow`, unique `IdempotencyKey`, status, JSONB response, conversation, and timestamps. Its nullable `ActorId` has a **user** foreign key; an API-client GUID cannot be put there. These constraints inform section 11.

## 4. Trigger Message Contract

**D1:** Only original `channel_post` with `chat.type == "channel"`, nonzero signed 64-bit chat ID, positive message ID, and nonempty text (or caption when text is absent). Do not require `from`, use author signatures as identity, or process forwarded discussion-group messages as channel posts. Ignore edits and unsupported/media-only content.

Forward update ID, chat ID/type, message ID, date, bounded text/caption, locale `fa-IR`, and correlation through the existing assistant endpoint with a new `ChannelPost` kind. No caller-selected tenant, actor, operation, prompt, or destination override. The backend constructs the query.

The sample must resolve to `دسینا`, `1405`, `5`. Its growth percentages, handles, and fiscal year-end are not inputs to the analysis. Receive/publish permission remains an operational prerequisite. Backend configuration restricts which channels this service client may trigger; this is a small authorization allowlist, not channel registration/UI.

## 5. Recognition and Field Extraction

### 5.1 Monthly report detection

**D2:** A new bounded backend recognizer accepts `فعالیت_ماهانه` (including whitespace/ZWNJ variants) or an explicit monthly production-and-sales report phrase with a one-month reporting-period marker. A period hashtag alone is insufficient. Reject explicitly multi-month-only reports. Do not depend on provider branding, percentage wording, or the whole message. Do not send source text to an LLM.

### 5.2 Symbol extraction

**D3/D4:** Collect hashtag tokens, excluding report-topic and month/year tokens. Normalize candidates with `Domain/Financial/Services/PersianSymbolNormalizer.Normalize`; reuse `ICompanyResolverService.ResolveBySymbolAsync`. The existing resolver has a name-fragment fallback and first-match behavior: require an exact normalized ticker/TSE-symbol match in its returned identity, and verify a unique matching eligible company before authorizing ingestion. Reject ambiguous candidates or inconsistent external company mappings. Do not guess a symbol from percentages, mentions, or arbitrary prose. Multiple hashtags resolving to the same canonical company are acceptable; multiple distinct companies are not.

Use the canonical company plus eligible integer external company ID internally. Do not assume the incoming hashtag is already the exact `NoavaranEligibleCompanies.TseSymbol` used by the admin controller.

### 5.3 Shamsi year/month extraction

Normalize Persian and Arabic-Indic digits to ASCII. Preserve separators while parsing: `PersianSymbolNormalizer` removes punctuation and must not normalize the entire date/message. Existing private helpers in `TelegramAiAssistantAdapter.NormalizeText/NormalizeDigit` demonstrate conventions; a small local recognizer helper is sufficient, with no generic normalization framework.

Map the twelve Persian month names, فروردین through اسفند, to 1–12. Accept `#مرداد_۱۴۰۵` and separator variants. Independently accept a date specifically bound to `دوره ۱ ماهه منتهی به`, validating the full Jalali date using the existing `ShamsiMonthCalculator`/`JalaliDateResolver` conventions and `PersianCalendar`. Ignore dates attached to `سال مالی منتهی به`.

When both month hashtag and reporting-end date exist, require agreement. One unambiguous valid source is sufficient. Never infer the year from the current date. The sample's `1405/05/31` validates the hashtag; `1405/12/29` is ignored as a fiscal-year end.

### 5.4 Validation and fail-safe behavior

Require exactly one eligible company and one period within the existing 1404–1500 / 1–12 range. Invalid dates, conflicting periods, missing year/month, unknown/ambiguous symbols, and relevant-but-unresolved posts produce terminal reason codes and no backfill/query/reply. Irrelevant messages are silently ignored. Bound input size and regex execution time to Telegram text/caption limits; do not add unbounded regex or catalog guessing.

## 6. Proposed Runtime Flow

```mermaid
sequenceDiagram
    participant T as Telegram channel
    participant G as Existing gateway worker
    participant A as Existing assistant API
    participant H as New bounded channel handler
    participant B as Existing direct ingestion
    participant Q as Existing AI orchestration
    T->>G: channel_post
    G->>A: ChannelPost envelope + API key
    A->>H: Authorized service actor + envelope
    H->>H: Recognize monthly report
    alt AutoBackfillEnabled is false
        H-->>G: Empty terminal skip; log disabled reason
    else AutoBackfillEnabled is true
    H->>H: Resolve and claim durable post key
    H->>B: ExecuteDirectAsync(company, year, month)
    B-->>H: Persisted run outcome
    H->>H: Verify fresh requested snapshot; checkpoint
    alt AutoPublishTrendEnabled is false
        H-->>G: BackfillOnlyCompleted; no query or reply
    else AutoPublishTrendEnabled is true and readiness passed
    H->>Q: روند تولید و فروش {canonicalSymbol}
    Q-->>H: AiQueryResponse
    H->>H: Validate result; render and persist parts
    H-->>G: TelegramAssistantResult
    G->>T: Existing sendMessage/sendPhoto to original chat ID
    G->>G: Persist confirmed parts and advance offset
    end
    end
```

The gates apply only to Feature 133 orchestration. With backfill disabled, stop even if publication is enabled; do not query previously persisted data. With publication disabled, stop after successful backfill/readiness, before conversation creation, billing reservation, trend query, rendering, or Telegram sends. Disabled outcomes return empty messages and advance the normal polling offset. Readiness failure still follows section 12 in either backfill-enabled mode.

No second receiver or background workflow is introduced. Normal execution remains request/response; ingestion and query receive a bounded server budget. Lost HTTP responses are recovered by replaying durable outcome, not blindly executing again.

## 7. Component Changes

All paths here are under `src/backend/`.

| Existing component / proposed new component | Responsibility today | Minimal change and placement reason |
|---|---|---|
| `FinancialCopilot.TelegramGateway/TelegramApiClient.cs` | Telegram DTOs, polling, sends | Add channel-post/caption mapping and explicit allowed updates; transport only. |
| `FinancialCopilot.TelegramGateway/TelegramGatewayPollingWorker.cs` | Serial update handling and delivery | Channel branch without `From`; forward envelope; reuse completion/delivery with post-based part keys. |
| `FinancialCopilot.TelegramGateway/PrimaryApiClient.cs` | Authenticated primary API calls and local DTO | Add ChannelPost contract support; same URL and unchanged shared credential/client/tenant for assistant and link-confirmation calls. |
| `FinancialCopilot.Application/Telegram/TelegramAiAssistantContracts.cs` | Update/result contracts | Add ChannelPost kind and optional chat-type field; retain interactive contract compatibility. |
| `FinancialCopilot.API/Controllers/TelegramAssistantController.cs` | API-client authorization and adapter dispatch | Dispatch channel kind before user-link adapter; enforce the backend allowlist for the existing gateway client/unchanged tenant and allowed channels. |
| **New** `FinancialCopilot.Infrastructure/Authentication/TelegramChannelMonthlyReportHandler.cs` | None | Bounded recognition orchestration, canonical resolution, checkpoints, refresh readiness, query invocation, renderer reuse. Keeps financial operations in primary backend. |
| **New** `FinancialCopilot.Application/Telegram/TelegramMonthlyReportRecognizer.cs` | None | Pure deterministic report tokens/period recognition; company resolution remains backend-owned. |
| **New** `FinancialCopilot.Infrastructure/Authentication/TelegramChannelMonthlyReportOptions.cs` | None | Global `AutoBackfillEnabled` and `AutoPublishTrendEnabled` booleans, both false, plus existing proposed service/channel authorization settings; `SectionName = "ChannelMonthlyReports"`. |
| `FinancialCopilot.Infrastructure/Authentication/Persistence/AuthDbContext.cs` / `AuthPersistenceModels.cs` | Processed-update storage | Reuse existing row/schema with feature-prefixed status and versioned JSON; no schema change expected. New handler supplies insert-before-work and conditional stage transitions. |
| `FinancialCopilot.Application/AI/Orchestration/MonthlyProductComparisonIntentRules.cs` | Feature 129 intent gate | Narrow trend exclusion described in 3.5; no financial calculation changes. |
| `FinancialCopilot.Infrastructure/ServiceCollectionExtensions.cs` | DI/options registration | Bind and startup-validate `TelegramChannelMonthlyReportOptions` using `AddOptions` / `BindConfiguration` / `ValidateOnStart`; handler consumes `IOptions<T>.Value` and gates existing calls. Existing ingestion, orchestration, and renderer remain dependencies. |

`GatewayIdempotencyStore`, ingestion services, calculators, billing hook, and renderer require no new business behavior. The new handler is necessary because the existing adapter requires a linked person and has no pre-query refresh lifecycle; it is a branch in Feature 130's integration, not a second gateway architecture.

## 8. Backfill Integration

**D5:** Keep HTTPS from gateway to assistant API; inside the primary API call the existing `ISingleCompanyMonthlyIngestionService.ExecuteDirectAsync`. Do not make a loopback admin HTTP call or distribute a WebAppUser admin token to Linode. This reuses the exact operation behind the requested admin URL after equivalent eligibility/period checks and explicitly granted, narrower service authorization. The admin endpoint and its policy remain unchanged.

**D6:** Require returned run `Completed`, zero errors, completion timestamp, and an actual snapshot for the resolved company and requested year/month through `ICompanyMonthlyActivityTrendSnapshotRepository.GetCompanyTrendAsync(externalCompanyId, year, month, year, month)`. Require matching `ExternalCompanyId`, `ReportYear`, `ReportMonth`, and `CalculatedAtUtc >= refreshStartUtc`, where the backend captures refresh start before direct ingestion. Retain those projected fields, refresh-start timestamp, and ingestion run ID/outcome in the checkpoint. Missing/stale snapshot is terminal `RefreshNotReady`, even after HTTP/service success. Do not wait for unrelated recalculation queues or use a fixed sleep as readiness proof.

`CompanyMonthlyActivityTrendSnapshotContracts.cs` exposes these fields on `CompanyMonthlyActivityTrendSnapshot`; `EfCoreCompanyMonthlyActivityTrendSnapshotRepository.MapToSnapshot` does not expose `SourceReportId` or `CurrentMonthOutputType`. They exist on the persisted `CompanyMonthlyActivityTrendSnapshotRow` and upsert contract, and the calculator selects OutputType=0 reports. OutputType=0 is therefore an existing calculation invariant, not a handler projection check. Explicitly remove `SourceReportId` and `CurrentMonthOutputType` from required runtime checkpoint evidence: no persisted-row read or projection extension is required. Company/period/existence/timestamp evidence is sufficient for this bounded readiness contract; source identity would not establish vendor-revision equivalence or all-output acquisition completeness.

This establishes availability of a refreshed requested period, not that all historical comparisons exist or that the vendor's report is economically identical to the channel post. Preserve existing missing-history warnings. NoDataYet does not authorize old-data analysis. No automatic repeated vendor refresh is added in this feature.

## 9. FinancialCopilot Analysis Invocation

This section applies only when both automation switches are enabled and the triggering post's refresh/readiness checks passed. Backfill-only execution creates no AI conversation and invokes no trend query or AI billing operation.

**D7:** After the routing correction in 3.5, call DI-selected `IAiQueryOrchestrationService.ExecuteAsync` with exactly `روند تولید و فروش {canonicalSymbol}`, the unchanged gateway API-client actor/tenant obtained from authenticated claims and checked against the backend service grant (section 14), API-client authentication mode with no user identity, a stable post correlation, `ExternalUserId=telegram-channel:{chatId}`, and a fresh per-post conversation. Do not populate a semantic frame to avoid billing or directly invoke a calculator.

Persist a new empty conversation through the existing `IConversationRepository` before the query and checkpoint its ID; do not reuse channel-wide conversational state. Use the existing `AiQueryResponse` and renderer. Require a non-null `MonthlyActivityTrendResult` for the canonical company and requested year/month, no clarification/error, and fresh calculation metadata before publishing. Persist immutable rendered parts for replay.

Since the ordinary query uses the latest snapshot, an older triggering period or a concurrent newer report can produce a different period. Fail closed as `PeriodMismatch`; do not silently substitute the latest result. Historical targeting through new orchestration slots is outside this bounded feature. This limitation is explicit rather than an invented existing capability.

## 10. Telegram Channel Reply

**D8:** Send all existing rendered parts to the incoming signed `TelegramChatId`, never a username/handle parsed from the body, sender ID, or discussion group. Publishing into the same channel is sufficient; threading/reply-to syntax is not required. Do not emit typing actions or account-link prompts for channel posts.

Keep source text out of generated content. Existing rendered trend answers do not reproduce the report trigger hashtags; also skip known outbound message IDs from confirmed send records when received again. This prevents ordinary feedback loops without assuming channel `from` identifies the bot. Preserve formatting/media fallback and existing renderer warnings.

## 11. Idempotency

**D9:** Use stable key `channel-monthly:{apiClientId}:{chatId}:{messageId}`. Chat plus message ID is sufficient for a post within one bot/client deployment; update ID is useful for polling/correlation but not post identity. Distinct reposts with different message IDs are distinct events; semantic deduplication across reposts/channels is outside scope.

Reuse `TelegramProcessedUpdateRow` with a versioned Feature 133 JSON payload and prefixed statuses. Insert the unique key **before** external work; losing concurrent requests read the winner. Persist tenant, chat, update and correlation normally; set `TelegramUserId=0`, `ActorId=null` because that foreign key targets users, and store the authenticated API-client ID in the versioned JSON. Interactive validation/replay remains unchanged. Channel replay reads its own schema, never the adapter's private serialized type.

Stages: `Claimed → Refreshing → Refreshed → Querying → Ready` or `Failed`. JSON stores resolved fields, start timestamps, run outcome, conversation ID, reason code, and final rendered messages. Conditional database updates on the previous status acquire each stage; only one request may enter a side-effecting stage. Do not hold a transaction open across provider/AI calls.

Add terminal non-error outcomes `AutomationSkipped` (backfill off) and `BackfillOnlyCompleted` (refresh/readiness passed, publication off), with reason codes and empty messages. Persist recognized post outcomes using the same key; re-enabling a switch does not retroactively execute skipped/completed posts. Evaluate current process options before resuming a stage or replaying a `Ready` response: when either switch is off, never return stored analysis parts for delivery. Preserve successful refresh checkpoints, and never interpret publishing enabled alone as permission to use old data. No new workflow or persistence subsystem is required.

Ordinary duplicate delivery replays `Ready`, resumes an unstarted next stage, or returns empty `InProgress` while the owner runs. The worker retains the offset for in-progress responses. Give work a bounded timeout below the configured primary request budget; use three polling attempts for transient connection failures before a terminal operational outcome so one event cannot block polling indefinitely. Persist the channel attempt count alongside the post state in the existing gateway state file, extending that file's versioned envelope only if needed; keep confirmed-send entries compatible.

A crash in `Refreshing` or `Querying` is **ambiguous**: the external work may have completed. Do not automatically reacquire and repeat it. After the processing deadline, retain a terminal review-required record and advance without publishing. Operator investigation uses run/conversation/correlation data; no scheduler or recovery UI is introduced. This sacrifices automatic recovery in the ambiguous crash window to avoid repeated backfills/charges.

Retain channel post records for 90 days and reject first-time processing of posts older than 30 days. Do not apply the interactive adapter's seven-day replay filter to these records. Post-based send keys `channel-monthly:{clientId}:{chatId}:{messageId}:part:{n}` prevent a different update ID from resending confirmed parts. Persist counts and confirmed message IDs before offset advancement; state-file loss/corruption disables this channel branch until repaired rather than silently starting empty.

Normal acknowledged retries produce one processing result and one confirmed delivery per part. Telegram acceptance followed by a lost response/crash before local persistence can still duplicate a part: Telegram sends and local persistence are not transactional. No claim of absolute exactly-once delivery is made. Preserve Feature 130's practical delivery semantics and document this residual risk.

## 12. Failure Handling

**D10:**

| Failure | Bounded behavior |
|---|---|
| Backfill off / publish off | Non-error `AutoBackfillDisabled`; no acquisition, query or send; advance. |
| Backfill off / publish on | Non-error `AutoPublishRequiresBackfill`; skip entire automated analysis path, including stored response replay; advance. |
| Backfill on / publish off | On successful refresh/readiness, non-error `AutoPublishTrendDisabled` / `BackfillOnlyCompleted`; no query or send; advance. |
| Irrelevant/unparseable message | Ignore irrelevant; record reason for relevant-invalid; advance, no backfill. |
| Unknown/ambiguous symbol | Terminal `SymbolUnresolved`; no guessing, query, or public error spam. |
| Missing/conflicting/invalid period | Terminal `PeriodUnresolved`; no refresh. |
| Backfill Failed/NoDataYet/exception | Persist failure; no analysis or reply; preserve any partially committed data for normal ingestion diagnostics. |
| Completed run but readiness fails | `RefreshNotReady`; do not serve the previous snapshot. |
| Query/billing failure after refresh | Keep refresh checkpoint; terminal failure, no automatic repeat query/backfill. |
| Wrong company/period, clarification, missing result | Suppress reply and record contract failure. |
| Lost primary response | Existing polling retry reads stored state/result; never starts a second claimed flow. |
| Telegram transient send failure | Retry only unsent parts through existing loop, bounded to three attempts for this channel event; honor available RetryAfter without busy looping. |
| Permanent send rejection / retry exhaustion | Record incomplete delivery, advance; no query or refresh retry. |
| Duplicate post | Replay persisted result and skip confirmed parts; terminal outcomes remain terminal. |
| 401/403 | Existing primary authentication health signal/log; channel branch sends no user-link/authentication notice. |

No failure publishes a purported fresh analysis. Terminal failures are operational logs, not financial commentary in the channel.

Configuration skips are expected control outcomes, not parsing, ingestion, analysis, or delivery failures; do not retry them or count them as operational errors.

## 13. Security and Authentication

**D11:** Keep `ApiClientOnly`, HTTPS, path scoping and rate limits. Additionally require the backend-configured existing gateway client/unchanged tenant and allowed channel IDs on the ChannelPost branch; merely possessing any API key permitted to use the interactive endpoint must not grant ingestion authority. Backend configuration is the explicit service grant for this fixed operation. Accept no arbitrary admin command, arbitrary prompt, or arbitrary destination. The channel contract has no tenant, actor, or billing-account selector; extra payload fields or Telegram text cannot override authenticated claims or billing resolution. Reject a different authenticated client/tenant even if it supplies an allowed chat ID.

Reuse `TelegramGateway:PrimaryApiKey`, API `KeyEnvironmentVariable` / `KeySha256`, and deployment secret injection without changing the existing client or tenant assignment. Keep both assistant and link-confirmation paths allowed. The link challenge tenant check remains unchanged; no second channel credential or credential router is introduced. No credentials in code/specs, no admin JWT on Linode, and no weakening of `NoavaranMonthlyBackfill`. Configuration carries only IDs/limits and references secrets. Do not log payload text, headers, bot tokens, provider credentials, or full exception bodies containing them.

## 14. Billing / System Identity

**D12 — Select Option A:** Preserve the shared gateway credential in its existing/default tenant. Transport authentication and automated execution/billing are distinct responsibilities, but need not have different credentials or tenants: the authorized channel branch executes as the authenticated gateway API client, while the unchanged interactive adapter resolves a linked person for interactive queries.

| Responsibility | Concrete identity/account decision |
|---|---|
| Existing Telegram link confirmation | `PrimaryApiClient.ConfirmLinkAsync` continues using the unchanged `TelegramGateway:PrimaryApiKey` and its existing client/tenant. Valid existing-user-tenant challenges still pass `TelegramLinkService.ConfirmAsync` tenant validation. |
| Feature 133 transport | `HandleUpdateAsync` uses that same key on `/api/v1/telegram/assistant/updates`; the backend authenticates the configured gateway client in its unchanged tenant (checked-in IDs in section 3.6). |
| Automated execution | After matching the authenticated client/tenant and allowed channel against trusted backend configuration, pass that API-client actor/tenant to orchestration and conversation creation, with `ApiClientId` populated and no `UserId`. Do not resolve a Telegram subscriber or synthesize another tenant. |
| Automated charges | The organization `CustomerAccount` belonging to the unchanged gateway tenant pays, shared with any other API-client usage billed to that organization; it is not a channel-specific wallet or an individual linked user's account. |
| Backend account derivation | Existing `AiFacadeBillingHook` uses `BillableAccountResolver.ResolveAsync`, whose API-client branch calls `ICustomerAccountRepository.FindOrganizationByTenantAsync(actor.TenantId)`. The repository selects `CustomerAccounts` by trusted tenant and `AccountType == Organization`; the resolver validates tenant equality. Its returned account ID is used by existing reservation/finalization. No configured or caller-supplied account-ID override is needed. |
| Spoofing boundary | API-key authentication obtains client/tenant from server credential configuration, never the event body. Backend service authorization fixes the permitted pair; tenant/account/actor fields are absent from the channel contract and any injected values cannot affect execution or charges. Channel ID supplies authorization/attribution only. |

Option A fits the existing resolver and API-client conversation path. A separate tenant/account is not required for this feature, so Option B's second credential is unnecessary. Option C's alternate execution-identity abstraction is not needed or introduced. Existing Feature 130 link confirmation, linked-user resolution, interactive billing and callbacks retain their current paths; the channel branch alone uses the service actor directly.

Queries are normally reserved/finalized through existing billing. No new free tier, charge bypass, or fabricated Telegram person is introduced. Stable post correlation preserves reservation/charge idempotency; durable stage ownership additionally prevents repeat AI execution. Conversations use the API-client actor in the existing tenant and existing actor/tenant authorization, not a separate-tenant isolation guarantee; never reuse a linked user's conversation or identity. New conversations per post prevent cross-symbol follow-up contamination.

The billing account selection is settled: the existing tenant's organization account. Live account existence and funding have not been verified. Operations must provision that account through existing billing facilities if absent, assign a funding owner, and fund it before publication. `ChannelMonthlyReports:AutoPublishTrendEnabled` must remain false in production until that account is funded. Funding ownership remains deferred; missing account/credit fails closed through existing billing. Backfill-only operation still creates no AI charges.

## 15. Observability

Log stage transitions with correlation, client/tenant, update/chat/message IDs, canonical company, period, sync run ID, conversation ID, elapsed time and fixed reason code. Log delivery part and confirmed Telegram message ID. Reuse existing logs and authentication health; one completion/failure counter by stage/reason is sufficient. No new telemetry subsystem or raw message storage for diagnostics.

For recognized posts, emit one informational outcome: `AutoBackfillDisabled` when both switches are off; `AutoPublishRequiresBackfill` when only publication is on; `AutoPublishTrendDisabled` after backfill-only success; or `AutomatedFlowCompleted` after all reply parts are confirmed by the gateway. Include both effective switch values in backend decision logs. Do not emit both disabled reasons for one post, repeat them on duplicate replay, or log irrelevant posts at information level. A startup warning for publish-on/backfill-off is sufficient to flag the ineffective combination without failing health/startup.

## 16. Deployment and Configuration

**D13:** Deploy the updated existing API and Linode gateway; no new service, inbound route to Linode, or worker deployment is required. Reuse Feature 130's supervisor, outbound networking, persistent offset/idempotency directory and secret environment. Keep a single poller per bot.

Use these two independent keys instead of a single Feature 133 enabled flag:

```json
{
  "ChannelMonthlyReports": {
    "AutoBackfillEnabled": false,
    "AutoPublishTrendEnabled": false
  }
}
```

| AutoBackfillEnabled | AutoPublishTrendEnabled | Behavior for a valid recognized post |
|---|---|---|
| false | false | Skip; no backfill, trend query, or channel analysis reply. |
| true | false | Existing backfill and readiness check only; no trend query or reply. |
| true | true | Existing backfill → readiness → existing trend query → same-channel reply. |
| false | true | Fail-safe skip with `AutoPublishRequiresBackfill`; no acquisition/query/reply or stale-data fallback. |

**Owner/scope:** global primary API application configuration, bound to the proposed `FinancialCopilot.Infrastructure/Authentication/TelegramChannelMonthlyReportOptions.cs` and consumed by `TelegramChannelMonthlyReportHandler`. This follows the gateway's section/PascalCase/IOptions pattern but places authoritative gates beside backend orchestration. Do not duplicate these switches in Linode settings or accept them in request bodies. Existing `TelegramGateway:Enabled` still controls the whole polling service independently; leave it enabled for interactive traffic even when both Feature 133 switches are off.

**Defaults/source:** both booleans default to false when omitted. Supply them in primary API `appsettings.json` / environment-specific appsettings, or override through `ChannelMonthlyReports__AutoBackfillEnabled` and `ChannelMonthlyReports__AutoPublishTrendEnabled` in the existing deployment environment. These booleans are not secrets. Keep the already-proposed authorization/limit settings in this options section; the JSON above shows only the amendment's keys.

**Validation/lifecycle:** use typed boolean binding and startup validation; malformed boolean values fail binding/startup. All four boolean combinations are accepted. Publish-on/backfill-off is effectively disabled, emits a startup warning, and remains fail-safe at runtime rather than preventing unrelated API/gateway traffic. Validate required authorization and processing settings before permitting backfill-enabled execution. Use `IOptions<T>.Value`; changes require primary API process restart (or normal container restart/redeployment when environment configuration is deployment-managed), not a code rebuild. Do not claim hot reload or add `IOptionsMonitor`/dynamic configuration infrastructure. On disablement, pause/drain gateway polling and restart the API before resuming it, so replies already returned to an in-flight gateway request cannot be sent after the cutover. This is not an instantaneous kill switch and cannot retract sent messages. Post replay after restart observes the new gates as specified in section 11.

Retain authorized service client/tenant, channel IDs, processing timeout, three-attempt channel retry bound, 30-day trigger age and 90-day record retention. API enforces authorization/age; gateway enforces transport attempt limits. Extend existing state persistence for channel attempts without invalidating prior confirmed sends. Configure gateway HTTP/proxy timeout above the server processing budget, within its current 120-second cap; measure the direct provider call in staging. A workload exceeding this bound is a rollout blocker, not justification to silently add a queue.

Preserve the shared API-client path scope on both `/api/v1/telegram/assistant/updates` and `/api/v1/telegram/link/confirm`; link confirmation remains required. Keep `TelegramGateway:PrimaryApiKey` and its API-side client/tenant assignment unchanged. The backend service grant authorizes that existing pair and the allowed channels; do not add `ChannelAutomationApiKey`, account-ID overrides, or migrate the credential to a system tenant. Provision/fund the organization account selected for the unchanged tenant before enabling publication; keep `AutoPublishTrendEnabled` false until funded. Schema reuse is expected; no migration is created here. Verify polling channel-post permissions and original-channel publish permission with one staging event. No production actions were performed for this design.

## 17. Testing Strategy

Describe only; implement later. Unit fixtures cover the exact sample, all month names, both digit sets, Arabic/Persian letter variants, ZWNJ, whitespace, fiscal-year date exclusion, hashtag/date conflicts, ambiguous symbols, multi-month reports, captions, edits and generated output loop negatives.

Add a configuration matrix suite for all four rows in section 16 using a valid report fixture. Assert zero backfill calls whenever backfill is off, zero trend queries and zero Telegram outbound calls whenever publication is off, and zero query/send calls for backfill-off/publish-on even when fresh-looking persisted data or a stored Ready response exists. Backfill-on/publish-off must perform acquisition/readiness without conversation creation or AI billing; both-on must preserve freshness/failure guards and same-channel delivery. Verify omitted keys default false, environment overrides bind correctly, malformed booleans fail startup, and publish-on/backfill-off logs its distinct non-error reason. Cover restart/replay with switches disabled and confirm later enablement does not re-execute terminal skipped/backfill-only posts. Tests are described only.

Compatibility coverage must create a valid, unexpired WebToTelegram challenge for an existing/default-tenant user and confirm it through `PrimaryApiClient.ConfirmLinkAsync` using the unchanged interactive credential, asserting successful linking. Repeat with automation disabled and enabled; a different-tenant challenge must still fail the existing tenant check. Alongside it, execute an authorized ChannelPost as the configured gateway API client, verify charges resolve to that same tenant's organization account rather than a linked user's individual account, and inject tenant/account/actor fields and source-text instructions to verify they cannot alter execution identity or charges. A different authenticated client/tenant must be rejected by the service grant.

Readiness fixtures use the actual `CompanyMonthlyActivityTrendSnapshot` read contract: matching company/period and a timestamp equal to or after refresh start pass after a successful run; absent rows, mismatched periods/companies and earlier timestamps fail. Do not require source-report identity or output-type fields absent from that projection.

Integration coverage uses actual PostgreSQL unique keys/conditional transitions: concurrent duplicate claims, lost response after Ready, fresh-snapshot readiness, empty OutputType=0, provider NoDataYet, failure after refresh, stale in-flight record, API-client foreign-key handling, billing/account failure, and fresh conversation isolation. Assert unchanged admin authorization and rejection of unauthorized clients/channels.

Run exact `روند تولید و فروش دسینا` through V1 and V2 plus semantic mode; assert trend output and correct company. Include Feature 129 product-comparison negatives and existing Feature 130 linked-message/callback regressions. Verify wrong/latest period suppression. Gateway tests cover signed channel IDs, absent From, ordered PNG/text parts, post-key replay, retry exhaustion, and corrupt state behavior. One staged real-bot smoke test validates provider readiness, billing attribution, and same-channel output. No full performance framework or tests are created now.

## 18. Acceptance Criteria Candidates

1. The supplied sample resolves to canonical `دسینا`, year 1405 and month 5.
2. Original channel posts without From are accepted; edited posts are ignored.
3. Textual media captions use the same recognition rules.
4. Irrelevant or unresolved monthly posts cause no refresh/query/send.
5. Conflicting reporting dates are rejected and fiscal-year dates are excluded.
6. Only an exactly resolved, uniquely eligible company can trigger ingestion.
7. Unauthorized API clients or channels cannot trigger the new branch.
8. The existing admin endpoint retains its WebAppUser permission policy.
9. With auto-backfill enabled, the handler invokes existing direct ingestion once per eligible claimed post under normal retries.
10. Failed/NoDataYet runs never authorize analysis publication.
11. After completed ingestion, readiness uses `GetCompanyTrendAsync` projected company/year/month, snapshot existence and `CalculatedAtUtc >= refreshStartUtc`; absent, mismatched or stale rows fail without requiring `SourceReportId` or `CurrentMonthOutputType`.
12. The exact query returns the existing trend capability for the correct company in V1/V2.
13. Explicit Feature 129 product-comparison requests retain their existing route.
14. Automated query charges use the organization account returned by `FindOrganizationByTenantAsync` for the unchanged authenticated gateway tenant through existing reservation/finalization, never a linked user's individual account.
15. Each post proceeding to a trend query has an isolated persisted conversation and stable billing correlation.
16. Wrong-company/period or clarification output is suppressed.
17. Existing rendered response parts are sent only to the originating channel ID.
18. Concurrent/repeated post delivery replays durable results without repeat completed work.
19. Confirmed reply parts are skipped during replay, including changed update IDs.
20. Ambiguous in-flight work is not automatically re-executed after a crash.
21. Channel retry exhaustion advances polling and records incomplete delivery.
22. Existing Feature 130 interactive and callback behavior remains covered and unchanged.
23. `ChannelMonthlyReports:AutoBackfillEnabled` and `ChannelMonthlyReports:AutoPublishTrendEnabled` bind independently as global booleans and each defaults to false.
24. Backfill-off/publish-off produces no backfill, trend query, or channel analysis reply.
25. Backfill-on/publish-off completes existing acquisition/readiness without a trend query or Telegram outbound call.
26. Backfill-on/publish-on executes the existing query and same-channel publication only after successful fresh acquisition.
27. Backfill-off/publish-on skips acquisition/query/publication, including stored-response replay, and logs `AutoPublishRequiresBackfill`.
28. Disabled configuration outcomes advance polling without operational-failure retries and remain distinguishable from processing failures.
29. Terminal skipped/backfill-only posts are not re-executed merely because configuration is later enabled.
30. Restarted API replay with either automation switch off returns no stored analysis messages for delivery.
31. An existing-user-tenant Telegram link challenge succeeds through the unchanged interactive credential with automation off or on; a mismatched tenant still fails the existing validation.
32. Feature 133 execution uses the authenticated gateway API-client identity in its unchanged tenant, selected by the trusted backend service grant, with no linked-user identity.
33. Injected tenant/account/actor values in request payloads or Telegram text cannot select or change the automated execution tenant or billing account.

## 19. Files Expected to Change During Implementation

Estimate: existing gateway `TelegramApiClient.cs`, `TelegramGatewayPollingWorker.cs`, `PrimaryApiClient.cs`, `GatewayIdempotencyStore.cs` (versioned channel retry state), `TelegramGatewaySettings.cs`; application `TelegramAiAssistantContracts.cs` and `MonthlyProductComparisonIntentRules.cs`; API `TelegramAssistantController.cs`; infrastructure `ServiceCollectionExtensions.cs`. Existing persistence models/configuration should need no schema edits; validate their reuse in integration tests.

Three new bounded source files are identified in section 7: recognizer, handler and options. Extend existing Telegram gateway/API, monthly trend routing and ingestion tests; add focused parser/state tests. Any later deployment notes belong to Feature 133, not edits to Feature 130. This is a future estimate, not authorization to generate these files in this design task.

The amendment additionally expects non-secret defaults in primary API appsettings and environment examples in Feature 133 deployment notes; binding stays in the existing DI file and runtime gates in the already-proposed handler. No additional component or production file is created by this amendment.

## 20. Risks and Open Decisions

- **Billing funding owner:** account resolution is fixed to the unchanged gateway tenant's organization account, shared with its other API-client usage. Operations must assign the funding owner, provision/fund that account if needed, and accept normal existing charges before enabling publication; no separate channel wallet or tenant isolation is promised. No free system mode is assumed.
- **Measured request budget:** confirm all-output provider refresh plus query fits the existing bounded HTTP deployment. If not, return for a separate narrowly scoped asynchronous design; do not enable a known timeout loop.
- **Routing prerequisite:** the observed Feature 129 collision must be corrected and validated before rollout. The recommendation is the existing trend alias, not new production-series output.
- Ordinary queries select latest data. Older report events or a race with newer data can be deliberately suppressed; historical period pinning is excluded.
- Snapshot timestamp readiness does not prove a provider has published a particular revision. No automatic correction/revision matching is promised.
- External side effects are not atomic with checkpoints. Ambiguous work can be lost pending operator investigation, and ambiguous Telegram sends can duplicate a part. These are explicit bounded-delivery tradeoffs.

## 21. Explicit Non-Goals

No alternate Telegram service, generic orchestration/message platform, new financial engine, LLM report parser, new billing model, arbitrary-message analysis, correction tracking, alert preferences, watchlists, channel-management UI, or redesign of earlier specifications. No previous spec is modified.

## 22. Recommended Implementation Slices

1. **Channel contract and recognition:** gateway mapping, authorized backend branch, deterministic parsing/resolution, global options binding/defaults/startup validation, unchanged shared-credential/tenant authorization and link-confirmation compatibility coverage, and narrow routing regression fix.
2. **Refresh and existing query:** configuration gates, durable stage claims and disabled outcomes, freshness guard, API-client billing/conversation invocation, renderer reuse, and configuration matrix/failure integration coverage.
3. **Delivery and rollout:** post-based send deduplication, disabled replay/no-send checks, bounded channel retry state, deployment configuration/restart procedure, regression checks and staging smoke verification.

## 23. Design Summary

One existing gateway forwards a bounded channel event to its existing primary API boundary; a small authorized backend handler refreshes existing data, runs existing billed FinancialCopilot trend orchestration, and returns existing rendered messages for same-channel delivery. Durable post state and confirmed-part state cover normal retries, with explicit limits for ambiguous crashes and sends. D1–D13 are answered in sections 4–16. This correction amends only this design document. The shared gateway credential retains its existing tenant and Feature 130 link validation; automated queries use that API client and the existing tenant-to-organization billing resolver. Readiness uses only company/period/existence/calculation timestamp from the current trend read projection, without a source-report checkpoint or projection extension.

## Design Review Corrections

- **F1 — Resolved:** Select Option A: preserve the shared interactive/channel credential in its existing tenant, retain Feature 130 link validation, and resolve automated API-client charges to that tenant's organization account through existing backend billing; request input cannot select identity/account.
- **F2 — Resolved:** Require only company, requested year/month, snapshot existence and `CalculatedAtUtc >= refreshStartUtc` through `GetCompanyTrendAsync`; remove unavailable source-report/output-type fields from required checkpoints, with no projection extension.

READY_FOR_DESIGN_REVIEW
