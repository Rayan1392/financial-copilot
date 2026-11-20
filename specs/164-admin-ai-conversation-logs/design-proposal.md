# Feature 164 — Admin AI Conversation Logs (design proposal)

**Status: Gate A: Approved by the owner on 2026-11-20**, with the agent's recommendations for OQ-115 … OQ-118. Gate B (implementation plan in `tasks.md`): Approved by the owner on 2026-11-20.

Planning note (2026-11-20): pairing orders `(CreatedAt, role rank, Id)` instead of 149's `(CreatedAt, Id)` because of BUG-037; whether 149 itself is fixed is OQ-119.

## Feature Folder   `164-admin-ai-conversation-logs`
## Layers / Area    backend · frontend · database (one optional index only) · Administration (Conversations read)
## Priority         to be set by the owner
## Complexity       M
## Depends On       149-admin-customer-chat-history (implemented; its PostgreSQL query-plan check is still open)
## References       `specs/149-admin-customer-chat-history/design.md`, `docs/architecture.md`, `src/backend/FinancialCopilot.Infrastructure/Administration/EfCoreAdminChatHistoryQueries.cs`, `src/backend/FinancialCopilot.Infrastructure/Conversations/Persistence/`, `src/backend/FinancialCopilot.API/Controllers/AiFacadeController.cs`, `src/frontend/src/components/admin/admin-page.tsx`, `src/frontend/src/components/app/message-list.tsx`, `src/frontend/src/lib/chat.functions.ts`

## 1. Request (owner, 2026-11-20)

A new Admin Panel page, **AI Conversation Logs — تاریخچه پرسش و پاسخ کاربران**, where administrators inspect user
questions, AI answers, user identity, and timestamps across all users: server-side pagination (20 / 50 / 100), search
and filters (question text, user id / email / phone, date range, user, response status), a details drawer that shows
the answer as the user saw it (tables, charts, text) without regenerating anything, admin-only and read-only. No new
logging table and no duplicate conversation storage.

## 2. Discovery

Bounded context: Administration reading the Conversations context. Feature 149 already built the same read for **one**
user inside Customer 360; this feature is the cross-user, filterable view over the same data.

| # | Finding | Where |
|---|---|---|
| F1 | `Conversations` (`Id`, `TenantId`, `ActorId` = user id, `Title`, `StartedAt`, `UpdatedAt`; index `(TenantId, ActorId, UpdatedAt)`) and `Messages` (`ConversationId`, `Role` User/Assistant, `Content` markdown, `AssistantPayloadJson` **text**, `CreatedAt`; index `(ConversationId, CreatedAt)`). No column links a question to its answer. | `Infrastructure/Conversations/Persistence/ConversationPersistenceModels.cs`, `ConversationEntityConfigurations.cs` |
| F2 | Each exchange is written in one transaction as one User row and one Assistant row **with the same `CreatedAt`** (the time the answer finished). The time the question arrived is not stored; question and response timestamps are always equal and latency cannot be shown. `MessageRepository.AppendAsync` has no callers. | `ConversationRepository.PersistExchangeAsync`, `MessagePersistenceFunction.PersistAsync` |
| F3 | Response metadata lives in the payload: `Outcome` (Answered, PartialAnswer, ClarificationNeeded, DisambiguationNeeded, NoData, Unsupported, TemporarilyUnavailable, Failed) serialized as an **integer** by the default `JsonSerializer`; `OutcomeReasonCode`; `Usage` (provider, model, prompt / completion / total tokens, credits, estimated cost, execution route). Old payloads without `Outcome` deserialize as `Answered`. | `Application/Conversations/ConversationContracts.cs` (`AssistantMessagePayload`), `AiOrchestrationContracts.cs` (`UsageAccountingResult`) |
| F4 | Handled failures are persisted with `Outcome = Failed / TemporarilyUnavailable`. Failures that throw (provider crash, `InsufficientCreditException`, cancellation) persist **nothing**, not even the question. | `FinancialCopilotWorkflowDefinition.cs` catch blocks |
| F5 | Users are in `AuthDbContext` (Identity user `Email`; `UserProfiles` `DisplayName`, first / last name, mobile as `MobileEncrypted` + `ContactMobileHash` + `MobileLast4`). Same PostgreSQL database, **separate DbContext**: one LINQ query cannot join users to messages. Identity `PhoneNumber` is unused. | `AuthPersistenceModels.cs`, `TahlilAppPersistenceModels.cs` |
| F6 | No session or correlation id is stored on conversations or messages. The channel can be derived: a conversation in `TelegramConversationBindings` came from Telegram, otherwise web. | `AuthDbContext.TelegramConversationBindings` |
| F7 | Feature 149 already has: turn pairing (a User message anchors a turn; following Assistant messages in the same conversation until the next User message, ordered by `(CreatedAt, Id)`), a payload allowlist, a content-free `SecurityAdminAuditRow` written before a full-content read (fails closed), `AdminUsersRead` policy, tenant from the actor. | `EfCoreAdminChatHistoryQueries.cs`, `AdminManagementController.cs:73-104` |
| F8 | The chat maps a saved payload to the camelCase response the UI renders in `AiFacadeController.MapMessage` / `MapAssistantContent` (private static). 149's admin view returns the raw PascalCase payload and shows it as JSON, so the chat renderer cannot use it. | `AiFacadeController.cs:572-631`, `admin-customer-chat-history.tsx:187-198` |
| F9 | The chat renderer `AssistantBlock` (payload → `QuarterlyFinancialTable`, `BalanceSheetReport`, `CashFlowReport`, `MonthlyActivityTrendChart`, `PsGauge`, `ScannerResultTable`, …) is not exported and is coupled to interactive parts (`FollowSymbolButton` performs real follow actions; suggestions, regenerate, analysis offer). `mapAssistantBlock` (reload path) is not exported and sits in a module that imports server-only code. | `components/app/message-list.tsx:159-436`, `lib/chat.functions.ts:761-818` |
| F10 | Admin sections are tabs in state with a permission-gated `navigation` array; `UsersPanel` is the server-filter + pagination pattern; the admin root is always light (`className="light …"`). Jalali / Tehran formatting: `formatDisclosureReceiptDate`; Jalali date picker: `DateFilter` in `_app.disclosures.tsx`; shadcn `sheet`, `table`, `select`, `badge` exist. | `admin-page.tsx:67,145-190,195,294,622+`, `lib/format/disclosure.ts` |
| F11 | Real-PostgreSQL tests exist through `PostgreSqlIntegrationFixture` (Testcontainers). | `tests/FinancialCopilot.IntegrationTests/PostgreSqlIntegrationFixture.cs` |

