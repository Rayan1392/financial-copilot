# Feature 141 — Evaluation Baseline Stabilization Report (2026-10-28)

**Verdict: `BASELINE_STABILIZED`.** One authoritative, versioned 20-case suite exists; the executable cases (1 answered, 3 typed rejections) pass reproducibly through the production chain with the live provider and an isolated database; latest-period query semantics are covered by 11 PostgreSQL scenarios and fail closed; the governance boundary is explained; every baseline test failure is attributed against a pristine `HEAD`. Chat routing was not enabled and no catalog approval was added or changed.

## 1. Authoritative case matrix

Source of truth: `evaluation/engine-e2e-suite-v1.json` (v1.0.0) with `evaluation/engine-e2e-fixture-v1.json`. The two earlier sets (`engine-e2e-cases.json` E01–E20 and `engine-e2e-results.json#cases` E141-01..20) are retained unchanged as history and mapped through `legacyIds`. `Feature141EngineE2ESuiteTests` enforces: 20 unique `F141-Exx` IDs, Persian question text, a typed expected outcome, a blocker from the vocabulary for every non-executable case, evidence artifacts on disk for every PASS, and agreement with the runner's cases (question, outcome, value, unit, Gregorian and Persian period).

| ID | Legacy | Category | Question | Expected | Status | Blocker |
|---|---|---|---|---|---|---|
| F141-E01 | E01, E141-01 | statement | سود خالص کگل چقدر است؟ | 1200 MillionIRR | BLOCKED | B-CONCEPT-BINDING (netProfit) |
| F141-E02 | E02 | statement | سود عملیاتی کگهر چقدر است؟ | 1100 MillionIRR | BLOCKED | B-CONCEPT-BINDING (operatingProfit) |
| F141-E03 | E03, E141-03 | statement | سود هر سهم فملی چقدر است؟ | 3200 IRRPerShare | BLOCKED | B-CONCEPT-BINDING (earningsPerShare) |
| F141-E04 | E04, E141-02 | statement | سود عملیاتی هر سهم کگهر چقدر است؟ | 3753 IRRPerShare, 2026-03-21..2026-06-21 = 1405-01-01..1405-03-31 | **PASS** | — |
| F141-E05 | E05, E141-04 | comparison | سود خالص کگل و کگهر را مقایسه کن | 1200 / 900 MillionIRR | BLOCKED | B-CONCEPT-BINDING + B-PLANNER-ALGEBRA |
| F141-E06 | E06 | comparison | کدام یک سود خالص بیشتری دارد، کگل یا کگهر؟ | کگل 1200 | BLOCKED | B-CONCEPT-BINDING + B-PLANNER-ALGEBRA |
| F141-E07 | E07, E141-06 | monthly | تولید گندله کگل در شهریور ۱۴۰۵ چقدر بود؟ | 25 Ton, 1405-06 | BLOCKED | B-MONTHLY-MEASURE-REVIEW |
| F141-E08 | E08, E141-08 | monthly | مبلغ فروش گندله کگهر در شهریور ۱۴۰۵ چقدر بود؟ | 700 MillionIRR, 1405-06 | BLOCKED | B-MONTHLY-MEASURE-REVIEW |
| F141-E09 | E09, E141-07 | monthly | مقدار فروش گندله کگل در شهریور ۱۴۰۵ چقدر بود؟ | 20 Ton, 1405-06 | BLOCKED | B-MONTHLY-MEASURE-REVIEW |
| F141-E10 | E10, E141-09 | discovery | کدام شرکت‌ها در شهریور ۱۴۰۵ گندله تولید کردند؟ | کگل, کگهر | BLOCKED | B-DISCOVERY-SCOPE + monthly review |
| F141-E11 | E11, E141-11 | equivalence | شرکت‌های تولیدکننده گندله سنگ‌آهن را پیدا کن | Incomplete until approved | BLOCKED | B-PRODUCT-EQUIVALENCE |
| F141-E12 | E12, E141-13 | period comparison | تولید گندله کگل … نسبت به شهریور ۱۴۰۴ | +3 Ton | BLOCKED | B-MONTHLY-MEASURE-REVIEW |
| F141-E13 | E13, E141-14 | period comparison | رشد مبلغ فروش گندله کگل … | 80 Percent | BLOCKED | B-MONTHLY-MEASURE-REVIEW |
| F141-E14 | E14 | ranking | شرکت‌ها را بر اساس سود خالص … مرتب کن | فملی, کگل, کگهر | BLOCKED | B-PLANNER-ALGEBRA (+ binding, scope) |
| F141-E15 | E15, E141-16 | filter | شرکت‌هایی با سود خالص بالای ۱۰۰۰ … | فملی, کگل | BLOCKED | B-PLANNER-ALGEBRA (+ binding) |
| F141-E16 | E16 | aggregation | مجموع سود خالص کگل و کگهر | 2100 MillionIRR | BLOCKED | B-PLANNER-ALGEBRA (+ binding) |
| F141-E17 | E17, E141-17 | multi-condition | … سود خالص بالای ۱۰۰۰ … و EPS بالای ۲۰۰۰ … | فملی, کگل | BLOCKED | B-PLANNER-ALGEBRA (+ binding) |
| F141-E18 | E18, E141-18 | ambiguous | فروش کگل چقدر است؟ | Ambiguous | **PASS** | — |
| F141-E19 | E19, E141-19 | unsupported | ارزش ذاتی کگل در سال آینده چقدر می‌شود؟ | Unsupported | **PASS** | — |
| F141-E20 | E20, E141-20 | unauthorized | سود عملیاتی هر سهم کگهر چقدر است؟ (no tenant/actor) | Unauthorized, 0 model calls | **PASS** | — |

