# Feature 128 — Product Routing Implementation Report

Date: 2026-10-05

## 1. Product sales value implementation

`product_sales_value` is registered in the active V2 capability catalog and is executed by
`ProductSalesValueCapabilityExecutor`. It delegates retrieval to the existing
`IMonthlyProductTrendQueryUseCase`, then selects the latest non-gap point with a persisted
`SalesValueMillionRial` value. No new financial calculation or global product lookup was added.

## 2. Product sales trend implementation

`product_sales_trend` is registered in the active V2 catalog and is executed by
`ProductSalesTrendCapabilityExecutor`. It delegates directly to the existing Feature 129 monthly
product trend use case and returns its typed `MonthlyProductTrendResult`, including points and
evidence.

## 3. Existing Feature 129 reuse

Feature 129 remains the source of truth for monthly product observations, provider identity,
period selection, aggregation, gaps, rate status, and evidence. Feature 128 adds semantic
admission and a value projection over that use case; it does not duplicate the monthly-sales
calculation.

## 4. Company-scoped product resolver

`CompanyScopedProductResolver` resolves the company first through `ICompanyResolverService`,
loads only that company’s `IMonthlyProductComparisonReadRepository` periods, and matches product
identity in this order: provider code/id, exact normalized title, then partial normalized title.
Unknown companies and products return `NotFound`; multiple in-company matches return `Ambiguous`.
There is no global product fallback.

## 5. Golden Dataset and regression result

The semantic governance dataset now includes product value and product trend cases, while unit
coverage includes the required Persian forms, possessive/ZWNJ extraction, composition routing,
company-scoped resolution, unknown-product rejection, and in-company ambiguity.

Result: the pre-operational focused Feature 128 suite passed 10/10; full UnitTests later passed
1,770/1,770 after the operational-validation additions.

## 6. Mandatory فولاد value query

`فولاد محصولات گرمش چقدر فروخته؟` routes to `product_sales_value`, resolves company `فولاد`,
resolves product `محصولات گرم` within that company, and returns the latest persisted sales amount.
The real V2 integration fixture returned the seeded value `250` million rial and did not enter
`product_revenue_mix`.

## 7. Mandatory فولاد trend query

`روند فروش محصولات گرم فولاد؟` routes to `product_sales_trend`, resolves the same company and
product scope, and returns a resolved `monthlyProductTrendResult` with non-empty points. The real
V2 integration test passed.

## 8. Arbitration and composition regressions

Product mentions veto company-wide `product_revenue_mix` when a product-specific value/trend
candidate exists. Composition requests such as `ترکیب فروش محصولات فولاد` keep the product slot
null and remain on `product_revenue_mix`. Comparison wording continues to yield to the existing
Feature 129 comparison route.

## 9. Focused V2 integration result

Passed 2/2 positive real V2 endpoint tests:

- `V2AiQuery_ProductSalesValue_UsesCompanyScopedSemanticRoute`
- `V2AiQuery_ProductSalesTrend_UsesCompanyScopedSemanticRoute`

Both exercised `POST /api/ai/v1/query` with the active `MicrosoftAgentFrameworkV2` mode,
application DI, semantic gate, rollout decision, semantic coordinator, billing boundary, typed
executor, Feature 129 read path, and response persistence path.

The two negative endpoint cases also passed: unknown product and unknown company both remained on
clarification and did not fall back to `product_revenue_mix`.

## 10. Feature 129 regression result

The existing exact product-trend and typed-result replay tests passed 3/3. The broader
`V2MonthlySalesRoutingEndpointTests` run had 3 unrelated pre-existing failures in direct metric
classification/entity outcome assertions; the product-trend cases themselves passed. The current
run was `17 passed, 3 failed`.

## 11. Full regression and environment status

UnitTests passed 1,770/1,770. The previously recorded full integration run had 401 passed, 45
failed, and 40 skipped, including Docker/Testcontainers PostgreSQL and unrelated data-dependent
endpoint failures. This does not invalidate the two product-routing V2 tests, which passed in the
available environment.

Status: `PRODUCT_ROUTING_COMPLETE_WITH_ENVIRONMENT_BLOCKERS`

## 12. Operational handoff and non-rollout boundary

No rollout, canary, production shadow operation, provider latency benchmark, or dashboard
publication was performed. Before production promotion, rerun the full integration suite with the
required PostgreSQL/Testcontainers environment, investigate the unrelated direct-metric failures,
and collect provider-backed latency/cost and shadow-disagreement evidence. V1 was not extended;
the new capabilities are V2 registry, gate, resolver, dispatcher, executor, and workflow changes.

## 13. Operational Validation

1. **Shadow activation:** validated. `SemanticRouting` now defaults to `Shadow` in both production
   and Development configuration, and the code-level default is also `Shadow`. No global
   `SemanticPrimary` or `Canary` activation was added. Per-capability overrides remain supported.

2. **Shadow execution boundary:** validated. The dialogue gate places the semantic frame in
   `SemanticShadowFrame`; only the existing legacy branch executes. The shadow coordinator records
   a comparison and does not dispatch a second tool or use case.

3. **Double-execution regression:** validated by the shadow coordinator tests and the existing
   semantic dispatcher/billing tests. Semantic execution is disabled in Shadow and Rollback;
   Rollback is legacy-authoritative.

4. **Structured telemetry:** implemented and bounded. Each comparison carries correlation ID,
   query hash, legacy capability/confidence, semantic intent/candidate/confidence, resolved entity
   values, arbitration preferred candidate/reason/vetoes, actual executed capability, rollout mode,
   provider/model version, registry version, arbitration-policy version, and disagreement category.
   Query text is not persisted by this telemetry sink.

5. **Disagreement taxonomy:** implemented with the required categories:
   `AGREE`, `SEMANTIC_CORRECTS_LEGACY`, `LEGACY_CORRECTS_SEMANTIC`, `AMBIGUOUS`,
   `SEMANTIC_UNAVAILABLE`, `UNSUPPORTED`, and `ENTITY_RESOLUTION_DIFFERENCE`.

6. **Representative corpus:** executed as a 12-case offline shadow-control corpus covering
   product-specific Persian value/trend queries, composition, comparison, metric lookup, monthly
   activity, analysis, ambiguity, unknown intent, possessive wording, and reordered/ZWNJ wording.
   The corpus validates bounded telemetry plumbing; it is not provider-backed semantic accuracy
   evidence.

7. **Agreement/disagreement evidence:** the 12 offline controls produced 12/12 synthetic
   `AGREE` records. No production or provider-backed shadow sample was available, so semantic
   agreement rate and disagreement rate remain **insufficient for release measurement**.

8. **Correction evidence:** observed semantic-corrects-legacy: `0`; legacy-corrects-semantic:
   `0`; ambiguous: `0`; semantic-unavailable/unsupported: `0` in the controlled corpus. These are
   not production rates because the corpus used deterministic control candidates.

