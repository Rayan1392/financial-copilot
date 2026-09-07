# Feature 133 — Tasks

## 1. Implementation Strategy

Implement the approved bounded branch in three slices. Reuse the existing Telegram gateway, assistant API boundary, direct ingestion service, trend projection/query, orchestration billing hook, renderer, and durable persistence. Do not add a workflow engine, queue, schema, or alternate identity. Each task below names its objective, likely components, required behavior and verification scope.

## 2. Slice 1 — Telegram Trigger, Recognition, and Backend Entry

### T133-1 — Extend the existing gateway channel-post contract

Objective: map original Telegram channel posts and captions through the existing poller.

Components: `FinancialCopilot.TelegramGateway/TelegramApiClient.cs`, `TelegramGatewayPollingWorker.cs`, `PrimaryApiClient.cs`, gateway DTO/settings and `TelegramAssistantContracts.cs`.

Behavior: request/map `message`, `callback_query`, and `channel_post`; ignore edited posts and unsupported media-only shapes; accept channel posts without `From`; preserve signed chat ID, message ID, text/caption, locale, correlation, and same-channel destination; retain existing offsets, ordinary messages, callbacks, multipart sends, and shared credential.

Tests: channel-post DTO/caption fixtures, absent-From handling, allowed-update behavior, signed IDs, edit/media negatives, and Feature 130 ordinary/callback regressions.

ACs: AC1–AC4, AC25–AC26, AC33.

### T133-2 — Implement bounded report recognition and field extraction

Objective: deterministically extract one supported monthly report, canonical company, Shamsi year, and month.

Components: new pure recognizer under `FinancialCopilot.Application/Telegram`, `PersianSymbolNormalizer`, `ShamsiMonthCalculator`/`JalaliDateResolver`, and `ICompanyResolverService` boundary.

Behavior: support approved markers, captions, month names, numeric dates, digit/letter/ZWNJ normalization, fiscal-date exclusion, disagreement rejection, bounded input/regex work, exact ticker and unique eligible-company resolution; never infer or guess.

Tests: sample 1405/5, all month names and digit sets, variants, fiscal dates, conflicts, multi-month reports, unknown/ambiguous symbols, captions, irrelevant and malformed inputs.

ACs: AC5–AC10, AC32.

### T133-3 — Add authorized backend entry and explicit options

Objective: route the new channel kind to a narrow Feature 133 handler and bind its two gates.

Components: `TelegramAssistantController.cs`, `ServiceCollectionExtensions.cs`, new channel handler/options, authentication policies and existing API configuration.

Behavior: require `ApiClientOnly`, authenticated gateway client/unchanged tenant, allowed channel IDs and fixed operation; reject body identity selectors; bind `ChannelMonthlyReports:AutoBackfillEnabled` and `AutoPublishTrendEnabled` as global booleans defaulting false with startup validation; leave admin backfill policy unchanged.

Tests: authorized/unauthorized client and channel tests, injected tenant/account/actor rejection, defaults, environment binding, malformed booleans, and admin authorization regression.

ACs: AC11, AC25–AC28, AC33.

## 3. Slice 2 — Refresh, Readiness, and Existing Trend Execution

### T133-4 — Integrate direct refresh and durable stage checkpoints

Objective: run the existing company/month direct ingestion once for a claimed post.

Components: new `TelegramChannelMonthlyReportHandler`, `AuthDbContext`/`AuthPersistenceModels`, `ISingleCompanyMonthlyIngestionService`, and existing run contracts.

