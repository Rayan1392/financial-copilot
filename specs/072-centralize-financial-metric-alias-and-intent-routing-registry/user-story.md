# User Story - Centralize Financial Metric Alias and Intent Routing Registry

## Story

As a FinancialCopilot platform operator and developer,

I want financial metric aliases, Persian/English user-facing phrases, and direct intent routing rules to be centralized in one canonical registry,

so that adding or changing a metric phrase such as `آخرین قیمت`, `PE`, `فروش ماهانه`, or `درصد تغییر روزانه` does not require duplicating the same vocabulary across catalog definitions, workflow phrase gates, parser fallbacks, and prompt text.

## Business Context

FinancialCopilot currently answers natural-language market and fundamental questions by combining semantic metric resolution, deterministic parser fallbacks, workflow routing, prompt-driven tool selection, and dynamic alias learning.

A regression in direct latest-price queries exposed a systemic architecture problem:

- `LATEST_PRICE` existed as a governed source metric.
- Persian phrases such as `آخرین قیمت`, `قیمت امروز`, and `قیمت پایانی` were not available as user-facing aliases.
- `DAILY_CHANGE_PCT` was used as quote context but was not defined in the semantic catalog.
- V2 direct metric routing used a separate hard-coded phrase gate.
- `LlmSymbolLookupParser` had deterministic fallbacks only for PE and monthly sales.
- Prompt-level trigger lists duplicated parts of the metric vocabulary.
- Runtime dynamic aliases existed, but they did not drive V2 direct phrase routing or parser fallbacks.

This means the product has multiple sources of truth for the same concept. The result is fragile behavior: a metric can exist in one layer but be invisible in another layer.

The goal of this feature is to create a canonical metric alias and routing registry, then progressively make workflow routing, parser recovery, prompt generation, and dynamic alias resolution consume that registry instead of maintaining disconnected hard-coded phrase lists.

## Problem Statement

Financial metric vocabulary is currently scattered across several layers:

1. Static semantic catalog
   - `PhaseOneFinancialSemanticCatalog`
   - Hard-coded C# metric definitions and aliases.

2. Runtime dynamic alias layer
   - `DynamicMetricAliases`
   - Admin-approved aliases consumed by `CompositeMetricAliasResolver`.

3. V2 workflow phrase gate
   - `FinancialCopilotWorkflowDefinition.ContainsDirectMetricTerm(...)`
   - Hard-coded phrase list used before alias resolution.

4. Symbol lookup deterministic fallbacks
   - `LlmSymbolLookupParser`
   - Hard-coded recovery paths for specific phrase families.

5. Prompt/system-instruction text
   - V2 and V1 routing prompts include duplicated trigger wording.

This violates single-source-of-truth design and causes regressions when a metric is added or renamed in only one layer.

## Goals

- Establish one canonical source for public metric aliases and routing capabilities.
- Make user-facing aliases reusable across:
  - metric alias resolution,
  - V2 direct metric routing,
  - deterministic parser recovery,
  - prompt/tool-routing instructions,
  - dynamic alias administration.
- Reduce duplicated phrase lists in workflow and prompt code.
- Preserve existing public behavior for PE, PS, EPS, margins, monthly sales, production, and quote context.
- Make quote metrics such as `LATEST_PRICE` and `DAILY_CHANGE_PCT` first-class lookup-capable metrics.
- Keep monthly production/sales quote-column omission scoped only to production/sales responses.

## Non-Goals

- Do not replace the existing `lookup_symbol_metrics` pipeline.
- Do not introduce a parallel quote tool or parallel market quote workflow.
- Do not remove the dynamic alias admin system.
- Do not rewrite the full AI orchestration framework.
- Do not change provider persistence, market quote storage, or the `LatestMarketQuotes` schema unless a separate data-provider bug requires it.
- Do not change user-facing pricing/credit rules.

## Current Architecture Summary

### Static Metric Catalog

`PhaseOneFinancialSemanticCatalog` is currently the main source of base metric definitions and aliases. Many aliases are hard-coded in C# with `Alias(...)` calls.

