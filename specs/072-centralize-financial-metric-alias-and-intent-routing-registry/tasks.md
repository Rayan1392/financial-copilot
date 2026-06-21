# Tasks - Centralize Financial Metric Alias and Intent Routing Registry

## Implementation Tasks

### 1. Baseline audit

- [ ] Review `PhaseOneFinancialSemanticCatalog` and list all metric definitions, display names, aliases, and source metrics.
- [ ] Review `DynamicMetricAliases` and `CompositeMetricAliasResolver` behavior.
- [ ] Review `FinancialCopilotWorkflowDefinition.ContainsDirectMetricTerm(...)` and document all hard-coded phrases.
- [ ] Review `LlmSymbolLookupParser` deterministic fallback phrase families.
- [ ] Review V1 and V2 prompt trigger lists.
- [ ] Identify every place where metric aliases, metric phrases, or intent trigger phrases are duplicated.
- [ ] Add an internal note or test fixture that captures the current duplication map.

### 2. Define canonical metric capability model

- [ ] Add or extend a domain model for metric routing capabilities.
- [ ] Support capability flags such as:
  - [ ] `LookupEligible`
  - [ ] `ScannerEligible`
  - [ ] `DirectQuestionEligible`
  - [ ] `QuoteMetric`
  - [ ] `QuoteContextMetric`
  - [ ] `MonthlyActivityMetric`
  - [ ] `ValuationMetric`
  - [ ] `FundamentalMetric`
  - [ ] `MarketStatisticMetric`
  - [ ] `SuppressInMonthlyActivityResponses`
- [ ] Ensure the capability model is immutable or safely cached.
- [ ] Keep compatibility with existing metric registry consumers.
- [ ] Avoid changing public API contracts unless explicitly required.

### 3. Extend semantic catalog coverage

- [ ] Add user-facing Persian and English aliases for `LATEST_PRICE`.
- [ ] Define `DAILY_CHANGE_PCT` consistently if it is missing from the semantic catalog.
- [ ] Add Persian and English aliases for `DAILY_CHANGE_PCT`.
- [ ] Assign quote-related capabilities to `LATEST_PRICE` and `DAILY_CHANGE_PCT`.
- [ ] Ensure `LATEST_PRICE` and `DAILY_CHANGE_PCT` are lookup-eligible and direct-question-eligible.
- [ ] Ensure quote metrics are not treated as monthly production/sales metrics.
- [ ] Add display names for quote metrics:
  - [ ] `آخرین قیمت`
  - [ ] `تغییر روزانه %` or the existing canonical Persian title used by the UI/API.

### 4. Build registry-derived routing helper

- [ ] Create a helper/service that can determine whether a query contains a direct metric phrase by consulting the canonical alias resolver/registry.
- [ ] The helper must return:
  - [ ] matched phrase
  - [ ] resolved metric code
  - [ ] metric capabilities
  - [ ] confidence or deterministic-match source if already supported by existing patterns
- [ ] Ensure longest-match precedence so `نسبت قیمت به سود` wins over generic `قیمت`.
- [ ] Add normalization for Persian variants:
  - [ ] Arabic/Persian Yeh and Kaf
  - [ ] punctuation
  - [ ] zero-width characters
  - [ ] common spacing variations
- [ ] Do not make broad generic words such as `تغییر` resolve without quote-specific context.

### 5. Update V2 direct metric routing

- [ ] Replace or wrap `ContainsDirectMetricTerm(...)` so it uses registry-derived routing where practical.
- [ ] Keep existing deterministic behavior for known stable phrases during migration.
- [ ] Ensure direct price questions route to `lookup_symbol_metrics`.
- [ ] Ensure PE phrases such as `قیمت به سود` route to `PE_TTM`, not `LATEST_PRICE`.
- [ ] Ensure monthly-sales phrases still route as monthly activity.
- [ ] Preserve existing V2 orchestration metadata behavior.

### 6. Update symbol lookup parser fallback behavior

- [ ] Refactor deterministic parser fallbacks so phrase-to-metric mapping comes from the registry where practical.
- [ ] Keep fallback responsibility focused on structural recovery:
  - [ ] symbol/company phrase extraction
  - [ ] mixed-script cleanup
  - [ ] punctuation and short-query handling
