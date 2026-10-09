# Feature 141 Monthly Production and Sales Evaluation

## Scope and evidence

The fixture at `artifacts/monthly-production-sales-evaluation.json` defines 13 required questions and ten essential database scenarios. This report separates catalog and planner/compiler tests from PostgreSQL fixture execution. All PostgreSQL values below are synthetic fixture data; no Development financial database was queried.

## Current implementation results

| Area | Result | Evidence and limits |
|---|---|---|
| Monthly measures | 4 direct source mappings loaded | Production quantity, sales quantity, sales amount, and provider-reported sales rate. Three can compile; sales rate fails closed until its unit is verified. Growth metrics remain separate from this direct-source slice. |
| Product identity | Planner binding added | Uses the existing company-scoped product resolver through `IStructuredQueryProductBinder`. The compiler requires one verified resolved product key with provenance. Cross-company equivalence is not inferred. |
| Plan validation | Implemented for direct monthly measures | Requires one resolved company and one verified product, exact monthly measure/projection, the catalog relationship, valid latest or bounded-period selection, and a maximum 36-month range. Planner unit coverage exercises company/product binding. |
| SQL compiler | Latest scalar/table retrieval and bounded time series compile | SQL identifiers come from the integrity-checked catalog snapshot; provider, report type, output type, revision policy, company, product, period limits, and pagination values are parameters. Free-form filters, aggregation, growth arithmetic, multi-company comparison, and ranking fail closed. |
| Authorization | Production snapshot remains unauthorized | Monthly objects and the logical relationship remain unapproved and non-queryable in the canonical catalog. An isolated test snapshot was explicitly approved only for the disposable PostgreSQL test database. |
| PostgreSQL execution | Passed on an isolated PostgreSQL 18 cluster | The compiled production query returned the latest accepted `ProductSales`/`OutputType=0` row. The compiled amount time series returned March and April in chronological order. Rejected revisions and cumulative `OutputType=1` rows were excluded. Two remaining accepted same-period rows returned `Ambiguous`. |
| Database role | Restricted reader verified | Test role had `SELECT` on `MonthlyReports` and `MonthlyReportLineItems`, no `INSERT`, and no superuser, database-creation, or role-creation privileges. The query ran through the existing read-only executor and Development authorizer contracts. |
| Financial statement regression | Passed | The existing Feature 131 PostgreSQL regression tests passed alongside the monthly fixture test. |
| Navigator evaluation | Not run | The 13 questions have not been scored through live Navigator/Planner/Compiler orchestration. Compiler support does not establish natural-language selection accuracy. |

### Verified SQL shape

Latest monthly retrieval compiles as a catalog-bound query over the report and line-item tables. Its selection constraints include the canonical company ID, resolved provider/company-scoped product code, `NoavaranCurrentApi`, `ProductSales`, `OutputType = 0`, `IsAccepted = true`, and revision status `Accepted`. It chooses the latest eligible `PeriodEnd`, returns the source value and row unit, and carries source report identity fields for evidence. It does not calculate a sales rate.

The time-series shape bounds `PeriodStart`/`PeriodEnd`, chooses at most the requested number of latest eligible monthly periods, and returns their line items ordered by reporting period ascending. It reports existing rows only; it does not interpolate missing months. More than one eligible row for the same period and product returns `Ambiguous` rather than choosing a report or silently deduplicating it.

All values are parameters. Table and column identifiers come from the catalog snapshot. The canonical production snapshot compiles these shapes as unauthorized, so the application runtime cannot execute them under current catalog governance.

## PostgreSQL scenario coverage

The executable fixture currently covers:

- Latest single-company production quantity.
- Multi-month sales-amount retrieval in chronological order.
- Accepted revision and `ProductSales`/`OutputType=0` filtering, including rejected and cumulative decoys.
- Ambiguous competing accepted same-period product rows.
- A restricted database role and read-only transaction.
- Production-catalog authorization rejection.
- Existing Feature 131 financial-statement PostgreSQL regression tests.

The separate 13-question evaluation fixture and ten scenario definitions are not all end-to-end executed yet. In particular, direct sales quantity and verified-unit sales rate have no PostgreSQL assertions; sales rate is intentionally unsupported until its unit is owner-verified. Missing-month behavior is row omission without interpolation, but no dedicated missing-month fixture assertion has been added. Tenant/company scope rejection and SQL-injection resistance have compiler/authorization-level coverage, not dedicated PostgreSQL cases. Row/byte/timeout guardrails are enforced by the shared executor but are not forced to their limits in this fixture.

