# Feature 161 — Routing and Regression Matrix (Deliverable E)

Status: Gate A: Approved by the owner on 2026-11-14

Part of [design-proposal.md](design-proposal.md). All rows describe the **V2** path (`AiOrchestration:Mode = MicrosoftAgentFrameworkV2`) with `CashFlowReport:Enabled = true`. With the flag off, every row in §1–§5 must route exactly as today (Feature 083 table for cash-flow aliases, today's route otherwise) — this is itself a test group (§8). V1 is frozen and unchanged in both cases.

Notation: **F161** = `financial_statement_table` capability → `UseCashFlowReport` branch. Form = `view / focus / metricId|analysisTopic / period`. Values are the fixture values in million IRR (not verified live).

## 1. Full reports

| # | Query | Expected route | Form | Expected answer / notes |
|---|---|---|---|---|
| 1 | صورت جریان وجوه نقد فلوله | F161 | default / report | Latest standalone statement (fixture: 6M to 1405/06/31) + same stage previous year column; General template |
| 2 | صورت جریان‌های نقدی فولاد | F161 | default / report | New V2 canonical alias (today unmatched, R-19) |
| 3 | گزارش جریان نقدی وبملت | F161 | default / report | Banking template; «گزارش» must not trigger the quarterly IS path (no quarter cue) |
| 4 | گردش وجوه نقد شگل | F161 | default / report | New alias |
| 5 | صورت جریان نقد فملی در شش ماهه ۱۴۰۵ | F161 | default / report / months 6, FY 1405 | Exact period; miss → `no_statement_for_period` + nearest periods |
| 6 | Cash Flow Statement فولاد | F161 | default / report | English, case-insensitive |
| 7 | صورت جریان وجوه نقد فولاد را بررسی کن | F161 | default / report | PD-27 analogue: plain «بررسی» + one statement alias = report |
| 8 | جدول کامل جریان وجوه نقد فولاد | Feature 083 | — | Full-table cue keeps 083 (unchanged) |
| 9 | صورت جریان وجوه نقد تلفیقی فولاد | Feature 083 | — | Variant cue keeps 083; F161 is standalone only |
| 10 | صورت جریان وجوه نقد فولاد در ۱۴۰۵/۰۶/۳۱ تلفیقی | F161 | outcome `unsupported_request` | Date + 083-only filter: typed message names both (Feature 154 PD-20 analogue), date never dropped |

## 2. Individual metrics

| # | Query | Route | Form | Expected answer (fixture) |
|---|---|---|---|---|
| 11 | جریان نقد عملیاتی فلوله چقدر است؟ | F161 | default / metric / `operating_cash_flow` | «جریان خالص نقد عملیاتی فلوله در شش‌ماهه منتهی به ۱۴۰۵/۰۶/۳۱: ۶٬۹۱۸٬۸۹۰ میلیون ریال» + status «تطبیق‌شده» |
| 12 | مانده وجه نقد وبملت چقدر است؟ | F161 | metric / `closing_cash` | ۳٬۵۳۶٬۹۹۶٬۲۲۷ میلیون ریال at 1405/06/31 (point-in-time wording «در پایان دوره») |
| 13 | فلوله چقدر تسهیلات دریافت کرده؟ | F161 | metric / `facilities_received` (23) | ۲۴٬۶۳۵٬۶۰۰; context sentence with repayments (۱۳٬۹۰۸٬۷۰۲) and net (۱۰٬۷۲۶٬۸۹۸) |
| 14 | خالص جریان نقد تامین مالی فولاد چقدر بوده؟ | F161 | metric / `financing_cash_flow` (123) | Signed value with «ورود/خروج» wording |
| 15 | جریان نقد آزاد کگل چقدر است؟ | F161 | metric / `free_cash_flow` | General: OCF − CapEx with the definition line; bank: typed «برای بانک‌ها محاسبه نمی‌شود» |
| 16 | وبملت چقدر سود سهام نقدی پرداخت کرده؟ | F161 | metric / `dividends_paid` (6) | ۷۶٬۰۷۲٬۹۰۹ میلیون ریال; **not** DPS |
| 17 | مخارج سرمایه‌ای فلوله | F161 | metric / `capex` | ۳۵۹٬۵۳۵ with components (10 reported; 48, 46 not reported) |
| 18 | اثر نرخ ارز بر وجه نقد وبملت | F161 | metric / `fx_effect` | ۶۱۷٬۷۸۷٬۳۰۰; share of total change ۷۵٫۴۹٪ |

## 3. Analytical questions (deterministic facts, no verdicts — D-13)

| # | Query | Route | Topic | Facts produced |
|---|---|---|---|---|
| 19 | آیا سود فلوله پشتوانه نقدی دارد؟ | F161 | `earnings_quality` | OCF, net profit (IS 143 same period), cash conversion (or the reason it is unavailable) |
| 20 | آیا فولاد از عملیات خود نقد تولید می‌کند؟ | F161 | `operating_generation` | OCF sign and amount for the anchor and the same stage last year; quarterly sign count when available |
| 21 | علت افزایش موجودی نقد وبملت چیست؟ | F161 | `cash_change_drivers` | Total change ۸۱۸٬۳۵۸٬۱۵۸ = operating ۳۸۶٬۲۰۶٬۵۴۳ + investing (۴۳٬۷۶۷٬۵۹۴) + financing (۱۴۱٬۸۶۸٬۰۹۱) + FX ۶۱۷٬۷۸۷٬۳۰۰ (۷۵٫۴۹٪ of the change) |
| 22 | آیا کگل برای سرمایه‌گذاری‌هایش به تسهیلات وابسته است؟ | F161 | `financing_dependence` | CapEx, OCF, FCF, net debt cash flow — general only; bank → typed not-applicable sentence |
| 23 | کیفیت سود شگل از نظر جریان نقدی چگونه است؟ | F161 | `earnings_quality` | As row 19 |
| 24 | تحلیل جریان نقد فولاد | F161 | `overview` | D-12/OQ-078: today Feature 081 (item 1); proposed F161 |
| 25 | تحلیل ترازنامه و جریان وجوه نقد فولاد | Today's route (Feature 081) | — | Two statements → unchanged |

## 4. Historical and period comparisons

| # | Query | Route | Form | Notes |
|---|---|---|---|---|
| 26 | روند جریان نقد عملیاتی فولاد در دو سال اخیر | F161 | quarterly / metric `operating_cash_flow` / 8 quarters | «روند» + «N سال» → 4N quarters (max 8); must beat `monthly_activity_trend` (R-20) |
| 27 | جریان نقد فلوله را با سال قبل مقایسه کن | F161 | default / report, changes emphasized | Same stage previous fiscal year; amount and percent (percent only when base > 0) |
| 28 | جریان نقد عملیاتی کگل و کچاد را مقایسه کن | F161 guided response | outcome `unsupported_request` (two companies) | D-14/OQ-080: one suggested action per company; no charge |
| 29 | صورت جریان نقدی وبملت را فصل به فصل نشان بده | F161 | quarterly / report / 4 quarters | Bank standalone quarters, FX per quarter |
| 30 | صورت جریان نقدی فلوله در چهار فصل اخیر | F161 | quarterly / report / 4 | Must not open the quarterly income statement (R-20) |
| 31 | جریان وجوه نقد وبملت در شش‌ماهه ۱۴۰۵ | F161 | default / report / 6, 1405 | |
| 32 | جریان نقد عملیاتی کگل نسبت به سال قبل | F161 | default / metric `operating_cash_flow` + change | |
| 33 | جریان نقد سالانه فولاد در سه سال اخیر | F161 | annual / report / 3 | 12-month statements only |
| 34 | گزارش جریان نقدی فصلی فولاد | F161 | quarterly / report | Today misroutes to the quarterly IS (R-20) |

## 5. Conversational follow-ups

Active marker `;report=feature161_cash_flow_report`; Feature 120 window (20 min, 6 turns, ≥ 0.85 confidence).

| Turn | Message | Expected route | Resolved form |
|---|---|---|---|
| U1 | صورت جریان نقدی فلوله را نشان بده | F161 | فلوله, default / report |
| U2 | همین گزارش را برای فولاد بده | F161 (repeat) | **فولاد**, same view/focus |
| U3 | دوره قبلش چطور بود؟ | F161 (follow-up) | فولاد, anchor = previous available statement date (`previous_report`), default view; notice when its YTD length differs; suggested action «نمایش فصل‌به‌فصل» |
| U4 | با سود خالصش مقایسه کن | F161 (follow-up) | فولاد, same anchor, analysis `earnings_quality` («سود خالص» must not start a profit-metric route while the F161 task is active and the message has no statement alias) |
| U5 | وضعیت تامین مالی آن چطور است؟ | F161 (follow-up) | فولاد, analysis `financing_structure` («وضعیت» must not score `comprehensive_analysis`) |
| U6 | ترازنامه‌اش را هم نشان بده | Feature 154 | فولاد; explicit alias switches statement |
| U7 | سود و زیانش چطور؟ | Today's income-statement route | فولاد; marker dropped |

## 6. Ambiguities

| # | Query | Expected route | Rationale |
|---|---|---|---|
| 35 | جریان نقد فولاد | F161 default report | A cash-flow phrase without a metric is the statement |
| 36 | جریان نقد (no company, no active task) | Existing slot clarification (Feature 120) | Company required |
| 37 | موجودی نقد وبملت | **Today's route** (pinned by a test) | D-15/OQ-082: balance-sheet line vs cash-flow closing cash |
| 38 | وضعیت نقدینگی وبملت / نسبت نقدینگی وبملت | Today's route; never F161 | Liquidity ratios / bank regulatory liquidity are not cash-flow statement values |
| 39 | جریان نقدینگی حقیقی فولاد | Never F161 (whole-word guard, R-20a) | Real money flow |
| 40 | سود نقدی هر سهم فولاد / DPS فولاد | Today's DPS route | Not dividends paid |
| 41 | سود خالص فلوله چقدر است؟ | Today's route | Accounting profit, not OCF |
| 42 | نسبت جاری فولاد | Today's route (Feature 081) | Liquidity ratio |
| 43 | جریان نقد عملیاتی بانک‌ها | Today's route | Industry-wide request; F161 is single-company |

## 7. Existing report regressions (must stay green)

| # | Query | Expected route (unchanged) | Existing guard |
|---|---|---|---|
| 44 | آخرین ترازنامه کگل | Feature 154 report (V2) | `BalanceSheetReport154RoutingTests`, `GoldenDatasets` |
| 45 | ترازنامه وبملت را با پایان سال مالی قبل مقایسه کن | Feature 154 comparison | Feature 154 suites |
| 46 | ترازنامه‌اش (after a 154 answer) | Feature 154 follow-up | `BalanceSheetReport154FollowUpEndpointTests` |
| 47 | گزارش فصلی فولاد / صورت سود و زیان فصلی فولاد | Feature 144 | `QuarterlyFinancialReports144*Tests` |
| 48 | عملکرد ۵ فصل اخیر فولاد / عملکرد فصلی فملی را بررسی کن | Feature 144 | Feature 154 PD-27 tests |
| 49 | جدول صورت سود و زیان فولاد | `financial_statement_table` (today's branch) | `SemanticDialogueGovernance` case `statement-table-fa` |
| 50 | تحلیل ترازنامه شگل / تحلیل صورت مالی فولاد | Feature 081 | Feature 081 tests |
| 51 | روند فروش ماهانه فولاد | `monthly_activity_trend` | Feature 136/150 tests |
| 52 | تحلیل جامع فولاد | Feature 159 route | Feature 159 tests |
| 53 | آخرین اطلاعیه جریان وجه نقد فولاد / اطلاعیه‌های کدال فولاد | `disclosure_listing` | `DisclosureListingIntentRules` tests |
| 54 | ترازنامه و جریان وجوه نقد فولاد | Today's route | Exactly-one-alias rule |
| 55 | Any V1 request (rows 1–54) | V1 unchanged | Feature 154 V1-freeze tests extended to cash-flow phrases |

## 8. Flag-off group

Rows 1–34 and U1–U5 with `CashFlowReport:Enabled = false`: the route and request object equal today's (golden snapshot recorded before implementation in task T-R00).
