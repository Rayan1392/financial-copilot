# Direct Price / Market Quote Routing Regression

Date: 2026-06-19
Severity: High
Status: Root cause confirmed
Scope: FinancialCopilot V2 direct metric routing, symbol lookup parsing, market quote alias coverage, quote-context rendering

## Current Behavior

Direct price questions such as:

- `آخرین قیمت کچاد؟`
- `آخرین قیمت کگل؟`
- `قیمت امروز کچاد؟`
- `تغییر قیمت کگل؟`
- `درصد تغییر قیمت کگل؟`

do not resolve through the market quote path. Instead, they fall into symbol-lookup metric parsing and produce catalog-resolution errors such as:

`Metric term 'آخرین قیمت' is not recognized in the supported catalog.`

At the same time, valuation lookups such as `pe کگل؟` still use the symbol lookup path and still request market quote context columns. In code, those PE tables should include `LATEST_PRICE` and `DAILY_CHANGE_PCT` when quote data is available. If those cells render as `Missing`, that is a separate quote-data availability or provider-resolution issue, not the same parser/routing failure as the direct price query.

## Expected Behavior

- Direct price phrases must be recognized as direct metric / market quote requests.
- The request must stay on the existing `lookup_symbol_metrics` pipeline and resolve to quote-backed metrics, not to quarterly fundamental aliases.
- `LATEST_PRICE` and `DAILY_CHANGE_PCT` must remain suppressed only for monthly production/sales responses.
- Valuation lookups such as `PE کگل` must continue to include quote context when quote data is available.

## Reproduction Steps

1. Send `آخرین قیمت کچاد؟` to `POST /api/ai/v1/query`.
2. Observe a clarification-style error about an unrecognized metric term instead of a quote response.
3. Send `pe کگل؟`.
4. Observe that the lookup path still returns `PE_TTM` and still attempts to include quote-context columns.

## Affected Queries

- `آخرین قیمت کچاد؟`
- `آخرین قیمت کگل؟`
- `قیمت امروز کچاد؟`
- `قیمت کگل؟`
- `قیمت پایانی کچاد؟`
- `تغییر قیمت کگل؟`
- `درصد تغییر قیمت کگل؟`
- `درصد تغییر روزانه کگل؟`
- Related follow-ups that rely on direct metric detection for quote phrases

## Root Cause Analysis

### 1. V2 direct-metric preflight does not recognize price phrases

The active V2 workflow short-circuits direct metric questions before the agent tool-selection loop. That preflight only triggers when `ContainsDirectMetricTerm(...)` matches the message.

Evidence:

- [src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs](</d:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs:469>)
- [src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs](</d:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs:528>)

Confirmed behavior:

- `ContainsDirectMetricTerm(...)` includes monthly-sales, production, PE, PS, EPS, ROE, ROA, current ratio, margin, and market cap phrases.
- It does not include direct price phrases such as `آخرین قیمت`, `قیمت`, `قیمت امروز`, `قیمت پایانی`, `تغییر قیمت`, or `درصد تغییر قیمت`.

Result:

- Direct quote questions are not routed through the deterministic direct lookup path.

### 2. The agent prompt also omits price from the “financial metric” trigger list

If direct preflight misses the request, the agent falls back to prompt-driven tool selection. The V2 system prompt describes financial metric triggers, but the explicit trigger list does not include direct price phrases either.

Evidence:

- [src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs](</d:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs:639>)
- [src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs](</d:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs:657>)

Confirmed behavior:

- The prompt says valuation/ratio lookups may fetch `LATEST_PRICE` and `DAILY_CHANGE_PCT` as context.
- The “FINANCIAL METRIC INTENT” trigger list still does not contain direct price/quote wording.

Result:

- There is no reliable V2 route for direct quote questions even after preflight is missed.

### 3. The symbol lookup parser has deterministic fallback only for PE and monthly sales

`LlmSymbolLookupParser` contains deterministic recovery for direct PE and direct monthly-sales patterns only.

Evidence:

- [src/backend/FinancialCopilot.Application/Scanner/LlmSymbolLookupParser.cs](</d:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Application/Scanner/LlmSymbolLookupParser.cs:13>)
- [src/backend/FinancialCopilot.Application/Scanner/LlmSymbolLookupParser.cs](</d:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Application/Scanner/LlmSymbolLookupParser.cs:241>)

Confirmed behavior:

- `TryParseDirectLookup(...)` only calls `TryParseDirectPeLookup(...)` and `TryParseDirectMonthlySalesLookup(...)`.
- There is no `TryParseDirectPriceLookup(...)`.

Result:

- Even if the lookup tool is invoked for a direct price query, there is no deterministic fallback that preserves the user phrase and maps it to quote metrics.

### 4. The metric alias catalog registers `LATEST_PRICE` as a source metric but gives it no user-facing aliases

The semantic catalog defines `LATEST_PRICE` as a source metric, but unlike `PE_TTM`, `MONTHLY_SALES`, and similar public query metrics, it has no Persian or English aliases.

Evidence:

- [src/backend/FinancialCopilot.Domain/Financial/Metrics/PhaseOneFinancialSemanticCatalog.cs](</d:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Domain/Financial/Metrics/PhaseOneFinancialSemanticCatalog.cs:141>)

Confirmed behavior:

- `LATEST_PRICE` is declared via `DefineSource(...)`.
- `MetricAliasResolver` resolves only exact aliases registered in the supported metric definitions.
- Since no aliases exist for `LATEST_PRICE`, phrases like `آخرین قیمت` and `قیمت امروز` cannot resolve.

Result:

- The current parser/catalog combination guarantees `Metric term 'آخرین قیمت' is not recognized...` whenever direct price text reaches alias resolution.

### 5. The market quote provider path still exists and is callable

The quote path itself is not removed.

Evidence:

- `IMarketQuoteResolver` registration:
  - [src/backend/FinancialCopilot.Infrastructure/ServiceCollectionExtensions.cs](</d:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/ServiceCollectionExtensions.cs:413>)
- Resolver wrapper:
  - [src/backend/FinancialCopilot.Infrastructure/Financial/Scanner/EfCoreScannerExecutionService.cs](</d:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Scanner/EfCoreScannerExecutionService.cs:408>)
- Persisted market quote provider:
  - [src/backend/FinancialCopilot.Infrastructure/Financial/Providers/StockMarketDb/PersistedMarketDataProvider.cs](</d:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Providers/StockMarketDb/PersistedMarketDataProvider.cs:19>)

Confirmed behavior:

- `IMarketQuoteResolver` is registered to `ProviderMarketQuoteResolver`.
- `ProviderMarketQuoteResolver` delegates to `IMarketDataProvider.GetLatestQuotesAsync(...)`.
- `PersistedMarketDataProvider` resolves quotes from `LatestMarketQuotes` via company-linked `TseSymbol` and direct instrument ticker fallback.

Result:

- The direct price regression happens before quote retrieval, not because the quote provider path was deleted.

### 6. Monthly-sales quote omission was scoped correctly in the lookup service

The monthly-sales change did not globally disable quote context.

Evidence:

- [src/backend/FinancialCopilot.Infrastructure/Financial/Scanner/EfCoreSymbolMetricLookupService.cs](</d:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Scanner/EfCoreSymbolMetricLookupService.cs:48>)
- [src/backend/FinancialCopilot.Infrastructure/Financial/Scanner/EfCoreSymbolMetricLookupService.cs](</d:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Scanner/EfCoreSymbolMetricLookupService.cs:324>)

Confirmed behavior:

- The lookup service adds `LATEST_PRICE` and `DAILY_CHANGE_PCT` when the requested metrics are not exclusively monthly-activity metrics.
- `ShouldIncludeMarketContext(...)` suppresses quote columns only when every requested metric is a monthly-activity metric.

Result:

- The monthly-sales omission rule is not the root cause of direct price failure.

### 7. Empty symbol-lookup tables are already suppressed at the API boundary

This part is not the regression. It is already implemented.

Evidence:

- [src/backend/FinancialCopilot.API/Controllers/AiFacadeController.cs](</d:/Source/TahlilApp-AI/src/backend/FinancialCopilot.API/Controllers/AiFacadeController.cs:213>)

Confirmed behavior:

- `MapSymbolLookupTable(...)` returns `null` when `table.Rows.Count == 0`.

Result:

- The direct price bug is not caused by empty lookup tables leaking through the controller.

## Quote Enrichment Findings

The PE path still expects quote enrichment to work when quote data exists.

Evidence:

- [tests/FinancialCopilot.IntegrationTests/SymbolLookupEndpointTests.cs](</d:/Source/TahlilApp-AI/tests/FinancialCopilot.IntegrationTests/SymbolLookupEndpointTests.cs:205>)

Confirmed behavior:

- Existing PE regression tests assert that `LATEST_PRICE` and `DAILY_CHANGE_PCT` are present for PE lookups.
- Existing monthly-sales regression tests assert that those columns are absent for monthly-sales output.
- There are no direct latest-price integration tests.

Conclusion:

- The direct price failure is a confirmed production code bug.
- The PE quote-column `Missing` symptom is related but not proven to share the same root cause.
- Most likely interpretation: direct price failure is routing/alias coverage; PE quote-column `Missing` is provider data availability, symbol-to-quote matching, or stale `LatestMarketQuotes` content in the current environment.

## Architecture Note - Alias And Intent Phrase Mapping

### Current alias / intent mapping locations

1. Static semantic catalog
   - Location:
     - [src/backend/FinancialCopilot.Domain/Financial/Metrics/PhaseOneFinancialSemanticCatalog.cs](/d:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Domain/Financial/Metrics/PhaseOneFinancialSemanticCatalog.cs:5)
   - Type: hard-coded C#
   - Current role:
     - This is the main canonical metric alias source for symbol/scanner metric resolution.
     - `MetricAliasResolver` resolves only against aliases exposed by `IFinancialMetricRegistry.GetSupportedMetrics(...)`.
   - Scope:
     - The catalog currently defines `63` metric definitions and `208` hard-coded `Alias(...)` entries.
     - Many public queryable metrics have aliases, including monthly sales, YTD monthly sales, revenue, net profit, EPS, PE/PS, margins, ROE/ROA, liquidity ratios, and related growth metrics.
   - Important asymmetry:
     - `LATEST_PRICE` exists only as a source metric definition with no aliases.
     - `MARKET_CAP` also exists only as a source metric definition with no aliases.
     - `DAILY_CHANGE_PCT` is not defined in `PhaseOneFinancialSemanticCatalog` at all.

2. Runtime dynamic alias layer
   - Locations:
     - [src/backend/FinancialCopilot.Infrastructure/Financial/Semantics/DynamicAliasInfrastructure.cs](/d:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/Financial/Semantics/DynamicAliasInfrastructure.cs:258)
     - [src/backend/FinancialCopilot.API/Controllers/AdminMetricAliasController.cs](/d:/Source/TahlilApp-AI/src/backend/FinancialCopilot.API/Controllers/AdminMetricAliasController.cs:14)
     - [src/backend/FinancialCopilot.Infrastructure/ServiceCollectionExtensions.cs](/d:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/ServiceCollectionExtensions.cs:571)
   - Type: database-based and runtime-configurable
   - Current role:
     - `CompositeMetricAliasResolver` wraps the static resolver and can load active aliases from the `DynamicMetricAliases` table.
     - Learned/admin-approved dynamic aliases are real runtime alias data.
   - Limitation:
     - Dynamic aliases only help after the request reaches `IMetricAliasResolver`.
     - They do not drive V2 direct metric phrase gating.
     - They do not drive deterministic parser fallbacks.

3. V2 workflow phrase gate
   - Location:
     - [src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs](/d:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs:469)
   - Type: hard-coded C#
   - Current role:
     - `IsDirectMetricLookupRequest(...)` uses `ContainsDirectMetricTerm(...)` as a static phrase gate before tool selection.
   - Current hard-coded phrases:
     - monthly-sales family
     - monthly production
     - PE / P-E phrasing
     - PS
     - EPS
     - ROE / ROA
     - current ratio
     - margin
     - market cap
   - Missing:
     - `آخرین قیمت`
     - `قیمت`
     - `قیمت امروز`
     - `قیمت پایانی`
     - `تغییر قیمت`
     - `درصد تغییر قیمت`
     - `درصد تغییر روزانه`

4. Symbol lookup deterministic fallbacks
   - Location:
     - [src/backend/FinancialCopilot.Application/Scanner/LlmSymbolLookupParser.cs](/d:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Application/Scanner/LlmSymbolLookupParser.cs:7)
   - Type: hard-coded C#
   - Current role:
     - The parser uses the LLM for pair extraction, then has deterministic fallback for specific phrase families.
   - Existing hard-coded fallback families:
     - direct PE lookup
     - direct monthly-sales lookup
   - Missing:
     - direct price lookup
     - direct daily-change lookup

5. Prompt-level trigger lists
   - Locations:
     - [src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs](/d:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs:639)
     - [src/backend/FinancialCopilot.Application/AI/Orchestration/LlmAiIntentDetector.cs](/d:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Application/AI/Orchestration/LlmAiIntentDetector.cs:13)
     - [src/backend/FinancialCopilot.Application/FinancialData/Ingestion/LlmComprehensiveAnalysisQueryParser.cs](/d:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Application/FinancialData/Ingestion/LlmComprehensiveAnalysisQueryParser.cs:5)
     - [src/backend/FinancialCopilot.Application/Scanner/LlmSymbolLookupParser.cs](/d:/Source/TahlilApp-AI/src/backend/FinancialCopilot.Application/Scanner/LlmSymbolLookupParser.cs:72)
   - Type: hard-coded prompt text
   - Current role:
     - V2 system instructions include trigger lists for analysis vs financial metric intent.
     - V1 `LlmAiIntentDetector` includes another trigger list plus a deterministic PE rule.
     - `LlmComprehensiveAnalysisQueryParser` contains hard-coded allowed topic slugs.
     - `LlmSymbolLookupParser` prompt is more generic; it tells the model to return symbol/metric pairs exactly as written, but it does not enumerate direct price aliases.

