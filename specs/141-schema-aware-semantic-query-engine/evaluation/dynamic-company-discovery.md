# Feature 141 — Dynamic company discovery and Persian end-to-end evidence

Run date: 2026-10-09. This report supersedes the earlier PostgreSQL-unavailable note in `persian-end-to-end-examples.md` for the runs listed here. All financial values below come from synthetic isolated PostgreSQL fixtures, not Development or production data.

## Run summary

An opt-in live run started from the exact Persian query, called the configured OpenAI model (`gpt-5.6-luna`), navigated and planned the query, compiled parameterized PostgreSQL, executed with the restricted isolated-fixture principal, and generated a Persian answer. The test database was created independently; the unrestricted application Development connection was not used.

| Measure | Result |
|---|---:|
| Population query exact company-set precision / recall | 1.00 / 1.00 (3 expected, 3 returned) |
| Population query exact set | true |
| Population PostgreSQL execution | `Incomplete`, with explicit partial-coverage warning |
| Live LLM + PostgreSQL cases in the saved run | 9 total: 8 SQL executions, 1 planning failure |
| Database-query outcomes | 5 fully correct user-facing answers; 3 quantity answers have unresolved unit rendering; 1 cross-company ranking is Unsupported |
| Live LLM use across the 8-case v17 trace | 16 calls, 70,079 input tokens, 7,699 output tokens; 12,371 ms mean and 14,352 ms p95 per case (from trace run metadata) |
| Population case model use | 2 calls, 9,787 input tokens, 718 output tokens, 9,590 ms |
| Unit tests | 2,001 passed, 0 failed, 0 skipped |

The report has 16 distinct Persian prompts: 9 executed (including the population case), and 7 explicitly marked BLOCKED. Six were not invoked by the saved live evaluation. The tenth attempted prompt could not enter the pipeline because PostgreSQL could not bind a local TCP socket when the stopped fixture server was restarted. BLOCKED is not a claim that the engine returned a typed failure for those prompts.

## Trace artifacts and interpretation

- [Population live LLM + PostgreSQL artifact](../artifacts/monthly-company-population-discovery-live-llm-postgresql-v1.json) records the exact question, owner-approved product resolution, unbound plan entities, authorized scope, window, SQL and parameters, rows, independently seeded expected IDs, exact-set comparison, provenance, generated answer, and telemetry.
- [Eight-case live LLM + PostgreSQL JSONL](../artifacts/monthly-production-sales-live-llm-postgresql-eight-cases-v17-2026-10-09.jsonl) records each exact prompt, semantic measure references, plan and period, SQL/parameters when compiled, execution status, expected and actual values, final answer and telemetry. JSONL line numbers below are 1-based. The query-plan and SQL artifacts are retained as executed; diagnostics such as `offline_plan_execution_disabled` are plan metadata and do not mean those recorded cases skipped PostgreSQL.
- [Live reliability artifact](../artifacts/monthly-production-sales-live-llm-postgresql-reliability-v18-2026-10-09.json) records the additional explicit-month production test. It is a prior run for the same accepted value of 47, not an additional unique question in this 15-prompt list.

The 8-case trace used catalog hash `91bd31549418bdbd870b878079efa2808f9122d02cb7725aab5d4bab8595d1a2` and relationship `rel_31e410fb3ae11956`. The population artifact separately records product metadata version `product-catalog-v1`, concept `product_iron_ore_pellets`, owner approval `owner-confirmed:feature141-product-equivalence:2026-10-09`, provider `NoavaranCurrentApi`, and report/row source references.

## Executed Persian questions

### 1. Unbound pellet producer discovery

