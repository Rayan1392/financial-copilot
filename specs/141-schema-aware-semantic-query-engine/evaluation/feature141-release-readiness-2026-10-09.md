# Feature 141 Release Readiness — Persian Question Scorecard (2026-10-09)

## Decision

**NOT PRODUCTION READY.** The measured 50-question live LLM Navigator suite has 23 completions (16/40 development; 7/10 untouched holdout), weak exact measure-set accuracy (22.2% development; 33.3% holdout), and no SQL or final answer generation in those 50 Navigator-only attempts. The separate 15-question live LLM + isolated PostgreSQL suite returned 10 answers: nine have an inspectable rendered result, while one sales-rate answer explicitly leaves its unit unknown. It also has two incomplete cross-company rankings, two rejected comparison operations, and one genuine ambiguity. Dynamic pellet discovery exactly matched the independent fixture company set (precision=recall=exact-set accuracy=1.00), while correctly disclosing incomplete alias coverage. This is fixture evidence, not exhaustive market coverage.

## Cohorts and unconditional metrics

| Cohort | Attempts | Navigator / answer completions | SQL queried | Incorrect numeric answers | Safe refusals / unresolved |
|---|---:|---:|---:|---:|---:|
| Live LLM development Navigator | 40 | 16/40 (40.0%) | 0 | 0 observed (no numeric answers) | 22 failures + 2 blocked, all retained |
| Separate untouched live LLM holdout Navigator | 10 | 7/10 (70.0%) | 0 | 0 observed (no numeric answers) | 3 not completed |
| Live LLM + isolated PostgreSQL fixture | 15 | 10/15 answered | 12 executed | 0 known incorrect numeric answers among answered cases; unit/semantic failures are reported per case | 5 non-success |
| Dynamic producer-discovery query | 1 | 1 structured result with disclosed incomplete coverage | 1 | 0 incorrect company-set membership in fixture | 1 partial-coverage result |

The two 50-question Navigator cohorts are reported separately from SQL-backed tests: they did not execute PostgreSQL and did not generate Persian final answers. Their 0 SQL executions are not successes. No question is dropped from a denominator. The development score includes two requests that stalled without a response row; these are explicitly represented as BLOCKED in the 40-row output, so `failedCaseCount` (22) plus 2 blocked accounts for all 24 non-completions. Model cost was unavailable from the provider.

### Navigator metrics

- Development (40): exact measure-set accuracy 22.2%; measure precision 22.2%; recall 22.2%; domain exact-set accuracy 12.5%; financial-reference exact accuracy 7.7%; operation accuracy 0.0%; period accuracy 7.7%.
- Holdout (10, one pass): exact measure-set accuracy 33.3%; measure precision 33.3%; recall 33.3%; domain exact-set accuracy 30.0%; financial-reference exact accuracy 0.0%; operation accuracy 0.0%; period accuracy 0.0%.
- Tokens / model calls / model latency: dev 317,743 input + 36,305 output / 60 calls / 450,674 ms; holdout 94,766 input + 11,740 output / 20 calls / 154,008 ms. Per-question latency is in captured artifacts.
- Company-set precision / recall / exact-set accuracy on the separate discovery fixture: **1.00 / 1.00 / 1.00** (four independently expected authorized companies); the SQL result is still `Incomplete` because the approved alias map is partial.
- Production/Development generic-query routing remains disabled. Navigator evaluation used the Development model configuration directly and did not access Development financial data. PostgreSQL calls below used the test-owned isolated fixture and restricted role.

## Product metadata audit and reviewer package

The audit used the bounded checked-in dictionary snapshots, not financial databases. It found 4,934 sampled product identity rows, 4,539 provider/product-code keys, 313 provider/external-company scopes, 2,699 exact labels and 2,629 normalized labels. All 4,934 require product review and canonical company resolution. There are 224 repeated exact-label/unit candidate groups, all pending and non-executable. Similarity/repetition was not treated as equivalence. The sole approved equivalence remains `گندله` = `گندله سنگ‌آهن` (`product_iron_ore_pellets`). See [coverage report](../product-catalog-coverage-review-2026-10-09.md) and [224 reviewer decisions](../ProductCatalog/reviewer-decisions/product-equivalence-candidates-v1.csv).

## Requested five-outcome root-cause review

The latest composed 16-record evidence set consists of the 15 live monthly cases plus the separate dynamic producer query. The observed five non-success monthly rows are listed below. The two cross-company rankings fail at PostgreSQL coverage validation after query planning; no incomplete ranking is presented as a complete answer. The two mixed-measure comparisons are rejected before SQL because their algebra is not approved. The industry sales-growth question is a genuine ambiguity: it names neither an industry nor whether growth means month-over-month or year-over-year, so the Navigator correctly asked for clarification before SQL.

| Case | Observed stage/outcome | Root cause | Reusable fix / disposition |
|---|---|---|---|
| `monthly-09-cross-company-product-sales` | SQL executed; PostgreSQL returned `Incomplete`; answer incomplete | The plan observed `ProductMappingCoverageStatus=partial` for producer discovery through the approved pellet aliases. This is missing reviewed product/company identity coverage, not evidence of a broken sales metric | No static producer list or inferred equivalence. Retain safe incomplete status; use reviewer decisions and extend mappings only after approval. |
| `producer-16-total-company-sales-ranking` | SQL executed; PostgreSQL returned `Incomplete`; ranking withheld | The product-derived producer population remains partial because pellet alias coverage is incomplete; the artifact reports company coverage complete within the supplied authorization scope. The ranking cannot claim an exhaustive population | Report partial product coverage; keep company discovery observation-driven. |
| `monthly-12-sales-vs-operating-profit-growth` | Requirements extraction rejected: `requirements_contain_unsupported_financial_value_operation` | Unsupported cross-domain/cross-period comparison algebra; no approved formula/period alignment | Correct fail-closed policy, not a Planner bug. Business/catalog approval would be needed to add an operation. |
| `monthly-13-production-vs-sales-growth` | Requirements extraction rejected: `requirements_contain_unsupported_financial_value_operation` | No approved cross-measure derived operation; wording asks a relationship between unlike production and sales measures | Preserve as unsupported/clarification; do not derive an unapproved metric. The cross-measure wording is semantically ambiguous; there is no SQL. |
| `monthly-11-industry-sales-growth-ranking` | `typed_ambiguity` during requirements/measure selection; no SQL | The question omits the industry and the growth comparison basis (MoM vs YoY); this is a genuine ambiguity, not a timeout or mapping defect | Keep clarification behavior; do not choose a default industry or comparison period. |

An independently verified reusable defect was fixed: compact outline source-candidate pruning removed a candidate from its map but left its ancestor in the included-node set, causing serialization to re-expand the full source-item list and exceed budget. The provider now rebuilds inclusion from the retained candidate set and bounds source-item references. Regression test `HierarchyProvider_BoundsOutlineAndRanksTheExactPersianSourceItemAheadOfSimilarTitles` at 16,000 characters passes. The final Release development run recorded zero outline-budget failures; unknown catalog IDs and unsupported operations remain.

## Dynamic company discovery fixture

- **Exact Persian question:** کدام شرکت‌ها گندله تولید می‌کنند؟
- **Extracted concept and measure:** `product_iron_ore_pellets` via owner approval `owner-confirmed:feature141-product-equivalence:2026-10-09`; measure `metric_monthly_production_quantity`; no company IDs in the question or plan.
- **Plan period:** `observed-production-trailing-12-months-v1`, 2025-10-09 through 2026-10-09.
- **SQL:** queried through the restricted test PostgreSQL role; result `Incomplete` / `population_discovery_product_alias_coverage_incomplete`; SQL and parameters are recorded in the artifact.
- **Independent expected company IDs:** 2a437020-35d6-4d57-b1bd-a209d7af06fb, 5202a885-2afc-4939-a09d-f794c6bf058a, a64bbbd9-7788-4bf0-a8c7-2ed304be5d68, f7a05252-afe0-4ddb-8537-8cc18da7a40b; actual rows: NEWP=62 ton (2026-08-23..2026-09-22; fixture-new-producer-shahrivar-1405); کگل=25 ton (2026-08-23..2026-09-22; fixture-kgel-shahrivar-1405); کگهر=47 ton (2026-08-23..2026-09-22; fixture-kgehr-shahrivar-1405); NOSALES=80 ton (2026-08-23..2026-09-22; fixture-production-only-shahrivar-1405).
- **Fixture metrics:** expected set size=4; actual set size=4; exact match=True; precision=1.00; recall=1.00. Positive production only; zero-production and unauthorized companies were excluded in the companion test assertions. Newly inserted company `NEWP` was discovered without company IDs or a producer registry.
- **Honest coverage:** mapping status `approved-aliases-only`. The response discloses partial coverage; the fixture match does not imply whole-market recall.
- **Persian answer actually returned:**

```text
در گزارش‌های ماهانهٔ واجد شرایط بازهٔ ۲۰۲۵-۱۰-۰۹ تا ۲۰۲۶-۱۰-۰۹، تولید مثبت گندله برای شرکت‌های زیر مشاهده شد: Dynamically discovered producer (NEWP): تولید ثبت‌شدهٔ گندله ۶۲ تن در بازهٔ ۲۰۲۶-۰۸-۲۳ تا ۲۰۲۶-۰۹-۲۲؛ 
منبع: NoavaranCurrentApi، گزارش fixture-new-producer-shahrivar-1405؛ Fixture Kegel (کگل): تولید ثبت‌شدهٔ گندله ۲۵ تن در بازهٔ ۲۰۲۶-۰۸-۲۳ تا ۲۰۲۶-۰۹-۲۲؛ 
منبع: NoavaranCurrentApi، گزارش fixture-kgel-shahrivar-1405؛ Fixture Kogهر (کگهر): تولید ثبت‌شدهٔ گندله ۴۷ تن در بازهٔ ۲۰۲۶-۰۸-۲۳ تا ۲۰۲۶-۰۹-۲۲؛ 
منبع: NoavaranCurrentApi، گزارش fixture-kgehr-shahrivar-1405؛ Positive pellet producer without sales (NOSALES): تولید ثبت‌شدهٔ گندله ۸۰ تن در بازهٔ ۲۰۲۶-۰۸-۲۳ تا ۲۰۲۶-۰۹-۲۲؛ 
منبع: NoavaranCurrentApi، گزارش fixture-production-only-shahrivar-1405. این فهرست جزئی است: پوشش دسترسی به شرکت‌ها یا نگاشت محصول کامل نیست و از آن نمی‌توان نتیجه گرفت که شرکت‌های فهرست‌نشده تولید نداشته‌اند.
```

## All 50 live Persian Navigator attempts

These are actual exact user strings sent through live OpenAI extraction/navigation. Expected/actual in this section are semantic references because this evaluation runner does not execute SQL. No numeric result or final Persian response is inferred. The question text, raw returned selection, failure stage and trace file are recorded for every attempt.

### fa-02 — dev

- **Original Persian question:** کدام محصولات کگل رشد تولید داشته‌اند؟
- **Expected intent and references:** monthly production growth/comparison; domains=monthly-activity; measures=MONTHLY_PRODUCTION_GROWTH_YOY; dimensions=dim_product_identity, dim_monthly_period; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=completed; selectedMeasures=metric_monthly_production_quantity; selectedNodes=dimension-dim_company, dimension-dim_monthly_period, dimension-dim_product_identity, domain-company-industry, domain-monthly-activity, financial-semantic-catalog, shared-dimensions, table-tbl_8646d83cdb350f92, table-tbl_8e759d70dada6bd8, table-tbl_8ecd0432a4292998; operation=DirectValueRetrieval; selectedFinancialReferences={'referenceType': 'MonthlyMeasure', 'catalogId': 'metric_monthly_production_quantity', 'providerName': None, 'statementType': None, 'sourceItemId': None, 'sourceItemCatalogId': None, 'metricCode': 'MONTHLY_PRODUCTION_QUANTITY', 'unitStatus': None, 'queryable': False}; error=—
- **Generated query plan:** measures=metric_monthly_production_quantity; operation=DirectValueRetrieval; period=latest; dimensions=dim_company, dim_monthly_period, dim_product_identity; relationships=rel_31e410fb3ae11956, rel_4e54d40aee8b45e4
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=monthly-activity; measures=MONTHLY_PRODUCTION_GROWTH_YOY; dimensions=dim_product_identity, dim_monthly_period; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=0.0; recall=0.0; f1=0.0.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** FAIL
- **Failure reason / exact stage:** Navigator completed with missing or wrong measure selection; phase=—; code=—.
- **Source provenance:** catalog version/hash in the run record; selected references={'referenceType': 'MonthlyMeasure', 'catalogId': 'metric_monthly_production_quantity', 'providerName': None, 'statementType': None, 'sourceItemId': None, 'sourceItemCatalogId': None, 'metricCode': 'MONTHLY_PRODUCTION_QUANTITY', 'unitStatus': None, 'queryable': False}; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-dev-v1.json) · [captured LLM output](../artifacts/release-readiness-dev-live-v3-final-2026-10-09.jsonl) · [score detail](release-readiness-dev-score-v3-final-2026-10-09.json)

### fa-03 — dev

- **Original Persian question:** روند فروش کگل را نمایش بده.
- **Expected intent and references:** monthly sales; domains=monthly-activity; measures=MONTHLY_SALES; dimensions=dim_monthly_period; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=completed; selectedMeasures=metric_monthly_sales; selectedNodes=dimension-dim_company, dimension-dim_monthly_period, domain-company-industry, domain-monthly-activity, financial-semantic-catalog, shared-dimensions, table-tbl_8646d83cdb350f92, table-tbl_8e759d70dada6bd8, table-tbl_8ecd0432a4292998; operation=DirectValueRetrieval; selectedFinancialReferences={'referenceType': 'MonthlyMeasure', 'catalogId': 'metric_monthly_sales', 'providerName': None, 'statementType': None, 'sourceItemId': None, 'sourceItemCatalogId': None, 'metricCode': 'MONTHLY_SALES', 'unitStatus': None, 'queryable': False}; error=—
- **Generated query plan:** measures=metric_monthly_sales; operation=DirectValueRetrieval; period=—; dimensions=dim_company, dim_monthly_period; relationships=rel_31e410fb3ae11956, rel_4e54d40aee8b45e4
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=monthly-activity; measures=MONTHLY_SALES; dimensions=dim_monthly_period; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=1.0; recall=1.0; f1=1.0.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** PASS (Navigator only)
- **Failure reason / exact stage:** Navigator completed; SQL/response phases were not run; phase=—; code=—.
- **Source provenance:** catalog version/hash in the run record; selected references={'referenceType': 'MonthlyMeasure', 'catalogId': 'metric_monthly_sales', 'providerName': None, 'statementType': None, 'sourceItemId': None, 'sourceItemCatalogId': None, 'metricCode': 'MONTHLY_SALES', 'unitStatus': None, 'queryable': False}; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-dev-v1.json) · [captured LLM output](../artifacts/release-readiness-dev-live-v3-final-2026-10-09.jsonl) · [score detail](release-readiness-dev-score-v3-final-2026-10-09.json)

### fa-04 — dev

- **Original Persian question:** سود خالص فولاد چقدر است؟
- **Expected intent and references:** financial statement/value lookup; domains=financial-statements; measures=NET_PROFIT; dimensions=dim_company; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=failed; selectedMeasures=—; selectedNodes=—; operation=—; selectedFinancialReferences=—; error=requirements_contain_unknown_catalog_id
- **Generated query plan:** measures=—; operation=—; period=—; dimensions=—; relationships=—
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=financial-statements; measures=NET_PROFIT; dimensions=dim_company; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=0.0; recall=0.0; f1=0.0.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** BLOCKED
- **Failure reason / exact stage:** requirements_contain_unknown_catalog_id; phase=requirements_extraction; code=requirements_contain_unknown_catalog_id.
- **Source provenance:** catalog version/hash in the run record; selected references=—; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-dev-v1.json) · [captured LLM output](../artifacts/release-readiness-dev-live-v3-final-2026-10-09.jsonl) · [score detail](release-readiness-dev-score-v3-final-2026-10-09.json)

### fa-05 — dev

- **Original Persian question:** سود خالص تلفیقی فولاد چقدر است؟
- **Expected intent and references:** financial statement/value lookup; domains=financial-statements; measures=NET_PROFIT; dimensions=dim_company; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=failed; selectedMeasures=—; selectedNodes=—; operation=—; selectedFinancialReferences=—; error=requirements_contain_unknown_catalog_id
- **Generated query plan:** measures=—; operation=—; period=—; dimensions=—; relationships=—
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=financial-statements; measures=NET_PROFIT; dimensions=dim_company; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=0.0; recall=0.0; f1=0.0.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** BLOCKED
- **Failure reason / exact stage:** requirements_contain_unknown_catalog_id; phase=requirements_extraction; code=requirements_contain_unknown_catalog_id.
- **Source provenance:** catalog version/hash in the run record; selected references=—; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-dev-v1.json) · [captured LLM output](../artifacts/release-readiness-dev-live-v3-final-2026-10-09.jsonl) · [score detail](release-readiness-dev-score-v3-final-2026-10-09.json)

### fa-06 — dev

- **Original Persian question:** مقایسه سود خالص اصلی و تلفیقی فولاد
- **Expected intent and references:** financial statement/value lookup; domains=financial-statements; measures=NET_PROFIT; dimensions=dim_company; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=failed; selectedMeasures=—; selectedNodes=—; operation=—; selectedFinancialReferences=—; error=requirements_contain_unknown_catalog_id
- **Generated query plan:** measures=—; operation=—; period=—; dimensions=—; relationships=—
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=financial-statements; measures=NET_PROFIT; dimensions=dim_company; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=0.0; recall=0.0; f1=0.0.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** BLOCKED
- **Failure reason / exact stage:** requirements_contain_unknown_catalog_id; phase=requirements_extraction; code=requirements_contain_unknown_catalog_id.
- **Source provenance:** catalog version/hash in the run record; selected references=—; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-dev-v1.json) · [captured LLM output](../artifacts/release-readiness-dev-live-v3-final-2026-10-09.jsonl) · [score detail](release-readiness-dev-score-v3-final-2026-10-09.json)

### fa-07 — dev

- **Original Persian question:** کدام شرکت‌های صنعت فلزات اساسی بیشترین رشد فروش را داشته‌اند؟
- **Expected intent and references:** company/product discovery or industry comparison; domains=company-industry, monthly-activity; measures=MONTHLY_SALES_GROWTH_YOY; dimensions=dim_company, dim_industry, dim_monthly_period, dim_product_identity; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=failed; selectedMeasures=—; selectedNodes=—; operation=—; selectedFinancialReferences=—; error=requirements_contain_unsupported_financial_value_operation
- **Generated query plan:** measures=—; operation=—; period=—; dimensions=—; relationships=—
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=company-industry, monthly-activity; measures=MONTHLY_SALES_GROWTH_YOY; dimensions=dim_company, dim_industry, dim_monthly_period, dim_product_identity; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=0.0; recall=0.0; f1=0.0.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** UNSUPPORTED
- **Failure reason / exact stage:** requirements_contain_unsupported_financial_value_operation; phase=requirements_extraction; code=requirements_contain_unsupported_financial_value_operation.
- **Source provenance:** catalog version/hash in the run record; selected references=—; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-dev-v1.json) · [captured LLM output](../artifacts/release-readiness-dev-live-v3-final-2026-10-09.jsonl) · [score detail](release-readiness-dev-score-v3-final-2026-10-09.json)

### fa-08 — dev

