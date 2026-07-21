# User Story - AI Full Financial Statement Table Query

## Status
`[ ]` Proposed

## Feature
AI query support for displaying the latest full financial statement table for a company, including income statement, balance sheet, and cash-flow statement, from the configured financial-statement provider.

## Story

As a TahlilApp-AI user,

I want to ask natural-language questions such as "آخرین صورت سود و زیان کگل", "آخرین ترازنامه کگل", or "آخرین جریان وجه نقد کگل",

so that I can see all persisted line items from the latest relevant financial statement in a clean, source-bound table without manually searching the provider reports.

## Business Context

The system already persists normalized financial-statement headers and line items from provider feeds. Feature `081-ai-financial-statement-period-analysis-query` focuses on deterministic analysis and selected governed metrics. Feature `082-noavaran-financial-statement-full-item-and-variant-persistence` expands storage so all vendor statement items and variant flags are available structurally.

This feature adds a direct AI retrieval and rendering path for **full statement tables**. The answer must show all relevant line items from the selected statement, not only summarized metrics.

The provider must be selected from the configured `ProviderName` in `appsettings.json`. Query-time reads must filter by that configured provider and must not hardcode a provider name inside the AI flow.

## User Questions Covered

The user may ask using exact or similar phrases.

### Income statement

- آخرین صورت سود و زیان کگل
- صورت سود و زیان کگل را نشان بده
- آخرین سود و زیان کگل چیست؟
- گزارش سود و زیان ۶ ماهه کگل
- صورت عملکرد کگل
- درآمدها و هزینه‌های کگل در آخرین گزارش

### Balance sheet

- آخرین ترازنامه کگل
- ترازنامه کگل را نشان بده
- آخرین وضعیت مالی کگل
- ترازنامه ۱۲ ماهه حسابرسی‌شده کگل
- دارایی‌ها و بدهی‌های کگل در آخرین ترازنامه

### Cash-flow statement

- آخرین جریان وجه نقد کگل
- صورت جریان وجوه نقد کگل را نشان بده
- جریان نقدی ۹ ماهه کگل
- آخرین صورت جریان نقد کگل

### Variant-specific filters

- آخرین صورت سود و زیان ۳ ماهه کگل
- آخرین ترازنامه ۶ ماهه حسابرسی‌شده کگل
- صورت جریان وجه نقد ۱۲ ماهه حسابرسی‌نشده کگل
- صورت سود و زیان تجدید ارائه شده کگل
- ترازنامه تجدید ارائه نشده کگل
- آخرین صورت سود و زیان تلفیقی کگل
- آخرین صورت سود و زیان غیرتلفیقی کگل

## Scope

### In Scope

- Detect statement-table intent from natural-language user questions.
- Resolve company symbol/name using existing company resolution flow.
- Detect statement type:
  - `IncomeStatement`
  - `BalanceSheet`
  - `CashFlow`
- Detect optional period duration:
  - `ThreeMonths`
  - `SixMonths`
  - `NineMonths`
  - `TwelveMonths`
- Detect optional variant filters:
  - audited / unaudited
  - represented / original
  - consolidated / standalone
- Read the configured provider name from application configuration.
- Filter financial statements by configured `ProviderName`.
- Select the latest matching statement header deterministically.
- Return all persisted line items for the selected statement.
- Render income statement and cash-flow statement as standard one-sided tables.
- Render balance sheet in a standard two-sided left/right table layout.
- Include source metadata and variant flags in the response.

### Out of Scope

- Re-ingesting provider data.
- Calling Noavaran/NADPCO APIs at AI query time.
- Creating new financial ratios.
- Recalculating derived metrics.
- Editing existing ingestion mappings unless discovery proves required line items are not persisted.
- Frontend charting.

## Data Source Rules

- Query-time data must come only from persisted normalized financial statements and persisted statement line items.
- The AI flow must filter rows by the configured financial-statement `ProviderName` from `appsettings.json`.
- The configured provider is the source of truth. Do not hardcode `NadpcoApi`, `NoavaranCurrentApi`, or any other provider value in business logic.
- If the configured provider has no matching statement for the resolved company and filters, return a clear missing-data message.
- Do not silently fallback to another provider.

## Statement Selection Policy

Given a resolved company, statement type, configured provider, and optional filters, select exactly one statement header.

### Required filters

- `ProviderName = configured ProviderName`
- resolved company identifier, preferably `ExternalCompanyId` when available
- selected `StatementType`

### Optional filters

Apply only when explicitly present in the user question:

- period duration: 3 / 6 / 9 / 12 months
- audited / unaudited
- represented / original
- consolidated / standalone

### Default variant behavior

- If the user does not ask for consolidated / تلفیقی, use `IsComposing = false` by default.
- If the user explicitly asks for consolidated / تلفیقی, use `IsComposing = true`.
- If the user explicitly asks for standalone / غیرتلفیقی / شرکت اصلی, use `IsComposing = false`.
- Do not fallback from standalone to consolidated unless the user explicitly asked for consolidated.

### Latest ordering