- **Original question:** کدام شرکت‌ها گندله تولید می‌کنند؟
- **Extracted intent:** monthly production quantity; company population discovered from eligible observations; product concept Iron Ore Pellets; no explicit month; positive production; discovery/list operation.
- **Generated plan:** `metric_monthly_production_quantity`; one verified Product entity (`product_iron_ore_pellets`); no Company entities or company IDs; authorized tenant scope `feature141-isolated-tenant-company-scope`; `observed-production-trailing-12-months-v1`, 2025-10-09 through 2026-10-09; partial coverage policy.
- **SQL execution:** Yes, restricted PostgreSQL; `postgresStatus=Incomplete`. Parameterized SQL joins `MonthlyReportLineItems → MonthlyReports → Companies`, applies accepted `ProductSales`, `OutputType=0`, positive production, exact approved aliases, authorized `@company_ids`, and the 12-month period. Full SQL and parameters are in the artifact.
- **Expected:** Independently seeded exact set: three fixture companies, with positive 25 ton, 47 ton, and 62 ton observations, each in 2026-08-23–2026-09-22. Fixture construction also includes a zero-production-only company that must be excluded.
- **Actual:** Those same three IDs and values; `exactCompanySetMatch=true`, so precision=recall=1.00. New producer `NEWP` appears without a company list in the question or planner. Returned companies: `NEWP` 62 ton, کگل 25 ton, کگهر 47 ton.
- **Status / failure reason:** **PASS with partial coverage disclosed**. No execution failure. Overall query status is `succeeded_with_partial_coverage`; PostgreSQL diagnostic `population_discovery_product_alias_coverage_incomplete`. The SQL mapping contains only owner-approved aliases, so this must not be described as exhaustive across unmapped provider identities.
- **Provenance:** `NoavaranCurrentApi`; reports `fixture-new-producer-shahrivar-1405`, `fixture-kgel-shahrivar-1405`, `fixture-kgehr-shahrivar-1405`; each source-row fingerprint is in `evidence[].SourceReferences` in the artifact.
- **Mode:** Live LLM + isolated PostgreSQL.
- **Complete user-facing Persian answer produced by the renderer:**

  > در گزارش‌های ماهانهٔ واجد شرایط بازهٔ ۲۰۲۵-۱۰-۰۹ تا ۲۰۲۶-۱۰-۰۹، تولید مثبت گندله برای شرکت‌های زیر مشاهده شد: Dynamically discovered producer (NEWP): تولید ثبت‌شدهٔ گندله ۶۲ تن در بازهٔ ۲۰۲۶-۰۸-۲۳ تا ۲۰۲۶-۰۹-۲۲؛
  > منبع: NoavaranCurrentApi، گزارش fixture-new-producer-shahrivar-1405؛ Fixture Kegel (کگل): تولید ثبت‌شدهٔ گندله ۲۵ تن در بازهٔ ۲۰۲۶-۰۸-۲۳ تا ۲۰۲۶-۰۹-۲۲؛
  > منبع: NoavaranCurrentApi، گزارش fixture-kgel-shahrivar-1405؛ Fixture Kogهر (کگهر): تولید ثبت‌شدهٔ گندله ۴۷ تن در بازهٔ ۲۰۲۶-۰۸-۲۳ تا ۲۰۲۶-۰۹-۲۲؛
  > منبع: NoavaranCurrentApi، گزارش fixture-kgehr-shahrivar-1405. این فهرست جزئی است: پوشش دسترسی به شرکت‌ها یا نگاشت محصول کامل نیست و از آن نمی‌توان نتیجه گرفت که شرکت‌های فهرست‌نشده تولید نداشته‌اند.

  This answer is copied from the saved artifact. Fixture labels are intentionally visible; these are not real issuer results.

### 2. Explicit-month production quantity

- **Original question:** مقدار تولید گندله کگهر در شهریور ۱۴۰۵ چقدر بوده؟
- **Intent / plan:** `metric_monthly_production_quantity`; company کگهر; company-scoped pellet identity; explicit Shahrivar 1405 interval 2026-08-23–2026-09-22; direct scalar retrieval.
- **SQL execution:** Yes; `Succeeded` (JSONL line 1, case `monthly-02-production-product-month`).
- **Expected / actual:** 47 ton / 47 (fixture independently seeded). The raw actual is 47, but the final renderer emitted `source-quantity-unit` instead of `ton`; numeric evidence passed, user-facing unit failed.
- **Status / failure:** **FAIL** at Persian response unit rendering; not a SQL failure.
- **Provenance:** `NoavaranCurrentApi`; `fixture-kgehr-shahrivar-1405`; row fingerprint in JSONL line 1 `observedRows`/evidence.
- **Mode:** Live LLM + isolated PostgreSQL. **Answer actually returned:** «کگهر گندله در بازهٔ ۲۰۲۶-۰۸-۲۳ تا ۲۰۲۶-۰۹-۲۲، مقدار تولید ماهانه: ۴۷ source-quantity-unit. منبع: NoavaranCurrentApi، گزارش fixture-kgehr-shahrivar-1405.»

### 3. Month-over-month production growth

