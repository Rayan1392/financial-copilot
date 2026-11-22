# User Story: Admin In-App Messages and Support Replies

## Feature Folder

`165-admin-in-app-messages`

## Layers / Area

Layers: backend | database | frontend
Area: Administration | Notifications

## Priority

P2 (proposed planning priority)

## Complexity

L (estimate, including two-sided conversation state, permission and privacy checks, and PostgreSQL concurrency verification)

## Depends On

`035-admin-identity-and-entitlement-management` and `037-frontend-admin-panel`. The existing customer directory and account menu are integration points. `153-mobile-pwa-workspace` is a dependency for the included mobile inbox.

## References

- `design-proposal.md` (Gate A approved on 2026-11-22) and `specs/FEATURE_DEVELOPMENT_WORKFLOW.md`.
- `docs/domain-glossary.md` for project terms, `docs/architecture.md` for layer and admin boundaries, `docs/admin-management-api.md` and `docs/frontend-admin-panel.md` for authorization and audit behavior.
- `docs/notification-orchestration.md` for the Telegram delivery boundary. `AdminManagementController`, `EfCoreAdminUserQueries`, `AdminContentReadAudit`, `AuthDbContext` and `FinancialCopilotPermissions` for existing API, tenant, persistence and authorization patterns.
- `admin-page.tsx`, `admin-client.ts`, `account-menu.tsx`, `sidebar.tsx`, `routes/_app.tsx`, `routes/m.account.index.tsx`, and `lib/experience/path-map.ts` for the UI and route integration points.

## Standards Source Of Truth

Obey `.claude/rules/*` and `specs/FEATURE_DEVELOPMENT_WORKFLOW.md`.

## User Story

- As a Sapio administrator, I want to find an existing user and send an official in-app message, so that I can communicate about their account or a service issue.
- As a signed-in user, I want to notice new messages discreetly, see message history, and respond to a support message when replies are enabled, so that I can follow up within Sapio.
- As an authorized support operator, I want a shared queue of unanswered user replies, so that another operator can continue a conversation when the sender is unavailable.

## Scope

Manual, immediate messages to one existing web user. Reply mode is chosen at creation and defaults to one-way. Support replies stay in that conversation. Inbox access and replies are free for all plans. Display the staff author as «تیم ساپیو». Use the desktop and mobile/PWA workspaces.

## Out Of Scope

User-initiated conversations, bulk sends, scheduling, automatic messages, operator assignment, manual close/reopen, message edits or recall, attachments and clickable links, email, Telegram and browser push.

## Functional Requirements

1. Reuse the existing Users search, filters, pagination, and visibility rules. The Customer 360 composer shows the fixed recipient, required subject and body, and reply mode. Preview and confirm before sending. A failed attempt retains the draft. A confirmed successful send is available in the inbox.
2. Require nonblank subject and body after trimming. Use the proposed limits of 120 characters for subject and 2,000 for each body. Preserve plain-text line breaks. Disabled or locked in-tenant recipients are ineligible for staff sends and replies.
3. Show one inbox row per conversation, ordered by latest activity, with «همه» and «خوانده‌نشده» filters. Show a static dot by the desktop account trigger and an unread count in the menu, capped at `۹۹+`. Count unread staff entries. Render subject, chronological messages within the loaded page and Tehran-local dates. Acknowledge only staff entries displayed in an explicitly opened detail view. Acknowledgements preserve the first read timestamp. A failed acknowledgement can be retried and preserves unread status until confirmed. User replies do not create an unread item for the user.
4. Allow the recipient to reply only within a reply-enabled conversation. Preserve each submitted reply in history. The first unanswered user reply places the conversation in «نیازمند رسیدگی». Additional user replies preserve the original wait time. Opening the item preserves its queue state. A committed staff reply clears the queue state and creates an unread staff message. A subsequent user reply makes it pending again.
5. In the shared «پیام‌های کاربران» view, default to pending with oldest unanswered activity first. Allow listing all support conversations with latest activity first. Display subject, account identity under the directory's visibility rules, and timing. Record an audit event before returning detail or message bodies. Staff replies carry the reviewed revision. On a stale revision, preserve the draft and require reviewing the refreshed conversation before resubmission.

## Data / Schema

The existing `AuthDbContext` owns the migration and `SecurityAdminAudits`. Add the proposed logical tables `InAppConversations` and `InAppMessages`. A conversation owns recipient, tenant, subject, reply mode, revision, latest activity and pending-since time. Entries own a per-conversation sequence, authenticated author ID and role, body, send time and idempotency key. Staff entries also own first-read time. Keep the author identity in storage for audit. Add a database-enforced idempotency key unique within tenant and author, consistent tenant references, and indexes for the inbox, staff queue, detail paging and unread lookup. UTC is the storage time basis. A foreign key to the recipient's membership deletes the conversation and its entries when that membership is removed. Joining again starts with an empty inbox. A temporary lock or disable blocks access while retaining history for the existing membership. Security audit rows keep their independent lifecycle.

## API

Paths below are relative to `/api/v1`. Request and response DTOs use the API's JSON conventions. Query pages are cursor-based with a proposed default of 25 and maximum of 50. Bind cursors to tenant, recipient or queue scope and filters. Detail serves the latest page first, with a cursor for older history.

