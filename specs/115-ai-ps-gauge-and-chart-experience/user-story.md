# Feature 115 - AI P/S Gauge and Chart Experience (Updated)

## Gauge Rendering Algorithm

Input: - a,b,c,d,e,f - start - end - current PS ratio

Segments: a,b,c,d,e,f in visual low-to-high order.

Angle: six equal 30-degree arcs. Population controls percentage labels only.

Numeric ranges:

- `a`: `start..min`;
- `b..e`: four equal intervals across `min..max`;
- `f`: `max..end`.

The marker uses piecewise linear interpolation within the segment containing
the current value. A value below `start` clamps to the left edge; a value
above `end` clamps to the right edge. `min` and `max` are internal boundaries,
not the complete rendered axis.

Needle: current TTM `ps_ratio` is mapped using the piecewise ranges above.

Boundary and percentage labels must use high-contrast foreground text,
visible in both light and dark themes, with sufficient font weight and size
for the gauge and its exported image.

The Gauge is shown only when feature configuration enables it.


User Story - AI P/S Gauge and Historical Chart Experience

Status

[ ] Not yet implemented

Story

As a Financial Copilot user,

I want to see a P/S gauge and historical P/S trend for a stock when I explicitly ask for the gauge,and optionally see a compact P/S gauge inside the existing monthly-sales trend chart,

so that I can understand the latest persisted price-to-sales position alongside factual historicalcontext without a live provider dependency or AI-invented valuation conclusion.

Business Context

Spec 114 persists three CyclicalWaves data components:

six gauge distribution buckets and verified provider boundary semantics;

current TTM and Forward P/S values;

the latest successful active historical P/S series.

This story exposes those persisted facts through the existing AI query/conversation flow and renders:

a standalone P/S gauge plus optional historical P/S chart when the user explicitly asks for agauge/range/needle visualization;

a compact P/S gauge inset in the existing monthly-sales trend chart when enabled by backendconfiguration.

“Live” in this feature means composed at request time from the newest eligible persisted snapshot.It does not mean a synchronous CyclicalWaves request. AI, API, frontend, conversation reload, andimage-export paths must never call the vendor.

Rollout and Configuration

Suggested backend contract:

{
  "CyclicalWavesPsVisualization": {
    "Enabled": false,
    "EnableStandaloneGauge": true,
    "IncludeGaugeInMonthlySalesTrendChart": false,
    "IncludeHistoryInStandaloneGauge": true,
    "AllowStaleStandaloneGauge": true,
    "MaxSyncAgeHours": 48,
    "MaxObservationLagTradingDays": 2,
    "MaxHistoryPoints": 5000,
    "DisplayPercentageDecimals": 2
  }
}

Rules:

Enabled=false prevents registration/execution of the new visualization branch and omits monthlyenrichment.

Standalone and monthly-sales flags are independent under the overall gate.

The frontend does not read appsettings.json; the backend decides whether to include the optionalstructured block, and the renderer follows payload presence/status.

Options are validated at startup.

Production remains disabled until spec 114 boundary/needle parity and data-readiness gates pass.

Standalone Intent, Aliases, and Precedence

Dedicated intent:

PsGaugeVisualization

The intent requires:

a resolvable symbol/company; and

explicit gauge, range, band, or needle semantics.

Representative Persian aliases include normalized variants of:

گیج P/S غگلپا

گیج PS غگلپا را نشان بده

گیج پی اس غگلپا

گیج نسبت قیمت به فروش غگلپا

عقربه P/S غگلپا کجاست؟

محدوده P/S غگلپا را نمایش بده

غگلپا در کدام محدوده قیمت به فروش است؟

وضعیت غگلپا روی گیج قیمت به فروش

P/S gauge غگلپا

price to sales gauge غگلپا

Normalization must account for case, slash/space variants, Persian/Arabic characters, ZWNJ,punctuation, and PS/P S/P/S/پی اس forms without broadening the intent to ordinary P/S lookup.

Routing safeguards:

P/S غگلپا remains the existing SymbolLookup/PS_TTM point lookup.

