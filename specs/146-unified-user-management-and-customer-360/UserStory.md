# Feature 146 — Unified User Management and Customer 360

## Product goal

Give authorized operations staff one tenant-scoped view of a local SapioAI user, the user's original acquisition attribution, linked identity providers, current Billing state, locally recorded purchases, and credit history. Keep each system of record authoritative and preserve existing login, Billing, privacy, and audit rules.

## Personas

- **Support operator:** finds a local user, understands how the account was created, sees safe identity and entitlement status, and explains a purchase or credit event.
- **Identity administrator:** reviews authentication state, provider links, account status, sessions, and roles; uses existing audited account actions.
- **Billing administrator:** reviews a current subscription, credit capacity and history, locally recorded checkout/payment evidence, and uses existing Billing mutations.
- **Product operator:** later evaluates user and acquisition trends from precise distinct-user metrics without treating external entitlements as revenue.

## User stories

1. As a support operator, I can find a local user with bounded server-side search and filters so I can support the correct account without exporting the whole user table.
2. As an administrator, I can distinguish a user's original acquisition source from the providers currently linked to the account and from the provider used at last successful sign-in.
3. As an administrator, I can inspect native credential availability, email verification, local account status, tenant, provider-link status, and last successful login without seeing passwords, hashes, tokens, or unmasked sensitive profile values.
4. As a Billing administrator, I can inspect the local current subscription, wallet/reservation summary, credit allocations and history, and any local checkout/payment evidence for the user's Billing account.
5. As an administrator, I can use existing authorized status, unlock, session-revocation, role, subscription, or credit-adjustment controls with the same owner services, reasons, idempotency, and audit rules already in place.
6. As a product operator, I can later see acquisition and usage metrics that count local users, linked identities, external entitlements, and purchases separately.

## Scope

### MVP — Operational Customer 360

- Extend Identity metadata to record local account creation time, original acquisition source, and last successful sign-in time/provider.
- Add a tenant-scoped, cursor-paginated user directory with supported Identity/provider filters and permission-aware Billing summaries on returned rows.
- Add a Customer 360 overview with local account and provider-link summaries, current subscription, wallet/credit summary, and data-source/freshness indicators.
- Show paginated Billing credit/usage ledger history and locally recorded checkout/payment history.
- Enhance the existing React Admin user workflow with a directory and user detail route/tabs.
- Reuse existing admin permissions, account actions, subscription controls, credit adjustments, and audit infrastructure.
- Keep TahlilApp national ID/mobile masked; do not add reveal or search.

### Phase 2 — Usage and customer analytics

- Usage summaries by date range, operation and capability; last activity and utilization.
- Subscription history where an authoritative history projection is available.
- Distinct-user acquisition cohorts, provider-link metrics, active/paying definitions, and credit-consumption trends.
- Advanced saved filters, audited export, and a lightweight customer-operations dashboard.

### Future

- Noavaran Amin authentication, only after a separate provider contract and security review.
- General-purpose identity-linking requests and user-initiated link/unlink flows beyond existing provider workflows.
- CRM, support case management, customer communication, and advanced revenue attribution.

## Non-goals

- Implementing Noavaran Amin authentication or an external identity contract.
- Automatically merging local accounts by email, mobile, or national ID.
- Replacing Feature 035 admin APIs, Feature 037 panel, Feature 087 Telegram linking, Feature 139 TahlilApp exchange, or Feature 145 email verification.
- Adding parallel user/Billing tables, a data warehouse, microservice, or separate frontend.
- Editing wallet balances, bypassing Billing ledger commands, or overwriting TahlilApp-owned subscription state.
- Exposing AI conversation content to administrators by default.
- Treating external subscription entitlements, grants, credit allocations, or checkout intent as SapioAI revenue.
- Searching encrypted mobile or national-ID fields or adding plaintext lookup columns.

## Acceptance criteria

### Directory and identity