| Method | Route | Request | Response | Notes |
|---|---|---|---|---|
| POST | `/admin/users/{userId}/in-app-messages` | `subject`, `body`, `repliesEnabled`, `idempotencyKey` | Conversation ID, message ID and send time | Requires user-directory read and message-start permissions. |
| GET | `/in-app-messages/me/summary` | — | `unreadCount` capped at 100 | Show `۹۹+` for 100. |
| GET | `/in-app-messages/me` | `filter`, `cursor`, `pageSize` | Conversation summaries and next cursor | Returns title, latest activity and unread state. |
| GET | `/in-app-messages/me/{conversationId}` | `cursor`, `pageSize` | Conversation metadata and message page | Read-only. Includes staff entry IDs and their read state. |
| POST | `/in-app-messages/me/{conversationId}/replies` | `body`, `idempotencyKey` | Message ID and send time | Owner of a reply-enabled conversation only. |
| PUT | `/in-app-messages/me/{conversationId}/read` | `messageIds`, bounded by the detail page limit | Success after scoped acknowledgement | Accept up to 50 distinct staff IDs in one batch. Reject a batch containing an invalid or out-of-scope ID without writing read state. |
| GET | `/admin/in-app-messages/support` | `filter`, `cursor`, `pageSize` | Queue metadata and next cursor | Pending/all filters. No message bodies. |
| GET | `/admin/in-app-messages/support/{conversationId}` | `cursor`, `pageSize` | Conversation, reviewed revision and message page | Audit full reads before returning body text. |
| POST | `/admin/in-app-messages/support/{conversationId}/replies` | `body`, `expectedRevision`, `idempotencyKey` | Message ID and send time | Clear pending state in the same transaction. |

Use existing ProblemDetails and correlation conventions. Report invalid requests as `400`, missing authentication as `401`, insufficient permission or unsupported actor type as `403`, absent or out-of-scope IDs as `404`, ineligible recipients or state conflicts as `409`, and rate limiting as `429`, following existing authorization conventions. An identical idempotent retry returns the original success. Validate the key against the normalized operation and payload before treating an old revision as a conflict.

## UI

Use the existing RTL components, design tokens and dialog patterns. Desktop uses `/messages` and a route for conversation detail under the signed-in workspace. The shared admin view belongs to the existing admin navigation. Add mobile account access, a quiet indicator and experience path mapping. Include empty, loading, offline, validation and retry states. Focus remains on the current task when the badge changes. Message text wraps, user- and staff-authored entries have accessible labels, and status is conveyed in text as well as visually.

## Non-Functional Requirements

- Check the authenticated web actor, tenant and current membership on every operation. Use distinct start and support permissions with `admin.users.read` on the corresponding admin APIs. Scope detail, list, unread, reply and acknowledgement queries to the recipient or staff tenant. Reject API-key actors.
- Append each message, conversation transition and applicable write audit atomically. Serialize concurrent appends and apply the expected staff revision. Use a bounded actor-and-tenant write rate limit on both sides. Bound queries and page sizes. Refresh the user summary every 60 seconds while visible and online as an initial target, and on session entry, tab return and successful acknowledgement.
- Apply `Cache-Control: no-store`; keep protected data out of PWA/persistent caches. Scope frontend query keys to the session and remove cached data when accounts change. Escape message text and exclude content and contact details from logs, metrics and audit metadata. Collect bounded operational metrics for send/reply results and pending age.

## Acceptance Criteria

- [ ] An authorized admin can search and select a user, preview the fixed recipient and send either type of message. Invalid content, inactive recipients and unauthorized actors are rejected.
- [ ] A committed send is available in the recipient's inbox. Duplicate and concurrent retries make one entry and one successful staff write audit. A failed audit cannot leave a visible send.
- [ ] The desktop account trigger works in both sidebar modes. Read status changes only for rendered staff entries after explicit open and persists across reload or device changes. The history remains available after reading. Initial, empty, offline and error states are usable with RTL and keyboard navigation.
- [ ] The recipient can reply in enabled conversations. One-way notices reject replies through both UI and API. No user initiation route is exposed.
- [ ] A second authorized operator can handle a pending reply. A queue view preserves pending state, an answer clears it, and another user reply reopens it. Simultaneous staff replies from the same reviewed revision yield one append and one conflict.
- [ ] A different user, another tenant, an API-key actor, or a staff member missing the required permissions cannot read or modify protected content. Successful admin detail access is audited without body text. Identity changes clear private UI state.
- [ ] PostgreSQL schema and pagination checks pass. The selected mobile and lifecycle behaviors in OQ-125 and OQ-126 are verified. OQ-127 and OQ-129 are reflected in the access and support handoff checks.

## Assumptions

- OQ-128 is answered. User replies and the shared queue are included.
- OQ-125 includes desktop and mobile/PWA. OQ-126 retains messages for the lifetime of the membership. OQ-127 grants both permissions to `SuperAdmin` initially, with explicit delegation. OQ-129 remains open for support handoff and rollout.
- Endpoint names, page limits, query plans and refresh cadence are reviewable implementation-plan choices. The availability of the migration, rate-limit behavior and query performance must be verified in the implementation environment.