## 3. Scope

In scope:

- A new admin tab with a server-paged, filterable list of question/answer turns across all users of the admin's tenant.
- A details drawer with the full question, every answer of the turn rendered by the chat's own components in a
  read-only mode, user and conversation information, channel, model / provider / tokens / credits, and outcome as
  error details.
- Two read-only admin endpoints; audit of full-content reads.

Out of scope:

- Recording failures that persist nothing today (F4) — backlog (OQ-117).
- Persisting the time a question arrived or a session / correlation id (F2, F6) — not available, shown as not stored.
- Any change to how chat messages are written, to 149's endpoints or behaviour, or to retention and deletion.
- Exporting logs, editing or deleting messages, replaying or regenerating answers, LLM calls.
- Dark theme for this page: the admin panel is light-only today (F10); the page follows it.

## 4. Decisions (Gate A)

| # | Decision | Answer |
|---|---|---|
| D1 | Permission | Reuse `admin.users.read` / `AuthorizationPolicies.AdminUsersRead`, as Feature 149 (OQ-115). |
| D2 | Default date window | The page opens on the last 30 days (Tehran calendar days); the admin can clear or change it (OQ-116). |
| D3 | Failures that persist nothing | Not shown; recorded in `specs/backlog.md` (OQ-117). |
| D4 | Answer rendering | Refactor the chat renderer to add a read-only mode and reuse the chat's payload mapper, so the drawer shows what the user saw (OQ-118). |
| D5 | Status | Derived from the stored payload; no new column (see §6). A question with no assistant message gets the synthetic status `NoResponse`. |
| D6 | Index | No data migration. One partial index is added only if `EXPLAIN ANALYZE` on production-sized data shows the list needs it (see §8). |

## 5. Turn semantics

Unchanged from Feature 149 (F7): the turn id is the User message id; its answers are the following Assistant messages
in the same conversation ordered by `(CreatedAt, Id)` until the next User message. Multiple exchanges in one
conversation and concurrent conversations of one user are separate turns. Turns whose answer is missing stay visible
with status `NoResponse`. Ordering on the page is `(question.CreatedAt DESC, question.Id DESC)`.

Pairing is computed against **all** messages of the page's conversations, not only the rows that passed the filters,
so a filtered-out next question still ends the previous turn. The shared pairing, allowlist and audit code moves out
of `EfCoreAdminChatHistoryQueries` into one internal helper used by both features; 149's tests must stay green.