Behavior: claim stable `channel-monthly:{apiClientId}:{chatId}:{messageId}` before external work; after authentication, recognition/resolution, and claim, read the already-bound `ChannelMonthlyReports:AutoBackfillEnabled` and `AutoPublishTrendEnabled` options and own the four-state runtime decision. Evaluate gates before downstream work: (1) backfill off/publish off records `AutoBackfillDisabled` and calls no ingestion, readiness, query, billing, conversation, renderer, or send; (2) backfill on/publish off calls direct refresh then readiness and records `BackfillOnlyCompleted`/`AutoPublishTrendDisabled` without query, billing, conversation, renderer, or send; (3) both on calls direct refresh, readiness, existing trend capability, billing/conversation, validation/rendering, and same-channel delivery in that order; (4) backfill off/publish on records `AutoPublishRequiresBackfill` without backfill or use/replay of stored financial data and without readiness, query, billing, conversation, renderer, or send. Capture `refreshStartUtc`; call `ExecuteDirectAsync` with resolved integer company and requested period; persist conditional stages and refresh outcome; treat Failed, NoDataYet, exceptions and errors as non-publishable. Gate decisions and terminal configuration outcomes are owned by this handler on initial execution and resume/replay; later option changes do not re-execute terminal skipped/backfill-only posts.

Tests: claim concurrency, direct call arguments, failed/no-data/exception outcomes, stage transitions, and no duplicate ingestion.

ACs: AC12–AC17, AC31–AC32.

### T133-5 — Implement projection-based requested-period readiness

Objective: prevent stale pre-refresh data from reaching publication.

Components: `ICompanyMonthlyActivityTrendSnapshotRepository.GetCompanyTrendAsync`, `CompanyMonthlyActivityTrendSnapshot` contract, `EfCoreCompanyMonthlyActivityTrendSnapshotRepository`, and handler.

Behavior: query the exact requested company/year/month range; require a snapshot, matching fields, and `CalculatedAtUtc >= refreshStartUtc`; do not access or require `SourceReportId` or unavailable output-type projection fields.

Tests: actual projection fixtures for matching/fresh, equal timestamp, missing, mismatched, and stale snapshots; completed run with absent snapshot; verify direct normalization recalculates inline.

ACs: AC18–AC20, AC32.

### T133-6 — Reuse trend orchestration and apply the narrow routing fix

Objective: invoke and validate the existing trend capability after readiness.

Components: `MonthlyProductComparisonIntentRules`, V1 `AiQueryOrchestrationService`, V2 workflow/semantic executor, `AiQueryRequest`, billing hook, conversation repositories, and existing renderer.

Behavior: construct exactly `روند تولید و فروش {canonicalSymbol}`; exclude only the canonical trend phrase from product comparison while preserving explicit comparisons; pass API-client actor/tenant, no user ID, stable correlation and fresh per-post conversation; require expected company/period and no clarification/error; reuse existing billed query and renderer.

Tests: V1/V2/semantic trend routing, Feature 129 explicit comparison positives/negatives, wrong-period/company suppression, isolated conversation, organization billing and insufficient-credit suppression.

ACs: AC14, AC21–AC24, AC29–AC30, AC32.

## 4. Slice 3 — Idempotency, Delivery, Configuration Matrix, and Integration Verification

### T133-7 — Complete post persistence, replay, and outbound-part idempotency

Objective: make normal duplicate delivery safe using approved state mechanisms.

Components: `TelegramProcessedUpdateRow` persistence, versioned Feature 133 JSON state, `GatewayIdempotencyStore`, gateway polling delivery, and handler replay logic.

Behavior: persist insert-before-work claims, terminal disabled outcomes, refresh/query checkpoints and immutable rendered parts; enforce first-time trigger-age rejection for posts older than 30 days; retain channel records for 90 days; enforce a bounded processing deadline below the primary request budget and transition expired `Refreshing`/`Querying` work to terminal review-required without re-execution; replay only eligible stored outcomes under the current handler gates; skip confirmed parts including changed update IDs; state-file corruption or unavailability disables the channel branch until repaired; fail closed on ambiguous work and document no distributed exactly-once claim.

Tests: duplicate/concurrent posts, first-time post older than 30 days, 90-day retention/pruning, lost response after Ready, restart replay, disabled replay and later-enable non-replay, expired in-flight work to review-required without re-execution, confirmed-part deduplication, corrupt/unavailable state fail-closed behavior, and no repeat completed work.

ACs: AC12, AC15, AC31–AC32.

### T133-8 — Implement same-channel delivery and bounded retries