## Supported and unsupported operations

Supported in the current vertical slice:

- Direct production quantity, sales quantity, and sales amount plans for one company and one verified product identity.
- Latest eligible monthly retrieval and a bounded chronological time series of up to 36 periods.
- Noavaran `ProductSales`/`OutputType=0` policy with accepted revision filtering.
- Source row unit and provider/report/revision provenance in query results.
- Fail-closed ambiguity when more than one eligible row remains for a product and period.

Not supported yet:

- Direct sales rate: its source rate unit is not verified. The compiler returns `monthly_provider_rate_unit_unverified`.
- Growth/difference arithmetic, period comparisons, aggregation, ranking, grouping, multi-company comparisons, company-level totals, and cross-company product equivalence.
- General typed filters beyond company, product, and bounded reporting-period selection.
- Live Navigator/Planner orchestration of monthly questions.

Quantity values retain their row-level provider unit. Since aggregation and comparison arithmetic are unsupported, the engine currently does not combine quantities across units. Sales amounts retain the catalog's million-IRR source unit. No new primary sales rate is calculated.

## Governance and regression boundary

Canonical monthly tables, columns, and the application-level `MonthlyReportLineItems.MonthlyReportId` to `MonthlyReports.Id` relationship remain unauthorized. The integration test creates an isolated synthetic approved snapshot in memory and a temporary PostgreSQL database with a restricted role. It does not change production approval metadata or connect to the Development financial database. Existing specialized monthly routes and services were left unchanged.

Semantic Catalog validation passed with hashes `e6432e56ac141a79d9a773e82854a38370cf92a1826e209efa2014fe03fdf39a` / `b640493595646fe7573804a9e8afa4419cd7030105390504a7d6e12bab1130d4`.

## Validation run

- Release solution build: passed, 0 warnings and 0 errors.
- Focused planner/compiler tests: 32 passed.
- Isolated monthly and Feature 131 PostgreSQL tests: 7 passed.
- Full solution test run: 1,967 unit tests and 12 architecture tests passed; integration tests reported 428 passed, 71 failed, and 35 skipped. Failures include existing endpoint assertions in monthly lookup/product-revenue tests (for example Persian unit-label/company-name expectations and a missing prior-fiscal-year metric). The focused monthly compiler/executor PostgreSQL test and Feature 131 PostgreSQL regression both passed in this run.
- SemanticCatalog Python tests: 27 passed; `catalog.py validate` passed.
- The temporary PostgreSQL 18 cluster was stopped and removed after the fixture run.

## Slice C implementation and evidence (2026-10-08)

The existing typed plan/compiler/executor now supports bounded monthly `TimeSeries` and `ApprovedPeriodComparison` plans. Comparison SQL uses a one-month or 12-month `LAG` offset for month-over-month/year-over-year matching, returns the reported base value, and leaves missing periods missing. Growth is guarded for zero and unsupported negative baselines. The compiler also supports governed sum aggregation, top-N product ranking within an explicitly bound company, and explicit multi-company amount aggregation. Top-N ranking of companies by sales/growth and ranking within industry or reviewed cross-company product categories are not implemented. Each company ID is carried into the compiled query and independently checked against tenant/company execution grants. Quantity aggregation returns unit compatibility evidence and rejects mixed-unit results. No company, product, industry, or cross-company equivalence is hardcoded.

The isolated PostgreSQL integration test `MonthlyStructuredQueryPostgreSqlTests.CompiledMonthlyRetrievalAndTimeSeriesExecuteThroughRestrictedReadOnlyExecutor` passed against a disposable PostgreSQL 18 cluster. It executed latest production retrieval with source-report identity, chronological sales series, 150% MoM sales growth (100 to 250) and absolute change (+150), production growth (10 to 20), YoY sales growth (50 to 100), explicit missing-month rejection, zero-denominator and negative-baseline rejection, multi-company sales totals (130 and 200), compatible production and sales-quantity sums, product ranking by sales and production, mixed-unit aggregate rejection, SQL parameterization, competing accepted-row ambiguity, provider/OutputType/revision filtering, and tenant/company authorization rejection through the restricted read-only executor. This is synthetic fixture evidence only; no Development financial observation was queried. Industry/category ranking does not yet have PostgreSQL execution evidence.