If several rows match the filters, latest means:

1. maximum `AnnouncementDate`; then
2. maximum `PeriodEnd`; then
3. maximum period duration; then
4. deterministic tie-breaker by statement id or provider external report id.

If the user specifies a period duration, choose the latest row for that duration rather than the latest statement across all durations.

## Response Requirements

Every answer must include a compact source header before the table:

- Company symbol and company name
- Statement type in Persian
- Period duration
- Period end date, preferably Jalali when available
- Fiscal year end, preferably Jalali when available
- Announcement date, preferably Jalali when available
- Provider name
- Variant flags:
  - حسابرسی‌شده / حسابرسی‌نشده
  - تجدید ارائه‌شده / اصلی
  - تلفیقی / غیرتلفیقی
- Unit, if available

## Rendering Rules

### Income statement table

Render all selected statement line items in persisted display order.

Required columns:

| ردیف | شرح | مبلغ |
|---|---|---:|

Optional columns when available:

- prior period amount
- period-over-period change
- notes / source unit

Do not display source item identifiers or metric codes in the user-facing financial statement table.

### Cash-flow statement table

Render all selected cash-flow line items in persisted display order.

Required columns:

| ردیف | شرح | مبلغ |
|---|---|---:|

The renderer must preserve signs for cash outflows and avoid converting negative cash-flow rows into absolute values.
Do not display source item identifiers or metric codes in the user-facing cash-flow table.

### Balance sheet two-sided table

If the selected statement type is `BalanceSheet`, render a standard two-sided balance-sheet view.

Required layout:

| دارایی‌ها | مبلغ | بدهی‌ها و حقوق مالکانه | مبلغ |
|---|---:|---|---:|
| دارایی‌های جاری ... | ... | بدهی‌های جاری ... | ... |
| دارایی‌های غیرجاری ... | ... | بدهی‌های غیرجاری ... | ... |
| ... | ... | حقوق مالکانه ... | ... |
| جمع دارایی‌ها | ... | جمع بدهی‌ها و حقوق مالکانه | ... |

Rules:

- The left side contains assets.
- The right side contains liabilities and equity.
- Do not display source item identifiers or metric codes in the user-facing balance-sheet table.
- Preserve provider item titles when a governed classification is unavailable.
- Use governed mapping/classification when available to place rows under assets, liabilities, or equity.
- If a row cannot be classified reliably, place it in an "سایر اقلام" section and add a warning.
- If totals are present, show totals at the bottom.
- If totals are not present, do not invent them unless the product explicitly implements a deterministic total calculation with clear labeling.

## Explainability and Safety Rules

- The response must cite the selected persisted statement metadata, not a different statement variant.
- Do not mix header metadata from one statement with line items from another statement.
- Do not use consolidated data by default.
- Do not merge audited and unaudited rows.
- Do not merge represented and original rows.
- Do not infer missing financial items from similar labels unless governed mapping supports it.
- If the data is unavailable, explain which filters produced no match.

## Acceptance Criteria

### Intent and Parsing

- The AI detects full-statement-table requests for income statement, balance sheet, and cash-flow statement.
- Persian aliases for each statement type are supported.
- Period duration is detected from Persian and numeric phrases such as `۳ ماهه`, `سه ماهه`, `6 ماهه`, `۱۲ ماهه`.
- Audited, unaudited, represented, original, consolidated, and standalone phrases are detected.

### Provider Filtering

- Repository queries filter by the configured `ProviderName` from `appsettings.json`.
- A test proves that the feature does not return data from a different provider when multiple providers contain statements for the same company.

### Statement Selection

- Without a period filter, the latest matching statement is selected by deterministic ordering.
- With a period filter, the latest matching statement for that period is selected.
- Without consolidated wording, `IsComposing = false` is used.
- Explicit consolidated wording selects `IsComposing = true`.

### Table Rendering

- Income statement responses show all persisted line items as a table.
- Cash-flow responses show all persisted line items as a table and preserve negative signs.
- Balance-sheet responses show a two-sided assets vs liabilities/equity table.
- Source metadata is displayed above the table.

### Missing Data

- If no statement exists for the requested filters, the response states:
  - company
  - statement type
  - provider
  - period filter, if any
  - audited/represented/consolidated filters, if any
- The response must not consume credits for unsupported or no-data cases unless existing billing policy says otherwise.

## Suggested Enhancements For Better Product Value

These enhancements are recommended but can be implemented after the first table-query version:

1. **Interactive line-item drill-down**: Allow follow-up questions such as "این عدد نسبت به دوره قبل چقدر تغییر کرده؟" after a full statement table is shown.
2. **Statement comparison mode**: Support side-by-side comparison with previous period or same period last year for all line items.
3. **Quality and anomaly signals**: Highlight unusual changes, negative operating cash flow, equity erosion, debt growth, or major restatements directly above the table.
4. **Exportable response**: Let the frontend export the rendered statement table to Excel/PDF later.
5. **Governed item classification review**: Add admin review for unmapped balance-sheet line items so the two-sided layout becomes more accurate over time.
