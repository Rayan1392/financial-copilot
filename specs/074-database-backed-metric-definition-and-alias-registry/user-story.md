# User Story - Database-Backed Metric Definition and Alias Registry

## Story

As a FinancialCopilot platform operator and product owner,

I want all supported `DerivedMetrics.MetricCode` values, Persian/English display titles, user-facing aliases, period phrases, and routing metadata to be stored in reviewed database-backed registry tables,

so that when users ask questions with different Persian, English, market slang, or mixed-language expressions, the AI can resolve the intended metric deterministically without requiring code deployments for every new phrase.

## Business Context

FinancialCopilot already persists calculated and provider-sourced metrics in `public."DerivedMetrics"` using fields such as:

- `MetricCode`
- `MetricVersion`
- `CalculationPolicyVersion`
- `PeriodType`
- `PeriodStart`
- `PeriodEnd`
- `Value`
- `Unit`
- `ObservedAt`
- `LastSynchronizedAt`
- `WarningsJson`
- `SourceEvidenceJson`
- `DependencyEvidenceJson`
- `ExternalCompanyId`

The data table is a fact table. It stores metric values by company and period, but it does not describe how users naturally refer to those metrics.

Today, Persian labels and metric phrases are partially hard-coded in source code, semantic catalogs, parser fallbacks, workflow routing gates, and prompt text. This makes the product fragile:

- A metric may exist in `DerivedMetrics` but still be invisible to natural-language questions.
- Adding a new Persian phrase requires source code changes.
- Phrases such as `رشد فروش نسبت به پارسال`, `رشد فروش ماه مشابه`, `درصد رشد فروش نسبت به مدت مشابه`, and `YoY sales growth` should all resolve to `MONTHLY_SALES_GROWTH_YOY`, but this requires explicit alias coverage.
- Phrases such as `فروش ماه قبل` and `رشد فروش ماه قبل` are semantically different and must not resolve to the same metric accidentally.
- Phrases containing generic terms such as `قیمت`, `تغییر`, `فروش`, or `رشد` require longest-match and ambiguity rules to prevent incorrect routing.

Specs `045`, `046`, `072`, and `073` already establish symbol metric lookup, dynamic alias learning, centralized alias/routing, and period-specific metric coverage. This feature extends that direction by moving the canonical metric dictionary and public aliases into database-backed, versionable, seedable registry tables.

## Problem Statement

The current system has two related but separate concerns:

1. Metric facts
   - Stored in `DerivedMetrics`.
   - Queried by `ExternalCompanyId`, `MetricCode`, `PeriodType`, and period ordering.

2. Metric vocabulary and routing
   - Partly hard-coded in source code.
   - Partly maintained through dynamic alias learning.
   - Partly duplicated in prompts, parser fallbacks, and workflow preflight rules.

The missing layer is a governed canonical database registry that says:

- what each `MetricCode` means,
- what its default Persian/English title is,
- which aliases can resolve to it,
- which period selectors can be inferred from user language,
- whether it is lookup-eligible, scanner-eligible, direct-question-eligible, valuation-related, growth-related, monthly-activity-related, margin-related, quote-context-related, or fundamental,
- and how ambiguity must be handled.

## Goals

- Add database-backed metric definition and alias registry tables.
- Seed all currently observed `DerivedMetrics.MetricCode` values.
- Store canonical Persian and English display titles outside source code.
- Allow multiple Persian/English aliases per metric.
- Support mixed Persian/English aliases such as `P/E`, `پی به ای`, `sales growth yoy`, and `YoY فروش`.
- Support period phrase resolution separately from metric identity.
- Preserve `DerivedMetrics` as a numeric fact table and avoid adding display or alias fields to it.
- Make direct lookup, scanner parsing, workflow routing, and prompt generation consume the same registry service.
- Preserve dynamic alias learning, but make reviewed aliases converge into the governed alias registry.
- Add ambiguity and precedence rules so generic words do not override specific metric phrases.
- Improve the AI user experience for all currently persisted metrics.

## Non-Goals

- Do not change the `DerivedMetrics` fact schema unless absolutely required for foreign-key enforcement.
- Do not create duplicate metric value tables.
- Do not change provider ingestion formulas in this feature.
- Do not introduce new financial calculations.
- Do not allow the LLM to auto-create canonical metrics or modify formulas.
- Do not remove existing dynamic alias learning.
- Do not remove existing static semantic catalog coverage until parity is proven by tests.
- Do not change public response contracts unless existing orchestration metadata already supports the additional diagnostics.

