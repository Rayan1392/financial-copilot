# User Story - Frontend Monthly Activity Trend Chart

## Status
`[ ]` Not yet implemented

## Story

As a TahlilApp-AI user,

I want the chat UI to render the monthly activity trend response as a visual chart,

so that I can quickly compare current-year monthly sales against the previous year and the 12-month average without reading only prose or raw payloads.

## Business Context

Spec `076` created the persisted `CompanyMonthlyActivityTrendSnapshots` foundation.
Spec `077` added AI/API support through `POST /api/ai/v1/query` and returns a structured
`monthlyActivityTrendResult` payload for trend-oriented monthly production/sales questions.

The remaining gap is presentation. The backend now returns chart-ready data, but the frontend
chat experience still needs a dedicated renderer that turns the structured trend payload into a
usable visual chart similar to the monthly-sales comparison chart discussed in product reviews.

Example user-visible outcomes:

- When the user asks `روند فروش ماهانه کسرا را نشان بده`, the assistant message includes a chart block below the summary text.
- When the user asks `نمودار فروش ماهانه کهمدا را با سال قبل مقایسه کن`, the frontend renders:
  - previous fiscal-year bars,
  - current fiscal-year bars,
  - a 12-month average line,
  - Persian fiscal month labels,
  - the unit note `میلیارد تومان`.

## Acceptance Criteria

### Chart Rendering

1. When the AI response includes `monthlyActivityTrendResult`, the chat UI renders a dedicated trend chart component instead of leaving the payload as hidden or text-only data.
2. The chart uses:
   - previous fiscal-year sales as one bar series,
   - current fiscal-year sales as one bar series,
   - the 12-month average as a line series.
3. The X-axis uses Persian fiscal month names from the API payload.
4. The unit is displayed as `میلیارد تومان`.
5. Months with `null` current-year values are shown as missing/unreported, not zero.
6. Missing previous-year months are shown honestly and do not fabricate bars.

### Visual Behavior

1. The chart is readable on desktop and mobile widths.
2. The chart colors clearly distinguish:
   - current year,
   - previous year,
   - average line.
3. The chart title uses the company symbol or company name from the payload.
4. Tooltips or labels, if shown, use Persian display formatting and thousands separators.
5. The component does not display market quote fields because trend responses intentionally omit them.

### Integration Behavior

1. The renderer reads only the existing AI response contract from spec `077`; it must not call a second endpoint for chart data.
2. The frontend does not perform financial derivation logic beyond view shaping; the API payload remains the source of truth.
3. Conversation reload/history rendering also displays the saved trend chart for prior assistant messages.

### Empty and Partial Data

1. If `monthlyActivityTrendResult` is absent, the UI falls back to the normal text answer path.
2. If some chart points are missing, the component renders the available periods and an honest missing-data note when present in the payload.
3. If the API returns only partial current-year months, future months remain empty.

## Out of Scope

- Generating a PNG/image in the frontend or changing the canonical payload for Telegram. The
  server-side Telegram PNG renderer is independently owned by spec 089 and must consume the same
  `monthlyActivityTrendResult` used by this interactive web chart.
- Recomputing monthly trend data in the frontend.
- Product-level trend charts.
- Industry/peer benchmark overlays.
- Combined production-and-sales dual-axis charts unless the API contract is expanded in a later spec.

## Dependencies

- Spec `032` - Frontend Chat And Conversation API Cutover
- Spec `048` - Frontend AI Orchestration V2 Awareness
- Spec `076` - NADPCO Monthly Activity Trend Snapshot
- Spec `077` - AI Monthly Production and Sales Trend Query

## Priority

**Medium-High.** The backend already returns chart-ready trend data; this story turns that payload into the intended user-facing visualization.
