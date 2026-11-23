# User Story: Identify unresolved actors in the conversation log

## Feature Folder

`164-admin-ai-conversation-logs/changes/unresolved-actor-identity`

## Layers / Area

Layers: backend | frontend
Area: Administration

## Priority

P2

## Complexity

S

## Depends On

164-admin-ai-conversation-logs, 146-unified-user-management-and-customer-360/changes/bug-045-conversation-log-account-availability

## References

- `specs/164-admin-ai-conversation-logs/changes/unresolved-actor-identity/design-proposal.md` (approved at Gate A)
- `specs/146-unified-user-management-and-customer-360/changes/bug-045-conversation-log-account-availability/design-proposal.md`
- `src/backend/FinancialCopilot.Infrastructure/Administration/EfCoreAdminConversationLogQueries.cs`
- `src/backend/FinancialCopilot.API/Security/ApiKeyAuthenticationOptions.cs`
- `src/frontend/src/components/admin/ai-conversation-logs-panel.tsx`

## Standards Source Of Truth

Obey `.claude/rules/*`. Do not copy rule text into this spec.

## User Story

As an administrator reading the conversation log,
I want rows whose asker has no user account to show who or what asked (an API client name or the raw ID),
so that I can identify the source and see all of its questions instead of an anonymous «حساب کاربری در دسترس نیست».

## Scope

- Actor kind (`User`, `ApiClient`, `Unknown`) and API client name on the log user, in list and turn detail.
- Unresolved user cell: kind label, short ID, clickable actor dialog with copy and «show all questions» actions.
- GUID user filter matches the conversation actor ID within the tenant.

## Out Of Scope

- Correcting the «وب» channel label of conversations created through an API client (`specs/backlog.md`).
- Any change to Customer 360 or to rows with a user account.

## API

`GET /api/v1/admin/ai-conversation-logs` and `GET /api/v1/admin/ai-conversation-logs/{turnId}`: `user` gains
`actorKind` (`"User" | "ApiClient" | "Unknown"`) and `apiClientName` (string or null). `hasUserAccount` is unchanged.

## UI

Persian labels: «کلاینت API: <name>», «کلاینت API», «شناسهٔ ناشناخته», dialog title «شناسهٔ پرسش‌کننده», actions
«کپی شناسه» and «نمایش همهٔ پرسش‌های این شناسه». IDs are rendered LTR.

## Acceptance Criteria

- [x] AC-1: A tenant member keeps `actorKind = User`, `hasUserAccount = true`, and the Customer 360 behaviour.
- [x] AC-2: An actor matching an API client configured for the active tenant returns `actorKind = ApiClient` with its
  name; a client configured for another tenant or an unmatched ID returns `Unknown` with no name. Both have
  `hasUserAccount = false`. The same holds in the turn detail.
- [x] AC-3: An unresolved row shows the kind label and the shortened actor ID (full ID in the tooltip); clicking it
  opens the actor dialog with the full ID and sends no Customer 360 request.
- [x] AC-4: «نمایش همهٔ پرسش‌های این شناسه» sets the user filter to the ID, returns to page 1, and the list shows that
  actor's questions in the tenant only.
- [x] AC-5: A response without `actorKind` (older API) still renders an unresolved row as «شناسهٔ ناشناخته» with its ID.

## Assumptions

- Only administrators with `AdminUsersRead` see API client names; names are configuration labels, not secrets.
