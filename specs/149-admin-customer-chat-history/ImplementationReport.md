# Feature 149 — Implementation Report

## Verdict

**FEATURE_IMPLEMENTED_WITH_BLOCKERS**

The admin read APIs, Customer 360 tabs, safe lazy detail view, and focused test coverage are implemented. PostgreSQL execution/query-plan evidence is still missing, and the latest integration-test rebuild is blocked by an unrelated Feature 148 compile error in the current worktree.

## Feature location

`specs/149-admin-customer-chat-history/` contains the feature specification, design, design review, task list, and this report. Design review findings F149-DR-01 through F149-DR-04 were resolved before implementation.

## Architecture findings

- V1 and MAF V2 share `ConversationDbContext`, `ConversationRow`, and `MessageRow`; no second history store was introduced.
- Each persisted `User` message anchors a turn. Eligible `Assistant` messages follow it under `(CreatedAt, Id)` ordering until the next `User` message. Multiple assistant messages are returned in order; incomplete turns remain visible.
- Tenant and user ownership are enforced by joining each question to its conversation and filtering both `TenantId` and `ActorId` in the database query.
- Existing indexes inspected: conversations `(TenantId, ActorId, UpdatedAt)` and messages `(ConversationId, CreatedAt)`. No migration or index was added. Representative PostgreSQL plans remain to be checked.
- The existing Customer 360 modal was used as the UI reference; no screenshot was attached in the request context.

## Backend endpoints and controls

- `GET /api/v1/admin/users/{userId}/chat-history?page=1` returns server-paged 20-turn pages, exact count, total pages, newest-first order, and bounded summaries.
- `GET /api/v1/admin/users/{userId}/chat-history/{turnId}` returns the complete persisted question and assistant content on demand.
- Both endpoints require the existing `admin.users.read` policy. Cross-user or out-of-tenant detail IDs return 404.
- Full-content detail reads write a content-free `SecurityAdminAuditRow` before loading the answer. Audit failure prevents the response. No question, answer, tool input, or payload content is written to the audit or logs.
- System/tool roles and `ScannerQueryPlanJson` are excluded. Assistant payloads are allow-listed to public response fields; usage, plans, and unrelated internal fields are filtered.

## Frontend changes

- Added Account Information and Chat History tabs to the existing Customer 360 dialog. Account Information remains the default, and the existing account view remains in place.
- Chat History requests one server page at a time, fetches turn details only after expansion, resets state on user change, and supports keyboard expansion, page controls, loading/error/empty states, RTL, long content, and responsive wrapping.
- Stored Markdown uses the existing `MarkdownMessage` component, which applies `rehype-sanitize`; structured payload is shown as escaped JSON text.

## Tests and build results

- `dotnet build src/backend/FinancialCopilot.API/FinancialCopilot.API.csproj --no-restore`: passed, 0 warnings and 0 errors.
- Focused prebuilt `AdminManagementEndpointTests`: passed, 10/10, including 20-item paging, multi-assistant/incomplete turns, internal role exclusion, permissions, cross-user detail isolation, and content-free audit assertions.
- `npx vitest run src/components/admin/__tests__/admin-customer-chat-history.test.tsx --pool=vmThreads --maxWorkers=1`: passed, 4/4.
- `npm run build`: passed for client and SSR bundles. Vite reported existing route-file and large-chunk warnings.
- Focused ESLint for the changed admin UI, client, and tests: passed.
- A fresh integration test rebuild currently fails before test execution in the unrelated untracked `tests/FinancialCopilot.IntegrationTests/Feature148ChatProviderE2ETests.cs:552` because `NoOpScannerCache` is missing. The test suite was run successfully from the last built assembly; the latest equal-timestamp assertion is present in source but was not included in that prebuilt run.

## Acceptance criteria

| Criteria | Result |
|---|---|
| Two tabs; existing account view preserved; account tab default | Implemented; production frontend build passed |
| Newest-first list with stable `(CreatedAt, Id)` order and summaries | Implemented; focused API test passed for ordering and paging |
| Server-side 20-item pagination, current page and controls | Implemented; API pagination test and frontend paging test passed |
| Full question and all stored assistant messages fetched on expansion | Implemented; multi-assistant detail API test passed |
| Detail ownership constrained to immutable user and tenant | Implemented; cross-user detail test passed |
| Non-user/assistant messages and internal plan fields excluded | Implemented; API assertions passed |
| Incomplete turns visible without regeneration | Implemented; API assertion passed |
| Full-content access audited without content | Implemented; API audit assertions passed |
| Empty/loading/error states and user-switch reset | Implemented; frontend empty/error/user-switch tests passed |
| Safe Markdown/structured rendering, RTL, responsive content | Implemented; existing sanitized renderer reused; build passed |
| Existing persistence, retention, and deletion behavior preserved | Implemented; no schema or migration change |

## Outstanding blockers

1. Run the integration suite from source once the separate Feature 148 `NoOpScannerCache` compile error is resolved or excluded by its owner.
2. Execute the history queries against PostgreSQL, verify `Guid.CompareTo` translation, query counts, execution plans, and bounded behavior with large histories. In-memory tests do not establish provider-specific SQL performance.
3. Add/execute an Admin dialog-level UI test covering tab switching and preservation of the existing account panel; the new component tests cover history loading, paging, expansion, empty/error state, and user switching.
