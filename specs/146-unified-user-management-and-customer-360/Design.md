# Feature 146 — Design

## Decisions

1. Extend the existing Admin module and React Admin panel.
2. Compose a read-only Customer 360 view from Identity, provider-link, and Billing sources; each bounded context remains authoritative.
3. Add one small Identity-owned metadata row for missing provenance and sign-in facts. Do not add a duplicate user table or an MVP-wide generic provider-link table.
4. Retain TahlilApp and Telegram identity lifecycle in their current provider-owned models. Normalize them only in the admin query DTO.
5. Existing accounts default to Unknown acquisition source and unknown historical dates where evidence is absent. Provider-link presence is not acquisition evidence.
6. No PII reveal, mobile/national-ID search, account merge, external provider linking command, or Noavaran integration is part of MVP.

## Existing architecture and ownership

Admin Management already composes AuthDbContext, BillingDbContext, UserManager, and Billing services in EfCoreAdminManagementService. The new query belongs in the existing Administration application boundary; controllers remain thin.

| Data | Owner | Current persisted source | Customer 360 behavior |
|---|---|---|---|
| Local user, credentials, enabled/lockout state, roles, tenant membership, sessions | Identity | ASP.NET Identity tables and AuthDbContext rows | Read safe fields through Admin Management. Do not return password/security/token material. |
| Native email-confirmation state | Identity | auth_users.EmailConfirmed and Feature 145 state | Return booleans only. A TahlilApp user may have no email. |
| TahlilApp provider link and lifecycle | Identity | TahlilAppExternalIdentityRow and its migration | Return provider, link state, linked/last-launch times, masked external ID, and sync metadata where appropriate. TahlilApp remains externally unverified until Feature 139 staging gates pass. |
| TahlilApp profile | Identity | UserProfileRow; national ID/mobile protected by Data Protection | All TahlilApp profile values, including names, stay masked or omitted until Feature 139 profile access/reveal policy is completed. No profile reveal/search in MVP. |
| Telegram link and link history | Identity / Telegram link feature | TelegramAccountLinkRow and link audits | Return linked/revoked state, link dates, and safe identifier. Treat Telegram as a channel identity, not native login. |
| Customer account, current subscription, plan capabilities, wallet and ledger | Billing | BillingDbContext entities and services | Query by tenant and CustomerAccountRow.UserId through Billing-owned read contracts. Never calculate or mutate a wallet in Admin. |
| Checkout, payment and reconciliation status | Billing / payment boundary | Billing checkout and financial transaction records | Return only local recorded evidence. External subscription sync is not a local payment. |
| Audit events | Admin, Identity, Billing | Existing immutable security/Billing audit tables | Reuse existing audit reads and target IDs; add a filtered per-user timeline only if separately required. |

### Read path

The request flow is: Admin operator → existing React Admin → Admin Management API → Customer 360 query service → tenant-checked Identity/provider reads plus Billing-owned batched reads → bounded composed response with source and freshness labels.

The directory selects its page from one tenant-scoped Identity/provider query. Only after that query has applied all supported filters and keyset paging may the service batch-load Billing summaries for the selected user IDs. Billing reads are bounded by the maximum page size of 100; never post-filter a directory page or load Billing once per user. Do not introduce background synchronization or a warehouse for MVP.

## Identity, attribution, and authentication

### Local metadata

Add an Identity-side one-to-one row, provisionally UserOperationalMetadataRow in auth_user_operational_metadata:

| Field | Purpose and policy |
|---|---|
| UserId | Primary key and FK to auth_users; one row per local user. |
| RegisteredAtUtc | Time the local SapioAI account was created/first provisioned. Null for existing users when no trustworthy timestamp exists. It is not the upstream TahlilApp registration date. |
| AcquisitionSourceCode | Original local account acquisition attribution. String code initialized once by account-creation path. Initial known values are native, tahlilapp, and unknown; storage and API remain extensible and do not couple source codes to provider names. |
| LastSuccessfulLoginAtUtc | Time of the most recent successful credential/provider authentication. Null for unrecorded history. Token refresh does not update it. |
| LastLoginProviderCode | Provider used for that successful authentication, such as native or tahlilapp. Telegram linking does not update it. |
| ConcurrencyVersion | Optimistic concurrency marker for concurrent login metadata updates. |

