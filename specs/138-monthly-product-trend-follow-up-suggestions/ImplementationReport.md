# Feature 138 Implementation Report

## 1. Final decisions
- **D-A:** two company-context follow-ups only; no new product capability. 0/1/2 actions valid.
- **D-B:** `product_revenue_mix` = company-level composition context, message `ترکیب فروش محصولات {symbol}`; never product-specific wording.
- **D-C:** successful usable product trend with zero valid actions persists an explicit empty deterministic collection (no generic guidance).
- **D-D:** `Feature137SuggestionsApplied` -> `DeterministicSuggestionsApplied` (see README).

## 2. Discovered / rejected capabilities
Accepted: `product_revenue_mix`, `monthly_activity_trend`. Rejected: rate-focus `monthly_product_trend` (Focus ignored by use case and executors; same typed result), `product_sales_trend` (same use case/result), `product_sales_value` (subset of the answer), product YoY / quantity / product share (no capability; `monthly_product_comparison` is company-wide).

## 3. Final action policy
Priority 1 `product_revenue_mix` -> `ترکیب فروش محصولات {symbol}`; priority 2 `monthly_activity_trend` -> `روند فروش ماهانه {symbol}`. Reason `monthly_product_trend_follow_up`, kind `RunRelatedCapability`, `PresetSlots {symbol}`, `RegistryVersion = registry.Version`, ID `feature138:` + SHA-256(feature, reason, capability, registry version, ExternalCompanyId, ProductKey). Each action must pass, in memory: capability enabled in registry; the capability's intent rules (`LooksLike…`, `ExtractCompanySymbol` equals canonical symbol); not a product-trend query; `ICapabilityInterpreter` top candidate equals the expected code, no missing slots, exactly one entity equal to the symbol. Known parser limits: mix accepts 2-5 character symbols, company trend 2-6; multi-word symbols are rejected by the single-entity check.

## 4. Implementation
- New: `Application/AI/Orchestration/MonthlyProductTrendFollowUpSuggestionService.cs` (pure, no I/O), DI singleton in `ServiceCollectionExtensions.cs`.
- `FinancialCopilotWorkflowDefinition.ExecuteResultComputationStepAsync`: Feature 138 applies when intent is `MonthlyProductTrend`, result `IsResolved`, at least one non-gap point with a sales value, outcome Answered/PartialAnswer. Driven by the typed result, not phrasing. Feature 137 branch unchanged.
- Marker rename in `FinancialCopilotWorkflowMessages.cs`, `FinancialCopilotWorkflowDefinition.cs`, `MessagePersistenceFunction.cs`, `MessagePersistenceFeature137Tests.cs`. No new DTO; existing API/conversation/web/Telegram transport reused.
- Tests: `tests/FinancialCopilot.UnitTests/MonthlyProductTrendFollowUp138Tests.cs` (11 cases incl. 3 company/product fixtures, mix/trend unavailable, both unavailable -> empty, no duplicate of current result, long symbol, ordering/IDs/version, implicit vs explicit 12-month); API test `V2AiQuery_ProductTrend_ReturnsFeature138DeterministicCompanyContextActions` (2 phrasings) in `AiFacadeV2EndpointTests.cs`.

## 5. Results (2026-10-07)
- Feature 138 unit: 11/11 pass (theory expanded). Combined with Feature 136/137 unit classes and persistence tests: 60/60.
- Integration (ProductTrend / Feature137 / SuggestedAction / MonthlyActivityTrend filter): 23/23, including both Feature 138 phrasings returning identical two actions and the Feature 137 round-trips.
- Full unit: 1839 pass, 2 fail (`ConversationalCapabilityRegistryTests.PrecedencePolicy_ResolvesKnownConflictsDeterministically`, `SemanticDialogueGovernanceTests...ranking-fa` routes to `product_sales_trend`). Both concern interpreter/routing, which this feature does not modify; treated as pre-existing, not verified against a clean checkout.
- Architecture: 12/12. Telegram unit filter: 98/98. Frontend: 72 pass, 2 fail (disclosure timezone suffix; failed product-trend chart message text), neither touches suggested actions or files changed here; suggested-action tests pass.
- Integration/Architecture were built to a separate output path because Visual Studio holds `FinancialCopilot.API` binaries.
- V1: no V1 code changed; V1 never sets the marker, so it keeps generic guidance.

## 6. Zero-action behavior
Covered by unit test (both capabilities disabled -> empty, non-null) and by the marker semantics tested in `MessagePersistenceFeature137Tests` (applied + empty -> persisted empty, guidance not called).

