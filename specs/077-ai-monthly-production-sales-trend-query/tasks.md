# Tasks - AI Monthly Production and Sales Trend Query

## Task 1 - Define Query Intent and Contracts

Create Application-layer contracts:

`MonthlyActivityTrendQueryContracts.cs`

Suggested query:

```csharp
public sealed record MonthlyActivityTrendQuery(
    string UserMessage,
    string? SymbolOrCompanyName,
    int? LatestReportYear,
    int? LatestReportMonth,
    MonthlyActivityTrendMeasure Measure,
    bool IncludeChartPayload);

public enum MonthlyActivityTrendMeasure
{
    SalesAmount,
    ProductionQuantity,
    SalesQuantity
}
```

Suggested response:

```csharp
public sealed record MonthlyActivityTrendResponse(
    string CompanySymbol,
    string CompanyName,
    int LatestReportYear,
    int LatestReportMonth,
    string UnitLabelFa,
    decimal? LatestMonthlySalesAmount,
    decimal? SameMonthPreviousYearSalesAmount,
    decimal? Average12MonthSalesAmount,
    decimal? SalesAmountYoYGrowthPercent,
    decimal? SalesVsAverage12MonthPercent,
    decimal? YtdSalesAmount,
    decimal? YtdPreviousMonthSalesAmount,
    IReadOnlyList<MonthlyActivityTrendChartPoint> ChartPoints,
    IReadOnlyList<MonthlyActivityTrendInsight> Insights,
    IReadOnlyList<MonthlyActivityTrendMissingDataPoint> MissingDataPoints,
    string SourceProviderName,
    DateTime CalculatedAtUtc);
```

Chart point:

```csharp
public sealed record MonthlyActivityTrendChartPoint(
    int FiscalMonthIndex,
    string FiscalMonthNameFa,
    int? PreviousFiscalYear,
    decimal? PreviousFiscalYearSalesAmount,
    int? CurrentFiscalYear,
    decimal? CurrentFiscalYearSalesAmount,
    decimal? Average12MonthSalesAmount,
    bool IsCurrentYearReported,
    bool IsPreviousYearReported);
```

Acceptance:

- The contract is chart-ready and does not require frontend financial calculations.
- Null values represent missing/unreported periods; zero is only used for actual reported zero values.

---

## Task 2 - Implement Trend Query Use Case

Create use case:

`MonthlyActivityTrendQueryUseCase`

Responsibilities:

1. Resolve company symbol/name using the existing Companies-first resolver.
2. Load the latest trend snapshot for the company if the user did not specify a reporting period.
3. Build the annual comparison chart rows from persisted trend snapshots.
4. Calculate presentation-only deltas that are safe from already persisted values:
   - latest vs same-month previous year
   - latest vs average 12-month sales
5. Build insight objects, not free-form ungrounded prose.
6. Return missing-data metadata.

Important:

- The use case must not query `MonthlyReportLineItems`.
- The use case must not call the Noavaran API.
- The use case must not rely on the LLM for numeric calculations.

---

## Task 3 - Repository Projection for Annual Comparison Chart

Add repository method:

```csharp
Task<IReadOnlyList<MonthlyActivityTrendChartPoint>> GetLatestAnnualComparisonChartAsync(
    long externalCompanyId,
    int latestReportYear,
    int latestReportMonth,
    CancellationToken cancellationToken);
```

Required behavior:

1. Return 12 fiscal month rows when enough previous/current-year structure is known.
2. Previous fiscal-year series reads the previous year matching each fiscal month.
3. Current fiscal-year series reads the current year matching each fiscal month.
4. Months after the latest reported current-year month are returned with `CurrentFiscalYearSalesAmount = null`.
5. Average line is read from the current month's persisted average or from each month snapshot depending on renderer configuration.

Recommended chart convention for v1:

- Use the latest snapshot's `Average12MonthSalesAmount` as a constant horizontal line across all 12 fiscal months.
- Later versions may use per-month rolling averages.

---

## Task 4 - Semantic Catalog and Intent Routing

Update the central semantic/intent registry.

Add canonical intent:

`MONTHLY_ACTIVITY_TREND`

Add aliases/examples:

- `روند فروش`
- `روند فروش ماهانه`
- `نمودار فروش ماهانه`
- `نمودار تولید و فروش`
- `مقایسه فروش سال جاری و سال گذشته`
- `فروش امسال نسبت به پارسال`
- `فروش نسبت به میانگین ۱۲ ماهه`
- `گزارش تولید و فروش با نمودار`

Routing rules:

1. If the user asks for latest/monthly sales as a single number, keep existing `MONTHLY_SALES` behavior.
2. If the user asks for trend/chart/comparison over months/years, route to `MONTHLY_ACTIVITY_TREND`.
3. If the user asks for top products/product mix, route to `PRODUCT_REVENUE_COMPOSITION` / spec 075.
4. Do not route trend questions to generic quarterly `REVENUE`.
5. Do not attach `LATEST_PRICE` or `DAILY_CHANGE_PCT` to trend answers.

---

## Task 5 - AI Retrieval Provider

