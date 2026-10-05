# Feature 137 — Design

## 1. Current architecture

All chat queries enter through `POST /api/ai/v1/query` in `AiFacadeController`. The active development mode is Microsoft Agent Framework V2. The V2 workflow performs semantic/tool orchestration, deterministic result computation, persistence, and final response construction. The older `AiQueryOrchestrationService` V1 path remains available for emergency rollback but is frozen for new capabilities.

The relevant boundaries are:

| Boundary | Existing implementation | Role |
| --- | --- | --- |
| Company resolution | `ICompanyResolverService`, `ResolvedCompany` | Maps symbol/company text to canonical company identity and symbol. |
| Company trend intent | `MonthlyActivityTrendIntentRules` | Recognizes the existing company-level monthly activity trend capability and declines validated product trend requests. |
| Company trend query | `IMonthlyActivityTrendQueryUseCase`, `MonthlyActivityTrendQueryUseCase` | Reads persisted company trend snapshots and returns a typed response. |
| Product trend intent | `MonthlyProductTrendIntentRules` | Parses product and company slots and determines sales-versus-rate focus. |
| Product trend query | `IMonthlyProductTrendQueryUseCase`, `MonthlyProductTrendQueryUseCase` | Resolves a company-scoped product and returns a typed product time series. |
| Structured actions | `SuggestedAction`, `SuggestedActionHttpResponse` | Existing metadata contract for executable follow-up actions. |
| Web rendering | `chat.functions.ts`, `message-list.tsx` | Maps and renders actions as clickable buttons; click submits the action message and ID. |

## 2. Existing company monthly sales flow

The company-level flow is already implemented and must remain the source of the company answer and chart:

1. `MonthlyActivityTrendIntentRules.LooksLikeMonthlyActivityTrendQuery` detects trend/chart/monthly production-sales phrasing. It is semantic-capability routing logic, not a new Feature 137 phrase grammar.
2. V2 `FinancialCopilotWorkflowDefinition` reserves the request, invokes `IMonthlyActivityTrendQueryUseCase`, builds the deterministic Persian trend content, and marks the result as `DetectedIntent.MonthlyActivityTrend`.
3. `MonthlyActivityTrendQueryUseCase` resolves the company and reads `ICompanyMonthlyActivityTrendSnapshotRepository`.
4. The persisted source is `FinancialIngestionDbContext.CompanyMonthlyActivityTrendSnapshots`, mapped to `CompanyMonthlyActivityTrendSnapshotRow`. The response contains the latest amount, year-over-year amount/growth, 12-month average, YTD context, 12 chart points, insights, missing points, provider, and calculation time.
5. The V2 workflow carries the typed result through workflow messages and `MessagePersistenceFunction` into `AssistantMessagePayload`.
6. `AiFacadeController.MapMonthlyActivityTrendResult` maps it to `MonthlyActivityTrendChartResponse`; `chat.functions.ts` maps `monthlyActivityTrendResult`; the existing chart renders company sales bars and the existing company average.

The company trend query has no product list and should not be expanded to calculate product suggestions inside its snapshot calculator. Feature 137 adds a separate post-result selector using the product read model.

## 3. Existing product monthly sales flow

Feature 136 already provides the required product-level capability:

1. `MonthlyProductTrendIntentRules` extracts a product slot and company slot, accepts Persian/English trend/rate semantics, and prevents company-only queries from entering the product branch.
2. V2 gives precedence to `ProductRevenueMix` and `MonthlyProductComparison`, then `MonthlyProductTrend`, then exact company-only `MonthlyActivityTrend`.
3. `MonthlyProductTrendQueryUseCase` uses `ICompanyResolverService` and `IMonthlyProductComparisonReadRepository`.
4. `EfCoreMonthlyProductComparisonRepository` reads `MonthlyReports` joined to `MonthlyReportLineItems` and applies all of these predicates: matching external company, `ReportType == ProductSales`, `OutputType == 0`, and `IsAccepted`.
5. Product identity is company-scoped. The existing key prefers provider product code, then positive provider product ID, then normalized title plus normalized unit. The trend use case preserves `Resolved`, `NotFound`, and `Ambiguous` outcomes rather than selecting an ambiguous product. Feature 137 uses the exact same Feature-136-compatible candidate grouping and title selection; it does not introduce a renamed-product title policy.
6. With no explicit period, the product query selects the latest period in which the selected product has a non-null sales amount and returns that position plus the prior eleven fiscal-month positions. Missing positions are explicit gaps. One valid period is sufficient for the existing product trend query; there is no undocumented twelve-observation minimum.
7. Sales rate is calculated deterministically from sales value and sale quantity with checked decimal arithmetic. The provider rate is not used as the product trend calculation source.
8. `MonthlyProductTrendResult` is already carried through V2 workflow messages, conversation persistence, API mapping, frontend mapping, and Telegram rendering.

