# Tasks: Admin In-App Messages and Support Replies

Status: Gate B: Approved by the owner on 2026-11-22

## Feature Folder

`165-admin-in-app-messages`

## Before Starting

- [x] Record explicit Gate B approval of the implementation plan in this file.
- [x] Confirm the desktop/admin dependencies (035 and 037 completion records) and the existing mobile/PWA account screen and route mapping (153).
- [x] Record desktop/mobile scope, membership-based retention and initial `SuperAdmin` grants (OQ-125, OQ-126 and OQ-127). OQ-129 remains open for the support handoff.
- [x] Isolated worktree `C:/Source/financial-copilot-f165` on `feature/165-admin-in-app-messages`, based on `develop` at `6393c6fd`. Starting architecture suite passed 23 tests. The original workspace contained ongoing feature changes.

## Decisions and Affected Work

| Decision | Decision or proposal | Affected tasks |
|---|---|---|
| OQ-125, mobile scope | Include mobile/PWA inbox, quiet badge and replies, using the same API. | 6, 7 |
| OQ-126, message retention | Keep history while the membership exists. Membership removal deletes content in the same transaction and rejoining starts with an empty inbox. Temporary locks preserve content. | 2, 7, 8 |
| OQ-127, launch roles | Grant both independent permissions to `SuperAdmin` and delegate through existing role management. | 1, 2, 3, 7, 8 |
| OQ-129, queue responsibility | Assign a named support team, set staffed hours and agree on a response target for internal monitoring. A customer-visible response promise requires its own decision. | 7, 8 |

## Implementation Tasks

### 1. Contracts and application rules

- [x] Define the feature's commands, query DTOs and persistence ports in `FinancialCopilot.Application/InAppMessages`. Keep authenticated actor information out of client-controlled fields. Define bounded validation for subject, body, idempotency keys, revisions, cursors and acknowledgement IDs.
- [x] Implement use cases for start, user reply, staff reply, summary, list, detail, queue and read acknowledgement. Model active recipient eligibility, fixed reply mode, first pending time, staff reply revision and immutable content. Use the shared time provider and existing error conventions.
- [x] Add narrow `admin.in-app-messages.send` and `admin.in-app-messages.support` permission codes in `FinancialCopilotPermissions` and corresponding API policies. Review both the general permission catalog and the mandatory bootstrap role set before finalizing grants.

### 2. Persistence, concurrency and migrations

- [x] Add EF mappings and an `AuthDbContext` migration under Infrastructure Authentication persistence. Store threads and ordered entries with consistent tenant ownership, bounded fields, indexes for both list orders, detail sequence and unread staff entries. Encode OQ-126 in the retention/deletion design and avoid cascading away required audit history. Apply the role grants chosen in OQ-127 through the established seed/migration mechanism and verify a rollback on a disposable database.
- [x] Add PostgreSQL write adapters. Append an entry and update revision, activity and pending state atomically. Staff writes include their audit row in the same commit. Enforce database idempotency per tenant and author, and compare operation, target and normalized payload on replay. Serialize appends or use the existing concurrency mechanism with a bounded retry for user replies. Return a staff revision conflict with no extra append. Check for a committed idempotent result before a stale-revision response.
- [x] Add read adapters with tenant and membership predicates. Apply keyset ordering with an ID tie-breaker and bind cursors to filter and owner/scope. Use immutable sequence for detail paging. For the live inbox and queue, reconcile pages by ID and restart pagination from page one on refresh or a filter change. Scope every requested acknowledgement ID and write only previously unread staff entries. Audit every successful admin detail page before returning bodies.

### 3. API and security wiring

- [x] Add thin controller endpoints and DI in `FinancialCopilot.API`, using the existing actor context, authorization and ProblemDetails patterns. Require authenticated web-user actor, current membership, and the matching admin policy where applicable. Apply `no-store` and bounded write rate limits. Verify that private requests bypass persistent caches and that logs and audit metadata exclude content and contact details.
- [ ] Verify start-only and support-only roles through direct API calls, missing roles, cross-tenant access, missing or wrong IDs, inactive recipients, invalid or mixed read batches, invalid cursors and API-key rejection. Add content-free request metrics for outcomes and queue age.

### 4. Admin UI

- [x] Reuse directory search and selection in `src/frontend/src/components/admin/admin-page.tsx`. Add a focused composer component, permission-gated entry point and transport client. Freeze the target during composition, preview the target and reply mode, disable repeat clicks, retain failed drafts and reuse the attempt key after an ambiguous failure.
- [x] Add «پیام‌های کاربران» to the existing admin navigation and implement pending/all views, paging and audited detail. Keep queue ordering stable for a single result page. Refresh the visible queue on entry and after commit, and with bounded polling. Handle stale revisions by refreshing detail and retaining the draft for explicit review.

### 5. Desktop inbox and notifications

