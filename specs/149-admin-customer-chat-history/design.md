# Feature 149 — Design

## Repository findings

- Feature 146 provides the React Admin Customer 360 dialog and routes it through `AdminManagementController` and `IAdminUserQueries`.
- The dialog currently displays account, identity, subscription, credit, purchase, and provider information in one view. Its default selection flow loads `/customer-360` and the legacy user record.
- `ConversationDbContext` owns existing `Conversations` and `Messages`. A conversation has immutable `TenantId` and `ActorId`; each message has an ID, `ConversationId`, role, content, timestamps, optional scanner plan JSON, and optional assistant payload JSON.
- V1 and MAF V2 both persist the shared `ConversationRow`/`MessageRow` model. `PersistExchangeAsync` persists a user message and assistant message per exchange; later messages in a conversation form additional turns.
- Existing per-conversation reads order by `CreatedAt` and role but lack a unique tie-breaker. Existing message index is `(ConversationId, CreatedAt)`; conversation lookup index is `(TenantId, ActorId, UpdatedAt)`. No schema change is initially required.
- Admin APIs use `admin.users.read` for Customer 360 and existing security permission policies. The Auth database has immutable `SecurityAdminAuditRow` records. The frontend uses the existing admin API client, TanStack/React conventions, shadcn primitives, and RTL layout.

## Turn semantics

The turn anchor is a persisted message whose role is `User`. A turn's answer consists only of subsequent persisted `Assistant` message(s) in the same conversation, ordered by `(CreatedAt, Id)`, until the next `User` message. Messages with any other role are excluded. This handles multi-turn conversations and incomplete assistant responses without inventing content. The first assistant item is the list summary; detail returns every eligible assistant message in the turn in order. If there is no assistant item, the detail and list expose an incomplete status.

`CreatedAt` and `Id` define total deterministic ordering. The user question's `CreatedAt` is the turn timestamp. `Conversation.Title` is optional, with the default placeholder omitted. Content remains exactly as persisted; the API does not replay or transform it. `AssistantPayloadJson`, when present and valid, is included as a structured payload alongside `Content`; scanner plans and all non-assistant messages are excluded.

## API contract

- `GET /api/v1/admin/users/{userId:guid}/chat-history?page=1&pageSize=20`
- `GET /api/v1/admin/users/{userId:guid}/chat-history/turns/{turnId:guid}`

Both require authentication and `AuthorizationPolicies.AdminUsersRead`. Invalid page values are rejected or bounded consistently with neighboring API validation. The list response includes items, page, pageSize, totalCount, and totalPages. The detail response includes turn ID (the user-message ID), conversation ID/title, question content/time, ordered assistant response entries/content/time, and completeness status.

List counts and pages are computed against user-role messages joined to conversations where both `TenantId` and `ActorId == userId` match. The tenant is resolved from the authenticated actor. Page by `(question.CreatedAt DESC, question.Id DESC)` with database-side count, skip, and take. Detail selects the anchor question only when its owning conversation matches both tenant and requested immutable user ID, then fetches the bounded turn response range. A foreign, missing, or out-of-tenant turn is indistinguishable (404).

## Audit and privacy

Use the existing `auth_security_admin_audits` store for full-content detail reads. Record a stable action code, target type `chat_history_turn`, target ID as the user-message ID, effective tenant, administrator actor ID, permission code, timestamp, actor type, and request correlation ID. Do not include question/answer text, summaries, serialized payloads, tool inputs, or secrets in logs/audit payload. Persist the audit before returning full content; a failed audit write fails closed. List responses expose bounded summaries and do not return full stored content.

The response includes only `User` and `Assistant` roles. It never includes system/developer prompts, tool-call messages, scanner query plans, credentials, or hidden reasoning. Assistant payloads are the already persisted public response payload and are returned only as structured data; no historical answer is regenerated. Existing retention and deletion logic continues to govern all records.

## Data access and performance

Inject the existing `ConversationDbContext` into the infrastructure read service. Keep projections `AsNoTracking`; count, ordering, offset, and page size execute in PostgreSQL. Fetch detail content only after expansion. Pair page anchors and responses in a bounded number of database queries, not one query per row. Reuse current indexes where sufficient; add an index/migration only if the actual query plan demonstrates need. Avoid loading all messages in a conversation for list rows.

## Frontend design

Keep the current Customer 360 dialog and wrap its unchanged account-information subtree in a tab panel. Default to Account Information. The Chat History panel fetches page 1 when selected, uses 20 items/page, aborts or ignores stale user requests, and resets list/page/expanded state whenever `userId` changes. Each row is a semantic button with `aria-expanded`, summary, timestamps, and a disclosure indicator. Expanding fetches detail on demand; collapsing clears the open row. Provide loading, empty, page error, and detail error states and page position with Previous/Next.

Render question and answer content using the existing safe chat Markdown renderer after confirming its HTML handling. If the existing renderer cannot be safely used in this admin panel, use a text-only rendering fallback with preserved whitespace. The layout remains `dir="rtl"`, wraps long tables/text, and does not use nested dialogs.

## Authorization

Use the existing `admin.users.read` policy at each endpoint. Resolve actor tenant and administrator ID from `ICurrentActorContext`, and apply tenant + requested user ID in the database predicate. UI permission visibility is not an authorization boundary. No public route or general-user conversation replay endpoint is modified.

## Backward compatibility and schema

No new database table or entity is needed. No migration is planned unless query-plan evidence shows current indexes cannot support the read path. Existing account information, existing conversation replay endpoints, role storage, and both V1/V2 persistence paths remain unchanged.

## Test design

Cover server pagination/count/order, equal timestamps with ID tie-break, multi-turn grouping, multiple assistant messages, incomplete turns, hidden non-user/assistant roles, payload exposure limits, tenant/user isolation, cross-user detail IDs, unauthenticated and missing-permission rejection, audit success/fail-closed behavior without content in audit fields, and bounded query count. Frontend coverage includes tabs/default, preserved account view, lazy list/detail loads, pagination, keyboard expand/collapse, empty/loading/error states, switching users, safe rendering, RTL and responsive layout.
