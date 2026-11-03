# Feature 129 — Monthly Product Production and Sales Intelligence (v8)

## 1. Purpose and status

This design defines a small, read-only, on-demand comparison capability for one company and two available Jalali reporting periods. It explains monthly sales, production, sales quantity, sales rate, and the largest product-level movements using data already persisted by the current ingestion and product-revenue-mix pipeline.

The capability is a query over existing rows. It does not create a new financial fact, snapshot, forecast, cache, or accounting interpretation. Results are calculated in memory for the request and returned through the existing web-chat and Telegram response paths.

The design is standalone: implementation must not require consulting an earlier Feature 129 document to infer a formula, state, route, or persistence contract.

**Final status:** `READY_FOR_DESIGN_REVIEW`

## 2. Scope and simplification decisions

The first implementation supports one resolved company, a current period, and a comparison period. A caller may provide both periods; otherwise current is the latest available period and comparison is the immediately preceding available period. The result is a comparison, not a time-series warehouse.

The query reuses the existing `CompanyProductRevenueMix` read model through the existing product-revenue-mix repository conventions. Existing persisted rows are authoritative for this feature. The existing ingestion calculator and source normalizer remain unchanged; limitations in their data are surfaced as warnings where the read model permits detection.

The application owns four small responsibilities: resolve the company and periods, obtain the two existing row sets, normalize and conservatively match products in memory, and calculate a typed result. The language model may select and present the capability, but it never calculates financial values or changes the result.

## 3. Explicit non-goals

This version introduces no table, entity, migration, index, scheduled job, worker, outbox, queue, message contract, snapshot, manifest, pointer, alias subsystem, policy table, backfill, provider endpoint, or ingestion rewrite. It does not persist comparison results or maintain a separate evidence store. It does not replace `CompanyProductRevenueMixCalculator`, `MonthlyActivityTrendQueryUseCase`, or the existing semantic routing framework.

It does not promise audited accounting attribution. “Quantity-driven”, “price-driven”, and “mixed” are deterministic explanatory classifications based on the available monthly fields. A production-versus-sales difference is an inferred operational signal, not inventory movement, cost accounting, or a claim about physical stock.

## 4. Existing repository capabilities to reuse

The implementation must inspect and use the actual contracts and implementations, including:

- `src/backend/FinancialCopilot.Application/FinancialData/Ingestion/ProductRevenueMixContracts.cs`: existing product-revenue-mix repository and calculator contracts, including `GetLatestAsync`, `GetByPeriodAsync`, and the persisted monthly report line-item shape.
- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/EfCoreProductRevenueMixRepository.cs`: existing EF Core read path over `CompanyProductRevenueMix`, product ordering, and `ProductRevenueMixResponse` fields (`ProductName`, `SalesAmount`, `RevenueShare`, `Rank`, `Dominant`, `ProductionQuantity`, `SalesQuantity`, and `SalesRate`).
- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/CompanyProductRevenueMixCalculator.cs`: existing calculation behavior and data limitations. This feature must not silently recalculate ingestion facts differently.
- `src/backend/FinancialCopilot.Application/FinancialData/Ingestion/MonthlyActivityTrendQueryContracts.cs` and `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/MonthlyActivityTrendQueryUseCase.cs`: existing company resolution, period conventions, trend response conventions, and reusable chart-oriented semantics.
- `src/backend/FinancialCopilot.Application/AI/Orchestration/LlmAiIntentDetector.cs` and `ProductRevenueMixIntentRules.cs`: existing intent detection and product/sales language routing.
- `src/backend/FinancialCopilot.Application/AI/ModelProviders/AiModelContracts.cs`: `AiStructuredOutputContract`, which remains the boundary for model-produced structured output.
- `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs` and `Functions/MessagePersistenceFunction.cs`: existing tool invocation and assistant-message persistence flow.
- `src/backend/FinancialCopilot.Application/Conversations/ConversationContracts.cs`: `AssistantMessagePayload` and existing structured assistant payload conventions.
- `src/backend/FinancialCopilot.Infrastructure/Authentication/TelegramAssistantResponseRenderer.cs`: existing Telegram rendering and fallback behavior.
- `src/frontend/src/lib/chat.functions.ts`, `src/frontend/src/components/app/message-list.tsx`, and their existing tests: web-chat transport, structured-message rendering, and test conventions. Existing `monthly-activity-trend-chart.tsx` and its view model may be reused where appropriate.

