# Tasks - Switchable Monthly Activity Snapshot Direct Lookup

## Status

`[x]` Implemented on `2026-06-25`

Implemented scope summary:

- Added `MonthlyActivityLookup.DirectLookupSourceMode` with legacy-compatible default.
- Added a switchable lookup boundary that preserves the legacy path and optionally routes monthly production/sales direct lookups to snapshot-backed reads.
- Added snapshot-backed lookup coverage for the approved core monthly question family.
- Extended the semantic catalog and direct metric routing for the new monthly metrics.
- Added focused unit coverage for the config switch, parser/routing resolution, and snapshot-backed lookups.

## Implementation Tasks

### 1. Baseline audit

- [ ] Review the existing legacy monthly-sales direct lookup path end-to-end.
- [ ] Review the monthly trend/chart path from spec `077`.
- [ ] Review the snapshot calculation and repository contracts from spec `076`.
- [ ] Review the current database-backed semantic registry from specs `072` and `074`.
- [ ] Confirm which monthly-activity metric phrases are currently hard-coded and where.
- [ ] Document the current route split between monthly direct lookup and monthly trend/chart.

### 2. Define configuration switch

- [ ] Add an options contract for a new `MonthlyActivityLookup` configuration section.
- [ ] Add a `DirectLookupSourceMode` enum with at least:
  - [ ] `DerivedMetrics`
  - [ ] `TrendSnapshot`
- [ ] Define the default mode as legacy-compatible.
- [ ] Document the exact `appsettings.json` shape.
- [ ] Ensure the switch scope is limited to monthly production/sales direct lookups.

### 3. Define strategy boundary

- [ ] Introduce a strategy abstraction for monthly-activity direct lookup.
- [ ] Keep the current `DerivedMetrics`-backed implementation unchanged behind the legacy strategy.
- [ ] Add a new snapshot-backed strategy interface/contract.
- [ ] Add a strategy selector that reads `MonthlyActivityLookup.DirectLookupSourceMode`.
- [ ] Ensure unrelated intents do not consult this selector.

### 4. Define registry-backed source binding model

- [ ] Add a new metadata model for mapping queryable metric concepts to runtime data sources.
- [ ] Prefer a database-backed table such as `MetricQueryBindings` instead of code-only field maps.
- [ ] Support bindings for:
  - [ ] `DerivedMetrics`
  - [ ] `CompanyMonthlyActivityTrendSnapshots`
- [ ] Support direct-field bindings.
- [ ] Support safe same-row derived-value bindings.
- [ ] Support field-level guard conditions such as mixed-unit blocking.
- [ ] Support unit override metadata.

### 5. Define snapshot-backed metric coverage

- [ ] Reuse existing metric codes where semantics already match:
  - [ ] `MONTHLY_SALES`
  - [ ] `MONTHLY_SALES_YTD`
  - [ ] `MONTHLY_SALES_YTD_PREVIOUS_MONTH`
  - [ ] `AVG_12M_MONTHLY_SALES`
  - [ ] `MONTHLY_PRODUCTION_QUANTITY`
  - [ ] `MONTHLY_SALES_QUANTITY`
  - [ ] `MONTHLY_SALES_RATE`
  - [ ] `MONTHLY_SALES_GROWTH_YOY`
  - [ ] `MONTHLY_SALES_GROWTH_MOM`
- [ ] Define additional registry entries for snapshot-only concepts when needed:
  - [ ] production growth YoY
  - [ ] sales quantity growth YoY
  - [ ] sales-to-production ratio
  - [ ] latest report period
  - [ ] mixed-unit safety / data quality concepts
  - [ ] source provider / freshness concepts when exposed to end users
  - [ ] output-type provenance concepts when exposed to end users

### 6. Extend semantic registry seed data

- [ ] Seed any missing `FinancialMetricDefinitions` rows required for the new snapshot-backed concepts.
- [ ] Seed `MetricAliases` for approved Persian and English phrases.
- [ ] Seed `MetricPeriodAliases` for monthly period phrases used by this family.
- [ ] Ensure the semantic registry remains the AI-facing source of truth.
- [ ] Do not add monthly-activity financial phrase ownership to orchestration or parser hard-coded lists.

### 7. Snapshot field-to-metric binding seed

- [ ] Seed bindings from `CompanyMonthlyActivityTrendSnapshots` fields to queryable metric codes.
- [ ] Add direct bindings for:
  - [ ] `MonthlySalesAmount`
  - [ ] `MonthlyProductionQuantity`
  - [ ] `MonthlySalesQuantity`
  - [ ] `MonthlyAverageSalesRate`
  - [ ] `SameMonthPreviousYearSalesAmount`
  - [ ] `SameMonthPreviousYearProductionQuantity`
  - [ ] `SameMonthPreviousYearSalesQuantity`
  - [ ] `Average12MonthSalesAmount`
  - [ ] `YtdSalesAmount`
  - [ ] `YtdProductionQuantity`
  - [ ] `YtdSalesQuantity`
  - [ ] `YtdPreviousMonthSalesAmount`
  - [ ] `SalesAmountMomGrowthPercent`
  - [ ] `SalesAmountYoYGrowthPercent`
  - [ ] `ProductionQuantityYoYGrowthPercent`
  - [ ] `SalesQuantityYoYGrowthPercent`