- **Original Persian question:** نسبت P/E شرکت‌های یک صنعت را مقایسه کن.
- **Expected intent and references:** company/product discovery or industry comparison; domains=company-industry, financial-analytics; measures=PE_TTM; dimensions=dim_company, dim_industry; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=completed; selectedMeasures=metric_pe_ttm; selectedNodes=dimension-dim_company, domain-company-industry, domain-financial-analytics, financial-semantic-catalog, shared-dimensions, table-tbl_83be994ebc1a8627, table-tbl_8646d83cdb350f92, table-tbl_ca0643d8942492ab; operation=DirectValueRetrieval; selectedFinancialReferences={'referenceType': 'GovernedMetric', 'catalogId': 'metric_pe_ttm', 'providerName': None, 'statementType': None, 'sourceItemId': None, 'sourceItemCatalogId': None, 'metricCode': 'PE_TTM', 'unitStatus': None, 'queryable': False}; error=—
- **Generated query plan:** measures=metric_pe_ttm; operation=DirectValueRetrieval; period=latest; dimensions=dim_company; relationships=rel_231ce6ca5eb8a012, rel_5cce5a3ef281dec0
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=company-industry, financial-analytics; measures=PE_TTM; dimensions=dim_company, dim_industry; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=1.0; recall=1.0; f1=1.0.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** PASS (Navigator only)
- **Failure reason / exact stage:** Navigator completed; SQL/response phases were not run; phase=—; code=—.
- **Source provenance:** catalog version/hash in the run record; selected references={'referenceType': 'GovernedMetric', 'catalogId': 'metric_pe_ttm', 'providerName': None, 'statementType': None, 'sourceItemId': None, 'sourceItemCatalogId': None, 'metricCode': 'PE_TTM', 'unitStatus': None, 'queryable': False}; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-dev-v1.json) · [captured LLM output](../artifacts/release-readiness-dev-live-v3-final-2026-10-09.jsonl) · [score detail](release-readiness-dev-score-v3-final-2026-10-09.json)

### fa-09 — dev

- **Original Persian question:** قیمت پایانی کگل در ماه گذشته
- **Expected intent and references:** company/product discovery or industry comparison; domains=company-industry, market-data; measures=LATEST_PRICE; dimensions=dim_company; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=completed; selectedMeasures=—; selectedNodes=dimension-dim_company, domain-market-data, financial-semantic-catalog, shared-dimensions; operation=DirectValueRetrieval; selectedFinancialReferences=—; error=—
- **Generated query plan:** measures=—; operation=DirectValueRetrieval; period=latest; dimensions=dim_company; relationships=—
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=company-industry, market-data; measures=LATEST_PRICE; dimensions=dim_company; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=0.0; recall=0.0; f1=0.0.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** FAIL
- **Failure reason / exact stage:** Navigator completed with missing or wrong measure selection; phase=—; code=—.
- **Source provenance:** catalog version/hash in the run record; selected references=—; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-dev-v1.json) · [captured LLM output](../artifacts/release-readiness-dev-live-v3-final-2026-10-09.jsonl) · [score detail](release-readiness-dev-score-v3-final-2026-10-09.json)

### fa-10 — dev

- **Original Persian question:** روند شاخص کل در شش ماه گذشته
- **Expected intent and references:** financial query (per expected catalog references); domains=market-data; measures=index level and change percent; no generic metric ID asserted; dimensions=trading instrument, daily trading period; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=completed; selectedMeasures=—; selectedNodes=domain-market-data, table-tbl_8e1835aff483a0f4; operation=DirectValueRetrieval; selectedFinancialReferences=—; error=—
- **Generated query plan:** measures=—; operation=DirectValueRetrieval; period=latest; dimensions=—; relationships=—
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=market-data; measures=index level and change percent; no generic metric ID asserted; dimensions=trading instrument, daily trading period; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=0.0; recall=0.0; f1=0.0.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** FAIL
- **Failure reason / exact stage:** Navigator completed with missing or wrong measure selection; phase=—; code=—.
- **Source provenance:** catalog version/hash in the run record; selected references=—; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-dev-v1.json) · [captured LLM output](../artifacts/release-readiness-dev-live-v3-final-2026-10-09.jsonl) · [score detail](release-readiness-dev-score-v3-final-2026-10-09.json)

### fa-value-01 — dev

- **Original Persian question:** آخرین سود عملیاتی هر سهم کگل چقدر است؟
- **Expected intent and references:** financial statement/value lookup; domains=financial-statements; measures=—; dimensions=dim_company; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=failed; selectedMeasures=—; selectedNodes=—; operation=—; selectedFinancialReferences=—; error=requirements_contain_unknown_catalog_id
- **Generated query plan:** measures=—; operation=—; period=—; dimensions=—; relationships=—
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=financial-statements; measures=—; dimensions=dim_company; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=—; recall=—; f1=—.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** BLOCKED
- **Failure reason / exact stage:** requirements_contain_unknown_catalog_id; phase=requirements_extraction; code=requirements_contain_unknown_catalog_id.
- **Source provenance:** catalog version/hash in the run record; selected references=—; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-dev-v1.json) · [captured LLM output](../artifacts/release-readiness-dev-live-v3-final-2026-10-09.jsonl) · [score detail](release-readiness-dev-score-v3-final-2026-10-09.json)

### fa-value-02 — dev

- **Original Persian question:** آخرین سود عملیاتی کگل چقدر است؟
- **Expected intent and references:** financial statement/value lookup; domains=financial-statements; measures=—; dimensions=dim_company; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=failed; selectedMeasures=—; selectedNodes=—; operation=—; selectedFinancialReferences=—; error=requirements_contain_unknown_catalog_id
- **Generated query plan:** measures=—; operation=—; period=—; dimensions=—; relationships=—
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=financial-statements; measures=—; dimensions=dim_company; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=—; recall=—; f1=—.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** BLOCKED
- **Failure reason / exact stage:** requirements_contain_unknown_catalog_id; phase=requirements_extraction; code=requirements_contain_unknown_catalog_id.
- **Source provenance:** catalog version/hash in the run record; selected references=—; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-dev-v1.json) · [captured LLM output](../artifacts/release-readiness-dev-live-v3-final-2026-10-09.jsonl) · [score detail](release-readiness-dev-score-v3-final-2026-10-09.json)

### fa-value-03 — dev

- **Original Persian question:** سود خالص هر سهم کگل چقدر است؟
- **Expected intent and references:** financial statement/value lookup; domains=financial-statements; measures=—; dimensions=dim_company; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=failed; selectedMeasures=—; selectedNodes=—; operation=—; selectedFinancialReferences=—; error=requirements_contain_unknown_catalog_id
- **Generated query plan:** measures=—; operation=—; period=—; dimensions=—; relationships=—
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=financial-statements; measures=—; dimensions=dim_company; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=—; recall=—; f1=—.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** BLOCKED
- **Failure reason / exact stage:** requirements_contain_unknown_catalog_id; phase=requirements_extraction; code=requirements_contain_unknown_catalog_id.
- **Source provenance:** catalog version/hash in the run record; selected references=—; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-dev-v1.json) · [captured LLM output](../artifacts/release-readiness-dev-live-v3-final-2026-10-09.jsonl) · [score detail](release-readiness-dev-score-v3-final-2026-10-09.json)

### fa-value-04 — dev

- **Original Persian question:** موجودی نقد کگهر در پایان سال ۱۴۰۴ چقدر بود؟
- **Expected intent and references:** financial statement/value lookup; domains=financial-statements; measures=—; dimensions=dim_company; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=failed; selectedMeasures=—; selectedNodes=—; operation=—; selectedFinancialReferences=—; error=requirements_contain_unsupported_financial_value_operation
- **Generated query plan:** measures=—; operation=—; period=—; dimensions=—; relationships=—
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=financial-statements; measures=—; dimensions=dim_company; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=—; recall=—; f1=—.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** UNSUPPORTED
- **Failure reason / exact stage:** requirements_contain_unsupported_financial_value_operation; phase=requirements_extraction; code=requirements_contain_unsupported_financial_value_operation.
- **Source provenance:** catalog version/hash in the run record; selected references=—; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-dev-v1.json) · [captured LLM output](../artifacts/release-readiness-dev-live-v3-final-2026-10-09.jsonl) · [score detail](release-readiness-dev-score-v3-final-2026-10-09.json)

### fa-value-05 — dev

- **Original Persian question:** هزینه استهلاک فولاد در آخرین صورت سود و زیان چقدر است؟
- **Expected intent and references:** financial statement/value lookup; domains=financial-statements; measures=—; dimensions=dim_company; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=blocked; selectedMeasures=—; selectedNodes=—; operation=—; selectedFinancialReferences=—; error=no_response_row_after_stalled_request
- **Generated query plan:** measures=—; operation=—; period=—; dimensions=—; relationships=—
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=financial-statements; measures=—; dimensions=dim_company; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=—; recall=—; f1=—.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** BLOCKED
- **Failure reason / exact stage:** no_response_row_after_stalled_request; phase=live_llm_request; code=no_response_row_after_stalled_request.
- **Source provenance:** catalog version/hash in the run record; selected references=—; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-dev-v1.json) · [captured LLM output](../artifacts/release-readiness-dev-live-v3-final-2026-10-09.jsonl) · [score detail](release-readiness-dev-score-v3-final-2026-10-09.json)

### fa-value-06 — dev

- **Original Persian question:** سود خالص تلفیقی فولاد در آخرین گزارش چقدر است؟
- **Expected intent and references:** financial statement/value lookup; domains=financial-statements; measures=—; dimensions=dim_company; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=failed; selectedMeasures=—; selectedNodes=—; operation=—; selectedFinancialReferences=—; error=requirements_contain_unknown_catalog_id
- **Generated query plan:** measures=—; operation=—; period=—; dimensions=—; relationships=—
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=financial-statements; measures=—; dimensions=dim_company; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=—; recall=—; f1=—.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** BLOCKED
- **Failure reason / exact stage:** requirements_contain_unknown_catalog_id; phase=requirements_extraction; code=requirements_contain_unknown_catalog_id.
- **Source provenance:** catalog version/hash in the run record; selected references=—; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-dev-v1.json) · [captured LLM output](../artifacts/release-readiness-dev-live-v3-final-2026-10-09.jsonl) · [score detail](release-readiness-dev-score-v3-final-2026-10-09.json)

### fa-value-07 — dev

- **Original Persian question:** رشد سود عملیاتی کگل نسبت به سال قبل چقدر است؟
- **Expected intent and references:** financial statement/value lookup; domains=financial-statements; measures=—; dimensions=dim_company; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=failed; selectedMeasures=—; selectedNodes=—; operation=—; selectedFinancialReferences=—; error=requirements_contain_unsupported_financial_value_operation
- **Generated query plan:** measures=—; operation=—; period=—; dimensions=—; relationships=—
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=financial-statements; measures=—; dimensions=dim_company; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=—; recall=—; f1=—.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** UNSUPPORTED
- **Failure reason / exact stage:** requirements_contain_unsupported_financial_value_operation; phase=requirements_extraction; code=requirements_contain_unsupported_financial_value_operation.
- **Source provenance:** catalog version/hash in the run record; selected references=—; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-dev-v1.json) · [captured LLM output](../artifacts/release-readiness-dev-live-v3-final-2026-10-09.jsonl) · [score detail](release-readiness-dev-score-v3-final-2026-10-09.json)

### fa-value-08 — dev

- **Original Persian question:** حاشیه سود خالص کگهر چقدر است؟
- **Expected intent and references:** financial statement/value lookup; domains=financial-statements; measures=—; dimensions=dim_company; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=completed; selectedMeasures=metric_net_profit_margin; selectedNodes=dimension-dim_company, domain-company-industry, domain-financial-statements, financial-semantic-catalog, shared-dimensions, table-tbl_36036602dbf76362, table-tbl_4f2ef2d8dd59b46a, table-tbl_53d51e26e1ca6929, table-tbl_8646d83cdb350f92, table-tbl_a7c77d65fb2aaea2; operation=DirectValueRetrieval; selectedFinancialReferences={'referenceType': 'GovernedMetric', 'catalogId': 'metric_net_profit_margin', 'providerName': None, 'statementType': None, 'sourceItemId': None, 'sourceItemCatalogId': None, 'metricCode': 'NET_PROFIT_MARGIN', 'unitStatus': None, 'queryable': False}; error=—
- **Generated query plan:** measures=metric_net_profit_margin; operation=DirectValueRetrieval; period=latest; dimensions=dim_company; relationships=rel_2588cf95d01a651a, rel_635b794f3c9a6eec, rel_cd8a77a2717ac71f, rel_e4fa0fe989156533
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=financial-statements; measures=—; dimensions=dim_company; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=—; recall=—; f1=—.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** PASS (Navigator only)
- **Failure reason / exact stage:** Navigator completed; SQL/response phases were not run; phase=—; code=—.
- **Source provenance:** catalog version/hash in the run record; selected references={'referenceType': 'GovernedMetric', 'catalogId': 'metric_net_profit_margin', 'providerName': None, 'statementType': None, 'sourceItemId': None, 'sourceItemCatalogId': None, 'metricCode': 'NET_PROFIT_MARGIN', 'unitStatus': None, 'queryable': False}; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-dev-v1.json) · [captured LLM output](../artifacts/release-readiness-dev-live-v3-final-2026-10-09.jsonl) · [score detail](release-readiness-dev-score-v3-final-2026-10-09.json)

### readiness-dev-01 — dev

- **Original Persian question:** آخرین گزارش جداگانه فولاد چه میزان سود خالص ثبت کرده است؟
- **Expected intent and references:** financial statement/value lookup; domains=financial-statements; measures=NET_PROFIT; dimensions=dim_company; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=failed; selectedMeasures=—; selectedNodes=—; operation=—; selectedFinancialReferences=—; error=requirements_contain_unknown_catalog_id
- **Generated query plan:** measures=—; operation=—; period=—; dimensions=—; relationships=—
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=financial-statements; measures=NET_PROFIT; dimensions=dim_company; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=0.0; recall=0.0; f1=0.0.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** BLOCKED
- **Failure reason / exact stage:** requirements_contain_unknown_catalog_id; phase=requirements_extraction; code=requirements_contain_unknown_catalog_id.
- **Source provenance:** catalog version/hash in the run record; selected references=—; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-dev-v1.json) · [captured LLM output](../artifacts/release-readiness-dev-live-v3-final-2026-10-09.jsonl) · [score detail](release-readiness-dev-score-v3-final-2026-10-09.json)

### readiness-dev-02 — dev

- **Original Persian question:** سود خالص شرکت فولاد در آخرین صورت مالی چقدر گزارش شده؟
- **Expected intent and references:** financial statement/value lookup; domains=financial-statements; measures=NET_PROFIT; dimensions=dim_company; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=failed; selectedMeasures=—; selectedNodes=—; operation=—; selectedFinancialReferences=—; error=requirements_contain_unsupported_financial_value_operation
- **Generated query plan:** measures=—; operation=—; period=—; dimensions=—; relationships=—
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=financial-statements; measures=NET_PROFIT; dimensions=dim_company; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=0.0; recall=0.0; f1=0.0.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** UNSUPPORTED
- **Failure reason / exact stage:** requirements_contain_unsupported_financial_value_operation; phase=requirements_extraction; code=requirements_contain_unsupported_financial_value_operation.
- **Source provenance:** catalog version/hash in the run record; selected references=—; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-dev-v1.json) · [captured LLM output](../artifacts/release-readiness-dev-live-v3-final-2026-10-09.jsonl) · [score detail](release-readiness-dev-score-v3-final-2026-10-09.json)

### readiness-dev-03 — dev

- **Original Persian question:** در گزارش تلفیقی فولاد، رقم سود خالص چقدر است؟
- **Expected intent and references:** financial statement/value lookup; domains=financial-statements; measures=NET_PROFIT; dimensions=dim_company; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=failed; selectedMeasures=—; selectedNodes=—; operation=—; selectedFinancialReferences=—; error=requirements_contain_unknown_catalog_id
- **Generated query plan:** measures=—; operation=—; period=—; dimensions=—; relationships=—
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=financial-statements; measures=NET_PROFIT; dimensions=dim_company; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=0.0; recall=0.0; f1=0.0.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** BLOCKED
- **Failure reason / exact stage:** requirements_contain_unknown_catalog_id; phase=requirements_extraction; code=requirements_contain_unknown_catalog_id.
- **Source provenance:** catalog version/hash in the run record; selected references=—; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-dev-v1.json) · [captured LLM output](../artifacts/release-readiness-dev-live-v3-final-2026-10-09.jsonl) · [score detail](release-readiness-dev-score-v3-final-2026-10-09.json)

### readiness-dev-04 — dev

- **Original Persian question:** سود خالص جداگانه و تلفیقی فولاد را کنار هم مقایسه کن.
- **Expected intent and references:** financial statement/value lookup; domains=financial-statements; measures=NET_PROFIT; dimensions=dim_company; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=failed; selectedMeasures=—; selectedNodes=—; operation=—; selectedFinancialReferences=—; error=requirements_contain_unknown_catalog_id
- **Generated query plan:** measures=—; operation=—; period=—; dimensions=—; relationships=—
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=financial-statements; measures=NET_PROFIT; dimensions=dim_company; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=0.0; recall=0.0; f1=0.0.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** BLOCKED
- **Failure reason / exact stage:** requirements_contain_unknown_catalog_id; phase=requirements_extraction; code=requirements_contain_unknown_catalog_id.
- **Source provenance:** catalog version/hash in the run record; selected references=—; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-dev-v1.json) · [captured LLM output](../artifacts/release-readiness-dev-live-v3-final-2026-10-09.jsonl) · [score detail](release-readiness-dev-score-v3-final-2026-10-09.json)

### readiness-dev-05 — dev

- **Original Persian question:** آخرین مقدار گزارش‌شده سود عملیاتی هر سهم کگل چیست؟
- **Expected intent and references:** financial statement/value lookup; domains=financial-statements; measures=—; dimensions=dim_company; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=failed; selectedMeasures=—; selectedNodes=—; operation=—; selectedFinancialReferences=—; error=requirements_contain_unknown_catalog_id
- **Generated query plan:** measures=—; operation=—; period=—; dimensions=—; relationships=—
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=financial-statements; measures=—; dimensions=dim_company; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=—; recall=—; f1=—.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** BLOCKED
- **Failure reason / exact stage:** requirements_contain_unknown_catalog_id; phase=requirements_extraction; code=requirements_contain_unknown_catalog_id.
- **Source provenance:** catalog version/hash in the run record; selected references=—; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-dev-v1.json) · [captured LLM output](../artifacts/release-readiness-dev-live-v3-final-2026-10-09.jsonl) · [score detail](release-readiness-dev-score-v3-final-2026-10-09.json)

### readiness-dev-06 — dev

- **Original Persian question:** موجودی وجه نقد کگهر در پایان سال ۱۴۰۴ چه مبلغی بود؟
- **Expected intent and references:** financial statement/value lookup; domains=financial-statements; measures=—; dimensions=dim_company; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=failed; selectedMeasures=—; selectedNodes=—; operation=—; selectedFinancialReferences=—; error=requirements_contain_unknown_catalog_id
- **Generated query plan:** measures=—; operation=—; period=—; dimensions=—; relationships=—
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=financial-statements; measures=—; dimensions=dim_company; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=—; recall=—; f1=—.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** BLOCKED
- **Failure reason / exact stage:** requirements_contain_unknown_catalog_id; phase=requirements_extraction; code=requirements_contain_unknown_catalog_id.
- **Source provenance:** catalog version/hash in the run record; selected references=—; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-dev-v1.json) · [captured LLM output](../artifacts/release-readiness-dev-live-v3-final-2026-10-09.jsonl) · [score detail](release-readiness-dev-score-v3-final-2026-10-09.json)

### readiness-dev-07 — dev

