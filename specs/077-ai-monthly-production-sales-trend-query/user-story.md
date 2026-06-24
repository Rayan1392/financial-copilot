# User Story - AI Monthly Production and Sales Trend Query

## Status
`[x]` Implemented

## Story

As a TahlilApp-AI user,

I want to ask the assistant for a company's production and sales trend and receive a chart-ready comparison of the current fiscal year, previous fiscal year, and trailing 12-month average,

so that I can quickly interpret monthly operational performance without manually exporting and calculating historical production/sales data.

## Business Context

The assistant already supports direct financial metric lookup, monthly sales answers, and product revenue mix. The next step is to let the AI answer trend-oriented production/sales questions such as:

- روند فروش ماهانه کسرا را نشان بده
- نمودار فروش ماهانه کهمدا را با سال قبل مقایسه کن
- فروش امسال کچاد نسبت به پارسال چطور بوده؟
- تولید و فروش کگل در سال جاری نسبت به میانگین ۱۲ ماهه چگونه است؟

The expected chart is similar to a monthly sales comparison chart:

- Previous fiscal-year monthly sales bars.
- Current fiscal-year monthly sales bars beside the same fiscal months.
- A 12-month average reference line.

The AI must not calculate this chart from raw ProductSales rows. It must retrieve a chart-ready response from the derived trend snapshot created by spec 076.

## Acceptance Criteria

### Intent Detection

The AI recognizes production/sales trend intents in Persian, including:

- روند فروش
- روند تولید و فروش
- نمودار فروش ماهانه
- فروش امسال نسبت به سال قبل
- مقایسه فروش سال جاری و سال گذشته
- میانگین ۱۲ ماهه فروش
- گزارش تولید و فروش با نمودار

These intents must route to a dedicated monthly activity trend provider, not to generic `REVENUE`, market quote, valuation, or product-revenue-mix intent.

### Company Resolution

1. User can provide a symbol or company name.
2. Company-name resolution must use the existing Companies-first resolution path.
3. If multiple companies match, return a bounded disambiguation response.
4. The query provider must use `ExternalCompanyId` as the stable join key.

### Data Retrieval

1. The provider reads from `CompanyMonthlyActivityTrendSnapshots` or a chart-ready projection derived from it.
2. The provider may read the company resolver table as the second data source.
3. The provider must not read raw Noavaran ProductSales line items for historical trend answers.
4. The provider returns a structured contract containing chart series and insight rows.

### Chart Contract

The response must include a chart-ready payload with:

- `CompanySymbol`
- `CompanyName`
- `LatestReportYear`
- `LatestReportMonth`
- `UnitLabelFa = "میلیون ریال"`
- `FiscalMonthLabelsFa[]`
- `PreviousFiscalYearSalesSeries[]`
- `CurrentFiscalYearSalesSeries[]`
- `Average12MonthSalesSeries[]` or a constant `Average12MonthSalesAmount`
- `MissingDataPoints[]`
- `SourceProviderName`

Rules:

- Current-year months not yet reported must be null, not zero.
- Previous-year missing months must be null and flagged.
- The 12-month average line must reflect persisted snapshot values.
- The chart payload must be usable by the frontend without additional financial calculation.

### AI Answer Rendering

The text answer must include:

1. Direct summary of the latest month.
2. Comparison with same month previous year.
3. Comparison with trailing 12-month average.
4. Current fiscal-year progress when YTD data is available.
5. A chart-ready table or structured payload.
6. Source/freshness/explainability note.

The answer must not include market quote columns.

### Response Example

For a sales trend question, the assistant should produce a response shape similar to:

```text
فروش ماهانه کهمدا در آخرین گزارش ۳,۷۴۱,۰۰۶ میلیون ریال بوده است.
این عدد نسبت به ماه مشابه سال قبل X٪ تغییر کرده و نسبت به میانگین ۱۲ ماهه Y٪ بالاتر/پایین‌تر است.

نکته تحلیلی:
- فروش سال جاری تا این ماه نسبت به مسیر سال قبل بهتر/ضعیف‌تر است.
- مقدار فروش/تولید در صورت قابل اتکا بودن واحدها ذکر می‌شود.

[ChartPayload]
```

## Out of Scope

- Rendering the final chart image inside the backend.
- Forecasting months not yet reported.
- Product-level contribution/waterfall chart.
- Peer or industry chart comparison.
- Automatic alerting/watchlist notifications.

## Dependencies

- Spec 076 — NADPCO Monthly Activity Trend Snapshot
- Spec 057 — Monthly activity freshness and sales lookup
- Spec 059 — Monthly Activity Output Type Segmentation
- Spec 072/074 — centralized semantic alias and intent routing registry, if already active
- Existing AI orchestration V2 path

## Priority

**High.** This story converts the persisted trend snapshot into user-visible AI value and gives the product a visually interpretable monthly production/sales analysis capability before more complex intelligence features are added.
