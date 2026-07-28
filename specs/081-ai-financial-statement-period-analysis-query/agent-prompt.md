# Agent Prompt - Feature 081

Implement Feature 081: AI Financial Statement Period Analysis Query.

Read these specs first:

- `specs/029-financial-statement-schema-fix/user-story.md`
- `specs/029-financial-statement-schema-fix/tasks.md`
- `specs/040-nadpco-api-financial-statement-sync/user-story.md`
- `specs/040-nadpco-api-financial-statement-sync/tasks.md`
- `specs/045-symbol-metric-point-lookup/user-story.md`
- `specs/047-microsoft-agent-framework-orchestration-v2/user-story.md`
- `specs/072-centralize-financial-metric-alias-and-intent-routing-registry/user-story.md`
- `specs/074-database-backed-metric-definition-and-alias-registry/user-story.md`
- `specs/081-ai-financial-statement-period-analysis-query/user-story.md`
- `specs/081-ai-financial-statement-period-analysis-query/tasks.md`

Goal:

Add an AI query flow that answers Persian natural-language questions about a company's latest or requested 3/6/9/12-month financial statements using only persisted `NadpcoApi` or `NoavaranCurrentApi` normalized financial statements.

Critical constraints:

1. Do not call NADPCO API at query time.
2. Read from persisted normalized statements and line items only.
3. Filter source provider to `ProviderName = "NadpcoApi" or "NoavaranCurrentApi"`.
4. Use `StatementType` for income statement / balance sheet / cash flow.
5. Use `PeriodType` for 3/6/9/12 month duration.
6. Never confuse `StatementType` and `PeriodType`.
7. Never mix consolidated and non-consolidated data in one source-bound metric group.
8. Never show a consolidated/tلفیقی source label unless the selected statement has `IsComposing = true`.
9. If the user asks تلفیقی/consolidated, use consolidated only (`IsComposing = true`).
10. If the user asks غیرتلفیقی / شرکت اصلی, use non-consolidated only (`IsComposing = false`).
11. If the user does not specify a variant, always use non-consolidated statements (`IsComposing = false`). Do not include consolidated rows in default AI responses.
12. Do not silently fallback to consolidated data when the default non-consolidated statement is missing. Return a missing-data warning unless the user explicitly asks for consolidated data.
13. Use governed `MetricCode` mappings. Do not use runtime Persian title matching.
14. Add tests before or alongside implementation.

Questions that must work:

- آخرین صورت مالی غالبر چطور بود؟
- گزارش ۱۲ ماهه غالبر را تحلیل کن
- صورت مالی غالبر را تحلیل کن  # must select IsComposing=false by default
- صورت مالی تلفیقی غالبر را تحلیل کن  # must select IsComposing=true
- صورت مالی غیرتلفیقی غالبر را تحلیل کن  # must select IsComposing=false
- سود خالص غالبر چقدر شده؟
- EPS غالبر در آخرین گزارش چقدر است؟
- حاشیه سود عملیاتی غالبر چقدر است؟
- ترازنامه غالبر را خلاصه کن
- نسبت جاری غالبر چقدر است؟
- ROA و ROE غالبر چقدر شده؟
- گزارش سه ماهه غالبر را بگو

Expected implementation shape:

- Add intent `FinancialStatementPeriodAnalysis`.
- Add parser/semantic aliases for financial-statement, period, variant, audited status, and metric-focus terms.
- Add `FinancialStatementAnalysisQuery` / response contracts.
- Add statement-selection service.
- Add repository methods for selected statements and comparable prior periods.
- Add deterministic calculator for YoY changes, margins, debt/current ratios, ROA, and ROE.
- Add Persian renderer with source metadata.
- Register in Microsoft Agent Framework V2 orchestration.
- Add usage metering and telemetry.
- Add unit, integration, and end-to-end AI regression tests.
- Add explicit regression coverage proving generic/latest financial-statement prompts select `IsComposing = false` even when a same-period consolidated statement exists.

After implementation:

- Update `specs/081-ai-financial-statement-period-analysis-query/tasks.md` with implementation status.
- Add feature 081 to `specs/implementation-checklist.md`.
- Include a short note explaining exact data entities and mappings reused.
- Run relevant unit/integration/architecture tests and report results.
