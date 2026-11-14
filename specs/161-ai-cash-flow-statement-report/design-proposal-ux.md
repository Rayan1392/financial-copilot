# Feature 161 — UX Specification (Deliverable F)

Status: Gate A: Approved by the owner on 2026-11-14

Part of [design-proposal.md](design-proposal.md). This specification is meant to leave no visual decision to the implementer. Where it says "as Feature 154", reuse the named constant, class or helper from `src/frontend/src/components/app/balance-sheet-report/` instead of re-creating it. The frontend performs **no financial arithmetic**: every amount, percent, step start/end and state comes from the typed result.

## 1. Principles

- Same visual family as the quarterly income statement (Feature 144) and balance sheet (Feature 154): teal header band `#164e63`, section caption rows `bg-[#d9efed] text-[#164e63]`, Vazirmatn, RTL, light "paper" surface in both app themes (discovery R-28).
- Numbers: `Intl.NumberFormat("fa-IR-u-nu-arabext")`, thousands separators, accounting parentheses for negatives `(۳۵۹٬۱۰۴)`, «۰» for an explicit zero, «—» for not reported/unavailable (Feature 154 formatter `formatBalanceAmount`). Amounts `tabular-nums`, `whitespace-nowrap`. Ratios: «۱۹٫۲۴ برابر»; percent two decimals half away from zero «۷۵٫۴۹٪» (formatted from the backend value).
- Inflow/outflow is conveyed by sign and parentheses first, colour second (never colour alone).
- The verification state is always visible at the top of the report.

## 2. Component hierarchy

```text
CashFlowReport (layout chooser: compact | wide; view: default | quarterly | annual; local state: waterfallMode, expanded sections, showFullReport)
├── CashFlowHeader            title, metadata badges, verification badge, download action
├── CashFlowFocusAnswer       (metric / analysis focus only) sentence(s) + highlighted KPI
├── CashFlowWarnings          error banner (failed) / warning list / info notices / bank notice
├── CashFlowKpiStrip          KPI cards (template-specific set)
├── CashFlowWaterfallChart    (default view)   |  CashFlowTrendChart (quarterly / annual)
├── CashFlowStatementTable    wide: grid table   |  compact: accordion sections
├── CashFlowIndicators        derived indicators with definition tooltips and unavailable reasons
└── CashFlowFooter            source line, generated time, suggested actions (Feature 121)
```

Files: `cash-flow-report-types.ts`, `cash-flow-report-model.ts`, `cash-flow-report.tsx`, `cash-flow-report-wide.tsx`, `cash-flow-report-compact.tsx`, `cash-flow-waterfall-chart.tsx`, `cash-flow-trend-chart.tsx`, `cash-flow-report-image.ts`, tests under `components/app/__tests__/cash-flow-report*.test.tsx` and fixtures `fixtures/cash-flow-161-{falooleh,vbmellat,quarterly,failed}.json`. Layout breakpoint: the Feature 154 constant `BALANCE_SHEET_WIDE_MIN_WIDTH` (768 px), compact when narrower or when `AnswerDensityContext` is compact (`/m`).

## 3. Header

| Element | Content (فلوله fixture) | Style |
|---|---|---|
| Title | «صورت جریان‌های نقدی لوله و ماشین‌سازی ایران (فلوله)» | header band `#164e63`, white, `text-base font-bold` (wide) / `text-[15px]` (compact) |
| Period | «شش‌ماهه منتهی به ۱۴۰۵/۰۶/۳۱» | white/80, `text-sm` |
| Badges | «حسابرسی‌نشده» or «حسابرسی‌شده»; «جداگانه»; «تجدید ارائه‌شده» only when `isRepresented`; «انتشار: ۱۴۰۵/۰۷/…»; «واحد: میلیون ریال» | Feature 154 `statementBadgeText` pill style |
| Source | «منبع: صورت‌های مالی منتشرشده در کدال (طبقه‌بندی کدال)» | footer line, `text-[11px] text-slate-500` |
| Verification badge | see §3.1 | pill at the leading edge of the badge row |
| Download | «دانلود تصویر» (lucide `Download`) | as Feature 154 |

### 3.1 Verification badge

| Status | Label | Colours |
|---|---|---|
| `verified` | «تطبیق‌شده» | `bg-teal-50 text-teal-800 ring-teal-200` |
| `verified_with_warnings` | «تطبیق‌شده با هشدار» | `bg-amber-50 text-amber-900 ring-amber-200` |
| `unverified` | «تطبیق کامل ممکن نیست» | `bg-slate-100 text-slate-700 ring-slate-300` |
| `failed` | «عدم تطابق در جمع‌ها» | `bg-rose-50 text-rose-900 ring-rose-200` |

