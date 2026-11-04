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

## Proposed Enhancement - Grounded LLM/RAG Analysis Before the Chart

This enhancement changes the narrative section only. Keep the canonical trend response and chart
payload deterministic and unchanged. These tasks are pending; do not treat them as implemented by
the completed checklist above.

### Mandatory Review and Approval Gate

Before starting any implementation task below, obtain a new review of the current proposal and
explicit user approval after that review. Earlier reviews do not satisfy this gate. Until both steps
are complete, do not implement code, prompts, configuration, migrations, or UI changes for this
enhancement. Spec and review-document edits may continue without implementation.

### Task 11 - Build a Grounded Analysis Context

Prepare a bounded context from the same persisted trend response already loaded for the chart and
the deterministic Persian trend sentences already rendered from that response. The v1 grounding
source is `MonthlyActivityTrendResponse`; no separate vector index, semantic search, external
analysis corpus, or database reads beyond those already required for the chart are needed.

Include only:

1. The resolved company's chart/query-period data: `ChartPoints`, latest-period facts and
   comparisons, YTD fields, `Insights`, `MissingDataPoints`, and per-point reported/null flags.
   The current contract exposes completeness/comparability caveats through `Insights`; do not read
   hidden snapshot properties. If future model behavior requires a signal absent from the response,
   add it explicitly to the response contract as a separate scope item before using it.
2. The deterministic pre-chart summary string currently produced from that response, as a
   readability hint and comparison target for avoiding repetition. It is derived data, not a
   separate source of evidence and must never be indexed as a standalone document.
3. Provenance already present on the response: `CompanySymbol`, `CompanyName`, report period,
   `SourceProviderName`, and `CalculatedAtUtc`.

Rules:

- Use the existing Companies-first resolution and `ExternalCompanyId`-scoped snapshot query. Do not
  fetch conversation memory, tenant documents, another company, or additional report corpora.
- The exact chart points and report period define relevance; `CalculatedAtUtc` and
  `SourceProviderName` define provenance. Preserve the existing freshness display and completeness
  caveats; do not add an arbitrary second freshness window.
- Do not retrieve values with semantic similarity. All numeric history must be the same structured
  data used by the chart.
- No generated-sentence or embedding search is performed in v1. If an external/private corpus is
  proposed later, it requires a separate source approval and access-filter design before retrieval.

### Task 12 - Generate and Validate the Pre-chart Analysis

Use the configured LLM through the existing provider abstraction to add a `برداشت ترکیبی` section
after the existing deterministic insight/note sentences and before the chart. Preserve the
deterministic latest-month, prior-year, 12-month-average, source/freshness summary and its current
insight/note sentences verbatim. The LLM analysis must relate comparison horizons, not restate the
fixed bullets or narrate chart points. If generation is disabled or fails, render the existing
complete deterministic response unchanged. Require structured output, for example:

```json
{
  "headline": {
    "text": "...",
    "evidenceKeys": ["yoy_comparison", "mom_change"]
  },
  "observations": [
    {
      "classification": "above_average",
      "evidenceKeys": ["latest_vs_average"],
      "text": "..."
    }
  ],
  "caveats": [
    { "text": "...", "evidenceKeys": ["missing_periods"] }
  ]
}
```

The backend, not the LLM, attaches `SourceProviderName`, report period, and `CalculatedAtUtc` to
the displayed source/freshness note.

The generation prompt and validation must enforce:

- Use only the supplied structured response; never calculate, alter, or introduce numeric claims.
  Reject numeric literals in model-authored text; numeric values remain rendered by the backend.
- Allow at most one headline (120 characters), three observations (180 characters each), and two
  caveats (180 characters each). Require one or more evidence keys for the headline, every
  observation, and each caveat; reject unknown keys or unsupported classification/data combinations.
- When at least two comparison dimensions are available, the analysis must connect them in one
  synthesis (for example, strong year-over-year growth with a month-over-month pullback). It should
  explain what the combination says about the difference between annual comparison and recent
  momentum. A single-metric paraphrase or chart-point narration does not satisfy the task.
- A low-base or high-base comparison may be described only when the supplied same-period values
  support it. State it as a comparison effect, not as an external cause. Do not infer why sales
  changed.
- Allow only `rising`, `falling`, `mixed`, `above_average`, `below_average`, and
  `insufficient_data`. Permit `rising` or `falling` recent-direction classifications only when at
  least three consecutive reported monthly sales observations are present, and only if the latest
  three observations move strictly in the same direction at each step. `mixed` covers flat or
  non-monotonic movement. Permit average
  classifications only when a matching `VsAverage12Month` insight and average value exist; preserve
  the existing partial-period caveat from `Insights`. Gaps are missing data, not zero; with fewer
  points, require `insufficient_data` or omit recent-direction claims.