- **Original Persian question:** هزینه استهلاک فولاد را از آخرین صورت سود و زیان پیدا کن.
- **Expected intent and references:** financial statement/value lookup; domains=financial-statements; measures=—; dimensions=dim_company; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=blocked; selectedMeasures=—; selectedNodes=—; operation=—; selectedFinancialReferences=—; error=no_response_row_after_stalled_request
- **Generated query plan:** measures=—; operation=—; period=—; dimensions=—; relationships=—
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=financial-statements; measures=—; dimensions=dim_company; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=—; recall=—; f1=—.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** BLOCKED
- **Failure reason / exact stage:** no_response_row_after_stalled_request; phase=live_llm_request; code=no_response_row_after_stalled_request.
- **Source provenance:** catalog version/hash in the run record; selected references=—; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-dev-v1.json) · [captured LLM output](../artifacts/release-readiness-dev-live-v3-final-2026-10-09.jsonl) · [score detail](release-readiness-dev-score-v3-final-2026-10-09.json)

### readiness-dev-08 — dev

- **Original Persian question:** سود عملیاتی هر سهم کگل نسبت به سال قبل چند درصد تغییر کرد؟
- **Expected intent and references:** financial statement/value lookup; domains=financial-statements; measures=—; dimensions=dim_company; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=failed; selectedMeasures=—; selectedNodes=—; operation=—; selectedFinancialReferences=—; error=requirements_contain_unsupported_financial_value_operation
- **Generated query plan:** measures=—; operation=—; period=—; dimensions=—; relationships=—
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=financial-statements; measures=—; dimensions=dim_company; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=—; recall=—; f1=—.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** UNSUPPORTED
- **Failure reason / exact stage:** requirements_contain_unsupported_financial_value_operation; phase=requirements_extraction; code=requirements_contain_unsupported_financial_value_operation.
- **Source provenance:** catalog version/hash in the run record; selected references=—; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-dev-v1.json) · [captured LLM output](../artifacts/release-readiness-dev-live-v3-final-2026-10-09.jsonl) · [score detail](release-readiness-dev-score-v3-final-2026-10-09.json)

### readiness-dev-09 — dev

- **Original Persian question:** حاشیه سود ناخالص کگهر در تازه‌ترین گزارش چند درصد است؟
- **Expected intent and references:** financial statement/value lookup; domains=financial-statements; measures=—; dimensions=dim_company; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=failed; selectedMeasures=—; selectedNodes=—; operation=—; selectedFinancialReferences=—; error=requirements_contain_unsupported_financial_value_operation
- **Generated query plan:** measures=—; operation=—; period=—; dimensions=—; relationships=—
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=financial-statements; measures=—; dimensions=dim_company; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=—; recall=—; f1=—.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** UNSUPPORTED
- **Failure reason / exact stage:** requirements_contain_unsupported_financial_value_operation; phase=requirements_extraction; code=requirements_contain_unsupported_financial_value_operation.
- **Source provenance:** catalog version/hash in the run record; selected references=—; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-dev-v1.json) · [captured LLM output](../artifacts/release-readiness-dev-live-v3-final-2026-10-09.jsonl) · [score detail](release-readiness-dev-score-v3-final-2026-10-09.json)

### readiness-dev-10 — dev

- **Original Persian question:** نسبت قیمت به درآمد شرکت‌های صنعت فولاد را در یک جدول مقایسه کن.
- **Expected intent and references:** company/product discovery or industry comparison; domains=company-industry, financial-analytics; measures=PE_TTM; dimensions=dim_company, dim_industry; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=failed; selectedMeasures=—; selectedNodes=—; operation=—; selectedFinancialReferences=—; error=requirements_contain_unsupported_financial_value_operation
- **Generated query plan:** measures=—; operation=—; period=—; dimensions=—; relationships=—
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=company-industry, financial-analytics; measures=PE_TTM; dimensions=dim_company, dim_industry; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=0.0; recall=0.0; f1=0.0.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** UNSUPPORTED
- **Failure reason / exact stage:** requirements_contain_unsupported_financial_value_operation; phase=requirements_extraction; code=requirements_contain_unsupported_financial_value_operation.
- **Source provenance:** catalog version/hash in the run record; selected references=—; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-dev-v1.json) · [captured LLM output](../artifacts/release-readiness-dev-live-v3-final-2026-10-09.jsonl) · [score detail](release-readiness-dev-score-v3-final-2026-10-09.json)

### readiness-dev-11 — dev

- **Original Persian question:** بیشترین قیمت پایانی کگل در ماه گذشته چه بوده است؟
- **Expected intent and references:** company/product discovery or industry comparison; domains=company-industry, market-data; measures=LATEST_PRICE; dimensions=dim_company; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=failed; selectedMeasures=—; selectedNodes=—; operation=—; selectedFinancialReferences=—; error=requirements_contain_unsupported_financial_value_operation
- **Generated query plan:** measures=—; operation=—; period=—; dimensions=—; relationships=—
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=company-industry, market-data; measures=LATEST_PRICE; dimensions=dim_company; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=0.0; recall=0.0; f1=0.0.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** UNSUPPORTED
- **Failure reason / exact stage:** requirements_contain_unsupported_financial_value_operation; phase=requirements_extraction; code=requirements_contain_unsupported_financial_value_operation.
- **Source provenance:** catalog version/hash in the run record; selected references=—; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-dev-v1.json) · [captured LLM output](../artifacts/release-readiness-dev-live-v3-final-2026-10-09.jsonl) · [score detail](release-readiness-dev-score-v3-final-2026-10-09.json)

### readiness-dev-12 — dev

- **Original Persian question:** در سال اخیر کدام شرکت‌ها بر اساس گزارش مثبت، گندله تولید کرده‌اند؟
- **Expected intent and references:** company/product discovery or industry comparison; domains=company-industry, monthly-activity; measures=MONTHLY_PRODUCTION_QUANTITY; dimensions=dim_company, dim_product_identity, dim_monthly_period; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=completed; selectedMeasures=metric_monthly_production_quantity; selectedNodes=dimension-dim_company, dimension-dim_monthly_period, dimension-dim_product_identity, domain-company-industry, domain-monthly-activity, financial-semantic-catalog, shared-dimensions, table-tbl_8646d83cdb350f92, table-tbl_8e759d70dada6bd8, table-tbl_8ecd0432a4292998; operation=DirectValueRetrieval; selectedFinancialReferences={'referenceType': 'MonthlyMeasure', 'catalogId': 'metric_monthly_production_quantity', 'providerName': None, 'statementType': None, 'sourceItemId': None, 'sourceItemCatalogId': None, 'metricCode': 'MONTHLY_PRODUCTION_QUANTITY', 'unitStatus': None, 'queryable': False}; error=—
- **Generated query plan:** measures=metric_monthly_production_quantity; operation=DirectValueRetrieval; period=latest; dimensions=dim_company, dim_monthly_period, dim_product_identity; relationships=rel_31e410fb3ae11956, rel_4e54d40aee8b45e4
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=company-industry, monthly-activity; measures=MONTHLY_PRODUCTION_QUANTITY; dimensions=dim_company, dim_product_identity, dim_monthly_period; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=1.0; recall=1.0; f1=1.0.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** PASS (Navigator only)
- **Failure reason / exact stage:** Navigator completed; SQL/response phases were not run; phase=—; code=—.
- **Source provenance:** catalog version/hash in the run record; selected references={'referenceType': 'MonthlyMeasure', 'catalogId': 'metric_monthly_production_quantity', 'providerName': None, 'statementType': None, 'sourceItemId': None, 'sourceItemCatalogId': None, 'metricCode': 'MONTHLY_PRODUCTION_QUANTITY', 'unitStatus': None, 'queryable': False}; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-dev-v1.json) · [captured LLM output](../artifacts/release-readiness-dev-live-v3-final-2026-10-09.jsonl) · [score detail](release-readiness-dev-score-v3-final-2026-10-09.json)

### readiness-dev-13 — dev

- **Original Persian question:** شرکت‌های دارای تولید مثبت گندله سنگ‌آهن در شهریور ۱۴۰۵ را پیدا کن.
- **Expected intent and references:** company/product discovery or industry comparison; domains=company-industry, monthly-activity; measures=MONTHLY_PRODUCTION_QUANTITY; dimensions=dim_company, dim_product_identity, dim_monthly_period; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=completed; selectedMeasures=—; selectedNodes=dimension-dim_company, domain-company-industry, financial-semantic-catalog, shared-dimensions; operation=DirectValueRetrieval; selectedFinancialReferences=—; error=—
- **Generated query plan:** measures=—; operation=DirectValueRetrieval; period=explicit-period; dimensions=dim_company; relationships=—
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=company-industry, monthly-activity; measures=MONTHLY_PRODUCTION_QUANTITY; dimensions=dim_company, dim_product_identity, dim_monthly_period; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=0.0; recall=0.0; f1=0.0.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** FAIL
- **Failure reason / exact stage:** Navigator completed with missing or wrong measure selection; phase=—; code=—.
- **Source provenance:** catalog version/hash in the run record; selected references=—; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-dev-v1.json) · [captured LLM output](../artifacts/release-readiness-dev-live-v3-final-2026-10-09.jsonl) · [score detail](release-readiness-dev-score-v3-final-2026-10-09.json)

### readiness-dev-14 — dev

- **Original Persian question:** مقدار تولید گندله کگهر در ماه شهریور ۱۴۰۵ چقدر ثبت شده؟
- **Expected intent and references:** company/product discovery or industry comparison; domains=company-industry, monthly-activity; measures=MONTHLY_PRODUCTION_QUANTITY; dimensions=dim_company, dim_product_identity, dim_monthly_period; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=completed; selectedMeasures=metric_monthly_production_quantity; selectedNodes=dimension-dim_company, dimension-dim_monthly_period, dimension-dim_product_identity, domain-company-industry, domain-monthly-activity, financial-semantic-catalog, shared-dimensions, table-tbl_8646d83cdb350f92, table-tbl_8e759d70dada6bd8, table-tbl_8ecd0432a4292998; operation=DirectValueRetrieval; selectedFinancialReferences={'referenceType': 'MonthlyMeasure', 'catalogId': 'metric_monthly_production_quantity', 'providerName': None, 'statementType': None, 'sourceItemId': None, 'sourceItemCatalogId': None, 'metricCode': 'MONTHLY_PRODUCTION_QUANTITY', 'unitStatus': None, 'queryable': False}; error=—
- **Generated query plan:** measures=metric_monthly_production_quantity; operation=DirectValueRetrieval; period=explicit-period; dimensions=dim_company, dim_monthly_period, dim_product_identity; relationships=rel_31e410fb3ae11956, rel_4e54d40aee8b45e4
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=company-industry, monthly-activity; measures=MONTHLY_PRODUCTION_QUANTITY; dimensions=dim_company, dim_product_identity, dim_monthly_period; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=1.0; recall=1.0; f1=1.0.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** PASS (Navigator only)
- **Failure reason / exact stage:** Navigator completed; SQL/response phases were not run; phase=—; code=—.
- **Source provenance:** catalog version/hash in the run record; selected references={'referenceType': 'MonthlyMeasure', 'catalogId': 'metric_monthly_production_quantity', 'providerName': None, 'statementType': None, 'sourceItemId': None, 'sourceItemCatalogId': None, 'metricCode': 'MONTHLY_PRODUCTION_QUANTITY', 'unitStatus': None, 'queryable': False}; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-dev-v1.json) · [captured LLM output](../artifacts/release-readiness-dev-live-v3-final-2026-10-09.jsonl) · [score detail](release-readiness-dev-score-v3-final-2026-10-09.json)

### readiness-dev-15 — dev

- **Original Persian question:** رشد تولید محصولات کگل در مقایسه با دوره مشابه پارسال چه‌قدر بوده؟
- **Expected intent and references:** monthly production growth/comparison; domains=monthly-activity; measures=MONTHLY_PRODUCTION_GROWTH_YOY; dimensions=dim_product_identity, dim_monthly_period; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=completed; selectedMeasures=metric_monthly_production_quantity; selectedNodes=dimension-dim_company, dimension-dim_monthly_period, dimension-dim_product_identity, domain-company-industry, domain-monthly-activity, financial-semantic-catalog, shared-dimensions, table-tbl_8646d83cdb350f92, table-tbl_8e759d70dada6bd8, table-tbl_8ecd0432a4292998; operation=ApprovedPeriodComparison; selectedFinancialReferences={'referenceType': 'MonthlyMeasure', 'catalogId': 'metric_monthly_production_quantity', 'providerName': None, 'statementType': None, 'sourceItemId': None, 'sourceItemCatalogId': None, 'metricCode': 'MONTHLY_PRODUCTION_QUANTITY', 'unitStatus': None, 'queryable': False}; error=—
- **Generated query plan:** measures=metric_monthly_production_quantity; operation=ApprovedPeriodComparison; period=latest; dimensions=dim_company, dim_monthly_period, dim_product_identity; relationships=rel_31e410fb3ae11956, rel_4e54d40aee8b45e4
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=monthly-activity; measures=MONTHLY_PRODUCTION_GROWTH_YOY; dimensions=dim_product_identity, dim_monthly_period; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=0.0; recall=0.0; f1=0.0.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** FAIL
- **Failure reason / exact stage:** Navigator completed with missing or wrong measure selection; phase=—; code=—.
- **Source provenance:** catalog version/hash in the run record; selected references={'referenceType': 'MonthlyMeasure', 'catalogId': 'metric_monthly_production_quantity', 'providerName': None, 'statementType': None, 'sourceItemId': None, 'sourceItemCatalogId': None, 'metricCode': 'MONTHLY_PRODUCTION_QUANTITY', 'unitStatus': None, 'queryable': False}; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-dev-v1.json) · [captured LLM output](../artifacts/release-readiness-dev-live-v3-final-2026-10-09.jsonl) · [score detail](release-readiness-dev-score-v3-final-2026-10-09.json)

### readiness-dev-16 — dev

- **Original Persian question:** مبالغ فروش ماهانه کگل را برای شش گزارش آخر به ترتیب زمانی بیاور.
- **Expected intent and references:** monthly sales; domains=monthly-activity; measures=MONTHLY_SALES; dimensions=dim_monthly_period; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=completed; selectedMeasures=metric_monthly_sales; selectedNodes=dimension-dim_company, dimension-dim_monthly_period, domain-company-industry, domain-monthly-activity, financial-semantic-catalog, shared-dimensions, table-tbl_8646d83cdb350f92, table-tbl_8e759d70dada6bd8, table-tbl_8ecd0432a4292998; operation=DirectValueRetrieval; selectedFinancialReferences={'referenceType': 'MonthlyMeasure', 'catalogId': 'metric_monthly_sales', 'providerName': None, 'statementType': None, 'sourceItemId': None, 'sourceItemCatalogId': None, 'metricCode': 'MONTHLY_SALES', 'unitStatus': None, 'queryable': False}; error=—
- **Generated query plan:** measures=metric_monthly_sales; operation=DirectValueRetrieval; period=latest; dimensions=dim_company, dim_monthly_period; relationships=rel_31e410fb3ae11956, rel_4e54d40aee8b45e4
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=monthly-activity; measures=MONTHLY_SALES; dimensions=dim_monthly_period; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=1.0; recall=1.0; f1=1.0.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** PASS (Navigator only)
- **Failure reason / exact stage:** Navigator completed; SQL/response phases were not run; phase=—; code=—.
- **Source provenance:** catalog version/hash in the run record; selected references={'referenceType': 'MonthlyMeasure', 'catalogId': 'metric_monthly_sales', 'providerName': None, 'statementType': None, 'sourceItemId': None, 'sourceItemCatalogId': None, 'metricCode': 'MONTHLY_SALES', 'unitStatus': None, 'queryable': False}; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-dev-v1.json) · [captured LLM output](../artifacts/release-readiness-dev-live-v3-final-2026-10-09.jsonl) · [score detail](release-readiness-dev-score-v3-final-2026-10-09.json)

### readiness-dev-17 — dev

- **Original Persian question:** فروش ریالی کگل در شهریور ۱۴۰۵ به چه میزان رسید؟
- **Expected intent and references:** monthly sales; domains=monthly-activity; measures=MONTHLY_SALES; dimensions=dim_monthly_period; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=completed; selectedMeasures=metric_monthly_sales; selectedNodes=dimension-dim_company, dimension-dim_monthly_period, domain-company-industry, domain-monthly-activity, financial-semantic-catalog, shared-dimensions, table-tbl_8646d83cdb350f92, table-tbl_8e759d70dada6bd8, table-tbl_8ecd0432a4292998; operation=DirectValueRetrieval; selectedFinancialReferences={'referenceType': 'MonthlyMeasure', 'catalogId': 'metric_monthly_sales', 'providerName': None, 'statementType': None, 'sourceItemId': None, 'sourceItemCatalogId': None, 'metricCode': 'MONTHLY_SALES', 'unitStatus': None, 'queryable': False}; error=—
- **Generated query plan:** measures=metric_monthly_sales; operation=DirectValueRetrieval; period=explicit-period; dimensions=dim_company, dim_monthly_period; relationships=rel_31e410fb3ae11956, rel_4e54d40aee8b45e4
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=monthly-activity; measures=MONTHLY_SALES; dimensions=dim_monthly_period; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=1.0; recall=1.0; f1=1.0.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** PASS (Navigator only)
- **Failure reason / exact stage:** Navigator completed; SQL/response phases were not run; phase=—; code=—.
- **Source provenance:** catalog version/hash in the run record; selected references={'referenceType': 'MonthlyMeasure', 'catalogId': 'metric_monthly_sales', 'providerName': None, 'statementType': None, 'sourceItemId': None, 'sourceItemCatalogId': None, 'metricCode': 'MONTHLY_SALES', 'unitStatus': None, 'queryable': False}; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-dev-v1.json) · [captured LLM output](../artifacts/release-readiness-dev-live-v3-final-2026-10-09.jsonl) · [score detail](release-readiness-dev-score-v3-final-2026-10-09.json)

### readiness-dev-18 — dev

- **Original Persian question:** در آخرین گزارش، مقدار فروش گندله کگهر چند تن بوده است؟
- **Expected intent and references:** company/product discovery or industry comparison; domains=company-industry, monthly-activity; measures=MONTHLY_PRODUCTION_QUANTITY; dimensions=dim_company, dim_product_identity, dim_monthly_period; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=completed; selectedMeasures=metric_monthly_sales_quantity; selectedNodes=dimension-dim_company, dimension-dim_monthly_period, dimension-dim_product_identity, domain-company-industry, domain-monthly-activity, financial-semantic-catalog, shared-dimensions, table-tbl_8646d83cdb350f92, table-tbl_8e759d70dada6bd8, table-tbl_8ecd0432a4292998; operation=DirectValueRetrieval; selectedFinancialReferences={'referenceType': 'MonthlyMeasure', 'catalogId': 'metric_monthly_sales_quantity', 'providerName': None, 'statementType': None, 'sourceItemId': None, 'sourceItemCatalogId': None, 'metricCode': 'MONTHLY_SALES_QUANTITY', 'unitStatus': None, 'queryable': False}; error=—
- **Generated query plan:** measures=metric_monthly_sales_quantity; operation=DirectValueRetrieval; period=latest; dimensions=dim_company, dim_monthly_period, dim_product_identity; relationships=rel_31e410fb3ae11956, rel_4e54d40aee8b45e4
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=company-industry, monthly-activity; measures=MONTHLY_PRODUCTION_QUANTITY; dimensions=dim_company, dim_product_identity, dim_monthly_period; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=0.0; recall=0.0; f1=0.0.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** FAIL
- **Failure reason / exact stage:** Navigator completed with missing or wrong measure selection; phase=—; code=—.
- **Source provenance:** catalog version/hash in the run record; selected references={'referenceType': 'MonthlyMeasure', 'catalogId': 'metric_monthly_sales_quantity', 'providerName': None, 'statementType': None, 'sourceItemId': None, 'sourceItemCatalogId': None, 'metricCode': 'MONTHLY_SALES_QUANTITY', 'unitStatus': None, 'queryable': False}; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-dev-v1.json) · [captured LLM output](../artifacts/release-readiness-dev-live-v3-final-2026-10-09.jsonl) · [score detail](release-readiness-dev-score-v3-final-2026-10-09.json)