Unit guard coverage additionally verifies MoM SQL shape, aggregation/ranking plan constraints, and multi-company binding. Release solution build passed (0 warnings/errors), focused navigator/planner/compiler tests passed (50), and canonical SemanticCatalog validation passed. SalesRate remains unsupported: the normalizer copies the provider's SalesRate value (including legacy response-field aliases), but the row's `Unit` is a quantity unit and the source contract does not establish a verified rate unit. No divide-based replacement was introduced.

### Actual live model evaluation

The evaluator used the configured OpenAI `gpt-5.6-luna` provider at `api.openai.com` through Development configuration and User Secrets. Credential contents were not logged. The CLI runs independent requirements extraction and vectorless navigation only; it does not invoke company/product binding, the planner, compiler, executor, or any database query. All completed rows have `queryAuthorized=false`, `completenessStatus=Unsupported`, and no compile authorization because monthly domain/measure catalog approval remains pending. Thus the recorded live run is a Navigator evaluation, not a successful live Navigator-to-Executor pipeline.

| Set | Rows | Completed | Failed | Model calls | Input/output tokens | Exact measure on completed rows |
|---|---:|---:|---:|---:|---:|---:|
| Original monthly set | 13 | 8 | 5 | 27 | 125,737 / 13,867 | 6 / 8 |
| Independent held-out set | 3 | 2 | 1 | 6 | 32,406 / 2,850 | 2 / 2 |

Original-set outcomes: `monthly-03`, `monthly-12`, and `monthly-13` failed the financial-operation policy during requirements extraction; `monthly-09` and `monthly-11` hit the request input-token budget. Completed cases `monthly-04` and `monthly-06` extracted the wrong measure. Every completed navigation selected all 12 monthly metric IDs, including irrelevant ones, so context precision is not acceptable. `monthly-10` identified an approved-period comparison but did not pass completeness/governance. Provider cost metadata was unavailable.

| Case | Expected measure(s) | Extracted measure(s) | Company/product extraction | Period/operation extraction | Outcome |
|---|---|---|---|---|---|
| monthly-01 | Production quantity | Production quantity | `کگل` matched | Latest/direct matched | Navigator completed; unsupported/unapproved |
| monthly-02 | Production quantity | Production quantity | `کگهر`, `گندله` matched | Explicit month/direct matched | Navigator completed; unsupported/unapproved |
| monthly-03 | Production quantity | No extraction | None | MoM growth operation rejected by policy | Failed at requirements extraction |
| monthly-04 | Production quantity | Production growth YoY | `کگهر` matched | Extracted `latest` and `rank`; missed three-period comparison | Navigator completed; measure/period wrong, unsupported |
| monthly-05 | Sales amount | Sales amount | `کگل` matched | Explicit month/direct matched | Navigator completed; unsupported/unapproved |
| monthly-06 | Sales quantity | Sales amount | `کگهر`, `گندله` matched | Latest/direct matched | Navigator completed; wrong measure, unsupported |
| monthly-07 | SalesRate | SalesRate | Company and product were merged into one reference | Explicit two-month/direct selected; unit remains unverified | Navigator completed; unsupported/unapproved |
| monthly-08 | Sales amount | Sales amount | `کگل` matched | Explicit period and sort extracted; six-month window not independently represented | Navigator completed; unsupported/unapproved |
| monthly-09 | Sales quantity | No extraction | None | Cross-company ranking not reached | Failed at request input-token budget |
| monthly-10 | Sales amount | Sales amount | `کگل`, `کگهر` matched | Comparison scope `compare`; period extracted as `latest` | Navigator completed; unsupported/unapproved |
| monthly-11 | Monthly sales growth MoM | No extraction | None | Industry ranking not reached | Failed at request input-token budget |
| monthly-12 | Monthly sales growth MoM; operating profit growth YoY | No extraction | None | Cross-domain comparison operation rejected by policy | Failed at requirements extraction |
| monthly-13 | Production quantity; sales quantity | No extraction | None | Production/sales comparison operation rejected by policy | Failed at requirements extraction |

Compilation support and authorization were `false` for every completed row because catalog review is pending; the CLI stopped before company/product binding, planner, compiler, or execution. Therefore the table reports extracted navigation metadata, not successful query plans.

