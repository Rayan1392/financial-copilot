# Feature 136 — Implementation Report

Date: 2026-10-04

## Outcome

Feature 136 is implemented through the existing V2 orchestration path. V1 was
not changed or extended.

The implementation adds a typed `monthly_product_trend` result for a named
company/product request, calculates product sale rate from accepted product
sales facts, preserves source-row provenance, and renders product value bars
plus an independently scaled calculated-rate line in web and Telegram paths.

## Slice completion

1. Source integrity and accepted revisions — completed and passed.
2. Product trend domain/application behavior and V2 routing — completed.
3. Workflow, conversation persistence, API contracts, and replay mapping — completed.
4. Frontend product chart, browser PNG export, and Telegram renderer — completed.
5. Verification and regression — completed with external-suite blockers listed below.

## Acceptance coverage

- Strict source selection is enforced: accepted `ProductSales` reports with
  `OutputType == 0` only. Null and other output types are excluded.
- Logical report identity and immutable revision candidates are persisted with
  `LogicalReportKey`, `RevisionFingerprint`, provider publication evidence,
  `IsAccepted`, and `RevisionStatus`.
- Exact replay is a no-op; newer publication evidence supersedes the current
  candidate; older revisions are rejected; equal/unknown evidence remains
  pending.
- Product identity is company-scoped and provenance-first. Provider product
  code is preferred, then positive provider product ID, then title plus unit.
  Ambiguous resolution returns bounded typed candidates.
- Same-code rows with distinct economic/source evidence survive normalization;
  exact duplicate rows are represented with source multiplicity and do not
  double-count on replay or reorder.
- Rate calculation uses checked decimal arithmetic:
  `salesValueMillionRial * 100000 / saleQuantity`, with typed statuses for
  valid, zero-value, missing, zero-quantity, negative, overflow, and unavailable
  cases.
- Missing fiscal months are explicit gaps. The default window is product-bounded
  at the latest qualifying observation and contains at most 12 fiscal positions.
- Routing precedence keeps ProductRevenueMix and explicit product comparison
  ahead of product trend, and company-only trend requests on the company trend
  path. Product results never use `MonthlyAverageSalesRate`.
- Typed transport is wired through workflow messages, conversation persistence,
  API/controller mapping, frontend state, browser export, and Telegram output.

## Main production files

- `src/backend/FinancialCopilot.Application/FinancialData/Ingestion/MonthlyProductTrendContracts.cs`
- `src/backend/FinancialCopilot.Application/AI/Orchestration/MonthlyProductTrendIntentRules.cs`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/MonthlyProductTrendQueryUseCase.cs`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoApiMonthlyActivityNormalizer.cs`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/FinancialIngestionRows.cs`
- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/FinancialIngestionConfigurations.cs`
- `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs`
- `src/backend/FinancialCopilot.Infrastructure/Authentication/TelegramAssistantResponseRenderer.cs`
- `src/frontend/src/components/app/monthly-product-trend-chart.tsx`
- `src/frontend/src/components/app/monthly-product-trend-chart-image.ts`
- `src/frontend/src/lib/chat.functions.ts`

## Schema and migration

Added migration:

`src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/Migrations/20261004073802_Feature136MonthlyProductTrendRevision.cs`

It adds accepted-revision/provenance fields, source-row identity fields, and
replaces the old report/product-code uniqueness constraints with revision-safe
indexes. Existing rows are backfilled with deterministic logical keys and
fingerprints. The migration was compiled but not applied to a production
database in this work session.

## Verification

- Feature 136 focused tests: 9/9 passed.
- Full backend unit suite: 1,733/1,733 passed.
- Architecture tests: 12/12 passed.
- Feature-related monthly-trend integration tests: 13/13 passed.
- Backend solution build: passed, 0 warnings, 0 errors.
- Frontend production build: passed.

Remaining validation blockers:

- The full frontend Vitest suite has one unrelated existing disclosure-locale
  assertion failure (`disclosures.test.ts`, Tehran suffix expectation): 39/40
  passed.
- The full integration suite has unrelated environment/baseline failures in
  market-insight, scanner, Telegram-account-linking, admin-operation, and
  legacy metric tests. The Feature 136 monthly-trend integration subset passes.

No out-of-scope V1 capability, generic product-master architecture, fuzzy
matching, or company-snapshot redesign was introduced.

## Final blocker-attribution verification

Reference commit: `72ce97f` (`feat: add reporting type monthly backfill endpoint`).
The reference was checked in an isolated worktree; no production files were
changed by this verification.

### Frontend

The one failing test is
`src/frontend/src/integrations/financial-copilot/__tests__/disclosures.test.ts`.
Its stack trace ends at the unchanged disclosure formatter assertion expecting a
`(تهران)` suffix. Neither that test nor
`src/frontend/src/lib/format/disclosure.ts` is in the Feature 136 diff. The
same failure reproduces on the reference commit: 1 failed, 1 passed.

### Backend integration

The current full integration run reports 44 failed, 397 passed, and 40 skipped.
The reference full run reports 46 failed, 395 passed, and 40 skipped. The two
additional reference failures are the pre-existing company-trend routing cases
that Feature 136 corrected (`V2MonthlyActivityTrendEndpointTests`); both now
pass in the focused 13-test monthly-trend integration run.

The remaining 44 current failures are in unchanged legacy/environment paths:
market insight, scanner fixtures, Telegram account linking/membership, admin
operations, legacy metric lookups, explainable-answer expectations, and
Docker/Testcontainers PostgreSQL availability. Representative failing classes
were also rerun on the reference commit and reproduced with the same assertions
and stack locations. None exercises `monthly_product_trend` or the Feature 136
product chart/API/persistence path.

Feature 136 verification remains green: 1,733/1,733 backend unit tests,
12/12 architecture tests, 13/13 feature-related integration tests, and the
frontend production build pass.

FEATURE_136_BLOCKERS:

- feature-related: 0
- baseline/unrelated: 44

FINAL_STATUS: READY

FEATURE_136_READY_BASELINE_FAILURES_ONLY

## Runtime defect follow-up

The runtime diagnosis follow-up corrected the active V2 semantic-preemption defect. A validated
product slot now takes precedence over generic company monthly trend while ProductRevenueMix and
MonthlyProductComparison remain ahead of MonthlyProductTrend. The product parser accepts both the
legacy encoded aliases and real Unicode Persian input.

The repository no longer substitutes the generated report-local `ProductCode` for missing
`ProviderProductCode`; the resolver therefore uses the approved company-scoped provider-id/code
or deterministic title-plus-unit fallback.

`MonthlyProductTrendResult` is now passed into `MessagePersistenceFunction.PersistAsync`. The
typed payload survives the conversation message JSON and API reload. The existing targeted
single-company ingestion operation now accepts optional `OutputType`; `OutputType: 0` scopes the
run to Feature 136 ProductSales data without touching other output types.

Focused runtime follow-up tests: 5/5 V2 exact-query and replay tests passed; 15/15 focused unit
tests passed. Real NADPCO re-ingestion was not executed because local settings contain no usable
NADPCO credential; real-database resolver counts remain an operational blocker and are documented
in `RuntimeFixReport.md`.