The current development mode remains `MicrosoftAgentFrameworkV2` as configured in `src/backend/FinancialCopilot.API/appsettings.Development.json`; this feature must work through the active mode and preserve the V1 route’s existing behavior.

## 5. Query and period contract

The application query is conceptually:

```text
MonthlyProductComparisonQuery {
  companyText: string,
  currentPeriod: JalaliPeriod?,
  comparisonPeriod: JalaliPeriod?,
  productText: string?,
  focus: All | Sales | Production | Quantity | Rate
}
```

`companyText` is resolved by the existing `ICompanyResolverService` convention. An explicit current period is required to exist in persisted data. When omitted, current is the latest available period. When comparison is omitted, it is the immediately preceding available period for that company, not a fabricated calendar month. An explicit comparison period must exist. If the two periods are equal, the request is invalid.

The read adapter may extend an existing repository contract with a read-only available-period query, or introduce one application port with one implementation over the same existing EF read model. Either choice must avoid a new persistence model. It must fetch only the resolved company and the two selected periods, and it must filter to `OutputTypeId = 0` (product sales). Service-sales rows and unrelated output types are excluded.

Each returned result carries evidence references consisting of company identity, provider/source name when present, current and comparison periods, and the source product row identity or stable row coordinates available from the existing model (for example product rank plus period). If the underlying model cannot provide a unique source identity, the result includes `PossibleDuplicateRows` and does not claim row-level auditability.

## 6. Product identity and conservative matching

Matching is company-scoped and deterministic. The precedence is:

1. Use a reliable, nonzero persisted product identifier only when it is stable across the two source periods.
2. Otherwise compare a normalized Persian title together with a normalized unit.

Normalization is limited to deterministic Arabic/Persian character equivalence, digit normalization, whitespace collapse, ZWNJ normalization, punctuation removal, and canonical unit spelling. It does not use fuzzy similarity, edit distance, token guessing, LLM judgment, or global product aliases.

Rows with the same deterministic product key and compatible unit may be aggregated only within a period. Economically distinct products remain distinct. A duplicate-looking group with conflicting units, ambiguous names, or unstable identifiers is not decomposed; it is retained as an unattributed or ambiguous item and produces `ProductMatchAmbiguous`, `UnitChanged`, or `PossibleDuplicateRows` as applicable.

The matching output must retain source names and units so a user can see why two rows were or were not compared. A product filter is applied after safe matching and is company-scoped; an ambiguous filter match returns no decomposition rather than an invented match.

## 7. Deterministic calculations

For each period, total sales is the sum of `SalesAmount` across included product-sales rows. Company change is `currentTotal - comparisonTotal`. Percentage change is `change / comparisonTotal * 100` only when the denominator is nonzero; otherwise it is null and `ZeroCompanyRevenueChange` is emitted. Monetary and quantity units are preserved from the source and are never silently mixed.

For a continuing matched product, define:

```text
quantityEffect = (currentSalesQuantity - baseSalesQuantity) * baseSalesRate
priceEffect    = (currentSalesRate - baseSalesRate) * currentSalesQuantity
residual       = salesChange - quantityEffect - priceEffect
```

The result also exposes production-quantity change, sales-quantity change, and rate change. Effects are null when required inputs are missing, invalid, non-comparable, or have incompatible units. When all required inputs are valid, the three effects reconcile to the product sales change within the declared decimal tolerance. The implementation must use decimal arithmetic and a single documented rounding point for display.

