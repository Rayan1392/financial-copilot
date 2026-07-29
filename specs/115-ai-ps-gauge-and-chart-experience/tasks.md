# Feature 115 Tasks (Updated)

## [x] Task 5 - Implement Gauge Rendering

Use the verified CyclicalWaves semantics.

Do not implement: - vendor guessing - local quantile calculation -
alternative bucket algorithms

Acceptance: - six equal-width bands render in a,b,c,d,e,f visual order. - percentages use provider
counts and reconcile to 100. - needle uses current TTM PS ratio. - axis uses min/max. - start/end/avg
remain available as separate provider facts. - out-of-range values clamp visibly.

## Task 6 - Integrate AI Responses

Support: - direct P/S Gauge questions - optional Gauge inset in monthly
sales trend responses - no live provider calls from AI rendering path


Tasks - AI P/S Gauge and Historical Chart Experience

All tasks are specification-only and remain unimplemented.

## [ ] Task 1 - Define and Validate Visualization Options

Add the overall rollout gate and independent standalone/monthly options, freshness thresholds,history cap, and percentage precision.

Acceptance:

Overall Enabled defaults to false.

Monthly-sales inset defaults to false.

Invalid age, trading-day lag, point cap, or precision values fail startup validation.

Frontend behavior is driven by structured payload presence/status, not direct appsettings access.

## [ ] Task 2 - Define the Versioned Provider-Neutral Read Models

Create contracts for:

PsVisualizationResult;

identity/provenance;

current values with explicit zero/missing state;

six semantic gauge bands;

needle state;

lightweight history points and truncation metadata;

freshness, completeness, quality, and warning codes;

compact monthly-sales psGauge projection.

Acceptance:

Contracts expose no a-f, EF rows, raw provider DTOs, SVG/canvas/CSS, or chart-library objects.

Same-date history points remain representable and deterministically ordered.

Full and compact projections cannot accidentally include different financial values.

Contract versioning supports persisted conversation reloads.

## [ ] Task 3 - Implement Persisted Snapshot and Active-History Selection

Resolve a canonical company and read the newest renderable complete snapshot plus the latestsuccessful active history series from spec 114.

Acceptance:

No outbound HTTP call occurs.

Selection/fallback policy is deterministic and tested.

A fallback to an older snapshot is explicit in status/warnings.

Gauge and history can independently be complete, partial, invalid, unavailable, or not requested.

Only active history points are returned.

## [ ] Task 4 - Implement Dual-Dimension Freshness Evaluation

Evaluate local sync age and source observation lag against a governed market calendar/latesttrading date capability. Reuse the existing service when present; otherwise record the missingcapability as a prerequisite rather than hardcoding weekdays.

Acceptance:

Weekends/holidays do not create false stale status solely from wall-clock date age.

A recently synced but old provider observation is not called fresh.

Fresh/stale outcomes include the facts used to reach the status.

Standalone stale-display and monthly-inset fresh-only policies are independently enforced.

## [ ] Task 5 - Normalize Gauge Bands and Percentages

Map persisted provider buckets/boundaries into six semantic bands using the verified spec 114reference contract.

Acceptance:

Decimal arithmetic is used.

Exact percentages retain precision.

Display percentages use a deterministic largest-remainder reconciliation and total exactly 100 atconfigured precision.

Zero/invalid bucket totals do not render a normal gauge.

Reference fixtures reproduce vendor labels/order.

## [ ] Task 6 - Compute Deterministic Needle State

Apply the verified boundary/interpolation algorithm and produce needle band, angular position,source value, and clamp state.

Acceptance:

The exact source value driving the needle is documented and tested.

Out-of-range values preserve the original number and expose visual clamp state.

Invalid/unverified boundaries yield a non-renderable gauge rather than guessed placement.

Interactive and export code consume the same computed state.

## [ ] Task 7 - Add the Explicit Gauge Alias and Normalization Registry

Add deterministic Persian/English aliases and orthographic normalization for gauge/range/needlesemantics.

Acceptance:

Approved P/S, PS, P S, and پی اس gauge variants route correctly.

P/S {symbol} remains point lookup.

P/S filtering remains Scanner.

Existing ComprehensiveAnalysis P/S topic remains unchanged.

General stock analysis and unresolved/ambiguous symbols follow existing behavior.

When the overall feature is disabled, the new branch is not selected/executed.

## [ ] Task 8 - Align V1 and Microsoft Agent Framework V2

Add the dedicated branch/result to both orchestration paths and shared alias/precedence policy.

Acceptance:

Both paths invoke the same application use case.

The LLM has no provider HTTP, database-schema, percentage, boundary, or needle-calculation tool.

Billing, confidence, correlation, telemetry, and missing-answer behavior use existing facadeconventions.

V1/V2 return equivalent structured facts for the same persisted snapshot.

## [ ] Task 9 - Extend Live API and Conversation Contracts

Add optional versioned psVisualizationResult to live AI responses and persisted assistant messages.

Acceptance:

Existing clients tolerate the additive field.

Persisted payload contains presentation-ready facts only.

Reload reproduces the same values/history without reading the vendor or silently selecting newerdata.

Serialized payload size is bounded; existing immutable artifact storage may be used when supported.

Actor/tenant isolation remains unchanged.

