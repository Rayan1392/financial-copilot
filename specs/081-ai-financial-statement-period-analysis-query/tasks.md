# Feature 081 - Tasks

## Feature
AI Financial Statement Period Analysis Query

## Implementation Status

- [x] Added additive application contracts for financial-statement analysis queries/results/source references.
- [x] Added deterministic financial-statement intent rules and V1/V2 routing for `FinancialStatementPeriodAnalysis`.
- [x] Added persisted-statement repository, selection policy, use case, and Persian renderer backed only by normalized `FinancialStatements` and `FinancialStatementLineItems`.
- [x] Added API/conversation payload plumbing for `FinancialStatementAnalysisResult`.
- [x] Added focused unit regressions for intent detection, query parsing, and workflow message contracts.
- [~] Added V2 endpoint regression coverage for default non-consolidated vs explicit consolidated selection; compile is blocked by an unrelated existing integration-test error in `AdminDataOperationsEndpointTests.cs`.

## Reused Entities And Mappings

- Reused `NormalizedFinancialStatementRow` and `NormalizedFinancialStatementLineItemRow` as the only query-time data sources.
- Reused `WarningsJson` evidence emitted by the existing normalizers for `JalaliPeriodEnd`, `JalaliFiscalYearEnd`, `JalaliAnnouncementDate`, `AnnouncementDate`, `IsAudited`, `IsRepresented`, and `IsComposing`.
- Reused governed statement metric codes already present in persisted line items: `REVENUE`, `GROSS_PROFIT`, `OPERATING_PROFIT`, `NET_PROFIT`, `EPS`, and `TOTAL_EQUITY`.
- Current implementation can also read governed balance-sheet codes `TOTAL_ASSETS`, `TOTAL_LIABILITIES`, `CURRENT_ASSETS`, and `CURRENT_LIABILITIES` when they exist in persisted line items; NADPCO source-item coverage for those rows still needs a dedicated mapping pass if the provider feed does not already emit them through archive/current imports.
- Current implementation depends on whatever statement variants survived ingestion. Separate
  persistence of same-period `IsComposing=false` and `IsComposing=true` rows is a prerequisite for
  complete standalone-vs-consolidated query correctness and is owned by spec `082`.

## Dependencies

- `029-financial-statement-schema-fix`
- `040-nadpco-api-financial-statement-sync`
- `082-noavaran-financial-statement-full-item-and-variant-persistence`
- `045-symbol-metric-point-lookup`
- `047-microsoft-agent-framework-orchestration-v2`
- `072-centralize-financial-metric-alias-and-intent-routing-registry`
- `074-database-backed-metric-definition-and-alias-registry`
- `009-explainable-results`
- `010-usage-metering-and-billing-readiness`

---

## Task 081.1 - Discover Current Financial Statement Data Model

### Goal
Inspect the current codebase and database schema before implementation.

### Checklist

- [ ] Locate normalized statement entity/table.
- [ ] Locate normalized statement line-item entity/table.
- [ ] Confirm actual field names for:
  - ProviderName
  - ExternalCompanyId / CompanyId
  - CompanySymbol
  - StatementType
  - PeriodType
  - PeriodStart
  - PeriodEnd
  - JalaliPeriodEnd
  - FiscalYearEnd
  - JalaliFiscalYearEnd
  - AnnouncementDate
  - JalaliAnnouncementDate
  - IsAudited
  - IsRepresented
  - IsComposing
  - SourceEvidenceJson / WarningsJson
- [ ] Locate NADPCO income-statement item mappings.
- [ ] Locate NADPCO balance-sheet item mappings.
- [ ] Locate cash-flow item mappings if already available.
- [ ] Locate existing metric definitions and aliases.
- [ ] Verify whether balance-sheet fields needed for ratios are already mapped.
- [ ] Document missing item mappings before writing calculation code.

### Acceptance Criteria

- Implementation notes identify the exact reused entities and repositories.
- No duplicate raw financial-statement table is introduced.
- If required balance-sheet item mappings are missing, add them through the governed mapping path, not runtime title matching.

---

## Task 081.2 - Add Query Contracts

### Goal
Create API-safe application contracts for financial-statement analysis.

### Suggested Contracts

- `FinancialStatementAnalysisQuery`
- `FinancialStatementAnalysisResponse`
- `FinancialStatementAnalysisSection`
- `FinancialStatementMetricComparison`
- `FinancialStatementSourceReference`
- `FinancialStatementVariantPreference`
- `FinancialStatementPeriodPreference`
- `FinancialStatementMetricFocus`

### Required Query Fields