For a product present only in current, sales change is current sales amount and the classification is `New`. For a product present only in comparison, sales change is the negative comparison sales amount and the classification is `Discontinued`. These lifecycle classifications do not invent missing quantity or price effects.

Invalid, negative-when-forbidden, non-finite, or zero-rate inputs are not coerced into plausible values. The affected decomposition is null, a `PartialDecomposition` or `InvalidQuantity`/`MissingRate` warning is emitted, and the company total remains based on the persisted sales amount when that amount itself is valid. A completely unusable row is `Unattributed` with a fixed reason code.

For valid continuing products, let `absQ = abs(quantityEffect)` and `absP = abs(priceEffect)`. If `absQ / (absQ + absP) >= 0.60`, classify as `QuantityDriven`; if `absP / (absQ + absP) >= 0.60`, classify as `PriceDriven`; otherwise classify as `Mixed`. A zero denominator is `Unclassified`. The threshold is part of the contract, not a model prompt.

The inferred production-sales signal is calculated only when production and sales quantities share a compatible unit: `productionQuantityChange - salesQuantityChange`. It is labeled `ProductionAboveSales`, `SalesAboveProduction`, `NoMaterialDifference`, or `Unavailable`; it is never described as inventory.

The result identifies the largest positive and largest negative product sales changes using absolute deterministic ordering: change descending/ascending, then normalized product key, then source rank. Contributions are `productChange / companyChange * 100` only when company change is nonzero; otherwise null.

## 8. Response, states, and evidence

The use case returns one typed `MonthlyProductComparisonResponse`, not a bag of model-generated fields:

```text
MonthlyProductComparisonResponse {
  state: Available | Partial | Empty | Unavailable | Error,
  company, currentPeriod, comparisonPeriod,
  currentTotalSales, comparisonTotalSales, salesChange, salesChangePercent,
  largestPositive, largestNegative, primaryDriver,
  products: [ProductComparisonItem],
  warnings: [WarningCode], evidence: [EvidenceReference], narrative: string?
}
```

`ProductComparisonItem` contains source product name, normalized display-safe unit, lifecycle classification, current/base sales amount, sales change, nullable contribution, current/base production quantity, current/base sales quantity, current/base rate, nullable quantity/price/residual effects, driver classification, production-sales signal, warnings, and evidence references.

`Available` means the requested comparison is complete. `Partial` means totals exist but one or more product decompositions are suppressed or incomplete. `Empty` means the company and periods resolve but no usable product-sales rows exist. `Unavailable` is reserved for a non-blocking unsupported focus or data state already represented by the contract. `Error` is reserved for an unexpected failure; it must not expose provider internals.

The web UI renders the typed totals, period labels, product table/chart, driver labels, warnings, and evidence/source disclosure. Telegram renders a compact text/table representation using the existing renderer and preserves the same numeric values and warning codes. No channel may turn null into zero or hide a blocking state.

## 9. Errors and warnings

The blocking set is closed: `CompanyNotFound`, `CurrentPeriodNotFound`, `ComparisonPeriodNotFound`, and `NoMonthlyProductData`. Blocking responses contain no invented total, comparison, or analysis.

The warning set is closed: `ProductMatchAmbiguous`, `UnitChanged`, `MissingRate`, `InvalidQuantity`, `PossibleDuplicateRows`, `PartialDecomposition`, and `ZeroCompanyRevenueChange`. Warnings are attached at item level when possible and at response level otherwise. The warning text is localized by the existing application conventions, while the code is stable for tests and clients.

Known limitations from existing ingestion—such as duplicate replacement or missing source values—are reported through this warning model. The feature does not repair persisted rows and does not imply that absence of a warning proves accounting completeness.

## 10. Semantic integration and safety

The capability is selected for requests such as “فروش ماهانه شرکت در ماه جاری نسبت به ماه قبل”، “کدام محصول بیشترین افزایش فروش را داشت؟”، and “تغییر تولید و فروش را مقایسه کن”. Routing must accept natural Persian variation and varied word order through the existing intent/routing mechanisms. It must distinguish a product-comparison request from a simple `MONTHLY_SALES` lookup and from published analysis content.

