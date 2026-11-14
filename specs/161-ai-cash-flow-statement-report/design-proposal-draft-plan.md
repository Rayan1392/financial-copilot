# Feature 161 — Test Data and Expected Results

Status: Gate A: Approved by the owner on 2026-11-14 — test data for the plan in [tasks.md](tasks.md) (Gate B approved 2026-11-14)

The task list, phases, test strategy and definition of done that were drafted here before Gate A now live in [tasks.md](tasks.md). This file keeps the synthetic cases and exact expected results that the tasks and the acceptance criteria in [user-story.md](user-story.md) refer to. Section numbers are kept so existing links stay valid.

Operating cash flow is **item 128** (owner decision at Gate A, 2026-11-14). Item 122 is used only for check V3 (`128 + 121 = 122`).

## 4. Synthetic cases

| Case | Input (changes from fixture A unless stated) | Expected |
|---|---|---|
| S-1 | 124 = 15,584,973 (+123) | R1 failed (difference 123) and R2 failed (difference 123); status `failed`; banner; values shown as published |
| S-2 | 35 = 10, 125 unchanged | FX-3 warning `fx_possibly_embedded_in_net_change`; R2 failed by 10 |
| S-3 | 35 not reported, 125 = 22,277,404 | FX-4 `fx_missing_reconciliation_gap` 100; `failed` |
| S-4 | 35 not reported | FX-2 `inferred_zero`; R2 passed; status `verified` |
| S-5 | 120 = 500 | column `unsupported_presentation`; anchor → outcome `unsupported_presentation` |
| S-6 | 122 not reported | V3 `not_evaluated`; R1/R2 pass; status `verified`; report unchanged |
| S-6b | 128 not reported | column `operating_total_missing`; anchor → outcome `operating_total_missing` (OQ-087 (a): no derivation from 122 − 121) |
| S-6c | 123 not reported | `codal_totals_missing` (OQ-074 (a): no fallback to 127) |
| S-7 | 128 = 6,918,891 | OCF shown 6,918,891; R1 failed (6,918,891 − 359,104 + 9,025,064 = 15,584,851 vs 124 = 15,584,850, difference 1); V3 mismatch (6,559,787 vs 122 = 6,559,786); status `failed` |
| S-15 | 122 = 6,559,787 (only 122 changed) | V3 mismatch, warning `ocf_codal_subtotal_mismatch`; R1/R2 pass; status `verified_with_warnings`; OCF 6,918,890 |
| S-8 | Quarterly FY 1405, §5.3 | Q values in §5.3 |
| S-9 | IS 143 = −1,000 | cash conversion `non_positive_net_profit` |
| S-10 | 10 = 0 / 10 not reported | coverage `zero_capex` / capex `capex_component_missing` |
| S-11 | two line items for 123 with different values | cell `ambiguous`; R1 `not_evaluated`; status `unverified` |
| S-12 | two same-rank statements at the anchor date with different 121 | outcome `conflicting_report_revision` |
| S-13 | item 136 present on a standalone statement | warning `consolidated_item_on_standalone` |
| S-14 | fiscal year ends 1405/09/30, statement 6M to 1405/03/31 | fiscal-year label 1405; «شش‌ماهه ۱۴۰۵» selects it |

## 5. Expected results — synthetic and derived

### 5.1 Cross-statement metrics (synthetic IS values; not supplied data)

With fixture A and IncomeStatement 143 = 5,000,000 and 15 = 40,000,000 for the same standalone statement period: cash conversion = 6,918,890 ÷ 5,000,000 = 1.383778 → «۱٫۳۸ برابر»; OCF/revenue = 17.297225 % → «۱۷٫۳۰٪». Without IS data (the supplied fixture): both `income_statement_unavailable`.

### 5.2 Version and scope

A consolidated statement at the same date is ignored; with only consolidated statements → `no_standalone_cash_flow`, suggestion only when `consolidatedAlternativeAvailable`.

### 5.3 Synthetic quarterly fixture (FY 1405, Esfand; cumulative statements)

| YTD | 33 | 128 | 121 | 122 | 123 | 124 | 35 | 125 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 3M (1405/03/31) | 1,000 | 400 | −100 | 300 | 50 | 350 | 0 | 1,350 |
| 6M (1405/06/31) | 1,000 | 750 | −250 | 500 | 150 | 650 | 20 | 1,670 |
| 9M (1405/09/30) | 1,000 | 1,000 | −400 | 600 | 100 | 700 | 20 | 1,720 |
| 12M (1405/12/29) | 1,000 | 1,400 | −500 | 900 | −200 | 700 | 30 | 1,730 |

| Standalone quarter | Operating | Investing | Financing | Net change | FX | Opening | Closing |
|---|---:|---:|---:|---:|---:|---:|---:|
| Q1 | 400 | −100 | 50 | 350 | 0 | 1,000 | 1,350 |
| Q2 | 350 | −150 | 100 | 300 | 20 | 1,350 | 1,670 |
| Q3 | 250 | −150 | −50 | 50 | 0 | 1,670 | 1,720 |
| Q4 | 400 | −100 | −300 | 0 | 10 | 1,720 | 1,730 |