Therefore a product-level sales trend query exists. Feature 137 depends on it and must reuse its canonical identity, source eligibility, ambiguity, gap, and calculation semantics.

## 4. Proposed architecture

Add a narrowly scoped deterministic follow-up selector for the successful company trend capability. It is not a general recommendation service.

Proposed application boundary:

```text
IMonthlySalesProductFollowUpSuggestionService
    BuildAsync(MonthlyActivityTrendResponse trend, CancellationToken)
        -> IReadOnlyList<SuggestedAction>
```

The selector resolves the company once through `ICompanyResolverService`. It uses the exact canonical symbol chain `TseSymbol ?? Ticker ?? CompanySymbol`. If no canonical symbol is available, it returns zero Feature 137 actions.

The minimum-change implementation is a bounded extension beside `IMonthlyProductComparisonReadRepository`: one company-scoped set-based read returns the common anchor period, anchor observations, and the selected columns for the same accepted product-sales candidate universe that Feature 136's default resolver considers. “Bounded” means one external-company scope and one projection/read, not one query per product; the anchor itself is bounded by the company trend period. The repository query uses the existing accepted `MonthlyReports`/`MonthlyReportLineItems` read model and predicates. It does not run one query per product, call `MonthlyProductTrendQueryUseCase`, call a provider, or call an LLM.

The candidate projection is passed through one shared identity helper extracted from Feature 136's existing `ProductKey`, `ToCandidate`, grouping, and matching semantics. The extraction is behavior-preserving; Feature 136's production behavior is not changed. The helper exposes `ProductKey`, canonical `DisplayTitle`, company scope, provider identity, parser-safe product text, and whether the candidate has at least one non-null `SalesAmount` observation.

For each bounded candidate, parser safety is checked in memory by constructing `روند فروش {DisplayTitle} {CanonicalCompanySymbol}` and applying `MonthlyProductTrendIntentRules.LooksLikeMonthlyProductTrendQuery` and `BuildQuery`. The parsed product and company slots must normalize exactly to the candidate title and canonical symbol, and the shared Feature-136-compatible matcher must resolve the parsed product to exactly one `ProductKey`. Titles that are stripped as stop words, produce an empty/changed product slot, fail the product-trend gate, or resolve to zero/multiple keys are excluded. This is pure deterministic validation over the bounded candidate set, not a per-product Feature 136 execution.

The service is invoked only when:

- the request has resolved to the existing company-level monthly sales trend capability;
- the company trend result is usable and has a canonical company identity; and
- the request is not already a product trend, product comparison, or revenue-mix request.

The service returns the existing `SuggestedAction` type. The action kind is `RunRelatedCapability`, the capability code is `monthly_product_trend`, the label is the canonical product query, and the message is the exact query to submit.

## 5. Sequence / request flow

```text
Natural-language company trend query
        |
        v
Semantic routing / existing MonthlyActivityTrend capability
        |
        v
Deterministic company snapshot query + existing chart result
        |
        v
Resolve canonical company identity
        |
        v
Read one bounded company-scoped ProductSales candidate set and common anchor period
        |
        v
Normalize/group with the shared Feature-136-compatible identity/title helper;
reject parser-unsafe or non-round-trippable titles
        |
        v
Rank by anchor-period sales value, tie-break deterministically, take 3
        |
        v
Build SuggestedAction metadata using canonical title + symbol
        |
        v
Persist/map existing SuggestedActions contract
        |
        v
Web/Telegram renders structured follow-ups; click submits normal product query
```

The product follow-up query then enters the normal V2 `MonthlyProductTrend` path. The action does not bypass routing or invoke the product use case directly from the client.

## 6. Data sourcing

The source of truth for candidate products is the normalized monthly product-sales read model:

- table/entity: `MonthlyReports` / `NormalizedMonthlyReportRow`;
- line items: `MonthlyReportLineItems` / `NormalizedMonthlyReportLineItemRow`;
- company relationship: `ExternalCompanyId` resolved from `ResolvedCompany.ExternalCompanyId`;
- product identifiers: `ProviderProductCode`, positive `ProviderProductId`, and the existing guarded title-plus-unit fallback;
- display fields: `Title` and `Unit` from the accepted product line item;
- ranking value: `SalesAmount` in the source model’s million-Rial value semantics;
- period: Jalali fiscal month derived from `PeriodStart`/`PeriodEnd`.

Only accepted reports satisfying `ReportType = ProductSales`, `OutputType = 0`, and `IsAccepted = true` participate. Service-sales rows, YTD/adjustment output types, null output types, unaccepted revisions, and report-title inference are excluded exactly as in Feature 136.

The anchor period is the newest accepted `ProductSales` period with
`OutputType == 0`, usable product line items, and at least one non-null
`SalesAmount`, not later than the company trend's
`LatestReportYear/LatestReportMonth`. A report header without qualifying line
items is not an anchor. If no such period exists, no actions are returned.
All products are ranked inside this one common period; each product must not
use its own independently latest period.

## 7. Product eligibility rules

A product is eligible only when all rules pass:

1. The company was resolved through `ICompanyResolverService`.
2. The product occurs in the anchor accepted `ProductSales`/`OutputType = 0` period for that company.
3. Its canonical `ProductKey` can be built with the existing Feature 136 rules.
4. After grouping all same-key observations for the period, `SalesAmount` is non-null. A zero amount is a valid observed value and is not silently converted to missing.
5. The shared Feature-136-compatible resolver supplies the canonical
   `DisplayTitle`; Feature 137 never selects a latest-period title on its own.
6. The canonical display title is non-empty and is not the Feature 136
   missing-title fallback. Missing titles are excluded, not replaced with a
   fabricated label such as “other product”.
7. The generated full query passes the deterministic
   `MonthlyProductTrendIntentRules` round-trip check and resolves to exactly one
   `ProductKey` in the bounded shared candidate universe. No per-product
   `MonthlyProductTrendQueryUseCase` execution is allowed.
8. The normalized canonical title is unique among eligible action candidates.
   Distinct identities with the same display title are excluded together.

The existing product trend rule is the minimum-history rule: one valid sales-value observation is enough for a product trend result; older or missing months become typed gaps. Feature 137 does not invent a stricter observation count. The anchor-period requirement adds recency for follow-up relevance, not a new historical threshold.

## 8. Product ranking algorithm

The ranking metric is the product’s aggregated `SalesAmount` for the anchor period, in million Rial before presentation conversion. Revenue/value is selected rather than quantity because:

- the triggering response is a sales-value trend;
- sales value is comparable with the company-level sales total;
- product quantities may use incompatible units across products;
- the existing product trend domain already treats `SalesAmount` as the required latest-valid observation.

Algorithm:

1. Read the anchor period’s accepted product observations.
2. Build the existing product key for every observation.
3. Group observations by `ProductKey` and checked-sum non-null sales values.
   Use the exact Feature-136-compatible canonical `DisplayTitle`/unit selected
   by the shared resolver, not the latest-period title.
4. Remove candidates with no sales value, ambiguous title resolution,
   duplicate normalized display title, missing canonical title, or a failed
   parser round-trip.
5. Sort by descending aggregated sales value.
6. Break ties by normalized canonical title using ordinal comparison, then by `ProductKey` using ordinal comparison.
7. Take the first three.

The ordering is deterministic for the same accepted source revision. No ranking score, popularity heuristic, LLM result, market knowledge, or random tie-break is allowed.

## 9. Suggested prompt generation

For each selected candidate, build one existing `SuggestedAction`:

```text
Kind:            RunRelatedCapability
CapabilityCode:  monthly_product_trend
LocalizedLabel:  روند فروش {CanonicalProductTitle} {ResolvedCompanySymbol}
Message:         روند فروش {CanonicalProductTitle} {ResolvedCompanySymbol}
PresetSlots:     company={ResolvedCompanySymbol}, product={CanonicalProductTitle}
RelevanceReason: monthly_sales_product_follow_up
```

The product title is the canonical `DisplayTitle` selected by the shared
Feature-136-compatible resolver, including for renamed products. Normalization
is used for identity comparison and parser round-trip validation only. Persian/
Arabic Unicode variants are handled by the existing normalizer; the feature
must not maintain a second transliteration or variant table. The company symbol
is exactly `TseSymbol ?? Ticker ?? CompanySymbol`; company display name is never
used as a fallback.