Known issue:

- `LATEST_PRICE` exists as a source metric but has no user-facing aliases.
- `DAILY_CHANGE_PCT` is used by quote enrichment but is not consistently defined as a semantic metric.

### Dynamic Alias Layer

The runtime alias layer is backed by `DynamicMetricAliases` and admin approval. It is consumed by `CompositeMetricAliasResolver`.

Known limitation:

- It helps only after a request reaches alias resolution.
- It does not currently drive V2 direct metric preflight or deterministic parser fallback phrase lists.

### V2 Workflow Routing

`FinancialCopilotWorkflowDefinition.ContainsDirectMetricTerm(...)` currently acts as a static phrase gate. It duplicates catalog vocabulary and can block valid metrics before they reach the resolver.

### Parser Fallbacks

`LlmSymbolLookupParser` includes deterministic fallback logic for PE and monthly-sales phrases. This is useful for structural recovery but currently also acts as a duplicated metric-vocabulary owner.

### Prompt Trigger Lists

Prompt/system instructions duplicate parts of the vocabulary. These lists can drift from the actual supported metric registry.

## Target Architecture

The semantic metric registry should become the canonical source for metric vocabulary and routing capabilities.

Each metric definition should expose at least:

- `MetricCode`
- Persian display name
- English display name
- Persian aliases
- English aliases
- metric family/category
- source/provider domain
- routing capabilities
- response behavior flags

Recommended capability flags:

- `LookupEligible`
- `ScannerEligible`
- `DirectQuestionEligible`
- `QuoteMetric`
- `QuoteContextMetric`
- `MonthlyActivityMetric`
- `ValuationMetric`
- `FundamentalMetric`
- `MarketStatisticMetric`
- `SuppressInMonthlyActivityResponses`

Routing should be derived from resolved metric capabilities rather than from unrelated hard-coded phrase lists.

Recommended flow:

```text
User query
  -> normalize text
  -> identify candidate symbol/company phrase and candidate metric phrase
  -> resolve candidate metric phrase through canonical alias resolver
  -> route by metric capabilities
  -> execute existing lookup/scanner/analysis pipeline
  -> render response according to metric response behavior flags
```

## Desired Behavior Examples

### Direct Quote Questions

- `آخرین قیمت کگل؟` -> `LATEST_PRICE`
- `قیمت امروز کچاد؟` -> `LATEST_PRICE`
- `قیمت پایانی کگل؟` -> `LATEST_PRICE`
- `تغییر قیمت کگل؟` -> `DAILY_CHANGE_PCT`
- `درصد تغییر روزانه کچاد؟` -> `DAILY_CHANGE_PCT`

These must route through the existing symbol lookup / market quote pipeline.

### Valuation Questions

- `pe کگل؟` -> `PE_TTM`
- `نسبت قیمت به سود کچاد؟` -> `PE_TTM`, not `LATEST_PRICE`
- `ps کگل؟` -> `PS_TTM`

Valuation answers may include quote context when quote data exists.

### Monthly Production/Sales Questions

- `آخرین فروش کگل؟`
- `فروش ماهانه کچاد؟`

These must continue to omit quote columns such as:

- `LATEST_PRICE`
- `DAILY_CHANGE_PCT`
- `آخرین قیمت`
- `درصد تغییر آخرین قیمت`

## Acceptance Criteria

### Canonical Registry

- A single canonical registry exposes base metric aliases and metric routing capabilities.
- Existing `PhaseOneFinancialSemanticCatalog` definitions are either extended or wrapped so that routing metadata is available without duplicating phrase lists elsewhere.
- `LATEST_PRICE` has Persian and English user-facing aliases.
- `DAILY_CHANGE_PCT` is defined consistently and has Persian and English user-facing aliases.
- The canonical registry can distinguish broad price phrases from PE phrases such as `قیمت به سود`.

### V2 Routing