The badge is a button opening a popover listing each check (R1, R2, V3 operating + investing = item 122, residuals) with expected, actual and difference.

## 4. Desktop layout (wide, ≥ 768 px)

Order, top to bottom, inside one card (`rounded-lg border border-slate-200 bg-white`):

1. Header (§3).
2. Focus answer (metric/analysis only): `bg-[#f0fdfa]` box, sentence `text-[15px] leading-7`, the metric value `font-bold text-[#164e63]`; «نمایش کامل گزارش» toggles the rest (client-side; default collapsed for metric/analysis focus, expanded for report focus).
3. Warnings (§8).
4. KPI strip: CSS grid `grid-cols-3 xl:grid-cols-6`, gap 8 px. Card: label `text-xs text-slate-600`, value `text-lg font-bold tabular-nums`, sub-line with the comparison column value and change («دوره مشابه سال قبل: …»). Negative values in `text-rose-800`. Unavailable KPI shows «—» and a one-line reason.
   - General: OCF, investing, financing, net change, closing cash, FCF.
   - Banking: OCF, investing, financing, net change, FX effect, closing cash.
5. Chart block (`h-[320px]`), title «از مانده ابتدای دوره تا مانده پایان دوره», with the mode toggle (§5.2).
6. Statement table (§6).
7. Indicators (§7).
8. Footer: source, «تهیه‌شده در …», suggested actions.

Maximum card width follows the chat column; the table never scrolls horizontally at ≥ 768 px (label column flexes, wraps to two lines).

## 5. Charts

### 5.1 Waterfall (default view)

Steps come from `waterfall.steps` in order (backend computed):

| Step | Kind | Label | فلوله | وبملت |
|---|---|---|---:|---:|
| 1 | start | مانده ابتدای سال مالی | 6,692,454 | 2,718,638,069 |
| 2 | delta | فعالیت‌های عملیاتی | +6,918,890 | +386,206,543 |
| 3 | delta | فعالیت‌های سرمایه‌گذاری | −359,104 | −43,767,594 |
| 4 | delta | فعالیت‌های تأمین مالی | +9,025,064 | −141,868,091 |
| 5 | subtotal | مانده قبل از اثر نرخ ارز | 22,277,304 | 2,919,208,927 |
| 6 | delta | اثر تغییرات نرخ ارز | 0 (omitted for General when FX is `inferred_zero`) | +617,787,300 |
| 7 | end | مانده پایان دوره | 22,277,304 | 3,536,996,227 |

