# Feature 146 — Implementation Tasks

This plan covers only repository gaps. Existing Admin Management, Identity, Billing, and Admin UI features are reused rather than rebuilt.

## Slices

### 146-T01 — Freeze the read contract and permission map

- **Complexity:** S
- **Dependencies:** None
- **Expected changes:** Freeze the directory allow-list to Identity/provider filters and explicitly exclude `planCode`, `subscriptionStatus`, and `creditState` filters from MVP. Confirm cursor semantics, Customer 360 section envelopes, source/freshness labels, tenant-resolution rules, and per-section permission requirements against Feature 035 contracts. The Customer 360 route requires `admin.users.read` for base Identity data and checks existing Billing permissions independently for optional sections. Keep current GET /api/v1/admin/users contract intact.
- **Relevant components:** Feature 146 Design.md; src/backend/FinancialCopilot.Application/Administration/AdminManagementContracts.cs; src/backend/FinancialCopilot.API/Controllers/AdminManagementController.cs.
- **Tests:** Contract-level policy matrix for base access, each optional-section permission, `not_authorized` versus `unavailable`, and legacy response compatibility design.
- **Acceptance mapping:** AC-01, AC-02, AC-04, AC-16–AC-20.
- **Definition of Done:** API request/response fields, supported filter allow-list, five-second directory statement timeout/error behavior, cursor semantics, tenant checks, per-section authorization/error states, and permission combinations are approved in code review before implementation tasks begin.

### 146-T02 — Add Identity operational metadata and write paths

- **Complexity:** M
- **Dependencies:** 146-T01
- **Expected changes:** Add one-to-one Auth metadata for local registration time, acquisition source, last successful login time/provider, and concurrency. Add an additive EF migration, including an Auth membership lookup index beginning with `(TenantId, UserId)`, a status lookup index, metadata indexes for registration/source/login filters and keyset order, and a tenant-scoped provider-membership index where needed. Backfill existing users to acquisition unknown and nullable historical timestamps. Set native during direct registration and tahlilapp only when a local account is first provisioned by TahlilApp. Update successful sign-in paths; do not update on refresh, link, or failed authentication.
- **Relevant components:** src/backend/FinancialCopilot.Infrastructure/Authentication/Persistence/AuthPersistenceModels.cs; AuthDbContext.cs; new Auth migration; OwnedIdentityService.cs; src/backend/FinancialCopilot.Infrastructure/Authentication/TahlilApp/TahlilAppAccountProvisioner.cs; registration/auth contracts.
- **Tests:** Source stays unchanged during native credential enrollment; historical default Unknown/null; login metadata updates only after success; concurrent login updates preserve the latest event; no behavior change to Feature 139/145 sessions.
- **Acceptance mapping:** AC-06, AC-07, AC-09, AC-20.
- **Definition of Done:** Migration is additive, no PII columns/indexes are added, each account-creation path writes metadata transactionally, older/missing rows safely read Unknown/null, and identity regression tests pass.

### 146-T03 — Implement tenant-scoped cursor-paged directory query

- **Complexity:** L
- **Dependencies:** 146-T01, 146-T02
- **Expected changes:** Add directory query/filter DTOs and an Admin query service that performs one tenant-scoped, `AsNoTracking` Identity/provider query. Apply every supported filter before keyset ordering and `Take(pageSize + 1)`. Exclude plan/subscription/credit-state filters from the MVP request contract. After selecting the page, batch-load roles, memberships, linked providers, and authorized Billing summaries for at most 100 user IDs. Keep existing SearchUsersAsync endpoint and DTO unchanged.
- **Relevant components:** src/backend/FinancialCopilot.Application/Administration/AdminManagementContracts.cs; Admin Management service contract; src/backend/FinancialCopilot.Infrastructure/Administration/EfCoreAdminManagementService.cs or a focused sibling query service; src/backend/FinancialCopilot.API/Contracts/AdminManagementContracts.cs; AdminManagementController.cs.
- **Tests:** Page bounds, cursor stability, combined supported Identity/provider filters, unknown date/source handling, tenant isolation, provider-scoped external lookup, no national ID/mobile search, no post-page filtering or application-side candidate loop, no N+1 query behavior, and no duplicate local users when providers are linked. Verify supported filter plans at representative scale; on query timeout return no page or continuation cursor.
- **Acceptance mapping:** AC-01–AC-08, AC-19.
- **Definition of Done:** Every supported filter is applied by the same database query before paging; returned pages are deterministic and complete, cursor cannot be reused across tenant/filter/sort scope, the Identity query uses a five-second statement timeout and never returns partial rows/cursors, no application-side scan is used, and page size is bounded at 100.

### 146-T04 — Add Customer 360 identity and current-state composition