- V2 direct metric routing no longer depends on a manually duplicated phrase list as the primary source of truth.
- Direct lookup routing uses alias resolution and metric capabilities where practical.
- `آخرین قیمت کگل؟` reaches the existing `lookup_symbol_metrics` path.
- `قیمت به سود کگل؟` continues to resolve as `PE_TTM`.

### Parser Recovery

- Deterministic parser fallbacks remain available only for structural recovery.
- Parser fallbacks do not become an independent vocabulary source.
- Parser tests prove PE, monthly-sales, latest-price, and daily-change questions resolve correctly.

### Prompt Consistency

- Prompt trigger text is reduced or generated from registry categories where practical.
- Prompt wording no longer contains a full duplicated list of all metric aliases.
- Prompt updates remain compatible with V1 and V2 behavior.

### Dynamic Alias Integration

- Dynamic aliases remain supported.
- The system documents clearly where dynamic aliases participate in routing.
- If dynamic aliases cannot safely drive preflight routing in this feature, the limitation must be documented and covered by follow-up tasks.

### Backward Compatibility

- Existing PE, PS, EPS, margin, monthly-sales, monthly-production, and scanner queries continue to work.
- Existing monthly-sales quote omission behavior remains unchanged.
- Existing admin dynamic alias behavior remains unchanged.
- Existing API response contracts remain compatible.

## Edge Cases

- `قیمت` alone is broad and should be treated as latest price only when it is not part of a known valuation phrase.
- `قیمت به سود`, `نسبت قیمت به سود`, and `P/E` must remain PE intent.
- `تغییر` alone should not become daily-change intent without quote-related wording.
- Company names that contain metric-like words must not be truncated incorrectly.
- Follow-up messages must not treat previous answer text as the new symbol phrase.
- Mixed Persian/English queries must be supported, such as `latest price کگل` and `daily change کچاد`.

## Observability And Diagnostics

The final implementation should make it easier to debug routing decisions by exposing structured diagnostics in logs or test-only diagnostics:

- normalized query
- detected symbol/company phrase
- detected metric phrase
- resolved metric code
- routing capability used
- selected tool/pipeline
- quote context inclusion/exclusion reason

This diagnostic information must not expose sensitive data and must not change public API contracts unless already supported by orchestration metadata.

## Rollout Strategy

1. Preserve current behavior with tests.
2. Add quote metrics and aliases safely.
3. Add registry capability metadata.
4. Update V2 routing to use registry-derived capabilities.
5. Update parser fallback to use registry-resolved metric codes where possible.
6. Reduce prompt duplication.
7. Add regression tests for direct price, PE protection, monthly-sales isolation, and dynamic alias behavior.

## Dependencies

- Existing `IFinancialMetricRegistry`
- Existing `IMetricAliasResolver` / `CompositeMetricAliasResolver`
- Existing dynamic alias infrastructure
- Existing `lookup_symbol_metrics` pipeline
- Existing market quote resolver/provider path
- Existing V1/V2 orchestration tests

## Reference Bug

This feature is motivated by the confirmed bug report:

`specs/bugs/direct-price-market-quote-routing-regression-2026-06-19.md`

## Change Request - 2026-06-20 - Period-Specific Financial Metric Aliases

The canonical alias/routing registry must support period-aware direct metric questions for
CyclicalWaves snapshot fields listed in spec `073`. The registry should resolve both the canonical
metric code and the requested relative period selector when phrases include terms such as
`آخرین فصل`, `فصل قبل`, `فصل مشابه سال قبل`, `آخرین ماه`, `ماه قبل`, `ماه مشابه سال قبل`, or
`سال قبل`.

This change keeps metric identity separate from period selection: for example, all three net-profit
margin quarter phrases resolve to `NET_PROFIT_MARGIN`, but with different selectors Q0, Q1, or Q4.
Likewise, `متوسط فروش ۱۲ ماهه` and `متوسط فروش ۱۲ ماهه سال قبل` both resolve to
`AVG_12M_MONTHLY_SALES`, with M0 and M12 selectors respectively.
