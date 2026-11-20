# User Story: Admin AI Conversation Logs — تاریخچه پرسش و پاسخ کاربران

## Feature Folder

`164-admin-ai-conversation-logs`

## Layers / Area

Layers: backend | frontend | database (one conditional index, task 8)
Area: Administration (reads Conversations; Authentication for user identity)

## Priority

To be set by the owner.

## Complexity

M

## Depends On

149-admin-customer-chat-history (implemented and merged on `develop`, commit `f200c846`; its PostgreSQL query-plan check is still open and is covered here by task 7, its same-timestamp pairing defect is BUG-037 / OQ-119).

## References

- `specs/164-admin-ai-conversation-logs/design-proposal.md` (approved at Gate A on 2026-11-20; OQ-115 … OQ-118)
- `specs/149-admin-customer-chat-history/design.md` (turn semantics, audit, privacy)
- `docs/architecture.md` (layer dependencies), `docs/domain-glossary.md` terms used: conversation, turn, outcome
- Data model: `ConversationDbContext` (`ConversationRow` → `Conversations`, `MessageRow` → `Messages`),
  `AuthDbContext` (`FinancialCopilotUser`, `UserProfileRow` → `UserProfiles`, `TelegramConversationBindingRow`,
  `SecurityAdminAuditRow`); payload record `AssistantMessagePayload`, `UsageAccountingResult`, `DialogueOutcome`
- Code: `src/backend/FinancialCopilot.Infrastructure/Administration/EfCoreAdminChatHistoryQueries.cs`,
  `src/backend/FinancialCopilot.API/Controllers/AiFacadeController.cs` (`MapMessage`, `MapAssistantContent`),
  `src/backend/FinancialCopilot.Infrastructure/Authentication/Profile/OwnProfileStore.cs` (mobile protection),
  `src/frontend/src/components/admin/admin-page.tsx`, `src/frontend/src/components/app/message-list.tsx`,
  `src/frontend/src/lib/chat.functions.ts` (`mapAssistantBlock`), `src/frontend/src/lib/format/disclosure.ts`,
  `src/frontend/src/routes/_app.disclosures.tsx` (`DateFilter`)

## Standards Source Of Truth

Obey `.claude/rules/*`. Do not copy rule text into this spec.

## User Story

As an administrator with `admin.users.read`,
I want one page that lists every user's questions with the AI answers, searchable and filterable, with a details view
that shows each answer as the user saw it,
so that I can review answer quality, investigate user reports and find failed or unsupported questions without
opening users one by one.

## Business Context

Today an admin can read conversations only per user (Feature 149, Customer 360 → Chat History) and sees structured
answers as raw JSON. Quality review, support investigations and unmet-demand analysis need a cross-user view with
filters by text, user, date and outcome, and a faithful rendering of tables and charts.

## Scope

- Admin tab «تاریخچه پرسش و پاسخ کاربران» in `/admin`, gated on `admin.users.read`.
- Server-paged list (20 / 50 / 100) of question/answer turns of the admin's tenant, newest first.
- Filters: question text, user (id, email, name, mobile), Jalali date range (default last 30 days), response status.
- Details drawer: full question, every answer of the turn rendered read-only by the chat's own components, user,
  conversation, channel, timestamps, model / provider / tokens / credits, outcome and reason code.
- Two read-only admin endpoints; audit of full-content reads.
- Behaviour-preserving extractions: the chat's assistant-content mapper (backend) and payload view-model mapper
  (frontend), a read-only mode of the chat answer renderer, the Jalali date filter, the admin content-read audit and
  contact-mobile protection helpers.
- BUG-037 fix for Feature 149 only if the owner answers OQ-119 (a) (tasks section 9 and
  `specs/149-admin-customer-chat-history/changes/fix-same-timestamp-turn-pairing/`).

## Out Of Scope

- Turns whose AI run threw and persisted nothing (OQ-117 → `specs/backlog.md`).
- Persisting question-received time, session or correlation ids (`specs/backlog.md`).
- Changes to how chat messages are written, to retention or deletion; exporting, editing or deleting logs;
  regenerating answers or any LLM call.
- A dark theme for the admin panel (the admin panel is light-only today).
- A new permission (OQ-115 (a)).

## Functional Requirements

1. **Turn.** A turn is a persisted `User` message (turn id = its message id) and the `Assistant` messages that follow
   it in the same conversation until the next `User` message. Messages are ordered by `(CreatedAt, role rank, Id)`
   with `User` = 0 and `Assistant` = 1, because one exchange stores both rows with the same `CreatedAt` (BUG-037).
   Roles other than `User` and `Assistant` are never returned. Pairing considers all messages of the conversation,
   not only rows that pass the filters.
2. **List.** Ordered by `(question CreatedAt DESC, question Id DESC)`. Each item: turn id, conversation id and title
   (default title omitted), user (id, display name, email, masked mobile), question preview (first 240 characters),
   first-answer preview (first 320 characters) or none, question and first-answer `CreatedAt` (UTC), status, channel.