The structured tool input contains only the query contract above. The model may supply extracted company, period, product, and focus; the application validates and resolves them. The tool result is authoritative for values. The final answer may explain the deterministic driver labels and warnings, but must not add estimates, forecasts, unsupported causal claims, or alternative calculations.

Existing authorization, rate limiting, billing/usage accounting, conversation persistence, language selection, and provider-failure handling are reused. No new authorization rule or quota is introduced by this feature.

## 11. Acceptance criteria

Each criterion below is one independently testable behavior and maps to one named test. There are 20 criteria, not grouped ranges.

| ID | Atomic acceptance criterion | Named test |
|---|---|---|
| AC-01 | A known company name resolves to exactly one company identity using the existing resolver; an unknown name returns `CompanyNotFound`. | `ResolveCompany_UsesExistingResolver` |
| AC-02 | When current period is omitted, the use case selects the latest available persisted period for the resolved company. | `DefaultCurrentPeriod_SelectsLatestAvailable` |
| AC-03 | When comparison period is omitted, the use case selects the immediately preceding available period and never fabricates a missing calendar period. | `DefaultComparisonPeriod_SelectsPreviousAvailable` |
| AC-04 | The read query includes only persisted product-sales rows with `OutputTypeId = 0`. | `ReadQuery_ExcludesNonProductSalesRows` |
| AC-05 | Company totals equal the decimal sum of valid persisted `SalesAmount` values for each selected period. | `Totals_SumPersistedSalesAmounts` |
| AC-06 | Sales change equals current total minus comparison total, and percentage is null with `ZeroCompanyRevenueChange` when the comparison total is zero. | `Change_UsesZeroSafePercentage` |
| AC-07 | Rows with the same deterministic key and compatible unit aggregate within a period, while distinct or incompatible rows remain separate. | `Rows_AggregateOnlySafeDeterministicKeys` |
| AC-08 | An ambiguous or unit-conflicting product match suppresses decomposition and emits the applicable warning instead of using fuzzy or LLM matching. | `Matching_AmbiguitySuppressesDecomposition` |
| AC-09 | A continuing product exposes production, sales quantity, rate, and sales-value changes for both periods when source values are valid. | `ContinuingProduct_EmitsPeriodValuesAndChanges` |
| AC-10 | For a valid continuing product, quantity effect plus price effect plus residual equals product sales change within the documented decimal tolerance. | `Effects_ReconcileToProductSalesChange` |
| AC-11 | A current-only product is classified `New` with current sales change and no invented base effects. | `CurrentOnlyProduct_IsNew` |
| AC-12 | A comparison-only product is classified `Discontinued` with negative comparison sales change and no invented current effects. | `ComparisonOnlyProduct_IsDiscontinued` |
| AC-13 | Missing rate or invalid quantity produces null affected effects and the applicable warning without changing a valid company total. | `InvalidInputs_PartiallySuppressEffects` |
| AC-14 | A production/sales quantity difference is emitted only for compatible units and is labeled inferred, never inventory. | `ProductionSalesDifference_IsUnitSafeAndInferred` |
| AC-15 | Largest positive and negative product changes use deterministic tie-breaking and are present in the typed response when products exist. | `LargestChanges_UseDeterministicOrdering` |
| AC-16 | A valid product is `QuantityDriven` when quantity share is at least 60%, `PriceDriven` when price share is at least 60%, and `Mixed` otherwise. | `DriverClassification_UsesSixtyPercentRule` |
| AC-17 | The typed response preserves nulls, warning codes, periods, and evidence references without model-generated numeric substitutions. | `Response_IsTypedNullAndEvidenceSafe` |
| AC-18 | Persian requests with varied word order route to this capability when they request monthly product comparison, and do not route simple metric lookup requests here. | `SemanticRouting_DistinguishesProductComparison` |
| AC-19 | The web chat renders available, partial, empty, and blocking states with totals, warnings, and evidence disclosure. | `WebChat_RendersAllComparisonStates` |
| AC-20 | Telegram renders the same typed values and warning meanings in compact form, and an unexpected failure uses the existing safe fallback. | `Telegram_PreservesTypedValuesAndFallback` |

