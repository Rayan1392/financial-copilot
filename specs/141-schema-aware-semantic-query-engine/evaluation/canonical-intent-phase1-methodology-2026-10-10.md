# Canonical intent Phase 1 evaluation methodology

## Benchmark and gold handling

`canonical-intent-benchmark-v2.json` is reproducibly built from the unchanged Feature 141 development-v1 (40 cases), historical holdout-v2 (10 cases), and historical holdout-v3 (10 cases). `canonical-intent-corrections-v2.json` is the auditable overlay: it records the source Persian question ID, old label by reference to the untouched source file, corrected semantic interpretation, evidence/rationale, expected fields, and status. The builder preserves each original metric label as `legacyExpectedMetrics`.

Eleven cases currently have explicit corrected/adjudicated canonical expectations. Other cases retain their source question and historical labels but have no exact canonical expected fields until reviewed. They are not scored as correct or incorrect. Cases with catalog/authorization limitations and genuine ambiguity remain in the benchmark and have explicit status; those statuses are not treated as malformed intent.

The 40+10 questions have previous live runs and are development/historical-v2 data, not unseen holdout. Holdout-v3 is also marked previously used. No new unseen holdout is claimed.

## Shadow run

From repository root, after checking provider configuration and using an authorized evaluation tenant:

```powershell
python specs/141-schema-aware-semantic-query-engine/evaluation/build_canonical_intent_benchmark.py
dotnet run --project tools/SemanticCatalog --configuration Release -- evaluate-canonical-intent `
  --tenant-id <evaluation-tenant-guid> `
  --dataset specs/141-schema-aware-semantic-query-engine/evaluation/canonical-intent-benchmark-v2.json `
  --output specs/141-schema-aware-semantic-query-engine/artifacts/canonical-intent-shadow-<run-id>.jsonl
python specs/141-schema-aware-semantic-query-engine/evaluation/score_canonical_intent_shadow.py `
  --shadow specs/141-schema-aware-semantic-query-engine/artifacts/canonical-intent-shadow-<run-id>.jsonl `
  --output specs/141-schema-aware-semantic-query-engine/artifacts/canonical-intent-shadow-score-<run-id>.json