Totals: 20 cases, 4 PASS, 16 BLOCKED, 0 FAIL, 0 NOT_RUN. No unexecuted case is marked passed; the summary block is checked by the suite test.

## 2. Test commands and exit codes

```powershell
# disposable isolated PostgreSQL (Docker Linux engine still returns Internal Server Error on this host)
powershell -NoProfile -ExecutionPolicy Bypass -File tools/SemanticCatalog/scripts/Feature141DisposablePostgres.ps1 -Action start
$env:FINANCIAL_COPILOT_TEST_POSTGRES_CONNECTION_STRING = (Get-Content "$env:LOCALAPPDATA\feature141-disposable-postgres\connection.txt" -Raw).Trim()

# A. live full-chain runner: 1 answered + 3 typed rejections                      -> Passed 4/4, exit 0 (two consecutive runs: 19:20Z and 19:21Z)
$env:FEATURE141_RUN_LIVE_LLM_POSTGRES = "1"
dotnet test tests/FinancialCopilot.IntegrationTests/FinancialCopilot.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~Feature141EngineVerticalSliceTests"

# B. latest-period semantics (11 scenarios, no LLM)                               -> Passed 1/1, exit 0 (failed 3 scenarios before the executor fix; artifact kept)
dotnet test tests/FinancialCopilot.IntegrationTests/FinancialCopilot.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~Feature141LatestPeriodSemanticsPostgreSqlTests"

# C. suite invariants + legacy matrix                                             -> Passed 2/2, exit 0
dotnet test tests/FinancialCopilot.IntegrationTests/FinancialCopilot.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~Feature141EngineE2ESuiteTests|FullyQualifiedName~Feature141EngineE2ECaseMatrixTests"

# D. unit tests touched by the changes                                            -> Passed 82/82, exit 0
dotnet test tests/FinancialCopilot.UnitTests/FinancialCopilot.UnitTests.csproj -c Release --filter "FullyQualifiedName~ScalarTieResolution|FullyQualifiedName~CanonicalSemanticIntentTests|FullyQualifiedName~CanonicalIntentRequestedFieldNormalizationTests|FullyQualifiedName~StructuredQueryDirectSourceEffectiveUnitTests|FullyQualifiedName~CanonicalIntentCatalogBindingTests|FullyQualifiedName~MonthlyCompiler|FullyQualifiedName~DirectSourceMapping"
```

Full-suite numbers (working tree vs. pristine `HEAD` worktree, same host, same isolated database where applicable) are in section 6.

## 3. Query-semantic findings (Task 2)

