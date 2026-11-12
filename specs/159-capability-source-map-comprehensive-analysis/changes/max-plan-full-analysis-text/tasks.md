# Tasks: Feature 159 change — whole analysis text for Max subscribers

Status: Gate B: Approved by the owner on 2026-11-11

Approval record: Gate A «با همه پیشنهادات موافقم و تایید میکنم» (2026-11-11); Gate B «Gate B تأیید» (2026-11-11) for
this plan, including T6b.

## Feature Folder

`159-capability-source-map-comprehensive-analysis/changes/max-plan-full-analysis-text` (branch
`track/a-routing-answer-correctness`, on top of the uncommitted Feature 150 / BUG-020 work)

## Before Starting

- [x] This file's `Status:` line records Gate B approval by the owner.
- [x] Read `user-story.md` and `design-proposal.md` in this folder.

## Implementation Tasks

### 1. Billing (entitlement data and rule)

- [x] 1.1 `BaselinePlanCapabilities`: add `Enabled("Max", "ComprehensiveAnalysis.FullText")`; billing migration
  `Feature162MaxComprehensiveAnalysisFullText` (one `InsertData` row, `Down` deletes it).
- [x] 1.2 `IPlanCapabilityService.IsEnabledAsync(account, capabilityCode, ct)`: `true` only when the effective plan
  (existing `ReadEffectivePlanCodeAsync`) is set and has an enabled row; no plan → `false` (fail closed).
  `ValidateCanExecuteAsync` is not changed.

### 2. Application (port and query)

- [x] 2.1 Port `IComprehensiveAnalysisFullTextEntitlement.IsGrantedAsync(ComprehensiveAnalysisReader reader, ct)`
  (`reader` = tenant, actor, user id, API client id); constant `ComprehensiveAnalysisFullText.CapabilityCode`.
- [x] 2.2 `ComprehensiveAnalysisQueryRequest` gains `bool IncludeFullText = false`;
  `ComprehensiveAnalysisQueryResponse` gains `bool FullTextIncluded = false`.
- [x] 2.3 `ComprehensiveAnalysisTeaserPolicy.FullText(title, text)`: the whole text with a repeated title removed.
- [x] 2.4 `QueryExecutionContext` gains optional `UserId` and `ApiClientId`; fill them at the three V2 construction
  sites (`FinancialCopilotAgentWorkflowRunner`, `FinancialCopilotWorkflowDefinition` ×2) from the request.
  **Deviation:** no change needed; both fields already existed and all three sites already fill them.
- [x] 2.5 `ComprehensiveAnalysisCapabilityExecutor`: ask the port (2-second budget, any failure → `false`), pass
  `IncludeFullText` to both queries (normal and BUG-020 fallback), record the counter (FR6).

### 3. Infrastructure

- [x] 3.1 `ComprehensiveAnalysisFullTextEntitlement`: API client or no user id → `false`; resolve the individual account
  (`ICustomerAccountRepository.FindIndividualByUserAsync`, tenant checked) → `IsEnabledAsync`; exceptions → `false`,
  logged as a category without user id. Register in DI (scoped). No-op `false` implementation when billing is absent.
- [x] 3.2 `CyclicalWavesV2ComprehensiveAnalysisQuery`: `IncludeFullText` → `FullText(...)` instead of `Teaser(...)`,
  response `FullTextIncluded = true` only when items exist.
- [x] 3.3 `ComprehensiveAnalysisToolAdapter.QueryAsync` (agent tool path): always `IncludeFullText = false` (explicit).

### 4. Card and Telegram

- [x] 4.1 `ComprehensiveAnalysisCardComposer.ComposeMarkdown/ComposePlainText`: when `FullTextIncluded`, show the
  whole text, leave out the read-more sentence, and use the link line «تحلیل‌های جامع تحلیل‌اپ»; otherwise unchanged.
- [x] 4.2 Plain-text composer: optional maximum length; Telegram renderer passes 4,096; the text is cut at the last
  sentence end (else word boundary) that fits, then « …», the link line kept.
  **Deviation:** the renderer passes its own `TelegramMessageLimit` (3,900, which leaves room for MarkdownV2
  escaping), not 4,096. The renderer also already splits longer messages into parts («بخش ۱/۲»), see Results.
