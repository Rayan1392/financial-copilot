# Feature 129 — Design-v8 Final Bounded Review

## 1. Review metadata

- **Reviewed document:** `specs/129-monthly-product-production-sales-intelligence/Design-v8.md`
- **Review type:** Independent final bounded design review
- **Review date:** 2026-08-25
- **Repository:** `D:\Source\TahlilApp-AI`
- **Approval boundary:** Existing persisted data, one company, two Jalali monthly periods, deterministic application calculations, existing semantic orchestration, web chat, and Telegram
- **ACs reviewed:** 20 (`AC-01` through `AC-20`)
- **Verdict:** `NEED_CHANGES`

## 2. Executive verdict

Design-v8 is substantially simpler and correctly excludes the over-engineered persistence and background-processing systems from scope. Its response shape, deterministic arithmetic intent, conservative matching policy, three-slice plan, and channel goals are generally aligned with the fixed first-release boundary.

It is not yet safe for User Story and implementation-task decomposition because the proposed primary read model cannot provide two approval-critical inputs that the design requires: stable product identity/unit evidence and an explicit `OutputTypeId = 0` provenance filter. The existing persisted Product Revenue Mix row and response contracts contain product title, generated row identity, quantities, rate, and revenue, but no product code or quantity unit. The existing normalized monthly line-item persistence contains product code and unit, and the monthly-report persistence contains output type, but Design-v8 does not select or join that path. In addition, the existing Product Revenue Mix calculator filters out non-positive product totals, so the design’s stated negative-revenue behavior is not guaranteed by its declared source path.

These are Major findings, not requests to add migrations, jobs, snapshots, aliases, queues, or other excluded subsystems. The minimum correction is to define a read-only query over the existing normalized monthly report and line-item rows, or explicitly narrow the first release to conservative null/suppressed decomposition when those fields are unavailable. The design must also make negative persisted/source sales authoritative rather than relying on a read model whose current calculator excludes them.

## 3. Repository areas inspected

The following repository areas were inspected because they directly determine whether the simplified design can execute:

- Product Revenue Mix contracts and persisted row model:
  - `src/backend/FinancialCopilot.Application/FinancialData/Ingestion/ProductRevenueMixContracts.cs`
  - `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/CompanyProductRevenueMixRows.cs`
  - `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/CompanyProductRevenueMixConfigurations.cs`
  - `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/EfCoreProductRevenueMixRepository.cs`
  - `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/CompanyProductRevenueMixCalculator.cs`
- Existing normalized monthly report and line-item persistence:
  - `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/FinancialIngestionRows.cs`
  - `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/FinancialIngestionConfigurations.cs`
- Existing company resolution:
  - `src/backend/FinancialCopilot.Application/FinancialData/Ingestion/CompanyResolutionContracts.cs`
  - `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/CompanyResolverService.cs`
- Existing monthly trend period/query behavior:
  - `src/backend/FinancialCopilot.Application/FinancialData/Ingestion/MonthlyActivityTrendQueryContracts.cs`
  - `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/MonthlyActivityTrendQueryUseCase.cs`
- Existing semantic and structured orchestration:
  - `src/backend/FinancialCopilot.Application/AI/Orchestration/ProductRevenueMixIntentRules.cs`
  - `src/backend/FinancialCopilot.Application/AI/Orchestration/LlmAiIntentDetector.cs`
  - `src/backend/FinancialCopilot.Application/AI/ModelProviders/AiModelContracts.cs`
  - `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs`
  - `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Functions/MessagePersistenceFunction.cs`
- Existing response, replay, and channel paths:
  - `src/backend/FinancialCopilot.Application/Conversations/ConversationContracts.cs`
  - `src/backend/FinancialCopilot.Infrastructure/Conversations/Persistence/ConversationRepositories.cs`
  - `src/backend/FinancialCopilot.Infrastructure/Authentication/TelegramAssistantResponseRenderer.cs`
  - `src/frontend/src/lib/chat.functions.ts`
  - `src/frontend/src/components/app/message-list.tsx`
  - `src/frontend/src/components/app/monthly-activity-trend-chart.tsx`