Create provider:

`MonthlyActivityTrendProvider`

Responsibilities:

- Accept structured `MonthlyActivityTrendQuery`.
- Resolve company.
- Load chart-ready trend data.
- Build `MonthlyActivityTrendResponse`.
- Attach evidence metadata.
- Return a deterministic structured result to the orchestration layer.

Provider output must be independent of the LLM wording. The LLM may summarize, but it may not invent data points or perform calculations.

---

## Task 6 - Renderer / Answer Shaping

Implement renderer rules for Persian responses.

Default response sections:

1. `خلاصه آخرین ماه`
2. `مقایسه با ماه مشابه سال قبل`
3. `مقایسه با میانگین ۱۲ ماهه`
4. `نمودار/داده نمودار`
5. `منبع و واحد`

Table/chart data columns:

| ماه | فروش سال قبل | فروش سال جاری | میانگین ۱۲ ماهه |
|---|---:|---:|---:|

Formatting rules:

- Use Persian month names.
- Use thousands separators.
- Use `میلیون ریال` as unit.
- Do not display raw metric codes.
- Do not display market quote columns.
- If current-year value is null, render `—` or leave absent; do not render zero.
- If average is incomplete, add a short note: `میانگین ۱۲ ماهه با N دوره موجود محاسبه شده است.`

---

## Task 7 - API Contract for Frontend Chart Rendering

If the current chat API already returns structured assistant content, add a new content block type:

`monthlyActivityTrendChart`

Suggested payload:

```json
{
  "type": "monthlyActivityTrendChart",
  "title": "فروش ماهانه کهمدا",
  "unit": "میلیون ریال",
  "xAxis": ["فروردین", "اردیبهشت", "خرداد"],
  "series": [
    { "name": "فروش ۱۴۰۴", "kind": "bar", "values": [null, null, 3741006] },
    { "name": "فروش ۱۴۰۵", "kind": "bar", "values": [null, null, 3741006] },
    { "name": "میانگین ۱۲ ماهه", "kind": "line", "values": [1000000, 1000000, 1000000] }
  ],
  "missingDataPoints": []
}
```

The backend does not need to render an image. It must return structured chart data that the frontend can render consistently with the product design system.

---

## Task 8 - Evaluation and Regression Dataset

Add golden AI queries:

1. `روند فروش ماهانه کهمدا را نشان بده`
2. `نمودار فروش ماهانه کهمدا در سال جاری و سال قبل را بکش`
3. `فروش کهمدا نسبت به میانگین ۱۲ ماهه چطوره؟`
4. `تولید و فروش کچاد نسبت به سال قبل چه تغییری کرده؟`
5. `گزارش تولید و فروش کسرا با نمودار`
6. `پرفروش‌ترین محصول کچاد چیست؟` — must still route to product revenue mix, not trend.
7. `آخرین فروش کگل چقدر بوده؟` — must still route to monthly sales snapshot, not trend.
8. `درآمد فصلی فملی چقدر است؟` — must still route to quarterly revenue, not trend.

Expected assertions:

- Correct intent selected.
- Correct company resolved.
- No market quote fields in response.
- Chart payload exists for chart/trend queries.
- Null handling for missing future months.
- Product-mix and single-number monthly-sales regressions remain stable.

---

## Task 9 - Tests

### Unit tests

- Intent aliases resolve to `MONTHLY_ACTIVITY_TREND`.
- Single latest-sales questions do not resolve to trend.
- Product-mix questions do not resolve to trend.
- Renderer uses `میلیون ریال` and Persian month labels.
- Renderer formats null chart values as missing, not zero.

### Integration tests

- Query use case reads trend snapshot repository.
- Query use case does not query raw line-item repository.
- Company-name resolution works for company name and symbol.
- Chart payload contains previous-year, current-year, and average series.

### API-boundary tests

- `POST /api/ai/v1/query` returns structured `monthlyActivityTrendChart` content for chart questions.
- Response omits `LATEST_PRICE`, `DAILY_CHANGE_PCT`, `آخرین قیمت`, and `درصد تغییر آخرین قیمت`.
- Usage metering still charges the query through the existing AI facade path.

---

## Task 10 - Documentation

Update docs/spec references:

- Add examples to the AI query examples document.
- Document that trend chart values come from Noavaran trend snapshots.
- Document that chart monetary unit is million Rials.
- Document dependency on spec 076.

---

## Checklist Gate

Before marking this spec complete:

- [ ] `MONTHLY_ACTIVITY_TREND` intent exists in the governed semantic registry
- [ ] Trend/chart questions route to the new provider
- [ ] Latest single-number sales questions still route to existing monthly-sales snapshot
- [ ] Product revenue mix questions still route to spec 075
- [ ] Provider reads persisted trend snapshots, not raw line items
- [ ] Structured chart payload is returned
- [ ] Persian rendering rules are applied
- [ ] Missing values are null/flagged, not fabricated
- [ ] Market quote columns are omitted
- [ ] Golden regression queries pass
- [ ] `dotnet build FinancialCopilot.sln -c Release` passes
- [ ] `dotnet test` passes