## 12. Testing strategy

Unit tests cover normalization, stable-ID precedence, compatible-unit aggregation, ambiguity suppression, lifecycle rows, decimal effects, reconciliation, threshold classification, deterministic ordering, null semantics, and warning codes. Repository integration tests use persisted rows for two Jalali periods and verify `OutputTypeId = 0`, latest/previous period selection, company scoping, and no-results states. Contract tests verify the serialized DTO used by web chat, Telegram, and the model tool boundary. Semantic tests cover Persian variants and disambiguation from simple monthly-sales lookup. Existing frontend message-list tests are extended for the four response states; existing Telegram renderer tests cover compact rendering and fallback. No test requires a new database object, scheduled process, or external provider call.

## 13. Implementation slices

### Slice 1 — Query, matching, and calculation

Add the application query/response contracts and one read adapter over the existing `CompanyProductRevenueMix` data. Implement the deterministic normalizer, conservative matcher, calculator, warning model, period selection, and evidence references. Reuse existing repository and company-resolution conventions. Add unit and repository integration tests for AC-01–17.

### Slice 2 — Tool and conversation integration

Register the capability with existing semantic intent rules and the active orchestration workflow. Validate model input, invoke the typed use case, persist the existing assistant payload shape, and keep existing authorization, usage, and error handling. Add semantic and contract tests for AC-17–18.

### Slice 3 — Channel presentation

Render the existing typed payload in web chat and Telegram, including totals, product movements, inferred-label disclosure, warnings, evidence, and all states. Reuse existing chart/message components where their shape fits; otherwise render a table without adding a new visualization subsystem. Add AC-19–20 tests.

## 14. File-impact map

Expected new application/infrastructure files, subject to repository naming conventions, are limited to a comparison contract/use case, a read adapter, a normalizer, and a calculator under the existing FinancialData/Ingestion areas. Expected test additions are unit/integration/contract tests under the existing test projects and focused additions to the existing frontend and Telegram renderer tests.

Expected existing-file integration points are:

- `src/backend/FinancialCopilot.Application/FinancialData/Ingestion/ProductRevenueMixContracts.cs` and its existing repository implementation, only for a read-only query seam if required.
- `src/backend/FinancialCopilot.Application/AI/Orchestration/ProductRevenueMixIntentRules.cs`, `LlmAiIntentDetector.cs`, and the existing workflow definition for routing/tool registration.
- `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Functions/MessagePersistenceFunction.cs` and `src/backend/FinancialCopilot.Application/Conversations/ConversationContracts.cs` for the existing payload boundary.
- `src/backend/FinancialCopilot.Infrastructure/Authentication/TelegramAssistantResponseRenderer.cs` for channel rendering.
- `src/frontend/src/lib/chat.functions.ts`, `src/frontend/src/components/app/message-list.tsx`, and existing message tests for web presentation.

These files are explicitly unchanged by this design: database migrations and schema configuration, raw provider ingestion, `CompanyProductRevenueMixCalculator` semantics, trend snapshot persistence, workers, messaging infrastructure, scheduled jobs, backfill tooling, and production-data files.

## 15. Review gate

Before implementation task decomposition, reviewers must confirm that the actual repository still exposes the referenced read model and fields, that no required field is silently unavailable, that the selected Jalali period convention matches existing behavior, and that the typed payload can travel through both active orchestration modes. Any missing persisted field converts the affected decomposition to a documented warning/null path; it must not trigger a new table or ingestion redesign in this version.

The design is ready for design review when the 20 atomic criteria, their named tests, the three slices, and the file-impact map are accepted as written.

READY_FOR_DESIGN_REVIEW
