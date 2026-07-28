# User Story - Monthly Sales Trend Chart Export and Query Aliases

## Status

`[ ]` Not yet implemented

## Story

As a Financial Copilot user,

I want to receive the same monthly sales-trend chart for the common Persian ways of asking for it,
and download a complete image of that chart,

so that I can consistently inspect and share the reported sales trend without losing the values,
chart legend, unit, or explanatory text.

## Business Context

Specs `076`, `077`, and `078` establish the persisted monthly-activity trend data, the structured
AI query result, and the interactive web chart. This feature completes that web experience in two
ways:

1. Treat the approved equivalent Persian phrases as the same canonical monthly sales-trend intent.
2. Let a user download a faithful PNG image of the chart, including values visible on every bar and
   the explanatory content shown beneath the chart.

This is a presentation and deterministic intent-routing feature. It must reuse the existing
persisted `monthlyActivityTrendResult` contract; it must not query raw monthly-report line items,
invent data, or let the LLM access database structure or issue direct SQL.

## Canonical Query Semantics

For a resolvable symbol, each of the following questions must invoke the same monthly sales-trend
workflow and produce the same structured result and chart as `روند فروش ماهانه {symbol}`:

- `چارت فروش ماهانه {symbol}`
- `روند فروش {symbol}`
- `روند تولید و فروش {symbol}`
- `نمودار تولید و فروش ماهانه {symbol}`
- `نمودار فروش {symbol}`
- `نمودار فروش ماهانه {symbol}`

`روند تولید و فروش` and `نمودار تولید و فروش ماهانه` are aliases for the existing monthly
sales-trend chart in this feature. They do **not** introduce a second production series or a
dual-axis chart. A future feature may expand the canonical payload and visualization if a true
production trend is required.

The aliases must be recognized deterministically in both supported AI orchestration paths. The LLM
may assist with symbol extraction only within the existing governed flow; it must not choose a
different metric or bypass the monthly-trend use case.

## Downloadable Chart Image

The interactive trend chart must expose a clearly labelled Persian action, `دانلود تصویر`.

The downloaded PNG must contain a stable, complete rendering of the chart card:

- company symbol/name and chart title;
- Persian fiscal-month labels;
- current-year and previous-year bars, plus the 12-month average line when supplied by the
  existing payload;
- a readable formatted numeric label for every non-null bar value;
- legend and unit (`میلیارد تومان` or the payload-provided unit);
- the same explanation, freshness/missing-data note, and source/context text displayed below the
  interactive chart;
- an honest visual treatment for `null`/unreported periods—never a fabricated zero value.

The export must be legible when shared independently of the web page. It must use an appropriate
pixel density, a deterministic Persian filename containing the symbol and generation date, and must
not include controls, hover-only tooltips, or unrelated chat content.

## Acceptance Criteria

### Query Routing

1. All seven listed question forms resolve to the existing monthly sales-trend use case for a valid
   symbol.
2. They return the same result shape, monthly values, unit, notes, and chart renderer as the
   canonical request; only the original user-message metadata may differ.
3. The aliases do not route to generic symbol-metric lookup, a financial statement, comprehensive
   analysis, or an LLM-generated chart.
4. Unknown or unresolved symbols preserve the existing governed missing/clarification behavior.
5. The Microsoft Agent Framework V2 prompt/tool mapping and the V1 intent path remain aligned.

### Web Chart and Export

1. A trend chart with data shows a keyboard-accessible `دانلود تصویر` control.
2. Downloading creates a PNG without a server round trip or a second financial-data request.
3. Each non-null current-year and previous-year bar has its formatted value rendered in the image.
4. The exported chart includes the bottom explanatory section, not merely the plot area.
5. The interactive chart remains responsive; dense value labels may be hidden on narrow screens,
   but they are mandatory in the exported image.
6. The export works in both selected light and dark themes and has sufficient contrast in each.
7. Export failure is surfaced with a localized, non-destructive error and does not affect the
   displayed chart or chat conversation.

### Data and Safety

1. The client exports only the already-returned canonical trend payload; no recomputation of sales,
   averages, or missing values occurs in the browser.
2. `null` remains unreported/missing in both the chart and PNG.
3. Downloaded content is scoped to the current visible assistant message and must not expose data
   from another conversation or actor.
4. No database schema, credentials, raw provider payload, or internal error detail appears in the
   image or user-facing response.

## Out of Scope

- A true production quantity series, production-and-sales dual-axis chart, or product-level chart.
- Telegram image generation or changes to the existing Telegram renderer.
- New financial-data endpoints, persistence, or raw-data aggregation.
- PDF/CSV export and scheduled sharing.

## Dependencies

- Spec `076` - NADPCO Monthly Activity Trend Snapshot
- Spec `077` - AI Monthly Production and Sales Trend Query
- Spec `078` - Frontend Monthly Activity Trend Chart
- Spec `056` - Native Microsoft Agent Framework Workflows
- Existing web theme selection, so export has explicit light/dark rendering coverage.

## Priority

**High.** The requested aliases make a central chart discoverable in natural language, while the
downloadable image makes the resulting information usable outside the chat UI.
