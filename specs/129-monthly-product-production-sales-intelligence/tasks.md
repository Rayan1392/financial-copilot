# Feature 129 — Implementation Tasks

Tasks are ordered by dependency and derived from `Design-v9.md` and `user-story.md`. New filenames are implementation choices; each new file must be created in the stated target directory with the stated responsibility.

## Slice 1 — Read query and deterministic calculation

### T129-01 — Define application contracts

- Objective: Add the typed query, Jalali period, response, product item, state, warning, evidence, and focus contracts.
- Existing files likely to modify: `src/backend/FinancialCopilot.Application/FinancialData/Ingestion/` (new contract file); `CompanyResolutionContracts.cs` only if shared period/resolution types are reused.
- New files: one contract file in the existing FinancialData/Ingestion directory.
- Steps: define nullable fields and stable warning/state enums; document ProductSales/OutputType 0; expose evidence without ORM types; keep `decimal` values and nulls.
- Related ACs: AC-05–AC-17.
- Tests: `Response_IsTypedNullAndEvidenceSafe` and contract serialization coverage.
- Completion: application contracts compile without EF/provider references and represent every design state.
- Dependencies: none.

### T129-02 — Add normalized persistence read port and adapter

- Objective: Read two periods from normalized monthly persistence with exact source filtering.
- Existing files likely to modify: `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/FinancialIngestionDbContext.cs` only for registration if needed; `FinancialIngestionRows.cs` and `FinancialIngestionConfigurations.cs` are inspected contracts and should remain unchanged unless required by compilation.
- New files: one application repository interface in `Application/FinancialData/Ingestion/`; one EF adapter under `Infrastructure/Financial/Ingestion/NadpcoApi/` or the existing normalized ingestion area.
- Steps: query `MonthlyReports` by resolved `ExternalCompanyId`; filter `ReportType == "ProductSales"` and `OutputType == 0`; resolve latest/previous qualifying Jalali periods; join `MonthlyReportLineItems` by `MonthlyReportId`; project `ProductCode`, `Title`, `Unit`, `ProductionQuantity`, `SalesQuantity`, `SalesAmount`, `SalesRate`, report fields, and row ids; use `AsNoTracking`; never call providers.
- Related ACs: AC-02–AC-05.
- Tests: `ReadQuery_ExcludesNonProductSalesRows`, `DefaultCurrentPeriod_SelectsLatestAvailable`, `DefaultComparisonPeriod_SelectsPreviousAvailable`, repository company/period scoping test.
- Completion: a persisted fixture proves ProductSales/OutputType 0 only and source evidence is available.
- Dependencies: T129-01.

### T129-03 — Implement company and explicit-period resolution

- Objective: Reuse the canonical resolver and implement bounded clarification states.
- Existing files likely to modify: `src/backend/FinancialCopilot.Application/FinancialData/Ingestion/CompanyResolutionContracts.cs`; new use-case file in the existing application/infrastructure ingestion directories.
- New files: none beyond the use-case file if T129-02 does not contain it.
- Steps: invoke `ResolveBySymbolAsync`; map null to `CompanyNotFound`; validate Jalali year/month, equality, explicit availability, and missing data; never fall back from an invalid explicit period.
- Related ACs: AC-01–AC-03 and AC-17.
- Tests: `ResolveCompany_UsesExistingResolver`, invalid/equal/unavailable period clarification tests.
- Completion: every blocking condition returns a typed result/clarification with no invented totals.
- Dependencies: T129-01, T129-02.

### T129-04 — Implement deterministic product normalization and matching

- Objective: Match and aggregate products safely by company, stable code, title, and compatible unit.
- Existing files likely to modify: none required; reuse existing normalization conventions where appropriate.
- New files: matcher/normalizer file in the Feature 129 application calculation directory; unit test file under `tests/FinancialCopilot.UnitTests/`.
- Steps: normalize Arabic/Persian letters, digits, whitespace, ZWNJ, punctuation, and units; reject blank/zero codes; prefer valid code; fallback to title+unit; preserve ambiguous/incompatible rows; keep row ids as evidence only; apply optional product filter after matching.
- Related ACs: AC-07, AC-08, AC-14.
- Tests: `Matching_AggregatesOnlySafeKeys`, `Matching_AmbiguitySuppressesDecomposition`, compatible/incompatible unit and duplicate scenarios.
- Completion: no fuzzy, alias, semantic, cross-company, or LLM matching exists.
- Dependencies: T129-01 and T129-02.

### T129-05 — Implement calculations, warnings, ranking, and reconciliation

- Objective: Calculate totals and deterministic product explanations, retaining negative values.
- Existing files likely to modify: none; do not modify `CompanyProductRevenueMixCalculator.cs`.
- New files: calculator file in the Feature 129 application calculation directory; unit tests in `tests/FinancialCopilot.UnitTests/`.
- Steps: sum valid positive/zero/negative `SalesAmount`; exclude only null/unusable amounts with warning; calculate change and zero-safe percentage; calculate quantity/price/residual effects; handle lifecycle products; classify 60% drivers; calculate unit-safe inferred production-sales signal; rank positive/negative contributors with fixed tie-breakers; reconcile within one decimal tolerance; preserve nulls.
- Related ACs: AC-05–AC-16.
- Tests: `Totals_RetainPositiveZeroAndNegativeSales`, `Change_UsesZeroSafePercentage`, `Effects_ReconcileToProductSalesChange`, `CurrentOnlyProduct_IsNew`, `ComparisonOnlyProduct_IsDiscontinued`, `InvalidInputs_PartiallySuppressEffects`, `ProductionSalesDifference_IsUnitSafeAndInferred`, `LargestChanges_UseDeterministicOrdering`, `DriverClassification_UsesSixtyPercentRule`.
- Completion: all arithmetic is decimal application code; negative returns/reversals appear in totals and rankings; no positive-only filter exists.
- Dependencies: T129-04.