Migration backfill sets pre-existing sources to unknown and leaves registration/last-login timestamps null unless an independently verified source exists. It must not derive acquisition from a TahlilApp row, Telegram link, email, mobile, national ID, or role. New direct registration sets native; first local-account provision through TahlilApp sets tahlilapp. Native credential enrollment on an existing TahlilApp account leaves attribution unchanged. Corrections, if later offered, require a separately permissioned audited operation.

Successful authentication pipelines update last-login metadata only after credential/provider and account checks succeed. Update timestamp and provider atomically using event time and concurrency retry/conditional update. Refresh-token rotation, session refresh, profile sync, provider-link creation, failed login, and admin status change are not login events.

The UI labels the timestamp as local SapioAI account creation. It must not imply that a locally provisioned TahlilApp account was created on that date in TahlilApp.

### Linked identity representation

MVP uses a normalized read DTO assembled from the current owning models:

{ providerCode, externalUserIdDisplay, linkStatus, linkedAtUtc, revokedAtUtc, lastAuthenticatedAtUtc?, syncState?, sourceSystem }

- Native credentials are represented as an authentication method on the local Identity account, not a separate external identity row.
- TahlilApp uses its stable provider namespace and external subject; Active/Suspended/Revoked state and subscription synchronization remain owned by Feature 139.
- Telegram is a linked actor/channel identity with stable numeric Telegram user ID. Username is display-only and not a match key.
- The same external ID string under different provider namespaces is not a collision. Provider keys are case-normalized and namespaced by issuer/tenant where that provider defines tenant-local subjects.
- Existing uniqueness rules remain authoritative: TahlilApp identity uniqueness by provider/external subject and provider/local user; active Telegram identity uniqueness by stable Telegram user and actor/tenant.
- Future provider support must use a stable provider namespace and external subject with a uniqueness constraint scoped to the provider's identity namespace. This design does not specify or implement a Noavaran contract.
- A local user may have native credentials and links from multiple providers. A collision belongs to an explicit authenticated link workflow.

Identity linking is not an admin merge operation. A future link workflow must re-authenticate the local account, validate the external proof through its provider-owned service, verify email where the native credential-enrollment flow requires it, and write within a transaction protected by the provider uniqueness constraint. Concurrent claims for one provider subject have one winner; the losing request receives a non-enumerating identity-conflict result and changes neither account. Tenant-local provider subjects include tenant/issuer namespace in uniqueness; globally issued stable subjects use their provider namespace. Existing provider-specific uniqueness rules are preserved.

Account merging is prohibited in this feature. Never merge by email, mobile, or national ID. Existing Feature 139 verified-email enrollment applies only to the current authenticated local account and does not attach a different local user. Explicit multi-account linking/reconciliation remains deferred until owning provider workflows and security policy are approved. External revocation is read from the owning provider state; this feature does not bypass provider-specific revocation or erase tombstones.

## Directory API

Keep GET /api/v1/admin/users and its current list DTO for compatibility. Add an additive directory endpoint:

GET /api/v1/admin/users/directory

Suggested query:

| Parameter | Semantics |
|---|---|
| pageSize | Default 50; range 1–100. |
| cursor | Opaque, integrity-protected keyset cursor; scoped to tenant, sort, and active filters. |
| search | Internal GUID; normalized email; or exact/prefix external ID when provider is also specified. |
| status | enabled, disabled, locked, or any. |
| acquisitionSource | Exact source code, including unknown. |
| linkedProvider | Exact provider namespace; linked identity filter does not change acquisition source. |
| planCode / subscriptionStatus / creditState | Not accepted as directory filters in MVP. Authorized Billing summaries may still appear on each returned row. |
| registeredFromUtc / registeredToUtc / lastLoginFromUtc / lastLoginToUtc | UTC ranges; unknown dates do not match a date range. |
| sort | Allow-listed stable sorts, default registeredAtUtc-desc,userId-asc; email sort only where a normalized key is indexed. |