P/S threshold/filter questions remain Scanner.

Existing ComprehensiveAnalysis behavior for its governed نمودار P/S topic is not silentlyreplaced.

General wording such as غگلپا را تحلیل کن remains Analysis.

An unresolved or ambiguous symbol follows existing clarification/missing-answer behavior.

The new intent wins only when the explicit visualization semantics are present and the overallfeature is enabled.

The deterministic alias/precedence rules are shared by V1 rollback routing and the active MicrosoftAgent Framework V2 workflow. The LLM may select or introduce the use case but may not calculatebands, percentages, boundaries, needle position, history values, freshness, or quality status.

Persisted Data Selection Policy

The application read service selects:

the newest renderable complete gauge/current snapshot for the resolved canonical company;

the latest successful active history series from spec 114;

component quality and synchronization state.

If the newest attempted data is partial/invalid but an older renderable snapshot exists, fallback isallowed only by explicit policy and must be disclosed as an older snapshot. It must never be calledcurrent.

History and gauge are independently optional:

valid gauge + missing history => gauge result with Partial history status;

valid history/current values + non-renderable gauge boundaries => no normal gauge, with partialfactual result if the selected experience supports it;

IncludeHistoryInStandaloneGauge=false => history is deliberately omitted and markedNotRequested, not missing;

no renderable stored snapshot => localized unavailable result; no PS_TTM substitution and novendor fallback.

Freshness Model

Freshness uses two independent facts:

sync freshness: age of the last successful local snapshot synchronization;

observation freshness: lag between the source observation date and the latest expected tradingdate according to the existing market calendar/latest market data.

A result is Fresh only when both configured thresholds pass. This avoids treating a successfullyre-synced but old source observation as current and avoids weekend/holiday false staleness from asimple wall-clock date rule.

Exposed statuses:

Fresh

Stale

Partial

Invalid

Unavailable

NotRequested for deliberately omitted history

Standalone stale data may be shown only when AllowStaleStandaloneGauge=true, with the source dateand warning. The monthly-sales inset requires a fresh, renderable gauge by default; stale or invalidP/S enrichment must not fail the sales chart.

Canonical Structured Result

The backend returns a versioned provider-neutral PsVisualizationResult through the existing AIresponse contract.

Identity and provenance

contractVersion

canonical company ID, symbol, and company name

provider display symbol, when useful and non-conflicting

provider name

source observation date

gauge/current fetch timestamps

last successful local sync timestamp

freshness/completeness/quality statuses

bounded localized warning codes/messages

Current values

TTM P/S

Forward P/S

provider GaugeClose as a separately named source fact

explicit value-state metadata so zero and missing remain distinct

Gauge

renderability flag/status

six stable ordered bands

each band's semantic color role, provider count, exact percentage, display percentage, and verifiedboundary labels

needle source value

needle band

normalized angular position

clamped/out-of-range state

provider boundary/statistic facts

The contract uses semantic names and never exposes a through f, EF row types, SVG paths, canvascommands, CSS, or chart-library configuration.

History

isIncluded

ordered lightweight points containing date, ratio, and a provider-neutral stable sequence/key

same-date points remain distinct

source first/last date

source total point count

returned point count

isTruncated

history quality/freshness status

For the supplied 1,124-point series, all points fit under the proposed default MaxHistoryPoints=5000and must be returned. If a future series exceeds the governed cap, the result must explicitly statetruncation and use a deterministic documented range policy; it must never silently average, smooth,or invent points.

Deterministic Gauge Calculation

All calculations are non-LLM application/presentation functions based on the verified spec 114contract.

exact percentage = count / total × 100 using decimal arithmetic;

display percentages use configured decimal places;

display reconciliation uses a deterministic largest-remainder policy so displayed values totalexactly 100 at the selected precision without changing counts or exact percentages;

needle position follows the verified vendor formula;

out-of-range values are visually clamped but the original value and clamped state remain visible;

TTM, Forward, and GaugeClose never overwrite or substitute for one another;

ratios are unitless and receive no currency/unit conversion.

If verified semantics are unavailable or a required boundary contract is invalid, the system doesnot draw a normal gauge.

