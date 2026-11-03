# Feature 075 — Product Revenue Mix Follow-up Implementation Report

Date: 2026-10-07

## 1. Scope decision

Feature 075 is the owning feature because it defines `product_revenue_mix`, the persisted revenue
mix, `ProductRevenueMixResponse`, ranking, and rendering. Feature 128 remains routing-only. Feature
075 was extended rather than creating a new feature: its future exclusions cover new trend
analytics, while this work only links to existing Features 136/137/138 capabilities.

## 2. Specification files

- Added `README.md`.
- Added `Design.md`.
- Extended `tasks.md` with Tasks 11–14.
- Added this `ImplementationReport.md`.

## 3. Production files changed

- `FinancialCopilot.Application/AI/Orchestration/ProductRevenueMixFollowUpSuggestionService.cs`
- `FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs`
- `FinancialCopilot.Infrastructure/ServiceCollectionExtensions.cs`

No V1, database, ingestion, DTO, API mapping, web component, Telegram renderer, or persistence
contract was added or changed.

## 4. Final action policy

The pure selector reads `ProductRevenueMixResponse.Products` in its returned/displayed order,
scans until it finds at most two safe products, then tries the parent-company trend. Product
actions target `monthly_product_trend`; the company action targets `monthly_activity_trend`.
Maximum actions: three. The selector never suggests `product_revenue_mix` itself.

## 5. Ranking source reused

The returned typed product collection is authoritative. `EfCoreProductRevenueMixRepository`
already orders it by `ProductRank`, and the displayed table consumes that order. The selector does
not sort by a second sales/share formula. A focused test deliberately supplies conflicting sales,
share, and rank values and proves that typed/display order wins.

## 6. Product round-trip validation

For every product candidate, the selector validates `MonthlyProductTrendIntentRules`, exact
normalized company/product parser slots, and the capability interpreter's existing governed
`product_sales_trend` route. It does not execute Feature 136. Duplicate normalized titles are
treated as ambiguous and excluded; parser-unsafe, aggregate, “other”, empty, placeholder, and
over-length titles are skipped while scanning continues.

The typed mix row has no stable vendor product key. The existing canonical `ProductName` normalized
with Feature 136's `MonthlyProductTrendCalculator.NormalizeProductText` is the bounded in-result
identity. No database re-query or duplicated Feature 136 identity reader was introduced.

## 7. Deterministic persistence

Successful typed ProductRevenueMix results with `Answered` or `PartialAnswer` set the existing
`DeterministicSuggestionsApplied` marker. The selector's exact collection is passed through the
existing workflow/persistence/API path. Feature 137 persistence tests prove authoritative non-empty
and explicit-empty collections skip generic guidance; the new API test proves the non-empty Feature
075 collection survives persistence into the response.

## 8. Zero-action behavior

Missing canonical symbol, disabled targets, or wholly unsafe product rows can produce zero actions.
When the successful typed policy applies, that empty collection remains authoritative. No generic
capability guidance fills missing slots. Non-success ProductRevenueMix paths retain existing generic
guidance behavior.

## 9. API round-trip evidence

`V2AiQuery_ProductRevenueMixSuggestedActions_RoundTripThroughNormalApi` uses the existing seeded
`کچاد` fixture with three ranked mix rows. It verifies three returned actions, submits the first
product action through `POST /api/ai/v1/query`, and receives resolved `MonthlyProductTrend` for
`گندله سنگ آهن`/`کچاد`. It then submits the company action and receives `MonthlyActivityTrend` for
`کچاد`. Result: **PASS (1/1)**.

## 10. Feature regressions

- Feature 136 / 137 / 138 and deterministic persistence were included in a targeted unit run:
  **PASS**, 162/162 across the combined routing/follow-up set.
- Full unit suite: **PASS, 1887/1887**.
- V1 production code was unchanged.

## 11. Routing regression checks

`Feature128SemanticRoutingTests`, `MonthlyProductTrendRoutingGateTests`, and the current
`ComprehensiveAnalysisSemanticRoutingRegressionTests` passed inside the 162-test targeted run,
including product semantic false-positive coverage and the recent routing regression tests.

## 12. Other verification

- New focused selector tests: **PASS, 12/12**.
- Architecture tests: **PASS, 12/12**.
- Telegram-relevant unit regressions: **PASS, 38/38**.
- Frontend suggested-action mapping/click test (`message-list.test.tsx`): **PASS, 7/7**.
- `git diff --check`: **PASS** (line-ending conversion warnings only).

## 13. Unrelated failures

- Before this regression fix, the ProductRevenueMix API theory was **7/8 passed** because
  `رکیب فروش محصولات کچاد؟` returned HTTP 500. The focused rerun after the fix passed that typo,
  both valid composition phrasings, and the SuggestedActions round-trip (**4/4**). The whole
  ProductRevenueMix endpoint class still has unrelated routing/shared-fixture failures (**2/13
  passed**): fundamental analysis resolves `Unknown`, comparison resolves `product_revenue_mix`, and
  concurrent requests observe the shared fake's outer-tool call counter.
- Running the whole ProductRevenueMix endpoint class also exposed current routing-work failures:
  `تحلیل بنیادی فولاژ؟` resolved `Unknown`, and the product-comparison regression resolved
  `product_revenue_mix`. The repository already had uncommitted changes in
  `DeterministicCapabilityInterpreter.cs` and `CanonicalQueryEntityResolver.cs`; those user-owned
  files were preserved.
- The broader two-file frontend run had one unrelated punctuation expectation failure in
  `monthly-product-trend-chart.test.tsx` (expected a final period that rendering omits). The
  suggested-action-specific `message-list` suite passed.
- A later solution-wide build was blocked by a newly appeared, user-owned untracked
  `ComprehensiveAnalysisV2ProductionPathTests.cs` file (virtual protected members declared on a
  sealed test factory). The solution build had passed with zero warnings/errors before that file
  appeared; the full unit suite and all task-specific projects continued to compile and pass.

## 14. Remaining blockers

No implementation blocker remains for ProductRevenueMix follow-ups. Repository-wide green status
requires the unrelated routing, typo-fixture, frontend punctuation, and user-owned integration-test
issues above to be resolved by their owning work.

## Typo-query robustness regression

Root cause: the canonical `ProductRevenueMixIntentRules` correctly rejected the misspelled
composition phrase, but the broader composition detector still recognized the query shape. The
workflow then fell through to direct financial-metric lookup because it contained `فروش` and a
symbol. The lookup adapter threw `simulated live-metric outage`; the uncaught workflow exception
propagated through `AiFacadeController` and produced HTTP 500 before persistence or response
mapping.

The workflow now returns the existing typed Unsupported outcome for composition-shaped wording
that is not a supported ProductRevenueMix phrase, before metric lookup. It emits no mix result and
suppresses generic help suggestions for that malformed composition; `DeterministicSuggestionsApplied`
semantics and successful ProductRevenueMix SuggestedActions are unchanged.

Regression: `V2AiQuery_TypoProductRevenueMixQuery_ReturnsDeterministicUnsupportedWithoutPayload`
passes (1/1). It verifies HTTP 200, deterministic `Unknown` / `Unsupported` / `capability_not_recognized`,
no ProductRevenueMix payload, and no SuggestedActions. Supported phrase coverage is retained in the
existing ProductRevenueMix API theory. ProductRevenueMix regression result: **PASS** for the new typo
case and valid phrase/API round-trip checks; the full endpoint class still includes unrelated
pre-existing failures described above.