- Define evidence keys from available fields (for example `latest_vs_average`, `yoy_comparison`,
  `mom_change`, `recent_monthly_series`, `missing_periods`, `ytd_comparison`) and reject keys whose
  backing values are absent. Backend validation must ensure the classification is supported by the
  referenced fields.
- Do not assert causes, forecast, recommend buying/selling, or judge investment suitability.
- Avoid restating the deterministic numeric summary. The backend retains the original source,
  report-period and freshness sentence; no citation to generated prose is presented as independent
  evidence.
- Return no rewritten chart payload. Keep the canonical chart contract and numeric summary
  byte-for-byte unchanged.
- Guard the LLM step with a feature flag that is off by default. Use a configurable additional
  latency budget (initial target: at most 2 seconds). On timeout, budget exhaustion, provider error,
  invalid schema, or validation failure, fall back to the complete current deterministic response
  and render the chart normally.
- Charge this as part of the existing monthly-trend AI query operation, not as a second
  customer-billable operation. Record token usage and failures with the existing request/correlation
  identifiers.
- Cache only generated data-derived analysis, keyed by company, chart/report periods, snapshot
  `CalculatedAtUtc`, reply language, prompt/schema version and model configuration. Do not include
  user or tenant text in the cache key or cached content.

### Task 13 - Regression and Grounding Evaluation

Extend the monthly trend evaluation set to cover:

1. Compare model input against the chart result and deterministic sentences; verify every input
   belongs to the resolved company and selected chart/report periods.
2. Verify no vector, conversation-memory, tenant-document, or other-company retrieval is invoked.
3. Verify missing months and fewer than three consecutive observations cannot yield rising/falling
   claims or fabricated zeros.
4. Verify model-authored numeric literals and unknown evidence keys are rejected.
5. Verify unchanged latest-month, year-over-year, average, deterministic insight/note, and
   source/freshness sentences remain, followed by generated qualitative synthesis.
6. Give a mixed-signal case (e.g. high year-over-year comparison with month-over-month decline and
   latest sales above average); verify the analysis explains the differing horizons instead of
   paraphrasing each value or merely describing bars.
7. Verify disabled flag, latency timeout, provider error, and malformed/over-limit output all use
   the existing deterministic response and preserve the canonical chart payload.
8. Verify cache separation by company, period, snapshot calculation timestamp, language, schema
   version, and model configuration; verify no request/tenant text enters the cache.

Expected assertions:

- The generated synthesis supplements, and does not replace, the existing deterministic
  insight/note sentences; it appears before the chart.
- The synthesis connects distinct available comparison dimensions and adds an interpretation beyond
  restating fixed values or describing chart points.
- The headline, every observation, and every caveat reference valid evidence keys from the trend
  response.
- No model-authored numeric claims, unsupported causes, predictions, or investment-advice language
  are emitted.
- The structured chart series and deterministic source/freshness metadata remain unchanged.

---

## Checklist Gate

Before marking this spec complete:

- [x] `MONTHLY_ACTIVITY_TREND` intent exists in the governed runtime routing layer
- [x] Trend/chart questions route to the new provider
- [x] Latest single-number sales questions still route to existing monthly-sales snapshot
- [x] Product revenue mix questions still route to spec 075
- [x] Provider reads persisted trend snapshots, not raw line items
- [x] Structured chart payload is returned
- [x] Persian rendering rules are applied
- [x] Missing values are null/flagged, not fabricated
- [x] Market quote columns are omitted
- [x] Golden regression queries pass
- [x] `dotnet build FinancialCopilot.sln -c Release` passes
- [ ] `dotnet test` passes

Verification note:
- `dotnet test src/backend/FinancialCopilot.sln -c Release -m:1 --no-restore /p:UseSharedCompilation=false` still has 6 unrelated baseline integration failures outside spec 077:
  `ScannerExecutionEndpointTests.AiQuery_LiveSymbol_HasLivePriceFreshnessInCell`,
  `ScannerExecutionEndpointTests.AiQuery_FallbackSymbol_HasPreviousTradingDayFreshnessInCell`,
  `CachedScannerExecutionEndpointTests.RepeatedScannerQuery_ReturnsCachedFreshnessAndBillsCachedRateOncePerRequest`,
  `ScannerExecutionEndpointTests.AiQuery_DefaultColumns_IncludePriceAndConditionMetric`,
  `ExplainableAnswerEndpointTests.AiQuery_ExplainableAnswer_ConfidenceScoreIsBackendComputed`,
  and `V2ShgolDirectPriceRegressionEndpointTests.V2AiQuery_DirectPriceQuestion_Shgol_ShouldReturnLatestDailyFallbackQuote`.
