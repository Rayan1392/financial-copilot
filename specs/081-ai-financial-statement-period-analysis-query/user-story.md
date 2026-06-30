# User Story - AI Financial Statement Period Analysis Query

## Status
`[ ]` Proposed

## Feature
AI query support for latest quarterly/cumulative financial-statement analysis from Noavaran Amin financial statements.

## Story

As a TahlilApp-AI user,

I want to ask natural-language questions about a company's latest 3, 6, 9, or 12 month financial statements,

so that I can quickly understand revenue, gross profit/loss, operating profit/loss, net profit/loss, EPS, margins, balance-sheet strength, leverage, liquidity, ROA, and ROE with a cited source period.

## Business Context

Companies publish financial statements as cumulative 3, 6, 9, and 12 month periods. Each statement has:

- fiscal year end (`FiscalYearEnd` / `JalaliFiscalYearEnd`)
- period end (`PeriodEnd` / `JalaliPeriodEnd`)
- announcement/publication time (`AnnouncementDate` / `JalaliAnnouncementDate`)
- statement type (`IncomeStatement`, `BalanceSheet`, `CashFlow`)
- period duration (`ThreeMonths`, `SixMonths`, `NineMonths`, `TwelveMonths`)
- variant flags:
  - audited / unaudited
  - represented / original
  - composing / non-composing, where composing means consolidated/tلفیقی

The source provider for this feature is **Noavaran Amin / `NadpcoApi` or `NoavaranCurrentApi` financial statements**. The feature must read only persisted normalized data; it must not call the provider at query time.

Feature `029` already separates `StatementType` from `PeriodType`, and feature `040` already normalizes three financial-statement types from NADPCO API:

- `IncomeStatement`
- `BalanceSheet`
- `CashFlow`

This feature adds the AI query flow and deterministic financial analysis layer on top of those persisted statements.

## Source Sample Note

The attached sample for `غالبر` contains six income-statement records:

1. 3 months ended 1404/03/31
2. 6 months ended 1404/06/31
3. 6 months ended 1404/06/31 - consolidated
4. 9 months ended 1404/09/30
5. 12 months ended 1404/12/29
6. 12 months ended 1404/12/29 - consolidated

Important binding rule:

When a response uses values from a non-consolidated statement, the source label must not say consolidated/tلفیقی. When a response uses values from a consolidated statement, the data and source metadata must both come from that same consolidated statement. The AI renderer must never mix line-item values from one variant with source text from another variant.

## User Questions Covered

The user may ask with many different phrases while expecting the same analytical answer.

### General latest statement analysis

- صورت مالی اخیر غالبر چطور بود؟
- آخرین صورت مالی غالبر را تحلیل کن
- وضعیت سود و زیان غالبر در آخرین گزارش چیست؟
- گزارش ۱۲ ماهه غالبر را خلاصه کن
- عملکرد مالی غالبر در آخرین دوره منتشر شده چطور است؟
- سودآوری غالبر بهتر شده یا بدتر؟
- غالبر در آخرین صورت مالی سود ساخته یا زیان؟

### Income-statement focused questions

- درآمد عملیاتی غالبر چقدر تغییر کرده؟
- فروش / درآمد غالبر نسبت به دوره مشابه چقدر رشد یا افت داشته؟
- سود ناخالص غالبر چه تغییری کرده؟
- سود عملیاتی غالبر در گزارش اخیر چقدر است؟
- سود خالص غالبر چقدر شده؟
- EPS غالبر در آخرین صورت مالی چقدر است؟
- زیان هر سهم غالبر چقدر است؟
- حاشیه سود ناخالص، عملیاتی و خالص غالبر را بگو
- آیا حاشیه سود غالبر بهتر شده؟

### Balance-sheet and financial-position questions

- دارایی‌های غالبر نسبت به دوره قبل چقدر تغییر کرده؟
- بدهی‌های غالبر رشد کرده یا کم شده؟
- حقوق مالکانه غالبر چقدر است؟
- نسبت بدهی غالبر چقدر شده؟
- نسبت جاری غالبر بهتر شده یا بدتر؟
- وضعیت ترازنامه غالبر را خلاصه کن

