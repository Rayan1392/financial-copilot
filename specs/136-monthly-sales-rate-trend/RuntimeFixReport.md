# Feature 136 — Runtime Fix Report

Date: 2026-10-04

## Status

The code-level runtime defects are fixed and focused test coverage is green. Real-provider
re-ingestion and verification remain blocked because the local environment has no configured
NADPCO credential, so this report does not claim production-data counts.

## Routing root cause and fix

The active V2 `SemanticPrimary` branch returned a generic `monthly_activity_trend` result before
the deterministic product predicate ran. The workflow now computes the validated product slot
before semantic execution and excludes product-trend requests from that generic early return.
ProductRevenueMix and MonthlyProductComparison remain higher precedence. Company-only monthly
trend requests remain on the company path. The parser also accepts real Unicode Persian terms,
in addition to the repository's legacy encoded aliases.

## Product identity root cause and fix

The read repository previously mapped `ProviderProductCode ?? ProductCode`, making a generated,
report-local natural code look like provider identity. It now preserves missing provider code as
missing. Product keys therefore use provider code, positive provider ID, or the deterministic
company-scoped normalized title/unit fallback. Array position and generated line-item codes are
not used for cross-period identity. Ambiguous matches remain typed `Ambiguous` results.

## Persistence root cause and fix

`MessagePersistenceFunction` already accepted and serialized `MonthlyProductTrendResult`, but the
workflow omitted the argument when calling it. The workflow now passes the typed result through
result computation, persistence, API response, conversation reload, and frontend dispatch. The
replay test verifies the result discriminator, resolved product, and points after `GET /messages`.

## Billing test configuration

The diagnosis replay was blocked by `No billable customer account is configured for this actor`.
Focused integration tests use the existing test fixture's `EnsureBillingSeeded()` setup. No billing,
authorization, or production bypass was added.

## Re-ingestion/backfill mechanism

The existing DataAdmin single-company monthly ingestion path was reused and narrowed with an
optional `OutputType` field. It retains deterministic idempotency keys and is safe to re-run.
When `OutputType: 0` is supplied, only ProductSales output type 0 is enqueued; the other report
outputs are untouched.

Exact command for KGL (`ExternalCompanyId=5`) over the diagnosed available history:

```powershell
$body = @{ externalCompanyId = 5; fromShamsiYear = 1404; fromShamsiMonth = 1; toShamsiYear = 1405; toShamsiMonth = 4; outputType = 0 } | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri 'http://localhost:5074/api/v1/admin/noavaran-current/single-company-monthly-ingestion' -Headers @{ 'X-Api-Key' = '<DataAdmin test key>' } -ContentType 'application/json' -Body $body
```

Repeat with `externalCompanyId = 3` for KCHAD. The endpoint is DataAdmin-protected. The commands
were not executed in this session because the local NADPCO provider credential is not configured.

## Backfill counts and resolver verification

| Item | KGL / گندله | KCHAD / آهن اسفنجی |
|---|---:|---:|
| Reports reprocessed | not run | not run |
| Rows rewritten | not run | not run |
| Products resolved after real backfill | blocked | blocked |
| Ambiguous products remaining | not verifiable | not verifiable |
| Failed reports | not run | not run |

The focused V2 fixture verifies each product resolves to one product key with monthly points.
That is test-fixture evidence, not a claim about the external database.

## Exact query evidence

All five focused V2 tests passed: 5/5.

| Query | Expected and observed result |
|---|---|
| `روند فروش کگل` | `MonthlyActivityTrendResult`, company `کگل` |
| `روند فروش گندله کگل` | `MonthlyProductTrendResult`, company `کگل`, product `گندله` |
| `روند فروش ماهانه کچاد` | `MonthlyActivityTrendResult`, company `کچاد` |
| `روند فروش آهن اسفنجی کچاد` | `MonthlyProductTrendResult`, company `کچاد`, product `آهن اسفنجی` |

## Rate verification

The focused persisted fixture contains `productSaleValue = 250` million rial and
`productSaleAmount = 100` tons. The typed product result calculates:

`250 * 100000 / 100 = 250000` تومان per ton.