### Hard-coded vs config-based vs seed-based vs database-based

| Location | Current storage model |
|---|---|
| `PhaseOneFinancialSemanticCatalog` | Hard-coded C# |
| `FinancialCopilotWorkflowDefinition.ContainsDirectMetricTerm(...)` | Hard-coded C# |
| `LlmSymbolLookupParser` direct fallback arrays | Hard-coded C# |
| `LlmAiIntentDetector` system prompt + deterministic PE rule | Hard-coded C# |
| `LlmComprehensiveAnalysisQueryParser` allowed topic slugs | Hard-coded C# |
| Dynamic metric aliases | Database-based runtime data |
| `MetricAliasLearning` options | Config-based behavior flags, not alias content |

There is no seed-data or config-file source that currently defines the base Persian metric alias catalog for symbol lookup. The default alias set is compiled into code.

### Confirmed duplications

1. Semantic alias duplication
   - PE and monthly-sales phrases appear in:
     - `PhaseOneFinancialSemanticCatalog`
     - `FinancialCopilotWorkflowDefinition.ContainsDirectMetricTerm(...)`
     - `LlmSymbolLookupParser` deterministic fallback arrays
     - prompt text in V2 and V1 intent handling

2. Intent duplication
   - Financial metric / analysis trigger lists are duplicated between:
     - V2 `BuildSystemInstructions()`
     - V1 `LlmAiIntentDetector.SystemPrompt`
     - V2 direct preflight code

3. Routing duplication
   - The semantic catalog knows what a metric phrase means.
   - The workflow gate separately decides whether the message is even allowed into deterministic symbol lookup.
   - The parser separately hard-codes recovery for some phrase families when the LLM output is incomplete.

### Recommended long-term design

Use one canonical metric phrase source for public metric queries and derive the other layers from it.

Recommended target design:

1. Make the semantic metric registry the canonical source for user-facing metric phrases.
2. Add an explicit capability/classification layer per metric definition, for example:
   - lookup-eligible
   - scanner-eligible
   - quote-context metric
   - monthly-activity metric
3. Replace broad static phrase gates with a normalization + resolver-first flow:
   - extract candidate metric phrase
   - normalize
   - resolve through `IMetricAliasResolver`
   - route by resolved metric capabilities
4. Keep deterministic parser fallbacks only for structural recovery:
   - entity extraction
   - mixed-script cleanup
   - company-name preservation
   not as the primary owner of metric vocabularies.
5. Keep prompt trigger lists shorter and architecture-derived, not as a second full copy of metric alias knowledge.

### Minimum safe fix for this bug without broad refactoring

Do not refactor the architecture in this bug fix. Use the existing layers with the smallest safe changes:

1. Add direct quote phrase support to the V2 direct metric phrase gate.
2. Add deterministic direct price / daily-change fallback to `LlmSymbolLookupParser`.
3. Add semantic catalog coverage for quote metrics so alias resolution can succeed:
   - add aliases for `LATEST_PRICE`
   - add a proper metric definition for `DAILY_CHANGE_PCT` and its aliases, because it is currently not in the catalog
4. Update the relevant prompt trigger list(s) so the agent path stays consistent with deterministic routing.

This keeps the fix local and production-safe while avoiding a broader resolver/routing redesign in the same change.

## Suspected Files / Classes / Components

- `FinancialCopilotWorkflowDefinition`
  - `IsDirectMetricLookupRequest`
  - `ContainsDirectMetricTerm`
  - `BuildSystemInstructions`
- `LlmSymbolLookupParser`
  - `TryParseDirectLookup`
  - direct deterministic metric parsing
- `PhaseOneFinancialSemanticCatalog`
  - `LATEST_PRICE` / `DAILY_CHANGE_PCT` alias coverage
- `MetricAliasResolver`
  - exact-alias-only resolution behavior
- `EfCoreSymbolMetricLookupService`
  - quote-context inclusion and quote fallback
- `ProviderMarketQuoteResolver`
  - runtime quote retrieval bridge
- `PersistedMarketDataProvider`
  - runtime quote data source and symbol matching
- `AiFacadeController`
  - API suppression of empty lookup tables