`RegistryVersion` is the existing capability/action registry version exposed by
`IConversationalCapabilityRegistry.Version`; it is the Feature 137 action
version and is copied into `SuggestedAction.RegistryVersion`. Action IDs are a
bounded SHA-256-derived value from `feature137`, `monthly_product_trend`, the
registry version, canonical `ExternalCompanyId`, and `ProductKey`; raw Persian
text and display names are not used. IDs remain stable while those canonical
inputs and the registry version remain stable. A registry/policy change that
requires new action semantics must increment the existing registry version and
therefore intentionally changes the IDs. The existing action length bounds
remain authoritative.

The backend may optionally render a deterministic heading such as `پیشنهاد برای بررسی بیشتر` from the returned action list. The heading and labels are presentation only; the structured `SuggestedActions` array is the source of truth.

## 10. API/DTO contract changes

No new top-level `suggestedPrompts` field is required. The existing contract already provides the required structured metadata:

- application: `SuggestedAction`;
- API: `SuggestedActionHttpResponse` in `AiFacadeContracts.cs`;
- request correlation: `AiQueryHttpRequest.SuggestedActionId` remains available when a user clicks an action;
- conversation payload: `AssistantMessagePayload.SuggestedActions`;
- frontend: `AssistantChatBlock.suggestedActions` and `SuggestedAction`.

Feature 137 populates `SuggestedActions` for a successful company monthly trend
response. Existing clients that ignore this optional field continue to work.
For a successful `MonthlyActivityTrend` response, the V2 workflow persists a
non-null Feature 137 action set: one to three actions when eligible candidates
exist, or an empty collection when none exist. It must never contain placeholder
actions. Responses where Feature 137 does not apply retain the established
nullable generic-guidance behavior.

The action message is a complete normal-language product trend query, so the frontend does not parse answer text or assemble a query from hidden fields.

## 11. V2 action ownership and V1 behavior

Feature 137 is intentionally V2-only.

- MAF V2 ownership is: successful `MonthlyActivityTrend` computation -> deterministic Feature 137 selector -> `SuggestedActions` on `ResultsComputedMessage` -> `MessagePersistenceFunction` input -> `AssistantMessagePayload` and persisted conversation -> `PersistenceCompletedMessage` -> `AiQueryResponse` and `AiFacadeController` mapping -> existing frontend/Telegram rendering.
- `ResultsComputedMessage` and `PersistenceCompletedMessage` use a
  `Feature137SuggestionsApplied` marker so a non-null empty collection means
  “Feature 137 applied and no eligible products,” while null means the feature
  does not apply. `MessagePersistenceFunction` accepts the deterministic action
  collection and persists it unchanged.
- For a successful `MonthlyActivityTrend` result, Feature 137 actions are
  authoritative. `CapabilityGuidanceService.Suggest` is skipped for that
  result and cannot overwrite one to three actions or the explicit empty set.
- If the company trend fails, is a clarification/no-data response, or the
  result is not usable, the selector is not invoked and existing generic
  guidance behavior remains unchanged. Unrelated capabilities also retain
  existing generic guidance behavior.
- V1: do not add a new selector call, intent, parser, route, DTO, or response
  branch. Existing V1 company trend behavior remains unchanged and V1 returns
  no Feature 137 product follow-up actions. Existing V1 generic guidance
  behavior remains unchanged.
- If the emergency configuration switches from V2 to V1, absence of the new actions is an intentional documented capability difference, not a fallback that invents suggestions.
- Product trend clicks always return to the active normal routing path and, when V2 is active, resolve through `monthly_product_trend`.

This follows the repository’s V1 freeze policy and avoids duplicating the same feature in two orchestration systems.

## 12. UX integration expectations

The web frontend already renders `suggestedActions` as buttons after assistant content and calls `onSuggested(action.message, action.id)`. Feature 137 should use that existing component and placement; the existing company chart remains unchanged and the action buttons appear after it.

The frontend must display `LocalizedLabel`, submit `Message`, and preserve `Id` for attribution. It must not infer product identity from the assistant prose.

Telegram may render the same actions as bounded text because its interaction surface differs, but it must use the same structured actions and labels. A channel-specific renderer must not recalculate candidates.

## 13. Edge cases