The unit test also verifies a larger decimal case: `68,209,284 * 100000 / 109,949 =
62,037,202.70` تومان. Both use the calculated value and do not read provider `productSaleRate`.
The real-provider observation requested by the runtime diagnosis remains blocked until backfill.

## Conversation replay and frontend

The product query is persisted, reloaded through `GET /api/ai/v1/conversations/{id}/messages`, and
still contains `MonthlyProductTrendResult`, its product title, and chart points. Frontend typed
dispatch maps the field to `MonthlyProductTrendChart`; the production frontend build passed.

## Focused and regression verification

- Feature 136 unit focus: 13/13 passed after routing coverage was added.
- Targeted ingestion unit focus: 3/3 passed, including output-type-0 scoping.
- V2 exact routing and replay: 5/5 passed.
- Frontend production build: passed.
- Full V2 monthly-sales routing class: 13/15 passed; 2 existing unrelated financial-statement
  value-search classification failures remain.
- Real NADPCO backfill: blocked by missing provider credentials.

## Remaining blockers

1. Configure the supported NADPCO credential and execute the two DataAdmin commands above.
2. Collect real report/row/product/ambiguity/failure counts and one real persisted rate sample.
3. Re-run the four queries against that database and complete production deployment/restart.

FEATURE_136_RUNTIME_FIX_BLOCKED

## Production / Real-Provider Acceptance

Date: 2026-10-04

### 1. NADPCO connectivity

NADPCO authentication succeeded through the registered application token provider. Credentials
were not printed, persisted, or changed. Provider access status: valid.

### 2. Targeted real re-ingestion

The real company catalog maps `کگل` to ExternalCompanyId `4` and `کچاد` to ExternalCompanyId
`3`. The earlier report's `5` value is mapped to `ومعادن`; it was not used as the KGL target after
catalog verification.

The supported OutputType=0 processor job was run for Shamsi `1404/01` through `1405/04`:

| Company | Reports requested | Reports completed | Processed report records | Current accepted normalized rows | Rejected report candidates | Failures |
|---|---:|---:|---:|---:|---:|---:|
| کگل / ExternalCompanyId 4 | 16 | 16 | 16 | 150 | 15 | 0 |
| کچاد / ExternalCompanyId 3 | 16 | 16 | 16 | 234 | 15 | 0 |

The persistence pipeline exposes processed report records rather than a separate insert/update
split; the accepted normalized-row counts above are the post-run persisted counts. No provider
authentication or OutputType=0 processing failures remained. A preliminary attempt using the
incorrect catalog id `5` produced 16 provider invalid-response failures and was excluded from the
acceptance totals.

### 3. Real product identity

- `کچاد + آهن اسفنجی`: `Resolved`, exactly one ProductKey —
  `NoavaranCurrentApi:3:TITLE:آهن اسفنجی|UNIT:تن`.
- `کگل + گندله`: `Ambiguous`, four real candidates remain:
  `گندله`, `گندله ریزدانه`, `گندله ریزدانه صادراتی فروش صادراتی`, and `گندله فروش صادراتی`,
  all with unit `تن` and distinct title/unit ProductKeys.

Per the acceptance stop rule, the KGL product path was not allowed to fall back to company trend.

### 4. Real V2 routing

The `MicrosoftAgentFrameworkV2` orchestration entry was executed against the real database:

| Query | Observed result |
|---|---|
| `روند فروش گندله کگل` | `MonthlyProductTrendResult`, `Ambiguous`, no product selected |
| `روند فروش آهن اسفنجی کچاد` | `MonthlyProductTrendResult`, `Resolved`, product `آهن اسفنجی`, 12 points |
| `روند فروش کگل` | `MonthlyActivityTrendResult`, company `کگل` |
| `روند فروش ماهانه کچاد` | `MonthlyActivityTrendResult`, company `کچاد` |

The two control queries stayed on the company-trend path.

### 5. Real rate calculation

For the real KCHAD product result:

| Fiscal period | Product | Unit | productSaleValue | productSaleAmount | Application SaleRateToman | Independent recalculation | RateStatus |
|---|---|---|---:|---:|---:|---:|---|
| 1404/11 | آهن اسفنجی | تن | 299,991 | 1,360 | 22,058,161.764705882352941176471 | `299991 * 100000 / 1360 = 22,058,161.764705882352941176471` | `ValidRate` |