- `string? SymbolOrCompanyName`
- `Guid? CompanyId`
- `int? PeriodMonths` with allowed values `3`, `6`, `9`, `12`
- `FinancialStatementType? StatementTypeFocus`
- `FinancialStatementVariantPreference VariantPreference`
- `bool? IsAuditedPreference`
- `IReadOnlyList<string>? MetricFocusCodes`
- `bool IncludeBalanceSheetSummary`
- `bool IncludeReturnMetrics`
- `bool IncludeSourceDetails`

### Required Response Fields

- `CompanySymbol`
- `CompanyName`
- `SelectedPeriodMonths`
- `JalaliPeriodEnd`
- `JalaliFiscalYearEnd`
- `SelectedVariant`
- `SelectedAuditedStatus`
- `SummaryBullets`
- `Metrics[]`
- `SourceReferences[]`
- `Warnings[]`
- `ConfidenceScore`
- `GeneratedAtUtc`

### Acceptance Criteria

- Contracts are serializable.
- Contracts separate numeric facts from Persian prose.
- Source references can represent one or more statement types.
- Internal statement IDs are traceable without forcing UI display.

---

## Task 081.3 - Add Financial Statement Intent and Alias Coverage

### Goal
Route Persian user questions to the new financial-statement analysis path.

### New Intent

- `FinancialStatementPeriodAnalysis`

### Alias Groups

#### General statement analysis

- صورت مالی
- صورتهای مالی
- گزارش مالی
- گزارش ۳ ماهه
- گزارش سه ماهه
- گزارش ۶ ماهه
- گزارش شش ماهه
- گزارش ۹ ماهه
- گزارش نه ماهه
- گزارش ۱۲ ماهه
- گزارش سالانه
- آخرین گزارش
- آخرین صورت مالی

#### Income statement aliases

- سود و زیان
- درآمد عملیاتی
- فروش خالص
- درآمد
- سود ناخالص
- زیان ناخالص
- سود عملیاتی
- زیان عملیاتی
- سود خالص
- زیان خالص
- EPS
- سود هر سهم
- زیان هر سهم
- حاشیه سود
- حاشیه عملیاتی
- حاشیه خالص

#### Balance sheet aliases

- ترازنامه
- دارایی
- بدهی
- حقوق مالکانه
- حقوق صاحبان سهام
- نسبت بدهی
- نسبت جاری
- نقدینگی

#### Return metric aliases

- ROA
- بازده دارایی
- بازده دارایی‌ها
- ROE
- بازده حقوق صاحبان سهام
- بازده حقوق مالکانه

#### Variant aliases

- تلفیقی
- consolidated
- گروه
- غیرتلفیقی
- اصلی
- شرکت اصلی
- standalone
- parent
- حسابرسی شده
- حسابرسی‌شده
- حسابرسی نشده
- حسابرسی‌نشده

### Acceptance Criteria

- Intent detection works for symbol queries and company-name queries.
- The detector does not route product-revenue-mix questions to this feature.
- The detector does not route monthly production/sales questions to this feature.
- Regression examples are added for all question families in the user story.

---

## Task 081.4 - Implement Statement Selection Service

### Goal
Select the exact statement headers that will be used as the source of truth.

### Suggested Service

- `IFinancialStatementSelectionService`
- `FinancialStatementSelectionService`

### Selection Inputs

- company
- provider name: always `NadpcoApi` or `NoavaranCurrentApi`
- period preference
- statement type focus
- variant preference
- audited preference
- latest-published behavior

### Selection Rules

1. Filter by `ProviderName = "NadpcoApi" or "NoavaranCurrentApi"`.
2. Filter by company.
3. Filter by requested statement type when present.
4. If no period is specified, choose latest published period:
   - max `AnnouncementDate`
   - then max `PeriodEnd`
   - then max period duration
5. If period duration is specified, choose latest fiscal year/period with that duration.
6. Apply variant preference:
   - explicit consolidated -> `IsComposing = true`
   - explicit non-consolidated -> `IsComposing = false`
   - no variant specified -> `IsComposing = false`
   - do not fallback from non-consolidated to consolidated unless the user explicitly requested consolidated data or a future documented product policy explicitly allows it with a warning
7. Apply audited preference:
   - explicit audited -> `IsAudited = true`
   - explicit unaudited -> `IsAudited = false`
   - otherwise use canonical policy
8. For comparison, select same company, same statement type, same period duration, same variant/audit policy, prior fiscal year.

### Acceptance Criteria

- Same-period consolidated and non-consolidated statements never get mixed.
- Default selection uses non-consolidated statements (`IsComposing = false`).
- Consolidated statements (`IsComposing = true`) are selected only when the user explicitly asks for تلفیقی/consolidated.
- Source labels match selected variant flags.
- If no non-consolidated statement exists for the requested period and the user did not ask for consolidated data, return a clear missing-data warning instead of silently using consolidated data.
- Unit tests cover latest-period tie-breakers and all variant preferences, including default non-consolidated behavior.

---

## Task 081.5 - Implement Financial Statement Repository

