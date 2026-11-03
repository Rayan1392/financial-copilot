# Feature 129 — Monthly Product Production and Sales Intelligence (v9)

## 1. Purpose and status

Feature 129 is a read-only, on-demand comparison of one resolved company’s ProductSales data across two Jalali monthly periods. It returns company totals, product movements, production and sales quantities, sales rates, deterministic driver classifications, contributor rankings, warnings, and source evidence through the existing Financial Copilot conversation flow.

This version closes only the three bounded findings in `Design-v8-review.md`. The authoritative source is the existing normalized monthly-report persistence, not the derived positive-only Product Revenue Mix read model. The query reads `FinancialIngestionDbContext.MonthlyReports` and `MonthlyReportLineItems`, joins line items through `MonthlyReportId`, and performs all matching and arithmetic in deterministic application code.

Verified repository facts are stated as facts below. Proposed Feature 129 contracts and files are marked as proposed. The feature remains within the existing web-chat and Telegram paths and does not create a route, table, migration, worker, queue, snapshot, manifest, alias subsystem, backfill, or background calculation.

## 2. Fixed scope and non-goals

R1 resolves exactly one company with `ICompanyResolverService.ResolveBySymbolAsync`. It accepts a selected Jalali month and, by default, compares it with the immediately preceding available persisted Jalali month for that company. If the semantic contract supplies two explicit periods, both must exist and must differ. The query is read-only and fetches only the selected company and periods.

The query includes only reports where `ReportType == "ProductSales"` and `OutputType == 0`. `NormalizedMonthlyReportRow.OutputType` is the actual property name; it is nullable because legacy and service rows may not have an output type. Service sales, YTD, adjustment, previous-YTD, and legacy rows without the required filter values are excluded. The selected reports are joined to `NormalizedMonthlyReportLineItemRow` through `MonthlyReportId`.

The line-item fields used are the verified existing fields: `ProductCode`, `Title`, `Unit`, `ProductionQuantity`, `SalesQuantity`, `SalesRate`, and nullable `SalesAmount`. Report identity uses `NormalizedMonthlyReportRow.Id`, `ExternalCompanyId`, `PeriodStart`, `PeriodEnd`, `ProviderName`, `ExternalReportId`, and the line-item `Id` as row evidence. `CompanyId` may be used where populated, but the canonical resolver’s `ResolvedCompany.ExternalCompanyId` is the required company filter.

R1 explicitly excludes new database objects, schema changes, migrations, immutable snapshots, revision or accepted-pointer systems, product aliases, manifests, workers, RabbitMQ queues, outbox/retry state machines, provider calls from the query path, ingestion rewrites, historical backfill, anomaly detection, forecasting, investment recommendations, direct REST endpoints, standalone dashboards, and feature-specific persistence.

The existing `CompanyProductRevenueMixCalculator` and `EfCoreProductRevenueMixRepository` remain unchanged for existing monthly Product Revenue Mix behavior. Their positive-only derived rows are not authoritative for this feature because the calculator currently applies a `SalesAmount > 0` filter and the derived DTO does not contain product code or unit.

## 3. Existing contracts and integration seams

The application already owns `ICompanyResolverService`, `ResolvedCompany`, `ProductRevenueMixContracts.cs`, `MonthlyActivityTrendQueryContracts.cs`, `AssistantMessagePayload`, and the AI model contracts. `ResolvedCompany` supplies the canonical external company identifier. `FinancialIngestionDbContext` already exposes `MonthlyReports` and `MonthlyReportLineItems`.

The normalized persistence facts are:

- `NormalizedMonthlyReportRow` maps to `MonthlyReports` and contains `ExternalCompanyId`, `PeriodStart`, `PeriodEnd`, `ProviderName`, `ExternalReportId`, `ReportType`, nullable `OutputType`, and `Id`.
- `NormalizedMonthlyReportLineItemRow` maps to `MonthlyReportLineItems` and contains `MonthlyReportId`, `ProductCode`, nullable `Title`, nullable `Unit`, nullable `ProductionQuantity`, nullable `SalesQuantity`, nullable `SalesAmount`, and nullable `SalesRate`.
- `NormalizedMonthlyReportLineItemRowConfiguration` has a unique `(MonthlyReportId, ProductCode)` index, but the feature must still detect blank codes and ambiguous title/unit identity conservatively.
- `MonthlyReport` domain objects and `MonthlyReportLineItem` are not sufficient as the read DTO because the persistence row contains the required title, unit, and rate fields; the adapter must project the persistence rows into application-owned records.

