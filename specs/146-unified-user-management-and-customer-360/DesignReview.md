# Feature 146 — Focused Design Review

## Executive verdict

**NEED_CHANGES** — The design is strong on source ownership, tenant isolation, privacy, and compatibility. One major query-boundedness gap remains for optional Billing filters in the directory. Resolve that filter contract before implementation of the directory query. Two minor contract details should also be fixed at the planned 146-T01 read-contract gate.

| Measure | Result |
|---|---:|
| Blockers | 0 |
| Major findings | 1 |
| Minor findings | 2 |
| Note findings | 0 |
| Acceptance criteria | 17 Covered, 4 Partial, 0 Missing |

## Findings

### F146-DR-01 — MAJOR — Billing-filter candidate scans are not bounded

- **Document/section:** `Design.md` §Directory API; `tasks.md` 146-T03.
- **Repository evidence:** `EfCoreAdminManagementService.SearchUsersAsync` scopes candidates through tenant membership, orders them, and takes a caller limit (`src/backend/FinancialCopilot.Infrastructure/Administration/EfCoreAdminManagementService.cs:25-42`). Billing ownership is a separate context; the account table has a unique `(TenantId, UserId)` index (`src/backend/FinancialCopilot.Infrastructure/Billing/Persistence/BillingEntityConfigurations.cs:8-19`).
- **Scenario:** A tenant has many users and an administrator filters for a rare or absent plan/credit state. The proposed algorithm keeps reading ordered Identity candidates and batching Billing reads until it fills a page or exhausts the tenant. With no match, one request can traverse the entire tenant, so page size alone does not bound work. The documents list these Billing filters as optional API parameters and AC-04 requires combined supported filters, while AC-19 requires bounded query execution.
- **Minimal correction:** Freeze an MVP allow-list that excludes `planCode`, `subscriptionStatus`, and `creditState` from directory filtering, while still showing Billing summaries on returned rows. Defer those filters, or specify a queryable projection/join and measured indexes plus a hard per-request candidate/work budget and explicit continuation behavior. Update AC-04 and 146-T03 to match the selected contract.
- **Blocks:** Yes — blocks implementation of the current Billing-filtered directory contract until the query strategy and supported filter set are bounded.

### F146-DR-02 — MINOR — Composite detail route does not choose a permission contract

- **Document/section:** `Design.md` §Customer 360 APIs and §Authorization; `tasks.md` 146-T01 and 146-T04.
- **Repository evidence:** The proposed `GET /api/v1/admin/users/{userId}/customer-360` combines identity, subscription, credits, and purchase data (`Design.md` lines 109-123). Authorization assigns those sections to different permissions, then leaves the route choice as “require all relevant claims or split the endpoint” (`Design.md` lines 138-144). The task list expects permission-combination tests but does not select either contract (`tasks.md` lines 7-15, 37-44).
- **Scenario:** An operator with `admin.users.read` but without `admin.subscriptions.read` opens the user detail. Under an all-claims policy the whole overview is denied; under a users-only policy the composed response risks returning Billing fields. The current UI’s per-tab permission behavior cannot determine which response to expect.
- **Minimal correction:** At 146-T01, choose and document either a single route requiring every permission for every included field, or split identity, subscription, credits, and purchases into routes protected by their respective permissions. Define the response behavior for partially authorized users.
- **Blocks:** Yes — blocks 146-T04 route implementation until 146-T01 freezes the contract; the planned task order already provides this gate.

### F146-DR-03 — MINOR — “Active session” needs refresh-session semantics

- **Document/section:** `Design.md` §Customer 360 APIs, Identity item.
- **Repository evidence:** Existing admin session revocation selects unrevoked `AuthDbContext.RefreshTokens` rows and stamps `RevokedAt` (`src/backend/FinancialCopilot.Infrastructure/Administration/EfCoreAdminManagementService.cs:79-97`). The design asks for an “active-session count/status” but does not define expiry or whether it describes refresh sessions or all currently accepted access tokens (`Design.md` line 114).
- **Scenario:** Expired but unrevoked refresh-token rows can be counted as active if the detail query only checks `RevokedAt`; operators may also read “active sessions” as a statement about current access-token validity.
- **Minimal correction:** Define the field as active refresh sessions, count only rows with `RevokedAt == null` and `ExpiresAt > asOfUtc`, and avoid claiming access-token validity unless that is independently observable.
- **Blocks:** No — resolve in the detail DTO definition before UI implementation.

## AC-01–AC-21 traceability

| AC | Status | Review concern / evidence |
|---|---|---|
| AC-01 | Covered | Effective-tenant scope and target-user membership checks are explicit in `Design.md` §Authorization. |
| AC-02 | Covered | Default/max page size, deterministic keyset sort, scoped opaque cursor, and continuation are specified. |
| AC-03 | Covered | GUID, normalized email, and provider-scoped external-ID lookup are specified; sensitive national-ID/mobile search and deferred TahlilApp name search are excluded. |
| AC-04 | Partial | Billing filters are named, but the cross-context candidate scan has no request work bound; see F146-DR-01. |
| AC-05 | Covered | The story limits counts to reliable distinct-user/provider counts and avoids claims that cannot be queried. |
| AC-06 | Covered | Acquisition source is preserved across native credential enrollment and Telegram linking. |
| AC-07 | Covered | Historical source defaults to Unknown and unavailable timestamps remain null. |
| AC-08 | Covered | Provider links are read as relationships to existing local users; no read-side provisioning is proposed. |
| AC-09 | Partial | The fields are listed, but “active session” lacks the refresh-token definition and expiry rule; see F146-DR-03. |
| AC-10 | Covered | Provider namespace/status/timestamps and masked external IDs are specified; Telegram username is display metadata. |
| AC-11 | Covered | Subscription/entitlement source and freshness are required, with Unknown provenance rather than inference. |
| AC-12 | Covered | Spendable, reserved, credit-line, allocation, and expiry distinctions are preserved as Billing-owned values. |
| AC-13 | Covered | Credit/usage history reads immutable Billing ledger and period sources. |
| AC-14 | Covered | Only local checkout/financial evidence is treated as local purchase evidence; entitlement is kept separate from revenue. |
| AC-15 | Covered | Credentials, token values, ciphertext, and unmasked TahlilApp profile values are excluded. |
| AC-16 | Covered | Existing commands and authorization/audit protections are retained and reused. |
| AC-17 | Partial | Backend permission and tenant checks are required, but the composed detail route’s permission contract is undecided; see F146-DR-02. |
| AC-18 | Covered | Existing users endpoint/DTO are explicitly kept compatible; new routes are additive. |
| AC-19 | Partial | Batched reads avoid N+1, but the candidate loop for sparse/no-match Billing filters can scan the full tenant; see F146-DR-01. |
| AC-20 | Covered | Feature 139 state is presented with source/sync freshness and not described as live-verified. |
| AC-21 | Covered | The planned test matrix covers identity combinations, authorization, tenant isolation, billing distinctions, paging, concurrency, and compatibility. |

## Review scope and limits

Reviewed `ExistingCapabilitiesAndGapAnalysis.md`, `UserStory.md`, `Design.md`, `tasks.md`, and `OpenQuestions.md` against repository implementations for Admin Management, Identity session persistence, Billing account ownership, and the stated prior-feature constraints. The review is design-only: no production/spec source was changed, and no tests or migrations were run. Implementation and runtime behavior remain unverified by this review.