```

The CLI writes a started event before each attempt and a terminal record after both extractors finish; terminal records include original question, gold status/fields, raw legacy interpretation, Option B intent or sanitized failure, model-call count, token counts, latency, and a `secretValuesLogged=false` marker. It never calls catalog binding, query planning, SQL compilation, or PostgreSQL. `--preflight-only` reports eligible structured-output provider configuration without sending model requests. Outputs use create-new semantics to avoid overwriting earlier artifacts.

## Outcome taxonomy and denominators

The shadow scorer keeps malformed/truncated output, typed-schema failure, unsupported semantic intent, unknown catalog reference, missing catalog coverage, semantic-policy rejection, query-authorization rejection, planner/compiler unsupported operation, provider timeout/cancellation, provider failure/unavailable, and internal implementation error separate. Each field reports its own exact-match denominator. Unconditional intent validity includes extraction failures in the selected case count; conditional field accuracy is limited to completed, deterministically valid Option B intents. Unrun benchmark cases are not treated as failures. Unmatched started attempts are reported as canceled/reconciled rather than silently dropped.

## Phase 1 live shadow evidence (2026-10-10)

The configured Development OpenAI provider ran all 60 cases from the benchmark. This is the existing development and historically used holdout-v2/v3 material, not an unseen test set. The shadow path did not bind a catalog, plan or execute a query, access financial data, or route production/Development chat traffic.

| Measure | Result |
|---|---:|
| Terminal cases | 60 / 60 |
| Deterministically valid Option B intents | 54 / 60 (90.0%) |
| Failure taxonomy | 54 completed; 5 unsupported semantic intent; 1 provider failure; 0 unmatched starts |
| Cases with adjudicated gold | 11 / 60 (49 await gold review) |
| Operation | 10 / 11 exact (90.9%) |
| Comparison | 10 / 11 exact (90.9%) |
| Entity scope | 10 / 11 exact (90.9%) |
| Period | 9 / 11 exact (81.8%) |
| Output | 9 / 10 exact (90.0%) |
| Product labels | 6 / 6 exact (100%) |
| Clarification decision | 2 / 3 exact (66.7%) |
| Mean / max Option B latency | 5,577 ms / 9,782 ms |
| Reported token totals | 79,607 input / 28,077 output across 59 cases |

Measure-concept, unit, threshold, aggregation, and ranking exact-match results must not be interpreted as reliable semantic accuracy yet. The reviewed gold currently names concepts with English identifiers (for example `productionQuantity`), while the schema-independent contract allows the extractor to preserve free-form Persian concept labels. The raw string scorer consequently reports 0/11 exact measure labels; this is an unresolved gold/contract alignment issue, not evidence of 0% semantic recognition. Nonetheless, the live rows show substantive misses: `fa-value-02` omitted the per-share distinction and unit, and `readiness-holdout-v3-09` returned an unspecified unit for the requested million-IRR amount. Ranking/aggregation should also be reviewed individually before treating the current low scores as a model-only issue.

Artifacts: raw JSONL is `../artifacts/canonical-intent-shadow-phase1-live-2026-10-10.jsonl`; scorecard is `../artifacts/canonical-intent-shadow-phase1-live-2026-10-10-score.json`. The scorer was corrected after the run to normalize numeric enum values emitted by the initial runner; the scorecard is regenerated from the unchanged raw rows. The runner now writes stable camelCase enum strings for future runs.

Recommendation: do not proceed to Phase 2 routing/activation. Continue Phase 1 with adjudication of the remaining gold, align the measure-concept contract and scoring vocabulary, repair unit/measure qualifier extraction, and re-evaluate on newly authored unseen questions after the scoring rules are frozen. The present run establishes plumbing and exposes quality gaps; it is not a promotion result.

Verification after recording this run: `dotnet build src/backend/FinancialCopilot.sln --configuration Release --no-restore` succeeded with 0 warnings/errors; all 2,033 UnitTests passed; all 12 ArchitectureTests passed; the canonical intent scorer/builder Python tests passed 4/4; and `SemanticCatalog validate-catalog` verified source/hierarchy hashes (`91bd31549418bdbd870b878079efa2808f9122d02cb7725aab5d4bab8595d1a2`). The broad IntegrationTests suite was not run as part of this Phase 1 slice.

## Phase 1.5: canonical measures, qualifier preservation, and adjudication (2026-10-10)

### Gold provenance and benchmark construction

`canonical-intent-adjudications-v3.json` is an overlay; the three source datasets and v2 correction overlay are unchanged. The reproducible v3 builder produces 60 source-question rows, 30 cases with explicit reviewed gold, one disputed case, and 29 awaiting adjudication. Ten gold rows reuse earlier corrected cases whose model predictions had already been seen (`predictionBlindReview=false`); twenty additional original Persian questions were reviewed without inspecting this run's predictions (`predictionBlindReview=true`). Review was performed by one reviewer, not independently double-reviewed and not owner consensus. `fa-value-02` stays disputed: the original Persian question asks for latest operating profit, while the earlier correction says per-share operating profit; the question/overlay conflict is not silently resolved.

The new holdout is authored separately from the historical 60 and frozen before its first model request. It has 12 cases (9 unambiguous, 1 genuinely ambiguous, 2 genuinely unsupported). Its source and materialized benchmark SHA-256 values at freeze are respectively `D4C62DA1B9992BB311178BBE6880F467541FE341A8B6723F892D40743D3D3EF1E8` and `CCE24D5FE953981F454506A8A7D4DA79E3CF204C3D0BE3C002AB88CE4CBB9554`. Questions, gold, extractor, registry, and scorer were not changed after predictions were requested. It is independent of the historical question set and prediction-blind, but it has not received a second human adjudication; it must not be represented as independently validated domain gold.

### Contract and scorer changes

The v2 typed intent now keeps governed business concept IDs independent of catalog/MetricCode/SourceItem IDs, original user phrases, measure dimensions, reported/change value form, reporting basis, statement scope, company/product grain, requested unit, and effective source unit plus evidence. The registry resolves only exact reviewed, normalized terms; bare `فروش` stays ambiguous and unknown labels remain unresolved. It does not contain company/product dictionaries or physical data availability. The extractor cannot assert source-unit evidence; requested-unit compatibility, qualifier combinations, operation requirements, and population scope are validated deterministically. Unsupported operations are classified separately.

Scoring now compares concept IDs and per-measure qualifier/unit tuples, includes measure-resolution/ambiguity fields where adjudicated, normalizes named enums and unresolved source units, and emits per-case comparison details with explicit denominators. Historical source labels remain preserved. Legacy exact-match baseline on the same 30 reviewed cases: measure concepts 16/30 (53.3%), operation 11/30 (36.7%), comparison 15/30 (50.0%), period 14/30 (46.7%), entity scope 23/30 (76.7%), product labels 8/10 (80.0%), and clarification 2/6 (33.3%). Legacy has no representation for the new qualifier/dimension/source-evidence fields; those are unavailable, not 0%-accurate.

### Phase 1.5 live outcomes

The same 60 questions were sent through the shadow-only CLI using the final v2 intent contract. The isolated path made no catalog binding, planning, SQL compilation, financial-data query, or chat-route call.

| Measure | Result |
|---|---:|
| Terminal cases | 60 / 60 |
| Option B completed intents | 0 / 60 |
| Option B failure taxonomy | 60 provider failures (`hosted_provider_failed`, upstream `provider_error`); no semantic/schema outcomes observed |
| Adjudicated cases in benchmark | 30; exact-intent valid-output conditional denominator: 0 |
| Option B exact measure/qualifier/operation accuracy | Not observable: no valid model output |
| Legacy baseline | Same 30 reviewed cases; field rates above |

The 0/60 completion rate is an observed provider-path failure, not evidence that the registry or semantic extraction is 0% accurate. The new scorecard retains unconditional pipeline outcomes and reports conditional semantic denominators as zero. No semantic disagreement can be attributed to the extractor in this run.

The frozen 12-case holdout was requested once after the 60-case development run. It also returned 12/12 provider failures (same sanitized `hosted_provider_failed|http:na|upstream:provider_error` pattern), with no valid Option B intents. Its apparent 0/12 exact fields and 0/2 unsupported classifications are not semantic accuracy estimates; this first pass is inconclusive because the provider did not return model output. No retries, prompt edits, scorer changes, or gold changes were made after viewing it.

Artifacts: `../artifacts/canonical-intent-shadow-phase1_5-60-2026-10-10.jsonl` and `../artifacts/canonical-intent-shadow-phase1_5-60-2026-10-10-score.json`; first-pass holdout `../artifacts/canonical-intent-independent-holdout-v1-first-pass-2026-10-10.jsonl` and `../artifacts/canonical-intent-independent-holdout-v1-first-pass-score-2026-10-10.json`. Per-case legacy-vs-Option-B details are embedded in the scorecards. The Phase 1 live artifact remains unchanged.

### Verification and decision

Release build of `tools/SemanticCatalog`: succeeded, 0 warnings/errors. Focused `CanonicalSemanticIntentTests`: 14/14 passed. Python benchmark/scorer tests: 5/5 passed. Catalog integrity validation passed with source/hierarchy hash `91bd31549418bdbd870b878079efa2808f9122d02cb7725aab5d4bab8595d1a2`. Full UnitTests: 2,040 passed, 1 failed, 0 skipped; the failure was `Feature126WorkerTests.MasterDisabledDoesNotCreateScopeOrStartScheduledExecution` (empty recorded-scope assertion), outside the Phase 1.5 code touched here; no clean-baseline comparison was performed in this turn.

Recommendation: **do not proceed to Phase 2**. Phase 1.5 improves representation and makes gold/scoring language-independent, but this run cannot establish semantic correctness due to a provider outage; 10 of 30 reviewed rows are not prediction-blind, the adjudications have a single reviewer, and the first-pass holdout is provider-inconclusive. Restore provider availability, independently review/resolve gold provenance, then rerun the frozen holdout only against a stable extractor/scorer version. Keep production/Development routing and query execution disabled.

### Phase 1.5 provider/schema recovery diagnostics (2026-10-10)

The 60-case and frozen 12-case artifacts above remain unchanged. Their rows store only the broad `hosted_provider_failed|http:na|upstream:provider_error` wrapper and therefore do not prove an upstream outage, authentication failure, or quota exhaustion. Secret-safe endpoint probing reached `https://api.openai.com/v1/` and returned HTTP 401 without credentials (expected for an unauthenticated probe); the configured evaluation selected the same OpenAI `gpt-5.6-luna` provider and `UserSecrets` credential source, with a credential configured. The credential value was neither read into output nor persisted. A simple structured request through the existing AI abstraction succeeded (55 input / 17 output tokens).