### Return and ratio questions

- ROA غالبر چقدر است؟
- بازده دارایی غالبر بهتر شده یا بدتر؟
- ROE غالبر چقدر است؟
- بازده حقوق صاحبان سهام غالبر نسبت به دوره مشابه چه تغییری کرده؟

### Variant-specific questions

- صورت مالی تلفیقی غالبر را تحلیل کن
- صورت مالی غیرتلفیقی غالبر را بگو
- گزارش حسابرسی‌شده غالبر چیست؟
- صورت مالی حسابرسی نشده غالبر را بررسی کن
- سود تلفیقی غالبر چقدر است؟
- سود شرکت اصلی غالبر چقدر است؟

### Period-specific questions

- گزارش سه ماهه غالبر چطور بود؟
- صورت مالی ۶ ماهه غالبر را تحلیل کن
- عملکرد ۹ ماهه غالبر چطور است؟
- گزارش ۱۲ ماهه غالبر نسبت به سال قبل چه تغییری داشته؟
- آخرین دوره منتشر شده غالبر چیست؟

## Default Query Behavior

If the user does not specify a period, use the latest published financial-statement period for the company from `ProviderName = "NadpcoApi" or "NoavaranCurrentApi"`.

Latest means:

1. maximum `AnnouncementDate`; then
2. maximum `PeriodEnd`; then
3. maximum period duration if there is still a tie.

If the user explicitly asks for 3, 6, 9, or 12 months, select that period duration from the latest fiscal year where such a statement exists.

If both consolidated and non-consolidated statements exist for the selected period:

- If the user explicitly says تلفیقی / consolidated / گروه, use `IsComposing = true`.
- If the user explicitly says غیرتلفیقی / شرکت اصلی / parent / standalone, use `IsComposing = false`.
- If the user does not specify a variant, always use `IsComposing = false`.
- The AI response must not include consolidated/tلفیقی statements by default. Consolidated statements are allowed only when the user explicitly asks for تلفیقی/consolidated.
- If `IsComposing = false` data is unavailable but `IsComposing = true` data exists, do not silently fallback to consolidated data. Return a clear missing-data warning unless the user explicitly asks for consolidated data.

If both audited and unaudited statements exist for the same period and variant:

- If the user explicitly asks حسابرسی‌شده, use audited.
- If the user explicitly asks حسابرسی‌نشده, use unaudited.
- Otherwise use the canonical statement-selection policy, and include audited status in the source metadata.

## Required Analysis Output

The response may include all or a subset of the following, depending on the query intent and available data.

### Income Statement Summary

- Revenue / operating revenue
- Gross profit or gross loss
- Operating profit or operating loss
- Net profit or net loss
- EPS / loss per share
- Gross margin
- Operating margin
- Net margin
- YoY change for each item when the same-length comparable prior fiscal period exists

### Balance Sheet Summary

- Total assets
- Total liabilities
- Total equity
- Current assets
- Current liabilities
- Debt ratio = Total liabilities / Total assets
- Current ratio = Current assets / Current liabilities
- YoY or previous comparable change where available

### Return Metrics

- ROA = Net profit / Total assets
- ROE = Net profit / Total equity
- Compare to the previous comparable period where available

## Response Style Rules

The answer must be deterministic, concise, and source-backed.

Use positive/neutral/negative markers only after numeric direction is computed:

- ✅ for clear improvement
- ⭕️ for clear deterioration
- ⏹️ for neutral/mixed or balance-sheet movement that is not inherently good/bad

Do not use the icon as a substitute for reasoning.

Persian response format example:

```text
⭕️ کاهش 57 درصدی درآمدهای عملیاتی 12 ماهه 1404 در مقایسه با دوره مشابه؛ از 9,801,948 میلیون ریال به 4,170,440 میلیون ریال رسیده است.
⭕️ افزایش زیان ناخالص 12 ماهه 1404؛ از 647,944 میلیون ریال به -204,804 میلیون ریال رسیده است.
✅ افزایش 31 درصدی سود عملیاتی 12 ماهه 1404؛ از 346,086 میلیون ریال به 454,967 میلیون ریال رسیده است.
⭕️ افزایش زیان خالص 12 ماهه 1404 و تحقق زیان 410 ریالی به ازای هر سهم؛ از -6,130 میلیون ریال به -189,548 میلیون ریال رسیده است.
✅ حاشیه سود عملیاتی از 3.53% به 10.91% افزایش یافته است.
⭕️ حاشیه سود خالص از -0.06% به -4.55% کاهش یافته است.

منبع:
صورت‌های مالی 12 ماهه سال مالی منتهی به 1404/12/29 (حسابرسی نشده)
دوره منتهی به: 1404/12/29
زمان انتشار: 1405/04/09 09:23:24
Provider: NadpcoApi or NoavaranCurrentApi
```

Important: default source selection is non-consolidated (`IsComposing = false`). The first source line must say `صورت‌های مالی تلفیقی ...` only when the user explicitly requested consolidated statements and the selected statement has `IsComposing = true`. If the selected statement has `IsComposing = false`, the source label must not include `تلفیقی`.

## Acceptance Criteria

### Intent Detection and Routing

1. The AI detects financial-statement analysis intents from Persian natural language.
2. The intent detector distinguishes financial-statement analysis from monthly activity, price, PE/PS scanner, and product revenue mix intents.
3. The parser extracts:
   - company symbol or company name
   - requested period duration if present
   - statement type focus if present
   - consolidated/non-consolidated preference if present
   - audited/unaudited preference if present
   - metric focus if present
4. Company-name resolution must reuse the existing company resolver and must support both symbol and Persian company name.

### Data Retrieval

1. The query layer reads only persisted normalized statements and line items.
2. It filters `ProviderName = "NadpcoApi" or "NoavaranCurrentApi"`.
3. It uses `StatementType` for `IncomeStatement`, `BalanceSheet`, and `CashFlow`.
4. It uses `PeriodType` for 3/6/9/12 month duration.
5. It respects selected variant flags and never mixes rows from different statement headers/variants.
6. By default it filters to `IsComposing = false`; `IsComposing = true` is selected only for explicit تلفیقی/consolidated queries.
6. It returns missing-data explanations when comparable prior-period data or balance-sheet data is unavailable.

### Financial Calculations

1. YoY comparison uses the same company, same statement type, same period duration, same variant preference, and same fiscal period one fiscal year earlier.
2. Income-statement metrics are cumulative-period metrics; the feature must not treat 6/9/12 month statements as a single quarter.
3. Margins use revenue as denominator.
4. If revenue is zero or missing, margin fields are marked unavailable.
5. ROA and ROE require balance-sheet totals for the same selected period or the nearest matching balance-sheet period according to a documented deterministic rule.
6. Negative values must be rendered correctly as profit/loss; avoid phrases like “افزایش سود” when the value is actually a larger loss.

### Explainability

1. Every response includes source metadata:
   - statement title/variant
   - fiscal year end Jalali
   - period end Jalali
   - announcement Jalali date and time if available
   - audited status
   - consolidated status, which must be `false` by default unless the user explicitly requested consolidated data
   - provider name
2. The response contract includes selected `StatementId` / `ExternalStatementId` internally for traceability, but the UI does not need to display raw IDs by default.
3. If data comes from multiple statement types, source metadata lists each statement type used.

### Performance

1. AI query execution must not call the NADPCO API.
2. Queries must be indexed by company, provider, statement type, period duration, period end, announcement date, and variant flags.
3. The renderer receives a structured deterministic response model, not raw database rows.

### Safety / Financial Advice Boundary

The answer describes financial-statement facts and computed ratios. It must not produce buy/sell recommendations unless another explicit research feature is invoked.

## Out of Scope

- Provider ingestion changes unless missing fields are discovered.
- Forecasting future profitability.
- Valuation or target-price calculation.
- Full deep research across text reports.
- Real-time provider calls.
- UI charting.