### readiness-dev-19 — dev

- **Original Persian question:** کدام شرکت‌های صنعت فولاد رشد فروش ماهانه بیشتری داشته‌اند؟
- **Expected intent and references:** company/product discovery or industry comparison; domains=company-industry, monthly-activity; measures=MONTHLY_SALES_GROWTH_YOY; dimensions=dim_company, dim_industry, dim_monthly_period, dim_product_identity; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=failed; selectedMeasures=—; selectedNodes=—; operation=—; selectedFinancialReferences=—; error=TaskCanceledException
- **Generated query plan:** measures=—; operation=—; period=—; dimensions=—; relationships=—
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=company-industry, monthly-activity; measures=MONTHLY_SALES_GROWTH_YOY; dimensions=dim_company, dim_industry, dim_monthly_period, dim_product_identity; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=0.0; recall=0.0; f1=0.0.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** BLOCKED
- **Failure reason / exact stage:** TaskCanceledException; phase=requirements_extraction; code=TaskCanceledException.
- **Source provenance:** catalog version/hash in the run record; selected references=—; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-dev-v1.json) · [captured LLM output](../artifacts/release-readiness-dev-live-v3-final-2026-10-09.jsonl) · [score detail](release-readiness-dev-score-v3-final-2026-10-09.json)

### readiness-dev-20 — dev

- **Original Persian question:** شرکت‌های یک صنعت را با رشد فروش ماهانه مرتب کن.
- **Expected intent and references:** company/product discovery or industry comparison; domains=company-industry, monthly-activity; measures=MONTHLY_SALES_GROWTH_YOY; dimensions=dim_company, dim_industry, dim_monthly_period, dim_product_identity; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=completed; selectedMeasures=—; selectedNodes=—; operation=—; selectedFinancialReferences=—; error=—
- **Generated query plan:** measures=—; operation=—; period=—; dimensions=—; relationships=—
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=company-industry, monthly-activity; measures=MONTHLY_SALES_GROWTH_YOY; dimensions=dim_company, dim_industry, dim_monthly_period, dim_product_identity; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=0.0; recall=0.0; f1=0.0.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** FAIL
- **Failure reason / exact stage:** Navigator completed with missing or wrong measure selection; phase=—; code=—.
- **Source provenance:** catalog version/hash in the run record; selected references=—; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-dev-v1.json) · [captured LLM output](../artifacts/release-readiness-dev-live-v3-final-2026-10-09.jsonl) · [score detail](release-readiness-dev-score-v3-final-2026-10-09.json)

### readiness-dev-21 — dev

- **Original Persian question:** کدام شرکت‌های صنعت معدن در شهریور گندله تولید مثبت داشته‌اند؟
- **Expected intent and references:** company/product discovery or industry comparison; domains=company-industry, monthly-activity; measures=MONTHLY_PRODUCTION_QUANTITY; dimensions=dim_company, dim_product_identity, dim_monthly_period; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=completed; selectedMeasures=—; selectedNodes=dimension-dim_company, dimension-dim_industry, domain-company-industry, financial-semantic-catalog, shared-dimensions, table-tbl_0092e8a0b8fd0d23; operation=DirectValueRetrieval; selectedFinancialReferences=—; error=—
- **Generated query plan:** measures=—; operation=DirectValueRetrieval; period=explicit-period; dimensions=dim_company, dim_industry; relationships=—
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=company-industry, monthly-activity; measures=MONTHLY_PRODUCTION_QUANTITY; dimensions=dim_company, dim_product_identity, dim_monthly_period; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=0.0; recall=0.0; f1=0.0.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** FAIL
- **Failure reason / exact stage:** Navigator completed with missing or wrong measure selection; phase=—; code=—.
- **Source provenance:** catalog version/hash in the run record; selected references=—; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-dev-v1.json) · [captured LLM output](../artifacts/release-readiness-dev-live-v3-final-2026-10-09.jsonl) · [score detail](release-readiness-dev-score-v3-final-2026-10-09.json)

### readiness-dev-22 — dev

- **Original Persian question:** شرکت‌های تولیدکننده گندله را از نظر فروش مقایسه کن؛ منظورم فروش کل شرکت است یا خود محصول؟
- **Expected intent and references:** company/product discovery or industry comparison; domains=company-industry, monthly-activity; measures=MONTHLY_SALES_GROWTH_YOY; dimensions=dim_company, dim_industry, dim_monthly_period, dim_product_identity; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=completed; selectedMeasures=—; selectedNodes=—; operation=—; selectedFinancialReferences=—; error=—
- **Generated query plan:** measures=—; operation=—; period=—; dimensions=—; relationships=—
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=company-industry, monthly-activity; measures=MONTHLY_SALES_GROWTH_YOY; dimensions=dim_company, dim_industry, dim_monthly_period, dim_product_identity; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=0.0; recall=0.0; f1=0.0.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** FAIL
- **Failure reason / exact stage:** Navigator completed with missing or wrong measure selection; phase=—; code=—.
- **Source provenance:** catalog version/hash in the run record; selected references=—; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-dev-v1.json) · [captured LLM output](../artifacts/release-readiness-dev-live-v3-final-2026-10-09.jsonl) · [score detail](release-readiness-dev-score-v3-final-2026-10-09.json)

### readiness-dev-23 — dev

- **Original Persian question:** آیا رشد تولید کگهر با رشد فروش آن هم‌جهت بوده است؟
- **Expected intent and references:** company/product discovery or industry comparison; domains=company-industry, monthly-activity; measures=MONTHLY_SALES_GROWTH_YOY; dimensions=dim_company, dim_industry, dim_monthly_period, dim_product_identity; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=failed; selectedMeasures=—; selectedNodes=—; operation=—; selectedFinancialReferences=—; error=requirements_contain_unsupported_financial_value_operation
- **Generated query plan:** measures=—; operation=—; period=—; dimensions=—; relationships=—
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=company-industry, monthly-activity; measures=MONTHLY_SALES_GROWTH_YOY; dimensions=dim_company, dim_industry, dim_monthly_period, dim_product_identity; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=0.0; recall=0.0; f1=0.0.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** UNSUPPORTED
- **Failure reason / exact stage:** requirements_contain_unsupported_financial_value_operation; phase=requirements_extraction; code=requirements_contain_unsupported_financial_value_operation.
- **Source provenance:** catalog version/hash in the run record; selected references=—; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-dev-v1.json) · [captured LLM output](../artifacts/release-readiness-dev-live-v3-final-2026-10-09.jsonl) · [score detail](release-readiness-dev-score-v3-final-2026-10-09.json)

### readiness-holdout-v2-01 — holdout

- **Original Persian question:** کدام شرکت‌ها در گزارش شهریور ۱۴۰۵ تولید مثبت گندله سنگ‌آهن داشته‌اند؟
- **Expected intent and references:** company/product discovery or industry comparison; domains=company-industry, monthly-activity; measures=MONTHLY_PRODUCTION_QUANTITY; dimensions=dim_company, dim_product_identity, dim_monthly_period; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=completed; selectedMeasures=—; selectedNodes=—; operation=—; selectedFinancialReferences=—; error=—
- **Generated query plan:** measures=—; operation=—; period=—; dimensions=—; relationships=—
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=company-industry, monthly-activity; measures=MONTHLY_PRODUCTION_QUANTITY; dimensions=dim_company, dim_product_identity, dim_monthly_period; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=0.0; recall=0.0; f1=0.0.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** FAIL
- **Failure reason / exact stage:** Navigator completed with missing or wrong measure selection; phase=—; code=—.
- **Source provenance:** catalog version/hash in the run record; selected references=—; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-holdout-v2.json) · [captured LLM output](../artifacts/release-readiness-holdout-v2-live-2026-10-09.jsonl) · [score detail](release-readiness-holdout-v2-score-2026-10-09.json)

### readiness-holdout-v2-02 — holdout

- **Original Persian question:** در صنعت معدن، کدام شرکت‌ها در آخرین گزارش ماهانه گندله تولید کرده‌اند؟
- **Expected intent and references:** company/product discovery or industry comparison; domains=company-industry, monthly-activity; measures=MONTHLY_PRODUCTION_QUANTITY; dimensions=dim_company, dim_product_identity, dim_monthly_period; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=completed; selectedMeasures=metric_monthly_production_quantity; selectedNodes=dimension-dim_company, dimension-dim_industry, dimension-dim_monthly_period, dimension-dim_product_identity, domain-company-industry, domain-monthly-activity, financial-semantic-catalog, shared-dimensions, table-tbl_8646d83cdb350f92, table-tbl_8e759d70dada6bd8, table-tbl_8ecd0432a4292998; operation=DirectValueRetrieval; selectedFinancialReferences={'referenceType': 'MonthlyMeasure', 'catalogId': 'metric_monthly_production_quantity', 'providerName': None, 'statementType': None, 'sourceItemId': None, 'sourceItemCatalogId': None, 'metricCode': 'MONTHLY_PRODUCTION_QUANTITY', 'unitStatus': None, 'queryable': False}; error=—
- **Generated query plan:** measures=metric_monthly_production_quantity; operation=DirectValueRetrieval; period=latest; dimensions=dim_company, dim_industry, dim_monthly_period, dim_product_identity; relationships=rel_31e410fb3ae11956, rel_4e54d40aee8b45e4
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=company-industry, monthly-activity; measures=MONTHLY_PRODUCTION_QUANTITY; dimensions=dim_company, dim_product_identity, dim_monthly_period; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=1.0; recall=1.0; f1=1.0.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** PASS (Navigator only)
- **Failure reason / exact stage:** Navigator completed; SQL/response phases were not run; phase=—; code=—.
- **Source provenance:** catalog version/hash in the run record; selected references={'referenceType': 'MonthlyMeasure', 'catalogId': 'metric_monthly_production_quantity', 'providerName': None, 'statementType': None, 'sourceItemId': None, 'sourceItemCatalogId': None, 'metricCode': 'MONTHLY_PRODUCTION_QUANTITY', 'unitStatus': None, 'queryable': False}; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-holdout-v2.json) · [captured LLM output](../artifacts/release-readiness-holdout-v2-live-2026-10-09.jsonl) · [score detail](release-readiness-holdout-v2-score-2026-10-09.json)

### readiness-holdout-v2-03 — holdout

- **Original Persian question:** کدام تولیدکنندگان در آخرین گزارش، بیش از ۳۰ تن گندله فروخته‌اند؟
- **Expected intent and references:** company/product discovery or industry comparison; domains=company-industry, monthly-activity; measures=MONTHLY_SALES_GROWTH_YOY; dimensions=dim_company, dim_industry, dim_monthly_period, dim_product_identity; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=completed; selectedMeasures=metric_monthly_sales_quantity; selectedNodes=dimension-dim_company, dimension-dim_monthly_period, dimension-dim_product_identity, domain-company-industry, domain-monthly-activity, financial-semantic-catalog, shared-dimensions, table-tbl_8646d83cdb350f92, table-tbl_8e759d70dada6bd8, table-tbl_8ecd0432a4292998; operation=DirectValueRetrieval; selectedFinancialReferences={'referenceType': 'MonthlyMeasure', 'catalogId': 'metric_monthly_sales_quantity', 'providerName': None, 'statementType': None, 'sourceItemId': None, 'sourceItemCatalogId': None, 'metricCode': 'MONTHLY_SALES_QUANTITY', 'unitStatus': None, 'queryable': False}; error=—
- **Generated query plan:** measures=metric_monthly_sales_quantity; operation=DirectValueRetrieval; period=latest; dimensions=dim_company, dim_monthly_period, dim_product_identity; relationships=rel_31e410fb3ae11956, rel_4e54d40aee8b45e4
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=company-industry, monthly-activity; measures=MONTHLY_SALES_GROWTH_YOY; dimensions=dim_company, dim_industry, dim_monthly_period, dim_product_identity; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=0.0; recall=0.0; f1=0.0.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** FAIL
- **Failure reason / exact stage:** Navigator completed with missing or wrong measure selection; phase=—; code=—.
- **Source provenance:** catalog version/hash in the run record; selected references={'referenceType': 'MonthlyMeasure', 'catalogId': 'metric_monthly_sales_quantity', 'providerName': None, 'statementType': None, 'sourceItemId': None, 'sourceItemCatalogId': None, 'metricCode': 'MONTHLY_SALES_QUANTITY', 'unitStatus': None, 'queryable': False}; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-holdout-v2.json) · [captured LLM output](../artifacts/release-readiness-holdout-v2-live-2026-10-09.jsonl) · [score detail](release-readiness-holdout-v2-score-2026-10-09.json)

### readiness-holdout-v2-04 — holdout

- **Original Persian question:** تولیدکنندگان گندله را بر پایه فروش کل شرکت در شهریور ۱۴۰۵ از بیشترین به کمترین مرتب کن.
- **Expected intent and references:** company/product discovery or industry comparison; domains=company-industry, monthly-activity; measures=MONTHLY_SALES_GROWTH_YOY; dimensions=dim_company, dim_industry, dim_monthly_period, dim_product_identity; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=completed; selectedMeasures=metric_monthly_sales; selectedNodes=dimension-dim_company, dimension-dim_monthly_period, dimension-dim_product_identity, domain-company-industry, domain-monthly-activity, financial-semantic-catalog, shared-dimensions, table-tbl_8646d83cdb350f92, table-tbl_8e759d70dada6bd8, table-tbl_8ecd0432a4292998; operation=DirectValueRetrieval; selectedFinancialReferences={'referenceType': 'MonthlyMeasure', 'catalogId': 'metric_monthly_sales', 'providerName': None, 'statementType': None, 'sourceItemId': None, 'sourceItemCatalogId': None, 'metricCode': 'MONTHLY_SALES', 'unitStatus': None, 'queryable': False}; error=—
- **Generated query plan:** measures=metric_monthly_sales; operation=DirectValueRetrieval; period=explicit-period; dimensions=dim_company, dim_monthly_period, dim_product_identity; relationships=rel_31e410fb3ae11956, rel_4e54d40aee8b45e4
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=company-industry, monthly-activity; measures=MONTHLY_SALES_GROWTH_YOY; dimensions=dim_company, dim_industry, dim_monthly_period, dim_product_identity; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=0.0; recall=0.0; f1=0.0.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** FAIL
- **Failure reason / exact stage:** Navigator completed with missing or wrong measure selection; phase=—; code=—.
- **Source provenance:** catalog version/hash in the run record; selected references={'referenceType': 'MonthlyMeasure', 'catalogId': 'metric_monthly_sales', 'providerName': None, 'statementType': None, 'sourceItemId': None, 'sourceItemCatalogId': None, 'metricCode': 'MONTHLY_SALES', 'unitStatus': None, 'queryable': False}; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-holdout-v2.json) · [captured LLM output](../artifacts/release-readiness-holdout-v2-live-2026-10-09.jsonl) · [score detail](release-readiness-holdout-v2-score-2026-10-09.json)

### readiness-holdout-v2-05 — holdout

- **Original Persian question:** کدام محصولات کگهر در سه ماه اخیر بیشترین رشد تولید ماهانه را داشته‌اند؟
- **Expected intent and references:** financial statement/value lookup; domains=financial-statements; measures=NET_PROFIT; dimensions=dim_company; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=completed; selectedMeasures=metric_monthly_production_quantity; selectedNodes=dimension-dim_company, dimension-dim_monthly_period, dimension-dim_product_identity, domain-company-industry, domain-monthly-activity, financial-semantic-catalog, shared-dimensions, table-tbl_8646d83cdb350f92, table-tbl_8e759d70dada6bd8, table-tbl_8ecd0432a4292998; operation=ApprovedPeriodComparison; selectedFinancialReferences={'referenceType': 'MonthlyMeasure', 'catalogId': 'metric_monthly_production_quantity', 'providerName': None, 'statementType': None, 'sourceItemId': None, 'sourceItemCatalogId': None, 'metricCode': 'MONTHLY_PRODUCTION_QUANTITY', 'unitStatus': None, 'queryable': False}; error=—
- **Generated query plan:** measures=metric_monthly_production_quantity; operation=ApprovedPeriodComparison; period=latest; dimensions=dim_company, dim_monthly_period, dim_product_identity; relationships=rel_31e410fb3ae11956, rel_4e54d40aee8b45e4
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=financial-statements; measures=NET_PROFIT; dimensions=dim_company; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=0.0; recall=0.0; f1=0.0.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** FAIL
- **Failure reason / exact stage:** Navigator completed with missing or wrong measure selection; phase=—; code=—.
- **Source provenance:** catalog version/hash in the run record; selected references={'referenceType': 'MonthlyMeasure', 'catalogId': 'metric_monthly_production_quantity', 'providerName': None, 'statementType': None, 'sourceItemId': None, 'sourceItemCatalogId': None, 'metricCode': 'MONTHLY_PRODUCTION_QUANTITY', 'unitStatus': None, 'queryable': False}; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-holdout-v2.json) · [captured LLM output](../artifacts/release-readiness-holdout-v2-live-2026-10-09.jsonl) · [score detail](release-readiness-holdout-v2-score-2026-10-09.json)

### readiness-holdout-v2-06 — holdout

- **Original Persian question:** تولید کگل در شهریور ۱۴۰۵ نسبت به ماه مشابه سال قبل چند درصد تغییر کرد؟
- **Expected intent and references:** monthly production growth/comparison; domains=monthly-activity; measures=MONTHLY_PRODUCTION_GROWTH_YOY; dimensions=dim_product_identity, dim_monthly_period; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=completed; selectedMeasures=metric_monthly_production_growth_yoy; selectedNodes=dimension-dim_company, dimension-dim_monthly_period, domain-company-industry, domain-monthly-activity, financial-semantic-catalog, shared-dimensions, table-tbl_4f3dd93b63d1766f, table-tbl_8646d83cdb350f92, table-tbl_8e759d70dada6bd8, table-tbl_8ecd0432a4292998; operation=ApprovedPeriodComparison; selectedFinancialReferences={'referenceType': 'GovernedMetric', 'catalogId': 'metric_monthly_production_growth_yoy', 'providerName': None, 'statementType': None, 'sourceItemId': None, 'sourceItemCatalogId': None, 'metricCode': 'MONTHLY_PRODUCTION_GROWTH_YOY', 'unitStatus': None, 'queryable': False}; error=—
- **Generated query plan:** measures=metric_monthly_production_growth_yoy; operation=ApprovedPeriodComparison; period=explicit-period; dimensions=dim_company, dim_monthly_period; relationships=rel_31e410fb3ae11956, rel_4e54d40aee8b45e4
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=monthly-activity; measures=MONTHLY_PRODUCTION_GROWTH_YOY; dimensions=dim_product_identity, dim_monthly_period; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=1.0; recall=1.0; f1=1.0.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** PASS (Navigator only)
- **Failure reason / exact stage:** Navigator completed; SQL/response phases were not run; phase=—; code=—.
- **Source provenance:** catalog version/hash in the run record; selected references={'referenceType': 'GovernedMetric', 'catalogId': 'metric_monthly_production_growth_yoy', 'providerName': None, 'statementType': None, 'sourceItemId': None, 'sourceItemCatalogId': None, 'metricCode': 'MONTHLY_PRODUCTION_GROWTH_YOY', 'unitStatus': None, 'queryable': False}; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-holdout-v2.json) · [captured LLM output](../artifacts/release-readiness-holdout-v2-live-2026-10-09.jsonl) · [score detail](release-readiness-holdout-v2-score-2026-10-09.json)

### readiness-holdout-v2-07 — holdout