The application value matched the independent calculation. Provider `productSaleRate` was not
used as authoritative. KGL rate verification was stopped because its product identity remained
ambiguous.

### 6. Chart verification

Not completed. The required stop condition was reached before both products had an unambiguous
ProductKey, so no KGL chart or combined chart acceptance is claimed.

### 7. Conversation replay

Not completed for the acceptance claim for the same reason. The real KCHAD V2 response produced a
persisted conversation, but frontend chart/replay parity was not asserted after the KGL identity
failure.

### 8. Remaining Feature 136 blockers

One real-data defect remains: the product matcher treats the requested KGL title `گندله` as a
substring match and returns four title/unit candidates. A real ProductKey disambiguation decision
is required before KGL product-chart and full Feature 136 acceptance can pass. No production code
was changed to mask this result.

FEATURE_136_REAL_DATA_DEFECT

## Product Chart Presentation / Rendering Fix

Date: 2026-10-04

Scope: presentation-only correction for successful `MonthlyProductTrendResult`. Routing,
ingestion, ProductKey identity, revision selection, and sale-rate calculation were not changed.

### 1. Root causes found

- `MessageList` rendered the successful typed product result and the generated assistant prose/table
  together.
- `MonthlyProductTrendChart` rendered a permanent card for every monthly point, exposing internal
  `RateStatus` enum names.
- Product-chart labels were hard-coded as corrupted UTF-8/Latin-1 text in both the React card and
  canvas exporter.
- The product chart assigned sales value to the left axis and rate to the right axis, contrary to
  the approved Feature 136 presentation.
- The product line had no deterministic point-label layer, and the canvas exporter connected
  non-adjacent valid points across unavailable periods.

### 2. Files changed

- `src/frontend/src/components/app/message-list.tsx`
- `src/frontend/src/components/app/monthly-product-trend-chart.tsx`
- `src/frontend/src/components/app/monthly-product-trend-chart-model.ts`
- `src/frontend/src/components/app/monthly-product-trend-chart-image.ts`
- `src/frontend/src/components/app/__tests__/monthly-product-trend-chart.test.tsx`
- `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs`
- `src/backend/FinancialCopilot.Infrastructure/Authentication/TelegramAssistantResponseRenderer.cs`

No ingestion, identity, or financial-calculation files were changed for this fix. The two backend
changes are presentation-only renderers: successful product trends now emit a concise product
summary instead of duplicate monthly rows and internal rate-status values.

### 3. Successful product-trend response

For a resolved product trend, `MessageList` suppresses the assistant prose and any duplicate typed
table. The typed product chart remains the canonical response. Non-resolved product results retain
their explanatory text and error/ambiguity presentation.

### 4. Fixed monthly detail rows

The permanent month-by-month detail-card grid was removed. Production quantity, sales quantity,
sales value, and calculated rate remain in the typed chart data and are available through the
custom tooltip. Internal values such as `ZeroQuantity`, `ValidRate`, `MissingValue`, and `Overflow`
are not displayed to end users.

### 5. Download and RTL/BiDi

- The browser button now uses valid Persian text: `دانلود تصویر` and `در حال آماده‌سازی…`.
- The exported image uses valid Persian title, axis, legend, rate-label, and filename text.
- The canvas exporter uses RTL canvas direction and numeric bidi isolates; it does not reverse
  Persian strings manually.
- The exported chart carries the same product-only series semantics as the browser chart.

### 6. Title and series semantics

The resolved KCHAD product title is composed as `روند فروش آهن اسفنجی کچاد`, using the company
symbol once. The product chart contains only:

- sales-value bars on the right axis in billion Toman;
- the calculated product sale-rate line on the left axis in Toman per product unit.

There is no company orange average, company-level rate, production series, sales-quantity series,
or extra quantity axis. The legend identifies the line as `نرخ فروش`.

### 7. Rate labels and gaps

