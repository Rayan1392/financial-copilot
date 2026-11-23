# Tasks: Identify unresolved actors in the conversation log

Status: Gate B: Approved by owner on 2026-11-23

## Feature Folder

`164-admin-ai-conversation-logs/changes/unresolved-actor-identity`

## Before Starting

- [x] This file's `Status:` line records Gate B approval by the owner.
- [x] Feature 164 and BUG-045 are implemented (BUG-045 released in v1.23.1).
- [x] Read `user-story.md` and `design-proposal.md`.
- [x] No open question blocks this change.

## Implementation Tasks

1. [x] Application: add `AdminConversationLogActorKind`, replace `AdminConversationLogUser.HasUserAccount` with
   `ActorKind` + `ApiClientName` (computed `HasUserAccount`), add port `IApiClientDirectory`.
2. [x] Infrastructure: `EfCoreAdminConversationLogQueries` classifies unresolved actors through the directory for the
   list and turn detail; a GUID user filter matches the actor ID directly. Register an empty default directory.
3. [x] API: `ConfiguredApiClientDirectory` over `ApiKeyAuthenticationOptions`; map `actorKind` and `apiClientName` in
   the controller contract.
4. [x] Frontend: client type, unresolved user cell, actor dialog, filter action; drawer shows the same actor label.
5. [x] Record the channel-label observation in `specs/backlog.md`; add the change to `CHANGELOG.md` `[Unreleased]`.

## Tests

- [x] Integration (PostgreSQL): actor kinds and client name in list and detail; GUID filter returns an unresolved
  actor's turns and nothing from another tenant.
- [x] Endpoint test: `actorKind` and `apiClientName` serialized.
- [x] Unit: `ConfiguredApiClientDirectory` tenant match, invalid IDs, blank names.
- [x] Vitest: unresolved cell label and short ID, dialog without Customer 360 calls, filter action, missing `actorKind`.

## Definition Of Done

- [x] Focused backend tests and the admin frontend tests green; no failure outside `specs/test-baseline.md`.
- [x] Frontend lint (changed files), typecheck (no new errors) and build green.
- [x] Acceptance criteria checked.

## Verification evidence (2026-11-23)

- Backend Release build: succeeded, no warnings.
- Integration: `AdminConversationLog164*` and `ConfiguredApiClientDirectoryTests`, 20 of 20 passed, none skipped; the PostgreSQL cases ran.
  They cover a tenant member, an API client of the tenant, a client of another tenant, a member of another tenant and an unknown ID, in both the list and the detail, plus the actor-ID filter for each.
- Unit `AdminConversationLog164*` 22 of 22 and architecture tests 24 of 24 passed.
- Vitest: conversation-log panel and drawer, 24 of 24; release-notes changelog parser, 12 of 12.
- ESLint (prettier `endOfLine: auto`): no finding in changed code. 17 pre-existing formatting findings remain in the unchanged «Admin Users directory usage filters» block of `ai-conversation-logs-panel.test.tsx`.
- `npm run typecheck`: 37 diagnostics, equal to the baseline, none in changed files. `npm run build`: client and SSR succeeded.
- Not run: the full backend and frontend suites, and a live check against production API client configuration (the client list lives only in the server `.env`).