3. **Status.** The `Outcome` of the turn's first answer payload, one of `Answered`, `PartialAnswer`,
   `ClarificationNeeded`, `DisambiguationNeeded`, `NoData`, `Unsupported`, `TemporarilyUnavailable`, `Failed`;
   `Answered` when the payload or its `Outcome` is missing (payloads written before outcomes existed); `NoResponse`
   when the turn has no answer.
4. **Channel.** `telegram` when the conversation has a `TelegramConversationBindings` row (revoked or not), otherwise
   `web`.
5. **Pagination.** `page` ≥ 1 (default 1), `pageSize` ∈ {20, 50, 100} (default 20); response carries `page`,
   `pageSize`, `totalCount`, `totalPages`. Count, order, offset and limit run in PostgreSQL.
6. **Question text filter.** Case-insensitive substring match on the question; Arabic `ي`/`ك` and Persian `ی`/`ک`
   match each other; `%`, `_` and `\` in the term are literal. 1–200 characters after trimming.
7. **User filter.** One input, 1–200 characters after trimming: a GUID matches the user id exactly; a value that
   normalizes to an Iranian mobile number matches users whose TahlilApp mobile or self-service contact mobile equals
   it (narrowed by the stored last four digits, then compared after decryption in memory); otherwise a substring of
   email, display name, first/last name or local first/last name (as `EfCoreAdminUserQueries`). More than 200
   matching users → `400` with code `user_search_too_broad`. No match → empty page.
8. **Date range.** `fromUtc` inclusive, `toUtc` exclusive, on the question `CreatedAt`; `fromUtc` must be before
   `toUtc`. The UI sends Tehran calendar-day boundaries; the API applies no default range.
9. **Status filter.** One status value from requirement 3.
10. **Detail.** `GET …/{turnId}` returns the question (full), its `CreatedAt`, conversation id and title, channel,
    user, status, and every answer of the turn in order with: message id, full content, `CreatedAt`, outcome,
    outcome reason code, usage (provider name, model name, prompt / completion / total tokens, credits charged,
    completion status, execution route; each nullable) and `assistantContent` — the stored payload mapped by the same
    mapper the chat uses for `GET /api/ai/v1/conversations/{id}/messages`. No analysis hint is attached. A turn id
    that is not a `User` message of the admin's tenant returns `404`.
11. **Audit.** Before reading full content, the detail endpoint writes a `SecurityAdminAuditRow` (action code
    `admin.ai-conversation-log.turn-read`, target type `chat_history_turn`, target id = turn id, tenant, admin id,
    actor type, permission `admin.users.read`, correlation id) with no question, answer or payload text. If the
    audit write fails, the request fails and no content is returned.
12. **Never returned:** estimated cost, pricing policy version, scanner query plans, memory disclosures, prompts,
    tool or system messages, unmasked mobile numbers, encrypted values, national ids.
13. **UI.** A tab in `/admin`; filter form (question text, user, Jalali date range defaulting to the last 30 Tehran
    days, status select, page size select); applying a filter returns to page 1; table rows with user, question
    preview, answer preview, Jalali Tehran date-time, status badge, channel; page controls with «صفحه X از Y · N
    پرسش»; loading, empty and error states (error shows the correlation id); stale responses ignored. A row opens an
    RTL drawer showing requirement 10, the answers rendered read-only (no follow, suggestion, regenerate, copy/share,
    analysis-offer or diagnostics controls), timestamps in Jalali Tehran time with one completion time when question
    and answer times are equal, and «ذخیره نشده» for session id and question-received time.

## Data / Schema

- Reads `Conversations`, `Messages` (`ConversationDbContext`) and `AspNetUsers`, `UserProfiles`,
  `TelegramConversationBindings` (`AuthDbContext`); writes only `SecurityAdminAudits`.
- No new table or column. Status is read from `Messages.AssistantPayloadJson` (text) with
  `("AssistantPayloadJson"::jsonb ->> 'Outcome')` where `jsonb_typeof` is `number`.
- Conditional (task 8): partial index `IX_Messages_User_CreatedAt_Id` on `Messages ("CreatedAt" DESC, "Id" DESC)
  WHERE "Role" = 'User'`, created with `CREATE INDEX CONCURRENTLY` outside a transaction, in a `ConversationDbContext`
  migration, only if the measurement rule in task 8 is met.

## API

| Method | Route | Request | Response | Notes |
|---|---|---|---|---|
| GET | `/api/v1/admin/ai-conversation-logs` | `page`, `pageSize`, `q`, `user`, `fromUtc`, `toUtc`, `status` (query) | `{ items[], page, pageSize, totalCount, totalPages }` | `AdminUsersRead`; `400` validation problem for bad page size, range, length, status, `user_search_too_broad` |
| GET | `/api/v1/admin/ai-conversation-logs/{turnId:guid}` | — | turn detail (requirement 10) | `AdminUsersRead`; audited; `404` when not found in tenant |

## AI Query Path

None. No capability, route or model call is added or changed.

## UI

- `/admin` tab `ai-logs`, label «تاریخچه پرسش و پاسخ کاربران», icon `MessagesSquare`, visible with `admin.users.read`.
- Status labels: پاسخ داده شد، پاسخ ناقص، نیاز به توضیح، نیاز به ابهام‌زدایی، داده‌ای یافت نشد، پشتیبانی نمی‌شود،
  موقتاً در دسترس نیست، ناموفق، بدون پاسخ. Channel labels: وب، تلگرام.
- RTL, wraps long Persian text and wide tables inside the drawer, usable at phone width; follows the admin panel's
  light theme.

## Non-Functional Requirements

- Authorization on every endpoint with `AdminUsersRead`; tenant from the authenticated actor in every predicate.
- Read-only: `AsNoTracking` / raw `SELECT` only; the audit row is the only write.
- List: at most 6 database round trips per request regardless of page size (count, page, user identities, Telegram
  bindings, plus the user-filter lookup when used); no full payloads loaded for the list; previews cut in SQL.
- All SQL parameterized; no string concatenation of user input.
- The default 30-day page (20 rows) on the synthetic dataset of task 7 answers in under 300 ms on the local
  PostgreSQL container (measured, reported; production plan checked by the owner).
- Cancellation passed to every query; logs carry counts and ids only, never question or answer text.

## Acceptance Criteria

- [ ] AC1 — The tab appears only for `admin.users.read`; both endpoints return `401` unauthenticated and `403` without the permission.
- [ ] AC2 — The list is newest first with `(CreatedAt, Id)` tie-break, paged at 20 / 50 / 100 with correct `totalCount` and `totalPages`; other page sizes return `400`.
- [ ] AC3 — A question and its answer stored with the same `CreatedAt` and an answer id that sorts before the question id are paired correctly (BUG-037 case).
- [ ] AC4 — Several exchanges in one conversation, and concurrent conversations of one user, appear as separate turns each with its own answer; a filtered-out next question still ends the previous turn.
- [ ] AC5 — A question without an answer has status `NoResponse`; `Failed` and `TemporarilyUnavailable` turns show their status and reason code; a payload without `Outcome` shows `Answered`.
- [ ] AC6 — Each filter (question text incl. ی/ي and ک/ك and literal `%`, user id, email, name, TahlilApp mobile, contact mobile, date range, status) returns exactly the matching turns; `user_search_too_broad` above 200 users.
- [ ] AC7 — Turns of another tenant never appear in the list and return `404` in detail.
- [ ] AC8 — The detail returns every answer of the turn in order, `assistantContent` equal to what the chat messages endpoint returns for the same stored message, and usage fields; estimated cost, pricing policy, scanner plan and memory disclosures are absent.
- [ ] AC9 — A detail read writes one content-free audit row before returning; when the audit write fails the response is an error with no content.
- [ ] AC10 — The drawer renders a stored quarterly table, a monthly trend chart and a scanner table with the chat components, and shows no follow, suggestion, regenerate, copy/share, offer or diagnostics control.
- [ ] AC11 — Dates show in the Jalali calendar in Tehran time; the default filter covers the last 30 Tehran days.
- [ ] AC12 — Loading, empty and error states (with correlation id) are shown; a stale response does not replace a newer one.
- [ ] AC13 — The chat behaves as before: existing chat and reload tests pass, plus new regression tests for the moved mappers and the interactive renderer.
- [ ] AC14 — The list runs in at most 6 database round trips (PostgreSQL test with a command counter) and the measurement of task 7 is reported.
- [ ] AC15 — Feature 149's existing tests pass (and, if OQ-119 (a), its BUG-037 regression test).

## Assumptions

- Feature 149's implementation on `develop` is the dependency baseline; its tasks file predates checkboxes.
- Iran has used UTC+03:30 without daylight saving since 2022; Tehran day boundaries use the IANA zone `Asia/Tehran`
  through `Intl` on the client, so older data is still converted correctly.
- Mobile search decrypts only profiles whose stored last four digits match the searched number; at expected user
  counts this is a small, bounded set. This refines the Gate A text (hash match on `ContactMobileHash`), because
  TahlilApp mobiles have no hash; the approved behaviour (exact mobile match, masked display) is unchanged.
- An assistant row with no payload (written before payloads existed) counts as `Answered`.
- An empty or whitespace-only `q` or `user` query value is bound as absent (ASP.NET Core model binding) and applies
  no filter; the 1–200 character limits apply to a given value. The page trims inputs and omits empty filters.
- The masked mobile is `•••` followed by the last four digits (contact mobile before the TahlilApp mobile); the full
  number is never decrypted for display.