- [x] Add the user client and shared query state using existing authenticated transport and session hooks. Integrate `/messages` and its detail route with the signed-in layout. Implement unread filtering, state labels, detail pagination and reply form. Acknowledge staff messages only after display in an explicitly opened detail view.
- [x] Update `account-menu.tsx` for full and collapsed modes. Refresh the summary on entry, tab focus and while visible and online, with an initial 60-second interval. Refresh after acknowledgement. Keep layout and focus stable. Clear private data on logout or account switch and verify that inaccessible or expired content is not rendered from cache.

### 6. Mobile/PWA inbox

- [x] Add inbox access in `routes/m.account.index.tsx`, the quiet indicator on the account entry point, and corresponding mobile inbox/detail routes. Reuse session and presentation logic. Update `lib/experience/path-map.ts` and its tests. Verify touch, RTL, offline states and exclusion of inbox responses from the service worker and persistent storage. Verify private-cache exclusion on all delivered routes.

### 7. Integrated verification

- [x] Add Application tests for validation, reply mode, revisions, idempotency and queue transitions. Add integration tests for access control, actual PostgreSQL concurrency and atomic audit, cursor scoping, acknowledgement and the approved account lifecycle. Verify indexes with representative tenants and inbox sizes. Measure summary traffic at the proposed interval and record query plans and timings.
- [ ] Add frontend tests for recipient locking, permission gates, safe retry, quiet badge in both sidebar modes, detail-only acknowledgement, staff conflict handling and account-switch cache isolation. Run a browser walkthrough as an admin, a second operator, the recipient and an unrelated user. Cover keyboard navigation, RTL, dark theme and the selected mobile scope.
- [ ] Verify the full flow from send through queue handling and renewed unread state, including database restart or reload and a second signed-in device. Confirm that the team selected in OQ-129 can open and answer items using the role selected in OQ-127.

### 8. Documentation and rollout preparation

- [ ] Document API, role grants, privacy/retention, migration order and queue operations. Record what support monitors, during which hours, and the internal response target. Update the feature index and affected architecture, admin and web documentation. Record known limitations and deferred work in the established files.
- [ ] Verify rollout in a non-production environment on existing Auth data before enabling operator access. Prepare migration-first rollout and rollback instructions, including retention of messages if application code rolls back. Coordinate the Persian Unreleased entry through the repository's release-manager workflow before merging a user-visible change.

## Tests

- [ ] `dotnet test src/backend/FinancialCopilot.sln --configuration Release` plus focused Application, API and PostgreSQL tests for the new feature.
- [x] `dotnet ef migrations has-pending-model-changes --context AuthDbContext` with the existing Infrastructure project and API startup project. Apply and roll back the migration on a disposable PostgreSQL database with existing Auth data, then reapply it.
- [ ] In `src/frontend`, run `npm run lint`, `npm run test`, `npm run build`, and `npm run typecheck`. Record results against `specs/test-baseline.md`.

## Verification Evidence

- Feature suites passed 2 Application tests, 4 API/PostgreSQL tests, and 60 focused frontend tests, including a fixed-recipient composer and ambiguous-send retry check. Windows browser startup and DOM tests used a 15-second Vitest test timeout. The architecture suite passed 23 checks.
- The browser walkthrough used a mock API through the production web bundle. It covered the desktop inbox, read acknowledgement, customer reply, the staff queue, staff reply, and mobile conversation history. The API and persistence suites used the actual web host and a disposable PostgreSQL 17 database, respectively.
- The Auth migration applied, rolled back and reapplied on isolated PostgreSQL. EF reported no pending Auth model changes.
- An isolated PostgreSQL 17 sample contained 8 tenants, 1,200 memberships, 24,000 conversations and 24,000 entries. Representative first-page SQL probes using `EXPLAIN (ANALYZE, BUFFERS)` took 0.326 ms for the recipient inbox, 0.220 ms for the pending queue, and 0.545 ms for the capped unread count. The probes exercised recipient, pending and message indexes. The times measure local SQL execution and exclude HTTP, network and production concurrency. A visible tab makes approximately one summary request per minute plus activity-triggered refreshes.
- The full backend run passed architecture checks and reported 33 unit-test failures. 32 matched the documented baseline. One Telegram financial-statement test timed out in its regex recognizer and passed in the comparison workspace's previously compiled Release suite. The full integration suite was interrupted by a Windows test-assembly lock. Rerunning only Feature 165 on an isolated PostgreSQL connection after the lock cleared passed all 4 tests.
- The full frontend suite surfaced existing failures in Persian copy and financial chart fixtures and stopped making progress. It was stopped after over four minutes without test output. Feature checks and the final client/SSR builds passed separately.
- Frontend type-check reports 37 diagnostics on both the feature and comparison workspaces. The full frontend lint run flagged repository-wide CRLF formatting on the Windows checkout. Lint passed when scoped to the changed feature files.

## Definition Of Done

- [ ] Every acceptance criterion in `user-story.md` is verified. No regression outside the documented test baseline. No pending Auth model changes.
- [ ] Open owner decisions are reflected in the implemented scope, tests and support handoff. Reference docs, feature index, and applicable user-visible changelog entry are current.
- [ ] Completion evidence records migration verification, concurrency and access-control results, query measurements, browser coverage, and any remaining limits.
