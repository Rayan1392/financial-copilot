# Snapshot Field Question Matrix - `CompanyMonthlyActivityTrendSnapshots`

## Purpose

This matrix enumerates the persisted fields in `CompanyMonthlyActivityTrendSnapshots` and documents which direct monthly-activity question families can be served from each field in the proposed snapshot-backed lookup path.

## Coverage Legend

- `Direct` means the answer can be returned directly from the persisted field.
- `Derived from same row` means the answer can be computed from fields in the same snapshot row only, without reading raw monthly line items.
- `Metadata only` means the field supports explainability, safety, or period labeling rather than a primary financial value.
- `Not primary query target` means the field should usually not be exposed as a standalone financial answer.

## Field Matrix

| Field | Role | Queryability | Example supported question family | Notes |
|---|---|---|---|---|
| `Id` | Row identifier | Not primary query target | None | Internal persistence key only. |
| `ExternalCompanyId` | Stable company key | Metadata only | None | Used for joins and routing after company resolution. |
| `CompanySymbol` | Display symbol | Metadata only | `آخرین فروش ماهانه خفنر؟` | Used in answer rendering and diagnostics. |
| `CompanyName` | Display company name | Metadata only | `آخرین فروش ماهانه فنرسازی خاور؟` | Used in answer rendering and company disambiguation. |
| `IndustryId` | Industry reference | Metadata only | Future industry-scoped explanations | Not a primary monthly-activity financial answer. |
| `IndustryTitle` | Industry label | Metadata only | Future industry-scoped explanations | Optional display metadata. |
| `CategoryId` | Category reference | Metadata only | Future category-scoped explanations | Optional display metadata. |
| `CategoryTitle` | Category label | Metadata only | Future category-scoped explanations | Optional display metadata. |
| `ReportYear` | Jalali report year | Direct | `آخرین گزارش ماهانه خفنر مربوط به چه سالی است؟` | Used with `ReportMonth` for latest persisted period. |
| `ReportMonth` | Jalali report month number | Direct | `آخرین گزارش ماهانه خفنر مربوط به چه ماهی است؟` | Used with `FiscalMonthNameFa`. |
| `FiscalEndDate` | Fiscal year-end metadata | Metadata only | `سال مالی خفنر چه پایان سالی دارد؟` | Only if product decides this is user-facing. |
| `FiscalYear` | Fiscal year label | Direct | `این داده مربوط به سال مالی چند است؟` | Can support fiscal-year explanations. |
| `FiscalMonthIndex` | Fiscal month ordering | Metadata only | Relative-period selection | Important for ordering, not usually a direct answer. |
| `FiscalMonthNameFa` | Persian fiscal month name | Direct | `آخرین گزارش ماهانه خفنر مربوط به کدام ماه است؟` | Useful for human-readable period answers. |
| `CalendarYear` | Gregorian year | Direct | `این گزارش در سال میلادی چند ثبت شده؟` | Only if Gregorian reporting is product-approved. |
| `CalendarMonth` | Gregorian month | Direct | `این گزارش در ماه میلادی چند است؟` | Same note as above. |
| `MonthlySalesAmount` | Latest month sales amount | Direct | `آخرین فروش ماهانه خفنر؟` | Core direct monthly sales field. |
| `MonthlyProductionQuantity` | Latest month production quantity | Direct | `آخرین تولید ماهانه خفنر؟` | Quantity answer must respect mixed-unit safety notes. |
| `MonthlySalesQuantity` | Latest month sales quantity | Direct | `مقدار فروش ماهانه خفنر؟` | Quantity answer must respect mixed-unit safety notes. |
| `MonthlyAverageSalesRate` | Average sales rate for the month | Direct | `نرخ فروش ماهانه خفنر؟` | Suitable when product exposes monthly rate questions. |
| `HasMixedProductUnits` | Unit-safety flag | Metadata only | `آیا مقدار تولید/فروش خفنر قابل اتکا است؟` | Must block unsafe quantity ratios and possibly quantity totals. |
| `ProductUnitSummary` | Unit summary text | Metadata only | `واحدهای گزارش خفنر چیست؟` | Supports mixed-unit explanations and transparency. |
| `SameMonthPreviousYearSalesAmount` | Same month previous year sales | Direct | `فروش ماه مشابه سال قبل خفنر؟` | Also supports `مدت مشابه` / `دوره مشابه` wording if the registry approves it. |
| `SameMonthPreviousYearProductionQuantity` | Same month previous year production | Direct | `تولید ماه مشابه سال قبل خفنر؟` | Useful for production comparison questions. |
| `SameMonthPreviousYearSalesQuantity` | Same month previous year sales quantity | Direct | `مقدار فروش ماه مشابه سال قبل خفنر؟` | Quantity safety rules still apply. |
| `Average12MonthSalesAmount` | Trailing 12-month average sales | Direct | `متوسط فروش 12 ماهه خفنر؟` | Works with completeness metadata. |
| `Average12MonthPeriodCount` | Number of periods used in the average | Metadata only | `میانگین 12 ماهه با چند دوره محاسبه شده؟` | Important for partial-history explainability. |
| `YtdSalesAmount` | Fiscal-year-to-date sales to current month | Direct | `جمع فروش از ابتدای سال مالی تا کنون خفنر؟` | Snapshot-backed YTD sales answer. |
| `YtdProductionQuantity` | Fiscal-year-to-date production quantity | Direct | `جمع تولید از ابتدای سال مالی تا کنون خفنر؟` | Not in the user’s initial example list, but the field supports it. |
| `YtdSalesQuantity` | Fiscal-year-to-date sales quantity | Direct | `جمع مقدار فروش از ابتدای سال مالی تا کنون خفنر؟` | Quantity safety rules still apply. |
| `YtdPreviousMonthSalesAmount` | Fiscal-year-to-date sales through previous month | Direct | `جمع فروش از ابتدای سال مالی تا ماه گذشته خفنر؟` | Directly matches a requested example. |
| `SalesAmountMomGrowthPercent` | Month-over-month sales growth percent | Direct | `رشد فروش خفنر نسبت به ماه قبل؟` | Should not be the default meaning of a vague growth phrase unless approved in the registry. |
| `SalesAmountYoYGrowthPercent` | Year-over-year sales growth percent | Direct | `میزان رشد فروش خفنر؟` or `رشد فروش خفنر نسبت به سال قبل؟` | Reasonable reviewed default for vague sales-growth wording if product approves. |
| `ProductionQuantityYoYGrowthPercent` | Year-over-year production growth percent | Direct | `میزان رشد تولید خفنر؟` or `رشد تولید خفنر نسبت به سال قبل؟` | No persisted production MoM field currently exists in this table. |
| `SalesQuantityYoYGrowthPercent` | Year-over-year sales quantity growth percent | Direct | `رشد مقدار فروش خفنر؟` | Useful when the user clearly asks about quantity growth rather than sales amount growth. |
| `CurrentMonthOutputType` | Snapshot provenance for current-month facts | Metadata only | `این عدد از کدام نوع گزارش ماهانه آمده؟` | Explainability only. |
| `YtdOutputType` | Snapshot provenance for YTD facts | Metadata only | `فروش تجمیعی از چه outputType ای گرفته شده؟` | Explainability only. |
| `YtdPreviousMonthOutputType` | Snapshot provenance for previous-month YTD facts | Metadata only | `فروش تا ماه قبل از چه outputType ای گرفته شده؟` | Explainability only. |
| `SourceProviderName` | Source provider identity | Metadata only | `منبع این داده چیست؟` | Important for user-facing provenance. |
| `SourceReportId` | Provider report identifier | Metadata only | Future diagnostics | Usually internal. |
| `SourceRawPayloadId` | Raw payload traceability | Metadata only | Future diagnostics | Usually internal. |
| `IsComparablePreviousYearAvailable` | Comparable-period availability | Metadata only | `آیا داده ماه مشابه سال قبل برای خفنر موجود است؟` | Supports safe comparison messaging. |
| `IsAverage12MonthComplete` | Average completeness flag | Metadata only | `میانگین 12 ماهه کامل است؟` | Supports explainability for partial trailing history. |
| `DataCompletenessScore` | Snapshot completeness score | Metadata only | `کیفیت داده این گزارش چقدر است؟` | Better suited to explainability than headline financial answers. |
| `CalculatedAtUtc` | Snapshot calculation timestamp | Metadata only | `این snapshot چه زمانی محاسبه شده؟` | Freshness/provenance support. |

