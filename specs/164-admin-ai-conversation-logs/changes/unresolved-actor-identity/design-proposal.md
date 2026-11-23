# Design — Identify unresolved actors in the conversation log

Status: Gate A: Approved by owner on 2026-11-23

## Request

In the admin conversation log («تاریخچه پرسش و پاسخ کاربران»), rows whose actor has no user account in the active
tenant show only «حساب کاربری در دسترس نیست». The owner asked that these rows show the actor's ID and that clicking it
open a popup like the other users.

## Findings

- BUG-045 (`specs/146-unified-user-management-and-customer-360/changes/bug-045-conversation-log-account-availability/`)
  deliberately made these rows plain text: Customer 360 needs a tenant member, so the dialog returned 403/404.
- A conversation's `ActorId` is either a web user's subject or an API client ID (`HttpCurrentActorContext`). API
  clients are configured in `Authentication:ApiKeys` (`ApiKeyAuthenticationOptions`) with a `ClientId`, `TenantId` and
  `Name`; the BUG-045 actor was the Telegram gateway API client. Conversations created by an API client have no
  Telegram binding, so the log labels them «وب».
- The list's user filter matches a GUID only against tenant members, so an unresolved actor's questions cannot be
  filtered by its ID.

## Approved design (option 1)

- Each log user carries an actor kind: `User` (tenant member), `ApiClient` (an API client configured for the active
  tenant), or `Unknown`. For `ApiClient`, the configured client name is included. `HasUserAccount` stays in the HTTP
  contract for compatibility and equals `ActorKind == User`.
- The client lookup is an Application port (`IApiClientDirectory`) implemented in the API host from the configured API
  key clients (active or not, so older conversations stay identified). Hosts without API key configuration register an
  empty directory.
- The user cell of an unresolved row shows «کلاینت API: <name>» or «شناسهٔ ناشناخته» with the shortened ID (full ID
  on hover). Clicking it opens a simple actor dialog: kind, client name, full ID with a copy action, a note that
  Customer 360 is unavailable for this ID, and «نمایش همهٔ پرسش‌های این شناسه», which applies the ID as the user
  filter. The dialog makes no Customer 360 request.
- A GUID user filter matches the conversation actor ID directly; the tenant condition of the list already scopes it.
- No schema migration.

## Rejected alternative

Option 2 (open Customer 360 for the ID) would always show «حساب در دسترس نیست» and restore the 403/404 requests
BUG-045 removed.

## Noted, not in scope

Conversations created through an API client may actually come from Telegram but are labelled «وب». Recorded in
`specs/backlog.md`; correcting the channel needs separate approval.