Existing conversation persistence uses `AssistantMessagePayload`, which already has nullable feature-result extension points such as `ProductRevenueMixResult` and `MonthlyActivityTrendResult`. The implementation may add one typed Feature 129 result property or use the repository’s established typed extension mechanism, but it must not serialize a bag of model-generated financial fields. `MessagePersistenceFunction` remains the persistence path. Web transport uses `src/frontend/src/lib/chat.functions.ts` and `message-list.tsx`; Telegram uses `TelegramAssistantResponseRenderer.cs`.

## 4. Query contract and period resolution

The proposed application input is:

```text
MonthlyProductComparisonQuery {
  companyText: string,
  currentPeriod: JalaliPeriod?,
  comparisonPeriod: JalaliPeriod?,
  productText: string?,
  focus: All | Sales | Production | Quantity | Rate
}
```

`JalaliPeriod` contains a validated integer year and month in 1..12. The semantic layer may extract these values; the application validates them and never trusts an LLM-produced total or comparison. An omitted current period resolves to the latest available period among qualifying persisted reports for the resolved company. An omitted comparison period resolves to the immediately preceding available qualifying period, ordered by the repository’s Jalali `(year, month)` convention derived from `PeriodStart`; it must not fabricate a missing calendar month. An explicit period that has no qualifying report returns the corresponding blocking state. Equal periods are invalid and return a clarification/validation response.

The read adapter performs one bounded query for the resolved external company and requested period range, applies `ReportType == "ProductSales"` and `OutputType == 0`, joins line items, and projects only required fields. It must use `AsNoTracking`, avoid provider calls, and return source row/report coordinates for evidence. The adapter may expose an available-period query and a two-period line-item query under a new application port in the existing FinancialData/Ingestion area. No new ORM entity is created.

Clarification behavior is fixed. If the company resolver returns null, return `ClarificationRequired = true` with a localized message asking for one valid company ticker/name; no totals are returned. If an explicit period is malformed, equal to the other period, or unavailable, return a localized clarification naming the invalid/unavailable period and the required Jalali format; no invented fallback is used. If a valid company and periods have no qualifying rows, return `NoMonthlyProductData` with an unavailable/empty typed result and no financial narrative. An unsupported focus is validated locally and returns `Unavailable` with clarification; it does not cause a second interpretation.

## 5. Deterministic identity, units, and data quality

Matching is company-scoped and never fuzzy. First, use the same valid, nonblank, nonzero `ProductCode` across periods. A code is valid only after trimming and deterministic normalization; blank, whitespace-only, or known zero placeholders do not qualify. A code match is still required to have compatible normalized units. If a reliable code is absent, use normalized `Title` plus compatible normalized `Unit`.

Title/unit normalization is limited to Arabic/Persian character equivalence, Persian/Arabic digit normalization, whitespace and ZWNJ normalization, punctuation removal, and canonical unit spelling. It does not use edit distance, token guessing, semantic similarity, aliases, global product knowledge, or LLM judgment. Matching is performed after the company and report filters, so identical titles in different companies can never merge.

Rows sharing a deterministic key and compatible unit may be aggregated within a period. The display title and raw unit are retained. A title/code collision with incompatible units, unstable identity, blank identity on both sides, or otherwise ambiguous correspondence remains separate. The item receives `ProductMatchAmbiguous`, `UnitChanged`, or `PossibleDuplicateRows` as applicable; quantity/rate decomposition is suppressed for that item, but valid reported sales value remains in totals and product contribution calculations. No incompatible units are merged.

The generated line-item `Id` is evidence only, never a cross-period product identity. A product filter is applied after safe matching and is company-scoped. If the filter would require an ambiguous match, return no decomposed product match rather than guessing.

## 6. Financial calculations and reconciliation

