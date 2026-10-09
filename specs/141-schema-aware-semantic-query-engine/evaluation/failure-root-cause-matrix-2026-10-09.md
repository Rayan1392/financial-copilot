# Feature 141 Failure-Level Root-Cause Matrix — 2026-10-09

## Evidence and scoring boundaries

- Final eight-case live model-to-PostgreSQL replay: `artifacts/monthly-production-sales-live-llm-postgresql-eight-cases-v15-2026-10-09.jsonl` and `artifacts/monthly-production-sales-live-llm-postgresql-reliability-v16-2026-10-09.json`.
- Original 13-question Navigator set: unchanged source `artifacts/monthly-production-sales-evaluation.json`; UTF-8 Navigator sidecar `evaluation/navigation-monthly-original-13-preserved-utf8-2026-10-09.json`; output `evaluation/scored-navigator-monthly-original-13-final-v3-2026-10-09.jsonl`.
- New independent holdout first pass: `evaluation/navigation-heldout-v4-unseen-2026-10-09.json` and `evaluation/scored-navigator-heldout-v4-first-pass-2026-10-09.jsonl`. It ran before later original-set normalization fixes and was not tuned or rerun.
- Previous three-question holdout remains unchanged; its 2/3 completion and 2/3 exact-set result is recorded in the earlier evaluation artifacts.
- The Navigator evaluations do not execute SQL. `offline_plan_execution_disabled`, pending semantic approval and pending query authorization are expected for those runs. Only the eight-case PostgreSQL integration test executes, using the isolated fixture database and per-test restricted read-only role.

## Eight original live cases

| Case and question | Independently expected result | Final result and evidence | Failure analysis / disposition |
|---|---|---|---|
| `monthly-02-production-product-month` — مقدار تولید گندله کگهر در شهریور ۱۴۰۵ چقدر بوده؟ | ProductionQuantity; explicit Shahrivar 1405 period; 47 ton | **47 = 47**, exact reporting interval 2026-08-23 through 2026-09-22; DirectValue | No final failure. The explicit Jalali month resolved to the persisted report interval and the company-scoped product identity. |
| `monthly-03-production-mom-growth` — تولید کنسانتره کگل نسبت به ماه قبل چند درصد رشد کرده؟ | ProductionQuantity; GrowthPercentage; MonthOverMonth; two adjacent periods; 100% | **100% = 100%**, base quantity 12; GrowthPercentage + MonthOverMonth; maximumPeriods=2 | Earlier attempts terminated at requirements policy validation with `requirements_contain_unsupported_financial_value_operation`, before planning or SQL. The terminal rejection was an operation/reference contract mismatch after parsing a complete structured object. Final normalization derives the canonical comparison only from one catalog-typed monthly base measure, explicit prior-month language and typed comparison cues. |
| `monthly-04-production-top-products-growth` — کدام محصولات کگهر در سه ماه اخیر بیشترین رشد تولید را داشته‌اند؟ | ProductionQuantity; product ranking; latest three periods; GrowthPercentage + MonthOverMonth | **47 units; +34.28571428571428571400%**, expected +34.285714285714285714285714290%; latest window maximumPeriods=3; GrowthPercentage + MonthOverMonth | Earlier Navigator output selected/represented production growth as a YoY or direct-value shape, which lost the requested base measure and comparison. Generic normalization now requires one catalogued monthly base measure, latest bounded ranking, explicit growth wording, sum/group/sort shape and no YoY scope. Numeric difference is below 0.0001 tolerance. |
| `monthly-05-sales-amount-month` — فروش ریالی کگل در شهریور ۱۴۰۵ چقدر بوده؟ | SalesAmount; explicit Shahrivar 1405 period; million IRR | **850 = 850 million IRR**, exact reporting interval 2026-08-23 through 2026-09-22; DirectValue | No final failure. Amount measure and period are distinct from sold quantity and resolve to the correct source observation. |
| `monthly-06-sales-quantity-product-latest` — مقدار فروش گندله کگهر در آخرین گزارش ماهانه چقدر است؟ | SalesQuantity; latest accepted report; product-specific tonnage | **40 = 40 tons**, latest period; DirectValue | No final failure. The requested sold-volume measure remained distinct from ProductionQuantity and SalesAmount. |
| `monthly-08-sales-six-month-trend` — فروش ریالی کگل در شش ماه گذشته چه روندی داشته؟ | SalesAmount; six consecutive monthly periods; TimeSeries | **[120, 150, 200, 250, 300, 850]**, exact chronological series; TimeSeries + no comparison scope; maximumPeriods=6 | Earlier output attached a spurious MonthOverMonth scope to a plain series. The normalizer now clears comparison mode/scope unless growth or comparison intent is explicit; the compiler still returns six ordered monthly values. |
| `monthly-09-cross-company-product-sales` — کدام شرکت‌های تولیدکننده گندله بیشترین مقدار فروش را داشته‌اند؟ | Cross-company SalesQuantity aggregation and ranking over a common month | Typed planning failure `monthly_product_binding_required`; no SQL/value returned | A product key is company-scoped. The question does not specify a company and the catalog does not provide a reviewed cross-company pellet category/identity. Guessing that same-title products share an identity or unit would violate catalog policy. Correctly fails closed, but remains an unsupported query and does not count as successful coverage. |
| `monthly-10-company-sales-comparison` — فروش ماهانه کگل و کگهر را با هم مقایسه کن. | Same-period, compatible company totals; comparison table | Typed planning failure `multi_company_comparison_requires_common_bounded_period_and_sum` plus `retrieval_requirements_incomplete`; no SQL/value returned | The request has no explicit shared month/range or aggregate. The planner will not invent a comparison period or silently sum unlike scopes. A clarification is appropriate until the query specifies the shared period and aggregation. |

