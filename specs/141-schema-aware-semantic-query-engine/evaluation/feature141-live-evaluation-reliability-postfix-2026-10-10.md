# Feature 141 live evaluation reliability and navigator verification

Date: 2026-10-10  
Evaluation execution date: 2026-10-09  
Provider/model: OpenAI / `gpt-5.6-luna`  
Catalog version/hash: `1` / `91bd31549418bdbd870b878079efa2808f9122d02cb7725aab5d4bab8595d1a2`

## Verdict

The live evaluation harness is now reliable enough to produce durable, diagnosable evidence: all 60 attempted navigator questions (the frozen original 50 plus the untouched v3 holdout 10) have exactly one terminal record, including provider faults, invalid structured output, cancellation, and hard timeout paths. The prior non-terminating outline-budget loop is fixed and covered by regression tests.

Feature 141 is **not production-ready**. Navigation completion improved materially, but semantic correctness remains low, catalog governance still forces most otherwise-retrieved cases to `UnsupportedOperation`, one original question timed out, and 6 of 15 live PostgreSQL matrix cases did not produce a completed numeric answer.

## Root cause and reliability repair

The live `fa-04` smoke stalled after requirements extraction and before an InitialNavigation provider request. The cause was an infinite loop in `JsonCatalogNavigationHierarchyProvider.GetCompactOutlineAsync`: the budget-trimming loop selected removable nodes from `includedIds`; an optional node retained as the ancestor of a required node remained in that set and could be selected repeatedly without changing the outline.

The repair:

- selects only from an explicit active optional-node set;
- removes attempted optional candidates even when they must remain as required ancestors;
- checks cancellation inside the trimming loop;
- adds `HierarchyProvider_RequiredCatalogAncestorCannotCauseNonTerminatingBudgetTrim` as a regression test;
- wraps each evaluation case in an outer hard deadline that does not depend on provider cancellation cooperation;
- writes an atomic `running` record before work begins and atomically replaces it with exactly one terminal record;
- reconciles stale `running` records after interrupted runs;
- records terminal status, failure stage/category, provider/model, call type, response ID, retry count, token usage, latency, and completion reason where exposed;
- exits non-zero when any case is not a completed navigation result.

Terminal statuses are exact typed values: `Completed`, `Timeout`, `Cancelled`, `ProviderError`, `InvalidStructuredOutput`, `SemanticValidationFailure`, and `UnsupportedOperation`.

## Live smoke proof

Artifact: [`feature141-live-smoke-outline-fix-2026-10-09.jsonl`](../artifacts/feature141-live-smoke-outline-fix-2026-10-09.jsonl)

- Case: `fa-04`
- Process exit: 0
- Wall time: 11.482 seconds
- Durable rows: 1
- RequirementsExtraction: completed
- InitialNavigation: completed
- LLM request: confirmed by provider response IDs and usage
- Final status: navigation `completed`; typed terminal status `UnsupportedOperation` because catalog governance denied execution
- Secrets: none logged

Earlier timeout diagnostics are preserved separately in `feature141-live-smoke-reliable-2026-10-09.jsonl` and `feature141-live-smoke-reliable-diagnostic-2026-10-09.jsonl`.

## Frozen original 50: before/after

The original dataset was not changed. Development 40 and frozen holdout-v2 10 were rerun separately.

| Metric | Before repair | After repair | Change |
|---|---:|---:|---:|
| Valid completed navigation results | 23/50 (46.0%) | 36/50 (72.0%) | +13 questions / +26.0 pp |
| Durable terminal records | 47/50 reported (2 stalled, 1 cancelled) | 50/50 (100%) | no missing attempts |
| Development completion | not separately comparable | 29/40 (72.5%) | — |
| Holdout-v2 completion | not separately comparable | 7/10 (70.0%) | — |
| Development exact measure set | 22.2% | 7/27 (25.9%) | +3.7 pp |
| Holdout-v2 exact measure set | 33.3% | 4/9 (44.4%) | +11.1 pp |
| Combined exact measure set | — | 11/36 (30.6%) | scorer-applicable completed cases |
| Combined financial operation | — | 8/22 (36.4%) | scorer-applicable cases |
| Combined period interpretation | — | 9/22 (40.9%) | scorer-applicable cases |

The 50-question run used 96 model calls, 572,209 input tokens, 52,832 output tokens, and 652,853 ms of provider-reported model latency. Provider cost was not exposed by the execution/usage contract, so no cost is claimed.

Artifacts:

- [`feature141-original50-dev-postfix-2026-10-09.jsonl`](../artifacts/feature141-original50-dev-postfix-2026-10-09.jsonl)
- [`feature141-original50-dev-postfix-score-2026-10-09.json`](../artifacts/feature141-original50-dev-postfix-score-2026-10-09.json)
- [`feature141-original50-holdout-postfix-2026-10-09.jsonl`](../artifacts/feature141-original50-holdout-postfix-2026-10-09.jsonl)
- [`feature141-original50-holdout-postfix-score-2026-10-09.json`](../artifacts/feature141-original50-holdout-postfix-score-2026-10-09.json)

