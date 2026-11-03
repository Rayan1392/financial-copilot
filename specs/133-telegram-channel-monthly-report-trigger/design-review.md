# Feature 133 Design Re-Review

## 1. Verdict

APPROVED

Focused re-review of the corrected `design.md`, limited to F1, F2 and regressions directly introduced by their corrections. Both findings are resolved; no new Blocker or Major regression was identified. This is design approval against current source, not verification of an implemented Feature 133 or a deployed account.

Prior review history: the original R1–R17 review returned NEED_CHANGES with F1 (Major: shared credential moved to a separate tenant, breaking existing link confirmation) and F2 (Minor: required source-report checkpoint absent from the named read projection). Previously accepted areas are not reopened.

Source paths below are relative to `src/backend/`. Graph discovery was used first; missing symbols and stale graph source boundaries were supplemented with current file inspection.

## 2. F1 Verification

**F1: RESOLVED.**

### Existing linking compatibility

`FinancialCopilot.TelegramGateway/PrimaryApiClient.cs:14` and `:28` use the same `CreateClient`; line 49 supplies `settings.PrimaryApiKey` for both assistant updates and link confirmation. `FinancialCopilot.API/appsettings.json:362` defines the owned-user default tenant as `11111111-1111-1111-1111-111111111111`; lines 370–376 assign gateway client `22222222-2222-2222-2222-222222222222` to that tenant and allow both required routes.

`FinancialCopilot.Infrastructure/Authentication/TelegramLinkService.cs:19` creates a web challenge with the user's tenant. `ConfirmFromTelegramAsync` at line 103 requires an API client and forwards its authenticated tenant. `ConfirmAsync` at line 271 validates token hash, pending status, purpose, tenant equality and expiry; line 297 rejects a mismatched tenant. Actor and existing-link conflict checks remain in place.

Design sections 3.6, 7, 13, 14 and 16 now retain the existing key, client and tenant. A valid, unexpired, conflict-free existing-tenant challenge therefore continues through the same tenant validation, regardless of automation flags. No second credential, tenant migration or weakened challenge check is required. This conclusion concerns repository configuration; deployed secret values were not inspected.

### Trusted automated identity and billing

Design sections 4, 9, 13 and 14 require the channel branch to use the authenticated API-client actor, populated `ApiClientId`, no `UserId`, and the unchanged tenant, after a backend-configured client/tenant/channel grant. The contract exposes no tenant, organization-account, billing-account or execution-actor selector. Extra payload fields and Telegram text cannot override those values.

This matches `FinancialCopilot.API/Security/ApiKeyAuthenticationHandler.cs:20`: active credentials, secret/hash matching, valid client/tenant GUIDs and path scope are checked before claims are created from server configuration at lines 42–48. Request-body identity is not an authentication source.

The exact existing billing path is:

1. `FinancialCopilot.Application/AI/Orchestration/AiQueryOrchestrationService.cs:84`, or V2 `FinancialCopilot.Infrastructure/AI/OrchestrationV2/Functions/BillingFunctions.cs:8`, forwards the query actor/tenant/user/API-client fields into `BillingReservationRequest`.
2. `FinancialCopilot.Infrastructure/Billing/AiFacadeBillingHook.cs:25` builds `BillableActorContext` and calls `BillableAccountResolver.ResolveAsync`.
3. `FinancialCopilot.Billing/Services/BillableAccountResolver.cs:8` selects the API-client branch whenever `ApiClientId` is present and calls `FindOrganizationByTenantAsync(actor.TenantId)`. It rejects missing accounts and tenant mismatches.
4. `FinancialCopilot.Infrastructure/Billing/Persistence/BillingRepositories.cs:15` uses `SingleOrDefaultAsync` over `CustomerAccounts`, filtering by that tenant and `AccountType == Organization`.
5. The hook loads the returned account's wallet and reserves against that account through `CreditReservationService.ReserveAsync` (`FinancialCopilot.Billing/Services/CreditReservationService.cs:16`). Its reservation handle retains the resolved account ID; `AiFacadeBillingHook.FinalizeAsync` at line 122 passes that ID and tenant to `IUsageFinalizationService.CommitAsync`, using the correlation-derived charge key.

