# User Story - Switchable Monthly Activity Snapshot Direct Lookup

## Status
`[x]` Implemented on `2026-06-25`

## Implementation Notes

The delivered implementation adds a configuration-switched monthly-activity direct lookup path that reads from `CompanyMonthlyActivityTrendSnapshots` while preserving the legacy `DerivedMetrics` path as the default.

Delivered scope:

- Added `MonthlyActivityLookup.DirectLookupSourceMode` with `DerivedMetrics` default and `TrendSnapshot` optional mode.
- Added a switchable `ISymbolMetricLookupService` boundary so only monthly production/sales direct lookups consult the new snapshot-backed path.
- Added a snapshot-backed direct lookup provider for:
  - latest monthly sales
  - same-month previous-year sales
  - YTD sales
  - YTD sales to previous month
  - latest monthly production quantity
  - latest monthly sales quantity
  - latest monthly sales rate
  - monthly sales growth YoY
  - monthly sales growth MoM
  - monthly production growth YoY
  - monthly sales quantity growth YoY
  - monthly sales-to-production ratio
- Extended the existing governed semantic catalog and direct-metric routing registry for the new monthly question family, while keeping the dynamic alias flow intact.

Current implementation choice:

- The runtime switch/binding is strategy-based rather than a new database binding table. The existing semantic registry remains the AI-facing source of truth for metric recognition, and dynamic/admin-approved aliases still flow through the existing registry stack.

## Story

As a TahlilApp-AI operator and end user,

I want monthly production and sales direct-lookup questions to be answerable from `CompanyMonthlyActivityTrendSnapshots` through a new optional runtime path,

so that the AI can answer a broader monthly-activity question family from one persisted snapshot table while preserving the current `DerivedMetrics`-based path unchanged and switchable through configuration.

## Business Context

The product currently has two relevant monthly-activity capabilities:

- Direct monthly-sales lookup, primarily served from `DerivedMetrics` through the existing symbol metric lookup path.
- Trend/chart questions, served from `CompanyMonthlyActivityTrendSnapshots` through the dedicated monthly activity trend use case from specs `076` and `077`.

This leaves a functional gap:

- `CompanyMonthlyActivityTrendSnapshots` already stores rich company-month monthly-activity facts.
- The current direct monthly lookup path does not use that table.
- Several monthly production/sales questions are therefore either unsupported, partially supported, or answered through a path that was designed for a narrower metric family.

The goal of this feature is to add a **new direct lookup path** for the monthly production/sales family that reads from `CompanyMonthlyActivityTrendSnapshots`, while keeping the legacy path intact and switchable via `appsettings.json`.

Example question family to support from the new path:

- `آخرین فروش ماهانه خفنر؟`
- `جمع فروش از ابتدای سال مالی تا کنون خفنر؟`
- `جمع فروش از ابتدای سال مالی تا ماه گذشته خفنر؟`
- `فروش ماه مشابه دوره قبل خفنر`
- `فروش ماه مشابه سال قبل خفنر`
- `آخرین تولید ماهانه خفنر`
- `میزان رشد فروش خفنر`
- `میزان رشد تولید خفنر`
- `نسبت فروش به تولید خفنر`

Additional question families already supported by the persisted table shape and therefore in scope for this feature design:

- `مقدار فروش ماهانه خفنر؟`
- `نرخ فروش ماهانه خفنر؟`
- `تولید ماه مشابه سال قبل خفنر؟`
- `مقدار فروش ماه مشابه سال قبل خفنر؟`
- `جمع تولید از ابتدای سال مالی تا کنون خفنر؟`
- `جمع مقدار فروش از ابتدای سال مالی تا کنون خفنر؟`
- `رشد فروش خفنر نسبت به ماه قبل؟`
- `رشد مقدار فروش خفنر؟`
- `آخرین گزارش ماهانه خفنر مربوط به چه ماه و سالی است؟`
- `منبع این داده چیست؟`
- `آیا داده ماه مشابه سال قبل برای خفنر موجود است؟`
- `میانگین 12 ماهه کامل است؟`
- `واحدهای گزارش خفنر چیست؟`

## Problem Statement

The current direct monthly lookup path and the current trend/chart path are both valid, but they serve different shapes:

1. The current direct lookup path
   - is stable and already in production behavior,
   - must remain untouched for backward compatibility,
   - currently resolves monthly sales primarily from `DerivedMetrics`.

2. The snapshot table path
   - already contains richer monthly-activity context for one company-month,
   - already powers trend/chart answers,
   - can answer additional direct questions without reading raw line items at request time.

The missing capability is a **switchable monthly-activity direct lookup strategy**:

- keep the old path as-is,
- add a new path backed by `CompanyMonthlyActivityTrendSnapshots`,
- choose the active path with configuration,
- ensure AI-detected monthly-activity metric phrases are **not hard-coded in code**,
- reuse the existing database-backed metric and alias registry pattern from specs `072` and `074`.

## Goals

- Preserve the existing monthly-sales direct lookup path exactly as it is today.
- Add a new direct lookup path backed by `CompanyMonthlyActivityTrendSnapshots`.
- Add an `appsettings.json` switch to select which path answers monthly production/sales direct-lookup questions.
- Make the switch apply only to the monthly production/sales direct-lookup family, not to unrelated intents.
- Reuse the database-backed registry pattern for AI-recognized financial metric phrases.
- Avoid hard-coded Persian or English AI metric phrases in orchestration, parser, or provider code for this new capability.
- Support both persisted snapshot fields and safe same-row derived values.
- Keep trend/chart questions on the existing spec `077` path unless a query is a direct metric question.

## Non-Goals

- Do not change the current `DerivedMetrics` monthly-sales lookup implementation in this feature.
- Do not remove the current monthly trend/chart path.
- Do not replace raw monthly-activity ingestion, snapshot calculation, or backfill logic from specs `076` and `077`.
- Do not introduce live monthly-activity API calls at query time.
- Do not let the LLM invent metric mappings or alias phrases outside the governed registry.
- Do not hard-code a second isolated Persian phrase list for the new path.
- Do not redesign frontend chart behavior in this feature.

## Existing Architectural Constraints

### 1. Existing path must stay intact

The current direct monthly-sales lookup path must remain available and unchanged when the new mode is disabled.

### 2. Registry-backed metric recognition must be reused

The repo already contains a database-backed semantic registry pattern:

- `FinancialMetricDefinitions`
- `MetricAliases`
- `MetricPeriodAliases`
- `DynamicMetricAliases`
- `MetricAliasCandidates`

This pattern already exists so that AI-recognized financial concepts do not depend on scattered hard-coded phrase lists.

This feature must extend that pattern rather than bypass it.

### 3. Snapshot table is authoritative for the new path

The new path must read from:

- `CompanyMonthlyActivityTrendSnapshots`

and may also use:

- the existing company resolver / companies-first resolution path

It must not read:

- raw `MonthlyReports`
- raw `MonthlyReportLineItems`
- live Noavaran APIs

at request time.

## Configuration Requirement

Add a dedicated configuration section in `appsettings.json`.

Suggested section:

```json
"MonthlyActivityLookup": {
  "DirectLookupSourceMode": "DerivedMetrics"
}
```

Suggested values:

- `DerivedMetrics`
- `TrendSnapshot`

Required behavior:

- `DerivedMetrics` keeps today’s behavior unchanged.
- `TrendSnapshot` routes monthly production/sales direct-lookup questions to the new snapshot-backed path.
- Only the monthly production/sales direct-lookup family is affected by this switch.
- Trend/chart questions from spec `077` continue to use the existing trend use case.

## Routing Scope

The switch applies to direct monthly-activity lookup questions such as:

- latest monthly sales
- fiscal-year-to-date monthly sales
- fiscal-year-to-date through previous month monthly sales
- same-month previous-year sales
- latest monthly production quantity
- monthly sales growth
- monthly production growth
- monthly sales quantity growth
- monthly sales quantity
- monthly average sales rate
- sales-to-production ratio
- report period / latest month identification questions
- data quality / mixed-unit / completeness questions

The switch must not affect:

- monthly trend/chart questions already handled by spec `077`
- quarterly `REVENUE` / statement questions
- product revenue mix questions from spec `075`
- market quote, PE, PS, EPS, and other non-monthly-activity direct lookups

## Semantic Registry Requirement

### Core requirement

All AI-recognized monthly-activity concepts for the new path must come from the governed registry, not from hard-coded phrase lists.

### Required registry reuse

The implementation must reuse or extend:

- `FinancialMetricDefinitions`
- `MetricAliases`
- `MetricPeriodAliases`
- `DynamicMetricAliases`
- `MetricAliasCandidates`

### Required design extension

Because `CompanyMonthlyActivityTrendSnapshots` exposes values that are not all direct `DerivedMetrics` rows, the spec requires a **data-binding layer** that maps a registry metric to its data source.

Recommended approach:

- Add a new database-backed binding table such as `MetricQueryBindings`

Suggested binding fields:

- `MetricCode`
- `MetricVersion`
- `LookupSourceKind`
- `SourceEntityName`
- `SourceFieldName`
- `SecondarySourceFieldName`
- `ValueComputationPolicy`
- `UnitOverride`
- `RequiresComparableAvailability`
- `RequiresAverageCompleteness`
- `DisallowWhenMixedUnits`
- `IsActive`
- `Priority`

Suggested `LookupSourceKind` values:

- `DerivedMetrics`
- `CompanyMonthlyActivityTrendSnapshot`

Suggested `ValueComputationPolicy` values:

- `DirectField`
- `SameRowRatio`
- `SameRowDelta`
- `SameRowPercent`
- `PeriodLabel`
- `AvailabilityOnly`

This lets the same semantic metric registry remain the AI-facing source of truth, while the runtime lookup strategy resolves data from the proper persistence model.

## Supported Snapshot-Backed Concept Families

The new path should support at least the following concept families.

### Existing semantic metrics that can be rebound to snapshot fields

- `MONTHLY_SALES`
- `MONTHLY_SALES_YTD`
- `MONTHLY_SALES_YTD_PREVIOUS_MONTH`
- `AVG_12M_MONTHLY_SALES`
- `MONTHLY_PRODUCTION_QUANTITY`
- `MONTHLY_SALES_QUANTITY`
- `MONTHLY_SALES_RATE`
- `MONTHLY_SALES_GROWTH_YOY`
- `MONTHLY_SALES_GROWTH_MOM`

### Additional monthly-activity concepts that likely need new registry entries

- monthly production growth YoY
- monthly sales quantity growth YoY
- monthly sales-to-production ratio
- latest monthly activity report period
- snapshot data completeness / comparability status
- mixed-unit safety / product-unit summary

## Snapshot Table Coverage Summary

The exact table schema already supports these answer categories without reading raw monthly line items at request time:

### Direct monthly monetary values

- latest monthly sales amount
- same-month previous-year sales amount
- average 12-month sales amount
- fiscal-year-to-date sales amount
- fiscal-year-to-date sales amount through previous month

### Direct monthly quantity values

- latest monthly production quantity
- latest monthly sales quantity
- same-month previous-year production quantity
- same-month previous-year sales quantity
- fiscal-year-to-date production quantity
- fiscal-year-to-date sales quantity

### Direct monthly rate/growth values

- monthly average sales rate
- sales amount month-over-month growth percent
- sales amount year-over-year growth percent
- production quantity year-over-year growth percent
- sales quantity year-over-year growth percent

### Period and identity answers

- latest Jalali report year and month
- fiscal year and fiscal month index/name
- Gregorian calendar year and month
- company symbol and company name
- provider/source identity
- calculation timestamp

### Data quality / explainability answers

- whether the row contains mixed product units
- product unit summary text
- whether same-month previous-year comparison is available
- whether the 12-month average is complete
- how many periods were used in the 12-month average
- overall data completeness score
- output-type provenance for current month, YTD, and previous-month YTD

## Question Interpretation Policy

The new path must keep the same safety principle established in the semantic registry specs:

- explicit phrases should resolve deterministically,
- longest-match precedence must be respected,
- ambiguous broad phrases must not silently resolve to unsafe meanings unless a reviewed registry default exists.

Examples:

- `جمع فروش از ابتدای سال مالی تا کنون` -> current-month YTD sales
- `جمع فروش از ابتدای سال مالی تا ماه گذشته` -> previous-month YTD sales
- `فروش ماه مشابه سال قبل` -> same-month previous-year sales
- `میزان رشد فروش` -> may default to reviewed YoY sales growth only if the approved registry definition says so
- `میزان رشد تولید` -> may default to reviewed YoY production growth only if the approved registry definition says so
- `نسبت فروش به تولید` -> must clearly define whether this means sales quantity / production quantity and must only answer when units are safe

## Ratio and Safe Derived Value Policy

The new path may compute small deterministic values from a **single persisted snapshot row** when that derivation is explicitly governed by metadata.

Example:

- `sales-to-production ratio`

may be computed as:

- `MonthlySalesQuantity / MonthlyProductionQuantity`

only when:

- `HasMixedProductUnits = false`
- both values are present
- denominator is non-zero
- the metric binding explicitly allows this derivation

If the row has mixed units or missing quantities, the system must return a governed not-available response rather than inventing a ratio.

## Snapshot Field Coverage

This feature should explicitly support the snapshot fields documented in:

- `CompanyMonthlyActivityTrendSnapshots`

and the detailed mapping matrix stored alongside this spec:

- `snapshot-field-question-matrix.md`

That matrix is part of the feature scope, not optional documentation.

## Acceptance Criteria

### Configuration and backward compatibility