- **Original question:** تولید کنسانتره کگل نسبت به ماه قبل چند درصد رشد کرده؟
- **Intent / plan:** production quantity; کگل; کنسانتره; latest two eligible periods; `ApprovedPeriodComparison`, `MonthOverMonth`.
- **SQL execution:** Yes; `Succeeded` (line 2, `monthly-03-production-mom-growth`).
- **Expected / actual:** 6 to 12 ton means 100%; SQL returned 12 and 100.00000000000000000000%. Fixture expected 100%.
- **Status / failure:** **FAIL** at Persian response rendering; the numeric comparison is correct but the quantity unit is emitted as the placeholder `source-quantity-unit` for both periods.
- **Provenance:** `NoavaranCurrentApi`; current report `fixture-kgel-shahrivar-1405`; prior/current period values are in line 2; current report identity and source fingerprint are retained there.
- **Mode:** Live LLM + isolated PostgreSQL. **Answer actually returned:** «کگل کنسانتره، مقدار تولید ماهانه در دورهٔ جاری ۱۲ source-quantity-unit و در دورهٔ مبنا ۶ source-quantity-unit بوده است؛ مقدار ثبت‌شده برای تغییر درصدی: ۱۰۰ درصد. منبع: NoavaranCurrentApi، گزارش fixture-kgel-shahrivar-1405.»

### 4. Product ranking by production growth

- **Original question:** کدام محصولات کگهر در سه ماه اخیر بیشترین رشد تولید را داشته‌اند؟
- **Intent / plan:** production quantity; company کگهر; rank products by month-over-month growth over up to three periods; descending ranked table.
- **SQL execution:** Yes; `Succeeded` (line 3, `monthly-04-production-top-products-growth`).
- **Expected / actual:** گندله 35→47 ton gives 34.2857142857…%; SQL result 34.28571428571428571400%, within 0.0001 percentage-point fixture tolerance.
- **Status / failure:** **PASS** for ranking value; no failure. This ranks products within a specified company, not companies in an unbound population.
- **Provenance:** `NoavaranCurrentApi`; current report `fixture-kgehr-shahrivar-1405`; report ID and row fingerprint are in line 3.
- **Mode:** Live LLM + isolated PostgreSQL. **Answer:** «رتبه‌بندی بر اساس رشد مقدار تولید ماهانه: رتبهٔ ۱: کگهر گندله، ۳۴٫۲۸۵۷۱۴۲۸۵۷۱۴۲۸۵۷۱۴ درصد. منبع: NoavaranCurrentApi، گزارش fixture-kgehr-shahrivar-1405.»

### 5. Monthly sales amount

- **Original question:** فروش ریالی کگل در شهریور ۱۴۰۵ چقدر بوده؟
- **Intent / plan:** monthly sales amount; کگل; explicit Shahrivar 1405; company total with approved `SUM`; unit Million IRR.
- **SQL execution:** Yes; `Succeeded` (line 4, `monthly-05-sales-amount-month`).
- **Expected / actual:** 850 / 850 million IRR, fixture sum 450+400.
- **Status / failure:** **PASS**.
- **Provenance:** `NoavaranCurrentApi`; `fixture-kgel-shahrivar-1405`; source fingerprints for eligible lines are in JSONL line 4.
- **Mode:** Live LLM + isolated PostgreSQL. **Answer:** «کگل در بازهٔ ۲۰۲۶-۰۸-۲۳ تا ۲۰۲۶-۰۹-۲۲، مبلغ فروش ماهانه: ۸۵۰ میلیون ریال. منبع: NoavaranCurrentApi، گزارش fixture-kgel-shahrivar-1405.»

### 6. Latest sales quantity for a product

- **Original question:** مقدار فروش گندله کگهر در آخرین گزارش ماهانه چقدر است؟
- **Intent / plan:** monthly sales quantity; کگهر; گندله; latest eligible period; scalar value.
- **SQL execution:** Yes; `Succeeded` (line 5, `monthly-06-sales-quantity-product-latest`).
- **Expected / actual:** 40 ton / 40. Actual SQL value matches fixture, but renderer emits unresolved `source-quantity-unit`.
- **Status / failure:** **FAIL** at response unit rendering.
- **Provenance:** `NoavaranCurrentApi`; `fixture-kgehr-shahrivar-1405`; line 5 includes the report and source-row fingerprint.
- **Mode:** Live LLM + isolated PostgreSQL. **Answer actually returned:** «کگهر گندله در بازهٔ ۲۰۲۶-۰۸-۲۳ تا ۲۰۲۶-۰۹-۲۲، مقدار فروش ماهانه: ۴۰ source-quantity-unit. منبع: NoavaranCurrentApi، گزارش fixture-kgehr-shahrivar-1405.»

### 7. Six-month sales series

