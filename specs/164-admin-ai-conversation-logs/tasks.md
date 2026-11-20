# Tasks: Admin AI Conversation Logs — تاریخچه پرسش و پاسخ کاربران

Status: Gate B: Approved by the owner on 2026-11-20 (with OQ-119 (a): section 9 in scope)

## Feature Folder

`164-admin-ai-conversation-logs` — branch `feature/164-admin-ai-conversation-logs` (worktree
`C:/Source/financial-copilot-f164`, from `develop`; fast-forwarded to `47f7af68` on 2026-11-20 after 162 was merged).

## Before Starting

- [x] This file's `Status:` line records Gate B approval by the owner (see `specs/FEATURE_DEVELOPMENT_WORKFLOW.md`, "Recording Approval").
- [x] The features in `Depends On` are done: 149 is implemented on `develop` (`f200c846`); its open PostgreSQL check is task 7 here.
- [x] Read `user-story.md`, the references it lists, and the relevant `docs/` files.
- [x] No open question in `specs/open-questions.md` blocks this spec: OQ-119 answered (a), section 9 runs.
- [x] Record the baseline: `dotnet test` and frontend checks on this branch before the first change, compared with `specs/test-baseline.md`.
  Baseline on 2026-11-20 at `47f7af68` plus the task 1 files, `dotnet test src/backend/FinancialCopilot.sln -c Release`:
  architecture 23/23 passed; unit 34 failed of 3,992 (the 32 listed in `specs/test-baseline.md` plus
  `NavigatorEvaluationReliabilityTests.HardDeadline_DistinguishesCallerCancellation` and
  `HardDeadline_ReturnsFault_WithoutLosingException`, which pass when run alone: timing under the parallel integration
  run); integration 242 failed, 792 passed, 157 skipped of 1,191 (integration tests are not in the baseline file; the
  failing list is kept for the final comparison). Frontend: `npm run typecheck` 37 errors in the 17 listed files
  (unchanged); `vitest run` 7 tests failed of 578 and 10 files failed of 77 — the 3 PNG `*.browser.test.mjs` scripts
  (files), 4 timeouts while the backend suite ran (`mobile-answers`, `install-card`, `codal-alert-settings`,
  `sidebar-delete-confirmation`), `markdown-message` (16-row quarterly table), `monthly-product-trend-chart` (failed
  trend message) and `fa-terminology` (pre-existing violations in `profile-editor.tsx` and a browser fixture). The
  frontend suite is re-run alone before the final comparison.

## Implementation Tasks

Order: backend contracts and queries (1–4), API (5), PostgreSQL verification and measurement (6–8), frontend (10–13),
docs (14). Section 9 runs only after OQ-119 (a). Each task ends with its tests green.

### 1. Application contracts

- [x] 1.1 `src/backend/FinancialCopilot.Application/Administration/AdminConversationLogContracts.cs`: `AdminConversationLogQuery` (page, pageSize, text, user, fromUtc, toUtc, status), `AdminConversationLogStatus` (8 outcomes + `NoResponse`), `AdminConversationLogChannel` (`Web`, `Telegram`), `AdminConversationLogUser` (id, display name, email, masked mobile), `AdminConversationLogItem`, `AdminConversationLogPage`, `AdminConversationLogUsage`, `AdminConversationLogAnswer` (message id, content, created at, outcome, reason code, usage, `AssistantMessagePayload?`), `AdminConversationLogTurn`, `IAdminConversationLogQueries` (`GetPageAsync`, `GetTurnAsync` with administrator id, actor type, correlation id), and `AdminConversationLogUserSearchTooBroadException`.
- [x] 1.2 `AdminConversationLogQueryRules` (Application, pure): allowed page sizes, text / user length limits, status parsing, `ی/ي ک/ك` normalization and `LIKE` escaping, status derivation from outcome/presence. Unit tests in `tests/FinancialCopilot.UnitTests/AdminConversationLog164Tests.cs`.

### 2. Behaviour-preserving extractions (backend)