The first post-diagnostic canonical extraction reproduced a local request-serialization failure before HTTP: the Phase 1.5 schema text was not valid JSON. After repairing that syntax, OpenAI returned HTTP 400 with upstream code `invalid_json_schema`. The sanitized response identified the exact contract defect: `measures.items.required` included `requestedUnit`, but a misplaced brace had nested that property under `qualifiers.properties` instead of the measure's `properties`. The schema root/$defs structure and measure-property nesting are now corrected, and a regression test parses the schema and verifies strict required/property alignment. Thus, the shared failure is a malformed Phase 1.5 structured-output schema, while the broad client catch mislabeled application/request errors as provider errors. The initial 72 raw rows cannot retroactively be assigned an HTTP status; the reproducible local parse failure and subsequent actual 400 response establish the defect without claiming those rows received 400 responses.

This is the first verified Phase 1 → Phase 1.5 behavioral divergence: Phase 1's successful run used the prior `schema_requirements_v1` extraction contract with a catalog outline, while Phase 1.5 introduced the standalone `canonical_semantic_intent_v2` strict response schema with measure-unit and qualifier fields. The v2 extractor requests up to 900 output tokens, uses a 25-second extraction deadline, and has a two-call cap; the old path used configurable navigator token/call budgets. Provider/model selection, endpoint, Development configuration, and winning secrets source are unchanged in the current configuration comparison. The relevant request change was the v2 strict `json_schema` response format, not a provider/model or production-routing change. These schema-construction failures happen before useful generation or return immediately as HTTP 400, so token exhaustion, the configured 120-second HTTP timeout, rate limiting, retry delay, and cancellation do not explain the recorded fast failures. The OpenAI transport retries only HTTP 429 rate limits (up to three total attempts); the v2 schema parse/400 path is not retried. Phase 1 and 1.5 necessarily have different extraction prompts matching their different contracts; no prompt was edited during recovery. Error handling now distinguishes request serialization, invalid request schema, response deserialization, transport/auth/quota/rate-limit failures, invalid structured output, semantic validation, unsupported intent, provider timeout, evaluation-case timeout/cancellation, and internal application exceptions; provider status/code and sanitized schema diagnostics are retained where present.

