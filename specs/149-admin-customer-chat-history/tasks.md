# Feature 149 — Tasks

## 149-T01 — Add admin history contracts and endpoints

- Add paged list and turn detail DTOs, application contracts, and routes under the existing admin users API.
- Require the existing `AdminUsersRead` policy and pass actor tenant/admin ID to the read service.
- Acceptance: both routes are authenticated, permission-protected, user-scoped, and preserve existing routes.

## 149-T02 — Implement bounded conversation history queries and audit

- Extend existing admin infrastructure read service with `ConversationDbContext` access.
- Page question anchors in SQL, deterministic `(CreatedAt, Id)` ordering, compute exact counts, and derive summaries without loading complete histories.
- Scope every query to conversation tenant and immutable actor ID; detail ownership check uses both before content retrieval.
- Return only user and assistant content; exclude tool/system/developer/scanner-plan data; include assistant payload only as structured data.
- Persist content-free administrative detail audit event before returning full content, failing closed on audit-write failure.
- Acceptance: efficient bounded query count and no schema change unless verified necessary.

## 149-T03 — Extend Customer 360 client and dialog

- Add typed list/detail client methods and server pagination contracts.
- Add Account Information and Chat History tabs, keeping current account subtree unchanged and first tab selected by default.
- Implement lazy list/detail loads, 20-item paging, keyboard accessible expansion, error/loading/empty states, and user-change resets.
- Render only with a verified safe renderer or text-only fallback; maintain RTL and responsive layout.

## 149-T04 — Add backend and frontend verification

- Add focused tests for pagination, pairing, incomplete turns, filtering internal messages, tenant/user isolation, authorization, and auditing.
- Add UI tests for tabs, preserved existing view, loading/empty/error, paging, expansion, user switch, keyboard access, safe rendering, and RTL/responsive behavior.
- Run focused backend tests and available frontend checks. Report skipped/unavailable test infrastructure without claiming evidence.

## 149-T05 — Final review and implementation report

- Review authorization, privacy, grouping, stable ordering, query shape, safe rendering, backward compatibility, and all acceptance criteria.
- Create `ImplementationReport.md` with actual changed files, endpoints, database/migration findings, tests and results, acceptance status, outstanding issues, and verdict.