Only `ValidRate` and `ValidZeroRate` points receive numeric rate labels. Labels use Persian-number
formatting, alternate deterministic vertical offsets, and top-edge padding. Unavailable points are
represented as null rate values, so the browser line uses `connectNulls={false}` and the canvas
exporter draws separate line segments. Zero-quantity periods remain on the x-axis without a fake
zero-rate point or status text.

### 8. Tooltip

The product tooltip exposes fiscal period, production quantity, sales quantity, sales value in
billion Toman, and calculated sale rate with the product unit. It intentionally omits internal
status enum names.

### 9. Verification

- Focused product-chart tests: **3/3 passed**.
- Targeted ESLint for all changed product-chart files and the focused test: **passed**.
- Frontend production build (client and SSR): **passed**.
- Full frontend Vitest suite: **42/43 passed** across 9 test files. The one failure is the
  pre-existing disclosure receipt-date expectation (`(تهران)`) and is unrelated to Feature 136.
- Full frontend TypeScript check remains blocked by pre-existing workspace errors (router search
  parameter requirements, legacy test contract drift, existing activity-chart `Bar.connectNulls`
  typing). No remaining TypeScript error is reported against the new product-chart files.
- Isolated backend Infrastructure build: **passed** with shared compilation disabled; the normal
  build was initially blocked by existing VBCSCompiler file locks.
- Focused backend Feature 136 unit-test filter: **13/13 passed** in an isolated output directory.
- Browser smoke test: **not completed**. The local Vite server was listening on `127.0.0.1:5173`,
  but the configured computer-use environment exposed no browser (`no browser available`), so a
  screenshot-level visual claim is intentionally not made.
- Telegram rendering smoke: **not completed** in this environment; the shared product chart/export
  code uses the corrected RTL/BiDi and series logic, but no Telegram UI surface was available for
  visual verification.

### 10. Remaining blockers

1. Run the requested real browser smoke query `روند فروش آهن اسفنجی کچاد` in an environment with an
   available browser and verify the rendered chart and downloaded PNG visually.
2. Run the shared Telegram renderer smoke if Telegram acceptance is required.
3. The unrelated disclosure test and existing workspace TypeScript errors remain outside Feature
   136 presentation scope.

## Export localization and RTL rendering fixes

Date: 2026-10-04

### 1. Root causes

- The browser chart and canvas exporter consumed the raw backend fiscal label independently, so
  Latin-digit and reversed month/year forms could reach the UI.
- The exporter filename used only the product title and retained spaces instead of the product-chart
  slug convention with the company symbol.
- The canvas text helper wrapped the whole string in an RTL embedding in addition to numeric
  isolates. That over-constrained the bidi algorithm and could reverse numeric and rate labels.
- The right-axis title was positioned outside the safe right margin, allowing
  `مبلغ فروش (میلیارد تومان)` to be clipped.

### 2. Files changed

- `src/frontend/src/components/app/monthly-product-trend-chart-model.ts`
- `src/frontend/src/components/app/monthly-product-trend-chart-image.ts`
- `src/frontend/src/components/app/__tests__/monthly-product-trend-chart.test.tsx`
- `src/frontend/src/components/app/__tests__/monthly-product-trend-chart-image.test.ts`

No routing, ProductKey resolution, ingestion, ambiguity handling, rate calculation, Telegram
backend, or unrelated production code was changed for this export fix.

### 3. Filename fix

Passed. Product exports now use a safe hyphenated filename containing chart type, product, and
company symbol. The KCHAD example is:
`روند-فروش-آهن-اسفنجی-کچاد.png`.
Duplicate company suffixes are avoided when the product title already contains the symbol.

### 4. Date localization

Passed. Both the browser chart model and canvas exporter normalize `YYYY/MM` and `MM/YYYY` inputs
to the approved `۱۴۰۴/۰۵` form using Persian digits and a two-digit month.

### 5. RTL/BiDi numeric ordering

Passed by focused export coverage. Numeric-only canvas text is rendered in LTR direction after
Persian-digit normalization; mixed Persian text keeps only numeric runs isolated and no longer uses
the conflicting whole-string RTL embedding. No strings are manually reversed.

### 6. Rate-label rendering

