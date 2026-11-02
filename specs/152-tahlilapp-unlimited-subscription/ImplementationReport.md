# Feature 152 Implementation Report

**Local implementation readiness:** Complete for the approved in-repository contract, including the approved Max slug, capability matrix, and overlapping-window behavior.

**Production enablement:** Not ready. External TahlilApp checkout and joint staging/operations verification remain pending. No deployment, production migration, or production data change was made. Feature 151 partner delivery remains disabled.

## Changes and acceptance evidence

- Mapped approved external slug `sapio_max` to canonical plan `Max` in `TahlilAppOptions` defaults, exchange and status parser defaults, and the API's `Authentication:TahlilApp:PlanMap`. Existing configured mappings remain supported. Parser tests verify the Max slug and preserve TahlilApp's supplied start/expiry values.
- Added 13 explicit `Max` capability seed rows identical to Pro, including quota limits. `MaxSeededCapabilityMatrixExactlyMatchesPro` compares both model seed sets. No inheritance or numeric infinity sentinel is used.
- Added migration `20261101160414_Feature152MaxProCapabilityMatrix` to insert the approved Max capabilities into existing databases. The migration was exercised against the disposable local PostgreSQL cluster. EF reports no model changes pending.
- Preserved explicit `QuotaMode=Unlimited`, numeric allowance zero, and authoritative source-window expiry. Unlimited admission/usage does not grant, reserve, or debit numeric wallet credits.
- Implemented approved overlap resolution across entitlement, reservation, finite-source accounting, top-up eligibility, and user/admin reporting: active Max controls Unlimited mode through its own expiry; after Max expires, the latest-expiring active non-Max window determines plan/capabilities. In finite mode, spendable grants from all active plan/top-up sources sum, but each keeps its own expiry and is never transferred or extended. The existing Pro-over-Plus choice is retained only for exact non-Max expiry ties.
- Updated the spec, user story, checklist, and Feature 151 report to record the approved overlap behavior. No new exact-tie rule was introduced.
- Preserved Feature 151's disabled partner-delivery setting and unrelated Kesaveh investigation changes.

## Verification

### Run for this update

| Check | Result |
| --- | ---: |
| TahlilApp exchange/status parser and options validator + `PlanCapabilityServiceTests` (Release) | **28 passed, 0 failed, 0 skipped** |
| `TahlilAppMaxUsagePostgreSqlTests` on disposable PostgreSQL 18, port 55441 | **4 passed, 0 failed, 0 skipped**; migration chain applied to test databases |
| `dotnet ef migrations has-pending-model-changes --context BillingDbContext` | **No model changes pending**; EF tools 10.0.9 emitted its runtime 10.0.13 version warning |
| Backend solution Release build (`--no-restore`) | **Passed, 0 warnings, 0 errors** |
| `git diff --check` | **Passed**; Git emitted existing LF/CRLF normalization warnings |

The overlap update's focused domain/unit run was **34 passed, 0 failed, 0 skipped**. The combined PostgreSQL/API regression filter for billing persistence, billing endpoints, Feature 146, Max overlap, and Feature 151 top-up lifecycle reported **58 passed, 1 failed, 0 skipped**. The failure was `Feature146PostgreSqlIntegrationTests.Auth_migration_backfills_unknown_without_inferred_source_and_adds_query_indexes` at line 43 (`Sequence contains no elements`). It reproduced at the same line in a detached clean checkout of baseline `5f50f847` against the same disposable PostgreSQL server. The other Feature 146 test, `Customer360_applies_section_permissions_tenant_isolation_and_active_refresh_session_rules`, passed after plan/capability reads were combined; its dedicated rerun was **1 passed, 0 failed**. The focused Feature 151/152 PostgreSQL and endpoint subset was **17 passed, 0 failed, 0 skipped** across `TahlilAppMaxUsagePostgreSqlTests`, `TahlilAppTopUpPostgreSqlTests`, and `TahlilAppTopUpEndpointTests`.

The temporary database was created under the local temp directory and used only uniquely named disposable test databases. The PostgreSQL server was bound to loopback port 55441. It did not access production or staging.

### Reused successful Feature 139/151/152 checks

The previous implementation report recorded these passing checks. Checks not affected by overlap changes were reused; the current overlap update additionally reran the focused unit and PostgreSQL/API regressions listed above:

| Check | Previously recorded result |
| --- | ---: |
| Billing/TahlilApp/OwnedIdentity/Feature 139 focused unit regressions | **246 passed, 0 failed, 0 skipped** |
| Billing and Feature 151 endpoint regressions, including Max usage projection | **19 passed, 0 failed, 0 skipped** |
| Feature 139 launch, billing persistence, owned identity integration regressions | **52 passed, 0 failed, 0 skipped** |
| Disposable PostgreSQL lifecycle/top-up/Max/migration/concurrency filter | **25 passed, 0 failed, 0 skipped** |
| Feature 152 frontend `credit-progress.test.ts` | **23 passed, 0 failed** |
| Frontend build | **Passed**; existing route-like test-file and large-chunk warnings recorded previously |

The earlier Feature 151 report also records two frontend failures reproduced on its clean baseline in `MarkdownMessage` and `MonthlyProductTrendChart`; this update does not touch those surfaces. The full frontend suite was not rerun.

## Migration and configuration

- Existing migration: `20261101145026_Feature152UnlimitedQuotaMode`.
- New migration: `20261101160414_Feature152MaxProCapabilityMatrix`.
- Feature 151 dependency migration: `20261101131932_Feature151TahlilAppPaidCreditTopUps`. The overlap rule uses these persisted sources and requires no additional schema migration.
- Apply these through the normal reviewed migration process in local/staging environments only. Neither was applied to production.
- Default/API configuration now contains `Authentication:TahlilApp:PlanMap:sapio_max = Max`. External staging must verify that TahlilApp emits this slug and its authoritative subscription window.
- `Max` remains `QuotaMode=Unlimited` with `IncludedCredits=0`; no wallet sentinel or numeric grant is configured.
- Feature 151 partner delivery remains disabled by default (`Billing:TahlilAppTopUps:Enabled=false`).

## Remaining work

### External TahlilApp integration

- Implement and verify the Max offer and checkout/renewal flow in TahlilApp. This is outside this repository and is not claimed complete.
- Jointly verify TahlilApp's `sapio_max` status/exchange response, authoritative start/expiry window, expiry, and revocation in staging.

### Staging/operations gates

- Jointly verify authoritative expiry/revocation, active-Max Unlimited behavior, latest-expiring non-Max selection, independent finite-source expiry, and Feature 151's active-Max top-up exclusion. Then complete Feature 139's pending operations review and deployment approval. No staging or production gate is claimed complete.