1. An `appsettings.json` key exists to select the direct monthly-activity lookup source mode.
2. The default mode preserves the existing legacy path unchanged.
3. Enabling snapshot mode affects only the monthly production/sales direct-lookup family.
4. Quarterly, product-mix, valuation, quote, and trend/chart routes remain unchanged.

### New snapshot-backed direct lookup path

1. A dedicated snapshot-backed lookup provider/use case exists for monthly production/sales direct questions.
2. It reads from `CompanyMonthlyActivityTrendSnapshots` plus company resolution only.
3. It does not read raw line-item tables at query time.
4. It supports the question families defined in this story and in the field matrix.

### Registry-driven AI semantics

1. Supported monthly-activity direct question concepts are stored in the database-backed registry.
2. New snapshot-backed concepts are not introduced through hard-coded Persian or English phrase lists.
3. Period phrases are resolved through `MetricPeriodAliases` or an equivalent governed mechanism.
4. Runtime alias learning and admin-approved dynamic aliases remain compatible.

### Data-binding governance

1. Metric-to-source-field mapping is governed through reviewed metadata, not embedded switch statements for financial concepts.
2. The implementation can distinguish metrics served from `DerivedMetrics` versus `CompanyMonthlyActivityTrendSnapshots`.
3. Same-row derived values such as ratios require explicit governed computation metadata.

### Functional coverage

1. `آخرین فروش ماهانه خفنر؟` can be answered from `MonthlySalesAmount`.
2. `جمع فروش از ابتدای سال مالی تا کنون خفنر؟` can be answered from `YtdSalesAmount`.
3. `جمع فروش از ابتدای سال مالی تا ماه گذشته خفنر؟` can be answered from `YtdPreviousMonthSalesAmount`.
4. `فروش ماه مشابه سال قبل خفنر؟` can be answered from `SameMonthPreviousYearSalesAmount`.
5. `آخرین تولید ماهانه خفنر؟` can be answered from `MonthlyProductionQuantity`.
6. `میزان رشد فروش خفنر؟` can be answered through the reviewed default growth metric or return a governed clarification if no default is approved.
7. `میزان رشد تولید خفنر؟` can be answered through the reviewed default production growth metric or return a governed clarification if no default is approved.
8. `نسبت فروش به تولید خفنر؟` can be answered only when the snapshot row makes the ratio safe and the metric binding explicitly allows it.
9. `مقدار فروش ماهانه خفنر؟` can be answered from `MonthlySalesQuantity`.
10. `نرخ فروش ماهانه خفنر؟` can be answered from `MonthlyAverageSalesRate`.
11. `تولید ماه مشابه سال قبل خفنر؟` can be answered from `SameMonthPreviousYearProductionQuantity`.
12. `جمع تولید از ابتدای سال مالی تا کنون خفنر؟` can be answered from `YtdProductionQuantity`.
13. `جمع مقدار فروش از ابتدای سال مالی تا کنون خفنر؟` can be answered from `YtdSalesQuantity`.
14. `رشد فروش خفنر نسبت به ماه قبل؟` can be answered from `SalesAmountMomGrowthPercent`.
15. `رشد مقدار فروش خفنر؟` can be answered from `SalesQuantityYoYGrowthPercent` when the approved registry phrasing selects quantity growth explicitly.
16. `آخرین گزارش ماهانه خفنر مربوط به چه ماه و سالی است؟` can be answered from `ReportYear`, `ReportMonth`, and `FiscalMonthNameFa`.
17. `منبع این داده چیست؟` can be answered from `SourceProviderName`.
18. `آیا داده ماه مشابه سال قبل برای خفنر موجود است؟` can be answered from `IsComparablePreviousYearAvailable`.
19. `میانگین 12 ماهه کامل است؟` can be answered from `IsAverage12MonthComplete`.
20. `واحدهای گزارش خفنر چیست؟` can be answered from `ProductUnitSummary` together with `HasMixedProductUnits`.

## Out of Scope

- Replacing the trend/chart use case from spec `077`
- Recomputing snapshots from raw monthly activity data at request time
- Product-level production/sales lookup from snapshot rows
- Frontend chart redesign
- Automatic live fallback to raw provider APIs

## Dependencies

- Spec `072` - Centralize Financial Metric Alias and Intent Routing Registry
- Spec `074` - Database-Backed Metric Definition and Alias Registry
- Spec `076` - NADPCO Monthly Activity Trend Snapshot
- Spec `077` - AI Monthly Production and Sales Trend Query
- Existing company resolver and companies-first symbol resolution

## Priority

**High.** The snapshot table already contains information that can answer a wider monthly production/sales question family. This feature unlocks that value without breaking the current path and without regressing the semantic-governance architecture.