| Case | Required behavior |
| --- | --- |
| Company has no product breakdown | Return the normal company response with zero actions. |
| Service-only company | Return zero actions; do not use service titles as products. |
| One eligible product | Return one action. |
| Fewer than three eligible products | Return exactly the available eligible count. |
| More than three eligible products | Return the top three by documented ranking. |
| Duplicate product titles | Exclude ambiguous duplicate-title identities; never return duplicate action text. |
| Renamed product | `ProductKey` determines identity; use the canonical `DisplayTitle` selected by the shared Feature-136-compatible resolver, not the latest-period title. Without provider evidence, title-plus-unit identity does not assert continuity. |
| Zero sales product | Keep a valid zero `SalesAmount` candidate, rank it after positive values, and include it only if it falls within the top three. |
| Missing recent month | Use the latest qualifying product period not later than the company trend period; a product absent from the anchor period is not suggested. |
| Sparse historical data | One valid anchor observation is sufficient under Feature 136; other months remain product-trend gaps. |
| Persian/Arabic Unicode variants | Normalize only for matching/deduplication and preserve the canonical source title in the action. |
| Invalid/unresolved company | Return zero actions without attempting product discovery. |
| Product exists only in historical periods | Do not suggest it for the current company response because current anchor-period contribution is unavailable. |
| Ambiguous product identity | Exclude the candidate; do not ask the LLM or generate a query known to be ambiguous. |
| Provider/read failure | Keep the company answer and chart; return zero actions and record a bounded diagnostic. |

## 14. Error/fallback behavior

Suggestion generation is non-critical enrichment. A failure, timeout, cancellation after the company result is available, or malformed candidate must not fail or alter the company trend response. The service returns zero actions on recoverable data problems.

No fallback may use assistant prose, raw LLM output, company average sales rate, market knowledge, a generic product name, or a product from another company. A product-level click that later returns `NotFound` or `Ambiguous` remains a normal typed product-trend outcome and is not retried with a fabricated title.

## 15. Performance and bounded query shape

The selector uses one bounded company-scoped repository read (or one bounded
repository query plus one in-memory shared resolver pass) after the company
trend query. The required shape is:

1. Resolve the company once and derive the canonical symbol.
2. Find the newest accepted `ProductSales`, `OutputType == 0` period not later
   than the company trend period where qualifying line items with at least one
   non-null `SalesAmount` exist.
3. Load that anchor's product rows plus the selected columns for the same
   accepted company-scoped candidate universe used by Feature 136's default
   resolver, in one set-based read.
4. Aggregate anchor `SalesAmount` by `ProductKey` with checked decimal sums.
5. Apply the shared canonical title/matcher and pure parser round-trip checks in
   memory; do not execute `MonthlyProductTrendQueryUseCase` per candidate.
6. Exclude null-value, ambiguous, duplicate-title, missing-title, and
   parser-unsafe candidates.
7. Sort deterministically and take three.

`GetAvailablePeriodsAsync` followed by one `GetPeriodAsync` is not sufficient
to prove the cross-period Feature-136-compatible identity universe, and a loop
over products is forbidden. No provider/API call, LLM validation, or repeated
period scan per candidate is allowed. A report header without qualifying line
items cannot become the anchor.

## 16. Caching considerations

No new cache is required for the first rollout. The selector is deterministic and can reuse the existing query/result caching boundaries if they already cover the company trend response.

If cached independently later, the key must include tenant/actor scope where required by the surrounding response cache, canonical external company ID, anchor period, accepted source revision/fingerprint, selector policy version, and requested maximum count. Ingestion of a newer accepted product-sales revision must invalidate or version-bump the result.

## 17. Observability/logging

Add bounded metrics/traces for:

- selector invocation and duration;
- company trend result state;
- anchor period and source revision availability;
- raw candidate count, eligible count, ambiguous/duplicate/stale exclusion counts;
- returned action count;
- zero-action reason code;
- action click attribution through the existing `SuggestedActionId`/suggestion events.

Do not log full user messages or sensitive payloads unnecessarily. Product keys and external IDs should be redacted or hashed in ordinary logs; evidence IDs may be included only at the existing diagnostic level.

## 18. Security

Suggestions use publicly available normalized market data but still execute inside the authenticated tenant/actor request context. The selector must honor the same authorization and cache scoping as the parent AI response. It must not expose raw database IDs as user-facing query text, allow a user-provided company ID to override the resolved company, or allow cross-company product leakage.

The action ID is an attribution token, not an authorization grant. On click, the normal API authentication, billing, routing, and company/product resolution rules still apply.