## Final Regression Attribution

Baseline: temporary git worktree at `4a22218` (HEAD before Feature 138; contains Features 136/137 and unrelated admin work, excludes all Feature 138 changes). Bisect worktree checkouts of `ee9a6a1`, `bba705a`, `3a0796d` were used to date each failure. The worktree was removed afterwards; the working tree was never touched.

| Failure | Baseline result | Current result | Classification | Cause | Action |
|---|---|---|---|---|---|
| `ConversationalCapabilityRegistryTests.PrecedencePolicy_ResolvesKnownConflictsDeterministically` (`رتبه‌بندی کیفیت فروش ماهانه`) | FAIL (identical) | FAIL | CAUSED_BY_FEATURE_136_137_RECENT_CHANGES | Passes at `ee9a6a1` and `bba705a`, fails from `3a0796d` ("128 & 137 & some changes"), which added to `DeterministicCapabilityInterpreter` a `monthlyProductTrendQuery` branch scoring `product_sales_trend` 0.99, and loosened `MonthlyProductTrendIntentRules` product evidence. The ranking phrase contains "فروش ماهانه" and leftover tokens, so it is read as a product trend (mechanism inferred from the diff, commit-level attribution measured). | Not fixed here; separate follow-up (tighten product-trend gate or lower its precedence vs ranking). |
| `SemanticDialogueGovernanceTests...` case `ranking-fa` -> `product_sales_trend` | FAIL (identical) | FAIL | CAUSED_BY_FEATURE_136_137_RECENT_CHANGES | Same root cause and commit as above. | Same follow-up. |
| Frontend disclosures: missing-publication test expects `(تهران)` suffix | FAIL (identical; also fails at `ee9a6a1`, `bba705a`, `3a0796d`) | FAIL | PRE_EXISTING_UNRELATED (likely ENVIRONMENT: Node/ICU time-zone formatting) | Formatter output `۱۴۰۵/۰۴/۱۱, ۰۰:۰۰` has no suffix. Not investigated further. | None. |
| Frontend `monthly-product-trend-chart.test.tsx` "renders a failed product trend message once in chat" | FAIL (identical) | FAIL | CAUSED_BY_FEATURE_136_137_RECENT_CHANGES | Passes at `ee9a6a1`/`bba705a`, fails from `3a0796d`: its `message-list.tsx` change computes `displayMessage` with `replace(/[.。]\s*$/u, "")`, stripping the trailing period, so the test's exact text `...یافت شد.` is not found. Cosmetic rendering change vs stale expectation; not Feature 138 (no frontend change, `suggestedActions` empty in that test). | Not fixed here; decide whether to update the test or keep the period. |

Commands: `git worktree add --detach /d/Source/tahlil-baseline138 HEAD`; `dotnet test --filter "...PrecedencePolicy_ResolvesKnownConflictsDeterministically|...VersionedDataset_CoversEveryExecutableRouteAndPassesOfflineRegression"` in the baseline worktree and at the bisect commits; `npx vitest run <disclosures.test.ts> <monthly-product-trend-chart.test.tsx>` with the baseline `node_modules` junctioned.

- Feature 138 regressions found: 0.
- Feature 136/137-era regressions found: 3 failing tests / 2 root causes (ranking misrouting, trailing-period stripping), both from commit `3a0796d`.
- Remaining unrelated failures: the disclosures timezone test.
- Sanity rerun on the current tree: unit (Feature 138, 136, 137, persistence, Telegram) 158/158; API/integration filter 23/23; architecture 12/12; frontend suggested-action test passes.
- Production files changed during closeout: none.
- Closeout status: FEATURE_138_COMPLETE (the two ranking routing failures and the chart-message failure are tracked separately as Feature 136/137-era issues).

## Follow-up: ranking routing regression (from `3a0796d`) fixed separately

Not a Feature 138 change. `MonthlyProductTrendIntentRules.HasProductEvidence` now ignores analysis meta-vocabulary (ranking / quality / report words, ZWNJ-insensitive) when deciding whether a "product" candidate is credible, so `رتبه‌بندی کیفیت فروش ماهانه` no longer yields a product trend. Tests: `MonthlyProductTrendRoutingGateTests`. Full unit suite 1851/1851; architecture 12/12. Integration routing/revenue-mix filter: 19 failures identical with and without the fix (pre-existing). Known unrelated quirk left alone: `ProductSemanticIntentRules` possessive-"ش" fallback treats words ending in ش (e.g. `گزارش`) as product mentions.