- **Original Persian question:** فروش ریالی کگل را برای شش گزارش ماهانه اخیر به ترتیب زمان نشان بده.
- **Expected intent and references:** monthly sales; domains=monthly-activity; measures=MONTHLY_SALES; dimensions=dim_monthly_period; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=completed; selectedMeasures=metric_monthly_sales; selectedNodes=dimension-dim_company, dimension-dim_monthly_period, domain-company-industry, domain-monthly-activity, financial-semantic-catalog, shared-dimensions, table-tbl_8646d83cdb350f92, table-tbl_8e759d70dada6bd8, table-tbl_8ecd0432a4292998; operation=DirectValueRetrieval; selectedFinancialReferences={'referenceType': 'MonthlyMeasure', 'catalogId': 'metric_monthly_sales', 'providerName': None, 'statementType': None, 'sourceItemId': None, 'sourceItemCatalogId': None, 'metricCode': 'MONTHLY_SALES', 'unitStatus': None, 'queryable': False}; error=—
- **Generated query plan:** measures=metric_monthly_sales; operation=DirectValueRetrieval; period=latest; dimensions=dim_company, dim_monthly_period; relationships=rel_31e410fb3ae11956, rel_4e54d40aee8b45e4
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=monthly-activity; measures=MONTHLY_SALES; dimensions=dim_monthly_period; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=1.0; recall=1.0; f1=1.0.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** PASS (Navigator only)
- **Failure reason / exact stage:** Navigator completed; SQL/response phases were not run; phase=—; code=—.
- **Source provenance:** catalog version/hash in the run record; selected references={'referenceType': 'MonthlyMeasure', 'catalogId': 'metric_monthly_sales', 'providerName': None, 'statementType': None, 'sourceItemId': None, 'sourceItemCatalogId': None, 'metricCode': 'MONTHLY_SALES', 'unitStatus': None, 'queryable': False}; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-holdout-v2.json) · [captured LLM output](../artifacts/release-readiness-holdout-v2-live-2026-10-09.jsonl) · [score detail](release-readiness-holdout-v2-score-2026-10-09.json)

### readiness-holdout-v2-08 — holdout

- **Original Persian question:** فروش ریالی کگل در شهریور ۱۴۰۵ چه مبلغی ثبت شد؟
- **Expected intent and references:** monthly sales; domains=monthly-activity; measures=MONTHLY_SALES; dimensions=dim_monthly_period; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=failed; selectedMeasures=—; selectedNodes=—; operation=—; selectedFinancialReferences=—; error=requirements_contain_unsupported_financial_value_operation
- **Generated query plan:** measures=—; operation=—; period=—; dimensions=—; relationships=—
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=monthly-activity; measures=MONTHLY_SALES; dimensions=dim_monthly_period; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=0.0; recall=0.0; f1=0.0.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** UNSUPPORTED
- **Failure reason / exact stage:** requirements_contain_unsupported_financial_value_operation; phase=requirements_extraction; code=requirements_contain_unsupported_financial_value_operation.
- **Source provenance:** catalog version/hash in the run record; selected references=—; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-holdout-v2.json) · [captured LLM output](../artifacts/release-readiness-holdout-v2-live-2026-10-09.jsonl) · [score detail](release-readiness-holdout-v2-score-2026-10-09.json)

### readiness-holdout-v2-09 — holdout

- **Original Persian question:** مانده وجه نقد کگهر در پایان سال مالی ۱۴۰۴ چقدر گزارش شده است؟
- **Expected intent and references:** financial statement/value lookup; domains=financial-statements; measures=—; dimensions=dim_company; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=failed; selectedMeasures=—; selectedNodes=—; operation=—; selectedFinancialReferences=—; error=requirements_contain_unsupported_financial_value_operation
- **Generated query plan:** measures=—; operation=—; period=—; dimensions=—; relationships=—
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=financial-statements; measures=—; dimensions=dim_company; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=—; recall=—; f1=—.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** UNSUPPORTED
- **Failure reason / exact stage:** requirements_contain_unsupported_financial_value_operation; phase=requirements_extraction; code=requirements_contain_unsupported_financial_value_operation.
- **Source provenance:** catalog version/hash in the run record; selected references=—; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-holdout-v2.json) · [captured LLM output](../artifacts/release-readiness-holdout-v2-live-2026-10-09.jsonl) · [score detail](release-readiness-holdout-v2-score-2026-10-09.json)

### readiness-holdout-v2-10 — holdout

- **Original Persian question:** آیا رشد تولید و رشد فروش کگهر در آخرین دوره هم‌جهت بوده‌اند؟
- **Expected intent and references:** company/product discovery or industry comparison; domains=company-industry, monthly-activity; measures=MONTHLY_SALES_GROWTH_YOY; dimensions=dim_company, dim_industry, dim_monthly_period, dim_product_identity; relationships=—; operation=—; period=—
- **Actual Navigator/Planner output:** status=failed; selectedMeasures=—; selectedNodes=—; operation=—; selectedFinancialReferences=—; error=requirements_contain_unsupported_financial_value_operation
- **Generated query plan:** measures=—; operation=—; period=—; dimensions=—; relationships=—
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: domains=company-industry, monthly-activity; measures=MONTHLY_SALES_GROWTH_YOY; dimensions=dim_company, dim_industry, dim_monthly_period, dim_product_identity; relationships=—; operation=—; period=—. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: precision=0.0; recall=0.0; f1=0.0.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** UNSUPPORTED
- **Failure reason / exact stage:** requirements_contain_unsupported_financial_value_operation; phase=requirements_extraction; code=requirements_contain_unsupported_financial_value_operation.
- **Source provenance:** catalog version/hash in the run record; selected references=—; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset](release-readiness-holdout-v2.json) · [captured LLM output](../artifacts/release-readiness-holdout-v2-live-2026-10-09.jsonl) · [score detail](release-readiness-holdout-v2-score-2026-10-09.json)

## All 15 live LLM-to-PostgreSQL fixture questions

These records are independent fixture executions. Each SQL statement, bound parameters, plan, observed rows, evidence fingerprints, and full Persian answer (when one was returned) are preserved in the linked JSONL. Fixture values are synthetic and are not Development/production financial data. Where the fixture harness did not define an independent expected-value oracle for a case, the report says so; the observed pipeline value is not substituted as expected truth. `MonthlyReportLineItems.Unit` records the source line unit (fixture rows use `ton`); amount units are interpreted from the governed measure metadata. The sales-rate fixture answer explicitly reports an unknown unit and remains a rendering gap.

### monthly-02-production-product-month — live fixture pipeline

- **Original Persian question:** مقدار تولید گندله کگهر در شهریور ۱۴۰۵ چقدر بوده؟
- **Extracted intent:** expected measures=metric_monthly_production_quantity; selected measures=metric_monthly_production_quantity; entity bindings company=Status=0; CompanyId=f7a05252-afe0-4ddb-8537-8cc18da7a40b; CandidateCompanyIds=f7a05252-afe0-4ddb-8537-8cc18da7a40b; IdentityProvenance=exact_ticker; DiagnosticCode=—, product=Status=0; CompanyId=f7a05252-afe0-4ddb-8537-8cc18da7a40b; ProductKey=NoavaranCurrentApi:feature141-kgehr:TITLE:گندله|UNIT:ton; DisplayTitle=گندله; Unit=ton; IdentityProvenance=feature-129-company-scoped-product-row; CandidateProductKeys=—; DiagnosticCode=—; operation=DirectValueRetrieval/DirectValue/None; period=Policies=explicit-period; ComparisonScopes=—; SelectLatestByReportingPeriod=False; UseSynchronizationTimestampForRecency=False; ReportingPeriodStart=2026-08-23; ReportingPeriodEnd=2026-09-22; MaximumPeriods=—.
- **Generated Query Plan:** measure=metric_monthly_production_quantity; entities={'EntityKind': 'Company', 'UserReference': 'کگهر', 'ResolvedEntityId': 'f7a05252-afe0-4ddb-8537-8cc18da7a40b', 'ResolvedEntityKey': None, 'IdentityProvenance': 'exact_ticker', 'IdentityVerified': True}, {'EntityKind': 'Product', 'UserReference': 'گندله', 'ResolvedEntityId': None, 'ResolvedEntityKey': 'NoavaranCurrentApi:feature141-kgehr:TITLE:گندله|UNIT:ton', 'IdentityProvenance': 'feature-129-company-scoped-product-row', 'IdentityVerified': True}; period=Policies=explicit-period; ComparisonScopes=—; SelectLatestByReportingPeriod=False; UseSynchronizationTimestampForRecency=False; ReportingPeriodStart=2026-08-23; ReportingPeriodEnd=2026-09-22; MaximumPeriods=—; aggregation=—; sort=—; pagination=Offset=0; Limit=1.
- **SQL execution:** Yes; PostgreSQL status=Succeeded; compiler diagnostics=.
- **Expected result:** fixture oracle=47; expected growth=—; units interpreted from seeded fixture/source metadata: ton.
- **Actual result:** values=47; comparison rows=—; observed rows={'CompanyId': 'f7a05252-afe0-4ddb-8537-8cc18da7a40b', 'PeriodStart': '2026-08-23', 'PeriodEnd': '2026-09-22', 'ProductCode': 'NoavaranCurrentApi:feature141-kgehr:TITLE:گندله|UNIT:ton', 'ProductTitle': 'گندله', 'Value': 47, 'SourceUnit': 'ton', 'SamePeriodCandidateCount': 1, 'ProviderName': 'NoavaranCurrentApi', 'ReportType': 'ProductSales', 'OutputType': 0, 'RevisionStatus': 'Accepted', 'ReportIdentity': '17531772-5b8a-450f-ac1d-639c544007a9', 'ExternalReportId': 'fixture-kgehr-shahrivar-1405', 'SourceRowFingerprint': 'row-fixture-kgehr-shahrivar-1405-NoavaranCurrentApi:feature141-kgehr:TITLE:گندله|UNIT:ton', 'SourceMultiplicity': 1, 'ReportId': '17531772-5b8a-450f-ac1d-639c544007a9'}; actual-vs-fixture exact numeric equality=True.
- **Unit correctness:** PASS; expected render unit=تن; observed/source units=ton.
- **Persian answer correctness:** Answered; full returned answer follows.
- **Status:** PASS; failure stage=—; reason=none.
- **Source provenance:** provider=OpenAI; report identity, source row fingerprints, parameterized SQL and selected semantic refs are in the linked trace artifact.
- **Execution mode:** Live LLM + isolated PostgreSQL fixture; Development/production financial databases were not queried.
- **Complete user-facing Persian answer:**

```text
کگهر گندله در بازهٔ ۲۰۲۶-۰۸-۲۳ تا ۲۰۲۶-۰۹-۲۲، مقدار تولید ماهانه: ۴۷ تن.
منبع: NoavaranCurrentApi، گزارش fixture-kgehr-shahrivar-1405.
```
- **Trace:** [full query, SQL, params, rows, answer and provenance](../artifacts/monthly-production-sales-live-llm-postgresql-fifteen-cases-readiness-v2-2026-10-09.jsonl)

### monthly-03-production-mom-growth — live fixture pipeline

- **Original Persian question:** تولید کنسانتره کگل نسبت به ماه قبل چند درصد رشد کرده؟
- **Extracted intent:** expected measures=metric_monthly_production_quantity; selected measures=metric_monthly_production_quantity; entity bindings company=Status=0; CompanyId=2a437020-35d6-4d57-b1bd-a209d7af06fb; CandidateCompanyIds=2a437020-35d6-4d57-b1bd-a209d7af06fb; IdentityProvenance=exact_ticker; DiagnosticCode=—, product=Status=0; CompanyId=2a437020-35d6-4d57-b1bd-a209d7af06fb; ProductKey=NoavaranCurrentApi:feature141-kgel:TITLE:کنسانتره|UNIT:ton; DisplayTitle=کنسانتره; Unit=ton; IdentityProvenance=feature-129-company-scoped-product-row; CandidateProductKeys=—; DiagnosticCode=—; operation=ApprovedPeriodComparison/GrowthPercentage/MonthOverMonth; period=Policies=latest; ComparisonScopes=period-over-period; SelectLatestByReportingPeriod=True; UseSynchronizationTimestampForRecency=False; ReportingPeriodStart=—; ReportingPeriodEnd=—; MaximumPeriods=2.
- **Generated Query Plan:** measure=metric_monthly_production_quantity; entities={'EntityKind': 'Company', 'UserReference': 'کگل', 'ResolvedEntityId': '2a437020-35d6-4d57-b1bd-a209d7af06fb', 'ResolvedEntityKey': None, 'IdentityProvenance': 'exact_ticker', 'IdentityVerified': True}, {'EntityKind': 'Product', 'UserReference': 'کنسانتره', 'ResolvedEntityId': None, 'ResolvedEntityKey': 'NoavaranCurrentApi:feature141-kgel:TITLE:کنسانتره|UNIT:ton', 'IdentityProvenance': 'feature-129-company-scoped-product-row', 'IdentityVerified': True}; period=Policies=latest; ComparisonScopes=period-over-period; SelectLatestByReportingPeriod=True; UseSynchronizationTimestampForRecency=False; ReportingPeriodStart=—; ReportingPeriodEnd=—; MaximumPeriods=2; aggregation=—; sort=—; pagination=Offset=0; Limit=1.
- **SQL execution:** Yes; PostgreSQL status=Succeeded; compiler diagnostics=.
- **Expected result:** fixture oracle=—; expected growth=100; units interpreted from seeded fixture/source metadata: ton.
- **Actual result:** values=12; comparison rows=100.0; observed rows={'Value': 12, 'PeriodStart': '2026-08-23', 'PeriodEnd': '2026-09-22', 'ProductCode': 'NoavaranCurrentApi:feature141-kgel:TITLE:کنسانتره|UNIT:ton', 'SamePeriodCandidateCount': 1, 'PreviousPeriodStart': '2026-08-01', 'PreviousPeriodEnd': '2026-08-31', 'PreviousValue': 6, 'GrowthPercentage': 100.0, 'ComparisonStatus': 'comparable', 'ProductTitle': 'کنسانتره', 'SourceUnit': 'ton', 'PreviousSourceUnit': 'ton', 'CompanyId': '2a437020-35d6-4d57-b1bd-a209d7af06fb', 'ProviderName': 'NoavaranCurrentApi', 'ReportId': '23592725-e0f2-42ac-b903-9afe7ce426e3', 'ExternalReportId': 'fixture-kgel-shahrivar-1405', 'SourceRowFingerprint': 'row-fixture-kgel-shahrivar-1405-NoavaranCurrentApi:feature141-kgel:TITLE:کنسانتره|UNIT:ton', 'SourceMultiplicity': 1}; actual-vs-fixture exact numeric equality=—.
- **Unit correctness:** PASS; expected render unit=درصد; observed/source units=ton.
- **Persian answer correctness:** Answered; full returned answer follows.
- **Status:** PASS; failure stage=—; reason=none.
- **Source provenance:** provider=OpenAI; report identity, source row fingerprints, parameterized SQL and selected semantic refs are in the linked trace artifact.
- **Execution mode:** Live LLM + isolated PostgreSQL fixture; Development/production financial databases were not queried.
- **Complete user-facing Persian answer:**

```text
کگل کنسانتره، مقدار تولید ماهانه در دورهٔ جاری ۱۲ تن و در دورهٔ مبنا ۶ تن بوده است؛ مقدار ثبت‌شده برای تغییر درصدی: ۱۰۰ درصد.
منبع: NoavaranCurrentApi، گزارش fixture-kgel-shahrivar-1405.
```
- **Trace:** [full query, SQL, params, rows, answer and provenance](../artifacts/monthly-production-sales-live-llm-postgresql-fifteen-cases-readiness-v2-2026-10-09.jsonl)

### monthly-04-production-top-products-growth — live fixture pipeline

- **Original Persian question:** کدام محصولات کگهر در سه ماه اخیر بیشترین رشد تولید را داشته‌اند؟
- **Extracted intent:** expected measures=metric_monthly_production_quantity; selected measures=metric_monthly_production_quantity; entity bindings company=Status=0; CompanyId=f7a05252-afe0-4ddb-8537-8cc18da7a40b; CandidateCompanyIds=f7a05252-afe0-4ddb-8537-8cc18da7a40b; IdentityProvenance=exact_ticker; DiagnosticCode=—, product=—; operation=ApprovedPeriodComparison/GrowthPercentage/MonthOverMonth; period=Policies=latest; ComparisonScopes=period-over-period; SelectLatestByReportingPeriod=True; UseSynchronizationTimestampForRecency=False; ReportingPeriodStart=—; ReportingPeriodEnd=—; MaximumPeriods=3.
- **Generated Query Plan:** measure=metric_monthly_production_quantity; entities={'EntityKind': 'Company', 'UserReference': 'کگهر', 'ResolvedEntityId': 'f7a05252-afe0-4ddb-8537-8cc18da7a40b', 'ResolvedEntityKey': None, 'IdentityProvenance': 'exact_ticker', 'IdentityVerified': True}; period=Policies=latest; ComparisonScopes=period-over-period; SelectLatestByReportingPeriod=True; UseSynchronizationTimestampForRecency=False; ReportingPeriodStart=—; ReportingPeriodEnd=—; MaximumPeriods=3; aggregation={'CatalogId': 'metric_monthly_production_quantity', 'Operation': 1}; sort={'CatalogId': 'metric_monthly_production_quantity', 'Direction': 1}; pagination=Offset=0; Limit=3.
- **SQL execution:** Yes; PostgreSQL status=Succeeded; compiler diagnostics=.
- **Expected result:** fixture oracle=—; expected growth=34.285714285714285; units interpreted from seeded fixture/source metadata: ton.
- **Actual result:** values=47; comparison rows=34.285714285714285; observed rows={'Value': 47, 'PeriodStart': '2026-08-23', 'PeriodEnd': '2026-09-22', 'ProductCode': 'NoavaranCurrentApi:feature141-kgehr:TITLE:گندله|UNIT:ton', 'SamePeriodCandidateCount': 1, 'PreviousPeriodStart': '2026-08-01', 'PreviousPeriodEnd': '2026-08-31', 'PreviousValue': 35, 'GrowthPercentage': 34.285714285714285, 'ComparisonStatus': 'comparable', 'ProductTitle': 'گندله', 'SourceUnit': 'ton', 'PreviousSourceUnit': 'ton', 'CompanyId': 'f7a05252-afe0-4ddb-8537-8cc18da7a40b', 'ProviderName': 'NoavaranCurrentApi', 'ReportId': ['17531772-5b8a-450f-ac1d-639c544007a9'], 'ExternalReportId': ['fixture-kgehr-shahrivar-1405'], 'SourceRowFingerprint': ['row-fixture-kgehr-shahrivar-1405-NoavaranCurrentApi:feature141-kgehr:TITLE:گندله|UNIT:ton'], 'SourceMultiplicity': 1}; actual-vs-fixture exact numeric equality=—.
- **Unit correctness:** PASS; expected render unit=درصد; observed/source units=ton.
- **Persian answer correctness:** Answered; full returned answer follows.
- **Status:** PASS; failure stage=—; reason=none.
- **Source provenance:** provider=OpenAI; report identity, source row fingerprints, parameterized SQL and selected semantic refs are in the linked trace artifact.
- **Execution mode:** Live LLM + isolated PostgreSQL fixture; Development/production financial databases were not queried.
- **Complete user-facing Persian answer:**

```text
رتبه‌بندی بر اساس رشد مقدار تولید ماهانه: رتبهٔ ۱: کگهر گندله، ۳۴٫۲۸۵۷۱۴۲۸۵۷۱۴۲۸۵۷۱۴ درصد
منبع: NoavaranCurrentApi، گزارش 17531772-5b8a-450f-ac1d-639c544007a9، fixture-kgehr-shahrivar-1405.
```
- **Trace:** [full query, SQL, params, rows, answer and provenance](../artifacts/monthly-production-sales-live-llm-postgresql-fifteen-cases-readiness-v2-2026-10-09.jsonl)

### monthly-05-sales-amount-month — live fixture pipeline

