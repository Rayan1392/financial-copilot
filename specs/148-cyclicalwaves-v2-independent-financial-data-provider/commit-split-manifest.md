# Commit split manifest — `39d885d` ("149")

Classifies every file changed by local commit `39d885d` (parent `7c28937e`) for a safe split into an
independent Feature 148 commit and Feature 149 commit, with Feature 141 evaluation artifacts excluded from
both. Built from `git diff 7c28937e..39d885d` read file by file (and hunk by hunk for files touched by more
than one feature), not from file names. No secret value appears below or in the diffs inspected.

## Legend

- **F148** — CyclicalWaves v2 independent financial data provider.
- **F149** — Admin customer chat history.
- **F141** — Feature 141 generated evaluation artifacts (must not enter either feature commit).
- **SHARED** — file has hunks from more than one feature; split at hunk level, both land in the commit(s) noted.

## F141 — excluded from both feature commits (29 files)

All under `specs/141-schema-aware-semantic-query-engine/`:

- `artifacts/cross-company-postgresql-examples-v3-2026-10-09.jsonl` (M)
- `artifacts/financial-statement-generic-pipeline-2026-10-09.json` (M)
- `evaluation/artifacts/company-discovery-semantics-2026-10-31-052617.json` (A)
- `evaluation/artifacts/company-discovery-semantics-2026-10-31-053923.json` (A)
- `evaluation/artifacts/cross-company-comparison-semantics-2026-10-31/X01.json` … `X22.json` (A, 22 files)
- `evaluation/artifacts/latest-period-semantics-2026-10-31.json` (A)
- `evaluation/artifacts/monthly-production-semantics-2026-10-31-052517.json` (A)
- `evaluation/artifacts/monthly-production-semantics-2026-10-31-053824.json` (A)

These are test-run outputs, not authored changes. They stay on `backup/pre-feature-148-149-split` only
(§ Preservation below). No Feature 141 commit is created without explicit authorization.

## F149 — Admin customer chat history (10 files)

- `specs/149-admin-customer-chat-history/ImplementationReport.md` (A)
- `specs/149-admin-customer-chat-history/design-review.md` (A)
- `specs/149-admin-customer-chat-history/design.md` (A)
- `specs/149-admin-customer-chat-history/spec.md` (A)
- `specs/149-admin-customer-chat-history/tasks.md` (A)
- `src/backend/FinancialCopilot.Application/Administration/AdminChatHistoryContracts.cs` (A)
- `src/backend/FinancialCopilot.Infrastructure/Administration/EfCoreAdminChatHistoryQueries.cs` (A)
- `tests/FinancialCopilot.IntegrationTests/AdminManagementEndpointTests.cs` (M, whole diff is one new test
  method `CustomerChatHistory_PagesTurnsAndAuditsFullContentReads` plus its fixtures — no F148 content)
- `src/frontend/src/components/admin/__tests__/admin-customer-chat-history.test.tsx` (A)
- `src/frontend/src/components/admin/admin-customer-chat-history.tsx` (A)
- `src/frontend/src/integrations/financial-copilot/admin-client.ts` (A — `AdminChatHistoryPage` /
  `AdminChatHistoryItem` / `AdminChatHistoryTurn` types and the two new client calls; no F148 content)