Thus no caller-provided account ID, fabricated Telegram user or new billing resolver is needed. Channel attribution through `ExternalUserId` does not select the organization account. The organization is shared with other API clients in the same tenant, as the corrected design explicitly states; separate channel-wallet isolation is not promised.

### Security and operational gate

`FinancialCopilot.API/Controllers/TelegramAssistantController.cs:11` retains ApiClientOnly and authenticated-actor rate limiting. `FinancialCopilot.API/Controllers/NoavaranMonthlyBackfillController.cs:14` retains its admin policy; `FinancialCopilot.API/Security/ServiceCollectionExtensions.cs:152` requires valid actor context, WebAppUser mode and the explicit backfill or data-sync permission. Design sections 8 and 13 authorize only the new fixed internal service operation and do not grant gateway access to that admin endpoint.

Account provisioning/funding remains a valid operational prerequisite. Both automation flags default false, publishing must remain disabled until the selected organization account is funded, and missing account/credit follows existing failure behavior. No live account or balance was verified.

## 3. F2 Verification

**F2: RESOLVED.**

`FinancialCopilot.Application/FinancialData/Ingestion/CompanyMonthlyActivityTrendSnapshotContracts.cs:58` exposes `ExternalCompanyId`, `ReportYear`, `ReportMonth` and `CalculatedAtUtc` on the read snapshot. `FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/EfCoreCompanyMonthlyActivityTrendSnapshotRepository.cs:29` filters by company and inclusive year/month bounds. Supplying the same requested period for both bounds gives the required readiness read; an empty result means no snapshot. Its mapping at line 148 includes all required fields and does not include `SourceReportId` or `CurrentMonthOutputType`.

The current acquisition/calculation chain supports the corrected guard:

- `SingleCompanyMonthlyIngestionService.cs:22` in the same NadpcoApi directory fetches the requested company's ProductSales output types and awaits `ProcessPayloadAsync` at line 47, with a fresh direct-run idempotency key.
- `FinancialCopilot.Infrastructure/Financial/Ingestion/FinancialDataSyncProcessor.cs:119` awaits normalization before setting the completed outcome; lines 129–144 handle absent requested company/month report data as Failed/NoDataYet.
- `NadpcoApiMonthlyActivityNormalizer.cs:112` saves normalized data, selects single-month ProductSales groups and awaits the requested company/year/month trend recalculation at lines 132–143.
- `CompanyMonthlyActivityTrendSnapshotCalculator.cs:30` requires requested-period OutputType=0 reports and line items. On the successful calculation path it stamps `CalculatedAtUtc: DateTimeOffset.UtcNow` at line 233 and awaits repository upsert at line 235. The repository replaces the company/month snapshot and saves it at lines 11–26.

For a successful relevant calculation, the requested-period timestamp is therefore generated after refresh starts and persisted before direct ingestion returns. Completed ingestion alone is not sufficient: missing single-month inputs can produce no recalculation. Design section 8 correctly also requires a matching existing snapshot with `CalculatedAtUtc >= refreshStartUtc`, completed run, zero errors and completion time, otherwise failing closed.

Sections 8, 17 and AC 11 explicitly remove source-report/output-type fields from required checkpoint evidence. OutputType=0 remains the calculator's invariant. No projection extension or persisted-row read is needed.

This is sufficient for the stated bounded freshness contract: a newly calculated snapshot for the requested company and period after the attempted refresh. It does not prove exact vendor revision equivalence, all-output acquisition completeness or complete historical comparisons; those limitations remain explicit. Section 9 additionally suppresses a query response for a different company/period or without fresh calculation metadata.

## 4. Regression Check

**New Blocker/Major regressions caused by the corrections: 0.**