The genuinely separate held-out fixture uses three new question structures: two-month product sales-quantity series, explicit MoM sales growth, and bounded production top-three ranking. The series and ranking rows extracted the expected measure; held-out growth failed operation-policy validation. Dimensions were 2/3 complete by recall and exact entity extraction was 1/3. All completed rows remained unsupported and unauthorized. The fixture and captured outputs are in `artifacts/monthly-production-sales-heldout.json` and `artifacts/monthly-production-sales-heldout-live-2026-10-08.jsonl`; the original live output is `artifacts/monthly-production-sales-predictions-live-2026-10-08-v2.jsonl`.

| Held-out case | Expected / extracted measure | Company/product | Period/operation | Outcome |
|---|---|---|---|---|
| heldout-01 product sales series | Sales quantity / sales quantity | Mentions both `کگهر` and `گندله`; emitted as one combined reference | Two-period direct series intent was not fully represented as a time-series operation | Completed; unsupported/unapproved |
| heldout-02 MoM amount change | Sales amount / no extraction | No extraction | Growth operation rejected by policy | Failed at requirements extraction |
| heldout-03 production top three | Production quantity / production quantity | `کگل` matched | Sort extracted, bounded ranking intent incomplete | Completed; unsupported/unapproved |

### Clean-HEAD comparison

A clean detached worktree at `22f1f252e8e4163a6a25bf3566a7b34eec629318` and the working tree ran the full IntegrationTests project. Clean HEAD: 419 passed, 72 failed, 41 skipped (532 total). Working tree: 421 passed, 71 failed, 42 skipped (534 total). The 71 failing test names are identical; there are no working-tree-only failures. The sole clean-HEAD-only failure is `FinancialStatementSchemaTests.UniqueIndex_IsConfiguredOnTheNewTriple`. The existing monthly-sales route, previous-fiscal-year metric, company-name, and product-revenue endpoint failures reproduce on clean HEAD. The added monthly structured-query PostgreSQL fixture passes independently when pointed at the isolated local cluster. This comparison found no confirmed Feature 141 regression in the broad suite; it does not repair unrelated pre-existing failures.

### Remaining readiness

Slice C's minimum execution acceptance is met for one MoM comparison, one multi-company aggregation/comparison shape, and bounded ranking in the existing generic Planner -> Compiler -> Executor against isolated PostgreSQL. Before controlled Development activation, the catalog still needs reviewed monthly query authorization, improved Navigator precision/input usage, an operation/extraction policy contract for growth, actual validated company/product resolution under the service's authorization model, PostgreSQL cases for industry/category review and ranking of companies by sales/growth, and a full orchestration evaluation. Production routing remains disabled.


## Final Navigator precision and PostgreSQL evidence (2026-10-08)

### Navigator fix

The over-selection came from treating every monthly measure in the retrieved schema context as an answer measure. The Navigator now keeps three separate sets: context candidates, the LLM's final selected measures, and independently extracted required measures. Final selections are constrained to recognized measure IDs, while evaluation reports the broad candidate pool separately. The revised contract separates company and product references, requires typed monthly measure references and typed reporting-period bounds, and maps selected measures to their canonical catalog relationships.

The planner now treats a monthly `MonthlyMeasure` as the only value projection. Company, product, and reporting-period identities remain bound inputs; incidental shape projections cannot turn them into additional output measures. The plan validator checks this same contract. This fixed a real compiler rejection found when replaying the live question.

### Latest live Navigator evaluation

The refreshed evaluator used OpenAI `gpt-5.6-luna`. It executes requirements extraction and schema navigation only; it does not run plan validation, compilation, or a database query. The detailed per-question metrics and captured outputs are in `artifacts/monthly-production-sales-final-evaluation-2026-10-08.json` and the two JSONL prediction artifacts.

| Set | Cases | Navigator completed | Failed | Expected refs | Selected refs | Correct refs | Measure precision | Measure recall | Exact set among completed | Exact set across all cases |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Original, latest | 13 | 7 | 6 | 15 | 7 | 6 | 85.7% | 40.0% | 6/7 (85.7%) | 6/13 (46.2%) |
| Original, baseline | 13 | 8 | 5 | ? | ? | 6 | ? | ? | 6/8 (75.0%) | 6/13 (46.2%) |
| Independent held-out, latest | 3 | 2 | 1 | 3 | 2 | 1 | 50.0% | 33.3% | 1/2 (50.0%) | 1/3 (33.3%) |
| Independent held-out, baseline | 3 | 2 | 1 | ? | ? | 2 | ? | ? | 2/2 (100%) | 2/3 (66.7%) |