- [ ] Add governed metadata-only bindings for:
  - [ ] `HasMixedProductUnits`
  - [ ] `ProductUnitSummary`
  - [ ] `IsComparablePreviousYearAvailable`
  - [ ] `IsAverage12MonthComplete`
  - [ ] `Average12MonthPeriodCount`
  - [ ] `DataCompletenessScore`
  - [ ] `CalculatedAtUtc`

### 8. Safe same-row derived metrics

- [ ] Define a reviewed computation policy for `sales-to-production ratio`.
- [ ] Specify whether the ratio is:
  - [ ] `MonthlySalesQuantity / MonthlyProductionQuantity`
  - [ ] or another reviewed business formula
- [ ] Block ratio computation when:
  - [ ] `HasMixedProductUnits = true`
  - [ ] either quantity is null
  - [ ] denominator is zero
- [ ] Add deterministic not-available rendering for blocked cases.

### 9. Snapshot-backed direct lookup provider

- [ ] Create a dedicated snapshot-backed direct lookup use case/provider.
- [ ] Resolve the company using the existing companies-first path.
- [ ] Load the latest row ordered by `ReportYear DESC, ReportMonth DESC` when no explicit period is requested.
- [ ] Support period-relative lookups from the snapshot table when registry metadata requests them.
- [ ] Read only the rows required for the resolved metric and period.
- [ ] Never read raw monthly line items at request time.

### 10. Query family coverage

- [ ] Implement latest monthly sales lookup from snapshot.
- [ ] Implement latest monthly production quantity lookup from snapshot.
- [ ] Implement latest monthly sales quantity lookup from snapshot.
- [ ] Implement latest monthly average sales rate lookup from snapshot.
- [ ] Implement YTD sales lookup from snapshot.
- [ ] Implement YTD production quantity lookup from snapshot.
- [ ] Implement YTD sales quantity lookup from snapshot.
- [ ] Implement YTD previous-month sales lookup from snapshot.
- [ ] Implement same-month previous-year sales lookup from snapshot.
- [ ] Implement same-month previous-year production lookup from snapshot when requested.
- [ ] Implement same-month previous-year sales quantity lookup from snapshot when requested.
- [ ] Implement 12-month average sales lookup from snapshot.
- [ ] Implement sales growth lookup according to reviewed registry defaults and explicit period aliases.
- [ ] Implement production growth lookup according to reviewed registry defaults and explicit period aliases.
- [ ] Implement sales quantity growth lookup when requested.
- [ ] Implement sales-to-production ratio with safety guards.
- [ ] Implement mixed-unit safety and product-unit-summary explainability responses.
- [ ] Implement previous-year-comparable availability responses.
- [ ] Implement average-completeness and period-count responses.
- [ ] Implement source-provider and calculation-timestamp responses when product-approved.
- [ ] Implement output-type provenance responses when product-approved.

### 11. Period and date questions

- [ ] Support asking which month/year the latest snapshot belongs to.
- [ ] Support Jalali fiscal month/year wording from persisted snapshot fields.
- [ ] Support Gregorian month/year wording when product requirements expose it.
- [ ] Ensure period answers come from persisted snapshot metadata, not inferred text only.
- [ ] Support report-year/report-month answers from `ReportYear` and `ReportMonth`.
- [ ] Support fiscal-year answers from `FiscalYear`.
- [ ] Support fiscal-month-name answers from `FiscalMonthNameFa`.
- [ ] Support fiscal ordering answers from `FiscalMonthIndex` only if product wording requires it.
- [ ] Support Gregorian year/month answers from `CalendarYear` and `CalendarMonth` only when explicitly exposed.

### 12. Routing integration

- [ ] Update the monthly-activity direct lookup route to call the selected strategy.
- [ ] Keep the current trend/chart intent on the existing `MONTHLY_ACTIVITY_TREND` path.
- [ ] Keep quarterly `REVENUE` and other non-monthly-intent routing unchanged.
- [ ] Preserve product revenue mix routing from spec `075`.
- [ ] Preserve quote-context suppression rules for monthly-activity answers.

### 13. Parser and alias integration

- [ ] Ensure monthly-activity direct metric phrases are resolved through the database-backed semantic registry.
- [ ] Ensure period selector phrases come from `MetricPeriodAliases`.
- [ ] Keep deterministic parser fallbacks structural only where possible.
- [ ] Do not introduce a new independent hard-coded vocabulary owner for this feature.
- [ ] Support dynamic/admin-approved aliases through the existing dynamic alias flow.

### 14. Rendering and explainability