- Relevant tests:
  - `tests/FinancialCopilot.UnitTests/CompanyProductRevenueMix075Tests.cs`
  - `tests/FinancialCopilot.UnitTests/AiIntentDetectorTests.cs`
  - existing monthly-sales and monthly-trend integration/unit tests

## 4. Fixed-scope validation

The document consistently describes the capability as read-only and on demand for one company and two Jalali periods. It excludes new tables, migrations, workers, queues, snapshots, manifests, alias subsystems, backfills, direct REST endpoints, and standalone dashboards. No prohibited subsystem is reintroduced indirectly. The existing conversation persistence is correctly treated as the normal assistant-message path rather than feature-specific result storage.

The `OutputTypeId = 0` requirement is present in the design, but it is not implementable against the specific Product Revenue Mix row contract identified as the primary source. This is reported as a data-query contract finding below, not as a request for new storage.

## 5. Financial-correctness validation

The design defines company total, absolute change, zero-safe percentage, product change, contribution, symmetric quantity effect, symmetric price effect, residual, new/discontinued handling, 60% driver classification, and an inferred production-versus-sales signal. The effect identity is algebraically usable:

```text
(currentQuantity - baseQuantity) * baseRate
+ (currentRate - baseRate) * currentQuantity
+ residual
= currentSalesValue - baseSalesValue
```

The design also states that reported sales values remain authoritative, invalid inputs suppress effects, unit incompatibility suppresses physical decomposition, and decimal reconciliation is tested. These are good approval-boundary decisions.

However, the declared persisted source does not carry the unit needed to decide whether those formulas are valid, and its current calculator excludes non-positive product totals. Therefore the formulas are sound in isolation but not yet safely connected to the actual data path. The findings below prevent approval.

## 6. Product-matching validation

The matching policy is deterministic and company-scoped. It correctly prefers a stable non-zero identifier, otherwise normalized title plus unit; it forbids fuzzy matching, preserves ambiguity, distinguishes lifecycle rows, and avoids silently combining incompatible units.

The repository evidence shows that `CompanyProductRevenueMixRow` has a generated `Guid Id`, `ExternalCompanyId`, `ProductName`, production quantity, sales quantity, rate, amount, rank, provider, and calculation timestamp, but no stable vendor product code and no unit. `ProductRevenueMixProductItem` exposes the same omission. Consequently, the design’s required precedence and unit-aware fallback cannot be executed from the primary read model as written.

## 7. Existing-data and architecture validation

Company resolution is feasible through `ICompanyResolverService.ResolveBySymbolAsync`, and explicit period reads are feasible through `ICompanyProductRevenueMixRepository.GetByPeriodAsync`. The existing `GetLatestAsync` orders by report year/month and `BuildResponse` selects the latest period deterministically. A previous-period query can be implemented as a read-only application/infrastructure adapter over existing data.

The existing normalized monthly persistence provides a more complete source for the approval boundary: `NormalizedMonthlyReportRow` stores report type and nullable output type, while `NormalizedMonthlyReportLineItemRow` stores `ProductCode`, `Unit`, quantities, amount, title, and rate. This path can support the required company/period/output-type/product/evidence query without new persistence. Design-v8 currently names the Product Revenue Mix table as the read adapter’s source and does not specify this necessary join or fallback.

The existing structured assistant payload already has feature-result extension points, including product revenue mix and monthly activity trend results, and `MessagePersistenceFunction` already persists these through the existing conversation exchange. The architecture therefore needs no new response hierarchy or persistence subsystem; it needs a corrected source contract and a precise mapping into the existing result flow.

## 8. Semantic and channel validation

The design correctly keeps semantic routing in the existing orchestration, keeps arithmetic in application code, and requires server-calculated values for web and Telegram. The existing semantic rules and detector are actual repository paths. The existing assistant payload, persistence flow, Telegram renderer, frontend chat mapping, and message-list components are actual integration points.