## 19. Backward compatibility

The existing API response shape remains compatible because `SuggestedActions` is already optional. Older persisted assistant messages without actions continue to decode. Clients that do not render actions ignore them. Company trend values, chart points, chart units, calculations, and prose remain unchanged.

The only intentional behavioral difference is that V2 company trend responses may contain up to three additional structured actions. V1 does not gain the feature.

## 20. Testing strategy

Unit tests must cover the selector and action builder independently from the LLM
and API. Required identity/title cases include stable `ProductKey`, renamed
products using the shared Feature-136-compatible canonical title, duplicate
display titles, parser-unsafe title rejection, and canonical title round-trip.
Ranking cases include zero/one/exactly-three/more-than-three products, null and
zero sales, ties by sales/title/`ProductKey`, and repeated-run equality.
Anchor cases include the common latest accepted ProductSales period, exclusion
of periods later than the company trend, a header with no qualifying line
items, and no anchor period. Provider failure, invalid companies, sparse data,
historical-only products, and Unicode normalization must remain non-critical.

Integration tests must prove:

- company trend response contains existing company analysis/chart plus structured actions;
- at most three actions are returned and each uses source-backed canonical data;
- each returned action message resolves through the normal `MonthlyProductTrend` capability;
- no eligible-product path succeeds without actions;
- invalid company behavior remains unchanged;
- successful company-trend actions are not overwritten by `CapabilityGuidanceService`;
- a successful company trend with no eligible products persists an empty
  Feature 137 action set and remains successful;
- V2 persistence/API reload preserves actions;
- generated action messages round-trip through normal V2 routing to Feature 136
  without per-candidate Feature 136 execution;
- V1 has no Feature 137 product actions while existing generic guidance remains
  unchanged;
- bounded repository-call/query-shape tests prove no N+1 behavior;
- company and product chart regressions remain green;
- the `SuggestedActionId` click path remains wired.

### Acceptance criteria

- **AC-1:** A company-level monthly sales trend response can return structured product follow-up suggestions.
- **AC-2:** At most three product suggestions are returned.
- **AC-3:** Each suggestion uses a real company-owned product and the canonical `DisplayTitle` selected by Feature-136-compatible resolution, including for renamed products.
- **AC-4:** The LLM does not invent or select product names.
- **AC-5:** Each suggested product is eligible for the existing product-level monthly sales trend flow through one bounded shared identity/queryability resolution; the implementation never invokes `MonthlyProductTrendQueryUseCase` once per candidate.
- **AC-6:** Products without sufficient usable history are excluded.
- **AC-7:** Suggestion ordering is deterministic.
- **AC-8:** Ranking is based on the documented business metric: anchor-period aggregated product `SalesAmount`.
- **AC-9:** The resulting query uses the Feature-136-compatible canonical `DisplayTitle` and the canonical company symbol `TseSymbol ?? Ticker ?? CompanySymbol`, subject to existing normalization; display name is not a fallback.
- **AC-10:** If there are no eligible products, the response succeeds normally with zero suggestions.
- **AC-11:** Existing company monthly sales analysis and chart output remain unchanged.
- **AC-12:** Existing company sales calculations remain unchanged.
- **AC-13:** Existing product sales calculations remain unchanged.
- **AC-14:** Suggestions are exposed as structured response data rather than requiring text parsing.
- **AC-15:** Different natural-language phrasings routed to the same monthly-sales capability receive equivalent suggestion behavior.
- **AC-16:** No exact-phrase grammar is introduced for activating this feature.
- **AC-17:** The feature does not introduce direct LLM-to-database access.
- **AC-18:** V1 and MAF V2 behavior is explicitly verified; V1 receives no Feature 137 product follow-up actions, while existing V1 generic guidance remains unchanged.
- **AC-19:** A company with fewer than three eligible products returns only the available eligible products.
- **AC-20:** Duplicate product suggestions are not returned.
- **AC-21:** Unsupported company types, including companies without meaningful product-level monthly sales data, return no fabricated suggestions.
- **AC-22:** Existing clients that ignore structured suggested actions continue to work, and generic `CapabilityGuidanceService` behavior remains unchanged outside successful Feature 137 company-trend responses.

## 21. Rollout considerations

Implement behind a V2 configuration/rollout flag if the existing rollout framework supports capability-level flags. Start with development data and a manufacturing/mining symbol such as `کچاد` if present in the current fixture/development dataset.