### Per-question terminal classification: original development 40

Every attempted question is accounted for below.

| Classification | Count | Question IDs |
|---|---:|---|
| Completed navigation; safe unsupported outcome | 22 | `fa-03`, `fa-04`, `fa-05`, `fa-06`, `fa-value-01`, `fa-value-02`, `fa-value-03`, `fa-value-05`, `fa-value-06`, `fa-value-08`, `readiness-dev-01`, `readiness-dev-03`, `readiness-dev-05`, `readiness-dev-06`, `readiness-dev-07`, `readiness-dev-12`, `readiness-dev-14`, `readiness-dev-15`, `readiness-dev-16`, `readiness-dev-17`, `readiness-dev-20`, `readiness-dev-22` |
| Completed navigation; clarification / semantic validation | 7 | `fa-02`, `fa-08`, `readiness-dev-04`, `readiness-dev-13`, `readiness-dev-18`, `readiness-dev-21`, `readiness-dev-23` |
| Failed; invalid structured output persisted | 10 | `fa-07`, `fa-09`, `fa-10`, `fa-value-04`, `fa-value-07`, `readiness-dev-02`, `readiness-dev-08`, `readiness-dev-09`, `readiness-dev-10`, `readiness-dev-11` |
| Failed; hard timeout persisted | 1 | `readiness-dev-19` |

### Per-question terminal classification: frozen holdout-v2 10

| Classification | Count | Question IDs |
|---|---:|---|
| Completed navigation; safe unsupported outcome | 7 | `readiness-holdout-v2-01` through `readiness-holdout-v2-07` |
| Failed; invalid structured output persisted | 3 | `readiness-holdout-v2-08`, `readiness-holdout-v2-09`, `readiness-holdout-v2-10` |

## Untouched holdout-v3 first pass

Predictions were not inspected before execution completed. This cohort is reported separately and was not used to tune the implementation.

- Valid completed navigation: 9/10 (90.0%)
- Durable terminal records: 10/10 (100%)
- Exact measure set: 4/9 (44.4%)
- Financial operation: 0% on applicable scored cases
- Period interpretation: 0% on applicable scored cases
- Model usage: 21 calls; 121,052 input tokens; 11,584 output tokens; 161,675 ms latency
- Provider timeout/error/cancellation: none
- Failure: `readiness-holdout-v3-09`, invalid structured output caused by `requirements_contain_unsupported_financial_value_operation`

Per-question classification:

| Classification | Count | Question IDs |
|---|---:|---|
| Completed navigation; safe unsupported outcome | 8 | `readiness-holdout-v3-01`, `-03`, `-04`, `-05`, `-06`, `-07`, `-08`, `-10` |
| Completed navigation; clarification / semantic validation | 1 | `readiness-holdout-v3-02` |
| Failed; invalid structured output persisted | 1 | `readiness-holdout-v3-09` |

Artifacts:

- [`feature141-holdout-v3-first-pass-2026-10-09.jsonl`](../artifacts/feature141-holdout-v3-first-pass-2026-10-09.jsonl)
- [`feature141-holdout-v3-first-pass-score-2026-10-09.json`](../artifacts/feature141-holdout-v3-first-pass-score-2026-10-09.json)

## Fresh isolated PostgreSQL full-pipeline proof

A fresh PostgreSQL 18 cluster was initialized under the repository's `.tmp` directory on loopback port 55432 with test-only trust authentication. The fixture created its own database and restricted read-only execution role. The server was stopped and the verified test-only cluster directory was removed after the run.

The live-LLM 15-case integration matrix passed its test contract in 4m56s. All rows are fresh and tagged `postfix-2026-10-09`; historical evidence files were not overwritten.

| Result class | Count | Evidence |
|---|---:|---|
| SQL executed and exact fixture answer rendered | 9 | production, sales amount/quantity, MoM growth, ranking, six-month time series, and company comparison |
| Safe incomplete result | 2 | producer sales rankings withheld because product mapping coverage was partial |
| Safe unsupported/ambiguous result | 2 | sales-rate operation mismatch; underspecified industry-growth ranking |
| Navigation/pipeline failure before planning | 2 | unsupported financial-value operation in cross-domain comparison requests |

No executed numeric case returned an incorrect value. The successful answers include exact Persian-rendered values `۴۷`, `۱۰۰`, `۳۴٫۲۸۵۷۱۴۲۸۵۷۱۴۲۸۵۷۱۴`, `۸۵۰`, `۴۰`, the six-point trend `۱۲۰، ۱۵۰، ۲۰۰، ۲۵۰، ۳۰۰، ۸۵۰`, comparison `۷۰۰/۸۵۰`, `۱۲`, and `۳۷`. The matrix used 28 model calls, 148,496 input tokens, 14,985 output tokens, and 204,795 ms model latency; it logged no secret values.