Standalone Presentation

The standalone experience contains two coordinated panels when history is enabled:

Gauge/current panel

six-band semicircular gauge from the verified low-to-high color-role order;

percentage labels and verified boundary labels;

needle and آخرین value with source date;

TTM P/S and P/S Forward values below the gauge;

source, freshness, and quality disclosure;

textual band table/summary so meaning is not conveyed by color alone.

Historical P/S panel

responsive area/line chart;

chronological x-axis using localized dates;

unitless P/S y-axis;

tooltip with source date and exact ratio;

same-date observations retained as separate ordered points;

no browser-side financial recomputation, averaging, smoothing, or data deletion.

The panels may stack on mobile and sit side-by-side on wider screens. If history is disabled byconfiguration, only the gauge/current panel is returned and rendered.

The gauge is descriptive evidence, not a recommendation. Green/red colors must not be described asbuy/sell, cheap/expensive, safe/risky, or target-price signals unless a separate governed analyticalfeature is created later.

Optional Compact Gauge in Monthly-Sales Trend

When IncludeGaugeInMonthlySalesTrendChart=true and a fresh renderable snapshot exists:

the existing monthly-sales use case continues to use its existing persisted sales snapshot;

the P/S compact read occurs through an independent application read service and may execute inparallel;

the monthly-sales response receives an optional compact psGauge projection containing nohistorical series;

the interactive chart and deterministic PNG export use the same compact projection;

the gauge occupies a reserved upper-left inset consistent with the supplied composition;

title, legends, units, year totals, bars, labels, 12-month average line, explanations, and watermarkremain unobscured and at their existing readable sizes;

the compact inset shows at least the gauge, latest TTM value/date, TTM label, Forward label/value,and freshness/source indication.

When the option is false:

no P/S repository read is made by the monthly-sales use case;

no P/S property/block is emitted;

existing routing, calculations, visual layout, and export behavior remain backward-compatible.

When enabled but P/S data is missing, stale, partial, invalid, or the read fails:

the monthly-sales result and chart still succeed;

no fabricated gauge is drawn;

a bounded optional localized unavailable/stale note may occupy the reserved inset only when it doesnot obscure the sales chart;

no live provider request is attempted.

The backend is the single source of truth for whether the optional block exists. Interactive andexport renderers must not maintain conflicting feature-flag logic.

AI Narrative Policy

The response may include a short deterministic factual introduction, for example:

گیج P/S نماد {symbol} بر اساس آخرین داده ذخیره‌شده در تاریخ {date} نمایش داده شده است.

The AI must not:

paraphrase numbers differently from the structured result;

infer why P/S moved;

calculate valuation, fair value, target price, support/resistance, or buy/sell guidance;

call stale or previous data current;

substitute the generic PS_TTM metric for missing gauge bands.

Unavailable text:

داده‌ای برای نمایش گیج P/S نماد {symbol} یافت نشد.

Localized deterministic variants are required for stale, partial, invalid-boundary, history omitted,history truncated, and clamped states.

Conversation Persistence and Reproducibility

A live result and a reloaded conversation must render the same financial facts without a vendor call.

The persisted assistant-message payload stores a versioned, presentation-ready snapshot of the exactresult returned to the client, including the returned lightweight history points when history isincluded. It must not store raw provider DTOs, provider HTTP payloads, tokens, or internal databaseschema.

Persistence must enforce a governed maximum serialized size. If the existing conversation artifactstore supports immutable externalized chart payloads, a stable artifact reference may be used;otherwise the same explicit history cap/truncation policy applies before persistence. Reload mustnever silently switch to newer financial values.

Deterministic Image Export

Standalone and combined exports:

consume only the structured view model already returned/persisted;

perform no network or provider request;

use high-DPI rendering and embedded/approved Persian fonts already available to the product;

include source date, provider, freshness, and warnings;

exclude controls and hover-only UI;

preserve RTL text and all boundary/value labels;

match interactive values, percentages, bands, and needle state exactly.

The combined monthly-sales/P/S export follows the supplied composition and must not reduce existingsales-chart labels below the accepted readability baseline.