On the original set, every completed latest run separated the final selection from the 12-measure candidate pool. Six of seven completed cases selected exactly the expected measure; `monthly-06-sales-quantity-product-latest` selected sales amount instead of sales quantity. `monthly-04-production-top-products-growth` failed on an unrecognized reference. On held-out, `heldout-01-product-sales-quantity-series` selected sales amount instead of sales quantity, while the production top-products case selected the expected measure. This held-out regression is retained in the score; the precision repair does not resolve measure confusion for every new question structure.

Current original-set failures: `monthly-03`, `monthly-12`, and `monthly-13` were rejected during requirements extraction for unsupported financial-value operations; `monthly-04` failed on an unrecognized financial reference; `monthly-09` and `monthly-11` exceeded the request input-token budget during schema navigation. The held-out `heldout-02` case exceeded the same input-token budget. These are requirements/policy, reference-mapping, and prompt-budget failures, not provider quota, timeout, or JSON-parse failures. Provider billing estimates were unavailable.

The earlier baseline's five original failures and one held-out failure classify as follows: three original cases (`monthly-03`, `monthly-12`, `monthly-13`) and held-out `heldout-02` were rejected by the requirements-layer unsupported-operation policy; `monthly-09` and `monthly-11` exceeded the configured request input-token budget during navigation. In that baseline, no failure was due to provider quota, transport timeout, or output parsing. This classification is based on the recorded baseline artifacts, separate from the latest rerun's additional `monthly-04` reference failure.

### Deterministic and live PostgreSQL runs

The isolated PostgreSQL integration test seeds persisted fixture companies and company-scoped product identities, uses the existing company/product resolvers, authorizes only its disposable fixture catalog, and executes with a restricted read-only role. Three deterministic query structures passed through extraction replay, navigation replay, binding, planning, validation, compilation, and PostgreSQL execution:

- Product production quantity for `????`, Shahrivar 1405: **47** source units.
- Company monthly sales amount for `???`, Shahrivar 1405: **450** fixture units (stored in million IRR).
- Six-period monthly sales time series: **[100, 120, 150, 200, 250, 300]** in chronological order.

A separate live Persian natural-language question, `????? ????? ????? ???? ?? ?????? ???? ???? ?????`, ran through the configured LLM, real persisted company and product resolvers, planner, validator, fixture-authorized SQL compiler, and restricted PostgreSQL executor. The model selected `metric_monthly_production_quantity`; company and product bindings resolved to fixture identities; the compiled query returned **47**, matching the fixture. It used 2 model calls (7,318 input tokens and 827 output tokens in the successful recorded run). Production semantic approval remains pending; the test's explicit authorization was scoped to the isolated fixture only.

The final repeat validation passed 55 focused unit tests and the isolated `MonthlyStructuredQueryPostgreSqlTests` integration test (1/1). The live Navigator evaluation score must not be read as planner/compiler coverage; only the explicitly described fixture pipeline runs cover those stages. Canonical production approvals and application routing remain unchanged and disabled for this query path.

## Feature 141 stabilization closeout (2026-10-09)

### Root causes and repairs