Operating is differenced from item 128; `128 + 121 = 122` holds in every YTD row. Every quarter satisfies O + I + F = net change and opening + net change + FX = closing. Removing the 6M statement makes Q2 and Q3 `missing_predecessor`; closing cash for Q3 still shows 1,720 (balance, never differenced).

## 6. Expected results for the supplied datasets

Amounts in million IRR; computed with exact integers (discovery §3). They are fixtures until T20 confirms them in Development.

### 6.1 Dataset A — فلوله (ExternalCompanyId 255, SixMonths, PeriodEnd 2026-09-22 = 1405/06/31, General template)

| Item | Expected |
|---|---:|
| Operating cash flow (item 128) | **6,918,890** |
| V3: 128 + 121 = 122 | 6,918,890 − 359,104 = 6,559,786 = item 122 ✔ |
| Investing (121) | **−359,104** |
| Financing (123) | **9,025,064** |
| Net change (124); R1 = 6,918,890 − 359,104 + 9,025,064 | **15,584,850** ✔ |
| Opening cash (33) | 6,692,454 |
| FX (35) | 0 (reported zero; row hidden in General, waterfall step omitted) |
| Closing cash (125); R2 = 6,692,454 + 15,584,850 + 0 | **22,277,304** ✔ |
| Verification status | `verified` |
| Operating rows | subtotal 6,918,890; «از جمله: مالیات بر درآمد پرداختی» (561,560) |
| Investing rows | PP&E purchases (359,535); PP&E disposals 300; residual «سایر اقلام سرمایه‌گذاری» **131** (info: unmapped/unsupplied items); subtotal (359,104) |
| Financing rows | facilities received 24,635,600; principal repaid (13,908,702); interest paid (1,457,520); dividends paid (244,314); residual 0 (not shown); subtotal 9,025,064 |
| CapEx (10 only; 48, 46 not reported) | 359,535 |
| Free cash flow | **6,559,355** |
| CapEx coverage | 19.243995… → «۱۹٫۲۴ برابر» |
| Net debt cash flow (23 + 24) | 10,726,898 |
| Interest paid | 1,457,520 (outflow) |
| Dividends paid | 244,314 (outflow) |
| Cash conversion, OCF/revenue | `income_statement_unavailable` with the supplied data |
| Waterfall | 6,692,454 → +6,918,890 → −359,104 → +9,025,064 → subtotal 22,277,304 → end 22,277,304 |
| Flows-only waterfall | 0 → +6,918,890 → −359,104 → +9,025,064 → net change 15,584,850 → total change 15,584,850 |

### 6.2 Dataset B — وبملت (ExternalCompanyId 452, SixMonths, PeriodEnd 2026-09-22 = 1405/06/31, Banking template by GroupId)

| Item | Expected |
|---|---:|
| Operating cash flow (item 128) | **386,206,543** |
| V3: 128 + 121 = 122 | 386,206,543 − 43,767,594 = 342,438,949 = item 122 ✔ |
| Investing (121) | **−43,767,594** |
| Financing (123) | **−141,868,091** |
| Net change (124); R1 = 386,206,543 − 43,767,594 − 141,868,091 | **200,570,858** ✔ |
| Opening cash (33) | 2,718,638,069 |
| Cash before FX (33 + 124) | 2,919,208,927 |
| FX (35) | **617,787,300** (always shown) |
| Closing cash (125); R2 = 2,718,638,069 + 200,570,858 + 617,787,300 | **3,536,996,227** ✔ |
| Total change in cash (124 + 35) | 818,358,158; FX share **75.49 %** |
| Verification status | `verified` |
| Operating rows | subtotal 386,206,543; «از جمله: مالیات بر درآمد پرداختی» (240,267,354) |
| Investing rows | PP&E purchases (43,082,895); intangibles (6,849,443); dividends received 34,008; PP&E disposals 3,095,081; held-for-sale proceeds 3,035,655; residual 0; subtotal (43,767,594) |
| Financing rows | facilities received 1,247,174,422 (bank note); principal repaid (1,285,839,096) (bank note); interest paid (10,578,244); dividends paid (76,072,909); treasury purchases (89,299,066); treasury sales 72,746,802; residual 0; subtotal (141,868,091) |
| Treasury net | −16,552,264 |
| Dividends paid | 76,072,909 (outflow) |
| Not shown | FCF, CapEx, CapEx coverage, cash conversion, OCF/revenue, net debt cash flow |
| Notices | bank notice |
| Waterfall | 2,718,638,069 → +386,206,543 → −43,767,594 → −141,868,091 → subtotal 2,919,208,927 → FX +617,787,300 → end 3,536,996,227 |
