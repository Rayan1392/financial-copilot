# Industry comparison symbol-resolution regression

## Root cause confirmed before the production fix

## Runtime routing regression confirmed from the reported Redis failure

Commit `bba705a7` changed both API semantic-routing defaults from `SemanticPrimary` (100% rollout) to `Shadow`, and only opted the product sales capabilities into `Canary`. It left `symbol_vs_industry_relative_valuation` unconfigured, so the normal API request did not execute the existing Feature 125 semantic comparison handler. The request fell through to the V2 agent tool-calling path; that path can select `ScannerToolAdapter`, which reads `IScannerCache` before doing scanner work. API `appsettings.json` simultaneously sets `ScannerCache:UseRedis=true` with Redis at `localhost:6379`. With Redis unavailable, this fallback path fails with the reported `HMGET` timeout instead of returning the comparison table.

This explains the observed user-facing regression and Redis log. The earlier adapter/arbitration defects describe additional failures when the semantic comparison route is enabled (as in the endpoint tests), but they were not sufficient to restore production behavior while rollout configuration kept that route in Shadow mode.

## Production deployment follow-up: capability override was not loaded

The production request log for `کگهر را با صنعت خودش مقایسه کن` shows that semantic interpretation selected `symbol_vs_industry_relative_valuation`, extracted `کگهر`, and resolved its company ID. The same event records `RolloutMode=Shadow`, `ExecutionSource=Legacy`, and the final dialogue state `entity_ambiguous`; thus the resolver did not lose the ticker. The semantic comparison execution path was evaluated but not used, and the legacy V2 tool path returned the ambiguity message.

The repository's base `appsettings.json` scopes this capability to `SemanticPrimary`, but inspection of the running API container found only `appsettings.Production.json`, with no `SemanticRouting` section. Docker Compose injects the global `SemanticRouting__DefaultMode=Shadow`, and did not inject a capability-specific override. Production therefore used Shadow for this capability even though the checked-out source had the intended per-capability setting. Local Development loaded its development configuration and followed the semantic path, explaining the local/server difference.

The deployment correction is a Compose environment override for only `SemanticRouting__Capabilities__symbol_vs_industry_relative_valuation`. This makes the existing Feature 125 handler active in production while leaving the global default and other capabilities unchanged.

## Follow-up runtime report: remaining company-resolution gap

After enabling the semantic route, the reported response changed to the system's `DisambiguationNeeded` message. That wording is produced by `AiDialogueOutcomePolicy` when a capability execution reports an ambiguous company resolution. The endpoint regression fixture had supplied an exact `کگهر` entity span directly, so it did not cover a semantic proposal that omitted the ticker or returned a broad span for it. The adapter only resolved the proposed entity spans, leaving the canonical resolver no deterministic fallback to the exact ticker token already present in `OriginalText`.

The follow-up correction therefore asks the existing canonical resolver to check normalized tokens from the original user text against canonical ticker values and promotes only exact ticker matches. Fuzzy results for unrelated entity spans and exact-name ambiguities remain subject to the existing ambiguity mechanism.

## Table presentation regression confirmed from the screenshots

Commit `5bbf472` appended Persian classification phrases to each metric cell. The existing frontend already recognizes the exact metric-only Markdown header and builds the compact, color-classified industry table itself using the member percentages and industry benchmark row. Adding prose to the cells changed the established presentation and caused status text to be shown inside every cell. This was an unnecessary change to the renderer input and is being reverted.


Feature 128 added model-proposed, typed entity spans to the deterministic interpretation. The Feature 125 `IndustryRelativeValuationSemanticAdapter` still treated every span as a possible company name and submitted it to both the company and industry resolvers. For an own-industry comparison, a span such as `صنعت خودش` can therefore be evaluated as a company mention. If the canonical resolver returns an ambiguity for that generic phrase, the adapter immediately returns `Ambiguous` even when another span is the exact known ticker `کگهر`.

The same new semantic route also exposed a second routing gap for the supplied paraphrases: `SemanticArbitrator` unconditionally put every deterministic candidate ahead of the model proposal. Generic symbol lookup candidates for phrasings such as “کگهر نسبت به شرکت‌های هم‌گروه چطوره؟” therefore displaced the more confident semantic industry-comparison capability. This was verified through the API test harness before the arbitration correction.

The older Feature 125 tests exercised deterministic spans and verified the ticker and peer group, but did not include model-proposed spans with `EntityType=industry`. A new focused regression test reproduced the current failure before the production change: exact ticker plus typed industry reference returned `Ambiguous`.

The canonical resolver itself is not the source of the regression: its exact ticker match has confidence 1.0. The identity loss happens in the comparison adapter, which ignores entity type and lets unrelated ambiguity take precedence over that exact result. Feature 128's entity merge made these additional typed spans reach the adapter; the adapter failed to apply the type boundary. The paraphrase routing loss happens in semantic arbitration, which gives deterministic candidates unconditional precedence even when the semantic industry-comparison candidate has higher confidence.

## Intended correction

Honor semantic entity types in the existing comparison adapter. Only company/symbol mentions (and legacy untyped mentions) go to company resolution; only explicit industry mentions go to industry resolution. A relational phrase such as “its industry” is not a canonical industry name and should be excluded so the existing company membership lookup derives the peer group. A resolved exact ticker can overrule only fuzzy ambiguity from untyped spans on the single-company own-industry route; explicitly typed company ambiguity and exact-name ambiguity still require clarification. For industry-comparison capabilities, allow a higher-confidence semantic candidate to beat a weaker generic deterministic candidate while keeping deterministic precedence on ties.

The fix keeps the Feature 125 execution path: resolve the canonical company, derive its group from normalized eligibility data, and read the persisted industry-relative valuation result for the existing table presentation.

## Existing request path

The working Feature 125 path was:

1. `ConversationDialogueGate` selected `symbol_vs_industry_relative_valuation`.
2. `IndustryRelativeValuationSemanticAdapter` resolved the ticker through the Feature 119 canonical company resolver.
3. The adapter derived the company’s industry group from `NoavaranEligibleCompanies` and `IndustryGroups`.
4. `IndustryRelativeValuationCapabilityExecutor` read the already-published persisted valuation snapshot through `IIndustryRelativeValuationReadRepository`.
5. `IndustryRelativeValuationPresentation` rendered the P/E, P/S, and equilibrium comparison table using the existing metric classifications.

The broken Feature 128 path merged model entity spans into the interpretation. The comparison adapter treated a typed industry span as another company, so its fuzzy ambiguity short-circuited the exact ticker result. Separately, arbitration always preferred deterministic candidates over model candidates, allowing generic symbol lookup to displace a more confident industry-comparison intent for some paraphrases. The fix restores the existing path and does not add a retrieval or valuation mechanism.