- [x] 2.1 Move the content-free `SecurityAdminAuditRow` write of `EfCoreAdminChatHistoryQueries.GetTurnAsync` into `Infrastructure/Administration/AdminContentReadAudit.cs` (action code and target as parameters); 149 calls it with its current values. 149 tests in `AdminManagementEndpointTests` stay green.
- [x] 2.2 Move the contact-mobile protector purpose and unprotect helper out of `OwnProfileStore` into `Infrastructure/Authentication/Profile/ContactMobileProtection.cs`; `OwnProfileStore` uses it. Existing profile tests stay green.
  Done as the purpose constant plus `CreateProtector` (the unprotect call stays at each caller). `OwnProfileIntegrationTests` fail at baseline (10 of them, see "Baseline" below) and fail identically after the change.
- [x] 2.3 Move `MapMessage`, `MapAssistantContent` and the private mapping helpers they call from `AiFacadeController` into `src/backend/FinancialCopilot.API/Controllers/AssistantMessageContentMapper.cs` (internal static); `AiFacadeController` calls it. No response shape change: existing `AiFacadeEndpointTests` / `AiFacadeV2EndpointTests` stay green, plus a test that serializes one stored payload of each structured type through the chat messages endpoint before and after (golden JSON in the test).
  **Deviation (implementation detail, no behaviour change):** the class is `AiFacadeHttpMapper` (`public static`, because the integration tests compare against it and the API has no `InternalsVisibleTo`) and holds every response-mapping helper of `AiFacadeController` (`MapQueryResponse` … `MapConfidenceScore`); `TryMapAnalysisOptions` (request mapping) stays in the controller, which uses `using static`. The golden-JSON test was replaced by a mechanical check that the moved text is identical apart from `private` → `internal`/`public`, plus the AC8 equality test in 5.3; `AiFacade*` and `OwnProfile*` endpoint tests fail exactly as at baseline (14 baseline failures, none new, none fixed).

### 3. List query (Infrastructure)

- [x] 3.1 `Infrastructure/Administration/EfCoreAdminConversationLogQueries.cs` with `ConversationDbContext`, `AuthDbContext`, `TahlilAppProfileProtector`, `ContactMobileProtection`, `TimeProvider`.
- [x] 3.2 User filter resolution in `AuthDbContext` (FR 7): GUID; mobile (normalize with the domain `IranianMobile`, narrow by `MobileLast4` / `ContactMobileLast4`, decrypt, compare); else substring on email / names. Cap 200 → `AdminConversationLogUserSearchTooBroadException`.
- [x] 3.3 Count and page SQL with `ConversationDbContext.Database.SqlQuery<T>` (interpolated, parameterized): User messages joined to the tenant's conversations; `ActorId = ANY(@ids)`; date range; `translate(lower(...))` text match with escaped pattern; `LEFT JOIN LATERAL` next User message and first Assistant message (`a."CreatedAt" >= q."CreatedAt" AND (n IS NULL OR a."CreatedAt" < n."CreatedAt")`, ordered `("CreatedAt", "Id")`); status expression with `jsonb_typeof` guard; `ORDER BY q."CreatedAt" DESC, q."Id" DESC LIMIT/OFFSET`; previews via `left(…)`.
  **Deviation:** `SqlQueryRaw<T>` with explicit `NpgsqlParameter`s instead of interpolated `SqlQuery<T>`: the SQL is composed from constant fragments (filter conditions are constants, values are parameters), which the interpolated API cannot express. EF Core formats raw SQL with `string.Format`, so the SQL contains no braces (the outcome regex uses `length(...) <= 9`).
- [x] 3.4 Identities for the page in one `AuthDbContext` query (users + profiles, masked mobile as `****1234`), Telegram bindings in one query; assemble items.
  Masked mobile is `•••` + last four digits (`ContactMobileLast4` before `MobileLast4`), as the design's "masked to the last four digits"; the domain `ProfileMasking.MaskMobile` needs the decrypted number, which the page does not decrypt.
- [x] 3.5 Register `IAdminConversationLogQueries` in `ServiceCollectionExtensions` next to `IAdminChatHistoryQueries`.

### 4. Detail query (Infrastructure)