Also required in the **same commit** as F149 (mechanical fallout of F149's own constructor change, not F148):

- `tests/FinancialCopilot.IntegrationTests/Feature146PostgreSqlIntegrationTests.cs` (M, one line: an added
  `null!` argument to the `AdminManagementController` constructor call, made necessary only because F149 adds
  the `IAdminChatHistoryQueries chatHistoryQueries` parameter to that controller). This is **not** F148 scope;
  it is classified with F149 because the F148 commit does not change that constructor and must not need this
  fixup to build.

## SHARED — split at hunk level

- **`src/backend/FinancialCopilot.API/Controllers/AdminManagementController.cs`**
  - F149 hunks: the `IAdminChatHistoryQueries chatHistoryQueries` constructor parameter and the two new
    endpoints `GET users/{userId}/chat-history` and `GET users/{userId}/chat-history/{turnId}`.
  - No F148 hunk in this file.
  - → goes in the **F149 commit only**. (The F148 commit must not touch this file; see dependency note below.)
- **`src/backend/FinancialCopilot.Infrastructure/ServiceCollectionExtensions.cs`**
  - F149 hunk: `services.AddScoped<IAdminChatHistoryQueries, EfCoreAdminChatHistoryQueries>();`
  - F148 hunk: `services.AddCyclicalWavesV2AndProviderSelection(configuration);` plus its ordering comment.
  - → both hunks present; staged as two sequential patches, F148 hunk in the F148 commit, F149 hunk in the
    F149 commit (see § Ordering).
- **`src/frontend/src/components/admin/admin-page.tsx`**
  - F149 hunk: import and render of `AdminCustomerChatHistory`.
  - No F148 hunk in this file.
  - → **F149 commit only**.

### Dependency / ordering note

F149's `AdminManagementController` change does not depend on anything from F148. F148 does not touch
`AdminManagementController.cs`, `admin-page.tsx`, or the chat-history DI line. The two features are therefore
independent at the dependency-graph level even though `ServiceCollectionExtensions.cs` carries one hunk from
each: each hunk applies cleanly on top of the parent `7c28937e` on its own. Target order is **F148 first,
F149 second** (per the requested history shape); F148's hunk in `ServiceCollectionExtensions.cs` does not
require F149's hunk to compile, and F149's hunk applies cleanly after F148's commit.

## F148 — CyclicalWaves v2 independent financial data provider (remaining ~123 files)

Everything in the original 164-file list other than the F141 (29), F149 (12), and the F149-only hunks in the
3 shared files above. Includes, in particular:

- Root config/docs: `.env.example`, `README.md`, `docker-compose.yml`, `docs/providers/cyclicalwaves-v2/**`.
- Thirteen pre-existing spec `tasks.md`/`design.md`/`README.md` superseded-by-148 cross-references
  (`020`, `065`, `066`, `067`, `070`, `071`, `073`, `084`, `114`, `115`, `125`, `126`, `127`).
- `specs/148-cyclicalwaves-v2-independent-financial-data-provider/**` (8 files).
- `specs/README.md`, `specs/implementation-checklist.md` (F148 row/entries only — verified no F149 text).
- All `CyclicalWavesV2*` backend source under `API`, `Application`, `Domain`, `Infrastructure`, `Worker`.
- `AdminDataOperationsController.cs`, `CyclicalWavesPsVisualizationAdminController.cs`,
  `CyclicalWavesV2AdminController.cs`, `LegacyCyclicalWavesRetirement.cs`, `CyclicalWavesV2AdminContracts.cs`.
- `ChatCapabilityAvailability.cs`, and the F148 hunks (only) in `AiDialogueOutcomePolicy.cs`,
  `SemanticCapabilityExecutionContracts.cs`, `SemanticCapabilityExecutors.cs`,
  `FinancialProviderContracts.cs`, `ScannerExecutionContracts.cs`, `ScannerToolAdapter.cs`,
  `FinancialCopilotWorkflowDefinition.cs`, `ConditionalTrackerEvaluationProcessor.cs`,
  `ProfessionalScannerUseCases.cs`, `EfCoreScannerExecutionService.cs`, `EfCoreSymbolMetricLookupService.cs`.
- Migration `20261031045622_Feature148CyclicalWavesV2Foundation` and the model snapshot update.
- Frontend: `cyclicalwaves-v2-page.tsx` (+ its test), `admin_.data.cyclicalwaves-v2.tsx`, `routeTree.gen.ts`
  (route entries are exclusively the cyclicalwaves-v2 route), `data-admin-client.ts` (F148 types only),
  `data-management-overview-page.tsx`, `data-management-shell.tsx`.
- Test projects: `tests/FinancialCopilot.UnitTests/CyclicalWavesV2/**` (all 17 files), fixtures under it,
  `CyclicalWavesV2DomainAndMappingTests.cs`, `SemanticCapabilityDispatcherTests.cs`,
  `FinancialCopilot.UnitTests.csproj` (fixture `<Content Include>` entry only).
  `tests/FinancialCopilot.IntegrationTests/CyclicalWavesV2AdminEndpointTests.cs`,
  `CyclicalWavesV2LiveContractTests.cs`, `CyclicalWavesV2PostgreSqlIntegrationTests.cs`,
  `LegacyCyclicalWavesRetirementEndpointTests.cs`, `AiFacadeV2EndpointTests.cs` (C2 hunk only),
  `ComprehensiveAnalysisV2ProductionPathTests.cs`, `ConditionalTrackerEndpointTests.cs` (N1 tests, whole diff),
  `IndustryComparisonRoutingEndpointTests.cs`, `AdminDataOperationsEndpointTests.cs` (provider-health hunks
  only — verified no F149 text), `Feature146PostgreSqlIntegrationTests.cs` is **excluded** (see F149 above).
- `tests/FinancialCopilot.ArchitectureTests/CleanArchitectureDependencyTests.cs` (whole diff, pure F148).

Also staged in the F148 commit, on top of the above (the owner's own later verification-pass edits, currently
uncommitted on `develop`, not part of `39d885d`):

- `src/backend/FinancialCopilot.Application/Scanner/CyclicalWavesV2/ScreenerMetricMapping.cs`
- `tests/FinancialCopilot.IntegrationTests/Feature148ChatProviderE2ETests.cs`
- `tests/FinancialCopilot.UnitTests/CyclicalWavesV2/CyclicalWavesV2DomainAndMappingTests.cs` (further edits)
- `specs/148-cyclicalwaves-v2-independent-financial-data-provider/{spec,design,plan,impact-analysis,tasks,
  c1-census,ImplementationReport}.md`
- `specs/implementation-checklist.md` (further edit to the same row)

## Verification performed

- Every file in the 164-file list was accounted for above; counts: F141 29 + F149 (own) 10 + F149 (dependency
  fixup) 1 + SHARED 3 + F148 (remaining) 121 = 164.
- Files with hunks from more than one feature were diffed and read in full, not assumed from their name.
- No credential value appears in any diff inspected (only config keys set to empty string, or removed).
