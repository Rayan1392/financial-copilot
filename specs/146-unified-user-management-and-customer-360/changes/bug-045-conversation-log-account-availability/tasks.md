# Tasks — BUG-045 Customer 360 availability in conversation logs

Status: Gate B: Approved by owner on 2026-11-23

## Dependencies and order

Feature 164 provides the tenant-scoped conversation log and its batched Identity enrichment. The Feature 146 Customer 360 change supplies the shared dialog. Extend the backend contract first, then consume it in the frontend.

## Implementation

1. [x] Add a boolean `hasUserAccount` to the log user contracts in Application and API. In `EfCoreAdminConversationLogQueries`, pass the effective tenant into the existing batched user enrichment for the list and turn detail. Join existing Identity users to tenant membership before loading their profile metadata. Set the flag from the membership-backed match independently of display fields. Keep an unresolved actor's row and actor ID with the flag false and identity metadata empty. Map the flag in `AdminAiConversationLogsController`.
2. [x] Add the flag to `admin-client.ts` and gate the user button in `ai-conversation-logs-panel.tsx` on an explicit true value. Show «حساب کاربری در دسترس نیست» as plain text for unresolved rows. Retain the question detail action, existing label fallbacks for confirmed accounts, and table filters and counts. An omitted flag is treated as unverified during a mixed-version rollout. Deploy the compatible API before the frontend where deployment is staggered.
3. [x] In the shared Customer 360 dialog in `admin-page.tsx`, translate the expected typed tenant-scope or resource-not-found responses to a localized unavailable-account message if the selected account changes after listing. Handle either rejection order and preserve other errors. Keep both user endpoint authorization policies, tenant checks, and credit permissions.
4. [x] Add focused backend and frontend regressions. Update the bug status and document the additive API field and any verification limits. A later release follows the repository's separate release rules.

## Verification

- [x] Use the Feature 164 integration fixtures to confirm true for a tenant member with no usable display fields and false for a gateway actor or an identity that belongs only to another tenant. Confirm no cross-tenant profile fields are exposed in either endpoint, and verify the list's row count and the turn detail remain accessible.
- [x] Verify in Vitest that an unresolved or unverified row keeps its detail action and issues no Customer 360 requests. A confirmed unnamed row remains clickable. Simulate both expected endpoint errors after a confirmed row is listed and confirm the localized dialog message.
- [x] Run the focused Feature 164 backend integration tests, the admin frontend tests, and the frontend build. Compare relevant failures with `specs/test-baseline.md` and record any checks that did not run.

## Impact

The log response gains one field. Existing Identity membership records support the bounded lookup. The change requires no schema migration, new permissions, or change to the stored conversation records.

## Verification evidence

- Release tests for Feature 164 passed, 16 of 16 with no skips. The PostgreSQL cases verified tenant-scoped attribution in both the list and detail, retained question and answer content, and unchanged row counts.
- The new UI regressions passed, including an unverified row with the field missing, an explicitly unavailable account, a confirmed unnamed account, and the expected 403 and 404 responses after listing. The admin suite also verified that other errors retain their original details.
- The grouped admin run passed 38 of 39 tests. One unchanged user-analytics test hit the five-second timeout while the build and backend tests were running. All four user-analytics tests passed in isolation.
- Frontend client and SSR builds passed. Lint rules other than formatting passed for all changed frontend files. Type checking reported the existing 37 diagnostics and none in changed files.
