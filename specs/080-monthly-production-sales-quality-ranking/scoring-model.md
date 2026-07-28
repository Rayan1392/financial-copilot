# Feature 080 — Scoring Model

## Purpose

The scoring model measures monthly production/sales report quality. It is not a valuation model and not an investment recommendation model.

## Final Score

`QualityScore = WeightedAverage(AvailableDimensionScores)`

Default dimensions:

| Dimension | Weight |
|---|---:|
| Sales Growth vs 12M Average | 25 |
| Quantity Growth Quality | 20 |
| Rate Growth Quality | 15 |
| Product Mix Strength | 15 |
| Persistence / Trend | 15 |
| Industry Relative Strength | 10 |

If a dimension is unavailable, remove its weight and reweight remaining dimensions proportionally.

## Score Range

All dimension scores and final score must be clamped:

`0 <= score <= 100`

## Dimension 1 — Sales Growth vs 12M Average

Input:
- MonthlySalesAmount
- Avg12MonthSalesAmount

Formula:
`pct = (MonthlySalesAmount - Avg12MonthSalesAmount) / Avg12MonthSalesAmount * 100`

Suggested mapping:
- <= -50% => 0
- -25% => 25
- 0% => 50
- +50% => 80
- >= +100% => 100

Cap input at -50% and +150%.

## Dimension 2 — Quantity Growth Quality

Input:
- Product sales quantities
- Product production quantities
- Previous period quantities if available

Rules:
- Prefer top/dominant products.
- Do not aggregate incompatible product units.
- Sales growth with quantity growth = strong positive.
- Sales growth with quantity collapse = penalty.
- Quantity unavailable = dimension unavailable.

Suggested signals:
- StrongPositive
- Positive
- Neutral
- Negative
- StrongNegative
- Unavailable

## Dimension 3 — Rate Growth Quality

Input:
- Product sales rates
- Previous period rates if available
- Product sales quantities

Rules:
- Moderate rate growth with stable/increasing quantity = positive.
- Rate-only growth with falling quantity = weak quality.
- Suspicious extreme rate spikes reduce confidence.
- Zero/invalid rates are ignored.

## Dimension 4 — Product Mix Strength

Input:
- CompanyProductRevenueMix rows
- Top product share
- Dominant product flag
- Product rank changes if history available

Rules:
- Strong if top products drive growth and total sales improves.
- Risk if one product dominates too heavily and sales trend weakens.
- Negative if top product share drops sharply with falling total sales.
- Unavailable if product mix data missing.

## Dimension 5 — Persistence / Trend

Input:
- Last 3 monthly sales amounts
- 12M average

Rules:
- Upward 3-month trend = positive.
- Consistently above 12M average = positive.
- One-month spike after weak months = partial penalty.
- Current month below previous month and below 12M average = negative.

## Dimension 6 — Industry Relative Strength

Input:
- SalesVsAvg12MPercent for companies in same industry/group

Rules:
- Requires minimum peer count, suggested >= 5.
- Use percentile rank.
- Top quartile = positive.
- Bottom quartile = negative.
- Unavailable if peer count insufficient.

## Confidence Score

Confidence is not quality.

Suggested factors:

| Factor | Impact |
|---|---:|
| >=12 months history | +20 |
| 6-11 months history | +10 |
| product line items available | +20 |
| product mix available | +15 |
| MoM baseline available | +10 |
| YoY baseline available | +10 |
| industry peer count >=5 | +10 |
| no suspicious outlier | +5 |

Clamp 0..100.

## Driver Generation

Drivers must be deterministic.

Examples:
- «فروش ماهانه بالاتر از میانگین ۱۲ ماهه است»
- «رشد فروش با رشد مقدار همراه بوده است»
- «بخش مهمی از رشد فروش ناشی از افزایش نرخ است»
- «داده محصول برای تحلیل ترکیب فروش کامل نیست»
- «شرکت در چارک بالای صنعت قرار دارد»
- «روند ۳ ماهه فروش صعودی است»

## Labels

| Range | Persian Label |
|---:|---|
| 85-100 | گزارش بسیار قوی |
| 70-84 | گزارش قوی |
| 55-69 | گزارش متوسط رو به خوب |
| 40-54 | گزارش متوسط/خنثی |
| 25-39 | گزارش ضعیف |
| 0-24 | گزارش بسیار ضعیف یا دیتای ناکافی |