- **AC-01** The directory returns only users in the administrator's effective tenant unless an existing explicitly authorized platform scope is present.
- **AC-02** Directory requests use stable keyset pagination, a default page size of 50, a maximum of 100, deterministic sort order, and a continuation cursor; no request returns an unbounded user list.
- **AC-03** Search supports internal user ID, normalized email, and provider-scoped external ID lookup where the underlying provider index supports it. TahlilApp profile name search stays disabled until Feature 139 profile access is approved and implemented; national ID and mobile are never search fields.
- **AC-04** MVP directory filters include account status, original acquisition source, linked provider, local registration date, and last successful login date. `planCode`, subscription state, and credit state are not directory filters in MVP; current Billing summaries may be displayed on returned rows when authorized. Unsupported/unknown values are represented explicitly rather than inferred.
- **AC-05** Any count shown by the directory or later analytics distinguishes distinct local users, linked-identity rows, and distinct users linked to a provider. One user with two providers remains one local user; MVP does not claim counts it cannot query reliably.
- **AC-06** Adding native credentials to a TahlilApp-created account does not change acquisition source. Linking Telegram does not make Telegram the login provider or acquisition source.
- **AC-07** Historical users without a reliable source have acquisition source Unknown. Historical registration/login timestamps are null or explicitly unavailable when the repository cannot establish them.
- **AC-08** A provider link is displayed as a relationship to the existing local user; it never creates a second local user during an admin read.

### Customer 360

- **AC-09** The detail view displays local user ID, in-scope tenant memberships, account status, acquisition source, local account creation time, last successful sign-in time/provider, native credential availability, email-verification state, and Active Refresh Sessions for the effective tenant without returning raw tokens. Active Refresh Sessions count distinct refresh-token rows where `UserId` and `TenantId` match the detail scope, `RevokedAt` is null, and `ExpiresAt` is later than response `asOfUtc`; this is not online presence or a count of currently usable access tokens.
- **AC-10** Linked providers display provider namespace, provider-link status, linked/revoked time when present, and a safely displayed external identifier. Telegram username remains display metadata.
- **AC-11** Current plan, effective dates, entitlements, and external-sync status come from Billing and Identity-owned sources and include source/freshness indicators. Unknown provenance is displayed as Unknown.
- **AC-12** Credit capacity distinguishes spendable/available credits, reserved credits, approved credit line where applicable, expiring allocation periods, and source. No view calculates balance by summing heterogeneous records.
- **AC-13** Credit and usage history is read from Billing-owned ledger/period sources and remains immutable in the admin UI.
- **AC-14** Purchase history contains only local Billing checkout/financial records, including status, amount/currency, channel/provider and reconciliation state when recorded. A completed local purchase count is shown only when its source records are complete. External entitlements and TahlilApp purchases are labeled separately or shown as unavailable; they are never counted as local purchase revenue.
- **AC-15** Customer 360 does not expose conversation bodies, credentials, token values, raw profile ciphertext, or unmasked TahlilApp profile data.

### Administration, security, and compatibility

- **AC-16** Existing enable/disable/unlock, session revoke, role assignment, subscription management, and credit-adjustment actions continue to call existing authorized services and retain audit, reason, idempotency, tenant, and concurrency protections.
- **AC-17** The Customer 360 route requires `admin.users.read` for base Identity data. The backend checks `admin.subscriptions.read`, `admin.usage-ledger.read`, and `admin.billing-audit.read` independently before reading their respective optional sections. Each optional section reports `available`, `unavailable`, or `not_authorized`; `not_authorized` contains no data. Credit and purchase history routes enforce their corresponding backend permissions. All reads verify target-user tenant membership; frontend guards only control visibility and never replace backend authorization.
- **AC-18** The existing /api/v1/admin/users response and current admin consumers remain compatible; new page/detail contracts are additive.
- **AC-19** A single tenant-scoped Identity/provider query applies all MVP directory filters before keyset paging and returns at most `pageSize + 1` rows. No Billing filter is post-applied to a page. Identity/provider and authorized Billing summaries use set-based batches for at most 100 selected users, not per-user N+1 calls. The Identity page query has a five-second PostgreSQL statement timeout and never produces a partial page or cursor.
- **AC-20** Feature 139 operational incompleteness is shown as source/sync freshness state. The UI does not claim live TahlilApp status until staging contract checks and required key-ring/migration gates pass.
- **AC-21** Automated tests cover native-only, TahlilApp-only, TahlilApp plus native credentials, multiple links, unknown provenance, revoked links, collision rejection, tenant isolation, authorization, masking, billing distinctions, supported-filter pagination and query plans, Active Refresh Sessions across revoked/expired/rotated/replayed cases, duplicate `TokenHash` rejection, concurrency, and backward compatibility.

## Out of scope release gates

Feature 146 development does not apply migrations or verify a live database. Deployment still requires normal Auth/Billing migration review, applicable Feature 139 TahlilApp contract and Data Protection key-ring gates, and Feature 145 SMTP staging gates when the release includes those flows.