- Recharts `BarChart` (RTL: first step at the right — `reversed` x-axis, as Feature 154's chart orientation), one `Bar` whose value is the `[from, to]` range from the backend; `Cell` colour by kind/sign: start/subtotal/end `#164e63`; inflow delta `#0f766e`; outflow delta `#be123c`; FX `#b45309`. Dashed connector lines between consecutive bar ends (`ReferenceLine` segments, `#94a3b8`).
- Value labels above bars in Persian digits, compact suffix for the axis only («هزار میلیارد»/«میلیارد» from a fixed scale table); bar labels show the full amount.
- Y axis starts at 0 (no truncated axis). Tooltip: label, signed amount, running balance after the step.
- If R2 failed, the end bar is drawn at the **reported** 125 and a rose `ReferenceDot` marks the gap with the difference in the tooltip; the chart never invents a closing value.
- Hidden accessible table (`sr-only`) with the same steps; `role="img"` with `aria-label` summarising start, total change and end.

### 5.2 Mode toggle

Segmented control «از مانده ابتدای دوره» (default) | «فقط جریان‌ها». "Flows only" starts at 0 and shows operating, investing, financing, net change (subtotal), FX, total change (end) — useful when the opening balance dwarfs the flows (banks). Pure client-side switch between two backend-provided step lists (`waterfall.steps` and `waterfall.flowSteps`).

### 5.3 Trend chart (quarterly / annual views)

Recharts `ComposedChart` as Feature 144: grouped bars for operating `#0f766e`, investing `#be123c`, financing `#2563eb` per column; line for closing cash `#164e63` on a second axis (point-in-time, labelled «مانده پایان دوره»); `connectNulls={false}`; unavailable quarters show a gap and an `x`-axis footnote marker. Metric focus (for example «روند جریان نقد عملیاتی») shows only that series plus a zero reference line.

## 6. Statement table

### 6.1 Default view columns (wide)

| Column | Header | Width |
|---|---|---|
| 1 | «شرح» | flexible, min 260 px |
| 2 | «شش‌ماهه منتهی به ۱۴۰۵/۰۶/۳۱» + badges | 150 px |
| 3 | «دوره مشابه سال قبل» + its period end + badges | 150 px |
| 4 | «تغییر» (amount) | 130 px |
| 5 | «درصد تغییر» (only when base > 0, otherwise «—» with tooltip reason) | 90 px |

Column 3–5 are hidden when the comparison column is unavailable; a single-line notice states why («صورت جریان وجوه نقد دوره مشابه سال قبل موجود نیست»).

### 6.2 Quarterly / annual columns

«شرح» + one column per quarter («فصل ۲ ۱۴۰۵» with «۳ ماهه مستقل» sub-label and a `ytd_difference` info icon) or per fiscal year («سال مالی ۱۴۰۴»), newest at the right edge after the label column (RTL). Up to 8 columns; at 768–1099 px columns beyond 5 move into a horizontally scrollable region **inside** the table only, with the label column sticky (Feature 144 mobile pattern).

### 6.3 Rows and styles

| Section (caption row) | Rows (General) | Style |
|---|---|---|
| «فعالیت‌های عملیاتی» | subtotal «جریان خالص ورود (خروج) نقد حاصل از فعالیت‌های عملیاتی»; of-which «از جمله: مالیات بر درآمد پرداختی» | subtotal: `font-bold bg-[#f1f5f9]`; of-which: indented 16 px, `text-slate-600 text-[13px]` |
| «فعالیت‌های سرمایه‌گذاری» | detail rows in registry order (purchases of PP&E, intangibles, biological assets; disposals; investments; …); residual «سایر اقلام سرمایه‌گذاری (مابه‌التفاوت)»; subtotal «جریان خالص ورود (خروج) نقد حاصل از فعالیت‌های سرمایه‌گذاری» | detail normal; residual italic with info icon («تفاوت جمع کدال با اقلام نمایش‌داده‌شده»); subtotal bold |
| «فعالیت‌های تأمین مالی» | facilities received, principal repaid, interest paid, dividends paid, capital, treasury purchases/sales, bonds…; residual; subtotal | as above |
| «تطبیق وجه نقد» | «خالص افزایش (کاهش) در وجه نقد»; «مانده وجه نقد در ابتدای سال مالی»; «اثر تغییرات نرخ ارز»; «مانده وجه نقد در پایان دوره» | net change bold; closing cash row highlighted as the final total: `bg-yellow-200 font-extrabold text-amber-950` (Feature 144 gross-margin highlight) |

- A row is shown only when at least one column has a reported value (an explicit 0 counts). Exception: in the General template the FX row is hidden when it is 0 or `inferred_zero` in every column; the Banking template always shows it.
- Negative cells: `text-rose-900` with parentheses (no rose fill, to keep long bank rows readable). Missing: «—» `text-slate-400` with a tooltip giving the reason.
- Each row is focusable; its details popover lists source item ids, statement ids, formula, and (for differenced quarters) both YTD operands.
- Bank template: rows 23/24 carry a note icon «طبق طبقه‌بندی کدال در فعالیت‌های تأمین مالی؛ به معنای تغییر سپرده‌ها نیست»; the FX row is always present.

## 7. Indicators

Card list under the table, title «شاخص‌های مشتق از جریان نقد». Each card: label, value, unit, the formula line («جریان نقد عملیاتی − مخارج سرمایه‌ای»), the period column it refers to, and for unavailable indicators the reason text (`non_positive_net_profit` → «سود خالص دوره منفی یا صفر است؛ نسبت معنادار نیست»). General: FCF, CapEx, CapEx coverage, cash conversion, OCF/revenue, net debt cash flow, dividends paid, treasury net, capital transactions. Banking: dividends paid, treasury net, capital transactions, FX share of cash change. Indicators that do not apply to the template are **not rendered at all** (no «—» card for bank FCF); the bank notice explains why.

## 8. States and messages

| State | Presentation |
|---|---|
| `verified` | badge only |
| `verified_with_warnings` | amber list under the header, one line per warning (for example «جریان نقد عملیاتی محاسبه‌شده با رقم گزارش‌شده جدید (قلم ۱۲۸) برابر نیست؛ مبنای نمایش رقم کدال است.») |
| `unverified` | slate notice «برخی اقلام لازم برای تطبیق گزارش نشده است؛ ارقام بدون تأیید تطبیق نمایش داده می‌شوند.» |
| `failed` | rose banner at the top: «جمع اجزای جریان وجوه نقد با ارقام گزارش‌شده تطابق ندارد (اختلاف: ۱۲۳ میلیون ریال). ارقام همان‌گونه که منتشر شده‌اند نمایش داده می‌شوند.»; dependent indicators show «تأییدنشده» |
| Partial column (`codal_totals_missing`, `unsupported_presentation`, `missing_predecessor`) | column header greyed with an info icon; cells «—»; the reason in the header popover |
| Outcome without report (`no_standalone_cash_flow`, `no_statement_for_period`, `company_*`, `conflicting_report_revision`, `ineligible_storage_scale`, `unsupported_request`) | compact message card (no table) with the backend `message` and suggested actions (nearest periods, consolidated suggestion only when `consolidatedAlternativeAvailable`) |
| Bank notice | info box (`bg-sky-50 text-sky-900`): «در بانک‌ها جریان نقد عملیاتی عمدتاً از تغییر سپرده‌ها و تسهیلات اعطایی ناشی می‌شود و با سود خالص یا جریان نقد آزاد شرکت‌های تولیدی قابل مقایسه نیست. این صورت، شاخص‌های نقدینگی نظارتی بانک را نشان نمی‌دهد.» (copy pending OQ-085) |
| Loading / replay | same skeleton as Feature 154 |

## 9. Mobile (compact, `/m` and < 768 px)

Order: header (title wraps; badges in a horizontal wrap; verification badge first) → focus answer → warnings → KPI grid `grid-cols-2` (6 cards; value `text-base`) → chart in a collapsible «نمودار» panel (collapsed by default, Feature 144 mobile pattern; height 240 px; waterfall labels only on start/end bars, others in tooltip-on-tap via `chart-tap.ts`) → statement as **accordion sections** (whole-header `<button aria-expanded aria-controls>` with chevron; operating expanded by default, others collapsed; each row: label on its own line, then «دوره جاری» value and a secondary line with the comparison value and change) → indicators as a vertical list → footer.

- No `overflow-x` on the page at 320, 360, 390 px. Quarterly view on mobile: one card per row metric with the quarters as a vertical list (newest first), not a wide table.
- Touch targets ≥ 44 px; sticky nothing except the existing composer.

## 10. PNG export

Canvas via the shared brand header/footer helper (Feature 154). Width 1200 px (default view), 1400 px (quarterly with > 5 columns); DPR ≥ 2; Vazirmatn. Order: header and badges → verification line → KPI strip → waterfall (current mode) → table (all sections expanded) → indicators → warnings and notices → footer with source and generation time. Labels wrap to two lines; the widest وبملت amounts must fit their columns.

## 11. Text rendering (chat text and Telegram)

`CashFlowReportTextRenderer` emits Persian markdown in the mobile order: title and period; verification line; KPI lines «جریان خالص نقد عملیاتی: ۶٬۹۱۸٬۸۹۰»; sections as «شرح: مبلغ» lines with parentheses for negatives; warnings; bank notice; source. Metric focus emits only the focus sentence, the comparison sentence and the verification line. Same value rules as the screen; below 3,900 characters for the default view of the fixtures.

## 12. Acceptance (UX)

- AC-U01: wide and compact layouts render both fixtures with no horizontal page scroll at 320/360/390/768/1280/1440 px.
- AC-U02: every amount on screen equals the typed value (vitest snapshot of the view model; no arithmetic in components — lint rule/grep test for `+`/`-` on amount fields in `cash-flow-report-model.ts`).
- AC-U03: negative values use parentheses; zero «۰»; not reported «—»; Persian digits.
- AC-U04: waterfall steps match `waterfall.steps` exactly, including the bank FX step; the end bar equals reported 125.
- AC-U05: bank template shows no FCF/CapEx/cash-conversion/OCF-revenue/net-debt card and shows the bank notice.
- AC-U06: the four verification states and each partial/outcome state have a rendered fixture test.
- AC-U07: the report renders identically (fixed light surface) under the app's light and dark themes; contrast ≥ 4.5:1 for text.
- AC-U08: PNG exports for both fixtures (default and quarterly) from desktop and mobile; visual sign-off recorded (task T-F09).