## Current DerivedMetrics Coverage

The registry must seed definitions for all currently observed metric codes and period types:

### Monthly Activity Metrics

- `AVG_12M_MONTHLY_SALES` / `Monthly`
- `MONTHLY_PRODUCTION_QUANTITY` / `Monthly`
- `MONTHLY_SALES` / `Monthly`
- `MONTHLY_SALES_GROWTH_MOM` / `Monthly`
- `MONTHLY_SALES_GROWTH_YOY` / `Monthly`
- `MONTHLY_SALES_QUANTITY` / `Monthly`
- `MONTHLY_SALES_RATE` / `Monthly`
- `MONTHLY_SALES_YTD` / `Monthly`
- `MONTHLY_SALES_YTD_PREVIOUS_MONTH` / `Monthly`

### Quarterly / Periodic Profitability and Growth Metrics

- `REVENUE`
- `GROSS_PROFIT`
- `OPERATING_PROFIT`
- `NET_PROFIT`
- `EBIT`
- `REVENUE_GROWTH_QOQ`
- `REVENUE_GROWTH_YOY`
- `GROSS_PROFIT_GROWTH_QOQ`
- `GROSS_PROFIT_GROWTH_YOY`
- `OPERATING_PROFIT_GROWTH_QOQ`
- `OPERATING_PROFIT_GROWTH_YOY`
- `NET_PROFIT_GROWTH_QOQ`
- `NET_PROFIT_GROWTH_YOY`
- `EPS_GROWTH_QOQ`
- `EPS_GROWTH_YOY`
- `EQUITY_GROWTH_QOQ`
- `EQUITY_GROWTH_YOY`
- `GROSS_PROFIT_MARGIN`
- `OPERATING_PROFIT_MARGIN`
- `NET_PROFIT_MARGIN`

### Valuation Metrics

- `PE_TTM`
- `PS_TTM`

### Liquidity, Leverage, Asset Quality, and Efficiency Metrics

- `ASSET_TURNOVER`
- `AVERAGE_COLLECTION_PERIOD`
- `COMPREHENSIVE_LIQUIDITY_INDEX`
- `CURRENT_ASSETS_TO_TOTAL_ASSETS`
- `CURRENT_RATIO`
- `DEBT_TO_EQUITY`
- `NET_WORKING_CAPITAL`
- `TANGIBLE_FIXED_ASSETS_TURNOVER`

## Target Architecture

The target design introduces a canonical registry layer above `DerivedMetrics`.

```text
User query
  -> Persian/English normalization
  -> symbol/company resolution
  -> metric alias resolution from database-backed registry
  -> period alias resolution from database-backed period dictionary
  -> ambiguity and longest-match resolution
  -> route by metric capabilities
  -> query DerivedMetrics by ExternalCompanyId + MetricCode + PeriodType
  -> select latest or relative period row
  -> render answer using registry display metadata and source evidence
```

## Proposed Tables

### MetricDefinitions

Stores one canonical row per supported metric code.

Required fields:

- `Id`
- `MetricCode`
- `DefaultPersianTitle`
- `DefaultEnglishTitle`
- `Category`
- `DefaultUnit`
- `DefaultPeriodType`
- `DefaultPeriodSelector`
- `DescriptionFa`
- `DescriptionEn`
- `LookupEligible`
- `ScannerEligible`
- `DirectQuestionEligible`
- `IsMonthlyActivityMetric`
- `IsValuationMetric`
- `IsFundamentalMetric`
- `IsGrowthMetric`
- `IsMarginMetric`
- `IsBalanceSheetMetric`
- `IsLiquidityMetric`
- `IsEfficiencyMetric`
- `SuppressQuoteContext`
- `RequiresPeriodSelection`
- `IsActive`
- `SortOrder`
- `CreatedAt`
- `UpdatedAt`

### MetricAliases

Stores approved user-facing phrases for metrics.

Required fields:

- `Id`
- `MetricCode`
- `AliasText`
- `NormalizedAliasText`
- `Language`
- `MatchType`
- `Priority`
- `AppliesToPeriodType`
- `DefaultPeriodSelector`
- `ComparisonQualifier`
- `Source`
- `Status`
- `Confidence`
- `CreatedAt`
- `ApprovedAt`
- `ApprovedBy`

### MetricPeriodAliases

Stores period phrases independently from metric identity.

Required fields:

- `Id`
- `AliasText`
- `NormalizedAliasText`
- `Language`
- `PeriodType`
- `PeriodSelector`
- `Priority`
- `Status`

Examples:

- `آخرین ماه` -> `Monthly`, `M0`
- `ماه قبل` -> `Monthly`, `M1`
- `ماه مشابه سال قبل` -> `Monthly`, `M12`
- `آخرین فصل` -> `ThreeMonths`, `Q0`
- `فصل قبل` -> `ThreeMonths`, `Q1`
- `فصل مشابه سال قبل` -> `ThreeMonths`, `Q4`
- `شش ماهه` -> `SixMonths`, `Latest`
- `نه ماهه` -> `NineMonths`, `Latest`
- `دوازده ماهه` -> `TwelveMonths`, `Latest`

### MetricAliasCandidates

Stores unresolved or low-confidence expressions for review.

Required fields:

- `Id`
- `Expression`
- `NormalizedExpression`
- `Language`
- `SuggestedMetricCode`
- `SuggestedPeriodType`
- `SuggestedPeriodSelector`
- `SuggestedComparisonQualifier`
- `Confidence`
- `FrequencyCount`
- `DistinctActorCount`
- `EvidenceExamplesJson`
- `Status`
- `FirstSeenAt`
- `LastSeenAt`
- `ReviewedBy`
- `ReviewedAt`

## Resolution Rules

### Precedence

Metric phrase resolution must follow this order:

1. exact canonical alias,
2. longest canonical alias,
3. metric alias with explicit period selector,
4. approved dynamic/database alias,
5. fuzzy match above configured threshold,
6. LLM suggestion only as candidate, not final authority.

### Longest-Match Protection

Specific phrases must win over generic phrases:

- `نسبت قیمت به سود` -> `PE_TTM`, not `LATEST_PRICE`.
- `قیمت به فروش` -> `PS_TTM`, not `LATEST_PRICE`.
- `رشد فروش نسبت به سال قبل` -> `MONTHLY_SALES_GROWTH_YOY`, not `MONTHLY_SALES`.
- `فروش ماه قبل` -> `MONTHLY_SALES` + `M1`, not `MONTHLY_SALES_GROWTH_MOM`.
- `رشد فروش ماه قبل` is ambiguous unless product policy explicitly maps it to MoM.

### Ambiguity Policy

The resolver must not force unsafe matches for broad phrases.

Examples:

- `تغییر فروش` without `ماه قبل`, `سال قبل`, `مدت مشابه`, or period context should be treated as ambiguous.
- `رشد فروش` without comparison context can default to a configured product default only if the metric definition explicitly declares one.
- `قیمت` can resolve to latest price only if the query is about market price and not part of a valuation phrase.
- company names containing metric-like tokens must not be truncated.

## Seed Alias Requirements

### MONTHLY_SALES_GROWTH_YOY

Must support at least:

- `رشد فروش سالانه`
- `رشد فروش نسبت به سال قبل`
- `رشد فروش نسبت به پارسال`
- `رشد فروش ماهانه نسبت به سال قبل`
- `رشد فروش ماه مشابه`
- `رشد فروش ماه مشابه سال قبل`
- `درصد رشد فروش نسبت به مدت مشابه`
- `تغییر فروش نسبت به مدت مشابه`
- `YoY sales growth`
- `sales growth yoy`

### MONTHLY_SALES_GROWTH_MOM

Must support at least:

- `رشد فروش ماهانه`
- `رشد فروش نسبت به ماه قبل`
- `تغییر فروش نسبت به ماه قبل`
- `رشد ماه به ماه فروش`
- `فروش نسبت به ماه قبل چقدر رشد کرده`
- `MoM sales growth`
- `sales growth mom`

### MONTHLY_SALES

Must support at least:

- `فروش ماهانه`
- `آخرین فروش`
- `فروش آخرین ماه`
- `مبلغ فروش`
- `فروش شرکت`
- `فروش ماه قبل`
- `فروش ماه مشابه سال قبل`