All arithmetic uses `decimal`. For each selected period, company total sales is the sum of every valid non-null `SalesAmount` in qualifying line items, including positive, zero, and negative values. No `SalesAmount > 0` predicate is permitted in the Feature 129 query or calculation path. A row with a valid negative amount remains in totals, product change, contribution, and deterministic ranking. Negative values are sign-classified as reported reversals/returns where useful; they are not converted to zero.

Company sales change is `currentTotal - comparisonTotal`. Percentage change is `change / comparisonTotal * 100` only when the denominator is nonzero; otherwise it is null and `ZeroCompanyRevenueChange` is emitted. A valid zero amount is retained. A null amount is excluded from the total and emits `Unattributed`/`InvalidSalesAmount` at item level if the row is otherwise identifiable. The warning code is stable even if its localized text changes.

For a continuing safely matched product, let `base` mean comparison period and `current` mean selected period:

```text
quantityEffect = (currentSalesQuantity - baseSalesQuantity) * baseSalesRate
priceEffect    = (currentSalesRate - baseSalesRate) * currentSalesQuantity
residual       = productSalesChange - quantityEffect - priceEffect
```

The decomposition is available only when quantities, rates, and units are present, finite in the .NET decimal domain, valid for the calculation, and comparable. Effects reconcile to product sales change within one documented decimal tolerance after calculation; display rounding occurs once after reconciliation. `SalesRate == 0` is invalid for decomposition when a nonzero sales quantity requires a meaningful rate; it does not invalidate a separately valid reported sales amount. Missing rate produces `MissingRate`; invalid quantity produces `InvalidQuantity`; incompatible units produce `UnitChanged`; partially calculable items produce `PartialDecomposition`.

For a current-only item, product sales change is current sales amount and classification is `New`; no base quantity/rate effect is invented. For a comparison-only item, product sales change is the negative comparison sales amount and classification is `Discontinued`; no current effect is invented. Both lifecycle rows retain zero and negative amounts.

For a valid continuing item, `absQ = abs(quantityEffect)` and `absP = abs(priceEffect)`. If `absQ + absP` is zero, driver is `Unclassified`. Otherwise quantity share at least 0.60 is `QuantityDriven`, price share at least 0.60 is `PriceDriven`, and all other cases are `Mixed`. A production-sales signal is emitted only when production and sales quantities have compatible units: `productionQuantityChange - salesQuantityChange`, labeled `ProductionAboveSales`, `SalesAboveProduction`, `NoMaterialDifference`, or `Unavailable`. It is explicitly inferred and never called inventory.

Product contribution is `productSalesChange / companySalesChange * 100` only when company change is nonzero; otherwise it is null. Largest positive and negative products are selected by product change descending/ascending, then normalized deterministic key ascending, then minimum source rank, then source row id ascending. This ordering applies to negative values and ties. The response contains both contributors when products exist.

## 7. Typed response and presentation boundary

The proposed typed result is:

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

`ProductComparisonItem` contains raw/display product title, raw and normalized unit, product identity classification, current/base sales amount, sales change, nullable contribution, production quantity, sales quantity, sales rate for both periods, nullable quantity/price/residual effects, driver, inferred production-sales signal, warnings, and row/report evidence. Null remains null at serialization and rendering boundaries.

Blocking codes are `CompanyNotFound`, `CurrentPeriodNotFound`, `ComparisonPeriodNotFound`, `EqualPeriods`, `InvalidPeriod`, and `NoMonthlyProductData`. Warning codes are `ProductMatchAmbiguous`, `UnitChanged`, `MissingRate`, `InvalidQuantity`, `InvalidSalesAmount`, `PossibleDuplicateRows`, `PartialDecomposition`, and `ZeroCompanyRevenueChange`. `Available` means requested data and decomposition are complete; `Partial` means totals are valid but one or more decompositions are suppressed; `Empty` means periods resolve but no usable rows remain; `Unavailable` represents an explicitly unsupported focus/data state; `Error` is an unexpected safe failure without provider internals.

The web chat renders period labels, totals, change, driver explanation, bounded product rows, largest contributors, warnings, and evidence/source disclosure. It must not calculate arithmetic or turn null into zero. A bounded default renders a finite number of products and provides no standalone dashboard. Telegram renders the same values and warning meanings in a compact text/table form using the existing renderer and safe fallback. Both channels preserve RTL/Persian labels and source units.