Validation sequence:

1. Submit `روند فروش کچاد` through `POST /api/ai/v1/query` with V2 active.
2. Verify the existing company trend text and chart payload are unchanged.
3. Verify zero to three actions, canonical titles, deterministic order, and `monthly_product_trend` capability metadata.
4. Submit at least one returned action message, such as `روند فروش گندله کچاد`, through the same API.
5. Verify the response reaches the typed product trend path, not company trend or LLM-generated fallback.
6. Repeat with no product data, duplicate/ambiguous products, and a service-only or unresolved company.

Roll back by disabling the V2 enrichment flag. The company trend path remains usable because suggestions are non-critical.

## 22. Explicit design decisions

D-1. Feature 137 is V2-only; V1 receives no Feature 137 product follow-up actions.
D-2. Existing `SuggestedAction` is reused; no parallel `SuggestedPrompt` contract is created.
D-3. Maximum returned product actions: three.
D-4. Ranking uses one common accepted company ProductSales anchor period.
D-5. Ranking metric is checked aggregate `SalesAmount`.
D-6. Product identity uses Feature-136-compatible `ProductKey` semantics.
D-7. Product title uses the Feature-136-compatible canonical `DisplayTitle`, not the latest-period title.
D-8. Company symbol precedence is `TseSymbol ?? Ticker ?? CompanySymbol`; no display-name fallback is allowed.
D-9. The selector is bounded and must not execute `MonthlyProductTrendQueryUseCase` per candidate.
D-10. Parser-unsafe or non-round-trippable titles are excluded.
D-11. Successful V2 company-trend Feature 137 actions are authoritative over generic guidance; zero eligible products persists an empty Feature 137 action set.
D-12. Zero eligible products produces a successful company response with zero Feature 137 actions.
D-13. Existing V1 generic guidance behavior remains unchanged.
D-14. Feature 136 production behavior and calculations are not changed by this feature.

The minimum usable history is one valid non-null sales-value observation;
sparse older periods remain gaps. Ambiguous, duplicate-title, and historical-
only products are excluded. `SuggestedAction.RegistryVersion` uses the
existing capability/action registry version, and IDs are bounded hashes of
versioned canonical identity inputs. Any optional textual heading is derived
from structured actions only.

## 23. Explicit non-goals

- No general cross-capability recommendation engine.
- No investment recommendation or suitability judgment.
- No LLM product discovery, ranking, title generation, or database access.
- No direct SQL generation by the LLM.
- No new vector store, external service, or provider call in the response path.
- No new production migration unless a later implementation review proves an existing index is insufficient; this design assumes the current accepted-report read model is adequate.
- No company sales-rate reuse for product suggestions.
- No change to product trend calculations, accepted revision rules, identity rules, chart formulas, or company chart output.
- No frontend redesign or requirement to parse Persian answer text.
- No new V1 intent, route, parser, capability, or API branch.

## 24. Finding-resolution matrix

| Finding | Status | Spec section changed | Resolution |
|---|---|---|---|
| M-01 | RESOLVED | §§4, 5, 7, 15, 20; Tasks 1–4, 14 | One bounded candidate read plus shared Feature-136-compatible identity/matcher; parser round-trip is pure in-memory validation; no per-candidate Feature 136 execution. |
| M-02 | RESOLVED | §§3, 7–9, 13, 20, 22; Tasks 2, 5, 6, 14 | `ProductKey` determines identity and the shared Feature 136 canonical `DisplayTitle` is used, including renames; latest-period title is not used. |
| M-03 | RESOLVED | §§10–11, 19–20; Tasks 8–10 | `ResultsComputedMessage` -> `MessagePersistenceFunction` -> payload -> `PersistenceCompletedMessage`/API is explicit; deterministic actions take precedence over generic guidance. |
| M-04 | RESOLVED | §§4, 9, 22; Tasks 2, 6 | Symbol is exactly `TseSymbol ?? Ticker ?? CompanySymbol`; missing symbol yields zero actions. |
| M-05 | RESOLVED | §11, AC-18/22; Tasks 11 | Only Feature 137 product actions are absent in V1; existing generic V1 guidance remains unchanged. |
| N-01 | RESOLVED | §9, §22 versioning paragraph; Task 7 | Existing capability/action registry version is the action version and `SuggestedAction.RegistryVersion`; IDs are bounded hashes of canonical versioned inputs. |