### AVG_12M_MONTHLY_SALES

Must support at least:

- `متوسط فروش ۱۲ ماهه`
- `میانگین فروش ۱۲ ماهه`
- `متوسط فروش یک ساله`
- `میانگین فروش یک ساله`
- `average monthly sales`
- `12m average sales`

### Valuation Metrics

`PE_TTM` must support:

- `PE`
- `P/E`
- `پی به ای`
- `پی ای`
- `نسبت قیمت به سود`
- `قیمت به سود`

`PS_TTM` must support:

- `PS`
- `P/S`
- `پی به اس`
- `پی اس`
- `نسبت قیمت به فروش`
- `قیمت به فروش`

## Desired User Experience

When a direct metric question is answered, the response should include enough metadata to make the answer understandable:

- symbol/company,
- canonical Persian metric title,
- value,
- unit,
- period label,
- source/provider evidence when available,
- confidence or diagnostic metadata when already supported,
- warning when the row is missing, zero due to missing source, stale, or ambiguous.

Example:

```text
رشد فروش ماهانه کچاد نسبت به مدت مشابه سال قبل ۳۴٪ است.

نماد: کچاد
متریک: رشد فروش نسبت به مدت مشابه سال قبل
دوره: اردیبهشت ۱۴۰۵
منبع: DerivedMetrics / CyclicalWaves
```

## Acceptance Criteria

### Database Registry

- `MetricDefinitions` exists and is seeded for every currently observed `DerivedMetrics.MetricCode`.
- `MetricAliases` exists and supports multiple active aliases per metric.
- `MetricPeriodAliases` exists and resolves relative Persian/English period phrases.
- `MetricAliasCandidates` exists for unresolved or low-confidence expressions.
- `DerivedMetrics` remains the numeric fact table and does not store display aliases.

### Resolver Behavior

- The metric resolver reads from the database registry through a cache-aware service.
- The resolver returns `MetricCode`, display metadata, capabilities, match source, match confidence, and optional period selector.
- Longest-match precedence is enforced.
- Persian normalization is applied before matching.
- Ambiguous expressions are not silently mapped to unsafe metrics.

### Integration

- Symbol metric lookup can resolve metrics from database-backed aliases.
- Scanner parsing can resolve metrics from database-backed aliases.
- V2 direct metric routing can use registry capabilities instead of duplicated hard-coded phrase lists.
- Prompt generation or routing instructions can reference registry categories instead of full duplicated alias lists where practical.
- Dynamic alias candidates can be reviewed and promoted to `MetricAliases`.

### Backward Compatibility

- Existing PE/PS/monthly-sales/monthly-production/margin questions continue to work.
- Existing `DerivedMetrics` queries by `MetricCode` and `PeriodType` continue to work.
- Existing dynamic alias infrastructure remains compatible.
- Existing tests for specs `045`, `046`, `072`, and `073` remain green or are updated only where the new registry intentionally replaces hard-coded vocabulary.

## Dependencies

- `DerivedMetrics` persistence from current ingestion providers.
- Existing company/symbol resolution by `ExternalCompanyId`.
- Existing `IFinancialMetricRegistry` or equivalent semantic catalog abstraction.
- Existing `IMetricAliasResolver` / `CompositeMetricAliasResolver`.
- Existing dynamic alias learning infrastructure.
- Existing direct symbol metric lookup service.
- Existing V1/V2 orchestration paths.

## Rollout Strategy

1. Add tables and seed data behind feature flags.
2. Build read-only registry service and compare output with current hard-coded catalog in tests.
3. Switch alias resolution to database-backed registry with static fallback.
4. Switch direct routing and parser fallbacks to registry-derived resolution.
5. Enable alias candidate capture for unresolved metric phrases.
6. Add admin/review operations in a later or parallel UI-focused feature if not already available.
7. Remove duplicated hard-coded phrase lists only after parity tests pass.

## Diagnostics

The implementation should log or expose test-only diagnostics for:

- normalized query,
- matched alias text,
- resolved metric code,
- resolved period type,
- resolved period selector,
- match source,
- confidence,
- ambiguity reason,
- selected route/tool,
- final `DerivedMetrics` query shape.

Diagnostics must remain internal unless public orchestration metadata already supports them.