Objective: deliver existing rendered text/PNG parts to the originating channel safely.

Components: `TelegramGatewayPollingWorker.SendMessagesAsync`, `TelegramApiClient`, channel post correlation and retry state.

Behavior: send only to signed originating chat ID, preserve part order and formatting/media fallback, retry only unsent parts up to the approved three attempts with `RetryAfter`, record permanent rejection/exhaustion, advance polling, and never publish financial error commentary.

Tests: same-channel destination, multipart ordering, transient/permanent failures, retry exhaustion, changed update IDs and send suppression.

ACs: AC4, AC23, AC31–AC32.

### T133-9 — Verify the complete configuration and compatibility matrix

Objective: verify the handler-owned four-state runtime decision and every downstream suppression effect, plus compatibility.

Components: options, handler, direct ingestion, readiness, orchestration/billing, conversation, gateway send, and Feature 130 link flow.

Behavior: verify, but do not implement, the handler's already-owned gate evaluation and exact order for all four option combinations, defaults, startup binding, terminal outcomes, replay after restart, account funding gate, unchanged credential/tenant link confirmation, and mismatched-tenant rejection. Assert disabled outcomes remain terminal and are not replayed after later configuration changes.

Tests: matrix in section 6 below, existing link challenge regression with automation off/on, funding/insufficient-capacity failure, and identity spoof-prevention.

ACs: AC11–AC15, AC25–AC30, AC33.

### T133-10 — Run end-to-end integration and rollout verification

Objective: validate the approved three-slice behavior and operational prerequisites without implementing deployment changes here.

Components: PostgreSQL integration fixtures, V1/V2/semantic paths, gateway/API test seams, existing configuration/deployment documentation locations.

Behavior: exercise successful full path, all suppression paths, concurrency, routing, billing identity, same-channel output, restart/drain semantics, and production default-off/funding gate; document required deployment configuration in the implementation change later.

Tests: successful end-to-end orchestration, all rows in section 6, Feature 130 tests, relevant routing regressions, billing/identity tests, and no stale publication assertion.

ACs: AC1–AC33.

## 5. Acceptance Criteria Traceability

| AC | Task(s) |
|---|---|
| AC1 | T133-1 |
| AC2 | T133-1, T133-2 |
| AC3 | T133-1, T133-2, T133-8 |
| AC4 | T133-1, T133-8 |
| AC5 | T133-2 |
| AC6 | T133-2 |
| AC7 | T133-2 |
| AC8 | T133-2 |
| AC9 | T133-2 |
| AC10 | T133-2 |
| AC11 | T133-3, T133-9 |
| AC12 | T133-4, T133-7, T133-9 |
| AC13 | T133-4, T133-6, T133-9 |
| AC14 | T133-4, T133-5, T133-6, T133-10 |
| AC15 | T133-4, T133-7, T133-9 |
| AC16 | T133-4 |
| AC17 | T133-4, T133-10 |
| AC18 | T133-5 |
| AC19 | T133-5, T133-10 |
| AC20 | T133-5 |
| AC21 | T133-6 |
| AC22 | T133-6, T133-10 |
| AC23 | T133-6, T133-8 |
| AC24 | T133-6 |
| AC25 | T133-1, T133-3, T133-9 |
| AC26 | T133-1, T133-9 |
| AC27 | T133-3, T133-6, T133-9 |
| AC28 | T133-3, T133-9 |
| AC29 | T133-6, T133-9 |
| AC30 | T133-6, T133-9 |
| AC31 | T133-4, T133-7, T133-8 |
| AC32 | T133-2, T133-4, T133-5, T133-6, T133-7, T133-8, T133-10 |
| AC33 | T133-1, T133-3, T133-9 |

## 6. Test Matrix

