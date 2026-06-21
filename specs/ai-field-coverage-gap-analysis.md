# AI Field Coverage Gap Analysis - CyclicalWaves Snapshot Fields

## Question

Can AI answer direct user questions for the following fields based on the existing specs, stories,
and implemented tasks?

## Summary

Partially yes. The provider ingestion and `DerivedMetrics` persistence side is mostly covered by
specs `020` and `071`. Direct AI point lookup is covered by spec `045`, and alias centralization is
covered by spec `072`. However, the specs did not fully close the period-aware direct-question gap
for every field in the requested table.

The main missing capability is not data storage; it is user-language routing and exact period
selection. The AI must be able to understand that phrases such as `فصل قبل`, `فصل مشابه سال قبل`,
`ماه قبل`, and `متوسط فروش ۱۲ ماهه سال قبل` are not separate formulas. They are the same canonical
metric with a specific CyclicalWaves relative-period selector.

## Coverage Table

| Field | Data persistence coverage | Direct AI answer coverage before this update | Gap |
| --- | --- | --- | --- |
| `last_quarter_net_profit_margin` | Covered by `020`/`071` as Q0 `NET_PROFIT_MARGIN` | Partial | Needs explicit latest-quarter alias/display regression. |
| `penultimate_quarter_net_profit_margin` | Covered as Q1 `NET_PROFIT_MARGIN` | Gap | Needs previous-quarter period selector. |
| `last_year_same_quarter_net_profit_margin` | Covered as Q4 `NET_PROFIT_MARGIN` | Gap | Needs same-quarter-last-year period selector. |
| `last_quarter_gross_profit_margin` | Covered as Q0 `GROSS_PROFIT_MARGIN` | Partial | Needs explicit latest-quarter alias/display regression. |
| `penultimate_quarter_gross_profit_margin` | Covered as Q1 `GROSS_PROFIT_MARGIN` | Gap | Needs previous-quarter period selector. |
| `last_year_same_quarter_gross_profit_margin` | Covered as Q4 `GROSS_PROFIT_MARGIN` | Gap | Needs same-quarter-last-year period selector. |
| `last_quarter_operating_profit_margin` | Covered as Q0 `OPERATING_PROFIT_MARGIN` | Partial | Needs explicit latest-quarter alias/display regression. |
| `penultimate_quarter_operating_profit_margin` | Covered as Q1 `OPERATING_PROFIT_MARGIN` | Gap | Needs previous-quarter period selector. |
| `last_year_same_quarter_operating_profit_margin` | Covered as Q4 `OPERATING_PROFIT_MARGIN` | Gap | Needs same-quarter-last-year period selector. |
| `average_12_month_sale` | Covered by `070`/`071` as M0 `AVG_12M_MONTHLY_SALES` | Mostly covered | Needs explicit direct single-metric regression in addition to monthly snapshot layout. |
| `last_year_average_12_month_sale` | Covered by `071` as M12 `AVG_12M_MONTHLY_SALES` | Gap | Needs alias and period selector for last-year average. |
| `last_month_sale` | Covered by `070`/`071` as M0 `MONTHLY_SALES` | Covered for general latest sales | Needs explicit direct single-metric regression. |
| `penultimate_month_sale` | Covered by `020`/`071` as M1 `MONTHLY_SALES` | Gap | Needs previous-month period selector. |
| `last_year_same_month_sale` | Covered by `020`/`071` as M12 `MONTHLY_SALES` | Partial | Existing same-period behavior exists, but needs explicit CyclicalWaves M12 direct lookup. |
| `pe` | Covered by `071` as `PE_TTM` passthrough from provider `pe` | Covered | Keep regression coverage. |
| `ps` | Covered by `071` as `PS_TTM` passthrough from provider `ps` | Covered/partial | Ensure PS aliases are as complete as PE aliases. |

## Changes Made In This Zip

1. Added new spec:
   - `073-cyclicalwaves-direct-period-metric-query-coverage/user-story.md`
   - `073-cyclicalwaves-direct-period-metric-query-coverage/tasks.md`

2. Updated existing specs:
   - `045-symbol-metric-point-lookup/user-story.md`
   - `045-symbol-metric-point-lookup/tasks.md`
   - `072-centralize-financial-metric-alias-and-intent-routing-registry/user-story.md`
   - `072-centralize-financial-metric-alias-and-intent-routing-registry/tasks.md`
   - `020-cyclicalwaves-data-provider/user-story.md`
   - `implementation-checklist.md`

## Implementation Intent

Implement spec `073` after `072` because `073` depends on centralized alias/routing behavior. The
agent should not add a parallel calculation system. It should extend the existing symbol lookup,
semantic alias registry, `DerivedMetrics` read path, and renderers.