| Failure evidence | Root cause | Change / outcome |
|---|---|---|
| Baseline `monthly-03`, `monthly-12`, `monthly-13`; held-out growth case | Requirements extraction proposed a financial operation disallowed by the current operation policy. This was a semantic/policy failure, not provider quota or transport. | Kept unsupported growth typed and fail-closed where no approved operation/base is available. A governed sales growth reference now remains distinct from executable monthly source measures; production MoM and cross-domain growth remain unsupported pending policy/catalog review. |
| Baseline `monthly-04` | Model selected an unrecognized production-growth reference for a production-quantity ranking. | Monthly measure reference type now comes from the canonical monthly-measure mapping, not an ID prefix. The live question still selects the wrong growth metric, so no query executes. |
| Baseline `monthly-09` and `monthly-11` | Request-wide input budgets were exceeded (19,275/18,000 and 19,329/18,000 input tokens). Long policy/semantic prose and unrelated candidates inflated prompts. | Separated validated policy values from semantic prose; relevance-ranked and bounded monthly context; excluded irrelevant statement-source and industry nodes when inapplicable; included the output schema and protocol overhead in estimation. The latest original run had no input-budget failure. `monthly-11` now fails with `ModelOutputTruncated` after the bounded retry (HTTP 200), which is an output-cap issue rather than an input-budget or quota error. |
| Baseline `monthly-06` sales-quantity confusion | Sales amount and sales quantity semantics were not sufficiently explicit in context and the plan builder could promote incidental shape fields to value projections. | Canonical unit descriptions were strengthened, reference types are explicit, and monthly value projections are built only from the selected monthly measure. Latest live navigation selected sales quantity correctly; fixture execution returned 40. |
| Live `monthly-08` time-series validation | Planner intentionally removes LLM sort requests because chronological order is inherent, but the independent validator still compared against the removed LLM sort. | Validator now applies the same time-series ordering contract as the Planner. The corrected live series ran through SQL and matched all six expected periods. |
| Live `monthly-09` product ranking | The question names a product across companies without a resolvable company-scoped product identity. The resolver correctly refuses to guess. | Kept as `monthly_product_binding_required`; no SQL or numeric answer is produced. |
| Live `monthly-10` company comparison | Multiple company mentions were not independently bound and the plan lacked a valid comparison operation/scope. | Kept as typed plan validation failure; no SQL or numeric answer is produced. |
| Live `monthly-11` and `monthly-13` in the latest Navigator run | Structured output was truncated at HTTP 200 despite the single bounded retry. No quota or transport failure was recorded. | Retained as failed attempts in the score; no answer is inferred. |

### Navigator results and budgets

| Dataset/run | Completed | Exact selected measure set across all attempts | Input / output tokens | Model calls |
|---|---:|---:|---:|---:|
| Original baseline v6, corrected scorer | 7/13 (53.8%) | 6/13 (46.2%) | 144,532 / 16,561 | 29 |
| Original latest v9 | 9/13 (69.2%) | 7/13 (53.8%) | 140,908 / 18,533 | 32 |
| Independent holdout baseline | 2/3 (66.7%) | 1/3 (33.3%) | — | — |
| Independent holdout latest v3 | 2/3 (66.7%) | 2/3 (66.7%) | 38,069 / 5,047 | 8 |

The original set gained 15.4 percentage points in completion and 7.7 points in exact measure selection versus the corrected v6 baseline. Aggregate input tokens fell 2.5% while the number of model calls rose by three. The latest run had zero input-budget failures; output truncation and operation/reference errors remain. Per-case navigation latency was 18.3 seconds median and 23.1 seconds nearest-rank p95 (13 cases; provider cost metadata was unavailable). The evaluator does not have gold labels for all operation and typed-reference fields, so its operation accuracy and reference-type accuracy remain unscored; exact measure-set accuracy is the available reference-selection proxy.

Latest original failures/non-exact cases are retained in the raw artifact: `monthly-03` unsupported operation during extraction; `monthly-04` wrong/unrecognized production-growth selection; `monthly-10` completed without a selected measure; `monthly-11` and `monthly-13` truncated output; and `monthly-12` unsupported cross-domain operation. The other seven cases selected the expected measure set. Candidate/context IDs remain separately recorded from selected, required and incidental IDs.

### Natural-language to fixture PostgreSQL cases

The final eight-case run used Persian questions from the existing evaluation dataset, the live configured model, persisted fixture identities, existing company/product binders, Planner, validator, compiler, fixture-only authorization, and a restricted read-only PostgreSQL role. It produced four correct executions and four explicit non-executions. No incorrect numeric answer was returned.

| Case | Expected / selected measure | Result |
|---|---|---|
| `monthly-02-production-product-month` | Production quantity / production quantity | **47 = 47** source units; succeeded |
| `monthly-03-production-mom-growth` | Production quantity / production quantity | Typed pipeline failure: `requirements_contain_unsupported_financial_value_operation` |
| `monthly-04-production-top-products-growth` | Production quantity / production growth YoY | Measure-selection mismatch; unresolved projection/aggregation/sort references blocked planning |
| `monthly-05-sales-amount-month` | Sales amount / sales amount | **850 = 850** million IRR fixture units; succeeded |
| `monthly-06-sales-quantity-product-latest` | Sales quantity / sales quantity | **40 = 40** product units; succeeded |
| `monthly-08-sales-six-month-trend` | Sales amount / sales amount | **[120, 150, 200, 250, 300, 850] = expected**; latest six fixture reporting periods, chronological; succeeded |
| `monthly-09-cross-company-product-sales` | Sales quantity / sales quantity | Typed plan failure: company-scoped product identity could not be resolved without a company (`monthly_product_binding_required`) |
| `monthly-10-company-sales-comparison` | Sales amount / sales amount | Typed plan failure: company comparison binding and approved comparison operation/scope incomplete |