## One Bug Or Multiple Related Bugs?

This is at least two related issues:

1. Confirmed code regression / gap:
   - direct price and daily-change queries have no supported routing+alias path in V2.

2. Separate related runtime/data issue:
   - PE tables may show `LATEST_PRICE` / `DAILY_CHANGE_PCT` as `Missing` when quote data is unavailable or quote symbol matching fails.

The monthly-sales quote-omission change is not the direct root cause of either issue in the current code.

## Exact Recommended Fix Approach

Use the existing symbol lookup architecture. Do not add a parallel quote tool.

1. Extend V2 direct metric detection in `FinancialCopilotWorkflowDefinition` to include direct quote phrases:
   - `آخرین قیمت`
   - `قیمت`
   - `قیمت امروز`
   - `قیمت پایانی`
   - `تغییر قیمت`
   - `درصد تغییر قیمت`
   - `درصد تغییر روزانه`

2. Add deterministic direct quote parsing to `LlmSymbolLookupParser`, mirroring the existing PE/monthly-sales pattern:
   - preserve the company/symbol phrase exactly as written
   - map price phrases to `LATEST_PRICE`
   - map daily-change phrases to `DAILY_CHANGE_PCT`

3. Add explicit alias coverage in `PhaseOneFinancialSemanticCatalog` for:
   - `LATEST_PRICE`
   - `DAILY_CHANGE_PCT`
   with both Persian and English user-facing phrases.

4. Keep quote retrieval on the existing path:
   - parser
   - `SymbolLookupToolAdapter`
   - `EfCoreSymbolMetricLookupService`
   - `IMarketQuoteResolver`
   - persisted/live quote provider

5. Do not broaden the monthly-sales omission logic. Leave `ShouldIncludeMarketContext(...)` unchanged except for any bug fix needed to preserve current scoped behavior.

6. After the routing fix, separately verify quote availability in the runtime environment if PE tables still show `Missing`:
   - `LatestMarketQuotes` freshness
   - provider selection
   - `Companies.TseSymbol` / instrument ticker matching for affected symbols

## Regression Tests That Must Be Added

### Parser / unit

- `آخرین قیمت کچاد؟` -> `LATEST_PRICE`
- `قیمت کگل؟` -> `LATEST_PRICE`
- `قیمت امروز کچاد؟` -> `LATEST_PRICE`
- `قیمت پایانی کگل؟` -> `LATEST_PRICE`
- `تغییر قیمت کگل؟` -> `DAILY_CHANGE_PCT`
- `درصد تغییر قیمت کگل؟` -> `DAILY_CHANGE_PCT`
- `درصد تغییر روزانه کگل؟` -> `DAILY_CHANGE_PCT`

### Workflow / unit

- direct metric preflight recognizes price phrases and routes directly to `lookup_symbol_metrics`
- monthly-sales phrases still route as monthly-sales and still suppress quote columns

### Integration / API

- `آخرین قیمت کچاد؟` returns `SymbolLookup` with `LATEST_PRICE`
- `آخرین قیمت کگل؟` returns `SymbolLookup` with `LATEST_PRICE`
- `قیمت امروز کچاد؟` returns `SymbolLookup` with quote-backed price freshness labeling
- `تغییر قیمت کگل؟` returns `DAILY_CHANGE_PCT`
- `pe کگل؟` still returns `PE_TTM` and includes quote columns when seeded quote data exists
- monthly-sales queries still omit `LATEST_PRICE` and `DAILY_CHANGE_PCT`

### Runtime / provider verification

- quote lookup by `Companies.TseSymbol`
- quote lookup by direct instrument ticker fallback
- stale intraday row is labeled `PreviousTradingDay`, not `Live`

## Risks And Edge Cases

- `قیمت` is a broad term and can appear inside valuation phrases such as `نسبت قیمت به سود`; direct quote parsing must not steal PE intent from `قیمت به سود`.
- `تغییر` may appear in non-price contexts; daily-change aliases should require quote-style phrasing, not every generic “change” question.
- Price freshness labels must remain tied to quote source kind and trading date.
- Follow-up context handling must continue to reject contaminated conversation-history text as symbol/entity input.
- The fix must not reintroduce quote columns into monthly production/sales snapshots.

## Test Coverage Gap Confirmed

Current tests cover:

- PE symbol/company-name lookup with quote-context columns
- monthly-sales responses without quote columns

Current tests do not cover:

- direct latest price queries
- direct daily change queries
- explicit protection that monthly-sales quote omission does not disable direct price support

## Confirmation

No production code, prompts, migrations, or tests were modified for this investigation.
Only this bug report file was added.