- [x] 4.1 Anchor lookup scoped to tenant (`User` role, joined conversation); `null` → 404.
- [x] 4.2 `AdminContentReadAudit` with `admin.ai-conversation-log.turn-read` before reading content; failure propagates.
- [x] 4.3 Answers of the turn with the same boundary rule as 3.3 ordered `(CreatedAt, Id)`; deserialize `AssistantPayloadJson` to `AssistantMessagePayload` (invalid JSON → `null`, logged by message id only); usage fields from `payload.Usage` without `EstimatedCost` / `PricingPolicyVersion`; user and channel as in 3.4.

### 5. API

- [x] 5.1 `src/backend/FinancialCopilot.API/Controllers/AdminAiConversationLogsController.cs`, route `api/v1/admin/ai-conversation-logs`, `[ApiController]`, `[Authorize]`, `AdminUsersRead` on both actions; validation (FR 5–9) → `ValidationProblem`; `user_search_too_broad` → `400` problem with that code.
  A blank (empty or whitespace-only) `q` / `user` is bound as absent by ASP.NET and applies no filter; the 1–200 limit applies to a given value (endpoint test `An_overlong_search_is_rejected_and_a_blank_one_means_no_search`).
- [x] 5.2 HTTP response records (camelCase via the existing JSON options); detail answers carry `assistantContent` from `AssistantMessageContentMapper`.
  The mapper is `AiFacadeHttpMapper` (see 2.3).
- [x] 5.3 Endpoint tests `tests/FinancialCopilot.IntegrationTests/AdminConversationLog164EndpointTests.cs` on the existing in-memory admin host with a stub `IAdminConversationLogQueries` (the real SQL needs PostgreSQL): 401, 403, validation cases, 404, response shape, `assistantContent` equals the chat mapper's output, excluded fields absent.

### 6. PostgreSQL tests of the real queries

- [x] 6.1 Add `CreateConversationContext()` to `PostgreSqlTestDatabase`; migrate `ConversationDbContext` and `AuthDbContext` in the test database.
- [x] 6.2 `tests/FinancialCopilot.IntegrationTests/AdminConversationLog164PostgreSqlTests.cs` (`[SkippableFact]`, `PostgreSqlIntegrationCollection`): AC2–AC9 and AC14 against real SQL, including random-id same-timestamp pairs (AC3), multi-exchange and concurrent conversations, filtered-out next question, every filter, tenant isolation, audit row content and fail-closed (interceptor makes the audit insert fail), round-trip count with a `DbCommandInterceptor`.
  Ran against the Testcontainers PostgreSQL 17 on 2026-11-20: 4 of 4 passed (not skipped).

### 7. Measurement (no production access by the agent)

- [x] 7.1 Seed a synthetic dataset in the local PostgreSQL container (about 50,000 conversations, 500,000 messages over 180 days, 5,000 users) with a script under `specs/164-admin-ai-conversation-logs/evidence/`; run `EXPLAIN (ANALYZE, BUFFERS)` for: default 30-day page, text search, status filter, user filter, page 50; record plans and timings in `evidence/`.
  Done 2026-11-20 on PostgreSQL 17.12 (Docker, defaults): 50,000 conversations, 493,945 messages; scripts and plans in `evidence/` (`seed-synthetic.sql`, `explain-queries.sql`, `measurement-2026-11-20.md`, `query-shape-variants.sql`). Default 30-day page 195 ms before the index. **Finding:** a status filter exceeds the 5 s statement timeout on this volume (`Failed` count 8.8 s) — recorded as **OQ-120**, not decided.
- [~] 7.2 Hand the owner the same `EXPLAIN` statements as a read-only script for production; record the owner's results when available. Not blocking for tasks 10–14.
  `evidence/production-explain-readonly.sql` written (read-only, placeholders marked); waiting for the owner's production run.

### 8. Conditional index

- [x] 8.1 Decision rule: add the index if the default 30-day page exceeds 300 ms on the synthetic set, or a plan shows a sequential scan plus sort of `Messages` for it. Record the decision in `evidence/`.
  `evidence/index-decision.md`: time condition not met (195 ms), plan condition met (parallel seq scan of `Messages` plus sort) → index added.
