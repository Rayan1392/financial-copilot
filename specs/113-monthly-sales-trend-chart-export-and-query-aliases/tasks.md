# Tasks - Monthly Sales Trend Chart Export and Query Aliases

## [x] Task 1 - Audit the Existing Trend Contract and Renderers

Identify the canonical monthly trend result contract, the V1 and V2 intent-routing points, the
interactive chart component, and the persisted-message rendering path.

Acceptance:

- No new financial-data endpoint or raw-report query is proposed.
- The export receives one complete, already-rendered trend view model.
- Web-only scope is explicit; the Telegram renderer remains unchanged.

### Audit record — completed 2026-07-28

#### Canonical data contract

- The application-owned contract is
  `src/backend/FinancialCopilot.Application/FinancialData/Ingestion/MonthlyActivityTrendQueryContracts.cs`.
  `IMonthlyActivityTrendQueryUseCase.ExecuteAsync` returns `MonthlyActivityTrendResponse`; its
  `ChartPoints` preserve current/previous/12-month-average values as nullable decimals and expose
  explicit reported flags. It also carries `Insights`, `MissingDataPoints`, provider identity, and
  calculation time.
- `POST /api/ai/v1/query` maps that result without recalculation in
  `AiFacadeController.MapMonthlyActivityTrendResult`. The frontend receives the matching
  `MonthlyActivityTrendResult` interface in `src/frontend/src/lib/chat.functions.ts`.
- Therefore Feature 113 does not need a new trend endpoint, raw monthly-report query, or database
  change. The export view model must be a presentation-only projection of that existing payload.

#### Intent-routing audit

- V1 calls `MonthlyActivityTrendIntentRules.LooksLikeMonthlyActivityTrendQuery` from
  `LlmAiIntentDetector.DetectAsync`.
- V2 has an early deterministic branch in
  `FinancialCopilotWorkflowDefinition` that calls the same rule, extracts a symbol with
  `ExtractCompanySymbol`, and invokes `IMonthlyActivityTrendQueryUseCase` before the agent
  tool-calling loop.
- The existing phrase registry already covers `روند فروش`, `روند تولید و فروش`, `نمودار فروش`,
  `نمودار فروش ماهانه`, and the production-and-sales chart variants. `چارت فروش ماهانه` is the
  requested missing explicit phrase and belongs in the shared registry in Task 2. Both routing
  paths already share the registry, so no separate V1/V2 classifier should be created.

#### Web rendering and persistence audit

- `src/frontend/src/components/app/monthly-activity-trend-chart.tsx` renders the interactive
  Recharts card. It already renders current/previous bars, average line, Persian month labels,
  unit, insights, missing-data notes, and `LabelList` numeric labels for non-null bars.
- `AssistantBlock` in `src/frontend/src/components/app/message-list.tsx` renders that chart when
  `monthlyActivityTrendResult` exists. `src/frontend/src/lib/chat.functions.ts` maps the same
  structured field when assistant messages are loaded from a persisted conversation.
- V2 persistence passes `monthlyActivityTrendResult` through
  `MessagePersistenceFunction` into `AssistantMessagePayload`, so conversation reload has the
  required canonical payload and does not need a second request.
- Current gaps for later tasks: there is no download control/PNG renderer/export view model; the
  chart does not display provider/calculation context even though the payload contains it; and the
  chart's hard-coded axis/grid colors require an explicit theme-aware export treatment. Telegram
  already has an independent PNG renderer and is intentionally out of scope.

---

## [x] Task 2 - Define a Canonical Alias Registry

Add a deterministic registry/normalization rule for the seven approved Persian request forms:

- `روند فروش ماهانه {symbol}`
- `چارت فروش ماهانه {symbol}`
- `روند فروش {symbol}`
- `روند تولید و فروش {symbol}`
- `نمودار تولید و فروش ماهانه {symbol}`
- `نمودار فروش {symbol}`
- `نمودار فروش ماهانه {symbol}`

Acceptance:

- All phrases map to the existing monthly sales-trend intent.
- Production wording is documented as an alias, not a request to fabricate production data.

Completed 2026-07-28:

- Added `CanonicalMonthlySalesTrendPhrases` to the shared
  `MonthlyActivityTrendIntentRules` registry, including the previously unsupported explicit phrase
  `چارت فروش ماهانه`.
- Preserved the existing broader trend/comparison phrases as supported aliases and normalized the
  combined registry once for both detection and symbol extraction.
- Added deterministic unit coverage for all seven Feature 113 phrases, including symbol extraction.
- Symbol extraction and missing-symbol behavior preserve existing governed semantics.

---

## [x] Task 3 - Align V1 Intent Detection and V2 Tool Mapping

Apply the canonical alias registry to both supported orchestration paths.

Acceptance:

- V1 and Microsoft Agent Framework V2 select the same monthly trend use case.
- The V2 system prompt/tool description covers the aliases without exposing storage details.
- Generic metric, analysis, and financial-statement routing does not win for these phrases.

Completed 2026-07-28:

- V1 continues to classify every alias through the shared `MonthlyActivityTrendIntentRules` registry.
- V2 evaluates that same registry in its early deterministic trend branch, before model tool calling;
  it resolves the symbol and invokes only `IMonthlyActivityTrendQueryUseCase`.