- [ ] Render values using canonical registry titles.
- [ ] Render units according to binding metadata and snapshot semantics.
- [ ] Expose source and freshness metadata where existing API contracts support it.
- [ ] Render not-available explanations for mixed-unit or missing-data cases.
- [ ] Keep market quote columns excluded from this family.

### 15. Tests - configuration switching

- [ ] `DerivedMetrics` mode preserves today’s behavior for monthly sales lookup.
- [ ] `TrendSnapshot` mode routes monthly production/sales direct lookups to the new provider.
- [ ] The switch does not affect trend/chart queries.
- [ ] The switch does not affect product revenue mix, quarterly revenue, or quote lookups.

### 16. Tests - semantic resolution

- [ ] Snapshot-backed monthly activity aliases resolve through the registry tables.
- [ ] Period phrases resolve through `MetricPeriodAliases`.
- [ ] Broad growth phrases follow reviewed defaults only when such defaults are seeded.
- [ ] Unsafe ambiguous phrases return clarification when no reviewed default exists.
- [ ] Dynamic aliases participate without code changes.

### 17. Tests - snapshot field coverage

- [ ] Latest sales question reads `MonthlySalesAmount`.
- [ ] Latest production question reads `MonthlyProductionQuantity`.
- [ ] Latest sales quantity question reads `MonthlySalesQuantity`.
- [ ] Latest monthly sales rate question reads `MonthlyAverageSalesRate`.
- [ ] YTD sales question reads `YtdSalesAmount`.
- [ ] YTD production question reads `YtdProductionQuantity`.
- [ ] YTD sales quantity question reads `YtdSalesQuantity`.
- [ ] YTD previous-month sales question reads `YtdPreviousMonthSalesAmount`.
- [ ] Same-month previous-year sales question reads `SameMonthPreviousYearSalesAmount`.
- [ ] Same-month previous-year production question reads `SameMonthPreviousYearProductionQuantity`.
- [ ] Same-month previous-year sales quantity question reads `SameMonthPreviousYearSalesQuantity`.
- [ ] 12-month average sales question reads `Average12MonthSalesAmount`.
- [ ] Sales growth question can read `SalesAmountYoYGrowthPercent` and `SalesAmountMomGrowthPercent` according to registry intent.
- [ ] Production growth question reads `ProductionQuantityYoYGrowthPercent`.
- [ ] Sales quantity growth question reads `SalesQuantityYoYGrowthPercent`.
- [ ] Latest-report-period question reads `ReportYear`, `ReportMonth`, and `FiscalMonthNameFa`.
- [ ] Source question reads `SourceProviderName`.
- [ ] Comparable-availability question reads `IsComparablePreviousYearAvailable`.
- [ ] Average-completeness question reads `IsAverage12MonthComplete` and `Average12MonthPeriodCount`.
- [ ] Mixed-unit-safety question reads `HasMixedProductUnits` and `ProductUnitSummary`.
- [ ] Freshness/provenance question reads `CalculatedAtUtc`, `CurrentMonthOutputType`, `YtdOutputType`, and `YtdPreviousMonthOutputType` when exposed.

### 18. Tests - safe derived values

- [ ] Sales-to-production ratio is returned when quantities are present and units are safe.
- [ ] Sales-to-production ratio is blocked when units are mixed.
- [ ] Sales-to-production ratio is blocked when quantities are missing.
- [ ] Sales-to-production ratio is blocked when denominator is zero.

### 19. Tests - route isolation

- [ ] `آخرین فروش ماهانه خفنر؟` follows the selected monthly-activity direct lookup mode.
- [ ] `روند فروش ماهانه خفنر` still uses the trend/chart use case.
- [ ] `درآمد فصلی خفنر` still uses quarterly revenue logic.
- [ ] `پرفروش‌ترین محصول خفنر` still uses product revenue mix logic.

### 20. Documentation

- [ ] Document the configuration switch and default behavior.
- [ ] Document the strategy boundary between legacy and snapshot-backed direct lookup.
- [ ] Document the registry-backed binding pattern for snapshot fields.
- [ ] Document which snapshot fields are directly queryable.
- [ ] Document which questions are intentionally unsupported or guarded.
- [ ] Document mixed-unit and ratio safety rules.

## Acceptance Checklist

- [ ] Legacy monthly-sales direct lookup path remains unchanged and selectable.
- [ ] New snapshot-backed direct lookup path exists and is selectable.
- [ ] `appsettings.json` controls which path serves monthly production/sales direct lookups.
- [ ] Monthly-activity AI metric recognition is registry-driven, not hard-coded in a new phrase list.
- [ ] Snapshot-backed lookup reads `CompanyMonthlyActivityTrendSnapshots`, not raw line items.
- [ ] Snapshot-backed lookup can answer the approved question families from this spec.
- [ ] Ratio-style answers are governed and blocked when units are unsafe.
- [ ] Trend/chart routing remains on the existing spec `077` path.
- [ ] Unrelated non-monthly routes remain unchanged.