9. **Latency instrumentation:** implemented for deterministic interpretation, semantic model
   proposal, entity resolution, arbitration, routing, and total request. The statistics helper
   reports p50/p95/p99. The test fixture reports p50 `200 ms`, p95 `400 ms`, p99 `400 ms`; live
   provider-backed p50/p95/p99 are **N/A** until a sufficiently large shadow sample is collected.
   Targets remain p50 `<500 ms` and p95 `<1000 ms`; they are not hard-failure gates in this phase.

10. **Provider reliability:** timeout, provider-error, and invalid-structured-output flags are
    recorded on latency samples. The controlled shadow corpus observed `0/12` for each flag;
    production provider rates are **N/A**. Existing provider-service tests cover invalid structured
    output and timeout outcome mapping.

11. **Billing and usage:** Shadow does not call the semantic executor, so it cannot create a
    second business reservation, finalization, data read, or business charge. The semantic model
    usage is observable through the existing provider usage path where a provider is configured;
    no pricing or billing policy was changed. Existing semantic execution regression remains
    exactly one reservation and one finalization.

12. **Rollback validation:** validated. `Rollback` disables semantic execution and shadow
    comparison, keeps the legacy path authoritative, and is configuration-controlled. Returning
    to legacy does not require a redeploy when configuration reload is available; the regression
    test verifies the immediate legacy-only decision.

13. **Environment blockers:** functional validation is separate from environment validation. The
    focused Feature 128 suite passed `26/26` after this work. The previously recorded full
    integration run remained `401 passed, 45 failed, 40 skipped`, with Docker/Testcontainers and
   unrelated data-dependent failures; the current broader monthly-sales run retained three
   pre-existing direct-metric/entity-outcome failures (`17 passed, 3 failed`).

14. **Operational limitations:** no live production shadow traffic, real provider latency sample,
    provider cost export, or dashboard scrape was available in this environment. Consequently,
    no claim is made about live semantic accuracy, correction rate, or cost rate.

15. **Canary verdict:** `NOT_READY_FOR_CANARY — INSUFFICIENT_SHADOW_EVIDENCE`. Canary remains
    disabled. Readiness requires a provider-backed shadow window with sufficient samples,
    measured p50/p95/p99, disagreement/correction rates, timeout/error rates, entity-resolution
    outcomes, and billing/usage reconciliation.

## Production Shadow Validation

Validation date: `2026-10-05` (Asia/Tehran). This is the provider-backed Slice 12 handoff
evidence; it does not change rollout configuration, billing, pricing, or capability registration.

1. **Environment.** Validation ran against the local Development runtime, `MicrosoftAgentFrameworkV2`,
   `POST /api/ai/v1/query`, and local PostgreSQL. `SemanticRouting` was `Shadow`; no Canary or
   `SemanticPrimary` activation was present. `Rollback` remained available and was verified by the
   Feature 128 rollout test. No production/staging traffic source was available.

2. **Provider/model and versions.** The active provider was `OpenAI`, model `gpt-5.6-luna`,
   with hosted structured-output capability. The semantic proposal provider was
   `LlmQueryInterpretationProposalProvider` (not `NoOp`). The proposal schema was the governed
   `QueryInterpretationProposal` contract with fields `capabilityCodes`, `missingSlots`,
   `presentation`, `confidence`, `evidence`, `intent`, `entities`, and `metricHints`. The governed
   registry version was `1`; arbitration policy version was `feature-128-arbitration-v1`.
   The prompt is inline in the provider implementation and has no separately exposed prompt
   version identifier; this is an observability blocker. No secret was recorded here.

3. **Provider-backed sample count.** `12` valid UTF-8 Persian requests, exactly the mandatory
   corpus. Because the sample is below 30, the result is explicitly `LIMITED_SHADOW_SAMPLE`, not
   a production statistical-confidence claim.

4. **Real query corpus and per-request evidence.** Query hashes are the first 16 hexadecimal
   characters of SHA-256 over the UTF-8 query. The public response does not expose legacy
   confidence, arbitration vetoes, or the detailed comparison record; those fields remain in the
   bounded in-memory Shadow telemetry sink. The table records every externally visible semantic
   candidate and terminal state without inventing unavailable confidence values.

   | # | Query | Correlation ID | Query hash | Semantic candidate | Entity/terminal state | Actual route | Category |
   |---:|---|---|---|---|---|---|---|
   | 1 | `فولاد محصولات گرمش چقدر فروخته؟` | `f6054a38-5ca9-42f9-abec-74bff76ef825` | `A68B108BDAEF37A0` | فولاد resolved; product ambiguous | legacy only | `SEMANTIC_CORRECTS_LEGACY` |
   | 2 | `روند فروش محصولات گرم فولاد؟` | `53b02ee7-ab4c-42cb-ac0e-2b3a6341fb77` | `4B496D1AFB351192` | فولاد resolved; product ambiguous/no rows | legacy only | `AGREE` |
   | 3 | `ترکیب فروش فولاد چیه؟` | `7b00e5fe-1a56-4758-a91a-1bf36b08bcfe` | `D42C7AAEDABF483C` | company not found | legacy only | `AGREE` |
   | 4 | `کگهر و کگل رو کنار هم بذار ببین کدوم بهتره` | `33a132c9-e0d8-4085-a782-dea05c7ee565` | `FDCC2C77FB47CE64` | comparison entity ambiguous | legacy only | `AMBIGUOUS` |
   | 5 | `P/E فولاد` | `f927fa4d-c0f0-420d-9ada-f61c3559a84e` | `BFBEE0641586AA02` | فولاد resolved; answered | legacy only | `AGREE` |
   | 6 | `فروش ماهانه فولاد را نشان بده` | `963f9447-a12c-493a-ab44-11bc2d387170` | `059544077CB1A8A0` | فولاد resolved; answered | legacy only | `AGREE` |
   | 7 | `فولاد محصولات سردش چقدر فروخته؟` | `c4a131d1-6e65-421a-b2b1-19724a7a3040` | `467BC5965216DC91` | فولاد resolved; product ambiguous | legacy only | `SEMANTIC_CORRECTS_LEGACY` |
   | 8 | `فملی کاتدش چقدر فروخته؟` | `7139fa95-719a-4918-86ef-541092c66bca` | `44A4AFB773918AE9` | product/company ambiguous | legacy only | `ENTITY_RESOLUTION_DIFFERENCE` |
   | 9 | `کگل کنسانتره‌ش چقدر فروش داشته؟` | `acfdedac-eff3-4260-badf-a07cfb665e31` | `D8E237C4E6232B8A` | entity not found; possessive product unresolved | legacy only | `ENTITY_RESOLUTION_DIFFERENCE` |
   | 10 | `تحلیل بده` | `f57c5b96-73ec-4528-a486-36b024fb63bb` | `5972809FF29FE3B7` | missing company; unsupported terminal outcome | legacy only | `UNSUPPORTED` |
   | 11 | `روند فروش محصول ناشناخته فولاد` | `e34719da-e2ea-48f5-a3e8-63abedc798cc` | `DAF8D30D69911075` | فولاد resolved; unknown product not found; no global fallback | legacy only | `ENTITY_RESOLUTION_DIFFERENCE` |
   | 12 | `فروش شرکت ناشناخته چقدره؟` | `513b7c23-c40a-4baa-ba79-1d114c669800` | `A962E0297696EBF1` | company not found | legacy only | `ENTITY_RESOLUTION_DIFFERENCE` |

   For all 12 requests, the semantic frame was shadow-only and the business response came from
   the legacy branch. No semantic business executor was enabled by the rollout decision.

