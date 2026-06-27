# Monthly Sales Quality Ranking

This feature ranks companies by the quality of their monthly production and sales reports. It is not a valuation model and it is not buy/sell advice.

## Data Sources

- `CompanyMonthlyActivityTrendSnapshots` for company-month sales baselines, 12-month averages, MoM, and YoY context
- `MonthlyReports` and `MonthlyReportLineItems` for normalized monthly production/sales payloads
- `CompanyProductRevenueMix` for product-mix strength
- `Companies`, `Industries`, and `IndustryGroups` for company metadata and peer grouping

## Score Model

The deterministic calculator uses these default weights:

- Sales Growth vs 12M Average: `25`
- Quantity Growth Quality: `20`
- Rate Growth Quality: `15`
- Product Mix Strength: `15`
- Persistence / Trend: `15`
- Industry Relative Strength: `10`

Unavailable dimensions are reweighted out of the denominator instead of being scored as zero.

## Confidence

Confidence is separate from quality score. It increases with:

- deeper history
- product line-item availability
- product-mix availability
- MoM/YoY/12M baselines
- sufficient industry peer count

It decreases when coverage is incomplete or suspicious rate spikes are detected.

## Persistence

Snapshots are stored in `MonthlySalesQualityRankingSnapshots` and keyed by:

- unique: `(ExternalCompanyId, ReportYear, ReportMonth)`
- query indexes for market rank, industry rank, and symbol-period lookup

## API

Query endpoint:

```http
GET /api/ai/v1/monthly-sales-quality-rankings?reportYear=1405&reportMonth=2&direction=Top&limit=10
```

Admin recalculation endpoint:

```http
POST /api/v1/admin/monthly-sales-quality-rankings/recalculate
Content-Type: application/json

{
  "reportYear": 1405,
  "reportMonth": 2
}
```

## AI Queries

Examples supported by deterministic routing:

- `بهترین گزارش‌های ماهانه بازار کدامند؟`
- `۱۰ گزارش برتر تولید و فروش این ماه را بگو`
- `گزارش‌های فروش ضعیف این ماه کدامند؟`
- `در صنعت فلزات اساسی کدام شرکت‌ها گزارش ماهانه بهتری داشتند؟`

## Ingestion Integration

`NadpcoApiMonthlyActivityNormalizer` now recalculates ranking snapshots after single-month (`OutputType=0`) monthly activity normalization succeeds. Ranking recalculation failures are logged and do not roll back the raw monthly ingestion write path.

## Known Limitations

- AI industry filtering is text-based on persisted industry titles; API callers should prefer `industryId` and `industryGroupId`.
- Ranking snapshots are recalculated for affected Jalali periods after monthly ingestion, but there is no separate background queue for ranking yet.
- The current feature focuses on production/sales quality only and intentionally excludes market price and valuation signals from ranking math.