- **Original Persian question:** فروش ریالی کگل در شهریور ۱۴۰۵ چقدر بوده؟
- **Extracted intent:** expected measures=metric_monthly_sales; selected measures=metric_monthly_sales; entity bindings company=Status=0; CompanyId=2a437020-35d6-4d57-b1bd-a209d7af06fb; CandidateCompanyIds=2a437020-35d6-4d57-b1bd-a209d7af06fb; IdentityProvenance=exact_ticker; DiagnosticCode=—, product=—; operation=DirectValueRetrieval/DirectValue/None; period=Policies=explicit-period; ComparisonScopes=—; SelectLatestByReportingPeriod=False; UseSynchronizationTimestampForRecency=False; ReportingPeriodStart=2026-08-23; ReportingPeriodEnd=2026-09-22; MaximumPeriods=—.
- **Generated Query Plan:** measure=metric_monthly_sales; entities={'EntityKind': 'Company', 'UserReference': 'کگل', 'ResolvedEntityId': '2a437020-35d6-4d57-b1bd-a209d7af06fb', 'ResolvedEntityKey': None, 'IdentityProvenance': 'exact_ticker', 'IdentityVerified': True}; period=Policies=explicit-period; ComparisonScopes=—; SelectLatestByReportingPeriod=False; UseSynchronizationTimestampForRecency=False; ReportingPeriodStart=2026-08-23; ReportingPeriodEnd=2026-09-22; MaximumPeriods=—; aggregation={'CatalogId': 'metric_monthly_sales', 'Operation': 1}; sort=—; pagination=Offset=0; Limit=1.
- **SQL execution:** Yes; PostgreSQL status=Succeeded; compiler diagnostics=.
- **Expected result:** fixture oracle=850; expected growth=—; units interpreted from seeded fixture/source metadata: ton.
- **Actual result:** values=850; comparison rows=—; observed rows={'CompanyId': '2a437020-35d6-4d57-b1bd-a209d7af06fb', 'PeriodStart': '2026-08-23', 'PeriodEnd': '2026-09-22', 'ProductCode': None, 'ProductTitle': None, 'Value': 850, 'SourceUnit': 'ton', 'SamePeriodCandidateCount': 1, 'EligibleReportCount': 1, 'DuplicateProductCount': 0, 'UnitCompatibilityCount': 1, 'InvalidMultiplicityCount': 0, 'SourceMultiplicity': 1, 'ProviderName': 'NoavaranCurrentApi', 'ReportIdentity': ['23592725-e0f2-42ac-b903-9afe7ce426e3'], 'ExternalReportId': ['fixture-kgel-shahrivar-1405'], 'SourceRowFingerprint': ['row-fixture-kgel-shahrivar-1405-NoavaranCurrentApi:feature141-kgel:TITLE:کنسانتره|UNIT:ton', 'row-fixture-kgel-shahrivar-1405-NoavaranCurrentApi:feature141-kgel:TITLE:گندله|UNIT:ton']}; actual-vs-fixture exact numeric equality=True.
- **Unit correctness:** PASS; expected render unit=میلیون ریال; observed/source units=ton.
- **Persian answer correctness:** Answered; full returned answer follows.
- **Status:** PASS; failure stage=—; reason=none.
- **Source provenance:** provider=OpenAI; report identity, source row fingerprints, parameterized SQL and selected semantic refs are in the linked trace artifact.
- **Execution mode:** Live LLM + isolated PostgreSQL fixture; Development/production financial databases were not queried.
- **Complete user-facing Persian answer:**

```text
کگل در بازهٔ ۲۰۲۶-۰۸-۲۳ تا ۲۰۲۶-۰۹-۲۲، مبلغ فروش ماهانه: ۸۵۰ میلیون ریال.
منبع: NoavaranCurrentApi، گزارش 23592725-e0f2-42ac-b903-9afe7ce426e3، fixture-kgel-shahrivar-1405.
```
- **Trace:** [full query, SQL, params, rows, answer and provenance](../artifacts/monthly-production-sales-live-llm-postgresql-fifteen-cases-readiness-v2-2026-10-09.jsonl)

### monthly-06-sales-quantity-product-latest — live fixture pipeline

- **Original Persian question:** مقدار فروش گندله کگهر در آخرین گزارش ماهانه چقدر است؟
- **Extracted intent:** expected measures=metric_monthly_sales_quantity; selected measures=metric_monthly_sales_quantity; entity bindings company=Status=0; CompanyId=f7a05252-afe0-4ddb-8537-8cc18da7a40b; CandidateCompanyIds=f7a05252-afe0-4ddb-8537-8cc18da7a40b; IdentityProvenance=exact_ticker; DiagnosticCode=—, product=Status=0; CompanyId=f7a05252-afe0-4ddb-8537-8cc18da7a40b; ProductKey=NoavaranCurrentApi:feature141-kgehr:TITLE:گندله|UNIT:ton; DisplayTitle=گندله; Unit=ton; IdentityProvenance=feature-129-company-scoped-product-row; CandidateProductKeys=—; DiagnosticCode=—; operation=DirectValueRetrieval/DirectValue/None; period=Policies=latest; ComparisonScopes=—; SelectLatestByReportingPeriod=True; UseSynchronizationTimestampForRecency=False; ReportingPeriodStart=—; ReportingPeriodEnd=—; MaximumPeriods=—.
- **Generated Query Plan:** measure=metric_monthly_sales_quantity; entities={'EntityKind': 'Company', 'UserReference': 'کگهر', 'ResolvedEntityId': 'f7a05252-afe0-4ddb-8537-8cc18da7a40b', 'ResolvedEntityKey': None, 'IdentityProvenance': 'exact_ticker', 'IdentityVerified': True}, {'EntityKind': 'Product', 'UserReference': 'گندله', 'ResolvedEntityId': None, 'ResolvedEntityKey': 'NoavaranCurrentApi:feature141-kgehr:TITLE:گندله|UNIT:ton', 'IdentityProvenance': 'feature-129-company-scoped-product-row', 'IdentityVerified': True}; period=Policies=latest; ComparisonScopes=—; SelectLatestByReportingPeriod=True; UseSynchronizationTimestampForRecency=False; ReportingPeriodStart=—; ReportingPeriodEnd=—; MaximumPeriods=—; aggregation=—; sort=—; pagination=Offset=0; Limit=1.
- **SQL execution:** Yes; PostgreSQL status=Succeeded; compiler diagnostics=.
- **Expected result:** fixture oracle=40; expected growth=—; units interpreted from seeded fixture/source metadata: ton.
- **Actual result:** values=40; comparison rows=—; observed rows={'CompanyId': 'f7a05252-afe0-4ddb-8537-8cc18da7a40b', 'PeriodStart': '2026-08-23', 'PeriodEnd': '2026-09-22', 'ProductCode': 'NoavaranCurrentApi:feature141-kgehr:TITLE:گندله|UNIT:ton', 'ProductTitle': 'گندله', 'Value': 40, 'SourceUnit': 'ton', 'SamePeriodCandidateCount': 1, 'ProviderName': 'NoavaranCurrentApi', 'ReportType': 'ProductSales', 'OutputType': 0, 'RevisionStatus': 'Accepted', 'ReportIdentity': '17531772-5b8a-450f-ac1d-639c544007a9', 'ExternalReportId': 'fixture-kgehr-shahrivar-1405', 'SourceRowFingerprint': 'row-fixture-kgehr-shahrivar-1405-NoavaranCurrentApi:feature141-kgehr:TITLE:گندله|UNIT:ton', 'SourceMultiplicity': 1, 'ReportId': '17531772-5b8a-450f-ac1d-639c544007a9'}; actual-vs-fixture exact numeric equality=True.
- **Unit correctness:** PASS; expected render unit=تن; observed/source units=ton.
- **Persian answer correctness:** Answered; full returned answer follows.
- **Status:** PASS; failure stage=—; reason=none.
- **Source provenance:** provider=OpenAI; report identity, source row fingerprints, parameterized SQL and selected semantic refs are in the linked trace artifact.
- **Execution mode:** Live LLM + isolated PostgreSQL fixture; Development/production financial databases were not queried.
- **Complete user-facing Persian answer:**

```text
کگهر گندله در بازهٔ ۲۰۲۶-۰۸-۲۳ تا ۲۰۲۶-۰۹-۲۲، مقدار فروش ماهانه: ۴۰ تن.
منبع: NoavaranCurrentApi، گزارش fixture-kgehr-shahrivar-1405.
```
- **Trace:** [full query, SQL, params, rows, answer and provenance](../artifacts/monthly-production-sales-live-llm-postgresql-fifteen-cases-readiness-v2-2026-10-09.jsonl)

### monthly-08-sales-six-month-trend — live fixture pipeline

- **Original Persian question:** فروش ریالی کگل در شش ماه گذشته چه روندی داشته؟
- **Extracted intent:** expected measures=metric_monthly_sales; selected measures=metric_monthly_sales; entity bindings company=Status=0; CompanyId=2a437020-35d6-4d57-b1bd-a209d7af06fb; CandidateCompanyIds=2a437020-35d6-4d57-b1bd-a209d7af06fb; IdentityProvenance=exact_ticker; DiagnosticCode=—, product=—; operation=DirectValueRetrieval/TimeSeries/None; period=Policies=latest; ComparisonScopes=—; SelectLatestByReportingPeriod=True; UseSynchronizationTimestampForRecency=False; ReportingPeriodStart=—; ReportingPeriodEnd=—; MaximumPeriods=6.
- **Generated Query Plan:** measure=metric_monthly_sales; entities={'EntityKind': 'Company', 'UserReference': 'کگل', 'ResolvedEntityId': '2a437020-35d6-4d57-b1bd-a209d7af06fb', 'ResolvedEntityKey': None, 'IdentityProvenance': 'exact_ticker', 'IdentityVerified': True}; period=Policies=latest; ComparisonScopes=—; SelectLatestByReportingPeriod=True; UseSynchronizationTimestampForRecency=False; ReportingPeriodStart=—; ReportingPeriodEnd=—; MaximumPeriods=6; aggregation={'CatalogId': 'metric_monthly_sales', 'Operation': 1}; sort=—; pagination=Offset=0; Limit=6.
- **SQL execution:** Yes; PostgreSQL status=Succeeded; compiler diagnostics=.
- **Expected result:** fixture oracle=120, 150, 200, 250, 300, 850; expected growth=—; units interpreted from seeded fixture/source metadata: ton.
- **Actual result:** values=120, 150, 200, 250, 300, 850; comparison rows=—; observed rows={'CompanyId': '2a437020-35d6-4d57-b1bd-a209d7af06fb', 'PeriodStart': '2026-04-01', 'PeriodEnd': '2026-04-30', 'ProductCode': None, 'ProductTitle': None, 'Value': 120, 'SourceUnit': 'ton', 'SamePeriodCandidateCount': 1, 'EligibleReportCount': 1, 'DuplicateProductCount': 0, 'UnitCompatibilityCount': 1, 'InvalidMultiplicityCount': 0, 'SourceMultiplicity': 1, 'ProviderName': 'NoavaranCurrentApi', 'ReportIdentity': ['7fd27a56-35c5-4274-80e6-648bffe695e3'], 'ExternalReportId': ['fixture-kgel-trend-2'], 'SourceRowFingerprint': ['row-fixture-kgel-trend-2-NoavaranCurrentApi:feature141-kgel:TITLE:گندله|UNIT:ton']}, {'CompanyId': '2a437020-35d6-4d57-b1bd-a209d7af06fb', 'PeriodStart': '2026-05-01', 'PeriodEnd': '2026-05-31', 'ProductCode': None, 'ProductTitle': None, 'Value': 150, 'SourceUnit': 'ton', 'SamePeriodCandidateCount': 1, 'EligibleReportCount': 1, 'DuplicateProductCount': 0, 'UnitCompatibilityCount': 1, 'InvalidMultiplicityCount': 0, 'SourceMultiplicity': 1, 'ProviderName': 'NoavaranCurrentApi', 'ReportIdentity': ['a5f8eab0-db26-479d-ae4f-af7bf005dd89'], 'ExternalReportId': ['fixture-kgel-trend-3'], 'SourceRowFingerprint': ['row-fixture-kgel-trend-3-NoavaranCurrentApi:feature141-kgel:TITLE:گندله|UNIT:ton']}, {'CompanyId': '2a437020-35d6-4d57-b1bd-a209d7af06fb', 'PeriodStart': '2026-06-01', 'PeriodEnd': '2026-06-30', 'ProductCode': None, 'ProductTitle': None, 'Value': 200, 'SourceUnit': 'ton', 'SamePeriodCandidateCount': 1, 'EligibleReportCount': 1, 'DuplicateProductCount': 0, 'UnitCompatibilityCount': 1, 'InvalidMultiplicityCount': 0, 'SourceMultiplicity': 1, 'ProviderName': 'NoavaranCurrentApi', 'ReportIdentity': ['0061f9b0-b54c-4dc6-be95-8403812dbeb6'], 'ExternalReportId': ['fixture-kgel-trend-4'], 'SourceRowFingerprint': ['row-fixture-kgel-trend-4-NoavaranCurrentApi:feature141-kgel:TITLE:گندله|UNIT:ton']}, {'CompanyId': '2a437020-35d6-4d57-b1bd-a209d7af06fb', 'PeriodStart': '2026-07-01', 'PeriodEnd': '2026-07-31', 'ProductCode': None, 'ProductTitle': None, 'Value': 250, 'SourceUnit': 'ton', 'SamePeriodCandidateCount': 1, 'EligibleReportCount': 1, 'DuplicateProductCount': 0, 'UnitCompatibilityCount': 1, 'InvalidMultiplicityCount': 0, 'SourceMultiplicity': 1, 'ProviderName': 'NoavaranCurrentApi', 'ReportIdentity': ['4676f1c5-2329-4880-9474-baa7dc4e887f'], 'ExternalReportId': ['fixture-kgel-trend-5'], 'SourceRowFingerprint': ['row-fixture-kgel-trend-5-NoavaranCurrentApi:feature141-kgel:TITLE:گندله|UNIT:ton']}, {'CompanyId': '2a437020-35d6-4d57-b1bd-a209d7af06fb', 'PeriodStart': '2026-08-01', 'PeriodEnd': '2026-08-31', 'ProductCode': None, 'ProductTitle': None, 'Value': 300, 'SourceUnit': 'ton', 'SamePeriodCandidateCount': 1, 'EligibleReportCount': 1, 'DuplicateProductCount': 0, 'UnitCompatibilityCount': 1, 'InvalidMultiplicityCount': 0, 'SourceMultiplicity': 1, 'ProviderName': 'NoavaranCurrentApi', 'ReportIdentity': ['5e7ad8e1-b457-4179-96bc-37ecb70c5e45'], 'ExternalReportId': ['fixture-kgel-trend-6'], 'SourceRowFingerprint': ['row-fixture-kgel-trend-6-NoavaranCurrentApi:feature141-kgel:TITLE:کنسانتره|UNIT:ton', 'row-fixture-kgel-trend-6-NoavaranCurrentApi:feature141-kgel:TITLE:گندله|UNIT:ton']}, {'CompanyId': '2a437020-35d6-4d57-b1bd-a209d7af06fb', 'PeriodStart': '2026-08-23', 'PeriodEnd': '2026-09-22', 'ProductCode': None, 'ProductTitle': None, 'Value': 850, 'SourceUnit': 'ton', 'SamePeriodCandidateCount': 1, 'EligibleReportCount': 1, 'DuplicateProductCount': 0, 'UnitCompatibilityCount': 1, 'InvalidMultiplicityCount': 0, 'SourceMultiplicity': 1, 'ProviderName': 'NoavaranCurrentApi', 'ReportIdentity': ['23592725-e0f2-42ac-b903-9afe7ce426e3'], 'ExternalReportId': ['fixture-kgel-shahrivar-1405'], 'SourceRowFingerprint': ['row-fixture-kgel-shahrivar-1405-NoavaranCurrentApi:feature141-kgel:TITLE:کنسانتره|UNIT:ton', 'row-fixture-kgel-shahrivar-1405-NoavaranCurrentApi:feature141-kgel:TITLE:گندله|UNIT:ton']}; actual-vs-fixture exact numeric equality=True.
- **Unit correctness:** PASS; expected render unit=میلیون ریال; observed/source units=ton.
- **Persian answer correctness:** Answered; full returned answer follows.
- **Status:** PASS; failure stage=—; reason=none.
- **Source provenance:** provider=OpenAI; report identity, source row fingerprints, parameterized SQL and selected semantic refs are in the linked trace artifact.
- **Execution mode:** Live LLM + isolated PostgreSQL fixture; Development/production financial databases were not queried.
- **Complete user-facing Persian answer:**

```text
روند مبلغ فروش ماهانه بر اساس دوره‌های گزارش‌شده: در بازهٔ ۲۰۲۶-۰۴-۰۱ تا ۲۰۲۶-۰۴-۳۰ ۱۲۰ میلیون ریال
منبع: NoavaranCurrentApi، گزارش 7fd27a56-35c5-4274-80e6-648bffe695e3، fixture-kgel-trend-2؛ در بازهٔ ۲۰۲۶-۰۵-۰۱ تا ۲۰۲۶-۰۵-۳۱ ۱۵۰ میلیون ریال
منبع: NoavaranCurrentApi، گزارش a5f8eab0-db26-479d-ae4f-af7bf005dd89، fixture-kgel-trend-3؛ در بازهٔ ۲۰۲۶-۰۶-۰۱ تا ۲۰۲۶-۰۶-۳۰ ۲۰۰ میلیون ریال
منبع: NoavaranCurrentApi، گزارش 0061f9b0-b54c-4dc6-be95-8403812dbeb6، fixture-kgel-trend-4؛ در بازهٔ ۲۰۲۶-۰۷-۰۱ تا ۲۰۲۶-۰۷-۳۱ ۲۵۰ میلیون ریال
منبع: NoavaranCurrentApi، گزارش 4676f1c5-2329-4880-9474-baa7dc4e887f، fixture-kgel-trend-5؛ در بازهٔ ۲۰۲۶-۰۸-۰۱ تا ۲۰۲۶-۰۸-۳۱ ۳۰۰ میلیون ریال
منبع: NoavaranCurrentApi، گزارش 5e7ad8e1-b457-4179-96bc-37ecb70c5e45، fixture-kgel-trend-6؛ در بازهٔ ۲۰۲۶-۰۸-۲۳ تا ۲۰۲۶-۰۹-۲۲ ۸۵۰ میلیون ریال
منبع: NoavaranCurrentApi، گزارش 23592725-e0f2-42ac-b903-9afe7ce426e3، fixture-kgel-shahrivar-1405.
```
- **Trace:** [full query, SQL, params, rows, answer and provenance](../artifacts/monthly-production-sales-live-llm-postgresql-fifteen-cases-readiness-v2-2026-10-09.jsonl)

### monthly-09-cross-company-product-sales — live fixture pipeline