`Feature141LatestPeriodSemanticsPostgreSqlTests` seeds eleven companies, builds the plan through the production handoff/planner/validator (captured navigator, real hierarchy context), compiles, authorizes and executes under a SELECT-only role, twice per scenario.

| Scenario | Before fix | After fix (asserted) |
|---|---|---|
| S1 latest statement contains the item | Succeeded 3753 | Succeeded 3753, period 2026-06-21 |
| S2 latest statement lacks the item; older period has it | **Succeeded with no value** (`در دسترس نیست`) | NoData `eligible_statement_observation_missing`; older 3001 never substituted |
| S3a consolidated + standalone share latest PeriodEnd, scope unspecified | Ambiguous `different_reporting_period_scopes` | unchanged |
| S3b consolidated newer than standalone, scope unspecified | **Succeeded with the consolidated 2200** (silent basis change) | Ambiguous `statement_scope_unspecified_across_consolidation_bases` |
| S3c standalone requested | Succeeded 1100 (T1 standalone) | unchanged |
| S3d consolidated requested | Succeeded 2200 (T2 consolidated) | unchanged |
| S4a two statements, same PeriodEnd, same value | Succeeded 4000, tie resolved, both identities kept | unchanged |
| S4b two statements, same PeriodEnd, different values | Ambiguous `requested_item_values_conflict` | unchanged |
| S5 three periods with different values | Succeeded 5300 (latest) | unchanged |
| S6 latest line item has NULL value | **Succeeded with no value** | NoData `eligible_statement_observation_missing` |
| S7 another provider has a newer statement | Succeeded 7100 (provider filtered) | unchanged |

All eleven executions were deterministic (identical rows and status on the second run). The two fixes are confined to `NpgsqlReadOnlyStructuredQueryExecutor` (single-row resolution and basis check in `ResolveStatementTie`); no financial-data rule was invented: a missing or NULL observation is reported as missing, and an unscoped question spanning two consolidation bases is returned to the user as ambiguous. Evidence: `artifacts/latest-period-semantics-2026-10-28-before-executor-fix.json`, `artifacts/latest-period-semantics-2026-10-28.json`.

Open policy (not decided here): when the user names no basis and only one basis exists in the latest period while the other basis exists in older periods (S3b), the engine now asks instead of answering. If the product owner prefers "answer with the basis available in the latest period and state it", that is a one-line policy change plus a Persian response mentioning the basis.

## 4. Authorization findings (Task 3)

Four independent gates govern a direct statement-item retrieval:

1. **Source identity approval** (`identityApproval`, `titleApproval`, `identityStatus=verified-persisted-source-key` on the catalog entry): the provider/statement/item/UUID identity is verified. Required by the completeness validator, plan validator and compiler for any direct source.
2. **Semantic concept approval** (`semanticReviewStatus=approved` and `canonicalConceptBindings[].status=approved` on the entry): the item's meaning and its equivalence to a canonical concept. Required by `CanonicalSourceItemBindingResolver` / `CanonicalIntentCatalogBinding` to accept a concept-based question; a question that states the exact registered title can bypass concept equivalence by design.
3. **Query authorization** (`queryAuthorization.queryable=true` on the entry, `queryable`/`runtimeQueryable` on the four tables and three relationships, `approvedObjects` columns): the compiler derives `QueryAuthorized` from these only.
4. **Runtime execution grant** (`CatalogStructuredQueryAuthorizer` + executor identity check): environment, designated database, SELECT-only role, tenant, exact company IDs, operation, catalog hash, bounded limits.

Why `SemanticReviewApproved=false` while execution was eligible: the plan-level flag is computed by `SchemaContextCompletenessValidator` as "every selected hierarchy node has `semanticApproval.approved`" **and** every resolved reference is approved. The item entry and its own hierarchy node are approved, but the ancestor and sibling nodes pulled into context (`financial-semantic-catalog`, `financial-statement-source-items`, `source-items-incomestatement`, `domain-*`, `table-*`, `dimension-dim_company`) are still `pending-review`, so the conjunction is false. Eligibility came from the explicit exact-identity path: the validator's `directSourceIdentityAuthorized`, the compiler's `IdentityAuthorized` (verified identity + item/table/relationship/column query authorization) and the executor's `SemanticReviewApproved || IdentityAuthorized`. The catalog records this path deliberately (`queryAuthorization.state = approved-direct-source-identity`, reason "permits parameterized retrieval by the verified registered source identity only; it does not approve canonical semantic equivalence").