- **Original question:** فروش ریالی کگل در شش ماه گذشته چه روندی داشته؟
- **Intent / plan:** monthly sales amount; کگل; six latest periods; ascending time series; Million IRR.
- **SQL execution:** Yes; `Succeeded` (line 6, `monthly-08-sales-six-month-trend`).
- **Expected / actual:** `[120, 150, 200, 250, 300, 850]` / `[120, 150, 200, 250, 300, 850]` million IRR.
- **Status / failure:** **PASS**.
- **Provenance:** `NoavaranCurrentApi`; reports `fixture-kgel-trend-2` through `fixture-kgel-trend-6` and `fixture-kgel-shahrivar-1405`; IDs and date ranges are retained in line 6.
- **Mode:** Live LLM + isolated PostgreSQL. Complete generated answer is line 6 `finalPersianAnswer` in the JSONL artifact; it enumerates every period, value, provider, and report.

### 8. Cross-company producer ranking by sales

- **Original question:** کدام شرکت‌های تولیدکننده گندله بیشترین مقدار فروش را داشته‌اند؟
- **Intent / candidate plan:** rank an unbound producer population by pellet sales quantity; latest period.
- **SQL execution:** No; rejected at planning validation before compilation.
- **Expected / actual:** No independent numeric expectation was assigned; no SQL rows or result were returned. Actual typed failure is `monthly_product_binding_required` (line 7, case `monthly-09-cross-company-product-sales`).
- **Status / failure:** **UNSUPPORTED**, stage `planning_validation`. The pipeline does not yet compose producer population discovery with sales ranking.
- **Provenance:** None; no report was queried.
- **Mode:** Live LLM navigation/planning; no PostgreSQL query. **Answer:** «این درخواست با تعریف‌های تأییدشدهٔ پرس‌وجو قابل اجرا نیست.»

### 9. Two-company sales comparison

- **Original question:** فروش ماهانه کگل و کگهر را با هم مقایسه کن.
- **Intent / plan:** monthly sales amount; explicitly bound companies کگل and کگهر; company-level sum; latest common period; two-company comparison.
- **SQL execution:** Yes; `Succeeded` (line 8, `monthly-10-company-sales-comparison`).
- **Expected / actual:** Shahrivar 1405 (2026-08-23–2026-09-22): کگل 850, کگهر 700 million IRR / same returned. Exact values compared by company identity rather than row order.
- **Status / failure:** **PASS**.
- **Provenance:** `NoavaranCurrentApi`; `fixture-kgel-shahrivar-1405`, `fixture-kgehr-shahrivar-1405`; report IDs and fingerprints in line 8.
- **Mode:** Live LLM + isolated PostgreSQL. **Answer:** «مقایسهٔ مبلغ فروش ماهانه: کگل ۸۵۰ میلیون ریال (در بازهٔ ۲۰۲۶-۰۸-۲۳ تا ۲۰۲۶-۰۹-۲۲)؛ منبع: NoavaranCurrentApi، گزارش fixture-kgel-shahrivar-1405؛ کگهر ۷۰۰ میلیون ریال (در همان بازه)؛ منبع: NoavaranCurrentApi، گزارش fixture-kgehr-shahrivar-1405.»

## Seven requested Persian cases blocked or not executed

These cases have no successful pipeline execution to report. Case 10 was attempted as an additional live run but PostgreSQL startup failed before navigation; cases 11–16 were not submitted in the saved live evaluation. There is consequently no extracted LLM intent, generated plan, SQL, expected numeric result, or actual pipeline answer for them. The six unsubmitted cases are **BLOCKED** at evaluation scheduling, not pipeline-stage results. The pipeline was tested with seeded cases for zero-production exclusion, dynamically inserted producer inclusion, explicit month selection, alias resolution, and authorization scoping; those are covered by integration assertions, not represented below as live Persian answers.

### 10. Additional live lookup blocked by PostgreSQL startup

- **Original Persian question:** مقدار تولید کنسانتره کگل در شهریور ۱۴۰۵ چقدر بوده؟
- **Intent / plan:** Not extracted; PostgreSQL integration gate stopped the test before query navigation.
- **SQL execution:** No.
- **Expected / actual:** Fixture seed expectation was 12 ton; no actual pipeline result exists.
- **Status / failure:** **BLOCKED**, stage `PostgreSQL fixture startup`; `pg_ctl` failed with `FATAL: could not create any TCP/IP sockets` / `Permission denied` on loopback binding. No financial answer was generated.
- **Provenance:** Fixture seed intended `NoavaranCurrentApi`, report `fixture-kgel-shahrivar-1405`; not queried in this attempt.
- **Mode:** Not executed; no LLM calls.

### 11. Explicit-month production-only discovery

