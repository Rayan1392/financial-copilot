# User Story - AI Monthly Production and Sales Trend Query

## Status
`[x]` Implemented

## Proposed Enhancement Status
Grounded LLM/RAG interpretation before the chart is proposed and is **not implemented yet**. The
existing deterministic response remains the current behavior until this enhancement is implemented.

## Mandatory Review and Approval Gate
This enhancement must receive a new review of the current proposal and explicit user approval after
that review before implementation begins. Earlier reviews do not satisfy this gate. Until both steps
are complete, do not implement code, prompts, configuration, migrations, or UI changes for this
enhancement.

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

Proposed enhancement: before the chart, the assistant presents a concise, data-grounded
interpretation. Version 1 grounds generation in the same `MonthlyActivityTrendResponse` used for the
chart and the deterministic trend sentences already built from that response. It does not require a
separate vector index or additional narrative corpus. The snapshot response and its provenance remain
the source of truth; the LLM improves only the interpretation.

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
- `UnitLabelFa = "میلیارد تومان"`
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
- The same canonical chart payload may be consumed by channel-specific presentation adapters. A
  Telegram adapter may render a deterministic image from these values, but it must not recalculate
  or replace the canonical monthly-trend result.
- User-facing web/API and channel renderers display `NoavaranCurrentApi` as `نوآوران امین`; the
  internal provider identifier remains unchanged for provenance, persistence, and diagnostics.
- User-facing calculation dates use the zero-padded Jalali `yyyy/MM/dd` format; the canonical
  `CalculatedAtUtc` timestamp remains unchanged for machine consumers.

### AI Answer Rendering

The text answer must include:

1. Direct summary of the latest month.
2. Comparison with same month previous year.
3. Comparison with trailing 12-month average.
4. Current fiscal-year progress when YTD data is available.
5. A chart-ready table or structured payload.
6. Source/freshness/explainability note.

The answer must not include market quote columns.

## Proposed Enhancement Acceptance Criteria — Grounded LLM Analysis Before the Chart

These criteria describe a pending enhancement and are not part of the implemented Feature 077
acceptance gate above.

1. For trend requests, the assistant generates a short Persian analysis section before the chart
   using the structured trend response, its chart points and insight rows, and the current
   deterministic trend sentences generated from that same response. This is retrieval-grounded
   generation over the query's already retrieved data; a separate semantic/vector search is not
   required for version 1.
2. The trend response is retrieved by the existing company resolver and `ExternalCompanyId`, for the
   report period(s) already selected for that chart. Only data for that resolved company and those
   periods is supplied to generation. Do not retrieve conversation memory, tenant documents, or
   another company's data. The current trend snapshot is shared market data; if private or
   tenant-specific sources are added in a later version, authorization filtering must occur before
   retrieval.
3. The exact evidence comprises the `MonthlyActivityTrendResponse` fields already used for chart
   generation (`ChartPoints`, latest period and sales, previous-year comparison, trailing-average
   comparison, YTD values, `Insights`, `MissingDataPoints`, `SourceProviderName`, and
   `CalculatedAtUtc`) plus the deterministic Persian trend sentences generated from those fields.
   Do not separately index or embed the rendered sentences: they are a prompt context derived from
   the same response, not an independent evidence source.
4. The LLM must not calculate, alter, or supply numeric facts. The fixed summary sections and the
   existing deterministic insight/note sentences continue to render unchanged with their exact
   backend values. The LLM adds a separate, concise `برداشت ترکیبی` section after those notes and
   before the chart; it must synthesize relationships across available comparisons instead of
   paraphrasing or narrating individual chart points. On fallback, the current complete
   deterministic response is rendered unchanged.
5. When the data supports it, the synthesis must distinguish comparison horizons and reconcile
   mixed signals (for example, year-over-year growth alongside a month-over-month pullback, or a
   latest month above average while recent momentum has softened). It may note a low or high prior
   comparison base only when that conclusion follows directly from supplied values. It must not
   treat one strong monthly comparison as proof of a sustained trend.