### Goal
Create efficient read methods for selected statements and line items.

### Suggested Interface

`IFinancialStatementAnalysisRepository`

### Required Methods

- `GetLatestAvailableStatementAsync`
- `GetStatementsForSelectionAsync`
- `GetStatementLineItemsAsync`
- `GetComparablePriorStatementAsync`
- `GetBalanceSheetForPeriodAsync`
- `GetAvailablePeriodsAsync`

### Index Review

Verify or add indexes for:

- `(ProviderName, ExternalCompanyId, StatementType, PeriodType, PeriodEnd)`
- `(ProviderName, ExternalCompanyId, AnnouncementDate)`
- `(ProviderName, ExternalCompanyId, IsComposing, IsAudited, IsRepresented)`
- line items by `(StatementId, MetricCode)`

### Acceptance Criteria

- Repository uses cancellation tokens.
- Query never loads all statements into memory for large universes.
- Query uses metric codes, not Persian title matching.

---

## Task 081.6 - Add Metric Coverage for Required Line Items

### Goal
Ensure all required metrics are available through governed mappings.

### Income Statement Metric Codes

Required:

- `REVENUE` or `TOTAL_REVENUE`
- `GROSS_PROFIT`
- `OPERATING_PROFIT`
- `NET_PROFIT`
- `EPS`

Optional:

- `FINANCE_COSTS`
- `EBIT`
- `EPS_CONSOLIDATED`

### Balance Sheet Metric Codes

Required if balance-sheet analysis is enabled:

- `TOTAL_ASSETS`
- `TOTAL_LIABILITIES`
- `TOTAL_EQUITY`
- `CURRENT_ASSETS`
- `CURRENT_LIABILITIES`

### Cash Flow Metric Codes

Optional for future expansion:

- `OPERATING_CASH_FLOW`
- `INVESTING_CASH_FLOW`
- `FINANCING_CASH_FLOW`
- `FREE_CASH_FLOW` if derivable

### Acceptance Criteria

- Missing mappings are registered through the existing metric-definition / item-map mechanism.
- No runtime `itemTitle.Contains(...)` logic is introduced.
- Tests verify that unknown item IDs are ignored or reported according to existing ingestion policy.

---

## Task 081.7 - Implement Deterministic Calculation Service

### Goal
Calculate statement comparisons and ratios without LLM reasoning.

### Suggested Service

- `IFinancialStatementAnalysisCalculator`
- `FinancialStatementAnalysisCalculator`

### Calculations

#### YoY Change

`ChangePercent = (Current - Comparable) / Abs(Comparable) * 100`

Rules:

- If comparable is null, mark unavailable.
- If comparable is zero, mark percent unavailable and show absolute change only.
- Use sign-aware wording for losses.

#### Margins

- `GrossMargin = GrossProfit / Revenue * 100`
- `OperatingMargin = OperatingProfit / Revenue * 100`
- `NetMargin = NetProfit / Revenue * 100`

#### Balance Sheet Ratios

- `DebtRatio = TotalLiabilities / TotalAssets * 100`
- `CurrentRatio = CurrentAssets / CurrentLiabilities`

#### Return Metrics

- `ROA = NetProfit / TotalAssets * 100`
- `ROE = NetProfit / TotalEquity * 100`

### Acceptance Criteria

- Negative profit/loss values are rendered correctly.
- Margin calculations are unavailable when denominator is zero/missing.
- ROA/ROE are unavailable when balance-sheet denominator is zero/missing.
- Unit tests cover positive-to-negative, negative-to-more-negative, negative-to-less-negative, zero-comparable, and missing-comparable scenarios.

---

## Task 081.8 - Implement Use Case

### Goal
Combine parsing, selection, retrieval, calculation, and structured response creation.

### Suggested Use Case

`FinancialStatementAnalysisQueryUseCase`

### Flow

1. Resolve company from symbol or company name.
2. Parse period/variant/audit/metric focus.
3. Select source statements from persisted `NadpcoApi` or `NoavaranCurrentApi` statements.
4. Load current line items.
5. Load comparable prior-period statement when needed.
6. Load matching balance-sheet statement when balance-sheet or return metrics are requested.
7. Calculate comparisons and ratios.
8. Build structured response with source references and warnings.
9. Return to AI renderer.

### Acceptance Criteria

- Missing comparable period does not fail the request.
- Missing balance-sheet data does not fail income-statement analysis.
- The result includes enough structure for deterministic Persian rendering.

---

## Task 081.9 - Add Persian Renderer

### Goal
Render concise Persian answers with correct financial wording and source citation.

### Renderer Rules

- Use `میلیون ریال` for statement amounts unless evidence indicates another scale.
- Use `ریال` for EPS.
- Use percent with at most two decimals.
- Use comma separators for large numbers.
- Use `سود` only for positive values and `زیان` for negative values.
- Use `افزایش زیان` when a negative value becomes more negative.
- Use `کاهش زیان` when a negative value becomes less negative.
- Do not call a negative gross profit `سود ناخالص` without clarifying it is gross loss.
- Always append source metadata.