| Area | Focused result |
|---|---|
| Feature 130 linking | Existing credential/tenant and all challenge checks remain unchanged; AC 31 covers matching-tenant success and mismatched-tenant rejection with automation off/on. |
| Telegram interactive queries | `TelegramAiAssistantAdapter.HandleCoreAsync` (`FinancialCopilot.Infrastructure/Authentication/TelegramAiAssistantAdapter.cs:91`) still resolves the linked person, handles callbacks and forwards that person's identity into orchestration. The proposed channel branch precedes this adapter and does not alter it. |
| Backfill authorization | Existing WebAppUser admin permission remains intact; the new branch requires its separate narrow backend service grant before calling direct ingestion. |
| Billing isolation | Organization selection remains tenant-scoped and tenant-validated. No linked individual account or caller-selected account is used; per-post conversations remain under the authenticated API-client actor. |
| Requested-period freshness | Matching company/period/existence/timestamp and successful-run checks remain mandatory; wrong/latest-period responses are suppressed. Removing unavailable source identity does not weaken those checks. |
| Configuration matrix | All four outcomes, default-off values, replay gates and terminal disabled outcomes remain intact. |
| Three-slice boundary | Identity compatibility stays within the existing channel-contract slice; the projection-based guard stays within refresh/query work. No second credential router, projection extension or new workflow is introduced. |

## 5. Configuration Matrix

Verified against design sections 6, 11, 12, 16 and 17 and ACs 23–30. Both `ChannelMonthlyReports:AutoBackfillEnabled` and `ChannelMonthlyReports:AutoPublishTrendEnabled` remain independent primary-API options defaulting to `false`.

| Backfill | Publish | Behavior |
|---|---|---|
| Off | Off | Skip; no acquisition, conversation, query, billing or publication. |
| On | Off | Backfill and readiness only; no AI conversation, query, billing or publication. |
| On | On | Backfill → readiness → existing billed query → validation → same-channel publication. |
| Off | On | Skip with AutoPublishRequiresBackfill; no stale-data analysis or stored-response publication. |

Replay observes disabled switches; later enablement does not rerun terminal skipped/backfill-only posts. The existing restart/drain procedure remains specified. These are verified design requirements, not claims that the new options are already implemented.

## 6. Acceptance Criteria Check

**33 AC candidates checked for amended areas**, without reopening a full review of every criterion.

| Candidates | Verification |
|---|---|
| 7–8 | Preserve service/channel authorization and existing admin permission. |
| 10–11 | Require successful ingestion plus actual projected company/year/month/existence/timestamp evidence; explicitly require no SourceReportId or CurrentMonthOutputType. |
| 14 | Names the actual FindOrganizationByTenantAsync path for the unchanged authenticated gateway tenant and excludes an individual linked-user account. |
| 15–16 | Preserve isolated post conversations and wrong-company/period suppression alongside the corrected identity/readiness rules. |
| 22, 31 | Retain interactive/callback behavior; explicitly test existing-tenant link success with automation off/on and mismatched-tenant failure. |
| 23–30 | Preserve independent false defaults, the complete matrix and disabled replay/terminality semantics. |
| 32–33 | Require the trusted authenticated API-client identity and prohibit payload/text overrides of tenant, account or actor. |

Section 17 supplies corresponding compatibility, spoofing, organization-billing and actual-projection readiness fixtures. No contradictory AC was introduced.

## 7. Implementation Readiness

The corrected design is ready for implementation planning within the same three slices in section 22:

1. Channel contract and recognition, including unchanged credential/tenant authorization and link compatibility coverage.
2. Refresh and existing query, including projected readiness, organization billing, per-post conversations and matrix/failure coverage.
3. Delivery and rollout, including disabled replay, bounded retries and staging verification.

Funding and measured request-budget verification remain the previously accepted operational enablement gates. No new blocking design decision remains.

This re-review ran no production code, tests, provider ingestion, billing operations or Telegram calls. Feature 133 implementation and runtime acceptance tests remain future work. Only the existing review file was edited; no production code, Story.md or Tasks.md was created. No Story.md or Tasks.md exists in the Feature 133 directory; similarly named files in earlier features are pre-existing and outside scope.

## 8. Summary

F1 RESOLVED. F2 RESOLVED. No new Blocker/Major regressions. Configuration matrix verified. All 33 AC candidates checked for the amended areas. Three implementation slices remain plausible.

The Feature 133 directory was already untracked before this re-review. Consequently scoped Git status reports the directory as untracked, not a tracked review-file modification. The unchanged design was verified against its pre-edit SHA-256:
`B1E89F9A260A94897E74F1662F1B8F656206DEA826B8F2F0194333F3A2C8BD80`.

DESIGN_REVIEW_APPROVED