- **Original Persian question:** کدام شرکت‌ها در شهریور ۱۴۰۵ تولید گندله داشته‌اند؟
- **Intent / plan:** Not extracted/generated.
- **SQL execution:** No.
- **Expected / actual:** Fixture intent is to use only positive observations for the requested period, but no independent case expectation or actual answer was recorded for this prompt.
- **Status / failure:** **BLOCKED**, not executed; no failing pipeline stage.
- **Provenance:** None.
- **Mode:** Not executed.

### 12. Positive production filter

- **Original Persian question:** کدام شرکت‌ها در شهریور ۱۴۰۵ تولید گندله بیشتر از صفر داشته‌اند؟
- **Intent / plan:** Not extracted/generated.
- **SQL execution:** No.
- **Expected / actual:** Not evaluated; no values claimed.
- **Status / failure:** **BLOCKED**, not executed; no failing pipeline stage.
- **Provenance:** None.
- **Mode:** Not executed.

### 13. Dynamic discovery with another approved product

- **Original Persian question:** کدام شرکت‌ها کنسانتره تولید می‌کنند؟
- **Intent / plan:** Not extracted/generated.
- **SQL execution:** No.
- **Expected / actual:** Not evaluated; no values claimed. Current owner approval only records equivalence for the pellet labels.
- **Status / failure:** **BLOCKED**, not executed; general cross-product discovery was not part of this completed live run.
- **Provenance:** None.
- **Mode:** Not executed.

### 14. Three-month pellet-production history

- **Original Persian question:** کدام شرکت‌ها در سه ماه گذشته سابقهٔ تولید گندله داشته‌اند؟
- **Intent / plan:** Not extracted/generated.
- **SQL execution:** No new execution.
- **Expected / actual:** Not evaluated; no independent company set or result was recorded for this historical-window question.
- **Status / failure:** **BLOCKED**, not executed; no failing pipeline stage.
- **Provenance:** None.
- **Mode:** Not executed.

### 15. Unknown/unreviewed product alias

- **Original Persian question:** کدام شرکت‌ها محصول «گندله ویژه» تولید می‌کنند؟
- **Intent / plan:** Not extracted/generated.
- **SQL execution:** No.
- **Expected / actual:** No result claimed. The alias is not in the owner-approved product metadata.
- **Status / failure:** **BLOCKED**, not executed. Expected safe behavior is unresolved/unsupported, but this expected behavior is not counted as an observed result.
- **Provenance:** None.
- **Mode:** Not executed.

### 16. Unauthorized population request

- **Original Persian question:** از شرکت‌هایی که به آن‌ها دسترسی ندارم، کدام‌ها گندله تولید می‌کنند؟
- **Intent / plan:** No separate unauthorized-tenant run was performed; case 1 used the fixture's explicitly approved scope.
- **SQL execution:** No unauthorized query was issued.
- **Expected / actual:** Not evaluated; no inaccessible company identities or counts disclosed.
- **Status / failure:** **BLOCKED**, not executed. Authorization fail-closed behavior is covered by deterministic/compiler integration tests, not asserted as a live LLM case.
- **Provenance:** None.
- **Mode:** Not executed.

## Deterministic PostgreSQL integration coverage

The integration suite verifies both aliases map to the same versioned concept; multiple companies map to that concept through approved provider product identities; a newly inserted producer is discovered without a planner source edit or company IDs in the plan; zero-only/no-positive-production companies are excluded; explicit reporting month is enforced; only the approved tenant company scope is returned; and incomplete product mapping is surfaced as `Incomplete`. These assertions use fixture seed truth and PostgreSQL, but are not live Persian LLM test cases. See `tests/FinancialCopilot.IntegrationTests/MonthlyStructuredQueryPostgreSqlTests.cs` and the population artifact above.

## Remaining capability and quality gaps

- Cross-company discovery is implemented for verified positive monthly production observations; generic Boolean predicates, industry/category membership, numeric sales thresholds, multi-period continuous-growth filters, and company-level top-N ranking are not delivered by this run.
- Population response correctly discloses incomplete product mapping. `appsettings.json` has no production `CompanyDiscoveryScopes` entry, so an unconfigured production tenant remains fail-closed.
- Quantity response rendering still exposes the placeholder `source-quantity-unit` for two quantity cases. Treat them as numeric SQL successes but user-facing failures until the row's verified source unit is rendered.
- The unbound product resolver is deliberately limited to approved aliases. The artifact's `CompanyCoverageComplete=true` describes the authorized fixture scope; it does not override incomplete global product-alias coverage.
- No live Development financial data was queried; all reported numbers, names, tickers, and report IDs are synthetic fixture data.
