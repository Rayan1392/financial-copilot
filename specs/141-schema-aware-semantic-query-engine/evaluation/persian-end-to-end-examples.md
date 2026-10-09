# Feature 141 Persian end-to-end examples - fresh 2026-10-09 run

## Run summary

A fresh live LLM + isolated PostgreSQL evaluation ran **16 distinct Persian questions**: the 15-question matrix in the v19 JSONL and a separate producer-population discovery request. Each prompt received fresh LLM inference. SQL used the test-owned PostgreSQL 17 cluster and restricted read role. No Development or production financial database was queried. Every number is synthetic fixture data.

The disposable cluster used a dynamically selected loopback port. The wrapper verified the test-owned server stopped, checked for any process referencing its unique data directory, and removed that scoped directory. A separately retained directory from an earlier failed run remains because the Windows command policy blocked its removal; its narrow cleanup procedure is below.

**Trace files:** [15-case live matrix](../artifacts/monthly-production-sales-live-llm-postgresql-fifteen-cases-v19-2026-10-09.jsonl), [population discovery](../artifacts/monthly-company-population-discovery-live-llm-postgresql-v1.json), and [PostgreSQL integration assertions](../../../tests/FinancialCopilot.IntegrationTests/MonthlyStructuredQueryPostgreSqlTests.cs).

| Measure | Result |
|---|---:|
| Distinct Persian questions / fresh LLM prompts | 16 / 16 |
| Cases reaching PostgreSQL | 12 matrix questions plus population discovery (13 total) |
| Fully answered SQL cases | 10 |
| SQL cases classified Incomplete for partial coverage | 3 (2 rankings and population discovery) |
| Ambiguous or unsupported before PostgreSQL | 3 |
| PostgreSQL integration | PASS; test-owned cluster stopped and removed |
| Unit tests | 2,018 passed; 0 failed; 0 skipped |
| Release build | PASS; 0 warnings, 0 errors |
| Semantic Catalog validation | PASS |

Population precision/recall is **1.00/1.00** against four authorized fixture producers: NEWP (62 ton), کگهر (47 ton), کگل (25 ton), and NOSALES (80 ton with zero sales). The zero-production company and an unauthorized positive producer are excluded. Mapping coverage is partial, so answers disclose the limitation.

## Producer population discovery

- **Original Persian question (exact LLM input):** کدام شرکت‌ها گندله تولید می‌کنند؟
- **Extracted intent:** positive monthly production quantity; canonical concept `product_iron_ore_pellets` (گندله سنگ‌آهن); no company IDs in the question; dynamic population/list operation; trailing observation window `2025-10-09 to 2026-10-09`.
- **Generated query plan:** measures `["metric_monthly_production_quantity"]`; aliases `["گندله","گندله سنگ‌آهن"]`; SQL discovers company identities from positive accepted production rows; no producer registry; authorization scope `{"TenantId":"5f655e78-07c5-4805-a757-a8d1927ab1d2","ScopeId":"feature141-isolated-tenant-company-scope","ApprovalReference":"owner-approved-isolated-fixture-scope","CompanyCoverageComplete":true}`.
- **Selected semantic references:** source catalog IDs `["metric_monthly_production_quantity"]`; relationship IDs `["rel_31e410fb3ae11956","rel_4e54d40aee8b45e4"]`; physical object IDs `["tbl_8ecd0432a4292998_MonthlyReportId","tbl_8e759d70dada6bd8_Id","tbl_8e759d70dada6bd8_CompanyId","tbl_8646d83cdb350f92_Id","tbl_8ecd0432a4292998_ProductionQuantity","tbl_8ecd0432a4292998_ProductCode","tbl_8ecd0432a4292998_Title","tbl_8ecd0432a4292998_Unit","tbl_8e759d70dada6bd8_PeriodStart","tbl_8e759d70dada6bd8_PeriodEnd","tbl_8e759d70dada6bd8_ProviderName","tbl_8e759d70dada6bd8_ReportType","tbl_8e759d70dada6bd8_OutputType","tbl_8e759d70dada6bd8_IsAccepted","tbl_8e759d70dada6bd8_RevisionStatus","tbl_8e759d70dada6bd8_ExternalReportId","tbl_8ecd0432a4292998_SourceRowFingerprint","tbl_8ecd0432a4292998_SourceMultiplicity","tbl_8646d83cdb350f92_Name","tbl_8ecd0432a4292998","tbl_8e759d70dada6bd8","tbl_8646d83cdb350f92"]`.
- **SQL execution:** Yes; PostgreSQL status `Incomplete`, diagnostic `population_discovery_product_alias_coverage_incomplete`. SQL and parameters are preserved in the artifact.
- **Expected fixture result:** four authorized positive pellet producers in `2026-08-23..2026-09-22`: 62, 47, 25, and 80 ton. The zero-production and unauthorized companies must be absent. Expected IDs `["294fb8c6-5d8b-4e16-a48e-45c4b3102ce9","a1ac18b2-29c9-4ed6-9f41-6e284573005c","e315390b-489c-41c9-b27f-191602099080","e447619e-60bf-40f9-a1cd-6fd352af5a09"]`.
- **Actual result:** 4 rows; exact set match `True`; includes newly inserted NEWP and the positive producer with zero sales; excludes unauthorized and zero-production companies. Executor status `Incomplete`.
- **Unit correctness:** PASS; each company retains its persisted positive quantity unit, and this fixture reports tons. No unit is inferred from product names or converted.
- **Status:** **PASS WITH PARTIAL COVERAGE DISCLOSED**. No failure; product alias coverage is not exhaustive.
- **Source provenance:** NoavaranCurrentApi; per-row report identities and source fingerprints are in the trace.
- **Execution mode:** live LLM + isolated PostgreSQL (provider/model `OpenAI` / `gpt-5.6-luna`; `2` model calls).
- **Complete user-facing Persian answer from the renderer:**

> در گزارش‌های ماهانهٔ واجد شرایط بازهٔ ۲۰۲۵-۱۰-۰۹ تا ۲۰۲۶-۱۰-۰۹، تولید مثبت گندله برای شرکت‌های زیر مشاهده شد: Dynamically discovered producer (NEWP): تولید ثبت‌شدهٔ گندله ۶۲ تن در بازهٔ ۲۰۲۶-۰۸-۲۳ تا ۲۰۲۶-۰۹-۲۲؛ 
> منبع: NoavaranCurrentApi، گزارش fixture-new-producer-shahrivar-1405؛ Fixture Kegel (کگل): تولید ثبت‌شدهٔ گندله ۲۵ تن در بازهٔ ۲۰۲۶-۰۸-۲۳ تا ۲۰۲۶-۰۹-۲۲؛ 
> منبع: NoavaranCurrentApi، گزارش fixture-kgel-shahrivar-1405؛ Fixture Kogهر (کگهر): تولید ثبت‌شدهٔ گندله ۴۷ تن در بازهٔ ۲۰۲۶-۰۸-۲۳ تا ۲۰۲۶-۰۹-۲۲؛ 
> منبع: NoavaranCurrentApi، گزارش fixture-kgehr-shahrivar-1405؛ Positive pellet producer without sales (NOSALES): تولید ثبت‌شدهٔ گندله ۸۰ تن در بازهٔ ۲۰۲۶-۰۸-۲۳ تا ۲۰۲۶-۰۹-۲۲؛ 
> منبع: NoavaranCurrentApi، گزارش fixture-production-only-shahrivar-1405. این فهرست جزئی است: پوشش دسترسی به شرکت‌ها یا نگاشت محصول کامل نیست و از آن نمی‌توان نتیجه گرفت که شرکت‌های فهرست‌نشده تولید نداشته‌اند.
- **Trace artifact:** [`monthly-company-population-discovery-live-llm-postgresql-v1.json`](../artifacts/monthly-company-population-discovery-live-llm-postgresql-v1.json).

## Older stopped test directory

The earlier failed run left this exact temporary path: `C:\Users\free\AppData\Local\Temp\feature141-postgres-272b4d872a304f4bb2c8eb6229eb33be`. Its recorded PostgreSQL status was stopped and no `postmaster.pid` remained. The current attempt to remove this exact path was blocked by Windows command policy before deletion. To clean only that directory manually, confirm `pg_ctl status` reports it stopped, then run:

```powershell
$cluster = 'C:\Users\free\AppData\Local\Temp\feature141-postgres-272b4d872a304f4bb2c8eb6229eb33be'
& 'D:\Program Files\PostgreSQL\17\bin\pg_ctl.exe' -D (Join-Path $cluster 'data') status
# Continue only if pg_ctl reports that this cluster is not running.
Remove-Item -LiteralPath $cluster -Recurse -Force
```

## Fifteen-question live LLM matrix

### 2. monthly-02-production-product-month

- **Original Persian question (exact LLM input):** مقدار تولید گندله کگهر در شهریور ۱۴۰۵ چقدر بوده؟
- **Extracted intent:** measures `metric_monthly_production_quantity` (monthly production quantity); company کگهر; product گندله; period 2026-08-23 to 2026-09-22; operation `DirectValue` / comparison `None`.
- **Generated query plan:** `{"measures":["metric_monthly_production_quantity"],"entities":[{"EntityKind":"Company","UserReference":"کگهر","ResolvedEntityId":"e447619e-60bf-40f9-a1cd-6fd352af5a09","IdentityProvenance":"exact_ticker","IdentityVerified":true},{"EntityKind":"Product","UserReference":"گندله","ResolvedEntityKey":"NoavaranCurrentApi:feature141-kgehr:TITLE:گندله|UNIT:ton","IdentityProvenance":"feature-129-company-scoped-product-row","IdentityVerified":true}],"operation":"DirectValue","comparison":"None","period":"2026-08-23 to 2026-09-22","aggregation":[],"sort":[],"presentation":0,"filters":[],"populationSalesScope":null,"productConcept":null,"companyDiscoveryScope":null}`
- **Selected semantic references:** Selected semantic references: [{"ReferenceType":3,"CatalogId":"metric_monthly_production_quantity","SourceItemCatalogId":null,"ProviderName":null,"StatementType":null,"SourceItemId":null,"MetricCode":"metric_monthly_production_quantity","Unit":null,"UnitStatus":null,"IdentityStatus":"catalog-mapped","SemanticReviewStatus":"pending-or-unresolved","Queryable":false}]. Relationships: ["rel_31e410fb3ae11956"]. Product binding: {"Status":0,"CompanyId":"e447619e-60bf-40f9-a1cd-6fd352af5a09","ProductKey":"NoavaranCurrentApi:feature141-kgehr:TITLE:گندله|UNIT:ton","DisplayTitle":"گندله","Unit":"ton","IdentityProvenance":"feature-129-company-scoped-product-row","CandidateProductKeys":[],"DiagnosticCode":null}. Company binding: {"Status":0,"CompanyId":"e447619e-60bf-40f9-a1cd-6fd352af5a09","CandidateCompanyIds":["e447619e-60bf-40f9-a1cd-6fd352af5a09"],"IdentityProvenance":"exact_ticker","DiagnosticCode":null}.
- **SQL execution:** Yes; PostgreSQL was queried and returned Succeeded. SQL and parameters are preserved in the JSONL trace.
- **Unit correctness:** PASS; persisted source unit `ton` is preserved as its governed Persian display label.
- **Expected fixture result, independently verified:** Fixture expectation: [47] ton; period 2026-08-23 to 2026-09-22.
- **Actual result:** PostgreSQL Succeeded; observed Value rows [47] ton; periods 2026-08-23..2026-09-22. Pipeline status `succeeded`; response status `Answered`.
- **Status:** **PASS**. No failure. Validation diagnostics: `["offline_plan_execution_disabled"]`.
- **Source provenance:** providers=NoavaranCurrentApi; report identities=9ff6e9d5-46e4-400c-97be-43bd55f85f5e, fixture-kgehr-shahrivar-1405; source references=row-fixture-kgehr-shahrivar-1405-NoavaranCurrentApi:feature141-kgehr:TITLE:گندله|UNIT:ton
- **Execution mode:** live LLM + isolated PostgreSQL fixture (provider/model `OpenAI` / `gpt-5.6-luna`; 2 model calls).
- **Complete user-facing Persian answer:**

