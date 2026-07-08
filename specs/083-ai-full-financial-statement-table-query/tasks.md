# Feature 083 - Tasks

## Feature
AI Full Financial Statement Table Query

## Implementation Status

- [x] Implemented 2026-07-08

## Implementation Notes

- Reuses `NormalizedFinancialStatementRow`, `NormalizedFinancialStatementLineItemRow`, `FinancialStatementSourceItemCatalogRow`, and `NormalizedCompanyRow`; no duplicate statement tables were introduced.
- Reads provider configuration from `NoavaranCurrentApi:ProviderName` through `NadpcoApiProviderOptions.ProviderName`.
- Statement selection is read-only and filters persisted rows by configured provider, resolved `ExternalCompanyId`, statement type, period, audited/represented flags, and `IsComposing`; default `IsComposing` is `false`.
- Jalali period/fiscal/announcement metadata is currently read from statement `WarningsJson`; there are no dedicated persisted columns for those fields in the current normalized header row.
- Line-item display order is not persisted as a separate column; table rendering uses deterministic source catalog `SourceItemId`, then metric code, then row id.
- Balance-sheet side classification uses metric code and conservative title fallback because no governed side/category column exists yet.

## Dependencies

- `023-codaldb-financial-statement-ingestion`
- `029-financial-statement-schema-fix`
- `040-nadpco-api-financial-statement-sync`
- `045-symbol-metric-point-lookup`
- `047-microsoft-agent-framework-orchestration-v2`
- `072-centralize-financial-metric-alias-and-intent-routing-registry`
- `074-database-backed-metric-definition-and-alias-registry`
- `081-ai-financial-statement-period-analysis-query`
- `082-noavaran-financial-statement-full-item-and-variant-persistence`

---

## Task 083.1 - Discover Current Statement Storage And Configuration

### Goal
Inspect the current implementation before adding the query path.

### Checklist

- [x] Locate the normalized financial statement header entity/table.
- [x] Locate the normalized financial statement line-item entity/table.
- [x] Confirm the actual persisted fields for:
  - `ProviderName`
  - `ExternalCompanyId` / `CompanyId`
  - `CompanySymbol`
  - `CompanyName`
  - `StatementType`
  - `PeriodType`
  - `PeriodEnd`
  - `JalaliPeriodEnd`
  - `FiscalYearEnd`
  - `JalaliFiscalYearEnd`
  - `AnnouncementDate`
  - `JalaliAnnouncementDate`
  - `IsAudited`
  - `IsRepresented`
  - `IsComposing`
  - source report id / provider external id
- [x] Confirm how line-item display order is persisted.
- [x] Confirm how source item id/title/unit are persisted after spec `082`.
- [x] Locate the `ProviderName` configuration key in `appsettings.json`.
- [x] Confirm whether the configured provider is already exposed through an options class.
- [x] Document any missing persisted fields before implementation.

### Acceptance Criteria

- Implementation notes identify exact reused entities, repositories, and configuration option names.
- No duplicate financial-statement tables are introduced.
- The query feature is proven to read from persisted data only.

---

## Task 083.2 - Add Statement Table Query Contracts

### Goal
Introduce additive application contracts for the new AI result type.

### Suggested Contracts

- `FinancialStatementTableQuery`
- `FinancialStatementTableResult`
- `FinancialStatementTableSource`
- `FinancialStatementTableLineItem`
- `BalanceSheetTableSide`
- `BalanceSheetTableRow`

### Minimum Query Fields

- `CompanyQuery`
- `ResolvedCompanyId` / `ExternalCompanyId`
- `StatementType`
- `PeriodType` nullable
- `IsAudited` nullable
- `IsRepresented` nullable
- `IsComposing` nullable, defaulted later by selection policy
- `ProviderName`
- `Locale`

### Minimum Result Fields

- company symbol
- company name
- selected statement type
- selected period type
- fiscal year end
- period end
- announcement date
- provider name
- variant flags
- unit
- line items
- warnings

### Acceptance Criteria

- Contracts are additive and do not break existing `FinancialStatementAnalysisResult` from feature `081`.
- The result model can represent income statement, cash flow, and two-sided balance sheet.
- The result carries enough source metadata for explainability.

---

## Task 083.3 - Add Intent Detection And Alias Coverage

### Goal
Route full-statement-table questions to a dedicated deterministic intent.

### Implementation Notes

Add a new intent such as:

- `FinancialStatementTableLookup`

Do not overload analytical intent `FinancialStatementPeriodAnalysis` if the user asks to "show" or "display" the statement itself.

### Alias Examples

Income statement:

- `صورت سود و زیان`
- `سود و زیان`
- `صورت عملکرد`
- `درآمد و هزینه`

Balance sheet:

- `ترازنامه`
- `صورت وضعیت مالی`
- `دارایی و بدهی`
- `دارایی‌ها و بدهی‌ها`

Cash flow:

- `جریان وجه نقد`
- `صورت جریان وجوه نقد`
- `جریان نقدی`
- `صورت جریان نقد`

Period aliases:

- `سه ماهه`, `۳ ماهه`, `3 ماهه`
- `شش ماهه`, `۶ ماهه`, `6 ماهه`
- `نه ماهه`, `۹ ماهه`, `9 ماهه`
- `دوازده ماهه`, `۱۲ ماهه`, `12 ماهه`, `سالانه`

Variant aliases:

- audited: `حسابرسی شده`, `حسابرسی‌شده`
- unaudited: `حسابرسی نشده`, `حسابرسی‌نشده`
- represented: `تجدید ارائه شده`, `تجدید ارائه‌شده`
- original: `تجدید ارائه نشده`, `اصلی`
- consolidated: `تلفیقی`, `گروه`
- standalone: `غیرتلفیقی`, `شرکت اصلی`

### Acceptance Criteria

- `آخرین صورت سود و زیان کگل` routes to `FinancialStatementTableLookup` with `IncomeStatement`.
- `آخرین ترازنامه کگل` routes to `FinancialStatementTableLookup` with `BalanceSheet`.
- `آخرین جریان وجه نقد کگل` routes to `FinancialStatementTableLookup` with `CashFlow`.
- Period and variant filters are parsed without relying only on the LLM.
- Existing direct metric and period-analysis intents do not regress.

---

## Task 083.4 - Implement Provider-Aware Statement Selection Policy

### Goal
Select exactly one persisted statement header using configured provider and user filters.

### Selection Rules

1. Read `ProviderName` from the application configuration/options.
2. Resolve company through existing company resolution.
3. Filter by:
   - configured `ProviderName`
   - company id / `ExternalCompanyId`
   - `StatementType`
4. Apply explicit user filters:
   - `PeriodType`
   - `IsAudited`
   - `IsRepresented`
   - `IsComposing`
5. If the user did not mention consolidated/standalone, set `IsComposing = false`.
6. Order by:
   - `AnnouncementDate` descending
   - `PeriodEnd` descending
   - period duration descending
   - deterministic id descending
7. Return the first row.

### Acceptance Criteria

- No provider value is hardcoded in query logic.
- Multiple-provider data does not leak across the configured provider boundary.
- Explicit period filters are respected.
- Explicit audited/represented filters are respected.
- Consolidated data is not returned by default.

---

## Task 083.5 - Add Repository Query For Full Statement Items

### Goal
Load the selected statement header and all persisted line items in a single query path.

### Suggested Repository

- `IFinancialStatementTableRepository`

### Suggested Methods

- `FindLatestStatementAsync(FinancialStatementTableQuery query, CancellationToken ct)`
- `GetStatementLineItemsAsync(statementId, CancellationToken ct)`

### Line Item Requirements

Return at minimum:

- persisted display order
- source item id
- Persian title
- English title, if available
- numeric value
- unit
- metric code, if mapped
- section/group/classification, if available

### Acceptance Criteria

- All persisted vendor line items for the selected statement are returned.
- Unmapped source items are not dropped.
- The query is read-only.
- The repository is efficient for large statements and avoids N+1 loading.

---

## Task 083.6 - Implement Balance-Sheet Classification And Two-Sided Layout

### Goal
Render `BalanceSheet` as assets on one side and liabilities/equity on the other side.

### Classification Sources, In Priority Order

1. governed source-item mapping/classification, if available
2. metric code classification, if available
3. provider section/group metadata, if available
4. conservative title-based fallback only for obvious Persian headings
5. unclassified bucket with warning

### Layout Rules

- Left side: assets.
- Right side: liabilities and equity.
- Preserve provider display order within each side.
- Show totals at the bottom when provider total rows exist.
- Do not invent totals silently.
- Pad the shorter side with empty cells so the table remains readable.

### Acceptance Criteria

- `آخرین ترازنامه کگل` returns a two-sided table.
- Assets are not mixed with liabilities/equity.
- Unclassified rows are still shown and a warning is returned.
- Tests cover rows that are mapped, title-classified, and unclassified.

---

## Task 083.7 - Implement Persian Renderer

### Goal
Render a concise Persian answer with source metadata and table output.

### Header Format

Example:

```text
آخرین صورت سود و زیان کگل - شرکت معدنی و صنعتی گل‌گهر
دوره: ۶ ماهه منتهی به ۱۴۰۴/۰۶/۳۱
سال مالی منتهی به: ۱۴۰۴/۱۲/۳۰
تاریخ انتشار: ۱۴۰۴/۰۷/۲۹
منبع: NoavaranCurrentApi
نوع گزارش: غیرتلفیقی، حسابرسی‌نشده، اصلی
واحد: میلیون ریال
```

