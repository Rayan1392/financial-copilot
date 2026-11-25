# Tasks: Start and Follow Up on a Support Request

Status: Gate B: Approved by the owner on 2026-11-25

## Feature Folder

`165-admin-in-app-messages/changes/user-initiated-support`

## Dependencies and Handoff

- Feature 165 code and the original Auth migration are present. The dependency check on 2026-11-25 found the [baseline Definition of Done](../../tasks.md#definition-of-done) still open, including access-control verification, the full support flow after reload or on another device, and non-production rollout verification. The repository's spec-driven workflow requires the baseline Definition of Done before implementation. The new flow requires the baseline Auth schema in the target environment.
- On 2026-11-25, after the dependency was reported, the owner directed implementation to proceed («پیاده سازی را انجام بده»). The baseline rollout and support handoff remain open. Local verification results are recorded separately below.
- [OQ-129](../../../open-questions.md) remains open. Before rollout, confirm the queue owner, staffed hours, internal first-response target, and the designated operators' existing grants. Track this separately from shipping a customer-facing response-time promise.

## Contract and Persistence

| Operation | Proposed contract |
| --- | --- |
| `POST /api/v1/in-app-messages/me` | Accept `subject`, `body`, and `idempotencyKey`. Return the existing `MessageReceipt` with conversation ID, message ID and UTC send time after commit. Use existing ProblemDetails and rate-limit conventions. |
| Existing user inbox and detail GETs | Add nullable `supportStatus` to the conversation summary. Values are `waitingForSupport` and `answered`. Missing or null status renders no request label. |
| Existing support queue, replies and read acknowledgement | Use their current endpoints and state transitions. |

The existing `InAppConversations` and `InAppMessages` model has the fields needed. A user start is a reply-enabled conversation with revision 1, an initial message from the member at sequence 1, and `PendingSinceUtc` and latest activity set to the same UTC time. Use the persisted author side of the first message to determine origin. Compute user-facing status server-side from origin and pending state within the scoped inbox and detail projections. The staff pending index continues to support queue ordering. No new table, migration or data backfill is planned. Feature 165 membership retention applies.

## Implementation Order

1. [x] Record explicit Gate B approval. The owner wrote «**Gate B** مورد تایید است» on 2026-11-25 in this conversation.
2. [ ] Complete the Feature 165 baseline rollout and handoff. Local baseline authentication, queue and migration checks passed.
3. [x] In `InAppMessagesController.cs`, add the authenticated POST under the existing controller and write-rate policy. In `InAppMessageUseCases.cs` and `InAppMessageContracts.cs`, add an explicit user-start command and reuse the existing actor, normalization and validation rules. Derive recipient and tenant from the actor. Keep membership checks and reject API-key actors through existing application rules.
4. [x] In `EfCoreInAppMessageStore.cs`, reuse the start persistence path where practical and commit the conversation, pending time and first message together. Keep the staff start audit for staff operations and preserve the existing staff detail and reply audits. Extend start replay matching to check the author side as well as normalized payload, target and mode. Reuse the existing database uniqueness constraint so identical retries return the original receipt and operation collisions return a conflict. Add content-free telemetry for a committed user start and project request status without per-item database round trips.
5. [x] Extend `integrations/financial-copilot/in-app-messages.ts` and the shared inbox components, reusing the session-scoped query keys. Use the shared inbox form from the existing desktop and mobile routes, with visible subject and body labels, focus management, accessible error feedback and preserved input. Disable repeated submission in flight. Reuse the attempt key for an unchanged retry, and create a fresh key when normalized form content changes. On success refresh inbox data and open the existing device-specific detail route. Render the status on request rows and detail independently of unread state. Inspect the existing admin queue for needed copy adjustments and retain its current permission checks and polling.
6. [x] Run the existing `InAppMessage165Tests`. Extend `InAppMessage165EndpointTests`, `InAppMessage165PostgreSqlTests` and the inbox frontend suite to cover the new behavior. Test invalid input, unauthorized and cross-scope access, first-user-message persistence, atomic rollback on failed write, replay under a real PostgreSQL race, collisions between start and reply and between staff and user starts, pending ordering, status projection, unread acknowledgement and concurrent staff responses. Verify that old notices and support threads still render and work with their original rules.
7. [x] Run the targeted backend and frontend suites, including PostgreSQL integration tests, and check the backend and frontend build/typecheck results against [the test baseline](../../../test-baseline.md). Walk through desktop and mobile/PWA create → queue → reply → unread → follow-up, including retry after lost response, reload, keyboard navigation and narrow RTL layout. Check list and detail query shape and logs for unexpected extra queries or message content.
8. [ ] Record verification, deployment prerequisite and the queue handoff. Update applicable reference documentation and coordinate the user-visible changelog entry through the repository release workflow.

## Definition of Done

- [x] The [acceptance criteria](user-story.md#acceptance-criteria) pass in local checks and verification results are recorded.
- [x] Relevant backend, frontend and architecture checks pass within the repository baseline.
- [ ] Feature 165 dependencies, existing Auth schema availability and the support handoff are documented. Any remaining rollout blocker is explicitly recorded.

## Verification Evidence

- The initial Feature 165 baseline suites passed 2 Application tests, 4 API/PostgreSQL tests and 6 focused frontend tests.
- The changed inbox, admin composer and account menu suites passed 8 frontend tests. The architecture suite passed 24 checks. ESLint passed on the four changed frontend files. Repository-wide TypeScript checking still reports diagnostics outside the changed files, including the routing and existing fixture issues tracked in the [test baseline](../../../test-baseline.md).
- Release backend verification passed 2 Application and 5 API/PostgreSQL tests, with zero skipped. Checks include concurrent start replay, queue ordering and state changes, staff reply concurrency, ownership and rollback on a failed first message write.
- The frontend client and SSR builds passed. The Chromium browser scenario passed with a mocked API, covering an ambiguous create response, request replay, staff response, and mobile reload and follow-up.
- The existing Auth schema is a deployment prerequisite. No change-specific migration or backfill was added. The non-production rollout and OQ-129 support handoff are still open.