The v4 replay exposed an outdated test expectation for the six-period fixture after adding September data. The expectation was independently recomputed from the latest six persisted reports and both September product lines, then the preserved v5 replay passed all assertions. The live replay result is recorded in `artifacts/monthly-production-sales-live-llm-postgresql-eight-cases-v5-2026-10-08.jsonl`; the single-company production retrieval is also recorded in `...reliability-v6-2026-10-08.json`.

### Metamorphic/adversarial evaluation and remaining unsupported patterns

The 13-attempt metamorphic evaluation completed 10 cases, selected exact measure sets in 9/13, and passed 4/6 groups. Quantity-vs-amount, production-vs-sales, one-vs-two company count, and latest-vs-series presentation contrasts passed. Amount paraphrase and quantity-growth-vs-currency-growth did not. A targeted post-fix run correctly represented sales amount growth as `GovernedMetric` plus approved period comparison; quantity growth remains rejected because no approved quantity-growth operation is defined. SalesRate remains non-queryable until its provider unit is verified. Industry/category ranking, company rankings by sales/growth, ambiguous company-free product rankings, and unsupported production MoM remain typed failures or unverified paths.

### Quantitative release gates and readiness

Proposed gates for controlled Development activation: at least 90% Navigator completion and exact measure-set accuracy across all attempted original and independent holdout cases; at least 95% operation/reference accuracy on datasets with explicit gold labels; zero request-budget failures and zero incorrect numeric answers; at least 90% successful authorized fixture execution for supported cases; and 100% typed fail-closed behavior for ambiguous or unsupported cases. Expand the holdout before treating the thresholds as stable.

Current performance meets zero input-budget failures and zero incorrect numeric answers in the final fixture replay, but misses the Navigator gates (69.2% completion, 53.8% exact measure sets), the holdout exact-set gate (66.7%), and the fixture execution gate (4/8 overall). Operation/reference gold accuracy is not yet fully measurable. **Controlled Development activation is not technically ready.** Production query routing/authorization remains disabled; no Development or production financial data was accessed. Fixture authorization existed only inside the isolated PostgreSQL test, and the executor used the restricted read-only role.

### Final validation

- Full Release solution build: passed, 0 warnings and 0 errors.
- Focused Navigator/Planner/Compiler unit tests: 38 passed.
- Focused Planner unit tests after the time-series validator fix: 12 passed.
- Isolated `MonthlyStructuredQueryPostgreSqlTests`: 1 passed, 0 failed; final live eight-case replay assertions passed.
- Semantic Catalog Python tests: 27 passed.
- `python tools/SemanticCatalog/catalog.py validate`: passed; catalog hash `91bd31549418bdbd870b878079efa2808f9122d02cb7725aab5d4bab8595d1a2`.
- The disposable PostgreSQL 18 cluster was stopped after the final test.

## Feature 141 close-out — 2026-10-09

This dated close-out supersedes earlier metrics above where the same benchmark is reported. Detailed per-case evidence, root causes, normalization behavior, held-out outcomes and security boundaries are in [failure-root-cause-matrix-2026-10-09.md](evaluation/failure-root-cause-matrix-2026-10-09.md). All prior artifacts remain preserved. One malformed UTF-8 scoring sidecar is explicitly excluded there; the original 13-case source and its corrected UTF-8 evaluation sidecar are preserved.

### Before and after