## 8. Semantic and orchestration safety

The existing semantic/capability architecture is extended to recognize natural Persian variation and varied word order for monthly product comparisons, such as requests for monthly product sales change, the product with the largest increase, or production-versus-sales comparison. The capability must be distinct from a simple `MONTHLY_SALES` lookup, existing Product Revenue Mix lookup, and published analysis content. No rigid Persian sentence-pattern router is introduced.

The model may provide `companyText`, explicit periods, optional product text, and focus through the existing structured/tool boundary. The application validates each slot, resolves the company and periods, invokes the deterministic use case, and treats the typed result as authoritative. The LLM may explain returned driver labels and warnings but must not calculate totals, percentages, effects, rankings, or alternative estimates. Existing authorization, rate limiting, billing/usage accounting, language selection, conversation persistence, replay, and provider-failure handling are reused.

## 9. Acceptance criteria

Each criterion is atomic, independently testable, and mapped to one primary vertical slice and one named test.

| ID | Atomic acceptance criterion | Named test | Slice |
|---|---|---|---|
| AC-01 | A known company resolves to exactly one canonical company identity through `ICompanyResolverService`; an unknown name returns `CompanyNotFound`. | `ResolveCompany_UsesExistingResolver` | 1 |
| AC-02 | With no current period, the latest qualifying persisted period is selected. | `DefaultCurrentPeriod_SelectsLatestAvailable` | 1 |
| AC-03 | With no comparison period, the immediately preceding qualifying available period is selected and no missing calendar month is fabricated. | `DefaultComparisonPeriod_SelectsPreviousAvailable` | 1 |
| AC-04 | The read query joins normalized reports and line items and includes only `ReportType = ProductSales` and `OutputType = 0`. | `ReadQuery_ExcludesNonProductSalesRows` | 1 |
| AC-05 | Company totals equal the decimal sum of valid positive, zero, and negative persisted `SalesAmount` values. | `Totals_RetainPositiveZeroAndNegativeSales` | 1 |
| AC-06 | Sales change is current minus comparison, with null percentage and `ZeroCompanyRevenueChange` for a zero comparison total. | `Change_UsesZeroSafePercentage` | 1 |
| AC-07 | Same valid product code with compatible unit, or normalized title plus compatible unit without code, aggregates only within its company and period. | `Matching_AggregatesOnlySafeKeys` | 1 |
| AC-08 | Ambiguous identity or incompatible unit preserves rows, emits warning/reason codes, and suppresses quantity/rate decomposition without losing valid sales contribution. | `Matching_AmbiguitySuppressesDecomposition` | 1 |
| AC-09 | A continuing valid product exposes both-period production quantity, sales quantity, rate, sales value, and changes. | `ContinuingProduct_EmitsPeriodValuesAndChanges` | 1 |
| AC-10 | Valid quantity effect plus price effect plus residual reconciles to product sales change within documented decimal tolerance. | `Effects_ReconcileToProductSalesChange` | 1 |
| AC-11 | A current-only product is `New` with current sales change and no invented base effects. | `CurrentOnlyProduct_IsNew` | 1 |
| AC-12 | A comparison-only product is `Discontinued` with negative comparison sales change and no invented current effects. | `ComparisonOnlyProduct_IsDiscontinued` | 1 |
| AC-13 | Missing rate, invalid quantity, invalid amount, zero-rate edge cases, and null inputs deterministically suppress affected effects while retaining valid totals. | `InvalidInputs_PartiallySuppressEffects` | 1 |
| AC-14 | Production-sales quantity difference is emitted only for compatible units and labeled inferred, never inventory. | `ProductionSalesDifference_IsUnitSafeAndInferred` | 1 |
| AC-15 | Largest positive and negative product changes use deterministic tie-breakers and retain negative contributors. | `LargestChanges_UseDeterministicOrdering` | 1 |
| AC-16 | Driver classification uses the fixed 60% quantity/price threshold and `Mixed`/`Unclassified` zero behavior. | `DriverClassification_UsesSixtyPercentRule` | 1 |
| AC-17 | The typed response preserves nulls, states, warnings, periods, and evidence without model-generated numeric substitutions. | `Response_IsTypedNullAndEvidenceSafe` | 2 |
| AC-18 | Persian variation routes product-comparison requests to this capability, while simple monthly-sales lookup does not. | `SemanticRouting_DistinguishesProductComparison` | 2 |
| AC-19 | Web chat renders available, partial, empty, unavailable, and blocking states with bounded products, warnings, RTL values, and evidence. | `WebChat_RendersAllComparisonStates` | 3 |
| AC-20 | Telegram renders the same typed values and warning meanings compactly and uses the existing safe fallback for unexpected failure. | `Telegram_PreservesTypedValuesAndFallback` | 3 |