The independent dynamic company-discovery case succeeded with explicitly partial coverage and found four positive pellet producers, including the dynamically introduced `NEWP` fixture.

A separate generic financial-statement regression passed through captured navigation requirements, real offline planning/binding/validation, `StructuredQueryCompiler`, restricted Npgsql execution, and the Persian renderer. It returned the exact fixture value `3753` as `۳٬۷۵۳ IRRPerShare`.

Artifacts:

- [`monthly-production-sales-live-llm-postgresql-fifteen-cases-readiness-v2-2026-10-09-postfix-2026-10-09.jsonl`](../artifacts/monthly-production-sales-live-llm-postgresql-fifteen-cases-readiness-v2-2026-10-09-postfix-2026-10-09.jsonl)
- [`monthly-company-population-discovery-live-llm-postgresql-readiness-v2-postfix-2026-10-09.json`](../artifacts/monthly-company-population-discovery-live-llm-postgresql-readiness-v2-postfix-2026-10-09.json)
- [`monthly-production-sales-live-llm-postgresql-readiness-v2-diagnostic-2026-10-09-postfix-2026-10-09.json`](../artifacts/monthly-production-sales-live-llm-postgresql-readiness-v2-diagnostic-2026-10-09-postfix-2026-10-09.json)
- [`financial-statement-generic-pipeline-2026-10-09-postfix-2026-10-09.json`](../artifacts/financial-statement-generic-pipeline-2026-10-09-postfix-2026-10-09.json)

## Quality assessment

### A. Navigation reliability

- Valid terminal navigation results: **36/50 (72.0%)** on the frozen original set, up from 46.0%.
- Terminal-record reliability: **50/50 (100%)** original and **10/10 (100%)** v3 holdout.
- Remaining reliability defects: 13 invalid structured-output cases across the 60 questions and one hard timeout in the original 50.

### B. Semantic correctness

- Exact measure selection remains weak: 30.6% combined on scorer-applicable completed original cases.
- Financial operation and period interpretation are worse: 36.4% and 40.9% combined on applicable original cases, and both 0% on the untouched v3 holdout.
- Safe refusal behavior is strong: false-positive acceptance remained 0%, but this does not substitute for answering supported questions.

### C. End-to-end correctness

- 9/15 matrix cases executed with verified expected values and Persian rendering.
- 4/15 stopped safely with typed incomplete/unsupported results.
- 2/15 failed before planning.
- The generic financial-statement case executed correctly and preserved the exact value.
- No successful SQL execution produced an observed numeric mismatch.

## Verification

- Release solution build: passed, 0 warnings / 0 errors.
- Full Release unit suite: 2,027 passed, 0 failed, 0 skipped.
- Focused navigator/planner/compiler/reliability suite: 70 passed.
- Fresh live PostgreSQL matrix integration: 1 passed.
- Generic financial-statement PostgreSQL regression: 1 passed.
- Python semantic catalog validation: passed.
- .NET semantic catalog validation: passed.
- Hierarchy hash: `6995d437b5f0a7f1098e1b427e47ba227db51fba28916b1db607fcccb668b0aa`.

## Remaining bottleneck and recommended priorities

The dominant bottleneck is no longer missing evaluator records. It is the gap between retrieved context and governed, semantically correct executable intent.

1. Approve and publish navigation/query governance for the catalog nodes currently marked `navigation_metadata_pending`, `semantic_meaning_unapproved`, and `query_not_authorized`; these flags force most completed runs to `UnsupportedOperation`.
2. Constrain requirements extraction to the supported financial-operation registry and add repair/retry examples for comparison and cross-domain questions; this is the v3 failure and both matrix pipeline failures.
3. Improve structured-output recovery under the hard deadline. Invalid structured output accounts for 13/60 attempted questions.
4. Correct operation-kind and period inference, especially time-series versus direct value and comparison-mode selection; untouched-holdout operation and period accuracy are both 0%.
5. Complete governed product-equivalence and company-population coverage before enabling producer rankings; current behavior correctly withholds rankings under partial coverage.
6. Reduce prompt/context size after correctness is stabilized. The frozen 50 consumed 625,041 tokens and more than 10.8 minutes of summed provider latency.
7. Add an externally supplied provider-cost capture path if release decisions require currency-denominated cost evidence.

Release readiness should be reconsidered only after the frozen original 50 and a new untouched holdout show materially higher exact measure, operation, and period accuracy, the catalog approvals allow supported plans to execute, and the 15-case PostgreSQL matrix has no untyped pipeline failures.