Assessment: **intended governed behavior, not an authorization gap**, with one reporting gap. Concept equivalence for the executed case was approved at the item level (`operatingProfitPerShare` binding), so the question was answered under both identity and item-level semantic approval; the false plan flag reflects navigation-hierarchy metadata review, not the item's meaning. Recommended follow-up (no flag changed here): expose an item-level `SemanticBindingApproved` in the trace/plan so reports do not conflate node-level review with item-level meaning, and keep `SemanticReviewApproved` as the node-level signal. No additional query scope was permitted: all four PASS cases used only `source_item_682beceadddc1458`, one company, `DirectValueRetrieval`.

## 5. Typed rejection execution (Task 4)

Runner additions: a counting `IAiModelExecutionService` decorator (also injected into the navigator) and a counting executor decorator; the production `Feature141ChatQueryService` is invoked in-process for its pre-model gate.

| Case | Observed typed outcome | Model calls | Executor calls | SQL | Runs |
|---|---|---|---|---|---|
| F141-E18 ambiguous | `Ambiguous` — `canonical_measure_ambiguous` (candidates salesAmount/salesQuantity; clarification questions returned) | 1 | 0 | none | 3/3 identical |
| F141-E19 unsupported | `Unsupported` — extractor validator category `unsupported_semantic_intent` (deterministic, before any catalog work) | 1 | 0 | none | 3/3 identical |
| F141-E20 no tenant/actor | `Unauthorized` — `IsEnabled` false, message `این قابلیت برای درخواست احراز هویت‌شدهٔ فعلی فعال نیست.` | **0** | 0 | none | 3/3 identical |

Note on E19: the chat orchestrator renders an unsupported extraction as the generic "need more detail" clarification text; the typed category is visible in the extraction outcome, not in the user message.

## 6. Baseline failure attribution (Task 5)

Method: the same commands were run on the working tree and on a pristine `git worktree` of `HEAD` (`4cec2a0`) with the same isolated database.

| Suite | Working tree | Pristine HEAD | Attribution |
|---|---|---|---|
| Unit, focused Feature 141 filter (handoff, binding, UAT, planning, compilation, effective unit) | 54 passed / 12 failed (66) | 25 passed / 12 failed (37) | The 12 failures are identical on HEAD: **pre-existing / stale**. |
| Unit, full project | 2377 passed / 20 failed (2397) after this task (2372 / 20 of 2392 before it; the 5 new tests pass) | the same 20 failures reproduced on HEAD (focused reruns) | 12 stale Feature 141 tests + 8 unrelated pre-existing (NadpcoApi backfill, SchemaNavigator ×2, SemanticDialogueGovernance, Feature128 routing ×3, Telegram 143). |
| Integration, no live opt-in | 556 passed / 58 failed / 58 skipped (672) | 555 passed / 58 failed / 57 skipped (670) | Same 58 names on both: **pre-existing**, none in Feature 141 classes. |
| Integration, `MonthlyStructuredQueryPostgreSqlTests` with isolated database | `CompiledMonthlyRetrievalAndTimeSeries…` fails (population discovery → `Unauthorized` at line 259) | fails identically on HEAD | **Pre-existing**, not caused by the executor change (the change touches only non-monthly scalar resolution). |