## 10. Files and implementation slices

Proposed new files are limited to Feature 129 contracts/use case, a normalized monthly-report read adapter, deterministic matching/calculation support, and focused tests under existing Application, Infrastructure, UnitTests, IntegrationTests, and frontend test directories. Exact new filenames are implementation choices; tasks specify target directories and responsibilities rather than pretending they already exist.

Verified existing files likely to modify are:

- `src/backend/FinancialCopilot.Application/FinancialData/Ingestion/CompanyResolutionContracts.cs`
- `src/backend/FinancialCopilot.Application/FinancialData/Ingestion/MonthlyActivityTrendQueryContracts.cs` for period convention reuse only if required
- `src/backend/FinancialCopilot.Application/AI/Orchestration/ProductRevenueMixIntentRules.cs`
- `src/backend/FinancialCopilot.Application/AI/Orchestration/LlmAiIntentDetector.cs`
- `src/backend/FinancialCopilot.Application/Conversations/ConversationContracts.cs`
- `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs`
- `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Functions/MessagePersistenceFunction.cs`
- `src/backend/FinancialCopilot.Infrastructure/Authentication/TelegramAssistantResponseRenderer.cs`
- `src/frontend/src/lib/chat.functions.ts`
- `src/frontend/src/components/app/message-list.tsx`

The actual normalized source files that must be inspected and used by the new adapter are `FinancialIngestionRows.cs`, `FinancialIngestionConfigurations.cs`, and `FinancialIngestionDbContext.cs`. They are not changed unless dependency registration or an application port requires it. Intentionally unchanged are `CompanyProductRevenueMixCalculator.cs`, `EfCoreProductRevenueMixRepository.cs`, provider clients, migrations, workers, messaging infrastructure, scheduled jobs, trend snapshot persistence, and unrelated monthly features.

### Slice 1 — Read query and deterministic calculation

Define the application query/result and warning contracts; implement company/period resolution; query `MonthlyReports` joined to `MonthlyReportLineItems` with exact ProductSales/OutputType filtering; preserve non-positive sales; normalize and match products conservatively; calculate effects, classifications, rankings, and reconciliation; then add unit and repository integration tests for AC-01 through AC-16.

### Slice 2 — Semantic and conversation integration

Register the capability through existing orchestration; map typed semantic slots; locally validate company, period, product, and focus; invoke the deterministic use case; map the typed result into the existing assistant payload; preserve conversation persistence/replay and clarification behavior; add semantic and conversation contract tests for AC-17 and AC-18.

### Slice 3 — Web and Telegram presentation

Map the typed DTO in web chat; render bounded totals, periods, contributors, product rows, warnings, evidence, and empty/blocking states; preserve RTL Persian numbers and units; add Telegram compact summary/table and fallback behavior; add frontend and renderer tests for AC-19 and AC-20.

## 11. Bounded closure gate

The ten required checks pass: all required fields are available in normalized persistence; ProductSales/OutputType 0 is enforceable by actual property names; negative reported values bypass the positive-only derived calculator; identity and unit ambiguity is conservative; formulas reconcile; null/zero/negative/invalid inputs are deterministic; scope remains read-only and on-demand; all 20 ACs map to named tests and three slices; paths/contracts are verified; and tasks can be written without another product or architecture decision.

Deferred follow-ups are limited to optional observability and future product identity governance. They are not R1 requirements. No general design review is required before decomposition.

APPROVED_FOR_USER_STORY_AND_TASK_DECOMPOSITION