- [x] 8.2 If added: `ConversationDbContext` migration with `migrationBuilder.Sql("CREATE INDEX CONCURRENTLY IF NOT EXISTS \"IX_Messages_User_CreatedAt_Id\" …", suppressTransaction: true)` and matching `DROP INDEX CONCURRENTLY IF EXISTS` in `Down`; model configuration so `has-pending-model-changes` is clean; re-run 7.1 and record the improvement.
  Migration `20261120193227_AddMessagesUserCreatedAtIndex` (`CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_Messages_User_CreatedAt_Id" ON "Messages" ("CreatedAt", "Id") INCLUDE ("ConversationId") WHERE "Role" = 'User'`, `suppressTransaction: true`, concurrent drop in `Down`); model entry in `MessageRowConfiguration`; `has-pending-model-changes` clean. Together with it (no schema change, inside the approved design) the page query without a status filter now chooses the page of turns first and looks up answers for those rows only; measured with the index: default page 6 ms, user filter 10 ms, page 50 9 ms, text search 286 ms.

### 9. BUG-037 fix for Feature 149 (only after OQ-119 (a))

- [x] 9.1 Follow `specs/149-admin-customer-chat-history/changes/fix-same-timestamp-turn-pairing/tasks.md`: 149 pairing ordered `(CreatedAt, role rank, Id)` in both SQL and in-memory grouping; regression test with random ids and equal timestamps; existing 149 tests green.
  Regression test `AdminManagementEndpointTests.CustomerChatHistory_PairsSameTimestampAnswersWithTheirOwnQuestion` failed before the fix ("answer two" expected, `null` actual) and passes after; the other 149 tests pass. The in-memory grouping and the SQL predicate now use `answer.CreatedAt >= question.CreatedAt` and `nextQuestion.CreatedAt <= answer.CreatedAt` (User before Assistant at the same instant).

### 10. Frontend extractions (behaviour-preserving)

- [x] 10.1 Move `mapAssistantBlock`, `normalizeRenderableTable` and `isMonthlySalesTechnicalUnitNote` from `src/frontend/src/lib/chat.functions.ts` into the client-safe `src/frontend/src/lib/chat/assistant-block-mapper.ts`; `chat.functions.ts` imports them. Regression test `lib/chat/__tests__/assistant-block-mapper.test.ts` on representative payloads.
  Pure move (verified textually identical except `export`); `AssistantContentResponse` exported as a type. 6 regression tests.
- [x] 10.2 Add `readOnly?: boolean` to `AssistantBlock` in `components/app/message-list.tsx` and pass it to `ScannerResultTable` and `SymbolSnapshotCard`; when set, omit `AnswerActions`, `OrchestrationDiagnosticsPanel`, suggested questions and actions, `AnalysisOfferCard`, `FollowSymbolButton`, regenerate and paging callbacks. Export `ReadOnlyAssistantAnswer({ block })`. The interactive default is unchanged; tests for both modes.
  `showFollow` prop on `ScannerResultTable` and `SymbolSnapshotCard`; paging buttons stay visible but disabled without callbacks. Tests: `read-only-assistant-answer.test.tsx` (both modes).
- [x] 10.3 Extract `DateFilter` from `routes/_app.disclosures.tsx` into `components/app/jalali-date-filter.tsx`; the disclosures route uses it unchanged; add `tehranDayRangeToUtc` helper (Asia/Tehran via `Intl`) in `lib/format/` with tests.
  Component `JalaliDateFilter`; helpers `tehranDayStartUtc`, `tehranDayRangeToUtc`, `tehranToday`, `lastTehranDays` in `lib/format/tehran-day-range.ts` with 6 tests (incl. a pre-2022 daylight-saving day).

### 11. Admin client

- [x] 11.1 Types and `adminApi.aiConversationLogs(query)` / `adminApi.aiConversationLogTurn(turnId)` in `src/frontend/src/integrations/financial-copilot/admin-client.ts` (query string as `userDirectory`).

### 12. Admin page

- [x] 12.1 `components/admin/ai-conversation-logs-panel.tsx`: filter form (text, user, Jalali date range default last 30 Tehran days, status, page size), table, status and channel badges, Jalali Tehran times (`formatDisclosureReceiptDate`), pagination text, loading / empty / error (with correlation id), stale-response guard.
  **Deviation (UI structure only):** `Panel`, `Message` and `Loading` moved unchanged from `admin-page.tsx` into `components/admin/admin-ui.tsx` and `showError` into `admin-errors.ts` (Fast Refresh: component files export only components), so the panel shares them; labels and the Tehran date format in `ai-conversation-log-format.ts`, the status badge in `ai-conversation-log-labels.tsx`. The page-size select is in the filter form and applies immediately.
