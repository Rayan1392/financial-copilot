# Feature 080 — Monthly Production & Sales Quality Ranking

## Status
Proposed

## Feature number
080

## Title
رتبه‌بندی هوشمند شرکت‌ها بر اساس کیفیت گزارش تولید و فروش ماهانه

## Business Goal

در حال حاضر دیتای تولید و فروش ماهانه در سیستم موجود است و کاربر می‌تواند مقدار فروش، مبلغ فروش، نرخ فروش، تولید، میانگین ۱۲ ماهه و برخی شاخص‌های مرتبط را برای یک نماد مشاهده کند. ارزش افزوده این فیچر این است که سیستم فقط «عدد» نمایش ندهد، بلکه بتواند در سطح کل بازار/صنعت/Watchlist تشخیص دهد کدام شرکت‌ها گزارش ماهانه باکیفیت‌تر، ضعیف‌تر، یا مشکوک‌تر منتشر کرده‌اند.

این فیچر باید یک ranking قابل توضیح ایجاد کند؛ یعنی رتبه هر نماد فقط بر اساس رشد خام فروش نباشد، بلکه ترکیبی از کیفیت رشد، پایداری، تفکیک نرخ/مقدار، سهم محصول اصلی، مقایسه با میانگین تاریخی، و در صورت وجود مقایسه صنعتی باشد.

## User Story

به عنوان کاربر بازار سرمایه،
می‌خواهم بتوانم از AI بپرسم «بهترین گزارش‌های تولید و فروش این ماه کدامند؟» یا «کدام نمادها گزارش ماهانه باکیفیت داشتند؟»،
تا به جای بررسی دستی ده‌ها گزارش ماهانه، یک رتبه‌بندی قابل استناد و قابل توضیح از نمادهای دارای گزارش قوی/ضعیف دریافت کنم.

## Example User Queries

- بهترین گزارش‌های ماهانه بازار کدامند؟
- بهترین گزارش‌های تولید و فروش اردیبهشت ۱۴۰۵ را بگو
- کدام نمادها در گزارش ماهانه رشد باکیفیت داشتند؟
- گزارش‌های فروش ماهانه ضعیف این ماه کدامند؟
- در صنعت فلزات اساسی کدام شرکت‌ها گزارش ماهانه بهتری داشتند؟
- نمادهایی که فروششان بالاتر از میانگین ۱۲ ماهه بوده و مقدار فروش هم رشد کرده را لیست کن
- ۱۰ گزارش برتر تولید و فروش این ماه را رتبه‌بندی کن
- گزارش ماهانه کچاد نسبت به بقیه صنعت چقدر خوب بوده؟
- سهم‌هایی که رشد فروششان فقط به خاطر نرخ بوده نه مقدار را پیدا کن
- شرکت‌هایی که فروش ماهانه رکورد ۱۲ ماهه زدند را بگو

## Scope

### In scope

1. محاسبه امتیاز کیفیت گزارش ماهانه برای هر شرکت/نماد بر اساس آخرین گزارش موجود یا دوره انتخابی.
2. رتبه‌بندی top/bottom نمادها بر اساس امتیاز کیفیت.
3. امکان فیلتر بر اساس:
   - سال/ماه گزارش شمسی
   - صنعت یا گروه
   - نمادهای خاص یا watchlist
   - حداقل مبلغ فروش
   - فقط شرکت‌هایی که دیتای کافی دارند
4. ارائه خروجی explainable:
   - دلیل رتبه
   - عوامل مثبت
   - عوامل منفی
   - confidence/data coverage
   - منبع داده و تاریخ گزارش
5. پشتیبانی از پرسش طبیعی فارسی توسط AI Agent.
6. API مستقل برای استفاده در UI و Agent.

### Out of scope

1. توصیه خرید/فروش مستقیم.
2. پیش‌بینی قطعی سود آینده.
3. استفاده از قیمت بازار برای امتیاز اصلی، مگر به صورت extension اختیاری در فاز بعد.
4. جایگزینی تحلیل بنیادی کامل.
5. تحلیل متنی کدال با LLM؛ این فیچر فقط بر دیتای ساختاریافته تولید و فروش متکی است.

## Definitions

### Monthly Sales Quality Ranking
رتبه‌بندی نمادها بر اساس کیفیت گزارش ماهانه تولید و فروش.

### Quality Score
امتیاز عددی ۰ تا ۱۰۰ که نشان می‌دهد گزارش ماهانه یک شرکت از نظر کیفیت فروش، پایداری، رشد مقدار، رشد نرخ، ترکیب محصول و مقایسه تاریخی چقدر قوی است.

### Growth Quality
کیفیت رشد فروش، با تاکید بر اینکه رشد از مقدار فروش، نرخ فروش، محصول اصلی و پایداری چندماهه آمده یا صرفاً یک جهش ظاهری/ناپایدار است.

