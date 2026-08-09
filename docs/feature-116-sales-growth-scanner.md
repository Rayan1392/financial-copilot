# Feature 116 — Sales-Growth Symbol Scanner

Feature 116 is the governed list/screening path for symbols whose latest monthly sales have improved against a common comparison period. It is available through the existing `POST /api/ai/v1/query` facade and reuses the validated scanner table contract.

## Default comparison policy

When the user asks for positive sales growth without naming a baseline, the backend uses:

- baseline: `SameMonthPreviousYear`;
- comparison: strict `GrowthPercent > 0`;
- origin: `InferredDefault`;
- target period: the newest common monthly period meeting the configured coverage policy;
- sort: `GrowthPercent` descending, then symbol ascending.

The three governed baselines are:

| User meaning | Baseline | Displayed baseline |
|---|---|---|
| ماه قبل / previous month | `PreviousMonth` | Previous Month Sales |
| ماه مشابه سال قبل / YoY | `SameMonthPreviousYear` | Same Month Previous Year Sales |
| میانگین ۱۲ ماهه قبل | `AveragePrevious12Months` | Average Previous 12 Months Sales |

The average window contains exactly the twelve periods before the target month; the target month itself is never included. Missing, invalid, non-positive, or incomplete baselines are unavailable and do not create rows or infinite growth.

## Governed aliases and parameters

The semantic catalog owns aliases and comparison qualifiers. Common families include:

- sales growth: `sales growth`, `monthly sales growth`, `رشد فروش`;
- same-month previous year: `YoY`, `year over year`, `ماه مشابه سال قبل`, `سال گذشته`;
- previous month: `MoM`, `month over month`, `previous month`, `ماه قبل`;
- average baseline: `12-month average`, `average previous 12 months`, `میانگین ۱۲ ماهه`.

Thresholds are represented as positive, percentage, or multiple semantics. A multiple uses `CurrentSales / BaselineSales`; therefore `2x` is 100% growth. Strict and inclusive operators remain distinct (`>` versus `>=`). Persian/Arabic digits, decimal separators, percent signs, multiplication signs, Arabic letter variants, and ZWNJ spacing are normalized before resolution.

## Supported examples

| Example | Resulting route | Interpretation |
|---|---|---|
| `سهام با رشد فروش بالای ۳۰ درصد` | `screen_stocks` | Same-month previous year, `>30%` |
| `کدوما فروششون بهتر شده؟` | `screen_stocks` | Inferred same-month previous year, positive growth |
| `sales growth بالای 30 درصد` | `screen_stocks` | Mixed-language percentage filter |
| `سهام با رشد فروش حداقل ۱.۵ برابر میانگین ۱۲ ماهه` | `screen_stocks` | Average previous 12 months, `>=1.5x` |
| `رشد فروش ماه قبل` | clarification | Baseline is named but no discovery scope or threshold is supplied |
| `رشد فروش شغدیر` | `lookup_symbol_metrics` | Single-symbol lookup, not a discovery scan |
| `روند فروش ماهانه کچاد` | Feature 077 trend route | Chart/trend request |
| `ترکیب فروش محصولات کچاد` | Feature 075 product-mix route | Product-level mix |
| `رشد سود خالص شرکت‌ها` | generic financial-growth scanner | Net-profit growth, not sales growth |

Prompt text cannot introduce SQL, symbols, formulas, or financial values. Structured parameters are validated by the backend before execution.

## Web and Telegram

Web conversation messages persist the structured scanner response and reload the same ordered rows, values, evidence, and policy metadata. Telegram uses the same validated result, renders compact rows containing symbol/current sales/baseline/growth, and uses replay-safe `sgp1` pagination callbacks. Telegram does not calculate or re-query financial values during rendering.

## Dependencies and non-overlap

- `069` and `070` own symbol-level monthly-sales point/snapshot lookups; Feature 116 owns multi-symbol discovery.
- `075` owns product revenue mix; Feature 116 never treats product composition as symbol sales growth.
- `077` owns monthly production/sales trend and chart requests; Feature 116 returns a ranked scanner table.
- `015`, `072`, `073`, and `074` own semantic definitions, aliases, and direct-period registry behavior; Feature 116 adds no provider-specific alias or calculation source.
- `089` owns the Telegram AI adapter/channel boundary; Feature 116 only supplies its governed scanner result and pagination metadata.

All calculations read persisted normalized data. No synchronous external provider call is made from the AI query path.