Post-fix smoke outcomes are persisted in `../artifacts/canonical-intent-phase1_5-provider-diagnostics-2026-10-10.jsonl`. The first canonical question completed; of the three diverse Persian intent smokes, two completed and one was rejected by semantic validation (`requested_unit_dimension_mismatch`) with no provider HTTP error. Since not every requested diagnostic intent smoke passed, the gated 60-case benchmark and frozen 12-case holdout were not rerun. No benchmark labels, holdout questions, prompt, canonical intent semantics, Navigator, Planner, SQL compiler, or authorization were changed. Therefore post-fix measure/qualifier accuracy and any semantic improvement remain unmeasured; the earlier 54/60 Phase 1 result remains the only successful benchmark baseline.

Regression verification after the schema fix: Release solution build succeeded with 0 warnings/errors; UnitTests passed 2,042/2,042; ArchitectureTests passed 12/12; canonical intent focused tests passed 15/15; scorer tests passed 6/6. The full IntegrationTests project finished 421 passed, 71 failed, 43 skipped (535 total). Failures included broad scanner/monthly-sales/endpoint expectations; PostgreSQL-backed cases also reported Testcontainers could not start because Docker was unavailable. These failures are outside the shadow-only schema/provider edits and need a working Docker environment plus baseline triage; no clean-HEAD full-suite comparison was performed. The previously reported `Feature126WorkerTests.MasterDisabledDoesNotCreateScopeOrStartScheduledExecution` passed twice in isolation and in the full unit suite. Its test and Worker source are unchanged versus HEAD, so the earlier single full-suite failure was not reproduced and is not attributable to Phase 1.5; a separate clean-HEAD execution was not performed.

Recommendation: do not proceed to Phase 2 or claim semantic improvement yet. The request path now reaches and returns structured intent for representative questions, but one of the three diverse diagnostic prompts still hit a semantic validation failure. Diagnose that model-output mismatch without weakening the canonical contract, then rerun the complete smoke gate. Only after that gate is clean should the original 60 and untouched frozen 12 be run once and scored.