- [x] 4.3 Callers pass the response flag: `ComprehensiveAnalysisToolResult.AgentSummary`,
  `TelegramAssistantResponseRenderer`.

### 5. Admin label

- [x] 5.1 `src/frontend/src/components/admin/admin-page.tsx` `permissionLabels["ComprehensiveAnalysis.FullText"]` =
  «متن کامل تحلیل جامع».

### 6. Docs

- [x] 6.1 `AGENTS.md` card description, `docs/providers/cyclicalwaves-v2/README.md`, `docs/architecture.md` (entitlement
  port), `specs/README.md` change entry, `CHANGELOG.md` `[Unreleased]` Persian line.

## Tests

- [x] T1 Unit: `IsEnabledAsync` (Max active → true; Max expired, Pro, Plus, Free, no plan, disabled row → false).
- [x] T2 Unit: entitlement adapter (API client, no user id, no account, other tenant, exception → false).
- [x] T3 Unit: executor (granted → `IncludeFullText` on both queries; port throws or exceeds 2 s → preview, answer
  succeeds).
- [x] T4 Unit: composer full-text card (web and plain text, no read-more sentence, link line), preview card unchanged,
  Telegram 4,096 cut keeps the link.
- [x] T5 Unit: query returns the whole text only with `IncludeFullText`. Covered by T6 through the real query (the V2
  query has no unit-test harness).
- [x] T6 Integration (`Feature159MappedNoavaranAminChatApiFactory` with seeded billing accounts): AC-1, AC-2 (Free and
  expired Max), AC-3, AC-5, AC-7. `Feature159MaxFullTextE2ETests` (5 tests: Max, Free, expired Max, row disabled, API
  client). AC-5 (BUG-020 fallback) is covered at unit level (T3).
- [x] T6b Update the test that pins the Max capability rows. It was `PlanCapabilityServiceTests.MaxSeededCapabilityMatrixExactlyMatchesPro`
  (now `…MatchesPro_PlusOnlyTheFullAnalysisText`); `BillingDomainTests` does not pin the matrix.
- [x] T7 Migration: `dotnet ef migrations has-pending-model-changes --context BillingDbContext` clean.
- [x] T8 Frontend: `npm run test` 329/329, `vite build` green, `tsc` 37 errors (baseline BUG-002, none in the file).
  Lint of `admin-page.tsx` (with LF line endings): the same 15 prettier errors as `HEAD`, none in the new lines.

## Definition Of Done

- [x] `dotnet test src/backend/FinancialCopilot.sln --configuration Release`: no failure outside the known set
  (unit 32 baseline, architecture BUG-003, integration 186 also failing on `develop`).
- [x] No pending model changes for `BillingDbContext`.
- [x] Acceptance criteria in `user-story.md` checked (AC-6 Telegram and AC-5 at unit level; no live run).
- [x] Docs and `CHANGELOG.md` updated.
- [x] Not committed, merged or deployed without the owner's request.

## Results (2026-11-11)

- Full solution run: architecture 1 failure (BUG-003); unit 33 = the 32 baseline plus
  `TelegramAssistantResponseRenderer089Tests.Monthly_trend_is_rendered_as_a_deterministic_png_with_a_concise_caption`,
  which also failed in an earlier full run on this branch and passes alone (flaky under load); integration 189 in that
  run, of which 3 `TahlilAppTopUpEndpointTests` failed with `ObjectDisposedException: IServiceProvider` after 4 ms and
  pass alone; a second integration run gave 186, exactly the `develop` set.
- `dotnet ef migrations has-pending-model-changes --context BillingDbContext`: no changes.
- Not verified: a live run with a real Max account and real analysis texts; the Telegram card through the real gateway.
- Finding for the owner: `TelegramAssistantResponseRenderer` already splits a long message into parts («بخش ۱/۲»), so
  the whole text could be sent in two messages instead of being cut. Implemented as approved (cut); switching is small.