### Explainability
هر امتیاز باید همراه با توضیح محاسباتی و human-readable باشد، نه یک عدد سیاه‌جعبه.

## Ranking Dimensions

امتیاز نهایی باید حداقل از این ابعاد تشکیل شود. وزن‌ها باید در تنظیمات قابل تغییر باشند و در کد hard-code غیرقابل مدیریت نشوند.

| Dimension | Default Weight | Description |
|---|---:|---|
| Sales Growth vs 12M Average | 25 | فروش ماهانه نسبت به میانگین ۱۲ ماهه |
| Quantity Growth Quality | 20 | رشد مقدار فروش/تولید نسبت به دوره مرجع |
| Rate Growth Quality | 15 | رشد نرخ فروش با کنترل اثر غیرعادی |
| Product Mix Strength | 15 | سهم محصول اصلی، افزایش سهم محصول باکیفیت، عدم تمرکز ریسکی شدید |
| Persistence / Trend | 15 | پایداری روند ۳ ماه اخیر، نه فقط جهش یک‌ماهه |
| Industry Relative Strength | 10 | مقایسه با صنعت/گروه در صورت وجود دیتای کافی |

جمع وزن‌ها: ۱۰۰

## Score Interpretation

| Score Range | Label |
|---:|---|
| 85-100 | گزارش بسیار قوی |
| 70-84 | گزارش قوی |
| 55-69 | گزارش متوسط رو به خوب |
| 40-54 | گزارش متوسط/خنثی |
| 25-39 | گزارش ضعیف |
| 0-24 | گزارش بسیار ضعیف یا دیتای ناکافی |

## Required Output Columns

حداقل خروجی ranking باید شامل این ستون‌ها باشد:

- Rank
- Symbol
- CompanyName
- IndustryTitle
- ReportYear
- ReportMonth
- QualityScore
- QualityLabel
- MonthlySalesAmount
- Avg12MonthSalesAmount
- SalesVsAvg12MPercent
- SalesMonthOverMonthPercent
- SalesYearOverYearPercent, if available
- QuantityGrowthSignal
- RateGrowthSignal
- ProductMixSignal
- TrendSignal
- IndustryRelativeSignal
- PositiveDrivers
- NegativeDrivers
- ConfidenceScore
- DataCoverage
- SourceProviderName
- CalculatedAtUtc

## Data Sources

Primary source:
- Monthly production/sales normalized data persisted from Noavaran/Nadpco monthly activity ingestion.
- Existing monthly report aggregate fields if available.
- CompanyProductRevenueMix data for product mix strength if available.

Expected existing concepts/tables may include:
- MonthlyReports
- MonthlyReportLineItems
- CompanyProductRevenueMix
- Companies
- Industries / IndustryGroups
- DerivedMetrics where useful for existing monthly metrics

The implementation must inspect the actual codebase and database model names before coding. Do not invent table names if actual names already exist.

## Data Sufficiency Rules

A company is eligible for ranking only if:

1. It has a valid report for the selected month.
2. Monthly sales amount is not null and greater than zero.
3. At least one comparison baseline is available:
   - ۱۲ ماهه
   - ماه قبل
   - ماه مشابه سال قبل
   - industry peer baseline
4. If less than ۶ months of historical data exists, confidence must be reduced.
5. If product-level line items are missing, product mix score must be marked as unavailable and excluded/reweighted; do not silently score it as zero.

## Score Calculation Rules

### 1. Sales Growth vs 12M Average

Calculate:

`SalesVsAvg12MPercent = (MonthlySalesAmount - Avg12MonthSalesAmount) / Avg12MonthSalesAmount * 100`

Rules:
- If Avg12MonthSalesAmount is null or zero, mark unavailable.
- Cap extreme values to prevent one-off outliers from dominating.
- Suggested cap: -50% to +150%.

### 2. Quantity Growth Quality

Use product-level quantity where available.

Rules:
- Prefer dominant/top products rather than all noisy minor products.
- If quantity increased while sales increased, positive.
- If sales increased but quantity decreased sharply, lower quality unless rate growth explains it.
- For companies with heterogeneous units, aggregate quantity only per product/unit; do not combine incompatible units blindly.

### 3. Rate Growth Quality

Use sales rate per product where available.

Rules:
- Positive if rate growth is moderate and accompanied by stable/increasing quantity.
- Penalize if sales growth is only rate-driven and quantity collapses.
- Ignore zero/invalid rates.
- Detect suspicious rate spikes and reduce confidence.

### 4. Product Mix Strength

Use CompanyProductRevenueMix if available.