- [x] 12.2 `components/admin/ai-conversation-log-drawer.tsx`: RTL `Sheet`; question via `MarkdownMessage`; each answer via `mapAssistantBlock` + `ReadOnlyAssistantAnswer`; user, conversation, channel, times, usage, outcome and reason code; «ذخیره نشده» for session id and question-received time; detail loading / error states.
- [x] 12.3 `admin-page.tsx`: `AdminTab` `ai-logs`, gated `navigation` entry with `MessagesSquare`, panel switch.

### 13. Frontend tests

- [x] 13.1 `components/admin/__tests__/ai-conversation-logs-panel.test.tsx`: tab gating, default 30-day query, paging and page-size calls, filter resets page, status labels, loading / empty / error, stale response ignored.
- [x] 13.2 `components/admin/__tests__/ai-conversation-log-drawer.test.tsx`: quarterly table, monthly trend chart and scanner table payloads render read-only with no interactive controls; Jalali times; not-stored labels.

### 14. Documentation

- [x] 14.1 `docs/architecture.md`: admin read path for conversation logs (cross-context user lookup, raw SQL location, audit).
- [x] 14.2 `specs/README.md` entry updated; `CHANGELOG.md` `[Unreleased]` Persian line, for example «صفحهٔ تاریخچهٔ پرسش و پاسخ کاربران در پنل مدیریت، با جست‌وجو، فیلتر و نمایش پاسخ‌ها همان‌طور که کاربر دیده است.»

## Tests

- [ ] Unit: `AdminConversationLog164Tests` (rules, normalization, escaping, status derivation).
- [ ] Integration: `AdminConversationLog164EndpointTests` (in-memory host, stub queries), `AdminConversationLog164PostgreSqlTests` (real SQL), golden-JSON chat mapper test, existing `AdminManagementEndpointTests` and chat endpoint tests green.
- [ ] Architecture: existing `CleanArchitectureDependencyTests` green (Application contracts reference no Infrastructure or API type).
- [ ] Frontend (Vitest): mapper, renderer modes, date helper, panel, drawer; existing chat and disclosures tests green.

## Definition Of Done

- [ ] `dotnet test src/backend/FinancialCopilot.sln --configuration Release` green: no failure outside `specs/test-baseline.md`. PostgreSQL tests ran (not skipped) at least once locally; if Docker was unavailable, say so.
- [ ] For database work: `dotnet ef migrations has-pending-model-changes --context ConversationDbContext` reports no pending model changes (only if task 8.2 ran).
- [ ] In `src/frontend`: `npm run lint`, `npm run test` and `npm run build` green, and `npm run typecheck` adds no type error beyond `specs/test-baseline.md`.
- [ ] Acceptance criteria in `user-story.md` all checked.
- [ ] Reference docs updated: `specs/README.md` index entry, `docs/architecture.md`.
- [ ] User-visible change: a Persian, user-facing line under `## [Unreleased]` in `CHANGELOG.md`.
- [ ] New questions → `specs/open-questions.md`; deferred work → `specs/backlog.md`; defects found → `specs/bugs.md`.
- [ ] Nothing pushed, merged or deployed; final report lists verified results, measurements and remaining items.

## Risks

| Risk | Mitigation |
|---|---|
| Raw SQL is untestable on the in-memory host | Endpoint tests use a stub; the SQL is covered by PostgreSQL tests (section 6). |
| Status filter parses each candidate payload as jsonb | Bounded by the date window; measured in 7.1; `jsonb_typeof` guard against non-number values. |
| Extractions touch chat code (backend mapper, frontend mapper and renderer) | Pure moves first, golden JSON and renderer tests before and after; interactive mode is the default. |
| Mobile search decrypts profile values | Only profiles matching the last four digits; values never leave the query class; display stays masked. |
| Index build on a large `Messages` table | Only if measured; `CONCURRENTLY` outside a transaction so chat writes are not blocked. |