Classification of the 12 stale Feature 141 unit tests (require an approved contract decision, not weakening):
- `FinancialSourcePlannerHandoffTests.ExactIdentity_…` ×4: assert `Authorization.Queryable=false` / `not-authorized` for items the catalog now authorizes (`approved-direct-source-identity`).
- `FinancialSourcePlannerHandoffTests.ConsolidationQualifierWithoutCatalogEvidenceStopsBeforePlanner`: expects `statement_scope_evidence_unavailable`, a code the current handoff never emits (it maps the qualifier to `StatementScopes`). Decision needed: should a consolidation qualifier without catalog scope evidence stop before the planner (as the test says) or be passed through as a statement scope (as the code does)?
- `StructuredQueryCompilationTests` ×5: `@is_composing` expected in every direct SQL (compiler now adds it only when a scope is requested); snapshot builder expected to reject drift / not grant access (catalog now authorizes); development authorizer expectations tied to the older `ReviewedTenantCompanyBindings` semantics.
- `Feature141DeveloperUatAuthorizationTests` ×2: assert the old Development opt-in/allowlist policy that `Feature141DeveloperUatAuthorizationPolicy` replaced with the general flag + authentication.

Infrastructure-only failures: `Slice1PostgreSqlTests` ×6 and the `Failed to connect to Docker endpoint` noise (Testcontainers; Docker Linux engine broken on this host). Feature 141 regressions introduced by this work: **none** (every new or changed assertion passes; every failure reproduces on HEAD).

## 7. Production changes in this task (narrow, test-justified)

- `NpgsqlReadOnlyStructuredQueryExecutor`: single-row scalar resolution (NULL/absent item → `NoData`) and fail-closed basis check (S2, S3b, S6).
- `CanonicalSemanticIntentCanonicalizer`: drops an output field that merely restates a requested measure (the live model emitted `RequestedFields:["سود عملیاتی هر سهم"]` in one run and the typed binding/handoff rejected the slice; evidence `artifacts/engine-e2e-vertical-slice-2026-10-28-F141-E04-run2-unsupported-requestedfields.json`). Different fields are preserved and still rejected by the handoff.
- Runner: call counters, typed-outcome mapping (`unsupported_semantic_intent` → Unsupported), per-run artifact file names, suite-linked cases.

Observation recorded, not changed: `RequestedUnit=IRRPerShare` on the answered case is set by the canonicalizer's `ImpliedRequestedUnit`, not by the model; the typed binding accepts it only because it equals the approved effective unit.

## 8. Remaining blockers

| Blocker | Cases | Owner | Minimal action |
|---|---|---|---|
| B-CONCEPT-BINDING (netProfit, operatingProfit, earningsPerShare, totalAssets) | E01–E03, E05, E06, E14–E17 | Product owner / catalog governance | Approve `canonicalConceptBindings` on the source-item entries and hierarchy; regenerate hash. |
| B-MONTHLY-MEASURE-REVIEW | E07–E10, E12, E13 | Product owner | Approve the four monthly measure entries (`review.semanticApproved`, `review.queryable`). |
| B-PRODUCT-EQUIVALENCE / B-DISCOVERY-SCOPE | E10, E11, E14 | Product owner | Reviewed equivalence group; `Feature141:CompanyDiscoveryScopes`. |
| B-PLANNER-ALGEBRA | E05, E06, E14–E17 | Engineering | Comparison, ranking, filter, aggregation, multi-measure operators with tests. |
| Stale-test contract decisions | 12 unit tests | Product owner + engineering | Confirm current catalog/handoff/compiler contracts, then update tests (do not weaken). |
| Docker Desktop Linux engine | Testcontainers suites | Host owner | Repair, or keep the disposable instance as the supported path. |

## 9. Next recommended implementation slice

**Monthly production quantity for one company and month (F141-E07, `تولید گندله کگل در شهریور ۱۴۰۵ چقدر بود؟`).** Prerequisite: the product owner approves `metric_monthly_production_quantity` (`review.semanticApproved`, `review.queryable`) and the two monthly tables/relationship flags in the catalog. Everything else already exists and is proven: the monthly compiler, the product binder, the `گندله` product concept, the existing captured-navigator PostgreSQL test (`CompiledMonthlyRetrievalAndTimeSeries…` value 47) and the live monthly runner path in `MonthlyStructuredQueryPostgreSqlTests`. The slice then reuses `Feature141EngineVerticalSliceRunner` with a seeded monthly fixture (25 Ton, 1405-06) and adds the case to the suite as executable; no planner algebra work is needed. Comparison/ranking slices should follow only after the statement-item bindings are approved, because they need both approval and new operators.