Return { items, nextCursor, hasMore, asOfUtc }. Each row contains local UserId, masked email/display name, account status, acquisition source, linked provider codes, local registration/last successful login dates, current plan/entitlement state, and a credit-state summary only for administrators authorized for corresponding Billing reads.

Search limits:

- Do not search encrypted mobile or national-ID fields. Feature 139 intentionally has no keyed lookup columns.
- Do not add plaintext national ID/mobile columns or national-ID indexes.
- Email is PII despite current storage. Limit access to the existing admin read permission, avoid logging query text, and use the existing normalized email field plus bounded prefix/exact matching.
- TahlilApp names are profile PII under Feature 139's deferred admin-profile policy. Do not search or display them in MVP. Revisit name search only after the owner approves the profile-read permission, audit behavior, and query/index policy.
- External identity search is exact/prefix, provider-scoped, tenant-scoped, and not exposed in list rows as unmasked values.
- Export is not in MVP.

### Directory query plan and page completeness

`AuthDbContext` and `BillingDbContext` use the same configured `FinancialCopilot` PostgreSQL connection string, but remain separate EF models with separate migration ownership (`FinancialCopilot.Infrastructure/ServiceCollectionExtensions.cs`). A cross-context LINQ join is not available; a targeted SQL join is technically possible. Billing accounts have a unique ownership index on `(TenantId, UserId)` and a single-column foreign-key index on `SubscriptionPlanCode`, but `subscriptionStatus` and `creditState` are not canonical persisted directory fields. Credit state must be interpreted from wallet and allocation-period records. These indexes do not provide a bounded path for every combination of Billing state, Identity/provider filters, and directory ordering. A Billing-first query would still need to join and sort/intersect candidates before returning a correct page, so the three Billing filters are deferred from MVP rather than approximated.

For MVP, the directory filter allow-list is identity/provider only: status, acquisition source, linked provider, supported identifier search, registration date, and last-login date. Keep `planCode`, `subscriptionStatus`, and `creditState` out of the API request contract. The current plan/entitlement and credit summary can still be displayed for the page when the caller has the corresponding read permission; when permission is absent, omit those fields and do not query their Billing sources.

Apply tenant membership and every supported filter in the same `AsNoTracking` Identity/provider query before keyset ordering and `Take(pageSize + 1)`. Do not iterate over Identity candidates, materialize an unbounded user-ID set, or post-filter a page in application code. The cursor binds tenant, sort, filters, and the last sort tuple; registration-date nulls sort last and `UserId` is the unique tie-breaker. Execute the page query with a fixed five-second PostgreSQL statement timeout. A timeout fails the request without an `items` page or continuation cursor; it never returns a partial page. If an optional batched Billing summary read fails or times out, preserve the complete Identity page and mark only the affected Billing section `unavailable`; never use Billing data to remove rows after paging.

After selecting at most 100 user IDs, load provider details and authorized Billing summaries in bounded set-based batches. Resolve each Billing account by both effective `TenantId` and selected `UserId`; the existing Billing unique index on `(TenantId, UserId)` preserves one account per user in that tenant. Directory summaries read only current account/wallet state and the allocation aggregates needed for the summary; ledger and purchase histories remain paged detail reads. Do not sort by balance in MVP. Add/retain Auth indexes for tenant membership `(TenantId, UserId)`, user status `(IsEnabled, LockoutEnd, Id)`, metadata keyset/source `(RegisteredAtUtc DESC, UserId)` and `(AcquisitionSourceCode, RegisteredAtUtc DESC, UserId)`, last-login ranges `(LastSuccessfulLoginAtUtc, UserId)`, and tenant-scoped provider membership; keep existing normalized-email and provider-subject indexes. Before release, verify PostgreSQL plans for supported filter combinations at the expected tenant size under the five-second page-query timeout. If a proposed filter cannot meet that budget with its index path, remove it from the MVP allow-list rather than adding a candidate-scan fallback.

## Customer 360 APIs

Add an additive detail route so the existing basic user endpoint remains stable:

GET /api/v1/admin/users/{userId}/customer-360

Response is a composed, read-only summary:

- **Overview:** UserId, tenant memberships visible in the effective scope, enabled/locked status, acquisition source, local registered date, last successful sign-in time/provider.
- **Identity:** native credential available, email confirmed, Active Refresh Sessions count for the effective tenant, linked provider rows/status, masked external IDs, last launch/login state, profile-data freshness; no credentials or sensitive profile reveal. Native credential-link history is not currently persisted, so only current availability is shown.
- **Subscription:** Billing account ID, account type, current plan, effective dates, revision, current plan capabilities, source indicator, TahlilApp synchronization state and timestamps if applicable.
- **Credits:** wallet projection values for spendable/available and reserved amounts, approved credit line if applicable, source/allocation summary, and next expiry only where periods support it.
- **Purchases:** recent local checkout/payment summary and a completed local purchase count only when its records are complete/reconciled; external entitlement records shown separately or as unavailable. Do not show total revenue from entitlement data.
- **Provenance:** sourceSystem, asOfUtc, and a safe freshness status for each context. A field with no reliable source is Unknown/unavailable rather than inferred.

Each optional protected section uses a response envelope with `status: available | unavailable | not_authorized`. `available` carries the section data. `unavailable` means the caller has the section permission but its source has no usable data or could not be read; include a safe reason/source-freshness value where known. `not_authorized` carries no data and is returned without querying that section's source. Missing users and users outside the effective tenant return the same not-found outcome. A missing permission on an optional section does not deny the authorized base Identity response.

Active Refresh Sessions are calculated from `auth_refresh_tokens` at the response `asOfUtc`: include distinct rows for the user and the effective tenant only when `RevokedAt IS NULL` and `ExpiresAt > asOfUtc`. A read never changes token state. Expired rows are excluded even if an older row has not yet been stamped revoked; the refresh flow stamps an encountered expired row. During rotation, the previous row is revoked and linked by `ReplacedByTokenId`, so only the unexpired replacement can count. Reuse of a rotated token triggers the existing token-family revocation behavior. `TokenHash` has a unique database index, so duplicate records for the same token are rejected; different valid refresh-token rows for one user and tenant count as separate sessions. This is refresh capability only and must not be presented as online presence or access-token validity. Existing admin revocation semantics remain unchanged.

Add paged history endpoints:

- GET /api/v1/admin/users/{userId}/customer-360/credits?pageSize&cursor
- GET /api/v1/admin/users/{userId}/customer-360/purchases?pageSize&cursor

Credit history is a projection of existing immutable Billing ledger and subscription-credit-period records. Preserve original entry types, operation, source allocation, expiry, related entry, status, and timestamps. Purchases are a Billing-owned projection of checkout, financial transaction, receipt review, refund, and reconciliation state. A checkout intent is not a completed payment; report it with its recorded state. Do not create a local purchase from a TahlilApp entitlement.

Existing endpoints remain the mutation/source APIs:

- Subscription reads/writes: /api/v1/admin/customers/{customerAccountId}/subscription
- Usage/credit ledger: /api/v1/admin/customers/{customerAccountId}/usage-ledger
- Adjustments: /api/v1/admin/customers/{customerAccountId}/credit-adjustments
- Status/session/roles and audits: existing /api/v1/admin/users/* and /api/v1/admin/audits/*

The Customer 360 client may call existing routes after it obtains an authorized customer account identifier. The new user-keyed history endpoints are needed where existing reads do not provide a paged user entry point or where the frontend should not resolve context-owned IDs.

### Authorization

- Directory and identity detail use admin.users.read.
- Subscription fields use admin.subscriptions.read.
- Wallet and ledger fields use admin.usage-ledger.read.
- Purchase/payment/audit fields use admin.billing-audit.read.
- `GET /api/v1/admin/users/{userId}/customer-360` requires `admin.users.read` for the base Identity section. For each optional section, the backend checks its existing Feature 035 permission before reading its source and returns the section envelope described above; an absent permission produces `not_authorized` with no section data. The credits history route requires `admin.usage-ledger.read`; the purchases history route requires `admin.billing-audit.read` and returns the existing permission-denied response when absent.
- Directory Billing summary fields use the same section-level backend permission checks; omit unauthorized summary fields and do not query their sources. Frontend visibility is only a usability guard and never grants access. Do not widen existing Feature 035 permissions or route policies for mutations.
- Existing actions retain exact Feature 035 permissions for user status/session, roles, subscription, and credit adjustment.
- Verify effective tenant membership for the target UserId before returning Identity or Billing data. Never trust a client-supplied CustomerAccountId without resolving it to that tenant and user. Non-member, other-tenant, or missing-user outcomes must not disclose whether an external account exists.
- Admin read access to encrypted Tahlil profile data remains masked. Feature 139 future audited reveal work is not silently enabled here.

## Frontend design

Extend the existing /admin experience:

- Add a user-directory page/route using the existing RTL layout, typed admin client, auth refresh, correlation IDs, and permission checks.
- Search row: local ID/email, account status, acquisition source, linked provider(s), registered/last-login time, and authorized current plan/credit summaries. Plan/subscription/credit values are display-only in MVP. TahlilApp profile names remain unavailable until the profile-access decision is resolved.
- Filters: account status, acquisition source, provider, registration/login date. Plan/subscription/credit filters are excluded from the MVP directory; authorized Billing summaries remain visible on returned rows.
- User detail route: Overview, Identity, Subscription, Credits, Purchases, and Audit navigation. The client renders each API section status as available, unavailable, or not authorized; hiding a tab is only a usability choice and is not an authorization boundary.
- Overview cards show local-source/freshness labels. Distinguish local payment from external entitlement sync and admin grant.
- Existing enable/disable/unlock, session revoke, role, subscription, adjustment, and audit controls remain existing components/contracts or links; no new mutation semantics are introduced.
- Include loading, empty, partial-data, denied, stale, network-error, invalid-cursor, and missing-user states. Render partial context failures with source labels instead of fabricating an empty balance or no identity.
- Do not persist sensitive profile values or admin audit payloads in browser storage. Conversation content is not shown.

## Database and migration

One additive Auth migration creates the operational metadata table and indexes:

- PK/FK on UserId.
- Required acquisition source code, backfilled to unknown for existing accounts.
- Nullable local registration and last-login fields for historical accounts.
- Indexes for acquisition source plus registration time, last-login time/provider, and cursor ordering where query plans demonstrate use.
- No email/mobile/national-ID copy, no PII lookup hash, and no new billing balance, entitlement, or purchase table.
- New account paths create metadata in the same transaction as user creation/provisioning. A repair/backfill job is not part of MVP unless the migration cannot populate an Unknown row for every pre-existing user.

Do not backfill old registration time from refresh-token timestamps. Do not infer source from TahlilApp or Telegram rows. A deployment compatibility period may allow older rows to read as Unknown until migration and application version are aligned.

No migration is requested or run by this design task. Schema snapshots, migration ordering, rollback/roll-forward, and staging verification are implementation/release tasks.

## Backward compatibility

- Keep existing /api/v1/admin/users shape and semantics for current clients.
- Add metadata writes to direct registration and first-user external provisioning only after the additive migration is deployed.
- Missing metadata must read as Unknown/null, not fail sign-in or admin reads.
- Keep all TahlilApp and Telegram provider logic behind current owning services; new read DTOs must not change launch, provider status, linking, or revocation behavior.
- Do not change billing semantics, entitlement eligibility, credit expiry/transfer, refund, or wallet projection.
- Do not change email verification/password reset behavior or the current legacy-user policy.

## Performance and privacy

- One database-side Identity/provider query applies tenant membership and all supported filters before keyset paging, with `pageSize + 1` maximum rows returned. Use indexed tenant/user membership, metadata sort/filter, normalized email, and provider lookup paths; verify plans for supported combinations.
- Fetch the completed Identity page first, then batch fetch memberships/roles/provider links and authorized Billing summaries for no more than 100 selected user IDs. Do not post-filter, loop over candidates, or issue one query per row. The Identity page uses the five-second statement timeout and returns no page/cursor on timeout; optional summary timeouts mark only the affected section unavailable.
- Use query projections and AsNoTracking for read models; do not deserialize or log encrypted profile fields unnecessarily.
- Cache no privileged per-user PII or financial detail in a shared unscoped cache. Any later cache includes tenant and permission scope in keys and short TTL.
- Do not log search terms, external identifiers, payment references, profile values, or serialized Customer 360 payloads. Return context-level as-of timestamps.
- Credit and payment reports show source and status; no aggregate revenue until finance defines a reconciled measure.

## Testing design

Add tests following existing xUnit, API factory, EF SQLite, and PostgreSQL/Testcontainers conventions:

- Unit tests for source immutability on credential enrollment, unknown defaults, successful-login-only timestamp/provider updates, and concurrency ordering.
- Integration tests for directory keyset paging, stable sorts, combined supported Identity/provider filters, unknown historical dates, unique local-user counts, provider joins, and maximum page size. Verify there is no application-side candidate scan and that all supported filters are applied before the page limit.
- Identity/provider tests for native-only, TahlilApp-only, TahlilApp plus native credentials, Telegram-linked user, two links, revoked link, duplicate provider subject, and collision/non-enumeration. Active Refresh Sessions tests cover unexpired, expired, revoked, rotated, replay-revoked, multiple distinct rows, and duplicate TokenHash rejection.
- Tenant and authorization tests for every directory/detail/history route, cross-tenant UserId, missing permission, and mismatched account ownership. For Customer 360, verify missing optional-section permission returns `not_authorized` without source reads, while an authorized but missing/failed source returns `unavailable`.
- PII tests assert no TahlilApp profile name, national ID/mobile plaintext, encrypted value, or profile search route reaches response, logs, audit payload, or browser state. Verify masked/omitted fields.
- Billing tests compare Customer 360 projections to wallet/ledger/subscription-period source data, including reserved amount, expired period, adjustment, refund, Plus-to-Pro transfer, and downgrade cases without recomputing or mutating the wallet.
- Purchase tests distinguish pending checkout, completed/reconciled local financial transaction, receipt rejection/refund, admin grant, and TahlilApp entitlement synchronization; only local supported records appear in purchase/revenue fields.
- Contract tests preserve current /api/v1/admin/users response and Feature 139/145 auth flows.
- PostgreSQL tests cover simultaneous login metadata updates, identity collisions, cursor behavior and `EXPLAIN` plans for supported filter combinations at representative scale, plus bounded-timeout behavior with no partial page/cursor.
- Tests use fake provider gateways; live TahlilApp and email tests remain explicit staging/integration gates.

No test or migration is executed during this design-only task.

## Dependencies and risks

| Risk/dependency | Impact | Treatment |
|---|---|---|
| Feature 139 staging contract v2, authoritative effectiveFrom, API credential rotation, and live status/exchange checks are incomplete | TahlilApp current status and subscription freshness cannot be presented as live verified data | Show local sync status and last-known timestamp with an unverified/stale label; do not enable provider claims beyond recorded data. |
| Feature 139 persistent Data Protection key-ring backup/restore has not passed its operations gate | Stored national ID/mobile may be undecryptable after deployment/key loss | Keep sensitive fields masked and out of search; do not make reveal a Customer 360 dependency. |
| Feature 139 and 145 migrations have not been confirmed applied to a deployed database | Deployment versions may not match source code | Normal release migration review and staging gate; this feature adds only an additive Auth migration. |
| Billing plan/subscription/credit filters would cross separate EF models and lack a canonical indexed directory predicate | Billing-first filtering could scan/sort a large candidate set or return an incomplete page | Exclude these filters from MVP. Select the page with one indexed Identity/provider query, then batch-load Billing summaries for selected IDs. Reconsider filters only with a measured database-side query and explicit index contract. |
| Existing account registration/history is not recorded consistently | False historical attribution/date if inferred | Unknown/null defaults; show unavailable dates explicitly. |
| Payment provider and external purchase coverage varies | A checkout or entitlement may be mistaken for revenue | Show local payment evidence and external entitlements separately. Revenue is deferred until reconciled financial definitions exist. |
| Data retention/legal basis for profile PII is not settled in Feature 139 Q13 | Sensitive-data reveal/deletion workflow could conflict with legal retention | Keep PII masked in MVP; retain current provider-owned revocation behavior; require policy decision before reveal or deletion tooling. |