6. The generated text may interpret the available comparisons and chart direction, but may not
   introduce causes, forecasts, investment advice, suitability judgments, or facts not present in
   the supplied trend response. It must not claim a rising/falling direction unless at least three
   consecutive reported monthly points are available. With fewer points or gaps, it may describe
   only the available comparisons and must disclose the limitation.
7. Evidence provenance uses the same `SourceProviderName`, `LatestReportYear`/`LatestReportMonth`,
   and `CalculatedAtUtc` already returned with the trend result. The user-facing source/freshness
   note remains deterministic. No citation to a generated sentence is presented as an independent
   source; no external-document citations are required in version 1.
8. Snapshot freshness is not extended by generation or caching: display the original calculation
   timestamp and source. Only the exact chart/query periods are considered relevant. Use only
   data-quality and comparability caveats exposed in `Insights` and `MissingDataPoints` (including
   their existing text); do not infer hidden snapshot fields or apply a second, conflicting
   freshness window.
9. The LLM output uses a bounded structured contract, for example at most one headline, three
   observations, and two caveats, each with a short character limit. Observations use controlled
   classifications (such as `rising`, `falling`, `mixed`, `above_average`, `below_average`, or
   `insufficient_data`) and reference evidence keys from the supplied response. The backend rejects
   unknown evidence keys, unsupported classifications, numeric literals, and malformed or
   over-limit output. The headline and each caveat must also reference supporting evidence keys.
   Numeric sentences remain backend-rendered from the response.
10. Generation runs under a feature flag that is off by default until evaluation and latency/cost
   measurements are accepted. Set a configurable additional latency budget (initial target: no more
   than 2 seconds); timeout, provider failure, invalid output, or budget exhaustion falls back to
   the current deterministic response and must not block chart rendering.
11. The analysis is part of the existing monthly-trend query and its existing usage-metered AI
    operation; it does not create a second customer-billable operation. Model token usage and
    generation failures remain observable under the existing request/correlation identifiers.
    Cache only data-derived output, keyed by resolved company, chart/report periods, snapshot
    `CalculatedAtUtc`, reply language, prompt/schema version, and model configuration; never include
    user-specific or tenant-specific text in this cache.
12. The canonical structured chart payload, source, freshness, numeric summary, and existing
    deterministic notes are immutable inputs and outputs of this step. LLM generation must not
    rewrite or delay them.

### Response Example

For a sales trend question, the assistant should produce a response shape similar to:

```text
فروش ماهانه کهمدا در آخرین گزارش ۳۷۴.۱۰۱ میلیارد تومان بوده است.
این عدد نسبت به ماه مشابه سال قبل X٪ تغییر کرده و نسبت به میانگین ۱۲ ماهه Y٪ بالاتر/پایین‌تر است.

نکات تحلیلی (قطعی backend):
- [نکته موجود درباره مقایسه سالانه، با مقدار قطعی backend.]
- [نکته موجود درباره میانگین ۱۲ ماهه، با مقدار قطعی backend.]

برداشت ترکیبی (LLM):
- [توضیح رابطه بین افق‌های مقایسه، مانند رشد سالانه در کنار افت ماهانه؛ بدون تکرار اعداد یا علت‌تراشی.]
- [تفسیر سطح فروش نسبت به میانگین و احتیاط لازم درباره پایداری روند.]

[ChartPayload]
```

The bracketed descriptions above are illustrative. The assistant must preserve the existing
backend notes verbatim, add only observations supported by the actual response data, and must not
emit placeholders or claim a trend when the available periods are insufficient.

## Out of Scope

- Rendering the final chart image inside the core monthly-trend query/orchestration path. A
  Telegram-specific image renderer is owned by spec 089 and consumes this story's canonical
  chart-ready payload after orchestration.
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