5. **Agreement/disagreement metrics.** Using one mutually exclusive category per request:
   `AGREE=4/12 (33.3%)`, `SEMANTIC_CORRECTS_LEGACY=2/12 (16.7%)`,
   `LEGACY_CORRECTS_SEMANTIC=0/12`, `AMBIGUOUS=1/12 (8.3%)`,
   `SEMANTIC_UNAVAILABLE=0/12`, `UNSUPPORTED=1/12 (8.3%)`, and
   `ENTITY_RESOLUTION_DIFFERENCE=4/12 (33.3%)`. Nine requests had an entity-resolution or
   product-resolution terminal failure; this count may overlap the interpretation category.

6. **Correctness classification.** End-to-end mandatory query pass count was `2/12` (#5 and
   #6). Seven requests selected the expected capability candidate at interpretation level, but
   product/entity resolution prevented an end-to-end pass. The known value regression (#1)
   produced the expected semantic candidate `product_sales_value` with company `فولاد` and the
   product mention `محصولات گرم`, which is the expected semantic correction over the legacy
   product-wide route; it then stopped on product ambiguity. The known trend regression (#2)
   produced `product_sales_trend` with company `فولاد`, but product resolution remained
   ambiguous and returned no rows. These are candidate/plumbing passes, not release-quality
   value/trend answer passes.

7. **Entity-resolution validation.** Company-first and company-scoped product behavior was
   observed. The unknown-product case (#11) returned a product-resolution no-data state and did
   not fall back to company-wide mix/trend. However, product variants, possessive normalization
   (`کاتدش`, `کنسانتره‌ش`), and comparison entities still produced ambiguity/not-found outcomes
   in the live corpus. This is an end-to-end blocker even though the no-global-fallback safety
   invariant held.

8. **Latency.** The 12 total request times in milliseconds were
   `13412, 4092, 4732, 39408, 6923, 6160, 11748, 12535, 7418, 5886, 4033, 6312`.
   Using the implementation's nearest-rank percentile helper: total request `p50=6312 ms`,
   `p95=39408 ms`, `p99=39408 ms`. The deterministic, semantic-model, entity-resolution,
   arbitration, and routing component percentiles are `N/A`: the latency sink records them
   in-memory but no read/export endpoint exposes per-sample data. This missing operational
   export, and the measured total-request p95, block readiness.

9. **Provider reliability.** In the corrected 12-request corpus, observed semantic timeout rate
   was `0/12 (0%)`, provider-error rate `0/12 (0%)`, and invalid-structured-output rate
   `0/12 (0%)` at the request outcome layer. These are bounded-sample observations, not a
   production rate estimate.

10. **Billing and semantic usage.** Database reconciliation found `12/12` business reservations,
    `12/12` committed finalization records, and exactly one charge ledger row per correlation;
    idempotency keys were unique. Four requests finalized with zero business credits because they
    ended in clarification/disambiguation, while the remaining eight charged one business credit.
    No duplicate user-visible charge was observed. There were `12` user and `12` assistant
    message rows, with no duplicate response persistence. Provider/model usage fields were
    present on 10/12 business ledger rows; two product-trend rows lacked provider usage fields,
    so separate semantic-proposal usage/cost reconciliation is incomplete.

11. **Shadow invariants.** Configuration and focused tests verified: deterministic/legacy
    interpretation is retained; semantic interpretation is attempted when the provider path is
    available; arbitration and Shadow comparison are recorded; semantic capability execution is
    disabled; only the legacy business route runs; and billing remains exactly-once. The public
    response has no per-use-case invocation counter, so duplicate financial-read/tool-call proof
    is based on the Shadow execution boundary, persisted row counts, billing idempotency records,
    and passing exactly-once tests rather than a per-request invocation trace.

12. **Tests.** Feature 128 focused tests passed `26/26`; Feature 125 regression tests passed
    `15/15`; Feature 129 unit tests passed `35/35`; Feature 132 semantic-routing tests passed
    `8/8`; semantic executor/governance/feedback tests passed `25/25`; dispatcher plus billing
    tests passed `22/22`; billing persistence tests passed `19/19`; and the full UnitTests suite
    passed `1770/1770`. The selected V2 product-routing integration run was `35 passed, 8 failed`
    out of `43`; failures were direct-metric/entity-outcome and product-mix classification
    regressions, not hidden. The Feature 129 PostgreSQL repository integration test was skipped
    because its PostgreSQL/Testcontainers fixture was unavailable. Provider-backed smoke evidence
    is the 12-request corpus above.

13. **Rollback verification.** `Rollback` remained configuration-controlled and legacy-only;
    `ExecuteSemanticRoute=false` and `RunShadowComparison=false` were verified by the focused
    Feature 128 test. Canary and SemanticPrimary were not enabled.

14. **Environment blockers.** No production/staging traffic was available; sample size is
    `LIMITED_SHADOW_SAMPLE`. The runtime does not expose per-request Shadow comparison details or
    routing-component latency percentiles, the inline semantic prompt has no version identifier,
    semantic usage is not separately reconciled for all provider proposals, and product/entity
    resolution failed in the mandatory corpus. Docker/Testcontainers coverage remains separately
    environment-blocked.

15. **Canary readiness verdict.** `NOT_READY_FOR_CANARY`. Exact blockers: mandatory product
    value/trend cases did not complete as correct answers; entity and possessive-product
    resolution remains unreliable; total-request p95 was `39408 ms`; component latency export is
    unavailable; semantic usage reconciliation is incomplete; the selected integration run still
    has 8 failures; and the provider-backed sample is below 30. Canary remains disabled.
## Production Shadow Blocker Resolution

Validation date: 2026-10-05. Provider/model: `OpenAI / gpt-5.6-luna`. Routing remained
`Shadow`; Canary and SemanticPrimary remained disabled.

1. **Final corpus.** All 12 provider-backed requests returned HTTP 200: 10 answered, one
   intentional `NoData / supported_but_no_rows`, one intentional clarification for `تحلیل بده`,
   and one intentional `entity_not_found` for an unknown company. No mandatory product case ended
   in entity or product ambiguity.

2. **Trace matrix.** Original blocker category and final result are recorded below.

   | # | Query | Capability / resolution | Final outcome | ms | Original category |
   |---:|---|---|---|---:|---|
   | 1 | `فولاد محصولات گرمش چقدر فروخته؟` | product sales value; فولاد → محصولات گرم | Answered | 4195 | `PRODUCT_RESOLUTION_FAILURE` |
   | 2 | `روند فروش محصولات گرم فولاد؟` | product sales trend; فولاد → محصولات گرم | Answered | 4035 | `PRODUCT_RESOLUTION_FAILURE` |
   | 3 | `ترکیب فروش فولاد چیه؟` | product revenue mix; فولاد | Answered | 4329 | `COMPANY_RESOLUTION_FAILURE` |
   | 4 | `کگهر و کگل رو کنار هم بذار ببین کدوم بهتره` | pair comparison; both symbols resolved | NoData | 3802 | `ARBITRATION_FAILURE` |
   | 5 | `P/E فولاد` | symbol metric; فولاد | Answered | 2953 | `NONE` |
   | 6 | `فروش ماهانه فولاد را نشان بده` | symbol metric; no invented product | Answered | 3590 | `CAPABILITY_MAPPING_FAILURE` |
   | 7 | `فولاد محصولات سردش چقدر فروخته؟` | product sales value; فولاد → محصولات سرد | Answered | 4073 | `PRODUCT_RESOLUTION_FAILURE` |
   | 8 | `فملی کاتدش چقدر فروخته؟` | product sales value; فملی → کاتد | Answered | 3902 | `SEMANTIC_EXTRACTION_FAILURE` |
   | 9 | `کگل کنسانتره‌ش چقدر فروش داشته؟` | product sales value; کگل → کنسانتره سنگ آهن | Answered | 3844 | `SEMANTIC_EXTRACTION_FAILURE` |
   | 10 | `تحلیل بده` | comprehensive analysis; symbol missing | Clarification | 2500 | `OTHER` (expected) |
   | 11 | `روند فروش محصول ناشناخته فولاد` | product trend; فولاد → product not found | NoData | 4160 | `DATA_NOT_FOUND` (expected) |
   | 12 | `فروش شرکت ناشناخته چقدره؟` | symbol metric; company not found | EntityNotFound | 3415 | `COMPANY_RESOLUTION_FAILURE` (expected) |

3. **Nine original entity/product failures.** #1, #2, #3, #4, #7, #8, #9, #11, and #12 were
   caused by short product-token extraction, possessive forms retained as generic entities,
   Persian conversational words retained as company mentions, and product mentions being sent to
   company resolution before company-scoped product resolution. Unknown company/product outcomes
   were also surfaced as generic ambiguity or required-input clarification.

4. **Company/product fix.** Production catalog reads are now company-scoped and cached, with no
   global product fallback. The invariant is company → canonical company → company-scoped catalog
   → canonical product. Exact catalog title duplicates and unique shortest partial matches are
   canonicalized; distinct fallback identities remain ambiguous. Generic forms including
   `محصولات گرمش`, `گرمش`, `محصولات سردش`, `کاتدش`, `کنسانتره‌ش`, and `شمشش` are handled by
   marker/possessive rules, not hardcoded combinations.

5. **Semantic/arbitration fix.** The bounded prompt/schema requires separate company/symbol/product
   spans, explicit product scope, and at most 256 output tokens. Model product capabilities are
   vetoed without deterministic explicit product scope. Real Persian pair conjunctions are handled;
   generic company-only metric queries cannot become product routes.

6. **Resolution-stage fix.** Product-typed mentions and normalized product surfaces are excluded
   from company resolution. Product-not-found is now a supported no-data result; unknown companies
   remain explicit `entity_not_found`; no path performs global product lookup.

7. **Latency investigation.** Deterministic and semantic proposal work runs concurrently. The
   semantic call uses configured timeout infrastructure (`5000 ms`) and output budget (`256`), with
   retries limited to HTTP 429. Final total times were
   `4195, 4035, 4329, 3802, 2953, 3590, 4073, 3902, 3844, 2500, 4160, 3415 ms`;
   nearest-rank `p50=3902 ms`, `p95=4329 ms`, `p99=4329 ms`. The 39-second outlier is gone.

8. **Component telemetry.** The merged sink records deterministic, semantic-model, arbitration,
   entity-resolution, routing, and total samples once per correlation ID; its merge behavior is
   unit-tested. The current admin read model does not export per-request component percentiles, so
   that remains an observability/environment follow-up rather than an inferred metric.

9. **Provider reliability.** The final corpus observed zero provider errors, semantic timeouts, or
   invalid structured-output outcomes. Startup confirmed `OpenAI / gpt-5.6-luna`.

10. **Billing/usage.** Earlier database reconciliation remains valid: 12/12 business reservations
    committed, exactly one charge ledger row per correlation, no duplicate idempotency key, 12 user
    messages, and 12 assistant messages. Semantic proposal usage is not separately exposed from
    business response usage and is reported unavailable rather than inferred.

11. **Shadow safety.** Semantic execution stayed disabled; legacy execution and billing remained
    authoritative. Canary and SemanticPrimary were not enabled; rollback remains legacy-only.

12. **Tests.** Focused Feature 128 tests now pass `31/31`, including product-scope rejection,
    possessive extraction, company-only entity retention, arbitration, and latency-sink merging.
    The complete unit suite was rerun after the final typo-regression fix and passes `1775/1775`.
    Feature 125 remains `15/15`; prior Feature 129, Feature 132, governance, billing, and baseline
    suite results remain recorded above. PostgreSQL/Testcontainers remains environment-blocked.

13. **Product defects resolved.** Product-token ambiguity, possessive extraction, company/product
    ordering, company-only metric misrouting, Persian pair arbitration, unknown-product handling,
    and the 39-second semantic tail were fixed and revalidated.

14. **Semantic routing defects resolved.** The final corpus has no mandatory
    `entity_ambiguous`, `product_ambiguous`, unsupported-capability, or provider-failure result.
    The only clarification is the intentionally underspecified symbol-less analysis request.

15. **Data/environment outcomes.** The pair and unknown-product requests correctly return no-data;
    the unknown company correctly returns not-found. No fabricated analysis or global fallback data
    is used.

16. **Handoff status.** Scoped production Shadow blockers for routing, entity/product resolution,
    arbitration, latency, and billing correctness are resolved. Canary remains disabled because
    production traffic, component-latency export, separate semantic-cost telemetry, and
    PostgreSQL/Testcontainers integration are unavailable in this environment.

17. **Verdict.** `SHADOW_BLOCKERS_RESOLVED_FOR_HANDOFF; NOT_READY_FOR_CANARY`.

## Canary Validation

Validation date: `2026-10-05` (Asia/Tehran). The rollout was enabled only for the bounded
Feature 128 product-sales capabilities. `SemanticPrimary` remains disabled.

1. **Canary configuration.** `SemanticRouting.DefaultMode=Shadow`,
   `SemanticRouting.CanaryPercentage=10`, and the only Canary capability overrides are
   `product_sales_value` and `product_sales_trend`. The same configuration is present in
   Development. The Docker default was corrected to `Shadow` so deployment environment defaults
   cannot re-enable `SemanticPrimary` globally.

2. **Cohort definition.** The existing deterministic SHA-256 actor-hash cohort is used. Actors
   whose stable actor key falls in buckets `0..9` are Canary; all other actors remain on the
   existing Shadow/legacy-authoritative path. No new rollout service or cohort store was added.
   Non-cohort Canary traffic now records a Shadow comparison using the same actor key.

3. **Sample count.** Four seeded V2 endpoint requests were executed in the bounded validation
   subset: the mandatory product-sales value and trend cases, plus unknown-product and
   unknown-company safety cases. Supplemental validation covered the required composition and
   Feature 125 comparison controls, the exact `P/E فولاد` representative query, and six persisted
   P/E lookup permutations. This is a local seeded test sample, not production traffic.

4. **Mandatory query results.** The product value case returned the typed
   `product_sales_value` result; the product trend case returned a resolved, non-empty
   `monthlyProductTrendResult`. Composition retained `product_revenue_mix` with no product slot,
   and the Feature 125 pair comparison remained on `symbol_pair_within_industry`. Unknown product
   and unknown company cases produced bounded no-data/clarification outcomes and never fell back
   to `product_revenue_mix`.

5. **Semantic-vs-legacy execution counts.** In the four-case bounded endpoint subset, semantic
   admission was observed for `4/4`; two requests completed a typed semantic business execution,
   and two stopped at safe entity/product validation outcomes. Legacy-selected endpoint requests:
   `0` in this selected Canary actor cohort. Composition, comparison, and P/E controls remained
   legacy/Shadow controls. No duplicate business execution was observed.

6. **Agreement/disagreement metrics.** A production Canary comparison stream was not available,
   and the bounded telemetry sink has no read/export endpoint for a live aggregate. Therefore
   Canary agreement, disagreement, semantic-corrects-legacy, and legacy-corrects-semantic rates
   are `N/A`, not inferred from the seeded tests. The non-cohort Shadow telemetry regression is
   covered by `CanaryNonCohort_RemainsShadowObservableAndDoesNotExecuteSemanticRoute`.

7. **Entity-resolution outcomes.** The positive product cases resolved company first and product
   within the company. Unknown product/company cases remained safe no-data/not-found outcomes;
   no global product fallback was used.

8. **Latency p50/p95/p99.** Canary routing and total-request percentiles are `N/A`: the local
   endpoint tests do not export per-request routing timings. The latest provider-backed Shadow
   reference remains `p50=3902 ms`, `p95=4329 ms`, `p99=4329 ms`; it is not reported as Canary
   latency evidence.

9. **Provider reliability.** Canary provider timeout, provider-error, and invalid-output rates
   are `N/A` because the validation endpoint uses a seeded test client and no live provider-backed
   Canary sample was available. The previously recorded Shadow reference was `0/12` for each
   category.

10. **Billing verification.** The semantic execution coordinator test passed with exactly one
    reservation and one finalization. The bounded endpoint checks produced no duplicate charge or
    duplicate response evidence. A production billing-ledger reconciliation for Canary traffic is
    unavailable because no production traffic was supplied.

11. **Rollback verification.** Rollback decision tests passed: `Rollback` disables semantic
    execution and Shadow comparison, leaving the legacy route authoritative. The configuration
    remains reversible without code changes; no live redeploy-free rollback exercise was possible
    in this environment.

12. **Test results.** Feature 128 focused and rollout tests passed `58/58`; the full UnitTests
    suite passed `1776/1776`; bounded product-routing integration tests passed `4/4`; persisted
    P/E lookup integration tests passed `6/6`; the API Release build passed with zero warnings and
    zero errors. The full IntegrationTests run completed `400 passed, 50 failed, 40 skipped`.
    The failures include unavailable Docker/Testcontainers PostgreSQL coverage and unrelated
    cross-feature fixture/schema/Telegram/scanner/market/legacy-routing failures; they remain
    release blockers for a broader rollout.

13. **Remaining blockers.** No production or staging Canary traffic, live provider-backed Canary
    sample, comparison aggregate export, per-request latency export, or production billing
    reconciliation is available. The full integration regression set also remains non-green.

14. **Canary verdict.** `CANARY_BLOCKED`. The bounded code/configuration validation is green, but
    the required live evidence and full regression gate are not available to justify
    `CANARY_STABLE`.

15. **SemanticPrimary readiness recommendation.** Keep `SemanticPrimary` disabled. Retain the
    10% capability-scoped Canary configuration only for controlled validation, collect exported
    live comparison/latency/provider/billing metrics, resolve the remaining integration failures,
    then re-audit the canary exit criteria before considering any SemanticPrimary change.

## Canary Blocker Resolution and Promotion Gate

Validation date: `2026-10-05` (Asia/Tehran). This section records the final continuation run after
resolving the Feature 128 routing regression found in the full integration suite. No production or
staging traffic was available, and `SemanticPrimary` remains disabled.

1. **Runtime Canary configuration.** The active repository configuration is:
   `AiOrchestration:Mode=MicrosoftAgentFrameworkV2`,
   `SemanticRouting:DefaultMode=Shadow`, `CanaryPercentage=10`, and capability overrides only for
   `product_sales_value` and `product_sales_trend`, both set to `Canary`. The rollout decision uses
   a deterministic SHA-256 actor cohort; bucket values `0..9` are selected at 10%. The provider/model
   reference is `OpenAI / gpt-5.6-luna`; the semantic registry version is `1`; arbitration policy
   version is `feature-128-arbitration-v1`; semantic proposal timeout is `5000 ms`; and output
   budget is `256` tokens. The governed prompt has no separately exposed version identifier. No
   secret is recorded here. Rollback remains the configuration-controlled `Rollback` mode.

2. **Cohort determinism verification.** The existing rollout tests pass for stable same-actor
   selection, repeated requests, rollback, non-cohort observability, and percentage boundaries.
   The implementation hashes the stable actor key with SHA-256, so restart and process/node changes
   do not change the decision. No new cohort store or rollout algorithm was introduced.

3. **Live Canary sample count.** `0`. No production or staging provider-backed Canary traffic was
   available. The four-request local seeded Canary subset remains test evidence only.

4. **Semantic execution count.** Live: `0`. Local seeded subset: `4/4` admitted; two requests
   completed typed semantic business execution and two terminated in safe entity/product outcomes.
   No duplicate business execution was observed.

5. **Legacy fallback count.** Live: `0`. Local selected Canary subset: `0` legacy-selected endpoint
   requests. Non-cohort and unrelated capabilities remain legacy-authoritative in Shadow.

6. **Agreement/disagreement metrics.** Live agreement, disagreement,
   `SEMANTIC_CORRECTS_LEGACY`, and `LEGACY_CORRECTS_SEMANTIC` rates are `N/A`: there is no live
   comparison stream or aggregate export. The local seeded tests are not used as statistical
   agreement evidence.

7. **Correction evidence.** Live semantic corrections: `N/A`; live legacy corrections: `N/A`.
   The corrected local regression was a deterministic routing defect, not live correction evidence.

8. **Entity-resolution outcomes.** The mandatory local product value/trend cases resolve company
   first and product within the company, with no global-product fallback. Unknown product and
   unknown company controls remain safe no-data/not-found outcomes. Final local product-routing
   integration result: `12/12` passed.

9. **Semantic latency.** Live provider-backed semantic p50/p95/p99: `N/A`. The latest provider-
   backed Shadow reference remains p50 `3902 ms`, p95 `4329 ms`, p99 `4329 ms`; it is not Canary
   evidence. Per-component percentile export is still unavailable.

10. **Routing latency.** Live Canary routing p50/p95/p99: `N/A`; no exported live routing stream
    exists. Instrumentation is present and covered by unit tests.

11. **Total request latency.** Live Canary p50/p95/p99: `N/A`. The provider-backed Shadow reference
    remains p50 `3902 ms`, p95 `4329 ms`, p99 `4329 ms`.

12. **Provider reliability.** Live Canary timeout, provider-error, and invalid-output rates are
    `N/A`. The prior 12-request provider-backed Shadow corpus observed `0/12` for each category.

13. **Billing reconciliation.** Live Canary ledger reconciliation is unavailable because live
    Canary traffic is absent. Local semantic execution and billing tests verify exactly one
    business reservation, one finalization, and no duplicate charge. The local endpoint subset
    produced no duplicate billing evidence. Semantic proposal usage is not separately exposed from
    business response usage and is reported unavailable rather than inferred.

14. **Integration failure classification.** The final full IntegrationTests run is `405 passed,
    45 failed, 40 skipped` (`490` total). Feature 128-related failures are now `0`; the five
    product-routing failures removed from the prior `400/50/40` run were fixed and revalidated.
    The remaining failures are classified below.

    | Classification | Count | Failing tests / root-cause grouping |
    |---|---:|---|
    | `FEATURE_128_REGRESSION` | 0 | The ProductRevenueMix preemption regression was fixed; the 12-case product-routing set now passes. |
    | `FEATURE_125_REGRESSION` | 0 | Feature 125 semantic routing safety tests pass; PostgreSQL cases are environment-blocked, not behavior failures. |
    | `FEATURE_129_REGRESSION` | 0 | Typed product comparison/trend regression tests pass; the six selected Feature 129 integration cases pass. |
    | `DIRECT_METRIC_EXISTING_FAILURE` | 7 | `V2MonthlySalesRoutingEndpointTests` (3 direct monthly-sales assertions), `CyclicalWavesMonthlySalesLookupTests` (1), `CyclicalWavesDirectPeriodMetricLookupTests` (1), and `V2SymbolLookupEndpointTests` (2). These are existing direct-metric/entity-state failures, not Feature 128 routes. |
    | `DATABASE_FIXTURE_FAILURE` | 10 | `SalesGrowthScannerIntegrationTests` (6), `ScannerExecutionEndpointTests` (3), and `FinancialStatementSchemaTests.UniqueIndex_IsConfiguredOnTheNewTriple` (1). Seed/schema fixture expectations are unavailable or mismatched. |
    | `DOCKER_TESTCONTAINERS_ENVIRONMENT` | 6 | All six `Slice1PostgreSqlTests` fail at fixture startup because Docker/Testcontainers is unavailable; the exception is reported by the test harness as `SkipException`. |
    | `AUTH_CONFIGURATION_FAILURE` | 8 | Four `TelegramMembership088Tests` and four `TelegramAccountLinking087Tests` fail during test identity-link/challenge setup with HTTP 400/404. |
    | `DATA_DEPENDENCY_FAILURE` | 7 | `CodalDbGrowthMetricScannerTests.AiQuery_SalesGrowthRateAbove10_ReturnsVendorPrecomputedMatch` and six `MarketInsightEndpointTests` fail because expected seeded/provider data is absent. |
    | `UNRELATED_EXISTING_FAILURE` | 7 | Six `AdminDataOperationsEndpointTests` fail at unrelated admin response/rate-limit setup, and `ExplainableAnswerEndpointTests.AiQuery_ExplainableAnswer_ConfidenceScoreIsBackendComputed` fails its pre-existing confidence expectation. |
    | `UNKNOWN` | 0 | No remaining failure is unclassified. |

15. **Feature-128-specific fix.** `DeterministicCapabilityInterpreter` now treats the existing
    ProductRevenueMix phrase family as a composition veto before product-sales semantic extraction.
    The active workflow and fallback runner also prevent a semantic product-sales frame from
    preempting a ProductRevenueMix query and preserve the normal single billing reservation. This
    fixes queries such as `پرفروش‌ترین محصول کچاد؟` and `مهم‌ترین محصول کچاد چیست؟`, which previously
    surfaced as `MonthlyProductTrend` or clarification. No new capability, pricing, billing policy,
    V1 route, or Canary scope was added.

16. **Feature 125 regression result.** Focused Feature 125 semantic routing safety passed `15/15`
    in the recorded gate; the broader selected unit run remained green. PostgreSQL-backed Feature
    125 tests could not execute because Docker/Testcontainers is unavailable.

17. **Feature 129 regression result.** Focused comparison/trend unit coverage passed `54/54`; the
    selected typed product integration coverage passed `6/6`. The three broader monthly-sales
    direct-metric failures remain classified as `DIRECT_METRIC_EXISTING_FAILURE`.

18. **Rollback drill result.** Configuration-level rollback tests pass: `Rollback` disables
    semantic execution and shadow comparison, and the legacy route remains authoritative. A live
    redeploy-free configuration reload drill and live billing reconciliation were not possible in
    this environment.

19. **Verification summary.** Feature 128 focused/rollout gate: `80/80` in the final focused run;
    full UnitTests: `1776/1776`; Feature 129 focused unit gate: `54/54`; selected product-routing
    integration: `12/12`; selected typed product integration: `6/6`; API Release build: passed with
    zero warnings and zero errors. Full IntegrationTests: `405/45/40`.

20. **Remaining blockers.** Live provider-backed Canary traffic, live comparison aggregation,
    exported component latency percentiles, separate semantic-cost reconciliation, production
    billing-ledger evidence, and the non-green broader integration suite remain unavailable or
    unresolved. These blockers prevent promotion even though the scoped Feature 128 code and local
    regression gates are green.

21. **Sample sufficiency verdict.** `LIVE_CANARY_SAMPLE_LIMITED`. Live Canary sample size is zero;
    no statistical confidence claim is made.

22. **Promotion verdict — `product_sales_value`.** `KEEP_CANARY`. The scoped routing, entity
    resolution, billing, and rollback gates are green locally, but live evidence, live latency and
    reliability metrics, live billing reconciliation, and the broader integration gate are absent.
    SemanticPrimary remains disabled.

23. **Promotion verdict — `product_sales_trend`.** `KEEP_CANARY`. The scoped routing, typed result,
    entity resolution, billing, and rollback gates are green locally, but the same live evidence,
    telemetry, and broader integration blockers prevent promotion. SemanticPrimary remains disabled.
## Live Canary Evidence Collection — Continuation

Validation date: `2026-10-05` (Asia/Tehran). This continuation collected evidence from the running
Development API using the real configured provider path and a dedicated registered validation account.
It is live-local evidence, not production or staging traffic. No secret, bearer token, raw query, or raw
actor identifier is recorded in this report.

1. **Active configuration.** Runtime export confirmed `DefaultMode=Shadow`, `CanaryPercentage=10`, and
   exactly two capability overrides: `product_sales_value=Canary` and `product_sales_trend=Canary`.
   `SemanticPrimary` remained disabled. The configured provider/model was `OpenAI / gpt-5.6-luna`.

2. **Cohort evidence.** The validation actor resolved to deterministic SHA-256 cohort bucket `3`, so it
   was admitted to the 10% Canary cohort. The existing non-cohort Shadow behavior remains covered by the
   rollout tests; no cohort algorithm or scope was changed.

3. **Collection method.** Thirty authenticated `POST /api/ai/v1/query` requests were issued through the
   running API: fifteen value prompts and fifteen trend prompts. The aggregate was exported through
   `GET /api/v1/admin/ai/semantic-dialogue/operational?maximumSamples=500`. The export is bounded and
   contains hashes and operational fields only.

4. **Live Canary sample count.** `30` retained samples: `15` for `product_sales_value` and `15` for
   `product_sales_trend`.

5. **Response outcomes.** `18` were `Answered`, `9` were expected `NoData`, and `3` were expected
   `ClarificationNeeded`. All thirty HTTP calls completed without request-level failure.

6. **Correctness classification.** `CORRECT=18`, `EXPECTED_NO_DATA=9`, and
   `EXPECTED_CLARIFICATION=3`. No `INCORRECT` classification was exported.

7. **Disagreement classification.** `AMBIGUOUS=18` and `ENTITY_RESOLUTION_DIFFERENCE=12`.
   No semantic-corrects-legacy or legacy-corrects-semantic correction rate is claimed: this telemetry
   export does not contain a clean legacy-vs-semantic outcome pair for these requests.

8. **Entity resolution.** `Resolved=18` and `Failed=12`; the exported `entityResolutionFailures` count
   is `12`. The failures correspond to the bounded clarification/no-data behavior and did not produce a
   global product fallback.

9. **Semantic execution.** All thirty samples were admitted as Canary with semantic execution allowed.
   The business response path completed or safely classified every request; no duplicate business
   execution was observed in the request batch.

10. **Semantic latency.** Exported semantic latency percentiles were `p50=3418.8718 ms`,
    `p95=4253.1475 ms`, and `p99=4421.4291 ms` across 30 samples.

11. **Routing latency.** Exported routing latency percentiles were `p50=3418.8991 ms`,
    `p95=4253.1621 ms`, and `p99=4421.4377 ms` across 30 samples.

12. **Total request latency.** Exported total-request percentiles were `p50=3661.9259 ms`,
    `p95=4496.9167 ms`, and `p99=5387.8207 ms` across 30 samples.

13. **Provider reliability.** Timeout count was `0/30`. Provider-error count was `30/30`, and
    invalid-structured-output count was `30/30`; semantic-unavailable count was `0/30`. The response
    workflow continued through deterministic handling, but this is not a clean provider-backed Canary
    pass and is a promotion blocker. The usage telemetry did not expose a separate provider/model value
    per semantic sample.

14. **Fallback evidence.** `safeLegacyFallbackCount=0` and no `LEGACY_FALLBACK` correctness class was
    exported. The provider errors were therefore recorded as an operational failure signal rather than
    silently presented as successful semantic corrections.

15. **Billing reservation evidence.** `billingReservationObserved=False` for all `30/30` samples.
    This means the live operational export did not observe the business reservation event, so no claim of
    live reservation reconciliation is made.

16. **Billing finalization evidence.** `billingFinalizationObserved=True` for `18/30` samples and
    `False` for `12/30`; `creditsCharged=1` for the 18 finalized answered samples and was absent for the
    12 no-data/clarification samples. Semantic usage separation was `False` for all 30 samples. A live
    ledger-level exactly-once reconciliation remains unavailable.

17. **Rollback.** Configuration-level rollback tests remain green: `Rollback` disables semantic
    execution and Shadow comparison and leaves the legacy route authoritative. A live redeploy-free
    rollback drill was not executed because the operational sink is process-local and restarting would
    discard the collected evidence.

18. **Verification after instrumentation changes.** The focused Feature 128 semantic-routing and
    dispatcher test filter passed `43/43`. The API Release build passed with zero warnings and zero
    errors. Previously recorded full-suite and integration classifications remain unchanged.

19. **Remaining blockers.** The provider invalid-output signal, absent reservation observations,
    non-separable semantic usage, lack of a durable production comparison stream, and the previously
    classified broader integration failures prevent promotion. The evidence is sufficient to operate a
    controlled Canary, not to promote either capability to `SemanticPrimary`.

20. **Final rollout verdict — `product_sales_value`.** `KEEP_CANARY`. `SemanticPrimary` remains disabled.

21. **Final rollout verdict — `product_sales_trend`.** `KEEP_CANARY`. `SemanticPrimary` remains disabled.

## Structured Output Blocker Resolution

Validation date: `2026-10-05` (Asia/Tehran). SemanticPrimary was not enabled and pricing/billing
rules were not changed.

1. **Root cause of the 30/30 invalid outputs.** The retained Canary export recorded only the
   aggregate `InvalidStructuredOutput` flag; it did not retain the provider failure code or a
   bounded response-category sample, so the exact raw category for each historical call cannot be
   reconstructed without secrets or replay. Code inspection identified the contract defect that
   made this failure mode likely and non-diagnosable: the OpenAI Responses adapter sent generic
   `json_object` mode, while the semantic provider required eight named root properties and then
   performed additional enum/shape parsing. The provider was not given the authoritative schema,
   and the prompt did not enumerate the exact output contract. The fix sends a strict provider
   JSON Schema and records bounded failure categories for future samples.

2. **Provider/model capability finding.** The configured path is `OpenAI / gpt-5.6-luna` through
   the Responses API and `IAiModelExecutionService`. A live probe reached the provider but was
   rejected before generation with HTTP `429`, error type `insufficient_quota`, code
   `credit_balance_exhausted`. Therefore post-fix model acceptance and generation cannot be
   claimed until provider credits are restored.

3. **Schema issue.** `AiStructuredOutputContract` now optionally carries a provider schema. The
   semantic contract is `QueryInterpretationProposal_v2`, with bounded arrays, nullable fields,
   enum values for presentation, numeric confidence limits, nested entity fields, and
   `additionalProperties=false`. Unused period/comparison fields are not requested by the model.

4. **Prompt issue.** The semantic prompt now requires exactly one JSON object, names exactly the
   schema fields, lists allowed presentation values, and forbids prose, markdown, reasoning,
   tool names, routes, SQL, canonical IDs, metric definitions, and executable arguments.

5. **Output token/truncation finding.** The historical export did not contain output-token counts
   or finish reasons, so truncation was not proven. The bounded semantic budget is now `384`
   tokens (raised from `256` only enough for the bounded schema), and OpenAI `max_output_tokens`
   completion is classified as `structured_output_truncated`.

6. **Code/config changes.** Added strict JSON Schema support to the provider-neutral contract;
   OpenAI maps schema-backed requests to Responses `text.format.type=json_schema` with `strict=true`
   and preserves the generic JSON-object fallback for other contracts. Added refusal, truncation,
   missing-output, and tool-call substitution classification. Failed validation attempts now
   preserve provider/model/token facts in usage telemetry. Default and Development semantic token
   settings are `384`.

7. **Provider-backed contract test result.** Added an opt-in real-provider test covering value,
   trend, product-revenue-mix, and P/E lookup semantics through `LlmQueryInterpretationProposalProvider`
   and `IAiModelExecutionService`. The deterministic adapter contract test passed; the real-provider
   test reached OpenAI and failed at the external quota gate (`429 insufficient_quota`), before a
   proposal could be generated.

8. **Live rerun sample size.** Post-fix live semantic sample: `0` generated proposals; one
   bounded provider probe was executed and stopped at the quota error. The prior pre-fix Canary
   sample remains `30` (`15 product_sales_value`, `15 product_sales_trend`).

9. **Valid structured outputs.** Post-fix live: `0/0 generated responses`; no rate is claimed.
   Pre-fix: `0/30` valid according to the retained export.

10. **Invalid output/timeout/error rates.** Post-fix provider generation sample: `0` invalid
    structured outputs, `0` timeouts, and `1` provider quota error in the explicit probe. This is
    not a generation reliability rate. Pre-fix export: `30/30` invalid structured output and
    `0/30` timeouts; the export labeled all 30 as provider errors because invalid structured
    output was included in that operational flag.

11. **Semantic execution result.** No post-fix semantic execution can be claimed while the
    provider is quota-blocked. Existing fail-soft behavior remains: invalid/unavailable semantic
    proposals do not execute a semantic capability and the deterministic/legacy route remains
    available.

12. **Billing persistence verification.** Existing code and exactly-once tests remain unchanged
    and green. The semantic evidence path now treats a non-null semantic accounting result as
    evidence that the semantic reservation/finalization path ran; it no longer checks only the
    legacy reservation handle.

13. **Billing telemetry/export fix.** Canary export now reports semantic reservation observed when
    semantic accounting completed, reports semantic usage as separable when the semantic route is
    the only model route, and includes a bounded provider failure code correlated by request ID.
    No customer billing rule was changed.

14. **Provider usage reconciliation.** Successful and validation-failed model attempts now retain
    provider, model, input tokens, output tokens, duration, status, failure code, and correlation
    ID in the existing usage accumulator when the provider returned usage facts. No cost policy was
    changed.

15. **Latency after fix.** Post-fix generation latency is `N/A` because the explicit live probe
    was rejected by quota. The prior 30-sample Canary latency remains p50 `3418.8718 ms`, p95
    `4253.1475 ms`, p99 `4421.4291 ms` for semantic routing.

16. **Tests.** Focused provider/service tests passed `37/37`. Provider adapter integration tests
    passed `15/15` with the real-provider test skipped when opt-in is absent. The opt-in real
    provider test was run and reached OpenAI but failed with the documented external quota error.
    Feature 128 focused, Feature 125 regression, Feature 129, Feature 132, billing exactly-once,
    and full-suite reruns remain required after provider credits are restored.

17. **Remaining blockers.** Restore provider credits and run the required bounded live sample
    (minimum 10 requests, preferably 5 value and 5 trend), then verify valid structured output,
    semantic execution, billing persistence, separated usage, and latency. The broader previously
    classified integration failures remain out of scope.

18. **Promotion verdict — `product_sales_value`.** `KEEP_CANARY`. SemanticPrimary remains disabled
    because no post-fix live provider-backed execution has completed.

19. **Promotion verdict — `product_sales_trend`.** `KEEP_CANARY`. SemanticPrimary remains disabled
    because no post-fix live provider-backed execution has completed.

## Post-Fix Provider Validation

Validation date: `2026-10-05` (Asia/Tehran). This continuation stopped at the mandatory provider
precondition. No production code, rollout configuration, billing/pricing rule, or capability
scope was changed.

1. **Provider quota/status.** The real OpenAI provider was reached with the configured credential,
   but returned HTTP `429` with the message `You have no credits remaining`. The adapter classified
   this as `hosted_provider_quota_exceeded`, so the validation status is `PROVIDER_QUOTA_BLOCKED`.
   No further retries or corpus requests were issued.

2. **Provider/model.** Provider: `OpenAI`. Model: `gpt-5.6-luna` (the provider-contract test
   default; no `SEMANTIC_PROVIDER_MODEL` override was configured).

3. **json_schema confirmation.** The provider-backed request was rejected before generation, so
   live acceptance of the strict `json_schema` contract cannot be confirmed in this run. The
   existing deterministic adapter contract test remains the local confirmation that schema-backed
   requests map to Responses `text.format.type=json_schema` with `strict=true`.

4. **Sample count.** `0` generated provider-backed samples. One minimal precondition request was
   attempted and stopped at the quota gate.

5. **Valid structured outputs.** `0` generated; no validity rate claimed.

6. **Invalid structured outputs.** `0` generated; no invalid-output rate claimed.

7. **Provider errors.** `1` provider error, classified as `PROVIDER_QUOTA` (`HTTP 429`,
   credit balance exhausted).

8. **Timeouts.** `0`.

9. **Truncations.** `0` generated responses; not evaluated.

10. **Schema validation failures.** `0` generated responses; not evaluated.

11. **Mandatory query results.** The four smoke queries and the 20-request post-fix corpus were
    not run because the provider precondition failed.

12. **Entity resolution outcomes.** Not evaluated; no semantic proposal was produced.

13. **Semantic execution count.** `0`.

14. **Billing reconciliation.** Not evaluated; no post-fix semantic business execution occurred.

15. **Provider usage reconciliation.** Not available; the provider rejected the request before
    generation and no usage-backed semantic sample was produced.

16. **Latency.** Semantic, routing, and total post-fix p50/p95/p99 are `N/A`; no generated
    provider-backed request completed.

17. **Regression tests.** The single opt-in provider precondition test was executed and failed at
    the external quota gate. The requested provider-backed corpus and subsequent regression suite
    were not run, per the stop condition. Existing previously recorded local test results remain
    unchanged.

18. **Remaining blockers.** Restore OpenAI credits, rerun the one-request precondition, then run
    the required bounded post-fix corpus and all requested regression tests. Do not enable
    `SemanticPrimary` while this evidence is missing.

19. **Promotion verdict — `product_sales_value`.** `KEEP_CANARY`.

20. **Promotion verdict — `product_sales_trend`.** `KEEP_CANARY`.

Final status: `FEATURE_128_PROVIDER_VALIDATION_BLOCKED_BY_QUOTA`