### Source Format

```text
منبع:
صورت‌های مالی [تلفیقی اگر IsComposing=true] [N ماهه] سال مالی منتهی به [JalaliFiscalYearEnd] ([حسابرسی‌شده/حسابرسی‌نشده])
دوره منتهی به: [JalaliPeriodEnd]
زمان انتشار: [JalaliAnnouncementDate HH:mm:ss]
Provider: NadpcoApi or NoavaranCurrentApi
```

### Acceptance Criteria

- Source line matches selected variant exactly.
- The renderer never fabricates unavailable comparisons.
- The renderer can produce focused answers for single-metric questions and full summaries for broad questions.

---

## Task 081.10 - AI Orchestration Integration

### Goal
Register the feature as an AI tool/use-case in the Microsoft Agent Framework V2 orchestration path.

### Checklist

- [ ] Add new detected intent.
- [ ] Register use-case handler.
- [ ] Add semantic catalog entries or database-backed aliases.
- [ ] Ensure orchestration chooses this feature before generic unsupported-metric fallback.
- [ ] Add credit metering classification.
- [ ] Add explainability payload.
- [ ] Add telemetry tags:
  - intent
  - provider
  - statement type
  - period months
  - variant
  - audited status
  - data availability

### Acceptance Criteria

- Questions like `آخرین صورت مالی غالبر چطور بود؟` do not fall into `Metric term not recognized`.
- Unsupported metric wording is logged for alias-learning without returning a misleading answer.
- Usage metering is applied once per query.

---

## Task 081.11 - Tests

### Unit Tests

- Period alias parsing: 3/سه/سه ماهه, 6/شش/شش ماهه, 9/نه, 12/سالانه.
- Variant parsing: تلفیقی, غیرتلفیقی, شرکت اصلی.
- Audited parsing: حسابرسی‌شده, حسابرسی‌نشده.
- Latest selection tie-breakers.
- Same-period consolidated vs non-consolidated separation.
- YoY change calculation.
- Margin calculation.
- ROA/ROE calculation.
- Loss wording.
- Source metadata generation.

### Integration Tests

- Seed `غالبر`-like 3/6/9/12 month statements and verify latest-period selection.
- Seed same period with consolidated and non-consolidated variants and verify default query selects `IsComposing = false` and explicit تلفیقی query selects `IsComposing = true`.
- Verify `NadpcoApi` or `NoavaranCurrentApi` filter excludes `CodalDb` rows.
- Verify comparable prior year selection.
- Verify missing balance sheet produces warning, not failure.

### End-to-End AI Regression Tests

- آخرین صورت مالی غالبر چطور بود؟
- گزارش ۱۲ ماهه غالبر را تحلیل کن
- صورت مالی تلفیقی غالبر را تحلیل کن
- سود خالص غالبر چقدر شده؟
- EPS غالبر در آخرین گزارش چقدر است؟
- حاشیه سود عملیاتی غالبر چقدر است؟
- ترازنامه غالبر را خلاصه کن
- نسبت جاری غالبر چقدر است؟
- ROA و ROE غالبر چقدر شده؟
- گزارش سه ماهه غالبر را بگو
- سود شرکت اصلی غالبر چقدر است؟

### Acceptance Criteria

- All regression prompts route to `FinancialStatementPeriodAnalysis`.
- Numeric response facts match seeded line items.
- Source label never says تلفیقی when selected statement is non-consolidated.
- Generic/latest financial-statement prompts never return consolidated facts while a non-consolidated statement exists for the selected period.

---

## Task 081.12 - Documentation and Checklist

### Goal
Document the feature and add it to implementation tracking.

### Checklist

- [ ] Add feature folder to `specs/081-ai-financial-statement-period-analysis-query`.
- [ ] Add implementation-checklist row after feature `080`.
- [ ] Document sample source-binding caveat from `غالبر`.
- [ ] Document default canonical variant policy.
- [ ] Document metric coverage and unavailable metrics.

### Suggested Checklist Row

```md
| [ ] | 81 | [081](./081-ai-financial-statement-period-analysis-query/user-story.md) / [tasks](./081-ai-financial-statement-period-analysis-query/tasks.md) | AI Financial Statement Period Analysis Query | Depends on `029`, `040`, `045`, `047`, `072`, `074`, `009`, `010`; add AI support for Persian questions about latest or requested 3/6/9/12-month NADPCO financial statements, with deterministic statement selection, consolidated/non-consolidated and audited/unaudited variant binding, income-statement/balance-sheet/ratio calculations, YoY comparisons, Persian rendering, source metadata, and regression tests. |
```