## [ ] Task 10 - Implement Governed History Projection

Return ordered lightweight history points according to IncludeHistoryInStandaloneGauge andMaxHistoryPoints.

Acceptance:

The supplied 1,124-point fixture returns all 1,124 points.

All eight duplicate-date groups remain distinct.

History disabled is NotRequested, not Unavailable.

If the cap is exceeded, truncation/range policy is explicit and deterministic.

No averaging, smoothing, interpolation, or browser financial recomputation occurs.

## [ ] Task 11 - Add Configuration-Gated Monthly Trend Enrichment

Enrich the existing monthly-sales trend result with a compact P/S gauge projection only when thebackend option is enabled and a fresh renderable snapshot exists.

Acceptance:

Disabled means no P/S repository read and no response block.

Sales and P/S reads may execute independently/in parallel.

P/S failure degrades only the optional block.

Compact projection contains no history points.

No synchronous provider fallback exists.

## [ ] Task 12 - Implement the Standalone Responsive Gauge Panel

Render the six bands, percentages, verified boundaries, needle, latest value/date, TTM, Forward,source, freshness, quality, and textual band summary in Persian RTL.

Acceptance:

Missing is not rendered as zero; explicit zero remains visible.

Stale/fallback/clamped/non-renderable states are visible.

Color is not the only information channel.

Persian/Latin bidi text is correct.

No buy/sell, cheap/expensive, or recommendation wording appears.

## [ ] Task 13 - Implement the Historical P/S Panel

Render the returned history as a responsive area/line chart and accessible equivalent.

Acceptance:

Chronological order is stable.

Same-date points remain separate and visible/inspectable.

Tooltip/table includes date and exact ratio.

Y-axis is unitless P/S.

The supplied and maximum-size series meet the agreed performance budget.

Mobile, light, and dark themes remain readable.

## [ ] Task 14 - Compose the Compact Gauge into Monthly Sales

Reserve a non-overlapping upper-left inset matching the supplied reference composition.

Acceptance:

Existing title, units, legends, annual totals, current-year percentage, bars, labels, average line,narrative, and watermark remain readable and unchanged in meaning.

Inset displays the required compact values/source/freshness.

Mobile layout stacks or simplifies decorative details while retaining textual values.

The full P/S historical chart is never embedded in the monthly-sales chart.

Missing/stale P/S never blocks or distorts the sales chart.

## [ ] Task 15 - Extend Deterministic PNG Export

Support complete standalone P/S export and the same compact gauge in combined monthly-sales export.

Acceptance:

Export uses only the already-returned/persisted view model.

No network request occurs.

Interactive and export values, bands, percentages, boundaries, and needle match exactly.

High-DPI Persian labels, source, dates, freshness, warnings, and watermark are legible.

Controls/hover-only UI are excluded.

## [ ] Task 16 - Add Deterministic Localized Narrative and State Messages

Provide bounded Persian text for fresh, stale, older-fallback, partial, unavailable, invalidboundaries, history not requested, history truncated, and clamped states.

Acceptance:

The no-data sentence matches the story.

Numeric facts are not paraphrased inconsistently.

No AI-generated cause or valuation recommendation is added.

Stale/previous data is never described as current.

## [ ] Task 17 - Add Routing and Orchestration Regression Tests

Cover V1/V2 equivalence and negative precedence cases:

approved explicit gauge aliases;

P/S point lookup;

scanner thresholds;

ComprehensiveAnalysis P/S topic;

general analysis;

unresolved/ambiguous symbol;

feature disabled.

Acceptance:

The dedicated use case is invoked exactly when intended.

AI execution makes zero vendor calls.

Existing routing regressions pass.

## [ ] Task 18 - Add Backend Contract, Freshness, and Persistence Tests

Cover:

structured full/compact mapping;

newest-renderable and older-fallback selection;

sync-age and trading-day observation freshness;

complete/partial/invalid/unavailable/not-requested states;

exact percentage reconciliation;

needle clamp/non-renderable behavior;

1,124 history points and duplicate dates;

history cap/truncation;

conversation persistence/reload and payload bounds;

monthly flag off/on and P/S enrichment failure isolation.

## [ ] Task 19 - Add Frontend, Accessibility, and Export Tests

Cover:

six bands and textual equivalents;

boundary/needle screenshot parity;

TTM/Forward/GaugeClose separation;

source date/freshness/warnings;

duplicate-date history rendering;

zero/missing/stale/partial/invalid/clamped/truncated states;

responsive standalone/combined layouts;

keyboard/screen-reader behavior;

bidi, light/dark themes;

PNG parity and no-network behavior.

## [ ] Task 20 - Verify Performance, Security, and Rollout

Verify:

no provider request on AI, frontend, reload, or export paths;

history response/persistence/render performance within budget;

no token/raw provider payload/database schema/internal error leakage;

overall and monthly feature-disabled behavior;

canary enablement for verified sample companies;

visual parity with supplied screenshots and same-symbol provider fixtures;

rollback by configuration without migration rollback.

Acceptance:

Production enablement occurs only after spec 114 backfill, freshness, security, and gauge-semanticgates pass.

Existing monthly-sales, PS_TTM, Scanner, ComprehensiveAnalysis, chat history, and export regressionsuites pass.