Rules:
- Positive if dominant product contribution is stable or improving and total company sales increased.
- Positive if high-share product has growth in value and/or quantity.
- Negative if revenue concentration is too high and declining product diversity creates risk.
- Negative if top product share drops sharply while total sales falls.
- If only aggregate sales exists, mark unavailable.

### 5. Persistence / Trend

Use last ۳ monthly reports.

Rules:
- Positive if sales trend is upward or consistently above 12M average.
- Negative if current month is a one-off spike after weak months.
- Negative if current month is lower than both prior month and 12M average.
- Confidence increases with more complete historical data.

### 6. Industry Relative Strength

Compare with peers in the same industry/group for same report month.

Rules:
- Calculate percentile rank of SalesVsAvg12MPercent within industry.
- Use only industries with minimum peer count, suggested >= 5.
- If peer count is insufficient, mark unavailable and reweight.

## Reweighting Rule

If a dimension is unavailable because of missing data, reweight available dimensions proportionally instead of assigning zero.

Example:
- ProductMix unavailable.
- Total available weights = 85.
- Final score = weighted_sum / 85 * 100.

## Confidence Score

Confidence should be independent of QualityScore.

Suggested confidence factors:

- Historical months available
- Product line item completeness
- Availability of avg 12M
- Availability of MoM/YoY
- Industry peer count
- Data source freshness
- Presence of suspicious outliers

Confidence labels:
- High: >= 80
- Medium: 60-79
- Low: 40-59
- Very low: < 40

## AI Response Policy

The AI must not present ranking as investment advice. Use language like:

- «از نظر کیفیت گزارش تولید و فروش»
- «بر اساس داده‌های موجود»
- «این رتبه‌بندی توصیه خرید/فروش نیست»
- «برای تصمیم‌گیری باید valuation، سودآوری، وضعیت صنعت و قیمت سهم هم بررسی شود»

## Example Response

User:
«۱۰ گزارش ماهانه برتر بازار را بگو»

AI:
«بر اساس آخرین گزارش‌های تولید و فروش موجود، ۱۰ نماد زیر از نظر کیفیت گزارش ماهانه رتبه بالاتری دارند. این رتبه‌بندی توصیه خرید/فروش نیست و فقط کیفیت داده‌های تولید و فروش را ارزیابی می‌کند.»

| رتبه | نماد | شرکت | امتیاز کیفیت | برچسب | دلیل اصلی |
|---:|---|---|---:|---|---|
| 1 | فولاژ | فولاد آلیاژی ایران | 91 | بسیار قوی | فروش بالاتر از میانگین ۱۲ ماهه، رشد مقدار، نرخ پایدار |
| 2 | کچاد | معدنی و صنعتی چادرملو | 84 | قوی | رشد فروش و بهبود سهم محصول اصلی |
| 3 | کگل | معدنی و صنعتی گل‌گهر | 79 | قوی | فروش بالاتر از متوسط و روند ۳ ماهه مثبت |

Then include:
- ۳ نکته مثبت کل بازار
- ۳ هشدار/ریسک
- explanation about confidence and missing data

## API Contract — Query

Endpoint suggestion:

`GET /api/ai/monthly-sales-quality-rankings`

or application use case:

`MonthlySalesQualityRankingQuery`

Parameters:

```json
{
  "reportYear": 1405,
  "reportMonth": 2,
  "industryId": null,
  "industryGroupId": null,
  "symbols": ["کچاد", "کگل"],
  "scope": "Market",
  "direction": "Top",
  "limit": 10,
  "minimumSalesAmount": 0,
  "includeExplanation": true,
  "includeDimensionScores": true,
  "onlyEligibleRows": true
}
```

## API Contract — Response

```json
{
  "reportYear": 1405,
  "reportMonth": 2,
  "scope": "Market",
  "direction": "Top",
  "totalEligibleCompanies": 342,
  "generatedAtUtc": "2026-06-27T00:00:00Z",
  "items": [
    {
      "rank": 1,
      "symbol": "کچاد",
      "companyName": "معدنی و صنعتی چادرملو",
      "industryTitle": "استخراج کانه‌های فلزی",
      "qualityScore": 84.3,
      "qualityLabel": "گزارش قوی",
      "confidenceScore": 88,
      "monthlySalesAmount": 90879722,
      "avg12MonthSalesAmount": 57549287,
      "salesVsAvg12MPercent": 57.9,
      "salesMonthOverMonthPercent": 12.4,
      "salesYearOverYearPercent": null,
      "dimensionScores": {
        "salesGrowthVs12M": 91,
        "quantityGrowthQuality": 74,
        "rateGrowthQuality": 68,
        "productMixStrength": 85,
        "persistenceTrend": 79,
        "industryRelativeStrength": 82
      },
      "positiveDrivers": [
        "فروش ماهانه بالاتر از میانگین ۱۲ ماهه است",
        "روند ۳ ماهه فروش مثبت است"
      ],
      "negativeDrivers": [
        "بخشی از رشد ناشی از افزایش نرخ است"
      ],
      "dataCoverage": {
        "historyMonths": 12,
        "hasProductLineItems": true,
        "hasProductMix": true,
        "industryPeerCount": 18
      },
      "sourceProviderName": "NoavaranCurrentApi",
      "calculatedAtUtc": "2026-06-27T00:00:00Z"
    }
  ]
}
```