- **Original Persian question:** کدام شرکت‌های تولیدکننده گندله بیشترین مقدار فروش را داشته‌اند؟
- **Extracted intent:** expected measures=metric_monthly_sales_quantity; selected measures=metric_monthly_sales_quantity; entity bindings company=—, product=Status=0; CompanyId=—; ProductKey=product_iron_ore_pellets; DisplayTitle=گندله سنگ‌آهن; Unit=—; IdentityProvenance=product-catalog-v1:owner-confirmed:feature141-product-equivalence:2026-10-09; CandidateProductKeys=—; DiagnosticCode=—; operation=DirectValueRetrieval/DirectValue/None; period=Policies=approved-observation-window; ComparisonScopes=—; SelectLatestByReportingPeriod=False; UseSynchronizationTimestampForRecency=False; ReportingPeriodStart=2025-10-09; ReportingPeriodEnd=2026-10-09; MaximumPeriods=12.
- **Generated Query Plan:** measure=metric_monthly_sales_quantity; entities={'EntityKind': 'Product', 'UserReference': 'گندله', 'ResolvedEntityId': None, 'ResolvedEntityKey': 'product_iron_ore_pellets', 'IdentityProvenance': 'product-catalog-v1:owner-confirmed:feature141-product-equivalence:2026-10-09', 'IdentityVerified': True}; period=Policies=approved-observation-window; ComparisonScopes=—; SelectLatestByReportingPeriod=False; UseSynchronizationTimestampForRecency=False; ReportingPeriodStart=2025-10-09; ReportingPeriodEnd=2026-10-09; MaximumPeriods=12; aggregation={'CatalogId': 'metric_monthly_sales_quantity', 'Operation': 1}; sort={'CatalogId': 'metric_monthly_sales_quantity', 'Direction': 1}; pagination=Offset=0; Limit=10.
- **SQL execution:** Yes; PostgreSQL status=Incomplete; compiler diagnostics=.
- **Expected result:** fixture oracle=—; expected growth=—; units interpreted from seeded fixture/source metadata: ton.
- **Actual result:** values=—; comparison rows=—; observed rows={'CompanyId': '5202a885-2afc-4939-a09d-f794c6bf058a', 'CompanyName': 'Dynamically discovered producer', 'CompanySymbol': 'NEWP', 'PeriodStart': '2026-08-23', 'PeriodEnd': '2026-09-22', 'Value': 55, 'SourceUnit': 'ton', 'UnitCompatibilityCount': 1, 'EligibleReportCount': 1, 'SamePeriodCandidateCount': 1, 'InvalidMultiplicityCount': 0, 'SourceMultiplicity': 1, 'ProviderName': 'NoavaranCurrentApi', 'ReportIdentity': ['12c924fe-24b5-40d3-9c62-c3d52267e6b2'], 'ExternalReportId': ['fixture-new-producer-shahrivar-1405'], 'SourceRowFingerprint': ['row-fixture-new-producer-shahrivar-1405-NoavaranCurrentApi:feature141-new-producer:TITLE:گندله|UNIT:ton'], 'ProductMappingCoverageStatus': 'partial', 'CompanyCoverageComplete': True}, {'CompanyId': 'f7a05252-afe0-4ddb-8537-8cc18da7a40b', 'CompanyName': 'Fixture Kogهر', 'CompanySymbol': 'کگهر', 'PeriodStart': '2026-08-23', 'PeriodEnd': '2026-09-22', 'Value': 40, 'SourceUnit': 'ton', 'UnitCompatibilityCount': 1, 'EligibleReportCount': 1, 'SamePeriodCandidateCount': 1, 'InvalidMultiplicityCount': 0, 'SourceMultiplicity': 1, 'ProviderName': 'NoavaranCurrentApi', 'ReportIdentity': ['17531772-5b8a-450f-ac1d-639c544007a9'], 'ExternalReportId': ['fixture-kgehr-shahrivar-1405'], 'SourceRowFingerprint': ['row-fixture-kgehr-shahrivar-1405-NoavaranCurrentApi:feature141-kgehr:TITLE:گندله|UNIT:ton'], 'ProductMappingCoverageStatus': 'partial', 'CompanyCoverageComplete': True}, {'CompanyId': '2a437020-35d6-4d57-b1bd-a209d7af06fb', 'CompanyName': 'Fixture Kegel', 'CompanySymbol': 'کگل', 'PeriodStart': '2026-08-23', 'PeriodEnd': '2026-09-22', 'Value': 20, 'SourceUnit': 'ton', 'UnitCompatibilityCount': 1, 'EligibleReportCount': 1, 'SamePeriodCandidateCount': 1, 'InvalidMultiplicityCount': 0, 'SourceMultiplicity': 1, 'ProviderName': 'NoavaranCurrentApi', 'ReportIdentity': ['23592725-e0f2-42ac-b903-9afe7ce426e3'], 'ExternalReportId': ['fixture-kgel-shahrivar-1405'], 'SourceRowFingerprint': ['row-fixture-kgel-shahrivar-1405-NoavaranCurrentApi:feature141-kgel:TITLE:گندله|UNIT:ton'], 'ProductMappingCoverageStatus': 'partial', 'CompanyCoverageComplete': True}, {'CompanyId': 'a64bbbd9-7788-4bf0-a8c7-2ed304be5d68', 'CompanyName': 'Positive pellet producer without sales', 'CompanySymbol': 'NOSALES', 'PeriodStart': '2026-08-23', 'PeriodEnd': '2026-09-22', 'Value': 0, 'SourceUnit': 'ton', 'UnitCompatibilityCount': 1, 'EligibleReportCount': 1, 'SamePeriodCandidateCount': 1, 'InvalidMultiplicityCount': 0, 'SourceMultiplicity': 1, 'ProviderName': 'NoavaranCurrentApi', 'ReportIdentity': ['1a99773f-ab41-4bc3-9afc-ab3d63ff273f'], 'ExternalReportId': ['fixture-production-only-shahrivar-1405'], 'SourceRowFingerprint': ['row-fixture-production-only-shahrivar-1405-NoavaranCurrentApi:feature141-production-only:TITLE:گندله|UNIT:ton'], 'ProductMappingCoverageStatus': 'partial', 'CompanyCoverageComplete': True}; actual-vs-fixture exact numeric equality=—.
- **Unit correctness:** not evaluable (pipeline did not return a successful answer); expected render unit=تن; observed/source units=ton.
- **Persian answer correctness:** Incomplete; full returned answer follows.
- **Status:** BLOCKED; failure stage=restricted_postgresql_execution; reason=PostgreSQL returned Incomplete; observed product mapping coverage=partial; the artifact exposes no more specific execution error code.
- **Source provenance:** provider=OpenAI; report identity, source row fingerprints, parameterized SQL and selected semantic refs are in the linked trace artifact.
- **Execution mode:** Live LLM + isolated PostgreSQL fixture; Development/production financial databases were not queried.
- **Complete user-facing Persian answer:**

```text
پوشش جمعیت تولیدکنندگان یا نگاشت محصول برای رتبه‌بندی کامل نیست؛ رتبهٔ مالی گزارش نمی‌شود.
```
- **Trace:** [full query, SQL, params, rows, answer and provenance](../artifacts/monthly-production-sales-live-llm-postgresql-fifteen-cases-readiness-v2-2026-10-09.jsonl)

### monthly-10-company-sales-comparison — live fixture pipeline

- **Original Persian question:** فروش ماهانه کگل و کگهر را با هم مقایسه کن.
- **Extracted intent:** expected measures=metric_monthly_sales; selected measures=metric_monthly_sales; entity bindings company=—, product=—; operation=DirectValueRetrieval/DirectValue/MultiCompanyComparison; period=Policies=latest; ComparisonScopes=compare; SelectLatestByReportingPeriod=True; UseSynchronizationTimestampForRecency=False; ReportingPeriodStart=—; ReportingPeriodEnd=—; MaximumPeriods=—.
- **Generated Query Plan:** measure=metric_monthly_sales; entities={'EntityKind': 'Company', 'UserReference': 'کگل', 'ResolvedEntityId': '2a437020-35d6-4d57-b1bd-a209d7af06fb', 'ResolvedEntityKey': None, 'IdentityProvenance': 'exact_ticker', 'IdentityVerified': True}, {'EntityKind': 'Company', 'UserReference': 'کگهر', 'ResolvedEntityId': 'f7a05252-afe0-4ddb-8537-8cc18da7a40b', 'ResolvedEntityKey': None, 'IdentityProvenance': 'exact_ticker', 'IdentityVerified': True}; period=Policies=latest; ComparisonScopes=compare; SelectLatestByReportingPeriod=True; UseSynchronizationTimestampForRecency=False; ReportingPeriodStart=—; ReportingPeriodEnd=—; MaximumPeriods=—; aggregation={'CatalogId': 'metric_monthly_sales', 'Operation': 1}; sort=—; pagination=Offset=0; Limit=100.
- **SQL execution:** Yes; PostgreSQL status=Succeeded; compiler diagnostics=.
- **Expected result:** fixture oracle=850, 700; expected growth=—; units interpreted from seeded fixture/source metadata: ton.
- **Actual result:** values=850, 700; comparison rows=—; observed rows={'CompanyId': '2a437020-35d6-4d57-b1bd-a209d7af06fb', 'PeriodStart': '2026-08-23', 'PeriodEnd': '2026-09-22', 'ProductCode': None, 'ProductTitle': None, 'Value': 850, 'SourceUnit': 'ton', 'SamePeriodCandidateCount': 1, 'EligibleReportCount': 1, 'DuplicateProductCount': 0, 'UnitCompatibilityCount': 1, 'InvalidMultiplicityCount': 0, 'SourceMultiplicity': 1, 'ProviderName': 'NoavaranCurrentApi', 'ReportIdentity': ['23592725-e0f2-42ac-b903-9afe7ce426e3'], 'ExternalReportId': ['fixture-kgel-shahrivar-1405'], 'SourceRowFingerprint': ['row-fixture-kgel-shahrivar-1405-NoavaranCurrentApi:feature141-kgel:TITLE:کنسانتره|UNIT:ton', 'row-fixture-kgel-shahrivar-1405-NoavaranCurrentApi:feature141-kgel:TITLE:گندله|UNIT:ton']}, {'CompanyId': 'f7a05252-afe0-4ddb-8537-8cc18da7a40b', 'PeriodStart': '2026-08-23', 'PeriodEnd': '2026-09-22', 'ProductCode': None, 'ProductTitle': None, 'Value': 700, 'SourceUnit': 'ton', 'SamePeriodCandidateCount': 1, 'EligibleReportCount': 1, 'DuplicateProductCount': 0, 'UnitCompatibilityCount': 1, 'InvalidMultiplicityCount': 0, 'SourceMultiplicity': 1, 'ProviderName': 'NoavaranCurrentApi', 'ReportIdentity': ['17531772-5b8a-450f-ac1d-639c544007a9'], 'ExternalReportId': ['fixture-kgehr-shahrivar-1405'], 'SourceRowFingerprint': ['row-fixture-kgehr-shahrivar-1405-NoavaranCurrentApi:feature141-kgehr:TITLE:گندله|UNIT:ton']}; actual-vs-fixture exact numeric equality=True.
- **Unit correctness:** PASS; expected render unit=میلیون ریال; observed/source units=ton.
- **Persian answer correctness:** Answered; full returned answer follows.
- **Status:** PASS; failure stage=—; reason=none.
- **Source provenance:** provider=OpenAI; report identity, source row fingerprints, parameterized SQL and selected semantic refs are in the linked trace artifact.
- **Execution mode:** Live LLM + isolated PostgreSQL fixture; Development/production financial databases were not queried.
- **Complete user-facing Persian answer:**

```text
مقایسهٔ مبلغ فروش ماهانه: کگل ۸۵۰ میلیون ریال (در بازهٔ ۲۰۲۶-۰۸-۲۳ تا ۲۰۲۶-۰۹-۲۲)
منبع: NoavaranCurrentApi، گزارش 23592725-e0f2-42ac-b903-9afe7ce426e3، fixture-kgel-shahrivar-1405؛ کگهر ۷۰۰ میلیون ریال (در بازهٔ ۲۰۲۶-۰۸-۲۳ تا ۲۰۲۶-۰۹-۲۲)
منبع: NoavaranCurrentApi، گزارش 17531772-5b8a-450f-ac1d-639c544007a9، fixture-kgehr-shahrivar-1405.
```
- **Trace:** [full query, SQL, params, rows, answer and provenance](../artifacts/monthly-production-sales-live-llm-postgresql-fifteen-cases-readiness-v2-2026-10-09.jsonl)

### monthly-14-production-concentrate-explicit-month — live fixture pipeline

- **Original Persian question:** مقدار تولید کنسانتره کگل در شهریور ۱۴۰۵ چقدر بوده؟
- **Extracted intent:** expected measures=metric_monthly_production_quantity; selected measures=metric_monthly_production_quantity; entity bindings company=Status=0; CompanyId=2a437020-35d6-4d57-b1bd-a209d7af06fb; CandidateCompanyIds=2a437020-35d6-4d57-b1bd-a209d7af06fb; IdentityProvenance=exact_ticker; DiagnosticCode=—, product=Status=0; CompanyId=2a437020-35d6-4d57-b1bd-a209d7af06fb; ProductKey=NoavaranCurrentApi:feature141-kgel:TITLE:کنسانتره|UNIT:ton; DisplayTitle=کنسانتره; Unit=ton; IdentityProvenance=feature-129-company-scoped-product-row; CandidateProductKeys=—; DiagnosticCode=—; operation=DirectValueRetrieval/DirectValue/None; period=Policies=explicit-period; ComparisonScopes=—; SelectLatestByReportingPeriod=False; UseSynchronizationTimestampForRecency=False; ReportingPeriodStart=2026-08-23; ReportingPeriodEnd=2026-09-22; MaximumPeriods=—.
- **Generated Query Plan:** measure=metric_monthly_production_quantity; entities={'EntityKind': 'Company', 'UserReference': 'کگل', 'ResolvedEntityId': '2a437020-35d6-4d57-b1bd-a209d7af06fb', 'ResolvedEntityKey': None, 'IdentityProvenance': 'exact_ticker', 'IdentityVerified': True}, {'EntityKind': 'Product', 'UserReference': 'کنسانتره', 'ResolvedEntityId': None, 'ResolvedEntityKey': 'NoavaranCurrentApi:feature141-kgel:TITLE:کنسانتره|UNIT:ton', 'IdentityProvenance': 'feature-129-company-scoped-product-row', 'IdentityVerified': True}; period=Policies=explicit-period; ComparisonScopes=—; SelectLatestByReportingPeriod=False; UseSynchronizationTimestampForRecency=False; ReportingPeriodStart=2026-08-23; ReportingPeriodEnd=2026-09-22; MaximumPeriods=—; aggregation=—; sort=—; pagination=Offset=0; Limit=1.
- **SQL execution:** Yes; PostgreSQL status=Succeeded; compiler diagnostics=.
- **Expected result:** fixture oracle=12; expected growth=—; units interpreted from seeded fixture/source metadata: ton.
- **Actual result:** values=12; comparison rows=—; observed rows={'CompanyId': '2a437020-35d6-4d57-b1bd-a209d7af06fb', 'PeriodStart': '2026-08-23', 'PeriodEnd': '2026-09-22', 'ProductCode': 'NoavaranCurrentApi:feature141-kgel:TITLE:کنسانتره|UNIT:ton', 'ProductTitle': 'کنسانتره', 'Value': 12, 'SourceUnit': 'ton', 'SamePeriodCandidateCount': 1, 'ProviderName': 'NoavaranCurrentApi', 'ReportType': 'ProductSales', 'OutputType': 0, 'RevisionStatus': 'Accepted', 'ReportIdentity': '23592725-e0f2-42ac-b903-9afe7ce426e3', 'ExternalReportId': 'fixture-kgel-shahrivar-1405', 'SourceRowFingerprint': 'row-fixture-kgel-shahrivar-1405-NoavaranCurrentApi:feature141-kgel:TITLE:کنسانتره|UNIT:ton', 'SourceMultiplicity': 1, 'ReportId': '23592725-e0f2-42ac-b903-9afe7ce426e3'}; actual-vs-fixture exact numeric equality=True.
- **Unit correctness:** PASS; expected render unit=تن; observed/source units=ton.
- **Persian answer correctness:** Answered; full returned answer follows.
- **Status:** PASS; failure stage=—; reason=none.
- **Source provenance:** provider=OpenAI; report identity, source row fingerprints, parameterized SQL and selected semantic refs are in the linked trace artifact.
- **Execution mode:** Live LLM + isolated PostgreSQL fixture; Development/production financial databases were not queried.
- **Complete user-facing Persian answer:**

```text
کگل کنسانتره در بازهٔ ۲۰۲۶-۰۸-۲۳ تا ۲۰۲۶-۰۹-۲۲، مقدار تولید ماهانه: ۱۲ تن.
منبع: NoavaranCurrentApi، گزارش fixture-kgel-shahrivar-1405.
```
- **Trace:** [full query, SQL, params, rows, answer and provenance](../artifacts/monthly-production-sales-live-llm-postgresql-fifteen-cases-readiness-v2-2026-10-09.jsonl)

### monthly-01-production-latest — live fixture pipeline

- **Original Persian question:** کگل در آخرین ماه چه مقدار تولید داشته است؟
- **Extracted intent:** expected measures=metric_monthly_production_quantity; selected measures=metric_monthly_production_quantity; entity bindings company=Status=0; CompanyId=2a437020-35d6-4d57-b1bd-a209d7af06fb; CandidateCompanyIds=2a437020-35d6-4d57-b1bd-a209d7af06fb; IdentityProvenance=exact_ticker; DiagnosticCode=—, product=—; operation=DirectValueRetrieval/DirectValue/None; period=Policies=latest; ComparisonScopes=—; SelectLatestByReportingPeriod=True; UseSynchronizationTimestampForRecency=False; ReportingPeriodStart=—; ReportingPeriodEnd=—; MaximumPeriods=—.
- **Generated Query Plan:** measure=metric_monthly_production_quantity; entities={'EntityKind': 'Company', 'UserReference': 'کگل', 'ResolvedEntityId': '2a437020-35d6-4d57-b1bd-a209d7af06fb', 'ResolvedEntityKey': None, 'IdentityProvenance': 'exact_ticker', 'IdentityVerified': True}; period=Policies=latest; ComparisonScopes=—; SelectLatestByReportingPeriod=True; UseSynchronizationTimestampForRecency=False; ReportingPeriodStart=—; ReportingPeriodEnd=—; MaximumPeriods=—; aggregation={'CatalogId': 'metric_monthly_production_quantity', 'Operation': 1}; sort=—; pagination=Offset=0; Limit=1.
- **SQL execution:** Yes; PostgreSQL status=Succeeded; compiler diagnostics=.
- **Expected result:** fixture oracle=—; expected growth=—; units interpreted from seeded fixture/source metadata: ton.
- **Actual result:** values=37; comparison rows=—; observed rows={'CompanyId': '2a437020-35d6-4d57-b1bd-a209d7af06fb', 'PeriodStart': '2026-08-23', 'PeriodEnd': '2026-09-22', 'ProductCode': None, 'ProductTitle': None, 'Value': 37, 'SourceUnit': 'ton', 'SamePeriodCandidateCount': 1, 'EligibleReportCount': 1, 'DuplicateProductCount': 0, 'UnitCompatibilityCount': 1, 'InvalidMultiplicityCount': 0, 'SourceMultiplicity': 1, 'ProviderName': 'NoavaranCurrentApi', 'ReportIdentity': ['23592725-e0f2-42ac-b903-9afe7ce426e3'], 'ExternalReportId': ['fixture-kgel-shahrivar-1405'], 'SourceRowFingerprint': ['row-fixture-kgel-shahrivar-1405-NoavaranCurrentApi:feature141-kgel:TITLE:کنسانتره|UNIT:ton', 'row-fixture-kgel-shahrivar-1405-NoavaranCurrentApi:feature141-kgel:TITLE:گندله|UNIT:ton']}; actual-vs-fixture exact numeric equality=—.
- **Unit correctness:** PASS; expected render unit=تن; observed/source units=ton.
- **Persian answer correctness:** Answered; full returned answer follows.
- **Status:** PASS; failure stage=—; reason=none.
- **Source provenance:** provider=OpenAI; report identity, source row fingerprints, parameterized SQL and selected semantic refs are in the linked trace artifact.
- **Execution mode:** Live LLM + isolated PostgreSQL fixture; Development/production financial databases were not queried.
- **Complete user-facing Persian answer:**

