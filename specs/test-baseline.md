# Test Baseline

Known pre-existing failures (decisions OQ-001 and OQ-004 in `specs/open-questions.md`). Until they are fixed, "tests green" in a Definition Of Done means: **no failure outside this list**. A change that makes a listed test pass removes it from this list. A change may not add entries; a new failure is a defect of that change.

Fixing these failures is tracked as BUG-001 and BUG-002 in `specs/bugs.md` and follows `specs/FEATURE_DEVELOPMENT_WORKFLOW.md` (Gate A and Gate B).

## Backend unit tests

Captured 2026-11-09 on `develop` at `886f05e`, with only documentation changes in the working tree.

- Command: `dotnet test tests/FinancialCopilot.UnitTests --configuration Release`
- Result: 3,097 total, 3,065 passed, 32 failed (29 test methods; one method has 4 failing data cases).
- Run twice; both runs failed on exactly the same 29 methods, so none of them is flaky in this capture.
- The 2026-11-05 Feature 154 report recorded 27 failures; the set has grown since then.

| Test class | Test method |
|---|---|
| `CyclicalWavesPsProviderFixtureTests` | `Manifest_freezes_full_history_capture_facts_and_fixture_hashes` |
| `Feature128SemanticRoutingTests` | `DefaultRoutingMode_IsSafeShadowAndRollbackIsLegacyOnly` |
| `Feature128SemanticRoutingTests` | `DeterministicCandidateWinsTieOverModelConfidence` |
| `Feature128SemanticRoutingTests` | `ProductMentionVetoesCompanyWideMixEvenWhenModelConfidenceIsHigher` |
| `Feature141DeveloperUatAuthorizationTests` | `ApiClientMustMatchConfiguredApiClientAllowlistAndActorMode` |
| `Feature141DeveloperUatAuthorizationTests` | `UserIdentityRequiresDevelopmentOptInAllowlistAndMatchingServerActor` |
| `Feature150SharedChatRoutingTests` | `NetProfitMetricWords_AreNotExtractedAsCompanyEntities` |
| `FinancialSourcePlannerHandoffTests` | `ConsolidationQualifierWithoutCatalogEvidenceStopsBeforePlanner` |
| `FinancialSourcePlannerHandoffTests` | `ExactIdentity_CarriesLosslessTypedIntentThroughPlannerAndValidator` (4 cases) |
| `NadpcoApiProviderTests` | `ManualBackfillService_RepeatedIdenticalPayloadIsReportedAsAlreadyPersisted` |
| `ProductRevenueMixFollowUp075Tests` | `DuplicateNormalizedTitles_AreAllExcludedAsAmbiguous` |
| `ProductRevenueMixFollowUp075Tests` | `FourValidProducts_ReturnsFirstTwoInTypedOrderThenCompanyTrend` |
| `ProductRevenueMixFollowUp075Tests` | `OneValidProduct_ReturnsProductThenCompanyTrend` |
| `ProductRevenueMixFollowUp075Tests` | `ParserUnsafeFirstProduct_IsSkippedAndNextTwoAreUsed` |
| `ProductRevenueMixFollowUp075Tests` | `TypedOrderWinsOverSalesShareAndRankRecalculation` |
| `SchemaNavigatorTests` | `ExactApprovedSourceTitles_ResolveIdentityWithoutCreatingAliasesOrAuthorization` |
| `SchemaNavigatorTests` | `ReviewedCanonicalSourceBinding_IsExactAndDoesNotGrantQueryAuthorization` |
| `SemanticDialogueGovernanceTests` | `VersionedDataset_CoversEveryExecutableRouteAndPassesOfflineRegression` |
| `StructuredQueryCompilationTests` | `Compiles_catalog_identified_direct_source_item_without_metric_code_using_parameters` |
| `StructuredQueryCompilationTests` | `Development_test_scope_requires_exact_company_operation_and_catalog_approval` |
| `StructuredQueryCompilationTests` | `Enabled_executor_configuration_requires_valid_development_database_scope` |
| `StructuredQueryCompilationTests` | `RuntimeCatalogSnapshotBuilder_RejectsCanonicalPackageDrift` |
| `StructuredQueryCompilationTests` | `RuntimeCatalogSnapshotBuilder_UsesCanonicalSourceAndLogicalRelationshipWithoutGrantingAccess` |
| `SubscriptionCreditLifecycleTests` | `ConcurrentPlusAndPro_TransfersOnlyRemainingPlusCredits_AndIsIdempotent` |
| `SubscriptionCreditLifecycleTests` | `PlusExpiryWhileProRemainsActive_DoesNotStartCooldown` |
| `SubscriptionCreditLifecycleTests` | `PlusToPro_TransfersRemainingCreditsAndDuplicateUpgradeDoesNotGrantAgain` |
| `SubscriptionCreditLifecycleTests` | `ProExpiryWhilePlusRemainsActive_DoesNotStartCooldown` |
| `TelegramChannelFinancialStatementHandler143Tests` | `Reconciliation_refetches_fuller_income_once_and_completes_same_checkpoint` |
| `TelegramMembership088CompletionTests` | `ReservationBurst_ConcurrentRequests_DoNotOverspendDailyAllowanceWallet` |

Architecture tests: 1 known failure, `CleanArchitectureDependencyTests.MigratedSemanticExecutors_DoNotReintroduceLegacySymbolExtraction` (BUG-003), found 2026-11-09.

Integration tests are not in this baseline yet: many need PostgreSQL and other services, and the Feature 154 report recorded 164 failures there (2026-11-05). Capture them separately when an approved plan needs them.

## Frontend type errors

Captured 2026-11-09 at `886f05e`, in `src/frontend`, with `npm run typecheck` (`tsc --noEmit`, added 2026-11-09 under OQ-001; it writes no files). The script exits non-zero until these errors are fixed, so it cannot be a blocking CI step yet.

Result: 37 errors in 17 files (24 in test files under `__tests__/`, 13 in application files). Until they are fixed, a frontend change must add no type error, and a file the change touches must have no more errors than listed here.

| File | Errors |
|---|---:|
| `src/components/app/__tests__/sidebar-preferences.test.tsx` | 11 |
| `src/components/app/__tests__/message-list.test.tsx` | 6 |
| `src/components/app/__tests__/disclosure-listing-table.test.tsx` | 3 |
| `src/routes/verify-email.tsx` | 2 |
| `src/routes/_app.top-up.tsx` | 2 |
| `src/components/app/monthly-activity-trend-chart.tsx` | 2 |
| `src/routes/reset-password.tsx` | 1 |
| `src/routes/forgot-password.tsx` | 1 |
| `src/routes/admin_.getCompanyId.tsx` | 1 |
| `src/routes/admin.tsx` | 1 |
| `src/routes/_app.getCompanyId.tsx` | 1 |
| `src/components/app/sidebar.tsx` | 1 |
| `src/components/app/__tests__/quarterly-financial-table.test.tsx` | 1 |
| `src/components/app/__tests__/quarterly-financial-table-image.test.ts` | 1 |
| `src/components/app/__tests__/quarterly-financial-table-browser-fixture.tsx` | 1 |
| `src/components/app/__tests__/monthly-product-trend-chart-image.test.ts` | 1 |
| `src/components/admin/data-management-guard.tsx` | 1 |