A task-stage clarification behavior for an unresolved company or explicit missing period should be made concrete when the design is corrected. The existing response contracts already contain clarification fields, so this is not currently a Major. Natural Persian variation can be handled through the existing semantic capability/tool route and bounded input validation; the design does not require a rigid sentence parser.

## 9. Acceptance-criteria audit

Design-v8 contains 20 individually written AC rows, within the required range of 15–25. Each row describes one observable behavior and names a meaningful test scenario. The ACs cover resolution, period selection, source filtering, totals, changes, matching, continuing/new/discontinued products, invalid inputs, production-sales signaling, ranking, driver classification, typed response, semantic routing, web rendering, and Telegram rendering.

The ACs are not grouped ranges and do not require excluded infrastructure. AC-07, AC-08, AC-10, AC-14, and AC-16 depend on unit/product identity data that the named Product Revenue Mix source does not expose. AC-04 depends on an `OutputTypeId` field that is not present on that source row. AC-05 and AC-10 also depend on a source path that does not discard non-positive product totals. These are source-contract dependencies, not AC-count or atomicity defects.

## 10. Findings

### F-01 — MAJOR — Product identity and unit contract is unavailable in the named read model

- **Exact Design-v8 section and line:** Section 5, lines 60–62; Section 6, lines 69–75; Section 7, lines 89–99; Section 8, lines 112–116; AC-07, AC-08, AC-10, AC-14, and AC-16.
- **Relevant repository evidence:** `CompanyProductRevenueMixRow` at `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/CompanyProductRevenueMixRows.cs:3-40` has no product-code or unit property. `ProductRevenueMixProductItem` at `src/backend/FinancialCopilot.Application/FinancialData/Ingestion/ProductRevenueMixContracts.cs:17-34` likewise has no unit or stable product identifier. `EfCoreProductRevenueMixRepository.BuildResponse` at `.../EfCoreProductRevenueMixRepository.cs:86-121` strips the ORM row to that incomplete DTO.
- **Related AC:** AC-07, AC-08, AC-10, AC-14, AC-16.
- **Concrete failure scenario:** Two periods contain the same displayed title with different vendor units, or two products share a title but have different product codes. The proposed `CompanyProductRevenueMix` query cannot tell whether the rows are compatible. Matching by title alone would create false quantity/price effects; using generated row `Id` would fail cross-period matching; treating all quantities as compatible could report a materially wrong production-sales difference.
- **Minimal required correction:** Change Section 5 and the file-impact map to specify a read-only query over the existing normalized monthly report and line-item persistence, using `ProductCode` when stable and `Unit` plus normalized title as fallback, or explicitly state that missing unit/product identity forces `UnitChanged`/`ProductMatchAmbiguous` and null decomposition. Add the corresponding missing-data rule to AC-07/08/10/14/16. No new table or migration is required.

### F-02 — MAJOR — `OutputTypeId = 0` cannot be applied to the named Product Revenue Mix rows

- **Exact Design-v8 section and line:** Section 5, line 60; AC-04 at line 147; Section 14, lines 185–196.
- **Relevant repository evidence:** `CompanyProductRevenueMixRow` at `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/CompanyProductRevenueMixRows.cs:3-40` has no output-type property. `NormalizedMonthlyReportRow` stores nullable `OutputType` at `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/FinancialIngestionRows.cs:312-346`; the existing calculator filters source reports by `ReportType = "ProductSales"` and `OutputType = 0` in `CompanyProductRevenueMixCalculator.cs:25-39`, then persists only the derived mix rows. `EfCoreProductRevenueMixRepository.GetByPeriodAsync` at `.../EfCoreProductRevenueMixRepository.cs:24-38` filters only company/year/month on the mix table.
- **Related AC:** AC-04, with downstream impact on AC-05, AC-09, and AC-10.
- **Concrete failure scenario:** An implementation follows the named existing repository and queries `CompanyProductRevenueMix` by company and period. There is no `OutputTypeId` predicate to apply, so the implementation either cannot prove the required provenance or invents an additional field/assumption. If the table ever contains rows from another derivation, service/YTD/adjustment data could be included without detection.
- **Minimal required correction:** Define the query source as the existing normalized report plus line-item rows, joining by `MonthlyReportId` and filtering `ReportType = ProductSales` and `OutputType = 0`, or document the Product Revenue Mix table’s proven invariant and remove the impossible row-level `OutputTypeId` claim from AC-04. The first option is the safer correction because it also supplies unit, product code, and source row evidence.