| Evaluation | Before | Final | Interpretation |
|---|---:|---:|---|
| Eight live PostgreSQL cases | 4/8 correct executions; 0 incorrect numeric answers | **6/8 correct executions; 0 incorrect numeric answers** | Cases 03 and 04 now execute canonical production MoM comparisons; cases 09–10 remain typed planning failures. |
| Original 13-case Navigator completion | 9/13 | **11/13** | All attempted questions stay in the denominator. |
| Original 13-case exact selected-measure set | 7/13 | **10/13** | Financial operation field accuracy is 10/13 using the typed-operation labels recorded in the sidecar. |
| Previous independent three-question holdout | 2/3 completed and exact | **Retained unchanged: 2/3** | No rerun or replacement. |
| New independent seven-question holdout, first pass | — | **5/7 completed; 4/7 exact measure sets; 5/7 operation labels** | Navigator only; no SQL/database execution. The first-pass artifact is retained and was not used for tuning. |

### Eight-case expected versus actual

| Case | Expected | Actual | Outcome |
|---|---|---|---|
| `monthly-02` | Production quantity 47 | **47** | Correct; explicit month interval matched. |
| `monthly-03` | Production quantity growth 100% MoM | **100%** | Correct; typed GrowthPercentage + MonthOverMonth. |
| `monthly-04` | Top recent production growth 34.285714285714285714285714290% | **34.28571428571428571400%** | Correct within 0.0001 tolerance; base production quantity, ranked latest three-period comparison. |
| `monthly-05` | Sales amount 850 million IRR | **850 million IRR** | Correct. |
| `monthly-06` | Sales quantity 40 tons | **40 tons** | Correct. |
| `monthly-08` | Six-month series `[120, 150, 200, 250, 300, 850]` | **`[120, 150, 200, 250, 300, 850]`** | Correct; TimeSeries with six periods and no comparison scope. |
| `monthly-09` | Cross-company pellet sales quantity ranking | No numeric answer | Typed fail-closed `monthly_product_binding_required`; company-scoped product identity is not known across companies. |
| `monthly-10` | Same-period company sales comparison | No numeric answer | Typed fail-closed because the query supplies no common bounded period and sum aggregation. |

Final eight-case pipeline validity and compilation were **6/8**; restricted fixture execution succeeded **6/8**. No incorrect numeric answer was returned. Cases 09–10 are correct safety outcomes but remain failures for supported-answer coverage.

### Usage, latency, and failure stages

- Final eight-case replay: **81,415 input tokens**, **9,061 output tokens**, **18 model calls**, mean latency **14.072 s**, nearest-rank p95 **21.387 s**. The integration artifact does not split the two three-call cases into navigation expansion versus structured-output retry; every accepted result was fully parsed and validated before planning.
- Final original 13-question Navigator run: **128,832 input tokens**, **18,126 output tokens**, **28 model calls**, **3 bounded retries**, mean latency **15.580 s**, p95 **34.061 s**. Failures: one request-wide token-budget exhaustion (`monthly-11`) and two operation-validation failures for unsupported cross-domain derivations (`monthly-12`, `monthly-13`). `monthly-04`, `monthly-12`, and `monthly-13` each used one retry after an incomplete first response; the final outputs were parsed fully. `monthly-03` completed in the final pass but had stochastic invalid-operation failures in earlier attempts. Period-policy accuracy is **8/13** at the coarse latest/explicit-period level; exact bounds/maximum-period fields are not persisted by the Navigator scorer.
- New independent holdout first pass: **48,176 input tokens**, **5,464 output tokens**, **11 model calls**, no retries, mean latency **9.643 s**, p95 **15.420 s**. One operation-contract failure and one catalog-outline budget failure were counted in all seven attempts.

The 90% release target remains unmet: end-to-end answer coverage is **6/8**, original Navigator exact-set accuracy is **10/13**, and the new holdout exact-set score is **4/7**. Catalog approval, SalesRate unit verification, case 11 budget exhaustion and unsupported/ambiguous multi-company and cross-domain requests remain blockers. **Do not activate controlled Development routing.** Production routing and query authorization remain disabled.

### Final validation for this close-out

- `dotnet build src/backend/FinancialCopilot.sln -c Release --no-restore`: passed, **0 warnings / 0 errors**.
- Full UnitTests: **1,975 passed, 0 failed**.
- Isolated `CompiledMonthlyRetrievalAndTimeSeriesExecuteThroughRestrictedReadOnlyExecutor`: **1 passed, 0 failed**; this run wrote the v15 replay and v16 reliability artifacts.
- `python tools/SemanticCatalog/catalog.py validate`: passed; catalog integrity hashes unchanged.
- Disposable PostgreSQL 18 cluster: stopped after the final test.