> کگهر گندله در بازهٔ ۲۰۲۶-۰۸-۲۳ تا ۲۰۲۶-۰۹-۲۲، مقدار تولید ماهانه: ۴۷ تن.
> منبع: NoavaranCurrentApi، گزارش fixture-kgehr-shahrivar-1405.
- **Trace artifact:** [`monthly-production-sales-live-llm-postgresql-fifteen-cases-v19-2026-10-09.jsonl`, JSONL line 1](../artifacts/monthly-production-sales-live-llm-postgresql-fifteen-cases-v19-2026-10-09.jsonl#L1). The entry retains exact question, plan, SQL/parameters when compiled, execution status and rows, provenance, and answer.
### 3. monthly-03-production-mom-growth

- **Original Persian question (exact LLM input):** تولید کنسانتره کگل نسبت به ماه قبل چند درصد رشد کرده؟
- **Extracted intent:** measures `metric_monthly_production_quantity` (monthly production quantity); company کگل; product کنسانتره; period latest/approved observation window; operation `GrowthPercentage` / comparison `MonthOverMonth`.
- **Generated query plan:** `{"measures":["metric_monthly_production_quantity"],"entities":[{"EntityKind":"Company","UserReference":"کگل","ResolvedEntityId":"e315390b-489c-41c9-b27f-191602099080","IdentityProvenance":"exact_ticker","IdentityVerified":true},{"EntityKind":"Product","UserReference":"کنسانتره","ResolvedEntityKey":"NoavaranCurrentApi:feature141-kgel:TITLE:کنسانتره|UNIT:ton","IdentityProvenance":"feature-129-company-scoped-product-row","IdentityVerified":true}],"operation":"GrowthPercentage","comparison":"MonthOverMonth","period":"latest/approved observation window","aggregation":[],"sort":[],"presentation":0,"filters":[],"populationSalesScope":null,"productConcept":null,"companyDiscoveryScope":null}`
- **Selected semantic references:** Selected semantic references: [{"ReferenceType":3,"CatalogId":"metric_monthly_production_quantity","SourceItemCatalogId":null,"ProviderName":null,"StatementType":null,"SourceItemId":null,"MetricCode":"metric_monthly_production_quantity","Unit":null,"UnitStatus":null,"IdentityStatus":"catalog-mapped","SemanticReviewStatus":"pending-or-unresolved","Queryable":false}]. Relationships: ["rel_31e410fb3ae11956"]. Product binding: {"Status":0,"CompanyId":"e315390b-489c-41c9-b27f-191602099080","ProductKey":"NoavaranCurrentApi:feature141-kgel:TITLE:کنسانتره|UNIT:ton","DisplayTitle":"کنسانتره","Unit":"ton","IdentityProvenance":"feature-129-company-scoped-product-row","CandidateProductKeys":[],"DiagnosticCode":null}. Company binding: {"Status":0,"CompanyId":"e315390b-489c-41c9-b27f-191602099080","CandidateCompanyIds":["e315390b-489c-41c9-b27f-191602099080"],"IdentityProvenance":"exact_ticker","DiagnosticCode":null}.
- **SQL execution:** Yes; PostgreSQL was queried and returned Succeeded. SQL and parameters are preserved in the JSONL trace.
- **Unit correctness:** PASS; comparison result is rendered in its result unit (percent/absolute change); no implicit quantity conversion is applied.
- **Expected fixture result, independently verified:** Seeded current and prior production are 12 and 6 ton; independently expected growth is 100 percent.
- **Actual result:** PostgreSQL Succeeded; observed Value rows [12] ton; periods 2026-08-23..2026-09-22. Pipeline status `succeeded`; response status `Answered`.
- **Status:** **PASS**. No failure. Validation diagnostics: `["offline_plan_execution_disabled"]`.
- **Source provenance:** providers=NoavaranCurrentApi; report identities=fixture-kgel-shahrivar-1405; source references=row-fixture-kgel-shahrivar-1405-NoavaranCurrentApi:feature141-kgel:TITLE:کنسانتره|UNIT:ton
- **Execution mode:** live LLM + isolated PostgreSQL fixture (provider/model `OpenAI` / `gpt-5.6-luna`; 2 model calls).
- **Complete user-facing Persian answer:**

> کگل کنسانتره، مقدار تولید ماهانه در دورهٔ جاری ۱۲ تن و در دورهٔ مبنا ۶ تن بوده است؛ مقدار ثبت‌شده برای تغییر درصدی: ۱۰۰ درصد.
> منبع: NoavaranCurrentApi، گزارش fixture-kgel-shahrivar-1405.
- **Trace artifact:** [`monthly-production-sales-live-llm-postgresql-fifteen-cases-v19-2026-10-09.jsonl`, JSONL line 2](../artifacts/monthly-production-sales-live-llm-postgresql-fifteen-cases-v19-2026-10-09.jsonl#L2). The entry retains exact question, plan, SQL/parameters when compiled, execution status and rows, provenance, and answer.
### 4. monthly-04-production-top-products-growth

- **Original Persian question (exact LLM input):** کدام محصولات کگهر در سه ماه اخیر بیشترین رشد تولید را داشته‌اند؟
- **Extracted intent:** measures `metric_monthly_production_quantity` (monthly production quantity); company کگهر; product none; period latest/approved observation window; operation `GrowthPercentage` / comparison `MonthOverMonth`.
- **Generated query plan:** `{"measures":["metric_monthly_production_quantity"],"entities":[{"EntityKind":"Company","UserReference":"کگهر","ResolvedEntityId":"e447619e-60bf-40f9-a1cd-6fd352af5a09","IdentityProvenance":"exact_ticker","IdentityVerified":true}],"operation":"GrowthPercentage","comparison":"MonthOverMonth","period":"latest/approved observation window","aggregation":[{"CatalogId":"metric_monthly_production_quantity","Operation":1}],"sort":[{"CatalogId":"metric_monthly_production_quantity","Direction":1}],"presentation":4,"filters":[],"populationSalesScope":null,"productConcept":null,"companyDiscoveryScope":null}`
- **Selected semantic references:** Selected semantic references: [{"ReferenceType":3,"CatalogId":"metric_monthly_production_quantity","SourceItemCatalogId":null,"ProviderName":null,"StatementType":null,"SourceItemId":null,"MetricCode":"metric_monthly_production_quantity","Unit":null,"UnitStatus":null,"IdentityStatus":"catalog-mapped","SemanticReviewStatus":"pending-or-unresolved","Queryable":false}]. Relationships: ["rel_31e410fb3ae11956"]. Company binding: {"Status":0,"CompanyId":"e447619e-60bf-40f9-a1cd-6fd352af5a09","CandidateCompanyIds":["e447619e-60bf-40f9-a1cd-6fd352af5a09"],"IdentityProvenance":"exact_ticker","DiagnosticCode":null}.
- **SQL execution:** Yes; PostgreSQL was queried and returned Succeeded. SQL and parameters are preserved in the JSONL trace.
- **Unit correctness:** PASS; comparison result is rendered in its result unit (percent/absolute change); no implicit quantity conversion is applied.
- **Expected fixture result, independently verified:** Seeded comparable production is 47 ton currently and 35 ton previously; expected growth is (47-35)/35 = 34.285714285714285 percent.
- **Actual result:** PostgreSQL Succeeded; observed Value rows [47] ton; periods 2026-08-23..2026-09-22. Pipeline status `succeeded`; response status `Answered`.
- **Status:** **PASS**. No failure. Validation diagnostics: `["offline_plan_execution_disabled"]`.
- **Source provenance:** providers=NoavaranCurrentApi; report identities=fixture-kgehr-shahrivar-1405; source references=row-fixture-kgehr-shahrivar-1405-NoavaranCurrentApi:feature141-kgehr:TITLE:گندله|UNIT:ton
- **Execution mode:** live LLM + isolated PostgreSQL fixture (provider/model `OpenAI` / `gpt-5.6-luna`; 3 model calls).
- **Complete user-facing Persian answer:**

> رتبه‌بندی بر اساس رشد مقدار تولید ماهانه: رتبهٔ ۱: کگهر گندله، ۳۴٫۲۸۵۷۱۴۲۸۵۷۱۴۲۸۵۷۱۴ درصد
> منبع: NoavaranCurrentApi، گزارش 9ff6e9d5-46e4-400c-97be-43bd55f85f5e، fixture-kgehr-shahrivar-1405.
- **Trace artifact:** [`monthly-production-sales-live-llm-postgresql-fifteen-cases-v19-2026-10-09.jsonl`, JSONL line 3](../artifacts/monthly-production-sales-live-llm-postgresql-fifteen-cases-v19-2026-10-09.jsonl#L3). The entry retains exact question, plan, SQL/parameters when compiled, execution status and rows, provenance, and answer.
### 5. monthly-05-sales-amount-month

- **Original Persian question (exact LLM input):** فروش ریالی کگل در شهریور ۱۴۰۵ چقدر بوده؟
- **Extracted intent:** measures `metric_monthly_sales` (monthly sales amount); company کگل; product none; period 2026-08-23 to 2026-09-22; operation `DirectValue` / comparison `None`.
- **Generated query plan:** `{"measures":["metric_monthly_sales"],"entities":[{"EntityKind":"Company","UserReference":"کگل","ResolvedEntityId":"e315390b-489c-41c9-b27f-191602099080","IdentityProvenance":"exact_ticker","IdentityVerified":true}],"operation":"DirectValue","comparison":"None","period":"2026-08-23 to 2026-09-22","aggregation":[{"CatalogId":"metric_monthly_sales","Operation":1}],"sort":[],"presentation":0,"filters":[],"populationSalesScope":null,"productConcept":null,"companyDiscoveryScope":null}`
- **Selected semantic references:** Selected semantic references: [{"ReferenceType":3,"CatalogId":"metric_monthly_sales","SourceItemCatalogId":null,"ProviderName":null,"StatementType":null,"SourceItemId":null,"MetricCode":"metric_monthly_sales","Unit":null,"UnitStatus":null,"IdentityStatus":"catalog-mapped","SemanticReviewStatus":"pending-or-unresolved","Queryable":false}]. Relationships: ["rel_31e410fb3ae11956"]. Company binding: {"Status":0,"CompanyId":"e315390b-489c-41c9-b27f-191602099080","CandidateCompanyIds":["e315390b-489c-41c9-b27f-191602099080"],"IdentityProvenance":"exact_ticker","DiagnosticCode":null}.
- **SQL execution:** Yes; PostgreSQL was queried and returned Succeeded. SQL and parameters are preserved in the JSONL trace.
- **Unit correctness:** PASS; rendered with the governed million-IRR display unit, independent of quantity SourceUnit.
- **Expected fixture result, independently verified:** Fixture expectation: [850] million IRR; period 2026-08-23 to 2026-09-22.
- **Actual result:** PostgreSQL Succeeded; observed Value rows [850] million IRR (governed financial display unit); periods 2026-08-23..2026-09-22. Pipeline status `succeeded`; response status `Answered`.
- **Status:** **PASS**. No failure. Validation diagnostics: `["offline_plan_execution_disabled"]`.
- **Source provenance:** providers=NoavaranCurrentApi; report identities=974e6fb7-03c2-4578-b64e-57c7e7f651c8, fixture-kgel-shahrivar-1405; source references=row-fixture-kgel-shahrivar-1405-NoavaranCurrentApi:feature141-kgel:TITLE:کنسانتره|UNIT:ton, row-fixture-kgel-shahrivar-1405-NoavaranCurrentApi:feature141-kgel:TITLE:گندله|UNIT:ton
- **Execution mode:** live LLM + isolated PostgreSQL fixture (provider/model `OpenAI` / `gpt-5.6-luna`; 2 model calls).
- **Complete user-facing Persian answer:**

> کگل در بازهٔ ۲۰۲۶-۰۸-۲۳ تا ۲۰۲۶-۰۹-۲۲، مبلغ فروش ماهانه: ۸۵۰ میلیون ریال.
> منبع: NoavaranCurrentApi، گزارش 974e6fb7-03c2-4578-b64e-57c7e7f651c8، fixture-kgel-shahrivar-1405.
- **Trace artifact:** [`monthly-production-sales-live-llm-postgresql-fifteen-cases-v19-2026-10-09.jsonl`, JSONL line 4](../artifacts/monthly-production-sales-live-llm-postgresql-fifteen-cases-v19-2026-10-09.jsonl#L4). The entry retains exact question, plan, SQL/parameters when compiled, execution status and rows, provenance, and answer.
### 6. monthly-06-sales-quantity-product-latest

- **Original Persian question (exact LLM input):** مقدار فروش گندله کگهر در آخرین گزارش ماهانه چقدر است؟
- **Extracted intent:** measures `metric_monthly_sales_quantity` (monthly sales quantity); company کگهر; product گندله; period latest/approved observation window; operation `DirectValue` / comparison `None`.
- **Generated query plan:** `{"measures":["metric_monthly_sales_quantity"],"entities":[{"EntityKind":"Company","UserReference":"کگهر","ResolvedEntityId":"e447619e-60bf-40f9-a1cd-6fd352af5a09","IdentityProvenance":"exact_ticker","IdentityVerified":true},{"EntityKind":"Product","UserReference":"گندله","ResolvedEntityKey":"NoavaranCurrentApi:feature141-kgehr:TITLE:گندله|UNIT:ton","IdentityProvenance":"feature-129-company-scoped-product-row","IdentityVerified":true}],"operation":"DirectValue","comparison":"None","period":"latest/approved observation window","aggregation":[],"sort":[],"presentation":0,"filters":[],"populationSalesScope":null,"productConcept":null,"companyDiscoveryScope":null}`
- **Selected semantic references:** Selected semantic references: [{"ReferenceType":3,"CatalogId":"metric_monthly_sales_quantity","SourceItemCatalogId":null,"ProviderName":null,"StatementType":null,"SourceItemId":null,"MetricCode":"metric_monthly_sales_quantity","Unit":null,"UnitStatus":null,"IdentityStatus":"catalog-mapped","SemanticReviewStatus":"pending-or-unresolved","Queryable":false}]. Relationships: ["rel_31e410fb3ae11956"]. Product binding: {"Status":0,"CompanyId":"e447619e-60bf-40f9-a1cd-6fd352af5a09","ProductKey":"NoavaranCurrentApi:feature141-kgehr:TITLE:گندله|UNIT:ton","DisplayTitle":"گندله","Unit":"ton","IdentityProvenance":"feature-129-company-scoped-product-row","CandidateProductKeys":[],"DiagnosticCode":null}. Company binding: {"Status":0,"CompanyId":"e447619e-60bf-40f9-a1cd-6fd352af5a09","CandidateCompanyIds":["e447619e-60bf-40f9-a1cd-6fd352af5a09"],"IdentityProvenance":"exact_ticker","DiagnosticCode":null}.
- **SQL execution:** Yes; PostgreSQL was queried and returned Succeeded. SQL and parameters are preserved in the JSONL trace.
- **Unit correctness:** PASS; persisted source unit `ton` is preserved as its governed Persian display label.
- **Expected fixture result, independently verified:** Fixture expectation: [40] ton; period latest/approved observation window.
- **Actual result:** PostgreSQL Succeeded; observed Value rows [40] ton; periods 2026-08-23..2026-09-22. Pipeline status `succeeded`; response status `Answered`.
- **Status:** **PASS**. No failure. Validation diagnostics: `["offline_plan_execution_disabled"]`.
- **Source provenance:** providers=NoavaranCurrentApi; report identities=9ff6e9d5-46e4-400c-97be-43bd55f85f5e, fixture-kgehr-shahrivar-1405; source references=row-fixture-kgehr-shahrivar-1405-NoavaranCurrentApi:feature141-kgehr:TITLE:گندله|UNIT:ton
- **Execution mode:** live LLM + isolated PostgreSQL fixture (provider/model `OpenAI` / `gpt-5.6-luna`; 2 model calls).
- **Complete user-facing Persian answer:**

> کگهر گندله در بازهٔ ۲۰۲۶-۰۸-۲۳ تا ۲۰۲۶-۰۹-۲۲، مقدار فروش ماهانه: ۴۰ تن.
> منبع: NoavaranCurrentApi، گزارش fixture-kgehr-shahrivar-1405.
- **Trace artifact:** [`monthly-production-sales-live-llm-postgresql-fifteen-cases-v19-2026-10-09.jsonl`, JSONL line 5](../artifacts/monthly-production-sales-live-llm-postgresql-fifteen-cases-v19-2026-10-09.jsonl#L5). The entry retains exact question, plan, SQL/parameters when compiled, execution status and rows, provenance, and answer.
### 7. monthly-08-sales-six-month-trend

- **Original Persian question (exact LLM input):** فروش ریالی کگل در شش ماه گذشته چه روندی داشته؟
- **Extracted intent:** measures `metric_monthly_sales` (monthly sales amount); company کگل; product none; period latest/approved observation window; operation `TimeSeries` / comparison `None`.
- **Generated query plan:** `{"measures":["metric_monthly_sales"],"entities":[{"EntityKind":"Company","UserReference":"کگل","ResolvedEntityId":"e315390b-489c-41c9-b27f-191602099080","IdentityProvenance":"exact_ticker","IdentityVerified":true}],"operation":"TimeSeries","comparison":"None","period":"latest/approved observation window","aggregation":[{"CatalogId":"metric_monthly_sales","Operation":1}],"sort":[],"presentation":3,"filters":[],"populationSalesScope":null,"productConcept":null,"companyDiscoveryScope":null}`
- **Selected semantic references:** Selected semantic references: [{"ReferenceType":3,"CatalogId":"metric_monthly_sales","SourceItemCatalogId":null,"ProviderName":null,"StatementType":null,"SourceItemId":null,"MetricCode":"metric_monthly_sales","Unit":null,"UnitStatus":null,"IdentityStatus":"catalog-mapped","SemanticReviewStatus":"pending-or-unresolved","Queryable":false}]. Relationships: ["rel_31e410fb3ae11956"]. Company binding: {"Status":0,"CompanyId":"e315390b-489c-41c9-b27f-191602099080","CandidateCompanyIds":["e315390b-489c-41c9-b27f-191602099080"],"IdentityProvenance":"exact_ticker","DiagnosticCode":null}.
- **SQL execution:** Yes; PostgreSQL was queried and returned Succeeded. SQL and parameters are preserved in the JSONL trace.
- **Unit correctness:** PASS; rendered with the governed million-IRR display unit, independent of quantity SourceUnit.
- **Expected fixture result, independently verified:** Fixture expectation: [120,150,200,250,300,850] million IRR; period latest/approved observation window.
- **Actual result:** PostgreSQL Succeeded; observed Value rows [120,150,200,250,300,850] million IRR (governed financial display unit); periods 2026-04-01..2026-04-30, 2026-05-01..2026-05-31, 2026-06-01..2026-06-30, 2026-07-01..2026-07-31, 2026-08-01..2026-08-31, 2026-08-23..2026-09-22. Pipeline status `succeeded`; response status `Answered`.
- **Status:** **PASS**. No failure. Validation diagnostics: `["offline_plan_execution_disabled"]`.
- **Source provenance:** providers=NoavaranCurrentApi; report identities=00a84eb6-3e94-4453-8869-ce81308cf595, 0145093d-ec1e-45fe-a9f1-03c494d2d796, 96068f84-cb87-41ac-b006-46f78bfd26c4, 974e6fb7-03c2-4578-b64e-57c7e7f651c8, e9db2af2-97a4-4b66-a615-fcf564a3466a, f5a704dd-8e23-4342-8ceb-f90fe5897909, fixture-kgel-shahrivar-1405, fixture-kgel-trend-2, fixture-kgel-trend-3, fixture-kgel-trend-4, fixture-kgel-trend-5, fixture-kgel-trend-6; source references=row-fixture-kgel-shahrivar-1405-NoavaranCurrentApi:feature141-kgel:TITLE:کنسانتره|UNIT:ton, row-fixture-kgel-shahrivar-1405-NoavaranCurrentApi:feature141-kgel:TITLE:گندله|UNIT:ton, row-fixture-kgel-trend-2-NoavaranCurrentApi:feature141-kgel:TITLE:گندله|UNIT:ton, row-fixture-kgel-trend-3-NoavaranCurrentApi:feature141-kgel:TITLE:گندله|UNIT:ton, row-fixture-kgel-trend-4-NoavaranCurrentApi:feature141-kgel:TITLE:گندله|UNIT:ton, row-fixture-kgel-trend-5-NoavaranCurrentApi:feature141-kgel:TITLE:گندله|UNIT:ton, row-fixture-kgel-trend-6-NoavaranCurrentApi:feature141-kgel:TITLE:کنسانتره|UNIT:ton, row-fixture-kgel-trend-6-NoavaranCurrentApi:feature141-kgel:TITLE:گندله|UNIT:ton
- **Execution mode:** live LLM + isolated PostgreSQL fixture (provider/model `OpenAI` / `gpt-5.6-luna`; 2 model calls).
- **Complete user-facing Persian answer:**

> روند مبلغ فروش ماهانه بر اساس دوره‌های گزارش‌شده: در بازهٔ ۲۰۲۶-۰۴-۰۱ تا ۲۰۲۶-۰۴-۳۰ ۱۲۰ میلیون ریال
> منبع: NoavaranCurrentApi، گزارش 00a84eb6-3e94-4453-8869-ce81308cf595، fixture-kgel-trend-2؛ در بازهٔ ۲۰۲۶-۰۵-۰۱ تا ۲۰۲۶-۰۵-۳۱ ۱۵۰ میلیون ریال
> منبع: NoavaranCurrentApi، گزارش 96068f84-cb87-41ac-b006-46f78bfd26c4، fixture-kgel-trend-3؛ در بازهٔ ۲۰۲۶-۰۶-۰۱ تا ۲۰۲۶-۰۶-۳۰ ۲۰۰ میلیون ریال
> منبع: NoavaranCurrentApi، گزارش 0145093d-ec1e-45fe-a9f1-03c494d2d796، fixture-kgel-trend-4؛ در بازهٔ ۲۰۲۶-۰۷-۰۱ تا ۲۰۲۶-۰۷-۳۱ ۲۵۰ میلیون ریال
> منبع: NoavaranCurrentApi، گزارش f5a704dd-8e23-4342-8ceb-f90fe5897909، fixture-kgel-trend-5؛ در بازهٔ ۲۰۲۶-۰۸-۰۱ تا ۲۰۲۶-۰۸-۳۱ ۳۰۰ میلیون ریال
> منبع: NoavaranCurrentApi، گزارش e9db2af2-97a4-4b66-a615-fcf564a3466a، fixture-kgel-trend-6؛ در بازهٔ ۲۰۲۶-۰۸-۲۳ تا ۲۰۲۶-۰۹-۲۲ ۸۵۰ میلیون ریال
> منبع: NoavaranCurrentApi، گزارش 974e6fb7-03c2-4578-b64e-57c7e7f651c8، fixture-kgel-shahrivar-1405.
- **Trace artifact:** [`monthly-production-sales-live-llm-postgresql-fifteen-cases-v19-2026-10-09.jsonl`, JSONL line 6](../artifacts/monthly-production-sales-live-llm-postgresql-fifteen-cases-v19-2026-10-09.jsonl#L6). The entry retains exact question, plan, SQL/parameters when compiled, execution status and rows, provenance, and answer.
### 8. monthly-09-cross-company-product-sales

- **Original Persian question (exact LLM input):** کدام شرکت‌های تولیدکننده گندله بیشترین مقدار فروش را داشته‌اند؟
- **Extracted intent:** measures `metric_monthly_sales_quantity` (monthly sales quantity); company unbound/discovered; product گندله; period 2025-10-09 to 2026-10-09; operation `DirectValue` / comparison `None`.
- **Generated query plan:** `{"measures":["metric_monthly_sales_quantity"],"entities":[{"EntityKind":"Product","UserReference":"گندله","ResolvedEntityKey":"product_iron_ore_pellets","IdentityProvenance":"product-catalog-v1:owner-confirmed:feature141-product-equivalence:2026-10-09","IdentityVerified":true}],"operation":"DirectValue","comparison":"None","period":"2025-10-09 to 2026-10-09","aggregation":[{"CatalogId":"metric_monthly_sales_quantity","Operation":1}],"sort":[{"CatalogId":"metric_monthly_sales_quantity","Direction":1}],"presentation":4,"filters":[{"CatalogId":"dim_product_identity","Operator":0,"Value":{"Type":5,"Value":"product_iron_ore_pellets"}}],"populationSalesScope":1,"productConcept":{"Status":0,"CatalogId":"product_iron_ore_pellets","CatalogVersion":"product-catalog-v1","CanonicalLabel":"گندله سنگ‌آهن","ApprovedAliases":["گندله","گندله سنگ‌آهن"],"ApprovalReference":"owner-confirmed:feature141-product-equivalence:2026-10-09","MappingCoverageStatus":"approved-aliases-only","DiagnosticCode":null},"companyDiscoveryScope":{"TenantId":"5f655e78-07c5-4805-a757-a8d1927ab1d2","ScopeId":"feature141-isolated-tenant-company-scope","ApprovalReference":"owner-approved-isolated-fixture-scope","CompanyCoverageComplete":true}}`
- **Selected semantic references:** Selected semantic references: [{"ReferenceType":3,"CatalogId":"metric_monthly_sales_quantity","SourceItemCatalogId":null,"ProviderName":null,"StatementType":null,"SourceItemId":null,"MetricCode":"metric_monthly_sales_quantity","Unit":null,"UnitStatus":null,"IdentityStatus":"catalog-mapped","SemanticReviewStatus":"pending-or-unresolved","Queryable":false}]. Relationships: ["rel_31e410fb3ae11956"]. Product binding: {"Status":0,"CompanyId":null,"ProductKey":"product_iron_ore_pellets","DisplayTitle":"گندله سنگ‌آهن","Unit":null,"IdentityProvenance":"product-catalog-v1:owner-confirmed:feature141-product-equivalence:2026-10-09","CandidateProductKeys":[],"DiagnosticCode":null}.
- **SQL execution:** Yes; PostgreSQL was queried and returned Incomplete. SQL and parameters are preserved in the JSONL trace.
- **Unit correctness:** Correctly withheld with the incomplete/failed numeric result; no unverified unit was presented.
- **Expected fixture result, independently verified:** Integration assertions independently verify sales quantities [{"company":"NEWP","value":55,"unit":"ton"},{"company":"کگهر","value":40,"unit":"ton"},{"company":"کگل","value":20,"unit":"ton"},{"company":"NOSALES","value":0,"unit":"ton"}] in 2026-08-23..2026-09-22. Alias mapping coverage is partial, so the renderer withholds the ranking.
- **Actual result:** PostgreSQL Incomplete; observed Value rows [55,40,20,0] ton; periods 2026-08-23..2026-09-22. Pipeline status `typed_execution_failure`; response status `Incomplete`.
- **Status:** **INCOMPLETE (partial coverage; numeric rank withheld)**. Failure stage: `restricted_postgresql_execution`; internal status `typed_execution_failure`. Validation diagnostics: `["offline_plan_execution_disabled"]`.
- **Source provenance:** providers=NoavaranCurrentApi; report identities=8a705c90-5805-42ed-ae53-7ca19001d705, 974e6fb7-03c2-4578-b64e-57c7e7f651c8, 9ff6e9d5-46e4-400c-97be-43bd55f85f5e, d2a5126a-f415-4d57-a3cc-55f140442429, fixture-kgehr-shahrivar-1405, fixture-kgel-shahrivar-1405, fixture-new-producer-shahrivar-1405, fixture-production-only-shahrivar-1405; source references=row-fixture-kgehr-shahrivar-1405-NoavaranCurrentApi:feature141-kgehr:TITLE:گندله|UNIT:ton, row-fixture-kgel-shahrivar-1405-NoavaranCurrentApi:feature141-kgel:TITLE:گندله|UNIT:ton, row-fixture-new-producer-shahrivar-1405-NoavaranCurrentApi:feature141-new-producer:TITLE:گندله|UNIT:ton, row-fixture-production-only-shahrivar-1405-NoavaranCurrentApi:feature141-production-only:TITLE:گندله|UNIT:ton
- **Execution mode:** live LLM + isolated PostgreSQL fixture (provider/model `OpenAI` / `gpt-5.6-luna`; 2 model calls).
- **Complete user-facing Persian answer:**

> پوشش جمعیت تولیدکنندگان یا نگاشت محصول برای رتبه‌بندی کامل نیست؛ رتبهٔ مالی گزارش نمی‌شود.
- **Trace artifact:** [`monthly-production-sales-live-llm-postgresql-fifteen-cases-v19-2026-10-09.jsonl`, JSONL line 7](../artifacts/monthly-production-sales-live-llm-postgresql-fifteen-cases-v19-2026-10-09.jsonl#L7). The entry retains exact question, plan, SQL/parameters when compiled, execution status and rows, provenance, and answer.
### 9. monthly-10-company-sales-comparison

- **Original Persian question (exact LLM input):** فروش ماهانه کگل و کگهر را با هم مقایسه کن.
- **Extracted intent:** measures `metric_monthly_sales` (monthly sales amount); company کگل, کگهر; product none; period latest/approved observation window; operation `DirectValue` / comparison `MultiCompanyComparison`.
- **Generated query plan:** `{"measures":["metric_monthly_sales"],"entities":[{"EntityKind":"Company","UserReference":"کگل","ResolvedEntityId":"e315390b-489c-41c9-b27f-191602099080","IdentityProvenance":"exact_ticker","IdentityVerified":true},{"EntityKind":"Company","UserReference":"کگهر","ResolvedEntityId":"e447619e-60bf-40f9-a1cd-6fd352af5a09","IdentityProvenance":"exact_ticker","IdentityVerified":true}],"operation":"DirectValue","comparison":"MultiCompanyComparison","period":"latest/approved observation window","aggregation":[{"CatalogId":"metric_monthly_sales","Operation":1}],"sort":[],"presentation":1,"filters":[],"populationSalesScope":null,"productConcept":null,"companyDiscoveryScope":null}`
- **Selected semantic references:** Selected semantic references: [{"ReferenceType":3,"CatalogId":"metric_monthly_sales","SourceItemCatalogId":null,"ProviderName":null,"StatementType":null,"SourceItemId":null,"MetricCode":"metric_monthly_sales","Unit":null,"UnitStatus":null,"IdentityStatus":"catalog-mapped","SemanticReviewStatus":"pending-or-unresolved","Queryable":false}]. Relationships: ["rel_31e410fb3ae11956"].
- **SQL execution:** Yes; PostgreSQL was queried and returned Succeeded. SQL and parameters are preserved in the JSONL trace.
- **Unit correctness:** PASS; rendered with the governed million-IRR display unit, independent of quantity SourceUnit.
- **Expected fixture result, independently verified:** Fixture expectation: [850,700] million IRR; period latest/approved observation window.
- **Actual result:** PostgreSQL Succeeded; observed Value rows [850,700] million IRR (governed financial display unit); periods 2026-08-23..2026-09-22. Pipeline status `succeeded`; response status `Answered`.
- **Status:** **PASS**. No failure. Validation diagnostics: `["offline_plan_execution_disabled"]`.
- **Source provenance:** providers=NoavaranCurrentApi; report identities=974e6fb7-03c2-4578-b64e-57c7e7f651c8, 9ff6e9d5-46e4-400c-97be-43bd55f85f5e, fixture-kgehr-shahrivar-1405, fixture-kgel-shahrivar-1405; source references=row-fixture-kgehr-shahrivar-1405-NoavaranCurrentApi:feature141-kgehr:TITLE:گندله|UNIT:ton, row-fixture-kgel-shahrivar-1405-NoavaranCurrentApi:feature141-kgel:TITLE:کنسانتره|UNIT:ton, row-fixture-kgel-shahrivar-1405-NoavaranCurrentApi:feature141-kgel:TITLE:گندله|UNIT:ton
- **Execution mode:** live LLM + isolated PostgreSQL fixture (provider/model `OpenAI` / `gpt-5.6-luna`; 2 model calls).
- **Complete user-facing Persian answer:**

> مقایسهٔ مبلغ فروش ماهانه: کگل ۸۵۰ میلیون ریال (در بازهٔ ۲۰۲۶-۰۸-۲۳ تا ۲۰۲۶-۰۹-۲۲)
> منبع: NoavaranCurrentApi، گزارش 974e6fb7-03c2-4578-b64e-57c7e7f651c8، fixture-kgel-shahrivar-1405؛ کگهر ۷۰۰ میلیون ریال (در بازهٔ ۲۰۲۶-۰۸-۲۳ تا ۲۰۲۶-۰۹-۲۲)
> منبع: NoavaranCurrentApi، گزارش 9ff6e9d5-46e4-400c-97be-43bd55f85f5e، fixture-kgehr-shahrivar-1405.
- **Trace artifact:** [`monthly-production-sales-live-llm-postgresql-fifteen-cases-v19-2026-10-09.jsonl`, JSONL line 8](../artifacts/monthly-production-sales-live-llm-postgresql-fifteen-cases-v19-2026-10-09.jsonl#L8). The entry retains exact question, plan, SQL/parameters when compiled, execution status and rows, provenance, and answer.
### 10. monthly-14-production-concentrate-explicit-month

- **Original Persian question (exact LLM input):** مقدار تولید کنسانتره کگل در شهریور ۱۴۰۵ چقدر بوده؟
- **Extracted intent:** measures `metric_monthly_production_quantity` (monthly production quantity); company کگل; product کنسانتره; period 2026-08-23 to 2026-09-22; operation `DirectValue` / comparison `None`.
- **Generated query plan:** `{"measures":["metric_monthly_production_quantity"],"entities":[{"EntityKind":"Company","UserReference":"کگل","ResolvedEntityId":"e315390b-489c-41c9-b27f-191602099080","IdentityProvenance":"exact_ticker","IdentityVerified":true},{"EntityKind":"Product","UserReference":"کنسانتره","ResolvedEntityKey":"NoavaranCurrentApi:feature141-kgel:TITLE:کنسانتره|UNIT:ton","IdentityProvenance":"feature-129-company-scoped-product-row","IdentityVerified":true}],"operation":"DirectValue","comparison":"None","period":"2026-08-23 to 2026-09-22","aggregation":[],"sort":[],"presentation":0,"filters":[],"populationSalesScope":null,"productConcept":null,"companyDiscoveryScope":null}`
- **Selected semantic references:** Selected semantic references: [{"ReferenceType":3,"CatalogId":"metric_monthly_production_quantity","SourceItemCatalogId":null,"ProviderName":null,"StatementType":null,"SourceItemId":null,"MetricCode":"metric_monthly_production_quantity","Unit":null,"UnitStatus":null,"IdentityStatus":"catalog-mapped","SemanticReviewStatus":"pending-or-unresolved","Queryable":false}]. Relationships: ["rel_31e410fb3ae11956"]. Product binding: {"Status":0,"CompanyId":"e315390b-489c-41c9-b27f-191602099080","ProductKey":"NoavaranCurrentApi:feature141-kgel:TITLE:کنسانتره|UNIT:ton","DisplayTitle":"کنسانتره","Unit":"ton","IdentityProvenance":"feature-129-company-scoped-product-row","CandidateProductKeys":[],"DiagnosticCode":null}. Company binding: {"Status":0,"CompanyId":"e315390b-489c-41c9-b27f-191602099080","CandidateCompanyIds":["e315390b-489c-41c9-b27f-191602099080"],"IdentityProvenance":"exact_ticker","DiagnosticCode":null}.
- **SQL execution:** Yes; PostgreSQL was queried and returned Succeeded. SQL and parameters are preserved in the JSONL trace.
- **Unit correctness:** PASS; persisted source unit `ton` is preserved as its governed Persian display label.
- **Expected fixture result, independently verified:** Fixture expectation: [12] ton; period 2026-08-23 to 2026-09-22.
- **Actual result:** PostgreSQL Succeeded; observed Value rows [12] ton; periods 2026-08-23..2026-09-22. Pipeline status `succeeded`; response status `Answered`.
- **Status:** **PASS**. No failure. Validation diagnostics: `["offline_plan_execution_disabled"]`.
- **Source provenance:** providers=NoavaranCurrentApi; report identities=974e6fb7-03c2-4578-b64e-57c7e7f651c8, fixture-kgel-shahrivar-1405; source references=row-fixture-kgel-shahrivar-1405-NoavaranCurrentApi:feature141-kgel:TITLE:کنسانتره|UNIT:ton
- **Execution mode:** live LLM + isolated PostgreSQL fixture (provider/model `OpenAI` / `gpt-5.6-luna`; 2 model calls).
- **Complete user-facing Persian answer:**

> کگل کنسانتره در بازهٔ ۲۰۲۶-۰۸-۲۳ تا ۲۰۲۶-۰۹-۲۲، مقدار تولید ماهانه: ۱۲ تن.
> منبع: NoavaranCurrentApi، گزارش fixture-kgel-shahrivar-1405.
- **Trace artifact:** [`monthly-production-sales-live-llm-postgresql-fifteen-cases-v19-2026-10-09.jsonl`, JSONL line 9](../artifacts/monthly-production-sales-live-llm-postgresql-fifteen-cases-v19-2026-10-09.jsonl#L9). The entry retains exact question, plan, SQL/parameters when compiled, execution status and rows, provenance, and answer.
### 11. monthly-01-production-latest

- **Original Persian question (exact LLM input):** کگل در آخرین ماه چه مقدار تولید داشته است؟
- **Extracted intent:** measures `metric_monthly_production_quantity` (monthly production quantity); company کگل; product none; period latest/approved observation window; operation `DirectValue` / comparison `None`.
- **Generated query plan:** `{"measures":["metric_monthly_production_quantity"],"entities":[{"EntityKind":"Company","UserReference":"کگل","ResolvedEntityId":"e315390b-489c-41c9-b27f-191602099080","IdentityProvenance":"exact_ticker","IdentityVerified":true}],"operation":"DirectValue","comparison":"None","period":"latest/approved observation window","aggregation":[{"CatalogId":"metric_monthly_production_quantity","Operation":1}],"sort":[],"presentation":0,"filters":[],"populationSalesScope":null,"productConcept":null,"companyDiscoveryScope":null}`
- **Selected semantic references:** Selected semantic references: [{"ReferenceType":3,"CatalogId":"metric_monthly_production_quantity","SourceItemCatalogId":null,"ProviderName":null,"StatementType":null,"SourceItemId":null,"MetricCode":"metric_monthly_production_quantity","Unit":null,"UnitStatus":null,"IdentityStatus":"catalog-mapped","SemanticReviewStatus":"pending-or-unresolved","Queryable":false}]. Relationships: ["rel_31e410fb3ae11956"]. Company binding: {"Status":0,"CompanyId":"e315390b-489c-41c9-b27f-191602099080","CandidateCompanyIds":["e315390b-489c-41c9-b27f-191602099080"],"IdentityProvenance":"exact_ticker","DiagnosticCode":null}.
- **SQL execution:** Yes; PostgreSQL was queried and returned Succeeded. SQL and parameters are preserved in the JSONL trace.
- **Unit correctness:** PASS; persisted source unit `ton` is preserved as its governed Persian display label.
- **Expected fixture result, independently verified:** Fixture rows independently establish 37 ton in 2026-08-23..2026-09-22 (25 ton pellets plus 12 ton concentrate).
- **Actual result:** PostgreSQL Succeeded; observed Value rows [37] ton; periods 2026-08-23..2026-09-22. Pipeline status `succeeded`; response status `Answered`.
- **Status:** **PASS**. No failure. Validation diagnostics: `["offline_plan_execution_disabled"]`.
- **Source provenance:** providers=NoavaranCurrentApi; report identities=974e6fb7-03c2-4578-b64e-57c7e7f651c8, fixture-kgel-shahrivar-1405; source references=row-fixture-kgel-shahrivar-1405-NoavaranCurrentApi:feature141-kgel:TITLE:کنسانتره|UNIT:ton, row-fixture-kgel-shahrivar-1405-NoavaranCurrentApi:feature141-kgel:TITLE:گندله|UNIT:ton
- **Execution mode:** live LLM + isolated PostgreSQL fixture (provider/model `OpenAI` / `gpt-5.6-luna`; 2 model calls).
- **Complete user-facing Persian answer:**

> کگل در بازهٔ ۲۰۲۶-۰۸-۲۳ تا ۲۰۲۶-۰۹-۲۲، مقدار تولید ماهانه: ۳۷ تن.
> منبع: NoavaranCurrentApi، گزارش 974e6fb7-03c2-4578-b64e-57c7e7f651c8، fixture-kgel-shahrivar-1405.
- **Trace artifact:** [`monthly-production-sales-live-llm-postgresql-fifteen-cases-v19-2026-10-09.jsonl`, JSONL line 10](../artifacts/monthly-production-sales-live-llm-postgresql-fifteen-cases-v19-2026-10-09.jsonl#L10). The entry retains exact question, plan, SQL/parameters when compiled, execution status and rows, provenance, and answer.
### 12. monthly-07-sales-rate-two-months

- **Original Persian question (exact LLM input):** نرخ فروش گندله کگهر در مرداد و شهریور ۱۴۰۵ چقدر بوده؟
- **Extracted intent:** measures `metric_monthly_sales_rate` (monthly sales rate); company کگهر; product گندله; period 2026-07-23 to 2026-09-22; operation `DirectValue` / comparison `None`.
- **Generated query plan:** `{"measures":["metric_monthly_sales_rate"],"entities":[{"EntityKind":"Company","UserReference":"کگهر","ResolvedEntityId":"e447619e-60bf-40f9-a1cd-6fd352af5a09","IdentityProvenance":"exact_ticker","IdentityVerified":true},{"EntityKind":"Product","UserReference":"گندله","ResolvedEntityKey":"NoavaranCurrentApi:feature141-kgehr:TITLE:گندله|UNIT:ton","IdentityProvenance":"feature-129-company-scoped-product-row","IdentityVerified":true}],"operation":"DirectValue","comparison":"None","period":"2026-07-23 to 2026-09-22","aggregation":[],"sort":[],"presentation":1,"filters":[],"populationSalesScope":null,"productConcept":null,"companyDiscoveryScope":null}`
- **Selected semantic references:** Selected semantic references: [{"ReferenceType":3,"CatalogId":"metric_monthly_sales_rate","SourceItemCatalogId":null,"ProviderName":null,"StatementType":null,"SourceItemId":null,"MetricCode":"metric_monthly_sales_rate","Unit":null,"UnitStatus":null,"IdentityStatus":"catalog-mapped","SemanticReviewStatus":"pending-or-unresolved","Queryable":false}]. Relationships: ["rel_31e410fb3ae11956"]. Product binding: {"Status":0,"CompanyId":"e447619e-60bf-40f9-a1cd-6fd352af5a09","ProductKey":"NoavaranCurrentApi:feature141-kgehr:TITLE:گندله|UNIT:ton","DisplayTitle":"گندله","Unit":"ton","IdentityProvenance":"feature-129-company-scoped-product-row","CandidateProductKeys":[],"DiagnosticCode":null}. Company binding: {"Status":0,"CompanyId":"e447619e-60bf-40f9-a1cd-6fd352af5a09","CandidateCompanyIds":["e447619e-60bf-40f9-a1cd-6fd352af5a09"],"IdentityProvenance":"exact_ticker","DiagnosticCode":null}.
- **SQL execution:** Yes; PostgreSQL was queried and returned Succeeded. SQL and parameters are preserved in the JSONL trace.
- **Unit correctness:** PASS; the catalog unit is unresolved, so the renderer explicitly says unit unknown and does not reuse the row's quantity SourceUnit.
- **Expected fixture result, independently verified:** Seeded accepted rates for Mordad and Shahrivar 1405 are 17.5 and 16.7 in the catalog sales-rate unit.
- **Actual result:** PostgreSQL Succeeded; observed Value rows [16.7,17.5]; periods 2026-08-01..2026-08-31, 2026-08-23..2026-09-22. Pipeline status `succeeded`; response status `Answered`.
- **Status:** **PASS**. No failure. Validation diagnostics: `["offline_plan_execution_disabled"]`.
- **Source provenance:** providers=NoavaranCurrentApi; report identities=9ff6e9d5-46e4-400c-97be-43bd55f85f5e, b03de3bb-9423-4c41-896e-eb355d4a1343, fixture-kgehr-august-1405, fixture-kgehr-shahrivar-1405; source references=row-fixture-kgehr-august-1405-NoavaranCurrentApi:feature141-kgehr:TITLE:گندله|UNIT:ton, row-fixture-kgehr-shahrivar-1405-NoavaranCurrentApi:feature141-kgehr:TITLE:گندله|UNIT:ton
- **Execution mode:** live LLM + isolated PostgreSQL fixture (provider/model `OpenAI` / `gpt-5.6-luna`; 2 model calls).
- **Complete user-facing Persian answer:**

> کگهر Pellet در بازهٔ ۲۰۲۶-۰۸-۰۱ تا ۲۰۲۶-۰۸-۳۱، مقدار مالی گزارش‌شده: ۱۶٫۷ واحد اندازه‌گیری نامشخص.
> منبع: NoavaranCurrentApi، گزارش fixture-kgehr-august-1405.
- **Trace artifact:** [`monthly-production-sales-live-llm-postgresql-fifteen-cases-v19-2026-10-09.jsonl`, JSONL line 11](../artifacts/monthly-production-sales-live-llm-postgresql-fifteen-cases-v19-2026-10-09.jsonl#L11). The entry retains exact question, plan, SQL/parameters when compiled, execution status and rows, provenance, and answer.
### 13. monthly-11-industry-sales-growth-ranking

- **Original Persian question (exact LLM input):** کدام شرکت‌های یک صنعت بیشترین رشد فروش ماهانه را ثبت کرده‌اند؟
- **Extracted intent:** measures `metric_monthly_sales_growth_mom` (month-over-month monthly sales growth); company unbound/discovered; product none; period period not resolved; operation `unresolved` / comparison `None`.
- **Generated query plan:** `{"measures":["metric_monthly_sales_growth_mom"],"entities":[],"operation":"unresolved","comparison":null,"period":"period not resolved","aggregation":null,"sort":null,"presentation":null,"filters":null,"populationSalesScope":null,"productConcept":null,"companyDiscoveryScope":null}`
- **Selected semantic references:** Selected semantic references: ["metric_monthly_sales_growth_mom"]. Relationships: [].
- **SQL execution:** No; PostgreSQL was not queried.
- **Unit correctness:** Not independently verifiable from the selected measure metadata.
- **Expected fixture result, independently verified:** No independent numeric assertion was configured; see the actual SQL rows and provenance below.
- **Actual result:** No SQL result; planning or the pipeline returned before PostgreSQL. Pipeline status `typed_pipeline_failure`; response status `None`.
- **Status:** **UNSUPPORTED**. Failure stage: `pipeline`; internal status `typed_pipeline_failure`. Exact failure code: `SchemaNavigationOutputException`; detail: `requirements_contain_unsupported_financial_value_operation`.
- **Source provenance:** No persisted source provenance; no PostgreSQL row was read.
- **Execution mode:** live LLM + isolated PostgreSQL fixture (provider/model `None` / `None`; ? model calls).
- **Complete user-facing Persian answer:**

> Proposed rendering (renderer not reached): «این درخواست با تعریف‌های تأییدشدهٔ پرس‌وجو قابل اجرا نیست.»
- **Trace artifact:** [`monthly-production-sales-live-llm-postgresql-fifteen-cases-v19-2026-10-09.jsonl`, JSONL line 12](../artifacts/monthly-production-sales-live-llm-postgresql-fifteen-cases-v19-2026-10-09.jsonl#L12). The entry retains exact question, plan, SQL/parameters when compiled, execution status and rows, provenance, and answer.
### 14. monthly-12-sales-vs-operating-profit-growth

- **Original Persian question (exact LLM input):** رشد فروش ماهانه کگل با رشد سود عملیاتی آن چه تفاوتی داشته؟
- **Extracted intent:** measures `metric_monthly_sales_growth_mom` (month-over-month monthly sales growth); company unbound/discovered; product none; period period not resolved; operation `unresolved` / comparison `None`.
- **Generated query plan:** `{"measures":["metric_monthly_sales_growth_mom"],"entities":[],"operation":"unresolved","comparison":null,"period":"period not resolved","aggregation":null,"sort":null,"presentation":null,"filters":null,"populationSalesScope":null,"productConcept":null,"companyDiscoveryScope":null}`
- **Selected semantic references:** Selected semantic references: ["metric_monthly_sales_growth_mom"]. Relationships: [].
- **SQL execution:** No; PostgreSQL was not queried.
- **Unit correctness:** Not independently verifiable from the selected measure metadata.
- **Expected fixture result, independently verified:** No numeric result. Expected fail-closed handling for unsupported cross-measure or cross-domain growth operations.
- **Actual result:** No SQL result; planning or the pipeline returned before PostgreSQL. Pipeline status `typed_pipeline_failure`; response status `None`.
- **Status:** **UNSUPPORTED**. Failure stage: `pipeline`; internal status `typed_pipeline_failure`. Exact failure code: `SchemaNavigationOutputException`; detail: `requirements_contain_unsupported_financial_value_operation`.
- **Source provenance:** No persisted source provenance; no PostgreSQL row was read.
- **Execution mode:** live LLM + isolated PostgreSQL fixture (provider/model `None` / `None`; ? model calls).
- **Complete user-facing Persian answer:**

> Proposed rendering (renderer not reached): «این درخواست با تعریف‌های تأییدشدهٔ پرس‌وجو قابل اجرا نیست.»
- **Trace artifact:** [`monthly-production-sales-live-llm-postgresql-fifteen-cases-v19-2026-10-09.jsonl`, JSONL line 13](../artifacts/monthly-production-sales-live-llm-postgresql-fifteen-cases-v19-2026-10-09.jsonl#L13). The entry retains exact question, plan, SQL/parameters when compiled, execution status and rows, provenance, and answer.
### 15. monthly-13-production-vs-sales-growth

- **Original Persian question (exact LLM input):** آیا رشد تولید کگهر با رشد فروش آن هم‌جهت بوده است؟
- **Extracted intent:** measures `metric_monthly_production_growth_yoy, metric_monthly_sales_growth_yoy, metric_monthly_sales_quantity_growth_yoy` (metric_monthly_production_growth_yoy, metric_monthly_sales_growth_yoy, metric_monthly_sales_quantity_growth_yoy); company کگهر; product none; period latest/approved observation window; operation `TimeSeries` / comparison `None`.
- **Generated query plan:** `{"measures":["metric_monthly_production_growth_yoy","metric_monthly_sales_growth_yoy","metric_monthly_sales_quantity_growth_yoy"],"entities":[{"EntityKind":"Company","UserReference":"کگهر","ResolvedEntityId":"e447619e-60bf-40f9-a1cd-6fd352af5a09","IdentityProvenance":"exact_ticker","IdentityVerified":true}],"operation":"TimeSeries","comparison":"None","period":"latest/approved observation window","aggregation":[],"sort":[],"presentation":5,"filters":[],"populationSalesScope":null,"productConcept":null,"companyDiscoveryScope":null}`
- **Selected semantic references:** Selected semantic references: [{"ReferenceType":0,"CatalogId":"metric_monthly_production_growth_yoy","SourceItemCatalogId":null,"ProviderName":null,"StatementType":null,"SourceItemId":null,"MetricCode":null,"Unit":null,"UnitStatus":null,"IdentityStatus":"catalog-reference","SemanticReviewStatus":"pending-or-unresolved","Queryable":false},{"ReferenceType":0,"CatalogId":"metric_monthly_sales_growth_yoy","SourceItemCatalogId":null,"ProviderName":null,"StatementType":null,"SourceItemId":null,"MetricCode":null,"Unit":null,"UnitStatus":null,"IdentityStatus":"catalog-reference","SemanticReviewStatus":"pending-or-unresolved","Queryable":false},{"ReferenceType":0,"CatalogId":"metric_monthly_sales_quantity_growth_yoy","SourceItemCatalogId":null,"ProviderName":null,"StatementType":null,"SourceItemId":null,"MetricCode":null,"Unit":null,"UnitStatus":null,"IdentityStatus":"catalog-reference","SemanticReviewStatus":"pending-or-unresolved","Queryable":false}]. Relationships: ["rel_31e410fb3ae11956","rel_4e54d40aee8b45e4"]. Company binding: {"Status":0,"CompanyId":"e447619e-60bf-40f9-a1cd-6fd352af5a09","CandidateCompanyIds":["e447619e-60bf-40f9-a1cd-6fd352af5a09"],"IdentityProvenance":"exact_ticker","DiagnosticCode":null}.
- **SQL execution:** No; PostgreSQL was not queried.
- **Unit correctness:** Not independently verifiable from the selected measure metadata.
- **Expected fixture result, independently verified:** No numeric result. Expected a typed ambiguity for an industry-wide ranking without a reviewed industry-population mapping; SQL should not run.
- **Actual result:** No SQL result; planning or the pipeline returned before PostgreSQL. Pipeline status `typed_ambiguity`; response status `Unsupported`.
- **Status:** **AMBIGUOUS**. Failure stage: `requirements_or_measure_selection`; internal status `typed_ambiguity`. Validation diagnostics: `["plan_contains_unresolved_projection","plan_source_not_in_context:metric_monthly_production_growth_yoy","plan_source_not_in_context:metric_monthly_sales_growth_yoy","plan_source_not_in_context:metric_monthly_sales_quantity_growth_yoy","direct_value_operation_kind_mismatch","retrieval_requirements_incomplete"]`.
- **Source provenance:** No persisted source provenance; no PostgreSQL row was read.
- **Execution mode:** live LLM + isolated PostgreSQL fixture (provider/model `OpenAI` / `gpt-5.6-luna`; 2 model calls).
- **Complete user-facing Persian answer:**

> این درخواست با تعریف‌های تأییدشدهٔ پرس‌وجو قابل اجرا نیست.
- **Trace artifact:** [`monthly-production-sales-live-llm-postgresql-fifteen-cases-v19-2026-10-09.jsonl`, JSONL line 14](../artifacts/monthly-production-sales-live-llm-postgresql-fifteen-cases-v19-2026-10-09.jsonl#L14). The entry retains exact question, plan, SQL/parameters when compiled, execution status and rows, provenance, and answer.
### 16. producer-16-total-company-sales-ranking

- **Original Persian question (exact LLM input):** کدام شرکت‌های تولیدکننده گندله بیشترین فروش کل ماهانه را داشته‌اند؟
- **Extracted intent:** measures `metric_monthly_sales` (monthly sales amount); company unbound/discovered; product گندله; period 2025-10-09 to 2026-10-09; operation `DirectValue` / comparison `None`.
- **Generated query plan:** `{"measures":["metric_monthly_sales"],"entities":[{"EntityKind":"Product","UserReference":"گندله","ResolvedEntityKey":"product_iron_ore_pellets","IdentityProvenance":"product-catalog-v1:owner-confirmed:feature141-product-equivalence:2026-10-09","IdentityVerified":true}],"operation":"DirectValue","comparison":"None","period":"2025-10-09 to 2026-10-09","aggregation":[{"CatalogId":"metric_monthly_sales","Operation":1}],"sort":[{"CatalogId":"metric_monthly_sales","Direction":1}],"presentation":4,"filters":[],"populationSalesScope":0,"productConcept":{"Status":0,"CatalogId":"product_iron_ore_pellets","CatalogVersion":"product-catalog-v1","CanonicalLabel":"گندله سنگ‌آهن","ApprovedAliases":["گندله","گندله سنگ‌آهن"],"ApprovalReference":"owner-confirmed:feature141-product-equivalence:2026-10-09","MappingCoverageStatus":"approved-aliases-only","DiagnosticCode":null},"companyDiscoveryScope":{"TenantId":"5f655e78-07c5-4805-a757-a8d1927ab1d2","ScopeId":"feature141-isolated-tenant-company-scope","ApprovalReference":"owner-approved-isolated-fixture-scope","CompanyCoverageComplete":true}}`
- **Selected semantic references:** Selected semantic references: [{"ReferenceType":3,"CatalogId":"metric_monthly_sales","SourceItemCatalogId":null,"ProviderName":null,"StatementType":null,"SourceItemId":null,"MetricCode":"metric_monthly_sales","Unit":null,"UnitStatus":null,"IdentityStatus":"catalog-mapped","SemanticReviewStatus":"pending-or-unresolved","Queryable":false}]. Relationships: ["rel_31e410fb3ae11956"]. Product binding: {"Status":0,"CompanyId":null,"ProductKey":"product_iron_ore_pellets","DisplayTitle":"گندله سنگ‌آهن","Unit":null,"IdentityProvenance":"product-catalog-v1:owner-confirmed:feature141-product-equivalence:2026-10-09","CandidateProductKeys":[],"DiagnosticCode":null}.
- **SQL execution:** Yes; PostgreSQL was queried and returned Incomplete. SQL and parameters are preserved in the JSONL trace.
- **Unit correctness:** Correctly withheld with the incomplete/failed numeric result; no unverified unit was presented.
- **Expected fixture result, independently verified:** Integration assertions independently verify total sales amounts [{"company":"NEWP","value":900,"unit":"million IRR"},{"company":"کگل","value":850,"unit":"million IRR"},{"company":"کگهر","value":700,"unit":"million IRR"},{"company":"NOSALES","value":0,"unit":"million IRR"}] in 2026-08-23..2026-09-22. Alias mapping coverage is partial, so the renderer withholds the ranking.
- **Actual result:** PostgreSQL Incomplete; observed Value rows [900,850,700,0] million IRR (governed financial display unit); periods 2026-08-23..2026-09-22. Pipeline status `typed_execution_failure`; response status `Incomplete`.
- **Status:** **INCOMPLETE (partial coverage; numeric rank withheld)**. Failure stage: `restricted_postgresql_execution`; internal status `typed_execution_failure`. Validation diagnostics: `["offline_plan_execution_disabled"]`.
- **Source provenance:** providers=NoavaranCurrentApi; report identities=8a705c90-5805-42ed-ae53-7ca19001d705, 974e6fb7-03c2-4578-b64e-57c7e7f651c8, 9ff6e9d5-46e4-400c-97be-43bd55f85f5e, d2a5126a-f415-4d57-a3cc-55f140442429, fixture-kgehr-shahrivar-1405, fixture-kgel-shahrivar-1405, fixture-new-producer-shahrivar-1405, fixture-production-only-shahrivar-1405; source references=row-fixture-kgehr-shahrivar-1405-NoavaranCurrentApi:feature141-kgehr:TITLE:گندله|UNIT:ton, row-fixture-kgel-shahrivar-1405-NoavaranCurrentApi:feature141-kgel:TITLE:کنسانتره|UNIT:ton, row-fixture-kgel-shahrivar-1405-NoavaranCurrentApi:feature141-kgel:TITLE:گندله|UNIT:ton, row-fixture-new-producer-shahrivar-1405-NoavaranCurrentApi:feature141-new-producer:TITLE:گندله|UNIT:ton, row-fixture-production-only-shahrivar-1405-NoavaranCurrentApi:feature141-production-only:TITLE:گندله|UNIT:ton
- **Execution mode:** live LLM + isolated PostgreSQL fixture (provider/model `OpenAI` / `gpt-5.6-luna`; 2 model calls).
- **Complete user-facing Persian answer:**

> پوشش جمعیت تولیدکنندگان یا نگاشت محصول برای رتبه‌بندی کامل نیست؛ رتبهٔ مالی گزارش نمی‌شود.
- **Trace artifact:** [`monthly-production-sales-live-llm-postgresql-fifteen-cases-v19-2026-10-09.jsonl`, JSONL line 15](../artifacts/monthly-production-sales-live-llm-postgresql-fifteen-cases-v19-2026-10-09.jsonl#L15). The entry retains exact question, plan, SQL/parameters when compiled, execution status and rows, provenance, and answer.
## Interpretation and limitations

- Executions used synthetic isolated PostgreSQL fixtures, not Development, production, or actual Tehran Stock Exchange financial data.
- Three SQL executions returned rows but were classified Incomplete because approved product alias coverage is partial. The renderer withheld numeric ranks; source rows remain in the traces.
- Industry-wide monthly sales growth was ambiguous/unsupported. Sales-versus-operating-profit and production-versus-sales growth failed closed before PostgreSQL.
- Historical evaluation sections follow verbatim for comparison. The v19 traces are the fresh results for the rerun questions.

---

# Feature 141 Persian end-to-end examples

> **Superseded run-status note:** this file preserves the earlier evaluation snapshot. The PostgreSQL-unavailable statements below are historical. See the later [dynamic company discovery and Persian end-to-end evidence report](dynamic-company-discovery.md) for the fresh 2026-10-09 live LLM + isolated PostgreSQL run, generated population answer, and updated unit-test result.

Run date: 2026-10-09. The answers below were generated by the Feature 141 deterministic Persian response component from structured rows in persisted PostgreSQL fixture traces. The response replay did not query a database again. Original traces preserve exact questions, selected semantics, plans, SQL, parameters and observed rows. All values are synthetic fixture values; no Development or production financial database was queried.

The live PostgreSQL integration test was not runnable in this environment: `FINANCIAL_COPILOT_TEST_POSTGRES_CONNECTION_STRING` is unset, and Docker Desktop's PostgreSQL engine is unavailable. The current code has response-level assertions inside that isolated PostgreSQL test, but those assertions could not execute in this run. The deterministic answer replay over previously recorded PostgreSQL rows is reported separately from a fresh LLM + PostgreSQL run.

The current generated answers include provider/report references when the persisted rows expose them. Ambiguous and unsupported outcomes do not present candidate financial numbers as answers. This is an artifact replay of previously executed LLM/PostgreSQL traces, not a fresh first-pass evaluation in this turn. The isolated-PostgreSQL harness contains response assertions for supported fixture outcomes, but its test was skipped in this run. The requested 15-case end-to-end response acceptance set therefore remains unverified; no fresh first-pass evaluation was run because PostgreSQL could not be started.

## Generated-answer replay

| Source trace | Cases | Replay result |
|---|---:|---|
| [Live LLM + PostgreSQL trace v17](../artifacts/monthly-production-sales-live-llm-postgresql-eight-cases-v17-2026-10-09.jsonl) | 8 | 7 Answered; 1 Unsupported; 7 SQL executions in the original run |
| [Cross-company PostgreSQL trace v2](../artifacts/cross-company-postgresql-examples-v2-2026-10-09.jsonl) | 4 | 2 Answered; 1 NoData; 1 Ambiguous after the current YoY boundary policy |

Generated answers with their input traces and structured evidence: [live-result response replay](../artifacts/monthly-production-sales-persian-response-replay-v1-2026-10-09.jsonl), [cross-company response replay](../artifacts/cross-company-persian-response-replay-v1-2026-10-09.jsonl).

## Run summary

| Run | Questions | PostgreSQL | Outcome |
|---|---:|---|---|
| Live LLM replay | 8 distinct Persian questions | 7 queried; 1 stopped at planning validation | 7 PASS, 1 UNSUPPORTED; 0 incorrect numeric results |
| Deterministic fixture replay | 4 Persian questions | 4 queried | 2 numeric PASS, 1 expected `NoData` PASS, 1 AMBIGUOUS |

The live replay used OpenAI model `gpt-5.6-luna`: 16 model calls, 70,079 input tokens, 7,699 output tokens, 12,371 ms mean per-question model latency and 14,352 ms p95. The persisted per-case trace contains provider/model, token use, latency, semantic references, plan, compiled SQL and parameters, PostgreSQL outcome and returned rows. Finish reason and retry reason were not exposed by the Feature 141 navigation result and are therefore not claimed here.

The monthly sales amount catalog unit is **MillionIRR**. `SourceUnit` in returned monthly rows describes the product quantity unit; it is not the amount unit. Production and sales quantity examples use the row-scoped source unit, **ton**.

## Live LLM + PostgreSQL replay

The full serialized traces are in [`monthly-production-sales-live-llm-postgresql-eight-cases-v17-2026-10-09.jsonl`](../artifacts/monthly-production-sales-live-llm-postgresql-eight-cases-v17-2026-10-09.jsonl). Each referenced JSONL line records the exact question, extracted plan, resolved identities, generated SQL and bound parameters when compilation succeeded, execution outcome, source rows and telemetry. Semantic references use catalog hash `91bd31549418bdbd870b878079efa2808f9122d02cb7725aab5d4bab8595d1a2` and relationship `rel_31e410fb3ae11956`.

### 1. Production quantity for a company-scoped product

- **Original Persian question:** مقدار تولید گندله کگهر در شهریور ۱۴۰۵ چقدر بوده؟
- **Extracted intent:** measure `metric_monthly_production_quantity`; company کگهر (UUID `36b9d9da-3334-4d83-99fb-c7eac0fb5939`); product گندله, resolved within that company; Shahrivar 1405; direct value.
- **Generated query plan:** monthly production quantity; verified company and product key `NoavaranCurrentApi:feature141-kgehr:TITLE:گندله|UNIT:ton`; period `2026-08-23..2026-09-22`; scalar; no aggregation.
- **SQL execution:** PostgreSQL queried; `Succeeded`.
- **Expected result:** 47 ton in Shahrivar 1405, independently specified by the fixture.
- **Actual result:** 47 ton.
- **Status / failure reason:** **PASS**; none.
- **Source provenance:** `NoavaranCurrentApi`, report `fixture-kgehr-shahrivar-1405`; row fingerprint `row-fixture-kgehr-shahrivar-1405-NoavaranCurrentApi:feature141-kgehr:TITLE:گندله|UNIT:ton`.
- **Execution mode:** Live LLM + isolated PostgreSQL fixture.
- **Generated Persian answer (current deterministic renderer):**
  > کگهر گندله در بازهٔ ۲۰۲۶-۰۸-۲۳ تا ۲۰۲۶-۰۹-۲۲، مقدار تولید ماهانه: ۴۷ تن.
  > منبع: NoavaranCurrentApi، گزارش 8c3ffdbf-5239-4d3d-b3fe-99d317ed365d، fixture-kgehr-shahrivar-1405.
- **Trace:** live replay JSONL, line 1, case `monthly-02-production-product-month`.

### 2. Month-over-month production growth

- **Original Persian question:** تولید کنسانتره کگل نسبت به ماه قبل چند درصد رشد کرده؟
- **Extracted intent:** production quantity; کگل; product کنسانتره, company-scoped; latest month versus preceding eligible month; month-over-month growth percentage.
- **Generated query plan:** `metric_monthly_production_quantity`, `GrowthPercentage`, `MonthOverMonth`, latest two periods, product key `NoavaranCurrentApi:feature141-kgel:TITLE:کنسانتره|UNIT:ton`.
- **SQL execution:** PostgreSQL queried; `Succeeded`.
- **Expected result:** current production 12 ton versus previous eligible value 6 ton; `(12−6)/6 × 100 = 100%`.
- **Actual result:** 12 ton; 100.00000000000000000000% growth. The current report is identified; the SQL result exposes the previous value and period end but does not return the prior report identity.
- **Status / failure reason:** **PASS**; none.
- **Source provenance:** `NoavaranCurrentApi`; current report `fixture-kgel-shahrivar-1405`; current row fingerprint `row-fixture-kgel-shahrivar-1405-NoavaranCurrentApi:feature141-kgel:TITLE:کنسانتره|UNIT:ton`; previous observation ended 2026-08-31, value 6.
- **Execution mode:** Live LLM + isolated PostgreSQL fixture.
- **Generated Persian answer (current deterministic renderer):**
  > کگل کنسانتره، مقدار تولید ماهانه در دورهٔ جاری ۱۲ تن و در دورهٔ مبنا ۶ تن بوده است؛ مقدار ثبت‌شده برای تغییر درصدی: ۱۰۰ درصد.
  > منبع: گزارش 531d2dcc-d758-450b-98a4-0fb1ecea1bfc، fixture-kgel-shahrivar-1405.
- **Trace:** live replay JSONL, line 2, case `monthly-03-production-mom-growth`.

### 3. Product ranking by production growth

- **Original Persian question:** کدام محصولات کگهر در سه ماه اخیر بیشترین رشد تولید را داشته‌اند؟
- **Extracted intent:** production quantity; company کگهر; rank products by month-over-month growth across the latest three periods; descending top-N table.
- **Generated query plan:** `metric_monthly_production_quantity`; `SUM` at product grain; `GrowthPercentage` / `MonthOverMonth`; latest 3 periods; ranked table, deterministic tie ordering.
- **SQL execution:** PostgreSQL queried; `Succeeded`.
- **Expected result:** گندله rose from 35 ton in the previous eligible observation to 47 ton in the latest one: 34.2857142857142857…%.
- **Actual result:** current value 47 ton; 34.28571428571428571400%. This is within the test tolerance of 0.0001 percentage point.
- **Status / failure reason:** **PASS**; none.
- **Source provenance:** `NoavaranCurrentApi`; current report `fixture-kgehr-shahrivar-1405`; current row fingerprint `row-fixture-kgehr-shahrivar-1405-NoavaranCurrentApi:feature141-kgehr:TITLE:گندله|UNIT:ton`; previous value 35, previous period end 2026-08-31 (prior report identity is not returned by the comparison projection).
- **Execution mode:** Live LLM + isolated PostgreSQL fixture.
- **Generated Persian answer (current deterministic renderer):**
  > رتبه‌بندی بر اساس رشد مقدار تولید ماهانه: رتبهٔ ۱: کگهر گندله، ۳۴٫۲۸۵۷۱۴۲۸۵۷۱۴۲۸۵۷۱۴ درصد
  > منبع: گزارش 8c3ffdbf-5239-4d3d-b3fe-99d317ed365d، fixture-kgehr-shahrivar-1405.
- **Trace:** live replay JSONL, line 3, case `monthly-04-production-top-products-growth`.

### 4. Monthly sales amount

- **Original Persian question:** فروش ریالی کگل در شهریور ۱۴۰۵ چقدر بوده؟
- **Extracted intent:** monthly sales amount (`metric_monthly_sales`); کگل; Shahrivar 1405; direct company total aggregated by sum.
- **Generated query plan:** catalog monetary measure in MillionIRR; company-scoped report rows; exact period `2026-08-23..2026-09-22`; `SUM`, scalar.
- **SQL execution:** PostgreSQL queried; `Succeeded`.
- **Expected result:** 850 million IRR, independently seeded as 450 + 400.
- **Actual result:** 850 million IRR.
- **Status / failure reason:** **PASS**; none.
- **Source provenance:** `NoavaranCurrentApi`, `fixture-kgel-shahrivar-1405`; two row fingerprints for the two fixture products are persisted in the trace.
- **Execution mode:** Live LLM + isolated PostgreSQL fixture.
- **Generated Persian answer (current deterministic renderer):**
  > کگل در بازهٔ ۲۰۲۶-۰۸-۲۳ تا ۲۰۲۶-۰۹-۲۲، مبلغ فروش ماهانه: ۸۵۰ میلیون ریال.
  > منبع: گزارش 531d2dcc-d758-450b-98a4-0fb1ecea1bfc، fixture-kgel-shahrivar-1405.
- **Trace:** live replay JSONL, line 4, case `monthly-05-sales-amount-month`.

### 5. Sales quantity for a company-scoped product

- **Original Persian question:** مقدار فروش گندله کگهر در آخرین گزارش ماهانه چقدر است؟
- **Extracted intent:** monthly sales quantity; کگهر; product گندله; latest eligible report; direct value.
- **Generated query plan:** `metric_monthly_sales_quantity`; company UUID and company-scoped product key; latest reporting period; scalar.
- **SQL execution:** PostgreSQL queried; `Succeeded`.
- **Expected result:** 40 ton in the latest eligible report.
- **Actual result:** 40 ton.
- **Status / failure reason:** **PASS**; none.
- **Source provenance:** `NoavaranCurrentApi`, `fixture-kgehr-shahrivar-1405`; row fingerprint `row-fixture-kgehr-shahrivar-1405-NoavaranCurrentApi:feature141-kgehr:TITLE:گندله|UNIT:ton`.
- **Execution mode:** Live LLM + isolated PostgreSQL fixture.
- **Generated Persian answer (current deterministic renderer):**
  > کگهر گندله در بازهٔ ۲۰۲۶-۰۸-۲۳ تا ۲۰۲۶-۰۹-۲۲، مقدار فروش ماهانه: ۴۰ تن.
  > منبع: NoavaranCurrentApi، گزارش 8c3ffdbf-5239-4d3d-b3fe-99d317ed365d، fixture-kgehr-shahrivar-1405.
- **Trace:** live replay JSONL, line 5, case `monthly-06-sales-quantity-product-latest`.

### 6. Six-month sales time series

- **Original Persian question:** فروش ریالی کگل در شش ماه گذشته چه روندی داشته؟
- **Extracted intent:** monthly sales amount; کگل; six latest eligible reporting periods; ordered time series, no growth formula.
- **Generated query plan:** `metric_monthly_sales`; `TimeSeries`; latest six periods; ascending reporting-period output; MillionIRR.
- **SQL execution:** PostgreSQL queried; `Succeeded`.
- **Expected result:** `[120, 150, 200, 250, 300, 850]` million IRR, in period order.
- **Actual result:** `[120, 150, 200, 250, 300, 850]` million IRR.
- **Status / failure reason:** **PASS**; none. `Chart`/`TimeSeries` plan contract alignment was required for this case.
- **Source provenance:** `NoavaranCurrentApi`; reports `fixture-kgel-trend-2`, `fixture-kgel-trend-3`, `fixture-kgel-trend-4`, `fixture-kgel-trend-5`, `fixture-kgel-trend-6`, and `fixture-kgel-shahrivar-1405`; all row fingerprints are persisted in the trace.
- **Execution mode:** Live LLM + isolated PostgreSQL fixture.
- **Generated Persian answer (current deterministic renderer):**
  > روند مبلغ فروش ماهانه بر اساس دوره‌های گزارش‌شده: در بازهٔ ۲۰۲۶-۰۴-۰۱ تا ۲۰۲۶-۰۴-۳۰ ۱۲۰ میلیون ریال
  > منبع: گزارش c9bf75a2-2783-48fe-8398-aba1ae797781، fixture-kgel-trend-2؛ در بازهٔ ۲۰۲۶-۰۵-۰۱ تا ۲۰۲۶-۰۵-۳۱ ۱۵۰ میلیون ریال
  > منبع: گزارش 37985f48-6647-449f-9f8c-b60c5b2acb04، fixture-kgel-trend-3؛ در بازهٔ ۲۰۲۶-۰۶-۰۱ تا ۲۰۲۶-۰۶-۳۰ ۲۰۰ میلیون ریال
  > منبع: گزارش 5301b34b-a408-4da4-b6f9-700f79bb39d6، fixture-kgel-trend-4؛ در بازهٔ ۲۰۲۶-۰۷-۰۱ تا ۲۰۲۶-۰۷-۳۱ ۲۵۰ میلیون ریال
  > منبع: گزارش cf3032b9-b141-4919-b13d-5a65a9660f4e، fixture-kgel-trend-5؛ در بازهٔ ۲۰۲۶-۰۸-۰۱ تا ۲۰۲۶-۰۸-۳۱ ۳۰۰ میلیون ریال
  > منبع: گزارش 74af7f1a-c6e9-4dcf-a8b5-d478f43a73e9، fixture-kgel-trend-6؛ در بازهٔ ۲۰۲۶-۰۸-۲۳ تا ۲۰۲۶-۰۹-۲۲ ۸۵۰ میلیون ریال
  > منبع: گزارش 531d2dcc-d758-450b-98a4-0fb1ecea1bfc، fixture-kgel-shahrivar-1405.
- **Trace:** live replay JSONL, line 6, case `monthly-08-sales-six-month-trend`.

### 7. Cross-company product ranking without an authorized population

- **Original Persian question:** کدام شرکت‌های تولیدکننده گندله بیشترین مقدار فروش را داشته‌اند؟
- **Extracted intent:** sales quantity; a company population described as pellet producers; product گندله; ranking; the requested period is latest/common.
- **Generated query plan:** candidate `metric_monthly_sales_quantity`; ranked table; no resolved company population or verified company-scoped product key.
- **SQL execution:** No. Planning validation stopped before compilation.
- **Expected result:** No numeric fixture result is asserted. A numeric ranking requires an explicitly authorized company set or an approved industry/category population and reviewed product equivalence semantics.
- **Actual result:** typed planning failure with `monthly_product_reference_missing:CompanyOrSymbol` and `monthly_product_binding_required`.
- **Status / failure reason:** **UNSUPPORTED**, stage `planning_validation`; unrestricted company discovery and equal Persian product names are not treated as authorization or equivalence.
- **Source provenance:** none; no report was queried.
- **Execution mode:** Live LLM navigation/planning + fixture context; PostgreSQL was not queried.
- **Generated Persian answer (current deterministic renderer):**
  > این درخواست با تعریف‌های تأییدشدهٔ پرس‌وجو قابل اجرا نیست.
- **Trace:** live replay JSONL, line 7, case `monthly-09-cross-company-product-sales`.

### 8. Cross-company sales comparison using latest common month

- **Original Persian question:** فروش ماهانه کگل و کگهر را با هم مقایسه کن.
- **Extracted intent:** monthly sales amount; explicit companies کگل and کگهر; company-level sum; default latest common eligible month; side-by-side comparison.
- **Generated query plan:** `metric_monthly_sales`; two independently resolved company UUIDs; `SUM`; `MultiCompanyComparison`; latest common reporting period; no product-equivalence mapping needed for monetary company totals.
- **SQL execution:** PostgreSQL queried; `Succeeded`; the result was checked against each company UUID, independent of row order.
- **Expected result:** Shahrivar 1405 (2026-08-23..2026-09-22): کگل 850 million IRR, کگهر 700 million IRR.
- **Actual result:** کگل 850 million IRR; کگهر 700 million IRR. SQL row order was کگهر then کگل.
- **Status / failure reason:** **PASS**; none. The planner defaulted a valid latest-common policy after both company bindings and the cataloged sum shape were verified.
- **Source provenance:** `NoavaranCurrentApi`; `fixture-kgel-shahrivar-1405` and `fixture-kgehr-shahrivar-1405`; company UUIDs and per-row source fingerprints appear in the trace.
- **Execution mode:** Live LLM + isolated PostgreSQL fixture.
- **Generated Persian answer (current deterministic renderer):**
  > مقایسهٔ مبلغ فروش ماهانه: کگهر ۷۰۰ میلیون ریال (در بازهٔ ۲۰۲۶-۰۸-۲۳ تا ۲۰۲۶-۰۹-۲۲)
  > منبع: گزارش 8c3ffdbf-5239-4d3d-b3fe-99d317ed365d، fixture-kgehr-shahrivar-1405؛ کگل ۸۵۰ میلیون ریال (در بازهٔ ۲۰۲۶-۰۸-۲۳ تا ۲۰۲۶-۰۹-۲۲)
  > منبع: گزارش 531d2dcc-d758-450b-98a4-0fb1ecea1bfc، fixture-kgel-shahrivar-1405.
- **Trace:** live replay JSONL, line 8, case `monthly-10-company-sales-comparison`.

## Deterministic PostgreSQL examples

These were executed through the typed plan, deterministic compiler and restricted PostgreSQL executor. They are not LLM-generated plans. Full plan, SQL text, parameters, fixture identities and raw rows are in [`cross-company-postgresql-examples-v2-2026-10-09.jsonl`](../artifacts/cross-company-postgresql-examples-v2-2026-10-09.jsonl). They use the same relationship and catalog hash noted above.

### 9. Two-company comparison at latest common eligible month

- **Original Persian question:** فروش ماهانهٔ شرکت‌های MTH و MTH2 را در آخرین ماه مشترک با هم مقایسه کن.
- **Extracted intent:** `metric_monthly_sales`; companies MTH and MTH2; company total `SUM`; latest common month; comparison table.
- **Generated query plan:** two independently bound fixture UUIDs; monthly sales sum; latest-common period intersection; table, limit 10.
- **SQL execution:** PostgreSQL queried; `Succeeded`. SQL contains an intersection grouped by period with `HAVING COUNT(DISTINCT CompanyId) = 2`.
- **Expected result:** common period ending 2026-04-20: MTH 130 and MTH2 200 million IRR, computed independently by summing each company’s seeded eligible sales rows.
- **Actual result:** MTH 130; MTH2 200 million IRR. Output row `SourceUnit` is a quantity-unit field and is not the amount unit; catalog unit is MillionIRR.
- **Status / failure reason:** **PASS**; none.
- **Source provenance:** `NoavaranCurrentApi`; `fixture-2026-03` and `fixture-company-2-2026-03`; raw report IDs and source fingerprints are in the artifact.
- **Execution mode:** Deterministic replay + isolated PostgreSQL fixture.
- **Generated Persian answer (current deterministic renderer):**
  > مقایسهٔ مبلغ فروش ماهانه: MTH ۱۳۰ میلیون ریال (در بازهٔ ۲۰۲۶-۰۳-۲۱ تا ۲۰۲۶-۰۴-۲۰)
  > منبع: گزارش 2f52b3a4-f079-47ee-8a73-49c9331d0a85، fixture-2026-03؛ MTH2 ۲۰۰ میلیون ریال (در بازهٔ ۲۰۲۶-۰۳-۲۱ تا ۲۰۲۶-۰۴-۲۰)
  > منبع: گزارش 23762e39-3956-4944-91eb-88cbafa6f9ad، fixture-company-2-2026-03.
- **Trace:** cross-company JSONL, line 1.

### 10. Top-N ranking for an explicit company population

- **Original Persian question:** در آخرین ماه مشترک، شرکت‌های MTH و MTH2 را بر اساس مبلغ فروش ماهانه رتبه‌بندی کن.
- **Extracted intent:** monthly sales amount; explicit companies MTH and MTH2; company-level sum; latest common period; top 2 descending.
- **Generated query plan:** same bound company set and measure as example 9; `RankedTable`; tie break by stable company UUID.
- **SQL execution:** PostgreSQL queried; `Succeeded`; executed SQL orders by amount descending, then company UUID ascending.
- **Expected result:** MTH2 first at 200 million IRR; MTH second at 130 million IRR, period ending 2026-04-20.
- **Actual result:** `[200, 130]` million IRR in the expected ranking order.
- **Status / failure reason:** **PASS**; none.
- **Source provenance:** `NoavaranCurrentApi`; `fixture-2026-03` and `fixture-company-2-2026-03`.
- **Execution mode:** Deterministic replay + isolated PostgreSQL fixture.
- **Generated Persian answer (current deterministic renderer):**
  > رتبه‌بندی بر اساس مبلغ فروش ماهانه: رتبهٔ ۱: MTH2، ۲۰۰ میلیون ریال
  > منبع: گزارش 23762e39-3956-4944-91eb-88cbafa6f9ad، fixture-company-2-2026-03؛ رتبهٔ ۲: MTH، ۱۳۰ میلیون ریال
  > منبع: گزارش 2f52b3a4-f079-47ee-8a73-49c9331d0a85، fixture-2026-03.
- **Trace:** cross-company JSONL, line 2.

### 11. Comparison where no common month exists

- **Original Persian question:** فروش ماهانهٔ شرکت‌های MTH و MTH2 را برای ماه گزارش منتهی به ۲۱ مه ۲۰۲۶ مقایسه کن.
- **Extracted intent:** monthly sales amount; companies MTH and MTH2; exact report period ending 2026-05-21; company-level sum; comparison.
- **Generated query plan:** explicit period bounds; common-period intersection across both company UUIDs; no latest-month substitution.
- **SQL execution:** PostgreSQL queried; returned typed `NoData` and zero rows because MTH2 has no eligible report for that exact period.
- **Expected result:** typed `NoData`, independently established from fixture report periods.
- **Actual result:** `NoData`; no numeric values returned.
- **Status / failure reason:** **PASS** for the expected safe outcome; none.
- **Source provenance:** no eligible shared report identity exists for the requested interval; fixture period rows and SQL are in the trace.
- **Execution mode:** Deterministic replay + isolated PostgreSQL fixture.
- **Generated Persian answer (current deterministic renderer):**
  > برای MTH و MTH2 در بازهٔ ۲۰۲۶-۰۴-۲۱ تا ۲۰۲۶-۰۵-۲۱ دادهٔ واجد شرایطی پیدا نشد.
- **Trace:** cross-company JSONL, line 3.

### 12. Year-over-year sales growth

- **Original Persian question:** فروش ماهانهٔ محصول آزمایشی MTH در اردیبهشت ۱۴۰۵ نسبت به اردیبهشت ۱۴۰۴ چند درصد رشد کرده است؟
- **Extracted intent:** monthly sales amount; fixture company MTH and its persisted fixture product; Ordibehesht 1405 versus Ordibehesht 1404; year-over-year growth.
- **Generated query plan:** `metric_monthly_sales`; `GrowthPercentage` / `YearOverYear`; 13 latest eligible periods; verified product key `PRODUCT:MONTHLY-FIXTURE:PELLET`.
- **SQL execution:** PostgreSQL queried; `Succeeded`; adjacent matching-year rows checked by period.
- **Expected result:** fixture amounts are 75 million IRR for `2025-04-21..2025-05-20` and 250 million IRR for `2026-04-21..2026-05-21`. Their month-end dates differ by one day, so the fixture does not establish equivalent reporting-window boundaries; the expected growth is not accepted as financially verified.
- **Actual result:** SQL returned 233.3333333333333300%, current amount 250 million IRR and previous amount 75 million IRR.
- **Status / failure reason:** **AMBIGUOUS**, evaluation stage `post-execution period-alignment audit`. The pipeline and PostgreSQL executor reported success, but their calendar-month `LAG` comparison did not validate equal report-window boundaries.
- **Source provenance:** `NoavaranCurrentApi`; reports `fixture-yoy-2025-04` and `fixture-2026-04`; raw row fingerprints are in the trace.
- **Execution mode:** Deterministic replay + isolated PostgreSQL fixture.
- **Generated Persian answer (current deterministic renderer):**
  > داده‌های موجود برای ارائهٔ یک پاسخ مالی یکتا کافی نیستند؛ نتیجه مبهم است.
- **Trace:** cross-company JSONL, line 4.

## Remaining gaps

- The natural-language company-population ranking remains unsupported without an authorized explicit company population or approved industry/category membership, plus reviewed product equivalence semantics. It safely stopped before SQL; no company discovery shortcut was used.
- Cross-company comparisons of product quantities are not enabled by matching Persian names. The catalog still needs owner-reviewed product category/equivalence mappings and compatible-unit rules.
- Development data was deliberately not queried. Fixture success does not imply the pending canonical catalog is production-approved.
- Provider finish reason and retry reasons are not exposed in the navigation result. The trace records the available provider/model, calls, token totals, latency, validation failure, SQL and typed executor outcome.