## Persistence Recommendation

Create persisted snapshot table to avoid expensive recalculation on every query.

Suggested table/entity:

`MonthlySalesQualityRankingSnapshot`

Fields:
- Id
- ExternalCompanyId
- CompanySymbol
- CompanyName
- IndustryId
- IndustryTitle
- ReportYear
- ReportMonth
- MonthlySalesAmount
- Avg12MonthSalesAmount
- SalesVsAvg12MPercent
- SalesMonthOverMonthPercent
- SalesYearOverYearPercent
- QualityScore
- QualityLabel
- ConfidenceScore
- RankMarket
- RankIndustry
- DimensionScoresJson
- PositiveDriversJson
- NegativeDriversJson
- DataCoverageJson
- SourceProviderName
- CalculatedAtUtc

Indexes:
- `(ReportYear, ReportMonth, RankMarket)`
- `(ReportYear, ReportMonth, IndustryId, RankIndustry)`
- `(ExternalCompanyId, ReportYear, ReportMonth)`
- `(CompanySymbol, ReportYear, ReportMonth)`

Uniqueness:
- `(ExternalCompanyId, ReportYear, ReportMonth)`

## Acceptance Criteria

1. Given valid monthly production/sales data exists for a report month, when ranking is requested, then the system returns top/bottom ranked companies with quality score and explanation.
2. Given a company lacks product line items, when ranking is calculated, then product mix dimension is marked unavailable and available dimensions are reweighted.
3. Given a company has sales growth but quantity decline, when scoring is calculated, then quality score must not be inflated solely by sales amount growth.
4. Given industry filter is supplied, when ranking is requested, then rank must be based on eligible companies within that industry.
5. Given no reportYear/reportMonth is supplied, when query runs, then latest available report period must be selected deterministically.
6. Given a user asks in Persian natural language for «بهترین گزارش‌های ماهانه», AI must route to MonthlySalesQualityRanking intent, not generic metric lookup.
7. Given response is generated by AI, it must include a disclaimer that ranking is based on production/sales quality and is not buy/sell advice.
8. Given missing data reduces reliability, confidence score must reflect missing baselines and data coverage.
9. Given extreme outlier values exist, scoring must cap/normalize them and must not produce quality score outside 0..100.
10. Given recalculation is run multiple times for the same period, persisted snapshots must be idempotent/upserted, not duplicated.

## Non-functional Requirements

- Ranking query should be fast enough for UI usage; target p95 under 500ms for persisted snapshots.
- Calculation job may run after monthly ingestion or on-demand admin trigger.
- No LLM call should be required for numeric score calculation.
- LLM may be used only for final natural-language explanation after deterministic data is retrieved.
- All calculations must be deterministic and testable.
- Avoid hallucinated explanations; explanations must be generated from explicit score drivers.

## Implementation Notes for Agent

Before coding:
1. Inspect current models and repositories for MonthlyReports, MonthlyReportLineItems, CompanyProductRevenueMix, Companies, Industries, DerivedMetrics.
2. Reuse existing symbol/company resolution.
3. Reuse existing Persian/Jalali period conventions.
4. Reuse AI orchestration intent detection patterns.
5. Reuse existing response table rendering conventions.
6. Do not bind this feature to market price provider or valuation logic.
7. Keep this feature production/sales-only.

## Suggested File/Code Areas

The agent must inspect the actual repository and adapt names, but likely areas include:

- `FinancialCopilot.Application`
  - use cases
  - contracts
  - AI intent/catalog
- `FinancialCopilot.Infrastructure`
  - EF Core repository
  - calculation service
  - migrations
- `FinancialCopilot.API`
  - endpoint/controller/minimal API
- Tests
  - unit tests for scoring
  - repository tests
  - orchestration/routing tests
  - API contract tests

## Open Questions

1. Should ranking include only Bourse/FaraBourse operating companies, or all companies with monthly production/sales data?
2. Should industries with less than ۵ peers be excluded from industry-relative scoring or just marked low confidence?
3. Should user watchlist scope be implemented now if watchlist infrastructure already exists, or deferred?
4. Should quality score weights be persisted in database/admin settings or kept in configuration for first version?

Recommended MVP decision:
- Use configuration-based weights.
- Support market and industry scopes.
- Defer personal watchlist scope unless existing watchlist infrastructure is ready.