### F-03 — MAJOR — Negative product revenue is not retained by the declared existing calculation path

- **Exact Design-v8 section and line:** Section 7, lines 79–89; Section 14, lines 185–196; AC-05 and AC-10 at lines 148 and 151.
- **Relevant repository evidence:** `CompanyProductRevenueMixCalculator.RecalculateAsync` groups line items and applies `.Where(p => p.SalesAmount > 0)` at `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/CompanyProductRevenueMixCalculator.cs:50-65`. It then persists the filtered list. The Product Revenue Mix read model therefore does not guarantee that a negative product sales value reaches the feature, even though the review boundary requires reported sales value to remain authoritative and negative revenue to be retained.
- **Related AC:** AC-05 and AC-10.
- **Concrete failure scenario:** A persisted/source monthly report contains a valid negative product sales amount, such as a return or reversal. The feature reads only the derived Product Revenue Mix rows; the existing calculator has already removed a product aggregate whose amount is non-positive. Company/product totals and contributor ranking can then omit a real reported movement, so reconciliation to reported product revenue is false.
- **Minimal required correction:** Make the feature’s read contract use the existing normalized line-item rows and sum valid `SalesAmount` values without a positive-only filter, while retaining a warning for unusable rows. If Design-v8 intentionally accepts the existing positive-only derived model, it must explicitly remove the negative-revenue requirement and downgrade the affected AC coverage; that would narrow the approval boundary and should be stated rather than implied.

## 11. Non-blocking task-stage follow-ups

- **MINOR:** Section 10 should state the bounded clarification result for an unresolved company, invalid explicit period, or unsupported product filter. Existing `ClarificationRequired` and `ClarificationMessage` fields make this a localized task detail.
- **MINOR:** The file-impact map should name the existing normalized report/line-item persistence files if F-01/F-02 are corrected, rather than presenting only Product Revenue Mix contracts as the source seam.
- **NOTE:** The generated Product Revenue Mix row `Id` is useful as a persisted-row reference but is not a stable cross-period product identity because the calculator creates a new `Guid` during each upsert. Evidence should label it as a row reference, not a product identity.
- **NOTE:** Existing Telegram rendering uses explicit monthly-trend formatting and a media fallback. The new comparison renderer should preserve that established behavior while keeping large product lists bounded, but this is a presentation task detail and not an approval blocker.

## 12. Approval checklist

| Check | Result |
|---|---|
| Read-only, on-demand, one company, two Jalali periods | Pass |
| No prohibited new subsystem | Pass |
| Deterministic company and period resolution | Pass in principle; source correction required for previous-period query details |
| Existing-data query includes company, periods, product rows, and evidence | Fail: product/unit/output-type fields are not available in the named mix source |
| Conservative product matching | Fail: required stable identity/unit fields are absent from the named source |
| Financial formulas and zero-safe behavior | Pass algebraically; fail as an executable contract until source/unit and negative-value behavior are corrected |
| Deterministic typed response | Pass |
| Existing semantic architecture | Pass in principle |
| Existing web and Telegram paths | Pass in principle |
| 15–25 atomic ACs | Pass: 20 ACs |
| No more than three usable slices | Pass: 3 slices |
| No migration/worker/queue requirement | Pass |

## 13. Final verdict

The design is not approved for decomposition. Resolve F-01, F-02, and F-03 with localized source-contract wording and query-path corrections, then repeat the bounded review. No prohibited subsystem needs to be reintroduced.

NEED_CHANGES