### Income Statement / Cash Flow Table Format

```markdown
| ردیف | شرح | مبلغ | شناسه آیتم منبع |
|---|---|---:|---|
| ۱ | درآمدهای عملیاتی | ... | ... |
```

### Balance Sheet Table Format

```markdown
| دارایی‌ها | مبلغ | بدهی‌ها و حقوق مالکانه | مبلغ |
|---|---:|---|---:|
| دارایی‌های جاری | ... | بدهی‌های جاری | ... |
```

### Acceptance Criteria

- Persian labels are clear and financial-domain appropriate.
- Numeric values are formatted consistently with existing product conventions.
- Negative values remain visible.
- Large tables remain readable in chat.
- Warnings are displayed after the table when needed.

---

## Task 083.8 - Wire Into AI Orchestration And API Response Payloads

### Goal
Expose the new structured result through the existing AI conversation flow.

### Checklist

- [x] Add the new intent to the orchestration switch.
- [x] Add use-case invocation after company resolution.
- [x] Add result payload type to V1/V2 response contracts if structured UI payloads are used.
- [x] Ensure the frontend can distinguish:
  - analytical statement result from feature `081`
  - full table statement result from this feature
- [x] Ensure unsupported/missing-data responses are handled consistently with current credit policy.

### Acceptance Criteria

- The API returns a complete answer for all three statement types.
- Existing scanner, direct metric lookup, product mix, and period-analysis flows continue to work.
- Structured result payload is backward compatible.

---

## Task 083.9 - Add Missing Data And Error Handling

### Goal
Return explicit, actionable no-data responses.

### Cases To Cover

- company cannot be resolved
- statement type cannot be detected
- no statement exists for configured provider
- no statement exists for requested period
- no audited/unaudited match
- no represented/original match
- no standalone row exists and the user did not ask for consolidated
- selected statement has no line items

### Acceptance Criteria

- No-data responses include the applied filters.
- The AI does not hallucinate statement data.
- The AI does not recommend checking another provider unless the product explicitly supports provider switching.

---

## Task 083.10 - Tests

### Unit Tests

- intent detection for income statement aliases
- intent detection for balance-sheet aliases
- intent detection for cash-flow aliases
- period parsing for 3/6/9/12 month phrases
- audited / unaudited parsing
- represented / original parsing
- consolidated / standalone parsing
- latest ordering policy
- default `IsComposing = false`
- configured provider filtering
- balance-sheet side classification

### Integration Tests

- multiple providers exist; only configured provider is returned
- latest income statement line items are loaded
- latest balance sheet line items are loaded
- latest cash-flow line items are loaded
- period-specific statement selection
- audited-specific statement selection
- represented-specific statement selection
- selected statement header and line items belong to the same statement id

### End-To-End AI Regression Questions

- `آخرین صورت سود و زیان کگل`
- `صورت سود و زیان ۳ ماهه کگل`
- `آخرین صورت سود و زیان حسابرسی‌شده کگل`
- `آخرین صورت سود و زیان تجدید ارائه شده کگل`
- `آخرین ترازنامه کگل`
- `ترازنامه ۱۲ ماهه حسابرسی‌شده کگل`
- `آخرین جریان وجه نقد کگل`
- `جریان وجه نقد ۹ ماهه کگل`
- `آخرین صورت سود و زیان تلفیقی کگل`
- `آخرین صورت سود و زیان غیرتلفیقی کگل`

### Acceptance Criteria

- Tests prove that all filters are deterministic.
- Tests prove that balance-sheet rendering uses the two-sided layout.
- Tests prove that the feature does not call provider APIs at query time.
- Existing feature `081` tests continue to pass.

---

## Task 083.11 - Documentation And Evaluation Dataset

### Goal
Document the feature and add regression examples for future routing safety.

### Checklist

- [x] Add the feature to `implementation-checklist.md` only if the project convention requires it.
- [x] Add examples to the AI evaluation dataset.
- [x] Document provider configuration dependency.
- [x] Document default standalone behavior.
- [x] Document balance-sheet two-sided rendering rules.

### Acceptance Criteria

- Future agents can understand the difference between statement analysis and full statement table lookup.
- Evaluation prompts cover common Persian aliases and filters.

---

## Future Enhancements - Not In Scope

- Side-by-side comparison with previous period.
- Side-by-side comparison with same period last year.
- Automatic anomaly highlights.
- Excel/PDF export.
- Admin UI for improving balance-sheet source-item classification.
- Follow-up question memory over a selected statement, such as "ردیف سود خالص را با دوره قبل مقایسه کن".