- [ ] Add direct quote fallback for:
  - [ ] latest price
  - [ ] today price
  - [ ] closing price
  - [ ] daily change percentage
- [ ] Do not break PE fallback.
- [ ] Do not break monthly-sales fallback.
- [ ] Preserve the company/symbol phrase exactly as written by the user.

### 7. Update prompt/tool-routing consistency

- [ ] Review V2 system/tool-routing prompt trigger lists.
- [ ] Replace full duplicated alias lists with registry-category wording where possible.
- [ ] Add only minimal direct quote wording if a full prompt refactor is risky.
- [ ] Review V1 `LlmAiIntentDetector` prompt and deterministic rules for compatibility.
- [ ] Ensure prompt wording does not conflict with deterministic routing.

### 8. Preserve monthly production/sales quote omission

- [ ] Verify `ShouldIncludeMarketContext(...)` remains scoped to monthly-activity-only responses.
- [ ] Ensure `LATEST_PRICE` and `DAILY_CHANGE_PCT` remain omitted for monthly production/sales answers.
- [ ] Ensure valuation, screening, ratio, and market-statistic answers can still include quote context when available.
- [ ] Add tests proving quote omission does not disable direct price support.

### 9. Dynamic alias integration

- [ ] Verify whether dynamic aliases can participate in direct routing safely.
- [ ] If supported in this feature, update the routing helper to consult `CompositeMetricAliasResolver`.
- [ ] If not supported in this feature, document the limitation clearly and add a follow-up task.
- [ ] Ensure admin-approved aliases do not accidentally override higher-priority canonical phrases.
- [ ] Add precedence rules:
  - [ ] exact canonical alias
  - [ ] longest canonical alias
  - [ ] active dynamic alias
  - [ ] fallback parser recovery

### 10. Tests - catalog and alias resolution

- [ ] Add tests proving `LATEST_PRICE` aliases resolve correctly:
  - [ ] `آخرین قیمت`
  - [ ] `قیمت`
  - [ ] `قیمت امروز`
  - [ ] `قیمت پایانی`
  - [ ] `latest price`
  - [ ] `price`
- [ ] Add tests proving `DAILY_CHANGE_PCT` aliases resolve correctly:
  - [ ] `درصد تغییر قیمت`
  - [ ] `درصد تغییر روزانه`
  - [ ] `تغییر روزانه درصدی`
  - [ ] `daily change`
  - [ ] `daily change percent`
- [ ] Add tests proving PE aliases still resolve correctly:
  - [ ] `pe`
  - [ ] `P/E`
  - [ ] `پی به ای`
  - [ ] `قیمت به سود`
  - [ ] `نسبت قیمت به سود`

### 11. Tests - parser behavior

- [ ] `آخرین قیمت کچاد؟` maps to `LATEST_PRICE` and symbol phrase `کچاد`.
- [ ] `قیمت کگل؟` maps to `LATEST_PRICE` and symbol phrase `کگل`.
- [ ] `قیمت امروز کچاد؟` maps to `LATEST_PRICE`.
- [ ] `قیمت پایانی کگل؟` maps to `LATEST_PRICE`.
- [ ] `تغییر قیمت کگل؟` maps to `DAILY_CHANGE_PCT`.
- [ ] `درصد تغییر قیمت کگل؟` maps to `DAILY_CHANGE_PCT`.
- [ ] `درصد تغییر روزانه کگل؟` maps to `DAILY_CHANGE_PCT`.
- [ ] `نسبت قیمت به سود کگل؟` maps to `PE_TTM`, not `LATEST_PRICE`.
- [ ] `قیمت به سود کگل؟` maps to `PE_TTM`, not `LATEST_PRICE`.
- [ ] `آخرین فروش کگل؟` still maps to monthly sales.

### 12. Tests - workflow routing

- [ ] V2 direct metric preflight recognizes latest-price phrases.
- [ ] V2 direct metric preflight recognizes daily-change phrases.
- [ ] V2 direct metric preflight recognizes PE phrases with price wording correctly.
- [ ] V2 direct metric preflight recognizes monthly-sales phrases correctly.
- [ ] V2 direct metric routing uses registry capabilities, not a disconnected phrase list.
- [ ] Prompt-driven fallback path remains compatible when preflight does not trigger.