### T129-06 — Integrate use case and repository tests

- Objective: Compose resolution, read, matching, and calculation into one deterministic executor.
- Existing files likely to modify: dependency registration file in the existing Infrastructure composition root, only if required; existing test fixtures under `tests/FinancialCopilot.UnitTests/` and `tests/FinancialCopilot.IntegrationTests/`.
- New files: focused Feature 129 unit and repository integration test files in existing test projects.
- Steps: invoke adapter with one company/two periods; map empty/partial/available states; verify evidence and warnings; use in-memory/test database rows for ProductSales, service, YTD, adjustment, negative, null, zero, and incompatible-unit cases.
- Related ACs: AC-01–AC-17.
- Tests: all Slice 1 named tests from the acceptance table.
- Completion: Slice 1 passes without external provider calls or schema changes.
- Dependencies: T129-01 through T129-05.

## Slice 2 — Semantic and conversation integration

### T129-07 — Register semantic capability and typed slot mapping

- Objective: Route natural Persian product comparisons through existing orchestration.
- Existing files likely to modify: `src/backend/FinancialCopilot.Application/AI/Orchestration/ProductRevenueMixIntentRules.cs`, `LlmAiIntentDetector.cs`, and `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs`.
- New files: only if the existing capability registry requires a Feature 129 registration file, in its current orchestration directory.
- Steps: extend capability descriptions/registration; distinguish product comparison from simple `MONTHLY_SALES`, Product Revenue Mix, and analysis; accept varied word order; map company, two optional Jalali periods, product, and focus; do not create rigid sentence-pattern routing.
- Related ACs: AC-18.
- Tests: `SemanticRouting_DistinguishesProductComparison` with Persian variants and simple metric negatives.
- Completion: active V2 and existing V1 path can select the typed capability without arithmetic in the model.
- Dependencies: T129-06.

### T129-08 — Map validation, invocation, and assistant payload

- Objective: Invoke the deterministic use case and persist the typed result through the existing conversation flow.
- Existing files likely to modify: `src/backend/FinancialCopilot.Application/Conversations/ConversationContracts.cs`, `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Functions/MessagePersistenceFunction.cs`, and the orchestration integration point.
- New files: focused conversation/contract tests under `tests/FinancialCopilot.UnitTests/` and `tests/FinancialCopilot.IntegrationTests/`.
- Steps: validate extracted slots locally; map company/period clarification; invoke use case once; add the typed result using the existing assistant payload extension convention; preserve nulls, states, warnings, evidence, language, usage, replay, and safe unexpected-error handling.
- Related ACs: AC-17, AC-18.
- Tests: `Response_IsTypedNullAndEvidenceSafe`, semantic endpoint/conversation persistence and replay tests.
- Completion: persisted exchanges reproduce the same typed result and no model numeric substitution is accepted.
- Dependencies: T129-07.

## Slice 3 — Web and Telegram presentation

### T129-09 — Map and render bounded web comparison

- Objective: Present the typed response in existing web chat.
- Existing files likely to modify: `src/frontend/src/lib/chat.functions.ts`, `src/frontend/src/components/app/message-list.tsx`, and existing frontend message tests.
- New files: comparison view/model component in `src/frontend/src/components/app/` and focused tests, if existing component boundaries require it.
- Steps: map DTO states and nulls; render period labels, totals, change, driver, largest positive/negative products, bounded product table, warnings, evidence, and unavailable/empty states; preserve RTL Persian formatting and source units; never calculate financial values in client code.
- Related ACs: AC-19.
- Tests: `WebChat_RendersAllComparisonStates` and null/negative/RTL rendering cases.
- Completion: available, partial, empty, unavailable, and blocking payloads render without null-to-zero conversion or unbounded output.
- Dependencies: T129-08.

### T129-10 — Add Telegram compact renderer

- Objective: Render the same typed values and warnings through Telegram.
- Existing files likely to modify: `src/backend/FinancialCopilot.Infrastructure/Authentication/TelegramAssistantResponseRenderer.cs` and its existing tests.
- New files: none unless the renderer’s established test organization requires a focused Feature 129 test file under `tests/FinancialCopilot.UnitTests/`.
- Steps: add bounded Persian text/table formatting; preserve signs, nulls, warnings, units, and inferred production-sales wording; use existing safe fallback for unexpected failure; do not expose provider internals.
- Related ACs: AC-20.
- Tests: `Telegram_PreservesTypedValuesAndFallback` with positive, zero, negative, partial, empty, and error states.
- Completion: Telegram values match the typed server result and output remains bounded.
- Dependencies: T129-08.

## Final implementation gate

- Confirm AC-01 through AC-20 each map to at least one task and automated test.
- Confirm no task adds excluded tables, migrations, snapshots, workers, queues, manifests, aliases, backfills, or background computation.
- Confirm the query path performs no provider calls.
- Confirm no client or LLM performs financial arithmetic.
- Confirm existing unrelated monthly features and `CompanyProductRevenueMixCalculator` behavior remain unchanged.
- Confirm ProductSales/OutputType 0 filtering, negative-value retention, conservative matching, null semantics, reconciliation, conversation replay, web rendering, and Telegram rendering are verified.

READY_FOR_IMPLEMENTATION