Passed. Valid rate labels use the same Persian-number formatter as the browser chart and the export
stroke/fill paths both use LTR numeric direction. Underlying calculated rate values are unchanged.

### 7. Axis-title clipping

Passed by layout and export coverage. The full right-axis title is right-aligned inside the canvas
safe margin, while the left-axis title is placed inside the left margin. Title, subtitle, legend,
period labels, and boundary-adjacent numeric labels remain within the 1800px canvas bounds.

### 8. Browser/export consistency

The browser and exporter now share the period and number formatters. Product-only series semantics,
valid-rate labels, gap handling, title/subtitle semantics, and tooltip behavior remain unchanged.

### 9. Focused tests

- Product presentation and formatter tests: **4/4 passed** across 2 files.
- Targeted ESLint for the product chart, exporter, model, and focused tests: **passed**.

### 10. Build

- Frontend production client build: **passed**.
- Frontend production SSR build: **passed**.

### 11. Real smoke test and remaining blockers

The requested browser smoke test for `روند فروش آهن اسفنجی کچاد` could not be completed in this
environment. The local Vite server was available, but the computer-use surface exposed no browser
(`no browser available`), so downloaded-PNG visual inspection and filename observation were not
possible. Telegram visual smoke was likewise unavailable. No code-level Feature 136 export blocker
remains based on the focused tests and production build.

FEATURE_136_EXPORT_LOCALIZATION_FIXED

## Sale-rate label decimal suppression

Date: 2026-10-04

### Files and formatter changed

- `src/frontend/src/components/app/monthly-product-trend-chart-model.ts`: added
  `formatProductTrendRate`, and routed `formatProductTrendRateLabel` through it.
- `src/frontend/src/components/app/monthly-product-trend-chart.tsx`: applied the rate formatter
  to tooltip rate text and rate-axis ticks; point labels use the shared rate-label formatter.
  Null label values remain absent rather than being coerced to zero.
- `src/frontend/src/components/app/monthly-product-trend-chart-image.ts`: applied the same rate
  formatter to exported rate-axis ticks and both stroke/fill text for rate point labels.
- `src/frontend/src/components/app/__tests__/monthly-product-trend-chart.test.tsx`: updated
  expectations and added display-rounding and precise-model-preservation cases.
- `src/frontend/src/components/app/__tests__/monthly-product-trend-chart-image.test.ts`: updated
  rate-label assertions, checked integer rate ticks, and retained fractional sales-bar coverage.
- `src/frontend/src/components/app/__tests__/monthly-product-trend-rate-display.test.tsx`: added
  rendered point-label, rate-axis, and active-tooltip coverage using the actual chart content.
- This report.

### Display policy and scope

The existing monetary convention is `FinancialNumberFormatter.Whole` using
`MidpointRounding.AwayFromZero`. The new rate-only formatter uses `Intl.NumberFormat("fa-IR",
{ maximumFractionDigits: 0 })`, whose default halfExpand rounding matches that convention.
Both midpoint directions are covered, along with the requested examples:
`22,058,161.7647` displays as `۲۲٬۰۵۸٬۱۶۲`, and `26,976,680.46` as `۲۶٬۹۷۶٬۶۸۰`.

Decimals are suppressed in browser rate line-point labels, visible tooltip rate text, rate-axis
ticks, and exported rate labels/ticks. Other numeric formatting, sales bars, dates, filenames,
axis orientation, and gaps retain their existing behavior.

The current Telegram `RenderMonthlyProductTrend` emits a product/unit summary with no numeric
sale-rate values and does not call this browser canvas exporter; no shared numeric Telegram
rendering path requires changes.

### Verification and precision

- Focused Vitest suite: **10/10 passed** across 3 files.
- Targeted ESLint: **passed**.
- Frontend production client and SSR builds: **passed** (existing bundle-size/SSR import warnings).
- Workspace TypeScript check: existing errors remain in router search parameters, older test
  contracts, and the activity chart. No errors were reported in the modified product-trend files.
- Precision assertions confirm that the typed input and chart model still contain
  `22_058_161.7647` after formatting; formatting returns text only. The export also leaves its
  input unchanged. Domain calculation, API contracts, storage, and persistence were not modified.
