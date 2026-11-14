# Feature 161 — Financial Mapping Matrix (Deliverable D)

Status: Gate A: Approved by the owner on 2026-11-14 (amended: operating cash flow = item 128)

Part of [design-proposal.md](design-proposal.md). Evidence ids (E-xx) and findings (R-xx) are in [discovery.md](discovery.md).

Provider `NoavaranCurrentApi`, `StatementType = CashFlow`. Titles are copied from the catalog snapshot `specs/141-schema-aware-semantic-query-engine/SemanticCatalog/source-item-snapshot.json`. Section placement is **never** inferred from the Persian title alone (R-05: the catalog has no hierarchy). An item may be displayed as a detail row only when its placement is verified.

## Legend

**Verification**
- **S** — placement proven by exact subtotal closure in a supplied sample (R-09). The proof is per template: *S-both*, *S-bank*, *S-general*.
- **P** — proposed placement, consistent with revised Standard No. 2; needs the named evidence before it is displayed. Until then the amount is absorbed by the section's residual row and recorded in provenance.
- **X** — excluded by rule (different presentation, aggregate, or consolidated-only).

**Calculation role**: `primary-total` (authoritative Codal value), `derivation-input`, `reconciliation-input`, `cross-check`, `fallback-candidate` (disabled until evidence), `detail`, `metric-component:<metric>`, `presentation-marker`, `none`.