```text
کگل در بازهٔ ۲۰۲۶-۰۸-۲۳ تا ۲۰۲۶-۰۹-۲۲، مقدار تولید ماهانه: ۳۷ تن.
منبع: NoavaranCurrentApi، گزارش 23592725-e0f2-42ac-b903-9afe7ce426e3، fixture-kgel-shahrivar-1405.
```
- **Trace:** [full query, SQL, params, rows, answer and provenance](../artifacts/monthly-production-sales-live-llm-postgresql-fifteen-cases-readiness-v2-2026-10-09.jsonl)

### monthly-07-sales-rate-two-months — live fixture pipeline

- **Original Persian question:** نرخ فروش گندله کگهر در مرداد و شهریور ۱۴۰۵ چقدر بوده؟
- **Extracted intent:** expected measures=metric_monthly_sales_rate; selected measures=metric_monthly_sales_rate; entity bindings company=Status=0; CompanyId=f7a05252-afe0-4ddb-8537-8cc18da7a40b; CandidateCompanyIds=f7a05252-afe0-4ddb-8537-8cc18da7a40b; IdentityProvenance=exact_ticker; DiagnosticCode=—, product=Status=0; CompanyId=f7a05252-afe0-4ddb-8537-8cc18da7a40b; ProductKey=NoavaranCurrentApi:feature141-kgehr:TITLE:گندله|UNIT:ton; DisplayTitle=گندله; Unit=ton; IdentityProvenance=feature-129-company-scoped-product-row; CandidateProductKeys=—; DiagnosticCode=—; operation=DirectValueRetrieval/DirectValue/None; period=Policies=explicit-period; ComparisonScopes=—; SelectLatestByReportingPeriod=False; UseSynchronizationTimestampForRecency=False; ReportingPeriodStart=2026-07-23; ReportingPeriodEnd=2026-09-22; MaximumPeriods=—.
- **Generated Query Plan:** measure=metric_monthly_sales_rate; entities={'EntityKind': 'Company', 'UserReference': 'کگهر', 'ResolvedEntityId': 'f7a05252-afe0-4ddb-8537-8cc18da7a40b', 'ResolvedEntityKey': None, 'IdentityProvenance': 'exact_ticker', 'IdentityVerified': True}, {'EntityKind': 'Product', 'UserReference': 'گندله', 'ResolvedEntityId': None, 'ResolvedEntityKey': 'NoavaranCurrentApi:feature141-kgehr:TITLE:گندله|UNIT:ton', 'IdentityProvenance': 'feature-129-company-scoped-product-row', 'IdentityVerified': True}; period=Policies=explicit-period; ComparisonScopes=—; SelectLatestByReportingPeriod=False; UseSynchronizationTimestampForRecency=False; ReportingPeriodStart=2026-07-23; ReportingPeriodEnd=2026-09-22; MaximumPeriods=—; aggregation=—; sort=—; pagination=Offset=0; Limit=100.
- **SQL execution:** Yes; PostgreSQL status=Succeeded; compiler diagnostics=.
- **Expected result:** fixture oracle=—; expected growth=—; units interpreted from seeded fixture/source metadata: ton.
- **Actual result:** values=16.7, 17.5; comparison rows=—; observed rows={'CompanyId': 'f7a05252-afe0-4ddb-8537-8cc18da7a40b', 'PeriodStart': '2026-08-01', 'PeriodEnd': '2026-08-31', 'ProductCode': 'NoavaranCurrentApi:feature141-kgehr:TITLE:گندله|UNIT:ton', 'ProductTitle': 'Pellet', 'Value': 16.7, 'SourceUnit': 'ton', 'SamePeriodCandidateCount': 1, 'ProviderName': 'NoavaranCurrentApi', 'ReportType': 'ProductSales', 'OutputType': 0, 'RevisionStatus': 'Accepted', 'ReportIdentity': '6a326245-48a6-4e01-8b10-39d1fee0bbef', 'ExternalReportId': 'fixture-kgehr-august-1405', 'SourceRowFingerprint': 'row-fixture-kgehr-august-1405-NoavaranCurrentApi:feature141-kgehr:TITLE:گندله|UNIT:ton', 'SourceMultiplicity': 1, 'ReportId': '6a326245-48a6-4e01-8b10-39d1fee0bbef'}, {'CompanyId': 'f7a05252-afe0-4ddb-8537-8cc18da7a40b', 'PeriodStart': '2026-08-23', 'PeriodEnd': '2026-09-22', 'ProductCode': 'NoavaranCurrentApi:feature141-kgehr:TITLE:گندله|UNIT:ton', 'ProductTitle': 'گندله', 'Value': 17.5, 'SourceUnit': 'ton', 'SamePeriodCandidateCount': 1, 'ProviderName': 'NoavaranCurrentApi', 'ReportType': 'ProductSales', 'OutputType': 0, 'RevisionStatus': 'Accepted', 'ReportIdentity': '17531772-5b8a-450f-ac1d-639c544007a9', 'ExternalReportId': 'fixture-kgehr-shahrivar-1405', 'SourceRowFingerprint': 'row-fixture-kgehr-shahrivar-1405-NoavaranCurrentApi:feature141-kgehr:TITLE:گندله|UNIT:ton', 'SourceMultiplicity': 1, 'ReportId': '17531772-5b8a-450f-ac1d-639c544007a9'}; actual-vs-fixture exact numeric equality=—.
- **Unit correctness:** PARTIAL: answer explicitly says the sales-rate unit is unknown; expected render unit=—; observed/source units=ton.
- **Persian answer correctness:** Answered; full returned answer follows.
- **Status:** PARTIAL (unit unresolved); failure stage=—; reason=none.
- **Source provenance:** provider=OpenAI; report identity, source row fingerprints, parameterized SQL and selected semantic refs are in the linked trace artifact.
- **Execution mode:** Live LLM + isolated PostgreSQL fixture; Development/production financial databases were not queried.
- **Complete user-facing Persian answer:**

```text
کگهر Pellet در بازهٔ ۲۰۲۶-۰۸-۰۱ تا ۲۰۲۶-۰۸-۳۱، مقدار مالی گزارش‌شده: ۱۶٫۷ واحد اندازه‌گیری نامشخص.
منبع: NoavaranCurrentApi، گزارش fixture-kgehr-august-1405.
```
- **Trace:** [full query, SQL, params, rows, answer and provenance](../artifacts/monthly-production-sales-live-llm-postgresql-fifteen-cases-readiness-v2-2026-10-09.jsonl)

### monthly-11-industry-sales-growth-ranking — live fixture pipeline

- **Original Persian question:** کدام شرکت‌های یک صنعت بیشترین رشد فروش ماهانه را ثبت کرده‌اند؟
- **Extracted intent:** expected measures=metric_monthly_sales_growth_mom; selected measures=—; entity bindings company=—, product=—; operation=ApprovedPeriodComparison/GrowthPercentage/MonthOverMonth; period=Policies=latest; ComparisonScopes=period-over-period; SelectLatestByReportingPeriod=True; UseSynchronizationTimestampForRecency=False; ReportingPeriodStart=—; ReportingPeriodEnd=—; MaximumPeriods=2.
- **Generated Query Plan:** measure=—; entities=—; period=Policies=latest; ComparisonScopes=period-over-period; SelectLatestByReportingPeriod=True; UseSynchronizationTimestampForRecency=False; ReportingPeriodStart=—; ReportingPeriodEnd=—; MaximumPeriods=2; aggregation={'CatalogId': 'metric_monthly_sales', 'Operation': 1}; sort={'CatalogId': 'metric_monthly_sales', 'Direction': 1}; pagination=Offset=0; Limit=100.
- **SQL execution:** No; PostgreSQL status=None; compiler diagnostics=—.
- **Expected result:** fixture oracle=—; expected growth=—; units interpreted from seeded fixture/source metadata: —.
- **Actual result:** values=—; comparison rows=—; observed rows=—; actual-vs-fixture exact numeric equality=—.
- **Unit correctness:** not evaluable (pipeline did not return a successful answer); expected render unit=درصد; observed/source units=—.
- **Persian answer correctness:** Unsupported; full returned answer follows.
- **Status:** AMBIGUOUS; failure stage=requirements_or_measure_selection; reason=retrieval_requirements_incomplete.
- **Source provenance:** provider=OpenAI; report identity, source row fingerprints, parameterized SQL and selected semantic refs are in the linked trace artifact.
- **Execution mode:** Live LLM + isolated PostgreSQL fixture; Development/production financial databases were not queried.
- **Complete user-facing Persian answer:**

```text
این درخواست با تعریف‌های تأییدشدهٔ پرس‌وجو قابل اجرا نیست.
```
- **Trace:** [full query, SQL, params, rows, answer and provenance](../artifacts/monthly-production-sales-live-llm-postgresql-fifteen-cases-readiness-v2-2026-10-09.jsonl)

### monthly-12-sales-vs-operating-profit-growth — live fixture pipeline

- **Original Persian question:** رشد فروش ماهانه کگل با رشد سود عملیاتی آن چه تفاوتی داشته؟
- **Extracted intent:** expected measures=metric_monthly_sales_growth_mom; selected measures=—; entity bindings company=—, product=—; operation=—/—/—; period=—.
- **Generated Query Plan:** measure=—; entities=—; period=—; aggregation=—; sort=—; pagination=—.
- **SQL execution:** No; PostgreSQL status=not executed; compiler diagnostics=—.
- **Expected result:** fixture oracle=—; expected growth=—; units interpreted from seeded fixture/source metadata: —.
- **Actual result:** values=—; comparison rows=—; observed rows=—; actual-vs-fixture exact numeric equality=—.
- **Unit correctness:** not evaluable (pipeline did not return a successful answer); expected render unit=—; observed/source units=—.
- **Persian answer correctness:** no answer; full returned answer follows.
- **Status:** UNSUPPORTED; failure stage=pipeline; reason=requirements_contain_unsupported_financial_value_operation.
- **Source provenance:** provider=—; report identity, source row fingerprints, parameterized SQL and selected semantic refs are in the linked trace artifact.
- **Execution mode:** Live LLM + isolated PostgreSQL fixture; Development/production financial databases were not queried.
- **Complete user-facing Persian answer:**

```text
[No answer returned by the pipeline.]
```
- **Trace:** [full query, SQL, params, rows, answer and provenance](../artifacts/monthly-production-sales-live-llm-postgresql-fifteen-cases-readiness-v2-2026-10-09.jsonl)

### monthly-13-production-vs-sales-growth — live fixture pipeline

- **Original Persian question:** آیا رشد تولید کگهر با رشد فروش آن هم‌جهت بوده است؟
- **Extracted intent:** expected measures=metric_monthly_production_quantity; selected measures=—; entity bindings company=—, product=—; operation=—/—/—; period=—.
- **Generated Query Plan:** measure=—; entities=—; period=—; aggregation=—; sort=—; pagination=—.
- **SQL execution:** No; PostgreSQL status=not executed; compiler diagnostics=—.
- **Expected result:** fixture oracle=—; expected growth=—; units interpreted from seeded fixture/source metadata: —.
- **Actual result:** values=—; comparison rows=—; observed rows=—; actual-vs-fixture exact numeric equality=—.
- **Unit correctness:** not evaluable (pipeline did not return a successful answer); expected render unit=—; observed/source units=—.
- **Persian answer correctness:** no answer; full returned answer follows.
- **Status:** UNSUPPORTED; failure stage=pipeline; reason=requirements_contain_unsupported_financial_value_operation.
- **Source provenance:** provider=—; report identity, source row fingerprints, parameterized SQL and selected semantic refs are in the linked trace artifact.
- **Execution mode:** Live LLM + isolated PostgreSQL fixture; Development/production financial databases were not queried.
- **Complete user-facing Persian answer:**

```text
[No answer returned by the pipeline.]
```
- **Trace:** [full query, SQL, params, rows, answer and provenance](../artifacts/monthly-production-sales-live-llm-postgresql-fifteen-cases-readiness-v2-2026-10-09.jsonl)

### producer-16-total-company-sales-ranking — live fixture pipeline

- **Original Persian question:** کدام شرکت‌های تولیدکننده گندله بیشترین فروش کل ماهانه را داشته‌اند؟
- **Extracted intent:** expected measures=metric_monthly_sales; selected measures=metric_monthly_sales; entity bindings company=—, product=Status=0; CompanyId=—; ProductKey=product_iron_ore_pellets; DisplayTitle=گندله سنگ‌آهن; Unit=—; IdentityProvenance=product-catalog-v1:owner-confirmed:feature141-product-equivalence:2026-10-09; CandidateProductKeys=—; DiagnosticCode=—; operation=DirectValueRetrieval/DirectValue/None; period=Policies=approved-observation-window; ComparisonScopes=—; SelectLatestByReportingPeriod=False; UseSynchronizationTimestampForRecency=False; ReportingPeriodStart=2025-10-09; ReportingPeriodEnd=2026-10-09; MaximumPeriods=12.
- **Generated Query Plan:** measure=metric_monthly_sales; entities={'EntityKind': 'Product', 'UserReference': 'گندله', 'ResolvedEntityId': None, 'ResolvedEntityKey': 'product_iron_ore_pellets', 'IdentityProvenance': 'product-catalog-v1:owner-confirmed:feature141-product-equivalence:2026-10-09', 'IdentityVerified': True}; period=Policies=approved-observation-window; ComparisonScopes=—; SelectLatestByReportingPeriod=False; UseSynchronizationTimestampForRecency=False; ReportingPeriodStart=2025-10-09; ReportingPeriodEnd=2026-10-09; MaximumPeriods=12; aggregation={'CatalogId': 'metric_monthly_sales', 'Operation': 1}; sort={'CatalogId': 'metric_monthly_sales', 'Direction': 1}; pagination=Offset=0; Limit=10.
- **SQL execution:** Yes; PostgreSQL status=Incomplete; compiler diagnostics=.
- **Expected result:** fixture oracle=—; expected growth=—; units interpreted from seeded fixture/source metadata: ton.
- **Actual result:** values=—; comparison rows=—; observed rows={'CompanyId': '5202a885-2afc-4939-a09d-f794c6bf058a', 'CompanyName': 'Dynamically discovered producer', 'CompanySymbol': 'NEWP', 'PeriodStart': '2026-08-23', 'PeriodEnd': '2026-09-22', 'Value': 900, 'SourceUnit': 'ton', 'UnitCompatibilityCount': 1, 'EligibleReportCount': 1, 'SamePeriodCandidateCount': 1, 'InvalidMultiplicityCount': 0, 'SourceMultiplicity': 1, 'ProviderName': 'NoavaranCurrentApi', 'ReportIdentity': ['12c924fe-24b5-40d3-9c62-c3d52267e6b2'], 'ExternalReportId': ['fixture-new-producer-shahrivar-1405'], 'SourceRowFingerprint': ['row-fixture-new-producer-shahrivar-1405-NoavaranCurrentApi:feature141-new-producer:TITLE:گندله|UNIT:ton'], 'ProductMappingCoverageStatus': 'partial', 'CompanyCoverageComplete': True}, {'CompanyId': '2a437020-35d6-4d57-b1bd-a209d7af06fb', 'CompanyName': 'Fixture Kegel', 'CompanySymbol': 'کگل', 'PeriodStart': '2026-08-23', 'PeriodEnd': '2026-09-22', 'Value': 850, 'SourceUnit': 'ton', 'UnitCompatibilityCount': 1, 'EligibleReportCount': 1, 'SamePeriodCandidateCount': 1, 'InvalidMultiplicityCount': 0, 'SourceMultiplicity': 1, 'ProviderName': 'NoavaranCurrentApi', 'ReportIdentity': ['23592725-e0f2-42ac-b903-9afe7ce426e3'], 'ExternalReportId': ['fixture-kgel-shahrivar-1405'], 'SourceRowFingerprint': ['row-fixture-kgel-shahrivar-1405-NoavaranCurrentApi:feature141-kgel:TITLE:کنسانتره|UNIT:ton', 'row-fixture-kgel-shahrivar-1405-NoavaranCurrentApi:feature141-kgel:TITLE:گندله|UNIT:ton'], 'ProductMappingCoverageStatus': 'partial', 'CompanyCoverageComplete': True}, {'CompanyId': 'f7a05252-afe0-4ddb-8537-8cc18da7a40b', 'CompanyName': 'Fixture Kogهر', 'CompanySymbol': 'کگهر', 'PeriodStart': '2026-08-23', 'PeriodEnd': '2026-09-22', 'Value': 700, 'SourceUnit': 'ton', 'UnitCompatibilityCount': 1, 'EligibleReportCount': 1, 'SamePeriodCandidateCount': 1, 'InvalidMultiplicityCount': 0, 'SourceMultiplicity': 1, 'ProviderName': 'NoavaranCurrentApi', 'ReportIdentity': ['17531772-5b8a-450f-ac1d-639c544007a9'], 'ExternalReportId': ['fixture-kgehr-shahrivar-1405'], 'SourceRowFingerprint': ['row-fixture-kgehr-shahrivar-1405-NoavaranCurrentApi:feature141-kgehr:TITLE:گندله|UNIT:ton'], 'ProductMappingCoverageStatus': 'partial', 'CompanyCoverageComplete': True}, {'CompanyId': 'a64bbbd9-7788-4bf0-a8c7-2ed304be5d68', 'CompanyName': 'Positive pellet producer without sales', 'CompanySymbol': 'NOSALES', 'PeriodStart': '2026-08-23', 'PeriodEnd': '2026-09-22', 'Value': 0, 'SourceUnit': 'ton', 'UnitCompatibilityCount': 1, 'EligibleReportCount': 1, 'SamePeriodCandidateCount': 1, 'InvalidMultiplicityCount': 0, 'SourceMultiplicity': 1, 'ProviderName': 'NoavaranCurrentApi', 'ReportIdentity': ['1a99773f-ab41-4bc3-9afc-ab3d63ff273f'], 'ExternalReportId': ['fixture-production-only-shahrivar-1405'], 'SourceRowFingerprint': ['row-fixture-production-only-shahrivar-1405-NoavaranCurrentApi:feature141-production-only:TITLE:گندله|UNIT:ton'], 'ProductMappingCoverageStatus': 'partial', 'CompanyCoverageComplete': True}; actual-vs-fixture exact numeric equality=—.
- **Unit correctness:** not evaluable (pipeline did not return a successful answer); expected render unit=میلیون ریال; observed/source units=ton.
- **Persian answer correctness:** Incomplete; full returned answer follows.
- **Status:** BLOCKED; failure stage=restricted_postgresql_execution; reason=PostgreSQL returned Incomplete; observed product mapping coverage=partial; the artifact exposes no more specific execution error code.
- **Source provenance:** provider=OpenAI; report identity, source row fingerprints, parameterized SQL and selected semantic refs are in the linked trace artifact.
- **Execution mode:** Live LLM + isolated PostgreSQL fixture; Development/production financial databases were not queried.
- **Complete user-facing Persian answer:**

```text
پوشش جمعیت تولیدکنندگان یا نگاشت محصول برای رتبه‌بندی کامل نیست؛ رتبهٔ مالی گزارش نمی‌شود.
```
- **Trace:** [full query, SQL, params, rows, answer and provenance](../artifacts/monthly-production-sales-live-llm-postgresql-fifteen-cases-readiness-v2-2026-10-09.jsonl)

## Security and release boundary

- Generic query routing in production and Development remains disabled; no production or Development financial DB credentials were used.
- The 50-question Navigator evaluation queried no database. The 15-case SQL cohort and discovery test used the isolated PostgreSQL cluster and restricted fixture role.
- No canonical SQL catalog approval was made. No new product equivalence was approved. The pellet alias decision was already explicitly owner-approved and remains unchanged.
- No primary financial metric was derived from unapproved inputs; unsupported formulas were rejected before SQL.
- The feature is not production-ready: measured holdout has 7/10 Navigator completions, development has 16/40; product/company metadata coverage is incomplete, arbitrary product-specific cross-company ranking remains partial, and financial source-item metadata continues to block selection.
