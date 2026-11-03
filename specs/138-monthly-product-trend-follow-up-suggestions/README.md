# Feature 138 — Monthly Product Trend Follow-up Suggestions

Status: **Implemented (V2 only).** Created instead of extending Feature 137 (see "Why not a Feature 137 extension"). Decisions D-A..D-D below are final.

## Problem

Feature 137 adds deterministic `SuggestedActions` after a successful **company** monthly sales trend (company → product drill-down). A successful **product** trend (`monthly_product_trend`, Feature 136), such as `روند فروش گندله کگهر` or `روند فروش گندله کگهر در ۱۲ ماهه اخیر چطور بوده؟`, returns no deterministic actions; only generic guidance can appear.

## Goal

After a successful, resolved `MonthlyProductTrend` result, return at most three structured `SuggestedAction` items that continue analysis of the **same company/product context**, are chosen by a deterministic policy over capabilities that really exist, and are never semantic duplicates of the query just answered. The LLM does not choose them. Company, product and company symbol come from the typed result, not from re-parsing user text, so the implicit and explicit 12-month phrasings behave identically.

## Capability discovery (2026-10-07, current `develop`)

| Candidate | Exists | Accepts product slot | Verdict |
| --- | --- | --- | --- |
| `monthly_product_trend` with rate focus (`روند نرخ فروش X Y`) | Parser sets `Focus.Rate`, but `MonthlyProductTrendQueryUseCase` and both semantic executors (`product_sales_trend`, `product_sales_value`) never read `Focus` (executors hard-code `Sales`). The result type already carries `CalculatedSaleRateToman` for every point and the web chart renders it. | n/a | **Rejected: same capability, same typed result.** It would be a pure wording variant (a loop). |
| `product_sales_trend` | Yes (Feature 128) | Yes | **Rejected:** same use case and result as `monthly_product_trend`. |
| `product_sales_value` | Yes (Feature 128) | Yes | **Rejected:** a single latest value; a strict subset of the answer just shown. |
| Product YoY / same-period-last-year comparison | **No** capability. `monthly_product_comparison` is company-wide (symbol only, no product slot; `LooksLikeMonthlyProductComparisonQuery` explicitly yields to product-specific queries). | No | **Rejected: does not exist for one product.** |
| Product quantity trend | **No** separate capability (`Focus.Quantity` exists only on the company-wide comparison enum). | n/a | **Rejected: does not exist.** |
| "سهم {product} از فروش {company}" (product share) | **No** product-specific capability. `product_revenue_mix` takes only `symbol`. | No | **Rejected as worded.** The company-level mix is usable instead (next row). |
| `product_revenue_mix` (`ترکیب فروش محصولات {symbol}`) | Yes | Symbol only | **Accepted:** same company context, shows the product's contribution among all products. Label must say "محصولات {symbol}", not imply a product-specific share. |
| `monthly_activity_trend` (`روند فروش ماهانه {symbol}`) | Yes (Features 076/077/113) | Symbol only | **Accepted:** parent-company zoom-out. Different capability and different analytical scope. |

## Final deterministic policy (proposed)

Evaluated in order; each step is skipped, never replaced by free text, when it fails validation. Maximum 3, currently at most 2 reachable.

1. **Same product, different analytical angle** — *no eligible capability today* (D-A). Reserved for a future distinct capability.
2. **Company contribution/context** — `product_revenue_mix`, message `ترکیب فروش محصولات {CanonicalSymbol}`, reason `monthly_product_trend_follow_up`.
3. **Parent company zoom-out** — `monthly_activity_trend`, message `روند فروش ماهانه {CanonicalSymbol}`, reason `monthly_product_trend_follow_up`.

Validation per action, in memory, without executing the capability: the generated message must round-trip through `ICapabilityInterpreter.Interpret` (top candidate equals the expected capability code and the single company entity equals the canonical symbol) and through that capability's intent rules (`ProductRevenueMixIntentRules.ExtractCompanySymbol`, `MonthlyActivityTrendIntentRules.ExtractCompanySymbol`). Known consequence: `ProductRevenueMixIntentRules` accepts symbol tokens of 2–5 characters only, so longer symbols simply lose the mix action.

## Applicability

Applies only when the V2 typed result is `MonthlyProductTrendResult` with `IsResolved`, a non-empty `CompanySymbol`, and at least one non-gap point with a sales value, and the dialogue outcome is `Answered` or `PartialAnswer`. Clarification, ambiguous, not-found, unsupported-time-window, no-data and failed outcomes produce no deterministic actions and keep today's generic guidance. `product_sales_value` results (`ProductSalesValuePayload`) are out of scope.

## Why not a Feature 137 extension

- Feature 137 Design §4 scopes its selector to "the successful company trend capability. It is not a general recommendation service", and §23 lists "No general cross-capability recommendation engine" as a non-goal. A product-trend policy choosing among `product_revenue_mix` / `monthly_activity_trend` is a cross-capability follow-up policy.
- D-11 and AC-22 bind authoritative actions to successful company-trend responses; this feature changes that contract by generalizing `Feature137SuggestionsApplied`.
- The result the request expected (rate trend, YoY, product share) is not available from current capabilities, so the shipped behavior differs materially from the requested examples and needs a product decision.

## Persistence decision (proposed)

Option A: generalize `Feature137SuggestionsApplied` to a capability-agnostic `DeterministicSuggestionsApplied` on `ResultsComputedMessage`, `PersistenceCompletedMessage` and `MessagePersistenceFunction`. Semantics are unchanged: when true, the supplied collection (possibly empty) is authoritative and `CapabilityGuidanceService.Suggest` is skipped. This avoids a second boolean per feature. Feature 137 behavior is preserved; only the name and its call sites change.

## Design decisions (final)

- **D-A:** Two company-context actions are sufficient; Feature 138 does not introduce a new product analytical capability (no product rate / YoY / quantity / share). 0, 1 or 2 actions are all valid; no third action is fabricated. A future true product-level capability is a separate feature.
- **D-B:** `product_revenue_mix` is accepted only as company-level composition context ("zoom out from this product to see the company's sales composition"). Its wording is exactly `ترکیب فروش محصولات {symbol}`; it must never be worded as a product-specific share (`سهم گندله از فروش کگهر` is not allowed).
- **D-C:** A successful, resolved, usable product trend with zero valid Feature 138 actions persists an explicit empty deterministic collection; `CapabilityGuidanceService` is not substituted. Generic guidance is unchanged for clarification, ambiguous, not-found, unsupported-window, no-data, failed and unrelated paths.
- **D-D:** `Feature137SuggestionsApplied` is generalized to the capability-agnostic `DeterministicSuggestionsApplied` (workflow messages and `MessagePersistenceFunction`). Meaning: true = the workflow owns the `SuggestedActions` collection, including an explicit empty one; false = existing generic guidance applies. Feature 137 behavior is unchanged; no `Feature138SuggestionsApplied` exists. The marker is not persisted, so stored payloads are unaffected.

## Out of scope

LLM-chosen actions; changes to Feature 136/137 calculations; V1 (frozen per `specs/POLICY-V1-FREEZE.md`); new frontend components; new DTOs (`SuggestedAction` is reused).