**Report visibility**: `subtotal`, `kpi`, `detail` (shown when present, including an explicit 0), `of-which` (indented component of a total that the report does not decompose fully), `provenance` (never a row; listed in the cell's provenance), `hidden`.

**Industry**: `all`, `general` (non-financial template), `bank`, `consolidated-only`.

## 1. Totals and balances

| SourceItemId | Source title | Section | Codal classification | Calculation role | Visibility | Industry | Verification |
|---:|---|---|---|---|---|---|---|
| 122 | جریان وجه نقد قبل از فعالیت های تامین مالی - کدال | — | Codal total | `cross-check` (V3: `128 + 121 = 122`) | provenance | all | S-both |
| 121 | جریان وجه نقد سرمایه گذاری - کدال | Investing | Codal total | `primary-total`; R1 input | subtotal, kpi | all | S-both |
| 123 | جریان وجه نقد تامین مالی - کدال | Financing | Codal total | `primary-total`; R1 input | subtotal, kpi | all | S-both |
| 124 | خالص افزایش (کاهش) در وجه نقد - کدال | Reconciliation | Codal total (excludes FX, R-10) | `primary-total`; R1 target; R2 input | subtotal, kpi | all | S-both |
| 125 | مانده وجه نقد در پایان سال - کدال | Reconciliation | Codal point-in-time balance | `primary-total`; R2 target | subtotal, kpi | all | S-both |
| 33 | مانده وجه نقد در آغاز سال | Reconciliation | Shared; balance at fiscal-year start | `reconciliation-input` (R2); waterfall start | detail | all | S-both |
| 35 | تاثیر تغییرات نرخ ارز | Reconciliation | Shared; separate from 124 | `reconciliation-input` (R2) under rules FX-1…FX-4 | detail (always shown for bank; shown for general when present and ≠ 0) | all | S-both |
| 120 | جریان وجه نقد بازده سرمایه گذاریها و سود پرداختی بابت تامین مالی - کدال | — | Codal label of the **legacy** five-section layout | `presentation-marker`: ≠ 0 → `unsupported_presentation` | hidden | all | P (E-04) |
| 128 | جریان وجه نقد عملیاتی - جدید | Operating | Operating total (owner decision, Gate A 2026-11-14) | `primary-total`; R1 input | subtotal, kpi | all | S-both (`128 + 121 = 122` in both samples) |
| 126 | جریان وجه نقد سرمایه گذاری - جدید | Investing | New presentation | `fallback-candidate` | provenance | all | P (E-03) |
| 127 | جریان وجه نقد تامین مالی - جدید | Financing | New presentation | `fallback-candidate` | provenance | all | P (E-03) |
| 1 | جریان وجه نقد عملیاتی | — | Legacy (MetricCode `OPERATING_CASH_FLOW`, R-07) | `cross-check` candidate for "cash from operations before tax" only | provenance | all | P (E-05) |
| 58 | جریان وجه نقد بازده سرمایه گذاریها و سود پرداختی بابت تامین مالی | — | Legacy | `none` | hidden | all | X |
| 59 | جریان وجه نقد سرمایه گذاری - قدیم | — | Legacy | `none` | hidden | all | X |
| 60 | جریان وجه نقد قبل از فعالیت های تامین مالی | — | Legacy | `none` | hidden | all | X |
| 61 | جریان وجه نقد تامین مالی - قدیم | — | Legacy | `none` | hidden | all | X |
| 62 | خالص افزایش (کاهش) در وجه نقد | — | Legacy | `none` | hidden | all | X |
| 63 | مانده وجه نقد در پایان سال | — | Legacy | `none` | hidden | all | X |
| 106–113 | مجموع منابع/مصارف بازده سرمایه گذاری ها، سرمایه گذاری ها، تامین مالی؛ مجموع جریانات نقد ورودی/خروجی | — | Aggregates across sections | `none` (double counting) | hidden | all | X |

## 2. Operating activities

| SourceItemId | Source title | Section | Codal classification | Calculation role | Visibility | Industry | Verification |
|---:|---|---|---|---|---|---|---|
| — | ~~Derived operating total `122 − 121`~~ | — | Replaced at Gate A by item 128 | none | — | — | — |
| 9 | مالیات بر درآمد پرداختی | Operating | Revised Standard No. 2: operating | `detail` | of-which | all | P (E-05) |

No other operating component exists in the catalog. The report does **not** show «نقد حاصل از عملیات» (OCF − tax) until E-05 verifies that tax sits inside the Codal operating total; it is then a derived row cross-checked against item 1.

## 3. Investing activities

| SourceItemId | Source title | Codal classification | Calculation role | Visibility | Industry | Verification |
|---:|---|---|---|---|---|---|
| 10 | وجوه پرداختی بابت خرید دارایی‌های ثابت مشهود | Investing | `detail`; `metric-component:capex` | detail | all | S-both |
| 48 | وجوه پرداختی بابت تحصیل دارایی های نامشهود | Investing | `detail`; `metric-component:capex` | detail | all | S-bank |
| 46 | وجوه پرداختی جهت خرید دارایی زیستی مولد | Investing | `detail`; `metric-component:capex` | detail | general | P (E-06, E-02) |
| 14 | وجوه حاصل از فروش دارایی های ثابت مشهود | Investing | `detail` | detail | all | S-both |
| 83 | وجوه دریافتی بابت فروش دارایی های نامشهود | Investing | `detail` | detail | all | P (E-06) |
| 47 | وجوه حاصل از فروش دارایی زیستی مولد | Investing | `detail` | detail | general | P (E-06) |
| 11 | وجوه پرداختی جهت خرید سرمایه‌گذاری‌های بلندمدت | Investing | `detail` | detail | all | P (E-06) |
| 15 | وجوه حاصل از فروش سرمایه گذاری بلند مدت | Investing | `detail` | detail | all | P (E-06) |
| 12 | وجوه پرداختی بابت تحصیل سرمایه‌گذاری‌های کوتاه مدت | Investing | `detail` | detail | all | P (E-06) |
| 17 | وجوه حاصل از فروش سرمایه گذاری های کوتاه مدت | Investing | `detail` | detail | all | P (E-06) |
| 70 | وجوه پرداختی جهت تحصیل شرکتهای فرعی و وابسته | Investing | `detail` | detail | all | P (E-06) |
| 71 | وجوه دریافتی بابت فروش شرکتهای فرعی و وابسته | Investing | `detail` | detail | all | P (E-06) |
| 103 | وجوه پرداختی بابت خرید دارایی های غیر جاری نگهداری شده برای فروش | Investing | `detail` | detail | all | P (E-06) |
| 104 | وجوه دریافتی حاصل از فروش دارایی های غیر جاری نگهداری شده برای فروش | Investing | `detail` | detail | all | S-bank |
| 130 | پرداخت های نقدی برای تحصیل سرمایه گذاری در املاک | Investing (an investment, **not** CapEx) | `detail` | detail | all | P (E-06) |
| 129 | دریافت های نقدی حاصل از فروش سرمایه گذاری در املاک | Investing | `detail` | detail | all | P (E-06) |
| 4 | سود سهام دریافتی | Investing (revised standard) | `detail` | detail | all | S-bank; general P (likely the فلوله +131, R-11) |
| 3 | سود دریافتی بابت سپرده های سرمایه گذاری و اوراق مشارکت بلند مدت | Investing for general (revised standard); for a bank, interest is core operating | `detail` | detail | general; bank X until E-06 | P (E-06) |
| 42 | سود دریافتی بابت تسهیلات مالی | Investing for general; operating for a bank | `detail` | detail | general; bank X until E-06 | P (E-06) — **flag: industry-dependent** |
| 95 | وام نقدی پرداختی به سایر اشخاص | Investing for general; lending is operating for a bank | `detail` | detail | general; bank X | P (E-06) — **flag** |
| 96 | وصول (اقساط / اصل) وام نقدی پرداختی به سایر اشخاص | Investing for general | `detail` | detail | general; bank X | P (E-06) — **flag** |
| 13 | وجوه پرداختی جهت خرید سایر دارایی‌ها و موجودی | Ambiguous ("and inventory") | `detail` only after verification; **never** CapEx | provenance until verified | all | P (E-06) — **flag** |
| — | سایر اقلام سرمایه‌گذاری (مابه‌التفاوت) | **Derived** `121 − Σ displayed investing details` | residual | detail (only when ≠ 0) | all | rule D-08 |

## 4. Financing activities

| SourceItemId | Source title | Codal classification | Calculation role | Visibility | Industry | Verification |
|---:|---|---|---|---|---|---|
| 23 | دریافت تسهیلات مالی | Financing | `detail`; `metric-component:net_debt_cash_flow` (general only) | detail (bank label note) | all | S-both |
| 24 | بازپرداخت اصل تسهیلات مالی دریافتی | Financing | `detail`; `metric-component:net_debt_cash_flow` (general only) | detail (bank label note) | all | S-both |
| 5 | سود پرداختی بابت تسهیلات مالی/ پیش دریافت ها و سایر حساب های پرداختنی | Financing (revised standard) | `detail`; `metric-component:interest_paid` | detail | all | S-both |
| 6 | سود سهام پرداختی | Financing | `detail`; `kpi:dividends_paid` | detail, kpi | all | S-both |
| 117 | وجوه پرداختی بابت خرید سهام خزانه | Financing | `detail`; `metric-component:treasury_net` | detail | all | S-bank |
| 119 | وجوه دریافتی حاصل از فروش سهام خزانه | Financing | `detail`; `metric-component:treasury_net` | detail | all | S-bank |
| 21 | وجوه دریافتی حاصل از افزایش سرمایه/حق تقدم | Financing | `detail`; `metric-component:capital_transactions` | detail | all | P (E-06) |
| 131 | دریافت های نقدی حاصل از صرف سهام | Financing | `detail`; `metric-component:capital_transactions` | detail | all | P (E-06) |
| 40 | وجوه دریافت شده بابت انتشار اوراق مشارکت | Financing | `detail`; `metric-component:net_debt_cash_flow` | detail | all | P (E-06) |
| 41 | باز پرداخت اصل اوراق مشارکت | Financing | `detail`; `metric-component:net_debt_cash_flow` | detail | all | P (E-06) |
| 37 | سود پرداختی بابت اوراق مشارکت | Financing | `detail`; `metric-component:interest_paid` | detail | all | P (E-06) |
| 132 | دریافت های نقدی حاصل از انتشار اوراق خرید دین | Financing | `detail`; `metric-component:net_debt_cash_flow` | detail | all | P (E-06) |
| 133 | پرداخت های نقدی بابت اصل اوراق خرید دین | Financing | `detail`; `metric-component:net_debt_cash_flow` | detail | all | P (E-06) |
| 134 | پرداخت های نقدی بابت سود اوراق خرید دین | Financing | `detail`; `metric-component:interest_paid` | detail | all | P (E-06) |
| 114 | بازپرداخت اوراق اجاره (صکوک) / اجاره سرمایه ای | Financing (mixes sukuk and finance-lease principal) | `detail`; `metric-component:net_debt_cash_flow` | detail | all | P (E-06) — **flag** |
| 135 | پرداخت های نقدی بابت سود اجاره سرمایه‌ای | Financing | `detail`; `metric-component:interest_paid` | detail | all | P (E-06) |
| 22 | وجوه حاصل از افزایش سرمایه شرکت های فرعی - سهم اقلیت | Consolidated-only concept | `none` | provenance | consolidated-only | X (flag if present on a standalone statement) |
| 136–139 | NCI transactions (تحصیل منافع فاقد حق کنترل؛ واگذاری بخشی از شرکت فرعی؛ سهام خزانه شرکت فرعی) | Consolidated-only | `none` | provenance | consolidated-only | X (flag if present on a standalone statement) |
| — | سایر اقلام تأمین مالی (مابه‌التفاوت) | **Derived** `123 − Σ displayed financing details` | residual | detail (only when ≠ 0) | all | rule D-08 |

## 5. Derived rows and metrics (single owner: `CashFlowMetricsCalculator`)

| Id | Persian label | Definition (signed million IRR) | Applicability | Null / denominator rules |
|---|---|---|---|---|
| `operating_cash_flow` | جریان خالص نقد عملیاتی | item `128` | all | 128 not reported → unavailable (`operating_total_missing`, OQ-087); checked against `122 − 121` (rule V3) |
| `cash_before_fx` | مانده نقد قبل از اثر نرخ ارز | `33 + 124` | all | Waterfall subtotal only |
| `total_change_in_cash` | تغییر کل موجودی نقد | `124 + 35` (equals `125 − 33` when R2 holds) | all | Uses the FX value resolved by FX-1…FX-4 |
| `capex` | مخارج سرمایه‌ای نقدی | `−(10 + 48 + 46)`; absent component contributes 0 and is listed as "not reported"; **10 must be reported** (value or explicit 0) | general | 10 not reported → unavailable (`capex_component_missing`). 130 and 13 excluded (OQ-076) |
| `free_cash_flow` | جریان نقد آزاد | `operating_cash_flow − capex` | general only | Either side unavailable → unavailable. **Never** `operating + investing` |
| `capex_coverage` | پوشش مخارج سرمایه‌ای | `operating_cash_flow ÷ capex`, shown as «۱۹٫۲۴ برابر» | general only | `capex ≤ 0` → `zero_capex`; `operating ≤ 0` → `non_positive_operating_cash_flow` |
| `net_debt_cash_flow` | خالص جریان نقد استقراض | `Σ{23, 40, 132}` + `Σ{24, 41, 133, 114}` (repayments are negative) | general only | Only verified components (S/P-closed) are summed; component list shown |
| `interest_paid` | سود پرداختی بابت تأمین مالی | `Σ{5, 37, 134, 135}` | all | — |
| `dividends_paid` | سود سهام پرداختی | `6` | all | Not DPS; never compared to DPS |
| `treasury_net` | خالص معاملات سهام خزانه | `117 + 119` | all (shown when either is reported) | — |
| `capital_transactions` | وجوه حاصل از افزایش سرمایه | `21 + 131` | all | — |
| `cash_conversion` | نسبت تبدیل سود به نقد | `operating_cash_flow ÷ net_profit` (IncomeStatement 143, fallback 313 per Feature 144 approved pair), «برابر» with 2 decimals | general only | `net_profit ≤ 0` → `non_positive_net_profit` (both values still shown); IS missing for the same scope/period → `income_statement_unavailable` |
| `ocf_to_revenue` | نسبت جریان نقد عملیاتی به فروش | `operating_cash_flow ÷ revenue × 100` (IncomeStatement 15) | general only | `revenue ≤ 0` → `non_positive_revenue` |
| `fx_share_of_cash_change` | سهم اثر نرخ ارز از تغییر موجودی نقد | `35 ÷ (124 + 35) × 100` | bank (and general when 35 ≠ 0) | Only when `124 + 35 > 0` and both 124 and 35 are ≥ 0; otherwise amounts only (`mixed_sign_change`) |

Expected values for the supplied datasets are in [design-proposal-draft-plan.md §6](design-proposal-draft-plan.md#6-expected-results-for-the-supplied-datasets).

## 6. Mappings that need accounting verification before display

1. **9** — tax paid inside Codal operating (E-05).
2. **3, 42, 95, 96** — investing for general companies but operating for banks; industry-dependent (E-06).
3. **13** — "other assets and inventory": classification unclear; never CapEx (E-06).
4. **114** — mixes sukuk principal and finance-lease principal; treated as debt repayment (E-06).
5. **130/129** — investment property: investing, excluded from CapEx (OQ-076).
6. **1** — legacy operating item used as `OPERATING_CASH_FLOW` metric elsewhere (R-07, OQ-081).
7. **128** — operating total by owner decision; T01 measures its coverage and `128 + 121 = 122` across Development (E-03, OQ-087). **126/127** stay excluded (OQ-074 (a)).
8. **22, 136–139** — consolidated-only items; presence on a standalone statement is a data warning.
9. **4** for general companies — likely the فلوله residual +131 (R-11).
10. Every **P** item's sign (E-02).