## 6. Backend design

**Application** (`Application/Administration/AdminConversationLogContracts.cs`): `IAdminConversationLogQueries`,
`AdminConversationLogQuery` (page, pageSize ∈ {20, 50, 100}, text, user, fromUtc, toUtc, status),
`AdminConversationLogPage` (items, page, pageSize, totalCount, totalPages), `AdminConversationLogItem`,
`AdminConversationLogTurn`, `AdminConversationLogStatus` (the eight outcomes + `NoResponse`). Plain records, no
framework types.

**Infrastructure** (`Infrastructure/Administration/EfCoreAdminConversationLogQueries.cs`):

1. *User filter / search* — resolved first in `AuthDbContext`: a GUID matches the user id exactly; an email or name
   matches partially (upper-cased `Contains`, as `EfCoreAdminUserQueries`); a phone number is normalized, hashed with
   the existing mobile hasher and matched exactly on `ContactMobileHash` (partial phone search is impossible: mobiles
   are encrypted). More than a fixed cap of matching users → `400` asking the admin to narrow the search.
   *Refined at planning (2026-11-20):* TahlilApp mobiles have no hash, so a mobile is matched by narrowing on the
   stored last four digits and comparing decrypted values in memory; the behaviour (exact match, masked display) is
   unchanged. See `user-story.md` FR 7.
2. *List* — `AsNoTracking` query over User messages joined to the tenant's conversations, with `ActorId IN (…)`, date
   range on `CreatedAt`, question text `ILIKE` (Persian ی/ک and Arabic ي/ك normalized on both sides) and the status
   filter; database-side count, order, skip, take; previews trimmed in SQL.
3. *Status* — `COALESCE(("AssistantPayloadJson"::jsonb ->> 'Outcome')::int, 0)` of the turn's first answer, or
   `NoResponse` when there is none. Expressed through a mapped database function or composable SQL kept inside this
   class; the list never loads whole payloads.
4. *Answers for the page* — one query for message headers of the page's conversations from the earliest anchor on,
   pairing in memory, then previews only for the chosen answer ids (149's per-anchor `OR` of correlated subqueries
   does not scale to 100-row pages).
5. *Identities for the page* — one `AuthDbContext` query for the page's distinct user ids (display name, email,
   masked mobile) and one for Telegram bindings of the page's conversations. About five queries per page whatever the
   page size; no N+1.
6. *Detail* — anchor scoped to tenant (404 when foreign or missing), content-free audit row with action code
   `admin.ai-conversation-log.turn-read` written before content is read (fails closed), then the full question and
   answers, user information, conversation id / title, channel, and typed metadata from `Usage` (provider, model,
   prompt / completion / total tokens, credits charged, execution route) and outcome + reason code. Estimated cost,
   pricing policy, scanner plans, prompts, memory disclosures and any non-User/Assistant role are not returned.

**API**: `AdminAiConversationLogsController`, route `api/v1/admin/ai-conversation-logs`, `[Authorize]` +
`AdminUsersRead` on each action:

- `GET /api/v1/admin/ai-conversation-logs?page&pageSize&q&user&fromUtc&toUtc&status`
- `GET /api/v1/admin/ai-conversation-logs/{turnId:guid}`

Validation: page size whitelist, `fromUtc ≤ toUtc`, bounded search length, known status values; `400` with the
existing validation-problem shape. The answer payload in the detail response goes through the chat's own mapping,
extracted from `AiFacadeController` into a shared API-layer mapper with no behaviour change for the chat endpoints.

## 7. Frontend design

- `admin-page.tsx`: new tab `ai-logs`, label «تاریخچه پرسش و پاسخ کاربران», gated on `admin.users.read`, icon
  `MessagesSquare`; panel in `components/admin/ai-conversation-logs-panel.tsx`.
- Filter form: question text, user (id / email / phone), Jalali date range (default last 30 days), status select;
  applying filters resets to page 1.
- Table (`admin-table`): user name + id / contact, question preview, answer preview, Jalali Tehran time
  (`formatDisclosureReceiptDate`), status badge with Persian labels; page size select 20 / 50 / 100; page-number
  pagination with total count; loading, empty, and error states (error shows the correlation id); stale responses
  ignored.
- Drawer: RTL `Sheet` with the full question (`MarkdownMessage`), every answer rendered read-only, user, conversation,
  channel, timestamps (one timestamp, labelled as the completion time, since F2), model / provider / tokens / credits,
  outcome and reason code. "Not stored" is shown for session id and question-received time.