### Eight-case aggregate

- Correct end-to-end answers: **6/8** (75%).
- Correct numeric/series results: **6/6 executed**, **0/8 incorrect numeric answers**.
- Typed planning failures: **2/8** (cases 09 and 10). They are fail-closed but not successful answers for the attempted queries.
- Validated plan and compiled parameterized SQL: **6/8**; authorized fixture execution: **6/8**.
- Financial measure, operation, and required periods were exact for all six executed cases. Cases 09–10 were rejected before compilation/execution.
- Final replay usage: **81,415 input tokens**, **9,061 output tokens**, **18 model calls**, latency mean **14.072 s**, nearest-rank p95 **21.387 s**. The case artifact records total model calls but not the split between a navigation expansion and a structured-output retry for the two three-call cases. Every final result reached complete structured requirements/navigation and no partial response became a plan.

## Original 13-question Navigator set

| Cases | Root cause/status in the final 13-case pass |
|---|---|
| 01–02, 05–09 | Exact selected measure set and operation. Catalog review remains pending, so navigation outcome is not yet query-authorized/executable. Case 07 SalesRate also remains unsupported/clarification-required because the provider unit is unverified. |
| 03 | Exact ProductionQuantity and ApprovedPeriodComparison in the final pass. Earlier stochastic extraction responses returned an operation rejected by `ValidateRequirementPolicies`; a targeted repeat completed with the same canonical base measure and MoM operation. No invalid result was executed. |
| 04 | Exact ProductionQuantity and ApprovedPeriodComparison after normalizing the latest three-period ranked-growth shape to base-measure MoM; the PostgreSQL replay independently confirms maximumPeriods=3 and the expected growth. |
| 10 | Navigator completed with a clarification because the two-company request does not specify a common month/range. This is safer than guessing. |
| 11 | Request-wide token budget exhausted after 3 model calls (19,517 input and 3,318 output tokens; 34.061 s). No measure/operation result was accepted. This is a budget/context failure, not truncated JSON. |
| 12 | Requirements extraction failed operation validation for comparing monthly sales growth with financial-statement operating-profit growth. The catalog has no approved cross-period/cross-domain comparison operation; retaining distinct monthly and statement periods is required. |
| 13 | Requirements extraction failed operation validation for comparing production and sales growth direction. No approved cross-measure derived comparison exists. It remains a typed unsupported combination rather than a local formula. |