Accessibility and Responsive Requirements

Every band has textual order, range/boundary, count, and percentage information.

Color is never the only carrier of meaning.

Needle/current values are announced to assistive technology.

The history chart has an accessible summary and keyboard-reachable point details or equivalenttabular data.

Light/dark themes preserve contrast.

Mobile layout remains readable without horizontal clipping of critical labels.

Persian and Latin P/S text remains correctly ordered in RTL content.

Acceptance Criteria

Routing and Orchestration

Approved explicit gauge aliases route to PsGaugeVisualization only when enabled and a companycan be resolved.

V1 and V2 call the same application use case and return equivalent structured facts.

P/S {symbol}, scanner thresholds, ComprehensiveAnalysis P/S topics, and general analysis do notregress.

AI execution performs zero CyclicalWaves HTTP calls.

LLM prompts/tools cannot calculate financial bands or query provider/storage directly.

Data Selection and Freshness

The newest renderable complete snapshot is selected according to the documented policy.

Older fallback, stale, partial, invalid, unavailable, and not-requested states are explicit.

Freshness uses both local sync age and source observation lag against the market calendar.

The history query reads only the latest successful active series from spec 114.

The supplied 1,124-point series returns all points and all eight duplicate-date groups.

Standalone Gauge and History

Six bands, exact/display percentages, boundaries, needle, TTM, Forward, GaugeClose, and datesmatch the persisted source facts/reference algorithm.

Display percentages reconcile deterministically to 100 at configured precision.

Zero, missing, stale, invalid, clamped, and truncated states are displayed honestly.

The browser does not average, smooth, recalculate, or discard same-date points.

No recommendation or invented valuation conclusion appears.

Monthly-Sales Integration

Overall and monthly-inset flags default to disabled/off for rollout.

With the inset flag off, no P/S read/block/layout change occurs in the monthly-sales use case.

With the flag on and fresh data present, interactive and exported sales charts show the samecompact gauge in the reserved upper-left area.

P/S read/render failure never prevents the monthly-sales result or export.

The compact projection contains no P/S history points.

Existing title, units, bars, average line, totals, labels, narrative, and watermark remain readableand unchanged in meaning.

Conversation, Export, and Security

Conversation reload reproduces the exact returned facts without a vendor call or silent refresh.

Persisted payloads are versioned, bounded, and contain no raw provider DTOs, token, authorizationheader, database schema, or internal exception.

Interactive and PNG outputs match numerically and semantically.

Actor/tenant conversation isolation remains unchanged.

No public vendor-proxy endpoint is introduced.

Accessibility and Responsiveness

All color-coded information is also available as text.

Gauge and history are understandable by keyboard/screen-reader users.

RTL, mobile, light, and dark layouts pass visual/accessibility tests.

Downloaded output contains complete labels and source/freshness disclosure.

Out of Scope

Recalculating P/S from price, revenue, market cap, or forward-sales forecasts.

Buy/sell signals, intrinsic value, target price, or recommendation text.

Replacing existing PS_TTM lookup, P/S scanner, or ComprehensiveAnalysis behavior.

Calling CyclicalWaves synchronously from AI, API, frontend, reload, or export paths.

Peer/industry P/S gauges.

Notifications/alerts based on gauge-band changes.

Telegram-specific card/image delivery; it may be specified separately using the same structuredresult and export contract.

Dependencies

Spec 114 - CyclicalWaves P/S visualization data sync and persistence

Specs 045, 072, and 074 - governed metric/intent routing

Specs 047 and 056 - V2 workflow and V1 rollback alignment

Specs 076, 077, 078, and 113 - monthly-sales trend contract, rendering, and export

Specs 009, 018, and 028 - explainability, observability, and missing-answer feedback

Governed latest-trading-date/market-calendar capability for observation freshness; reuse theexisting capability when present, otherwise create a prerequisite abstraction/spec rather thanhardcoding weekdays

Priority

High after spec 114. It adds the requested P/S visualization while keeping acquisition off the AIcritical path and preserving existing P/S semantics.