## Safe Same-Row Derived Questions

The following question families are reasonable to support as derived-from-same-row values, provided the registry and binding metadata explicitly approve them.

### 1. Sales-to-production ratio

Suggested interpretation:

- `MonthlySalesQuantity / MonthlyProductionQuantity`

Example:

- `نسبت فروش به تولید خفنر؟`

Safety conditions:

- `HasMixedProductUnits = false`
- both quantity fields are present
- denominator is non-zero

If these conditions are not met, the answer should be a governed not-available response.

### 2. Sales versus 12-month average

Suggested interpretation:

- `MonthlySalesAmount` compared with `Average12MonthSalesAmount`

Example:

- `فروش این ماه خفنر نسبت به میانگین 12 ماهه چقدر بالاتر است؟`

This is a safe same-row percentage or delta calculation when the average exists and is non-zero.

## Useful Question Families Enabled by the Table

### Direct financial value questions

- latest monthly sales
- latest monthly production quantity
- latest monthly sales quantity
- monthly average sales rate
- same-month previous-year sales
- same-month previous-year production quantity
- same-month previous-year sales quantity
- 12-month average sales
- YTD sales
- YTD production quantity
- YTD sales quantity
- YTD sales through previous month
- sales growth MoM
- sales growth YoY
- production growth YoY
- sales quantity growth YoY

### Period and provenance questions

- latest reported Jalali month and year
- latest fiscal year
- fiscal month name
- company symbol/company name
- Gregorian month/year if exposed
- source provider name
- calculation timestamp
- output type provenance for current month, YTD, and previous-month YTD

### Data-quality questions

- is the previous-year comparable available
- is the 12-month average complete
- how many periods were used for the average
- are product units mixed
- what units were present
- what is the snapshot completeness score

## Explicit Gaps / Unsupported Without Further Schema or Policy

The following question families are not directly supported by the current table shape without adding fields, additional policy, or extra persisted projections.

- production month-over-month growth
- sales quantity month-over-month growth
- YTD previous-month production quantity
- YTD previous-month sales quantity
- product-level production/sales questions
- multi-product contribution analysis
- any answer that requires reading raw monthly line items at request time

## Recommended Registry Defaults

The following broad phrases should not be silently hard-coded in code. If the product wants them to resolve automatically, they should be approved through the database-backed registry:

- `میزان رشد فروش`
  - recommended reviewed default: `SalesAmountYoYGrowthPercent`
- `میزان رشد تولید`
  - recommended reviewed default: `ProductionQuantityYoYGrowthPercent`
- `نسبت فروش به تولید`
  - recommended reviewed default: governed same-row ratio metric with mixed-unit safety enforcement

If the product team does not approve these defaults in the registry, the runtime should return clarification rather than making an unsafe guess.