- Renderer refactor (D4): move `mapAssistantBlock` and its helpers to a client-safe module (the chat imports it from
  there); add a read-only mode to the assistant answer renderer that omits follow, suggestions, regenerate, copy /
  share actions, analysis offer and diagnostics. Chat behaviour unchanged; chat regression tests added.

## 8. Data access, performance, and the index

- No new table, column or data migration.
- The global newest-first list cannot use `(ConversationId, CreatedAt)`. Candidate index:
  `Messages ("CreatedAt" DESC, "Id" DESC) WHERE "Role" = 'User'` in `ConversationDbContext`. Added only if
  `EXPLAIN ANALYZE` on production-sized data shows a sequential scan + sort dominating the default 30-day page.
- Question text search is `ILIKE '%…%'` and scans; the default date window bounds it. A trigram index needs a new
  PostgreSQL extension and is not proposed.
- The status expression parses one JSON per candidate row; the date window bounds it. Payloads are always written by
  the serializer, so the cast does not fail on malformed JSON in practice; a test covers old payloads without
  `Outcome`.

## 9. Security and privacy

- Backend authorization on every endpoint; UI gating is not a boundary. Tenant from the authenticated actor in every
  predicate.
- The page widens exposure compared with 149: any `admin.users.read` holder can read every user's conversations
  without opening a user first (accepted with D1). Full-content reads are audited, content-free; list responses carry
  only trimmed previews.
- Never returned: tokens, credentials, system / developer prompts, tool messages, scanner plans, estimated cost,
  internal payload fields outside the allowlist; mobile is masked to the last four digits.
- Read-only: queries use `AsNoTracking`; the only write is the audit row.

## 10. Testing

Backend (integration, in-memory host as 149, plus one PostgreSQL Testcontainers test of the real SQL): pagination and
newest-first order with equal-timestamp tie-break; page-size whitelist; each filter (text incl. ی/ي, user id, email,
phone hash, date range, status incl. `NoResponse` and payloads without `Outcome`); several exchanges in one
conversation; concurrent conversations of one user; filtered-out next question still ends the turn; failed and
temporarily-unavailable outcomes; tenant isolation; 401 / 403; detail 404 for foreign ids; audit written without
content and fail-closed; structured payload returned in the chat's mapped shape; excluded fields absent; 149 tests
unchanged.

Frontend (Vitest): tab gating, default 30-day filter, server paging calls and page-size change, filters reset page,
loading / empty / error states, drawer renders a table/chart payload read-only with no follow or suggestion buttons,
Jalali dates; chat regression tests for the moved mapper and the default (interactive) renderer.

## 11. Risks

| Risk | Mitigation |
|---|---|
| Query plans on production volume unknown (149's check is also still open) | `EXPLAIN ANALYZE` task before deciding the index; PostgreSQL test for SQL translation (`Guid` comparison, jsonb expression). |
| Renderer refactor touches chat UI code | Behaviour-preserving extraction; chat regression tests; read-only mode is opt-in. |
| Admin misreads equal timestamps as instant answers | One timestamp labelled as completion time; "question received time not stored" note. |
| Hard failures invisible | Stated on the page's empty/help text; backlog item (OQ-117). |
| Wider admin read access | Audit of every full read; documented in §9. |

## 12. Expected files (to be finalized in `tasks.md`)

- `src/backend/FinancialCopilot.Application/Administration/AdminConversationLogContracts.cs` (new)
- `src/backend/FinancialCopilot.Infrastructure/Administration/EfCoreAdminConversationLogQueries.cs` (new),
  shared turn helper extracted from `EfCoreAdminChatHistoryQueries.cs`
- `src/backend/FinancialCopilot.Infrastructure/ServiceCollectionExtensions.cs` (registration)
- `src/backend/FinancialCopilot.API/Controllers/AdminAiConversationLogsController.cs` (new), shared assistant-content
  mapper extracted from `AiFacadeController.cs`
- Optional: index migration under `Infrastructure/Conversations/Persistence/Migrations/`
- `src/frontend/src/components/admin/ai-conversation-logs-panel.tsx` (new), `admin-page.tsx`, `admin-client.ts`,
  client-safe mapper module from `lib/chat.functions.ts`, read-only mode in `components/app/message-list.tsx`
- Tests in `tests/FinancialCopilot.IntegrationTests/` and `src/frontend/src/components/admin/__tests__/`
- `docs/architecture.md` (admin read path), `CHANGELOG.md` `[Unreleased]`