- No new browser visual smoke test was run for this display-only change; rendered DOM and canvas
  text-call tests verify the presentation boundaries. Earlier visual acceptance limitations remain.

FEATURE_136_RATE_LABEL_DECIMALS_REMOVED

## Chart density and export legend clarity fixes

Date: 2026-10-04

### Root causes and changed files

The web card reserved chart width both in the chart margins and in large axis gutters, while
Recharts' default legend consumed a separate full row with relatively large markers and text. The
export drew a footer rate note but no paired series key, and its rate-axis caption omitted the
`تومان/` unit prefix.

Files changed for this presentation fix:

- `src/frontend/src/components/app/monthly-product-trend-chart.tsx`
- `src/frontend/src/components/app/monthly-product-trend-chart-image.ts`
- `src/frontend/src/components/app/monthly-product-trend-chart-model.ts`
- `src/frontend/src/components/app/__tests__/monthly-product-trend-rate-display.test.tsx`
- `src/frontend/src/components/app/__tests__/monthly-product-trend-chart-image.test.ts`
- This report.

### Web layout and plot area

Passed. Replaced the default chart legend with a compact 11px HTML series key using small bar/line
markers. Reduced card padding and title/subtitle sizing, reclaimed axis gutters, reduced chart
margins, and increased plot height from 288px to 320px. The focused render test checks a 754px plot
width at 900px chart width and confirms both series key markers and labels.

### Export legend and consistent semantics

Passed. The standalone export now draws a green bar key labeled `مبلغ فروش` and an orange line key
labeled `نرخ فروش (تومان/تن)`. The rate-axis caption uses the same dynamic
`نرخ فروش (تومان/{ProductUnit})` label. Browser legend, axis, and series names use the shared
product-unit-aware series definition. The incorrect `نرخ فروش (تن)` wording is absent.

### Verification

- Focused product chart, export, and rate display tests: **10/10 passed** across 3 files.
- Targeted ESLint: **passed**.
- Frontend production client and SSR build: **passed**.
- Real screenshot smoke for `روند فروش آهن اسفنجی کچاد`: **not completed**. The Computer Use
  browser/UI service reported that its native pipe was unavailable. Export drawing and web layout
  were verified through focused rendered DOM/canvas tests; no visual screenshot claim is made.
- Existing browser/Telegram visual smoke limitations recorded above remain. Telegram uses a text
  summary for this result and has no chart-series renderer that consumes this legend.

FEATURE_136_CHART_DENSITY_AND_LEGEND_FIXED

## Quantity-panel enhancement

Date: 2026-10-04

### Design and files changed

Implemented a synchronized two-panel product trend. The top panel retains the existing sales-value bars and calculated sale-rate line with the established independent value/rate axes. The lower panel uses two color-distinct lines for production quantity and sales quantity, with a shared product-unit scale and the same fiscal-month positions. Point labels are intentionally omitted from the quantity lines to avoid crowding; missing values remain gaps and are not synthesized.

Files changed for this enhancement:

- `src/frontend/src/components/app/monthly-product-trend-chart.tsx`
- `src/frontend/src/components/app/monthly-product-trend-chart-model.ts`
- `src/frontend/src/components/app/monthly-product-trend-chart-image.ts`
- `src/frontend/src/components/app/__tests__/monthly-product-trend-chart.test.tsx`
- `src/frontend/src/components/app/__tests__/monthly-product-trend-chart-image.test.ts`
- `src/frontend/src/components/app/__tests__/monthly-product-trend-rate-display.test.tsx`
- This report.

### Web, tooltip, and export behavior

- Web: separate semantic headings and compact legends distinguish the two panels. Both Recharts panels use the same synchronization id and aligned month data. Only the lower panel shows the shared fiscal-month axis. Its unit is explicitly shown on the quantity axis and in the tooltip.
- Tooltip: the synchronized tooltip resolves the active fiscal month against the common data model and shows production quantity, sales quantity, sales value, and calculated sale rate. Unavailable rate points remain absent from the rate series.
- Export: the PNG uses the same two-panel layout, separate sales/rate and quantity legends, one shared row of fiscal month labels, and two distinct quantity-line colors. Quantities are shown as lines without per-point labels; only the quantity-axis ticks and dates are annotated, keeping the image less dense than putting four metrics into one plot.
- Telegram: it continues to use the existing concise product text renderer; it does not share a chart-image renderer, so no Telegram changes were needed.