- **Complexity:** M
- **Dependencies:** 146-T01, 146-T02
- **Expected changes:** Add GET /api/v1/admin/users/{userId}/customer-360 and a read-only composition DTO for safe identity state, normalized provider-link rows, current account and subscription, plan capabilities, wallet summary, TahlilApp sync state, and per-context freshness. Require `admin.users.read` for the base response; check existing Billing permissions independently and return `available`, `unavailable`, or `not_authorized` section envelopes, without querying a source for a section the caller cannot read. Verify target-user tenant membership before reading Billing. Define Active Refresh Sessions as distinct rows scoped to the target UserId and effective TenantId with `RevokedAt` null and `ExpiresAt` later than response `asOfUtc`; rotated/expired/revoked rows are excluded, duplicate token hashes are prevented by the existing unique index, and the count does not imply online presence.
- **Relevant components:** src/backend/FinancialCopilot.Application/Administration/AdminManagementContracts.cs; Admin Management query service; src/backend/FinancialCopilot.Infrastructure/Authentication/Persistence/TahlilAppPersistenceModels.cs; TelegramAccountLinkRow query; src/backend/FinancialCopilot.Infrastructure/Billing/Persistence/BillingPersistenceModels.cs; API contract/controller.
- **Tests:** Native/TahlilApp/Telegram combinations, multiple providers, revoked/suspended links, unknown subscription source, missing Billing account, failed context reads, masked PII, object-level tenant authorization, base versus optional-section permissions, no source query for `not_authorized`, and `unavailable` versus `not_authorized`. Cover Active Refresh Sessions for unexpired, expired, revoked, rotated, replay-revoked, multiple distinct tokens, and duplicate-token-hash rejection by the existing unique index.
- **Acceptance mapping:** AC-08–AC-12, AC-15, AC-17, AC-20.
- **Definition of Done:** Read DTO is composed from owning records, has explicit Unknown/stale values, returns no protected secrets/PII, is bounded and AsNoTracking, and introduces no Billing mutations or duplicate source rows.

### 146-T05 — Add paged Billing credit and local-purchase history reads

- **Complexity:** L
- **Dependencies:** 146-T01, 146-T04
- **Expected changes:** Add user-keyed, tenant-checked read endpoints for paged credit/usage history and local checkout/payment history. Enforce `admin.usage-ledger.read` for credit history and `admin.billing-audit.read` for purchase history on the backend. Implement query contracts inside Billing ownership and have Admin orchestrate them. Return ledger/period and checkout/financial evidence with status, source, currency, references, and reconciliation state where persisted. Do not synthesize local payment records for external subscriptions.
- **Relevant components:** src/backend/FinancialCopilot.Billing/Contracts/BillingContracts.cs; src/backend/FinancialCopilot.Infrastructure/Billing/Persistence/BillingPersistenceModels.cs; Billing purchase/query services; BillingDbContext.cs; Admin API contracts/controllers.
- **Tests:** Ledger consistency with wallet projection and period records; reservations vs spendable credit; expiry/transfer/refund; checkout pending/rejected/fulfilled/refunded states; external entitlement versus local purchase; tenant/account ownership and pagination.
- **Acceptance mapping:** AC-12–AC-14, AC-17, AC-19, AC-21.
- **Definition of Done:** Reads do not mutate wallet or checkout state, use Billing services/repositories, are paged, preserve financial source distinctions, and do not claim unsupported revenue.

### 146-T06 — Extend the existing React Admin user experience

- **Complexity:** L
- **Dependencies:** 146-T03, 146-T04, 146-T05
- **Expected changes:** Extend the existing admin client and panel with a server-paged user directory using only the supported Identity/provider filters, a Customer 360 route/detail sections, permission-aware Billing sections, source/freshness labels, explicit `unavailable`/`not_authorized` states, and links to existing safe actions. Do not create a new frontend framework or replace Feature 037 navigation.
- **Relevant components:** src/frontend/src/integrations/financial-copilot/admin-client.ts; src/frontend/src/components/admin/admin-page.tsx or focused admin components; src/frontend/src/routes/admin.tsx; TanStack route tree and route-level authorization patterns.
- **Tests:** Permission-aware links/sections, supported directory filters and cursor mapping, user detail rendering, empty/partial/stale/error/unauthorized states, masked PII, Active Refresh Sessions label, legacy existing panel actions.
- **Acceptance mapping:** AC-02–AC-05, AC-09–AC-18, AC-20.
- **Definition of Done:** Existing Admin 037 workflows continue to work; no sensitive data is persisted in browser storage; all new sections are driven by API permission claims.

### 146-T07 — Complete security, performance, and regression verification

- **Complexity:** M
- **Dependencies:** 146-T02–146-T06
- **Expected changes:** Add focused Identity/Admin/Billing and frontend tests following repository conventions. Review PostgreSQL plans for every supported directory filter combination and history query at representative scale; confirm bounded timeout and no partial page behavior. Document configuration, migration rollout order, cursor invalidation, and known external operational gates.
- **Relevant components:** tests/FinancialCopilot.UnitTests/; tests/FinancialCopilot.IntegrationTests/; src/frontend test setup if available; docs/admin-management-api.md and new feature documentation as appropriate.
- **Tests:** All AC-21 scenarios, PostgreSQL concurrency tests, backward compatibility, authorization, masking, tenant isolation, pagination/query plans, Active Refresh Sessions lifecycle and uniqueness semantics, and distinct local-user/linked-identity counts wherever counts are displayed.
- **Acceptance mapping:** AC-01–AC-21.
- **Definition of Done:** Focused test suites pass; PostgreSQL-only behaviors have PostgreSQL test evidence; supported directory queries are bounded, complete, and N+1-free; optional-section authorization is enforced by the backend; Feature 139/145 external staging checks are reported separately and are not falsely marked verified.

## Dependency order

146-T01 → 146-T02 → 146-T03/146-T04 → 146-T05 → 146-T06 → 146-T07

146-T03 and 146-T04 can proceed in parallel after metadata contracts are stable. 146-T05 depends on the Billing ownership contract established by 146-T04.

## MVP Definition of Done

- AC-01 through AC-20 are met by API and UI behavior.
- Existing Feature 035 commands, permissions, audit, tenant rules, and Billing invariants remain authoritative.
- User attribution and historical dates do not rely on provider-link inference.
- No direct wallet mutation, account merge, plaintext sensitive lookup field, or external purchase/revenue fabrication is introduced.
- Targeted tests and migration SQL review pass in implementation; deployment migration and Feature 139 operational gates are handled by release owners.
- Every code/schema/UI change is limited to approved feature scope and has a corresponding test and acceptance criterion.