The UTF-8 sidecar was derived from the unchanged 13-case file with canonical reference types and independent typed-operation labels. An earlier sidecar was generated through PowerShell's default non-UTF-8 decoding and corrupted Persian questions; that output is preserved for audit but **excluded from scoring**. The valid input was checked (`کگل در آخرین ماه چه مقدار تولید داشته است؟`) before running the final evaluator.

Final 13-case metrics use all 13 attempted questions: Navigator completion **11/13**; exact selected-measure-set accuracy **10/13**; financial-operation-label accuracy **10/13**; coarse period-policy accuracy **8/13**. The original baseline was **9/13 completion** and **7/13 exact measure sets**. Exact period bounds/maximum periods are not emitted in the Navigator JSONL; those details were checked for the supported eight-case fixture replay instead. Final Navigator usage: **128,832 input tokens**, **18,126 output tokens**, **28 model calls**, **3 bounded retries**, mean latency **15.580 s**, nearest-rank p95 **34.061 s**. Three final attempts used a bounded retry after an incomplete first response (monthly-04, monthly-12, monthly-13); the retry output was parsed fully. Operation-policy failures in monthly-12/13 occurred after the final response had been parsed. Provider finish reason is not persisted for successful responses.

The 13-case fixture labels for cases 09 and 10 describe an explicitly selected month/range, but their Persian questions name no date or range. The coarse period score counts the Navigator's `latest` policy as a mismatch for those two labels. Those cases correctly remain unexecuted/clarification-required; the period-label inconsistency is a benchmark limitation, not a reason to invent missing dates.

## Independent seven-question holdout, first pass only

| Case | First-pass finding |
|---|---|
| `holdout-v4-production-prior-month-growth` | Requirements operation validation failure; explicit prior-month growth did not yield a valid operation on this first pass. |
| `holdout-v4-sales-tonnage` | Clarification: requested Shahrivar has no year, so the period cannot be bound unambiguously. No measure was selected. |
| `holdout-v4-sales-amount` | Exact SalesAmount reference/DirectValue label; unsupported for execution because semantic approval/query authorization remain pending. |
| `holdout-v4-production-series` | Exact ProductionQuantity reference/DirectValue label; unsupported for execution because semantic approval/query authorization remain pending. |
| `holdout-v4-ranked-production-growth` | Exact ProductionQuantity reference and ApprovedPeriodComparison label; unsupported for execution because semantic approval/query authorization remain pending. |
| `holdout-v4-two-company-monthly-sales` | Catalog outline budget exceeded before any model call (0 model calls). |
| `holdout-v4-unverified-sales-rate` | Exact SalesRate reference/DirectValue label; typed unsupported because its source unit is not verified and approvals remain pending. |

First-pass metrics over all seven cases: completed navigation **5/7**, exact measure set **4/7**, financial operation label **5/7**. There was no database query or numeric answer in this Navigator-only evaluation. Usage: **48,176 input tokens**, **5,464 output tokens**, **11 model calls**, no retry, mean latency **9.643 s**, p95 **15.420 s**. The previous independent three-case holdout remains **2/3 completed and exact**. No prompt/code changes were made using the seven-question holdout results; later changes addressed the original benchmark and time-series contract only.

## Structured-output, security, and readiness

- Requirements/navigation outputs are bounded; initial output cap is 1,000 tokens and bounded retry cap is 2,200. The 13-case case-11 budget exhaustion and holdout case's outline-budget failure were recorded explicitly. They were not converted into partial valid plans.
- In the final eight-case replay, each question started from the Persian source question, extracted/validated requirements, navigated the catalog, resolved persisted fixture company/product IDs, built/validated a typed plan, compiled parameterized SQL, and executed only in the isolated test database using the restricted read-only role.
- No canonical query approvals were changed. No Development or production financial rows were queried. No production routing, runtime activation, Feature 082 ingestion, or historical data was changed.
- Release gates remain unmet: the fixture answer rate is 6/8, the 13-case exact-set rate is 10/13, and the independent seven-case exact-set rate is 4/7. **Controlled Development activation is not ready.**