### Tests and validation

- Focused product chart, tooltip, display, and canvas-export tests: **11/11 passed** across 3 files. Coverage includes two-panel separation, each panel's labels/series, shared fiscal-month alignment, missing-rate gaps with valid quantities, four-metric tooltip content, and readable two-color quantity export with no dense quantity labels.
- Targeted ESLint: **passed**.
- Frontend production client and SSR build: **passed**. Existing bundle-size and SSR import warnings remain.
- Full frontend suite: **50/51 passed**. The sole failure is the unrelated existing disclosure receipt-date expectation for a `(تهران)` suffix; no disclosure code or test was changed.

### Real visual smoke test and remaining blockers

The requested real-query visual smoke test was attempted against the local frontend. It opened to the account-login screen, and no authenticated session was available. I did not enter credentials or bypass authentication, so a real authenticated query and screenshot-level visual overlap check could not be completed. The implementation is verified by rendered component/tooltip tests and canvas drawing tests; **real-query visual acceptance remains outstanding**. No ingestion or production-data changes were made.

FEATURE_136_QUANTITY_PANEL_ADDED

## Product resolver exact-match precedence fix

Date: 2026-10-04

### 1. Root cause

The product resolver evaluated exact normalized-title, partial/`Contains`, and provider-code
matches as one candidate set. For KCHAD, the query `فولاد` therefore combined the exact product
`فولاد` with the distinct product `فولاد فروش صادراتی` and returned `AmbiguousProduct`.

### 2. Files changed

- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/MonthlyProductTrendQueryUseCase.cs`
- `tests/FinancialCopilot.UnitTests/MonthlyProductTrend136Tests.cs`
- This report.

No ingestion, ProductKey persistence, report revision, rate calculation, chart rendering, or
unrelated routing code was changed.

### 3. Exact-match precedence implemented

Candidate selection now uses deterministic tiers:

1. exact stable provider code or provider ID;
2. exact normalized product title;
3. partial/`Contains` fallback only when no stronger tier has candidates.

Candidates within the winning tier are not auto-selected. Multiple exact ProductKeys therefore
remain `AmbiguousProduct`. Existing Persian/Arabic normalization is reused; no fuzzy matching or
alias infrastructure was introduced.

### 4. Real KCHAD smoke result

The real accepted KCHAD candidate set documented in `KchadSteelResolutionDiagnosis.md` contains
both `فولاد` and `فولاد فروش صادراتی` in the same accepted `1405/06` report. The direct live
PostgreSQL resolver smoke selected `فولاد` for `فولاد` and resolved the longer-title query to
the normalized title `فولاد فروش صادراتی`; neither query returned `AmbiguousProduct`.

Authenticated login to the local API succeeded with the supplied account. However, the subsequent
`/api/ai/v1/query` replay returned `intent=Unknown` with `capability_not_recognized` and did not
enter the Feature 136 product-trend branch. This is a local AI-router smoke blocker, not a
resolver failure; no production data was changed.

### 5. Focused tests

- Feature 136 focused unit filter: **19/19 passed**.
- Covered KCHAD exact-vs-partial precedence, direct long-title resolution, partial-only
  ambiguity, multiple exact ProductKeys, company isolation, and KGL exact-title ambiguity.

### 6. Regression tests

- Full backend unit suite: **1744/1744 passed**.
- This includes the existing MonthlyProductTrend, MonthlyProductComparison, ProductRevenueMix,
  company-scoped resolution, and ambiguity-response coverage.

### 7. Remaining blockers

- The local AI-router path still needs to recognize the Feature 136 query before an end-to-end
  `/api/ai/v1/query` response can be used as the final smoke assertion. The direct live resolver
  path is verified for both KCHAD product queries and preserves the two products independently.

FEATURE_136_PRODUCT_RESOLVER_PRECEDENCE_FIXED