| Scenario | Backfill | Publish | Backfill call | Readiness | Query | Billing | Conversation | Render/Send |
|---|---:|---:|---:|---:|---:|---:|---:|---|
| Valid recognized post | Off | Off | 0 | 0 | 0 | 0 | 0 | 0 |
| Valid recognized post | On | Off | 1 | 1 | 0 | 0 | 0 | 0 |
| Valid recognized post | On | On | 1 | 1 | 1 | Existing billed path | 1 | Yes, ordered same-channel parts |
| Valid recognized post | Off | On | 0 | 0 | 0 | 0 | 0 | 0; `AutoPublishRequiresBackfill` |
| Irrelevant post | either | either | 0 | 0 | 0 | 0 | 0 | 0 |
| Malformed monthly report | either | either | 0 | 0 | 0 | 0 | 0 | 0 |
| Unresolved/ambiguous company | either | either | 0 | 0 | 0 | 0 | 0 | 0 |
| Refresh failure/NoDataYet | On | On | Attempted direct refresh; failure persisted | 0 | 0 | 0 | 0 | 0 |
| Stale readiness snapshot | On | On | 1 | 1 (fails) | 0 | 0 | 0 | 0 |
| Duplicate update/post | On | On | 0 for completed work | 0 for completed readiness | 0 for completed query | 0 duplicate charge | Reuse persisted result | Confirmed parts skipped |
| Query failure or wrong result | On | On | 1 | 1 | 1 (fails/suppressed) | Existing failure/release behavior | 0 or existing isolated failure state | 0 |
| Telegram transient send failure | On | On | 1 | 1 | 1 | No repeat charge | Persisted conversation | Retry unsent parts only, bounded |
| Successful full path | On | On | 1 | 1 | 1 | Organization account charged | 1 isolated post conversation | Yes, same-channel multipart response |
| Feature 130 link confirmation | either | either | N/A | N/A | N/A | Existing path | Existing linked-user path | Existing behavior |
| Tenant/account override attempt | On | On | 1 using authenticated claims | 1 | Existing query identity | Resolver-selected organization only | API-client actor only | Authorized originating channel only |
| First-time post older than 30 days | either | either | 0 | 0 | 0 | 0 | 0 | 0; terminal age rejection |
| Expired in-flight work | either | either | 0 after deadline | 0 | 0 | 0 | 0 | 0; terminal review-required, no re-execution |
| Corrupt/unavailable channel state | either | either | 0 | 0 | 0 | 0 | 0 | 0; channel branch disabled until repaired |

## 7. Implementation Boundaries

Feature 133 must not introduce a new Telegram bot, Telegram gateway service, database, message broker, financial calculation engine, generic semantic routing framework, billing subsystem, identity subsystem, tenant-selection API, generic workflow engine, per-channel configuration framework, or LLM parser for deterministic provider posts. It must not call the admin endpoint with a distributed WebAppUser token, duplicate monthly calculations, require `SourceReportId`, or alter previous feature specifications.

## 8. Definition of Done

- All T133 tasks are complete and every AC1–AC33 is verified.
- Focused Feature 133 tests pass, including all matrix and suppression rows.
- Existing Feature 130 Telegram, account-linking, callback, and multipart tests pass.
- V1, V2, semantic trend routing and explicit Feature 129 comparison regressions pass.
- Billing organization-resolution, insufficient-capacity, identity spoof-prevention, and conversation isolation tests pass.
- No stale snapshot or failed-refresh path can publish analysis.
- No account-linking regression exists under automation off or on.
- No out-of-scope architecture is introduced and both settings remain default-off.
- Production publication is not enabled until the organization account is provisioned and funded and the bounded request budget is validated.
- Required implementation deployment notes are updated during implementation; an `ImplementationReport` is created later in the implementation phase, not by this specification task.

## Review Corrections

F1 â€” Required trigger-age, retention, timeout, and fail-closed state protections have no concrete implementation owner â€” RESOLVED. T133-7 now owns the approved age, retention, deadline, review-required, and corrupt-state fail-closed behavior; its matrix rows verify the negative effects.

F2 â€” Runtime ownership of the independent four-state gates is underspecified â€” RESOLVED. T133-4 assigns gate evaluation, exact ordering, terminal outcomes, and replay enforcement to `TelegramChannelMonthlyReportHandler`; T133-9 remains verification-only.

TASKS_READY_FOR_REVIEW