### 13. Tests - integration/API

- [ ] `آخرین قیمت کگل؟` returns a `SymbolLookup` result with `LATEST_PRICE`.
- [ ] `آخرین قیمت کچاد؟` returns a `SymbolLookup` result with `LATEST_PRICE`.
- [ ] `قیمت امروز کگل؟` returns quote-backed latest price.
- [ ] `قیمت پایانی کچاد؟` returns quote-backed latest price or the appropriate latest/closing price according to existing provider semantics.
- [ ] `تغییر قیمت کگل؟` returns `DAILY_CHANGE_PCT`.
- [ ] `درصد تغییر روزانه کچاد؟` returns `DAILY_CHANGE_PCT`.
- [ ] `pe کگل؟` still returns `PE_TTM` and includes quote columns when seeded quote data exists.
- [ ] `ps کگل؟` still returns `PS_TTM` and includes quote columns when seeded quote data exists.
- [ ] `آخرین فروش کگل؟` still omits quote columns.
- [ ] `فروش ماهانه کچاد؟` still omits quote columns.

### 14. Diagnostics and logging

- [ ] Add structured diagnostics where appropriate for routing decisions.
- [ ] Diagnostics should include:
  - [ ] normalized query
  - [ ] matched metric phrase
  - [ ] resolved metric code
  - [ ] matched capabilities
  - [ ] selected route/tool
  - [ ] quote context inclusion/exclusion reason
- [ ] Keep diagnostics internal or behind existing diagnostic mechanisms.
- [ ] Do not change public response contracts unless already supported by orchestration metadata.

### 15. Documentation

- [ ] Document the canonical metric alias/routing registry design.
- [ ] Document how to add a new metric alias.
- [ ] Document how to mark a metric as lookup-eligible, scanner-eligible, quote-context, or monthly-activity.
- [ ] Document how dynamic aliases interact with canonical aliases.
- [ ] Document phrase precedence and ambiguity rules.
- [ ] Update the related bug report with the final resolution summary.

## Acceptance Checklist

- [ ] Metric alias ownership is centralized or clearly wrapped behind one registry interface.
- [ ] `LATEST_PRICE` and `DAILY_CHANGE_PCT` are first-class lookup-capable quote metrics.
- [ ] Direct price queries no longer fail with unsupported metric catalog errors.
- [ ] Daily-change queries route to `DAILY_CHANGE_PCT`.
- [ ] PE queries containing `قیمت به سود` still route to `PE_TTM`.
- [ ] Monthly production/sales answers still omit quote columns.
- [ ] Dynamic aliases remain compatible.
- [ ] Existing V1/V2 behavior remains backward-compatible.
- [ ] Regression tests cover direct price, daily change, PE protection, monthly-sales isolation, and valuation quote enrichment.
- [ ] No parallel quote tool or duplicate market quote workflow is introduced.

## Suggested Follow-Up Stories

- [ ] Move base metric aliases from hard-coded C# to reviewed seed data or versioned configuration if product wants runtime vocabulary management.
- [ ] Add an admin UI for reviewing canonical and dynamic aliases together.
- [ ] Add telemetry dashboards for unresolved metric phrases.
- [ ] Add automated suggestions for missing aliases based on failed user queries.

## Change Request Tasks - 2026-06-20 - Period-Specific Financial Metric Aliases

- [ ] Add period-aware aliases for all rows in the spec `073` coverage matrix.
- [ ] Return both `MetricCode` and period selector from registry-derived routing where the phrase
      contains an explicit relative period.
- [ ] Add longest-match precedence for phrases such as `حاشیه سود خالص فصل مشابه سال قبل` over
      generic `حاشیه سود خالص`.
- [ ] Add PS aliases if missing: `ps`, `P/S`, `پی به اس`, `قیمت به فروش`, `نسبت قیمت به فروش`.
- [ ] Add tests proving margin, monthly sales, average sales, PE, and PS aliases resolve to the
      intended metric code and period selector.