- Updated the V2 system guidance to document all aliases as workflow-owned, prohibit model-created
  charts/production series, and keep database/storage details out of the model prompt.
- Added V2 endpoint regression coverage for every Feature 113 alias, asserting the canonical chart
  payload and zero outer tool-selection calls.

---

## [x] Task 4 - Create an Export-Ready Chart Card View Model

Build a presentation-only view model from the existing `monthlyActivityTrendResult`.

Required fields:

- title and company identity;
- fiscal months, series values, and unit;
- formatted values for every non-null bar;
- legend labels;
- explanation, freshness/source, and missing-data notes below the chart;
- theme-aware export palette.

Acceptance:

- The client does not calculate or fill financial values.
- `null` values are preserved and labelled as unreported where necessary.

Completed 2026-07-28:

- Added `createMonthlyTrendChartCardViewModel`, a frontend-only projection of the existing trend
  payload with formatted Persian labels, nullable point values, legends, insight/missing-data
  explanation lines, source/calculation context, and light/dark export palettes.
- Refactored the interactive chart to consume the same title, identity, point, legend, and palette
  data. The model performs presentation formatting only; it does not calculate financial values or
  replace missing values.

---

## [x] Task 5 - Add the Interactive Download Action

Add the accessible `دانلود تصویر` control to the existing trend chart card.

Acceptance:

- The action is available only when chart data exists.
- It has an accessible Persian name, keyboard operation, loading state, and localized failure state.
- It does not alter conversation data or cause another AI/data request.

Completed 2026-07-28:

- Added a keyboard-accessible `دانلود تصویر` action to chart cards that have at least one rendered
  series value, with pending and localized error states.
- The action uses the in-memory chart-card view model only and does not issue another AI or
  financial-data request.

---

## [x] Task 6 - Implement Deterministic PNG Rendering

Render the export from the export-ready chart card/view model at a shareable resolution.

Requirements:

- Include all non-null bar labels, legend, unit, axes/month labels, and below-chart explanation.
- Render the centered, semi-transparent watermark `دستیار هوشمند تحلیل بازار` over the chart plot
  area without obscuring bar values, axes, legend, or the explanatory section.
- Exclude UI controls and hover-only content.
- Use the active light/dark theme with readable contrast.
- Produce a deterministic Persian filename containing symbol and export date.

Acceptance:

- A downloaded image can be understood without opening the original chat.
- Missing values do not become zero bars or numeric labels.
- The watermark is present in every exported image, remains readable in both themes, and does not
  compromise the visibility of financial values.

Completed 2026-07-28:

- Added a browser-only deterministic canvas PNG renderer over the chart-card view model. It draws
  all non-null current/previous-year bar values, average line, Persian month labels, legends, unit,
  explanation/source/calculation context, and the centered watermark `دستیار هوشمند تحلیل بازار`.
- The renderer uses the active light/dark palette, retains null periods as absent bars, and creates
  a Persian filename without sending a second request or serializing unrelated chat content.

---

## [x] Task 7 - Preserve Responsive Interactive Chart Behavior

Keep the normal web chart usable on desktop and mobile while separating dense export-only labels
from the interactive layout when needed.

Acceptance:

- The chart remains readable at narrow widths.
- Export always includes the complete set of required labels regardless of viewport width.
- Existing chart colors, themes, and tooltip behavior remain coherent.

Completed 2026-07-28:

- Made the chart card, height, margins, axes, legend, and bars responsive for narrow screens.
- The interactive bar labels are suppressed by CSS at small widths to prevent overlap; the PNG
  renderer remains independent and always includes every non-null bar label.
- The interactive chart observes the selected light/dark theme and applies the chart-card palette
  to its grid, axes, legend, bars, and line.

---

## [x] Task 8 - Tests: Alias Routing

Add V1, V2, and integration regression coverage for every approved phrase and a representative
symbol.

Acceptance:

- Each phrase yields the same `monthlyActivityTrendResult` as the canonical request.
- Tests prove no fallback to generic metrics, analysis, or LLM-generated output.
- Unknown-symbol behavior remains unchanged.

Completed 2026-07-28:

- Added V1 detector coverage for all seven approved aliases, asserting that each resolves only to
  `MonthlyActivityTrend`; an unresolved symbol continues through the existing governed behavior
  without arbitrary text being treated as a symbol.
- Added V2 endpoint equivalence coverage for each non-canonical alias against the canonical
  `روند فروش ماهانه کهمدا` request. The tests assert byte-for-byte equality of the structured
  `monthlyActivityTrendResult` and zero outer tool-selection calls, preventing generic metric,
  analysis, or model-generated fallbacks.
- Verified with 53 focused unit tests and 13 focused V2 integration tests.

---

## [ ] Task 9 - Tests: Chart Export

Add frontend tests for the chart card and image-export representation.

Coverage:

- download control visibility and accessibility;
- current/previous series value labels;
- unit, title, legend, and bottom explanation inclusion;
- `null` values;
- light and dark theme export styles;
- export error state without loss of the visible chart.

---

## [ ] Task 10 - Verification and Documentation

Verify the feature without changing persistence or Telegram behavior.

Acceptance:

- Focused backend/frontend tests pass.
- Frontend production build passes.
- Existing monthly trend, chat-history, and theme behavior regressions pass.
- Update this story status and the implementation checklist only after all work is complete.
