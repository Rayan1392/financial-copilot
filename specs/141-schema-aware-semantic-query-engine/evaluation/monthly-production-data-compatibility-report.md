# Feature 141 — Monthly Production & Sales Data Compatibility (2026-10-29)

## Update 2: monthly product retrieval fix (2026-10-29)

Verdict: **FEATURE141_MONTHLY_PRODUCT_RETRIEVAL_PARTIAL** — see F11 for GO / NO-GO.

Summary:

- **Fixed and verified on real data:** single-company production and sales-quantity retrieval, the monthly trend, producer
  discovery and the requested month of the producer ranking. Every Feature 141 number matches independent read-only SQL.
- **Product identity:** Feature 141 now identifies a product by normalized `Title` + `Unit` (plus approved concept aliases) and has
  no functional dependency on `MonthlyReportLineItems.ProductCode` (F5).
- **Not answered to users yet:** the producer ranking stays refused because the `گندله` concept is approved for aliases only
  (product coverage `partial`). The existing sales specialist still answers some questions first, and once answered a
  sales-quantity question with a sales amount (F10).

### F1. Root causes and exact fixes

| # | Symptom on real data | Exact cause | Fix |
|---|---|---|---|
| A | Single-company production / sales quantity: false NoData | `CompileMonthly` filtered `li."ProductCode" = <Feature 129 resolver key>`. Real `ProductCode` is a per-row hash (`PRODUCT:NATURAL:…`) that changes monthly and repeats across companies, so it never matched. Fixtures stored `ProductCode` = resolver key. | The binder returns the product's source labels (every persisted `Title` / `Unit` spelling that the Feature 129 normalizer maps to it, plus approved aliases); the compiler matches `Title = ANY(@product_titles) AND COALESCE(Unit,'') = ANY(@product_units)`. `ProductCode` was removed from the monthly mapping, SQL, grouping, partitions, ordering, discovery tie-breaks and output columns. |
| B | `retrieval_requirements_incomplete` for «تولید گندله کگل در شهریور ۱۴۰۵» (not for «مقدار فروش …») | The requirements model is shown a question-ranked catalog outline. The outline scorer ignores one-token aliases (`تولید`), and no multi-word production alias (`مقدار تولید`, `تولید ماهانه`) occurs in the question, so the production measure was not in the outline. The model then set `unknownOrAmbiguous` with the reason "The catalog outline contains no canonical production measure …", and completeness returned `ClarificationRequired`. | The chat service passes the typed monthly measure of the canonical intent (`productionQuantity` → `metric_monthly_production_quantity`, likewise sales quantity / amount) as `RequiredCatalogIds` for named-company intents (unbound discovery keeps its deterministic planner rewrite); the outline provider already supports required IDs. The validator now traces `retrieval_requirements_status:<status>` and every `retrieval_requirement_missing:<item>`; the chat log adds plan shape, period and the model's uncertainty (never rendered to users). |
| C | Trend: `direct_value_operation_kind_mismatch` (run 1), then false NoData (run 2) | Only `ExplicitMonth` periods were normalized deterministically. A `DateRange` kept the model's Gregorian bounds, which were sometimes missing (a time series without bounds is rejected), and the series then hit cause A. | `PersianReportingPeriodNormalizer.NormalizeRange` bounds «از فروردین تا شهریور ۱۴۰۵» deterministically (a start month without a year takes the end month's year); `ReportingPeriodRequirementsNormalizer.Apply(first, last)` replaces the model's bounds and period count. |
| D | Producer ranking used a 12-month window for «… در مرداد ۱۴۰۵» | The producer-sales branch of the planner rebuilt `StructuredShape` without the normalized month and cleared `PeriodPolicies`, so the trailing-window default applied. The compiler's sales period was a range. | The branch keeps the explicit month and `explicit-period` (as the production-discovery branch already did); the compiler uses exact `PeriodStart = @period_start AND PeriodEnd = @period_end` for producers and sales when the policy is `explicit-reporting-period-v1`. |
| E | Answers showed company names without symbols | Compilers preferred `Companies.Ticker`, which is empty for every Noavaran company; `CompanySymbol` was not catalog-approved. | Catalog approves `Companies.CompanySymbol` (Companies table `approvedObjects`; catalog hash `425b4087…`). One helper prefers `CompanySymbol` (Ticker only when CompanySymbol is not approved) for discovery, producer ranking and statement populations. Monthly single-company / comparison rows now also project `CompanyName` and `CompanySymbol`, so the renderer prints «نام (نماد)». |

### F2. Changed production files

- `Application/.../Navigation/StructuredQueryCompilation.cs`: label-based product predicates; `ProductIdentity` replaces `ProductCode` in partitions, grouping, LAG windows and ordering; `ProductCodeColumnCatalogId` removed from `StructuredSqlMonthlyMeasure`; discovery tie-break by title + source row fingerprint; exact explicit month for producer ranking; `CompanySymbolColumn` helper; company labels on monthly rows; equivalence members matched by their reviewed label.
- `Application/.../Navigation/StructuredQueryCatalogSnapshotBuilder.cs`: monthly measures no longer map `ProductCode`.
- `Application/.../Navigation/SchemaNavigationContracts.cs`: `MonthlyProductSelection`; `StructuredProductBinding.Selection`; `SchemaNavigationRequest.RequiredCatalogIds`.
- `Application/.../Navigation/StructuredQueryPlanning.cs`: `StructuredQueryPlan.ProductSelection` carried from the binding; explicit month kept in the producer-sales rewrite; completeness status / missing items traced.
- `Application/.../Navigation/LlmVectorlessSchemaNavigator.cs`: requirements outline includes `RequiredCatalogIds`.
- `Application/.../Navigation/ReportingPeriodRequirementsNormalizer.cs`, `Application/.../Intent/PersianReportingPeriodNormalizer.cs`: month ranges.
- `Application/.../Navigation/ReviewedSemanticScopeCatalogs.cs`: an equivalence member needs its label, not a company product key.
- `Application/.../Navigation/PersianStructuredQueryResponseGenerator.cs`: `ProductCode` removed from display facts.
- `Infrastructure/Financial/Semantics/StructuredQueryProductBinder.cs`: title/unit identity key and source labels (Feature 129 resolver unchanged).
- `Infrastructure/AI/OrchestrationV2/Workflow/Feature141ChatQueryService.cs`: required monthly measure, month-range normalization, richer non-eligibility trace.
- `specs/.../SemanticCatalog/tables/tbl_8646d83cdb350f92.yaml`: `CompanySymbol` approved (provenance `product-owner-directive:Feature141-monthly-product-retrieval-fix-2026-10-29`); `artifacts/navigation-hierarchy.*` and `manifest.yaml` regenerated (`catalog.py hierarchy build` + `validate`).

Not changed: database schema, migrations, data, Noavaran ingestion, the Feature 129 resolver and repository, statement mapping, billing, authorization architecture, `Feature141:Enabled` (still `false`).

### F3. Resolver → compiler contract

| | Before | After |
|---|---|---|
| Binder output | `ProductKey` = Feature 129 key `{Provider}:{Ext}:CODE:…` / `ID:…` / `TITLE:…\|UNIT:…` | `ProductKey` = `{Ext}:TITLE:<normalized title>\|UNIT:<normalized unit>` (identity only) and `Selection` = `MonthlyProductSelection(SourceTitles, SourceUnits)` |
| Plan | `PlanEntityReference.ResolvedEntityKey` | same key, plus `StructuredQueryPlan.ProductSelection` |
| SQL | `li."ProductCode" = @product_code` | `li."Title" = ANY(@product_titles) AND COALESCE(li."Unit", '') = ANY(@product_units)` |
| Same product, two units | Ambiguous (binder) | unchanged: Ambiguous, no unit is chosen |
| Two matching rows in one company/month | depended on codes | `SamePeriodCandidateCount > 1` → Ambiguous (no double counting) |
| Missing labels | n/a | compiler refuses `monthly_product_source_labels_missing` |

Source labels are built from the company's accepted single-month `ProductSales` rows: rows whose normalized unit equals the product's
and whose normalized title is the product's title or, when that title is an alias of an approved concept, any of the concept's
aliases. Real example: کچاد reports `گندله` with units `"تن"` and `"تن "` (trailing space); both spellings are labels of one product.

### F4. Generated SQL and parameters (real run P04, «تولید گندله کگل در آخرین ماه گزارش‌شده چقدر بوده؟»)

```sql
WITH latest_month AS (SELECT <Companies.Id of r0> AS "CompanyId", MAX(r0."PeriodEnd") AS "PeriodEnd"
  FROM "public"."MonthlyReports" r0
  WHERE (r0."ProviderName", r0."ExternalCompanyId") IN (SELECT mc."ProviderName", mc."ExternalCompanyId" FROM "public"."Companies" mc WHERE mc."Id" = @company_id)
    AND r0."ProviderName" = @provider_name AND r0."ReportType" = @report_type AND r0."OutputType" = @output_type
    AND r0."IsAccepted" = @is_accepted AND r0."RevisionStatus" = @revision_status
    AND EXISTS (SELECT 1 FROM "public"."MonthlyReportLineItems" li0 WHERE li0."MonthlyReportId" = r0."Id"
                AND li0."Title" = ANY(@product_titles) AND COALESCE(li0."Unit", '') = ANY(@product_units))
  GROUP BY <Companies.Id of r0>),
eligible_monthly_rows AS (SELECT <Companies.Id of r> AS "CompanyId", <Companies.Name of r> AS "CompanyName",
    <Companies.CompanySymbol of r> AS "CompanySymbol", r."PeriodStart", r."PeriodEnd", @product_identity::text AS "ProductIdentity",
    li."Title" AS "ProductTitle", li."ProductionQuantity" AS "Value", li."Unit" AS "SourceUnit",
    COUNT(*) OVER (PARTITION BY <Companies.Id of r>, r."PeriodEnd") AS "SamePeriodCandidateCount", …
  FROM "public"."MonthlyReportLineItems" li JOIN "public"."MonthlyReports" r ON li."MonthlyReportId" = r."Id"
  WHERE <same company/provider/report-type/OutputType/accepted/revision predicates>
    AND li."Title" = ANY(@product_titles) AND COALESCE(li."Unit", '') = ANY(@product_units)
    AND EXISTS (SELECT 1 FROM latest_month lm WHERE lm."CompanyId" = <Companies.Id of r> AND lm."PeriodEnd" = r."PeriodEnd"))
SELECT … ORDER BY "CompanyId", "PeriodEnd" ASC, "ProductIdentity", "SourceMultiplicity" LIMIT @row_limit OFFSET @row_offset
```

`<Companies.X of r>` = `(SELECT mc."X" FROM "public"."Companies" mc WHERE mc."ExternalCompanyId" = r."ExternalCompanyId" AND mc."ProviderName" = r."ProviderName")` (provider-aware mapping, unchanged).

Parameters: `provider_name = NoavaranCurrentApi`, `report_type = ProductSales`, `output_type = 0`, `is_accepted = true`,
`revision_status = Accepted`, `company_id = <کگل Companies.Id>`, `product_titles = ["گندله"]`, `product_units = ["تن"]`,
`product_identity = "4:TITLE:گندله|UNIT:تن"`. Explicit months add `period_start` / `period_end` (e.g. P22: 2026-08-23 / 2026-09-22).

### F5. Proof of zero `ProductCode` dependency

- **Code:** `grep -rn "ProductCode\|product_code"` over `Application/FinancialData/Semantics`, `Infrastructure/Financial/Semantics`
  and `Feature141*.cs` returns only comments stating the rule. The binder reads neither `ProductCode`, `ProviderProductCode` nor `ProviderProductId`.
- **Catalog snapshot:** `StructuredSqlMonthlyMeasure` has no product-code column; no compiled monthly query lists `*_ProductCode` among
  its physical objects (unit test `MonthlyCatalogSnapshotAndEveryMonthlyQueryShape_HaveNoProductCodeDependency`: scalar, series, growth, product ranking).
- **Critical acceptance test (PostgreSQL):** `MonthlyStructuredQueryPostgreSqlTests` executes 14 compiled monthly queries (single company,
  latest, series, MoM / absolute / YoY comparisons, product rankings, unit-mix and duplicate-report ambiguity, cross-company equivalence
  ranking, producer discovery), rewrites **every** persisted `ProductCode` (30% `'0'`, otherwise random hashes), re-executes them and asserts
  identical status, diagnostics and rows. It passes.
- **Fixtures:** `ProductCode` is now production-like everywhere (cycled `'0'` and two hashes shared across companies and products, or one hash for all rows); the same assertions still pass.
- **Feature 129** (unchanged) still projects `ProductCode` in its catalog read; the value is not part of its product key and is not used by Feature 141.

### F6. Real data: expected vs actual

Expected values: independent read-only SQL as `feature141_app_reader` (accepted, `RevisionStatus = Accepted`, `OutputType = 0`,
company by `(ProviderName, ExternalCompanyId)`, product by title; one source row per company/month in every case).
"Specialist" = the existing chat specialist answered before Feature 141; "F141 direct" = the Feature 141 service the chat fallback calls, run without the specialist.

| Question | Expected (SQL) | Before this fix | After (final runs 19:36–19:46) | Path |
|---|---|---|---|---|
| P22 کگل گندله production, شهریور ۱۴۰۵ | 1,085,157 تن | refused (`retrieval_requirements_incomplete`) | **1,085,157 تن**, «معدنی و صنعتی گل گهر (کگل)» | Feature 141 (chat) |
| P23 کگل گندله sales quantity, شهریور ۱۴۰۵ | 1,049,345 تن | false NoData | **1,049,345 تن** (chat, 19:37 and direct) | Feature 141 (chat) / F141 direct |
| P23 (runs 19:26, 19:31) | 1,049,345 تن | — | specialist answered «۱۵٬۴۰۹٫۵ میلیارد تومان» (an amount) | specialist — wrong measure, F10.2 |
| P24 کگل sales amount, شهریور ۱۴۰۵ | 154,095,165 million IRR | specialist correct | **154,095,165 میلیون ریال** (chat and direct) | Feature 141 (chat) / F141 direct |
| P04 کگل گندله production, latest month | 1,085,157 تن (شهریور) | false NoData | **1,085,157 تن, شهریور ۱۴۰۵** | Feature 141 (chat) |
| P25 کگهر گندله production, مرداد ۱۴۰۵ | 512,902 تن | refused | **512,902 تن**, «معدنی و صنعتی گهر زمین (کگهر)» | Feature 141 (chat) |
| P05 کگهر sales amount, latest month | 39,580,764 million IRR (مرداد) | specialist correct | specialist «۳٬۹۵۸٫۱ میلیارد تومان» (= 39,580,764 M IRR, correct); F141 direct **39,580,764 میلیون ریال, مرداد** | specialist / F141 direct |
| P26 کگل گندله production trend, فروردین–شهریور ۱۴۰۵ | 760,456 → 1,085,157 تن (6 months) | refused / false NoData | **6 rows, exact** (F7) | Feature 141 (chat) |
| P27 گندله producers, مرداد ۱۴۰۵ | کگل 1,033,321; کگهر 512,902; کچاد 383,727 تن | exact (names only) | **exact, with symbols**, disclosed as partial list | Feature 141 (chat) |
| P09 گندله producers, no period | latest month per producer in the trailing 12 months | exact (names only) | **کچاد 388,911 (شهریور), کگل 1,085,157 (شهریور), کگهر 512,902 (مرداد)**, with symbols | Feature 141 (chat) |
| P28 producer ranking by گندله sales, مرداد ۱۴۰۵ | کگل 136,725,376 > کگهر 39,580,764 > کچاد 23,273,196 M IRR | 12-month window | **مرداد only, rows exact**; rank not stated (coverage partial, F7) | Feature 141 (chat) |
| P29 year-to-date production | must not answer | typed Unsupported | typed Unsupported (`monthly_cumulative_reporting_scope_not_supported`), 0 queries; chat shows the generic refusal | Feature 141 (chat) |
| P16 وبملت net profit, standalone, latest | 1,025,278,132 million IRR | 1,025,278,132 | **1,025,278,132** (statement path unchanged) | Feature 141 (chat) |
| P11 وبملت net profit, basis unstated | Ambiguous (both bases exist) | Ambiguous | Ambiguous (unchanged) | Feature 141 (chat) |

Cumulative values never appeared: OutputType 1 / 3 / 4 values for the same months (e.g. کگل 8,597,877 / 7,512,720 تن, 986,809,838 M IRR; کگهر 4,006,084 تن) occur in no answer; every executed monthly query bound `output_type = 0`, `is_accepted = true`, `revision_status = Accepted`.
Units: quantities «تن» (row unit), amounts «میلیون ریال» (catalog unit). Jalali: شهریور ۱۴۰۵ = 2026-08-23..09-22, مرداد ۱۴۰۵ = 2026-07-23..08-22, فروردین–شهریور = 2026-03-21..09-22, P09 window «۷ آبان ۱۴۰۴ تا ۷ آبان ۱۴۰۵».

Regression found and fixed during verification: requiring the monthly measure in the outline also for **unbound** discovery let the
model select the measure with an extra sort, which the discovery compiler correctly refused (`population_discovery_unsupported_unbound_operator`,
P27 / P09 at 19:37–19:41). The requirement is now limited to named-company intents; P27 and P09 re-ran exact at 19:45–19:46 (P22 re-checked).

### F7. Trend and ranking verification

- **Trend (P26, «روند تولید ماهانه گندله کگل از فروردین تا شهریور ۱۴۰۵»):** the month range is normalized deterministically to
  2026-03-21..2026-09-22; Feature 141 returns six rows in chronological order: 760,456 / 1,112,124 / 1,071,093 / 1,042,566 / 1,033,321 / 1,085,157 تن,
  exactly the SQL values (فروردین..شهریور). Only `OutputType = 0`, accepted reports; one row per month.
- **Ranking (P28, «… بر اساس مبلغ فروش گندله در مرداد ۱۴۰۵ …»):** plan period 2026-07-23..2026-08-22, policy
  `explicit-reporting-period-v1`, SQL `pr."PeriodStart" = @period_start AND pr."PeriodEnd" = @period_end` and the same for sales.
  Rows: کگل 136,725,376 > کگهر 39,580,764 > کچاد 23,273,196 million IRR — exactly the SQL values. The answer still refuses to state a
  rank (`population_discovery_product_alias_coverage_incomplete`): the `product_iron_ore_pellets` concept is `approved-aliases-only`,
  so a rank over the producer population cannot be claimed. That is the intended guard, not a period problem.

### F8. Company symbols

- **Real data:** `Companies.Ticker` is empty for کگل, کگهر, کچاد and فملی; `CompanySymbol` holds the symbol.
- **Discovery (P27, P09) and ranking (P28):** rows carry `CompanySymbol` (کگل, کگهر, کچاد) and the answers render «معدنی و صنعتی گل گهر (کگل)», «معدنی و صنعتی گهر زمین (کگهر)», «معدنی و صنعتی چادرملو (کچاد)».
- **Single company / trend / company comparison:** monthly rows now project `CompanyName` and `CompanySymbol` and single-company answers now read «معدنی و صنعتی گل گهر (کگل) گندله در شهریور ۱۴۰۵، مقدار تولید ماهانه: ۱٬۰۸۵٬۱۵۷ تن» (P22, P23, P24, P25, P04).
- **Statement populations** use the same helper (fixtures X16–X18 verified).

### F9. Focused test summary

- **Unit (focused Feature 141 classes):** 188 passed, 9 failed. All 9 are in the pre-existing baseline list (catalog-state assertions:
  `Feature141DeveloperUatAuthorizationTests` ×2, `StructuredQueryCompilationTests` ×5, `SchemaNavigatorTests` ×2).
- **Unit (full suite):** 20 failed / 2,512 passed; none outside the pre-existing baseline list; two baseline tests now pass.
- **Architecture tests:** 11 passed, 1 failed (`MigratedSemanticExecutors_DoNotReintroduceLegacySymbolExtraction`, legacy symbol extraction in semantic executors; unrelated to Feature 141 and to the files changed here).
- **New unit tests:** title/unit predicates and `monthly_product_source_labels_missing`; zero-`ProductCode` across query shapes; binder
  source labels (unit spellings, approved aliases, `ProductCode` invariance, two units → Ambiguous); producer ranking keeps the explicit
  month (planner + compiler); month-range normalization; required monthly measure; real-catalog outline omits production without it.
- **PostgreSQL integration (`Feature141*`, `MonthlyStructuredQuery*`, `StructuredQuery*`, disposable PG18):** 39 passed, 1 failed, 76 skipped
  (live LLM / pilot). The failure is pre-existing: `CompiledMonthlyRetrievalAndTimeSeries…` line "runtime catalog leaves the query
  unauthorized" was already failing at 18:27, before this change (same assertion; the runtime catalog approves monthly measures since
  the core-capabilities slice). Every assertion before it passes, including the `ProductCode` rewrite.
- **Financial statement retrieval:** `FinancialStatementRetrievalExecutesThroughGenericPlanningCompilationAndPersianRendering`, cross-company
  statement comparison (X01–X18) and filters pass; real P11 / P16 in F6.
- **MAF V2 chat routing:** pilot runs through `IAiQueryOrchestrationService` (F6).
- **Fixture changes (Task G):** realistic distinct titles for distinct products in a report; production-like `ProductCode`; equivalence
  members matched by their source label; `CompanySymbol` populated in Feature 141 company fixtures.

### F10. Remaining blockers

1. **Ranking answer (owner decision):** product coverage for `گندله` is `approved-aliases-only`; the ranking is computed for the requested
   month but not stated. Enabling it needs a reviewed complete product mapping, or an owner rule allowing a partial-population rank with disclosure.
2. **Existing sales specialist (outside Feature 141):** for «مقدار فروش گندله کگل در شهریور ۱۴۰۵ چند تن بوده؟» the specialist answered
   «۱۵٬۴۰۹٫۵ میلیارد تومان», a sales **amount**, to a sales **quantity** question (expected 1,049,345 تن). Feature 141 is a fallback and
   did not run; Feature 141 itself answers it correctly (F6 direct). This is a pre-existing specialist defect.
3. **Billing (out of scope):** non-answers are still charged.
4. **Same title in two units** stays Ambiguous by design; no unit preference rule exists.
5. **Fiscal-year-to-date / corrections:** typed Unsupported (P29); cumulative-to-monthly subtraction not implemented.
6. **Pre-existing test debt:** the stale monthly authorization assertion and the 20 unit baseline failures.

### F11. GO / NO-GO

- **Feature 141 monthly single-company, trend and discovery: GO for a gated pilot** (flag stays off by default; enable per environment).
  Every number it returned matched the database; no cumulative value appeared.
- **Producer ranking: NO-GO** until the coverage decision (F10.1).
- **Overall monthly chat experience: NO-GO for general release** while the existing specialist can answer a quantity question with an
  amount before Feature 141 is reached (F10.2); routing changes were out of scope.

---

## Previous section: real monthly data verification (2026-10-29, before the product-retrieval fix)

Verdict: **FEATURE141_MONTHLY_PRODUCTION_FAILED**. Recommendation: **NO-GO** for Feature 141 monthly answers.

Summary:

- **Works on real data:** the company mapping, the `OutputType = 0` rule and the cumulative refusal. Producer discovery returns the
  correct companies and values.
- **Fails on real data:** every Feature 141 question about **one company's product** is either refused or reports
  "no qualifying data" when the data exists (false NoData). The cause is a product-identity mismatch (U2): the compiler
  matches the stored `ProductCode`, which is not a stable product identity in real Noavaran data.
- **Never happened:** no wrong number and no cumulative (`OutputType` 1–4) value was shown. Every incorrect outcome was a
  refusal or a false NoData.

### U1. Permissions (verified as `feature141_app_reader`)

- **Session:** `current_user` = `feature141_app_reader`.
- **Grants:** column-level SELECT on the 10 approved `MonthlyReports` columns and the 9 approved `MonthlyReportLineItems`
  columns (exactly the section 9 GRANT). There is no table-level grant on either table, and `INSERT`/`UPDATE`/`DELETE` are false.
- **Rows visible:** 41,739 `MonthlyReports` and 440,577 `MonthlyReportLineItems`.
- **Columns not readable** (correctly, since they are not granted): `CompanyId`, `ProviderProductCode`, `ProviderProductId`,
  `RevisionFingerprint`, `SourceRowKey`.

### U2. Root cause of the failure: `ProductCode` is not a product identity

- **What the compiler does:** `CompileMonthly` filters with `li."ProductCode" = @product_code`. `@product_code` is the
  plan's `ResolvedEntityKey` from the Feature 129 company-scoped product resolver.
- **What that key is:** `{Provider}:{ExternalCompanyId}:CODE:…|ID:…|TITLE:<normalized title>|UNIT:<unit>`
  (`MonthlyProductTrendCalculator.ProductKey`). For example, `NoavaranCurrentApi:4:TITLE:گندله|UNIT:تن`.
- **What real rows store:** a per-row hash such as `PRODUCT:NATURAL:22d58f29e3fedf1e`. Measured on accepted
  `OutputType = 0` Noavaran reports:
  - 7,625 of 7,785 (company, title, unit) products appear under **more than one** `ProductCode`. کگل's گندله has 17
    different codes in 18 months.
  - The same code is reused by different companies. `PRODUCT:NATURAL:22d58f29e3fedf1e` is گندله for both کچاد
    (مرداد–شهریور) and کگل (شهریور).
  - Within one report, (title, unit) is unique: 0 duplicates.
- **Effect:** the filter can never match a real row. The executor returns no rows and chat answers
  «برای کگل و گندله در شهریور ۱۴۰۵ دادهٔ واجد شرایطی پیدا نشد», which is false.
- **Why the fixtures did not catch it:** every Feature 141 fixture (vertical slice, chat MVP, monthly semantics) writes
  `ProductCode` equal to the resolver key. This is the same kind of gap as the earlier `CompanyId = NULL` defect.
- **Unaffected:** discovery and ranking match on `BTRIM(Title) = ANY(@product_aliases)`, so they find real rows.
- **Not fixed here:** see U8.

### U3. Expected values (independent read-only SQL) vs Feature 141

Independent SQL: join `MonthlyReports` ↔ `Companies` on `(ProviderName, ExternalCompanyId)`, `ProviderName =
'NoavaranCurrentApi'`, `ReportType = 'ProductSales'`, `OutputType = 0`, `IsAccepted`, line title `گندله`, unit `تن`.
Sales amounts are in million IRR. "Chat (MAF V2)" is the final answer, from Feature 141 or from the specialist route that
answered first.

| ID | Question (short) | Expected (DB) | Feature 141 | Chat (MAF V2) answer | Result |
|---|---|---|---|---|---|
| P22 | کگل گندله production, شهریور ۱۴۰۵ | 1,085,157 tons | not executed: `retrieval_requirements_incomplete` | generic "not executable" | FAIL |
| P23 | کگل گندله sales quantity, شهریور ۱۴۰۵ | 1,049,345 tons | executed, 0 rows (U2) | "no qualifying data" (false) | FAIL |
| P24 | کگل گندله sales amount, شهریور ۱۴۰۵ | 154,095,165 million IRR | not reached (specialist answered first) | specialist: ۱۵٬۴۰۹٫۵ billion toman = 154,095 billion IRR ✓ | specialist correct; Feature 141 not exercised |
| P25 | کگهر گندله production, مرداد ۱۴۰۵ | 512,902 tons | not executed: `retrieval_requirements_incomplete` | generic "not executable" | FAIL |
| P04 | کگل گندله production, latest month | شهریور ۱۴۰۵: 1,085,157 tons | executed, 0 rows (U2) | "no qualifying data" (false) | FAIL |
| P05 | کگهر گندله sales amount, latest month | مرداد ۱۴۰۵: 39,580,764 million IRR | not reached (specialist answered first) | specialist: ۳٬۹۵۸٫۱ billion toman = 39,581 billion IRR ✓ | specialist correct; Feature 141 not exercised |
| P26 | کگل گندله production trend, فروردین–شهریور ۱۴۰۵ | 760,456; 1,112,124; 1,071,093; 1,042,566; 1,033,321; 1,085,157 tons | not executed: `direct_value_operation_kind_mismatch` | generic "not executable" | FAIL |
| P27 | Which companies produced گندله in مرداد ۱۴۰۵ | کگل 1,033,321; کگهر 512,902; کچاد 383,727 tons | 3 rows, values exact, `Incomplete` (product alias coverage partial) | lists all three with values and report ids, marked partial | PASS |
| P09 | Which companies produce گندله (no period) | latest per company in the last 12 months: کچاد 388,911 (شهریور); کگل 1,085,157 (شهریور); کگهر 512,902 (مرداد) tons | 3 rows, values exact, `Incomplete` | lists all three, marked partial | PASS |
| P28 | Rank گندله producers by sales amount, مرداد ۱۴۰۵ | کگل 136,725,376 > کگهر 39,580,764 > کچاد 23,273,196 million IRR | 3 rows, values and order exact, `Incomplete` | refused: coverage incomplete, no ranking shown | Safe refusal; see U5 for a period defect |
| P29 | Cumulative گندله production, fiscal year to شهریور ۱۴۰۵ | must not answer 8,597,877 (`OutputType 1`) or 7,512,720 (`OutputType 3`) | typed Unsupported, 0 queries, 0 credits | standard refusal | PASS |

Pilot universe: کگل, کگهر, فملی, کچاد (کچاد added for this run as the third accepted گندله producer in مرداد ۱۴۰۵).
Run artifacts: `evaluation/artifacts/feature141-production-pilot/2026-10-29-1735*-P*.json` through
`2026-10-29-1739*-P*.json` (third run).

### U4. `OutputType` 1–4 never reach an answer as a single month (requirement 7)

- **Executed SQL:** every executed Feature 141 query bound `output_type = 0`, `is_accepted = true` and
  `revision_status = 'Accepted'`. This covers single-company, discovery and ranking queries in all three runs.
- **Answers:** no answer contains a cumulative figure. Checked: 8,597,877, 8,406,258, 986,809,838 (`OutputType 1`, کگل
  شهریور), 7,512,720 (`OutputType 3`) and 4,006,084 (`OutputType 1`, کگهر مرداد).
- **P29:** refused before SQL with `monthly_cumulative_reporting_scope_not_supported`.
- **Real data has the shape the rule guards against:** each company-month has one accepted report per `OutputType` 0–4 (for
  example, 6,991 accepted type-0 and 6,986 accepted type-1 reports), plus pending type-0 duplicates (30). The pending
  duplicates were never read.

### U5. Symbols, units and Jalali periods (requirement 8)

- **Jalali periods:** correct.
  - مرداد ۱۴۰۵ was bound as 2026-07-23..2026-08-22 and شهریور ۱۴۰۵ as 2026-08-23..2026-09-22, matching the stored
    `PeriodStart`/`PeriodEnd`.
  - The default window was rendered as «۷ آبان ۱۴۰۴ تا ۷ آبان ۱۴۰۵» (2025-10-29..2026-10-29).
- **Units:** correct. Quantities keep the row unit `تن`. Ranking reports `MillionIRR` for sales amount.
- **Symbols: defect.** Every Feature 141 row has `CompanySymbol = NULL`, and answers show the full name (for example,
  «معدنی و صنعتی گل گهر»). The compiler prefers `Companies.Ticker`, which is empty for all Noavaran companies. The symbol
  is in `Companies.CompanySymbol`, which the catalog does not approve. This is known since the first pilot and was not
  changed here (catalog approval decision).
- **Ranking period: defect (latent).** P28 asked for مرداد ۱۴۰۵, but the ranking SQL bound a 12-month window
  (2025-10-29..2026-10-29) and then took the latest month for which every producer had data. The explicit month is used
  only when `PopulationCoveragePolicyId = explicit-reporting-period-v1`, and ranking plans do not carry it. The result
  happened to be مرداد here. A question about تیر would be ranked on مرداد. Today the ranking is always refused because
  product alias coverage is partial, so no wrong ranking is shown. This must be fixed before alias coverage is completed.

### U6. Other failures observed (exact causes)

- **P22 and P25 (production quantity, one company): `retrieval_requirements_incomplete`.**
  - The LLM schema navigator's completeness check found a requirement missing from the navigated context. Sales quantity
    for the same company and month (P23) passed it.
  - The missing requirement list is in the navigation trace, which the chat path does not log.
  - Reproduced in all three runs for both companies.
  - Even if it passed, U2 would make the answer a false NoData.
- **P26 (time series): `direct_value_operation_kind_mismatch`.** The plan for "trend from فروردین to شهریور" was not a
  `TimeSeries` operation with time-series presentation and period bounds.
- **Billing (known since the first pilot):** non-answers are still billed 1 credit (P22, P25, P26, P23, P04). Only the
  typed Unsupported outcome (P29) is free. Unchanged here, because billing is out of scope.

### U7. Change made in this task (test harness only)

`tests/FinancialCopilot.IntegrationTests/Feature141ProductionDataPilotTests.cs`:

- **Problem:** in the first run, every single-company question failed with `monthly_product_binding_required`. The pilot
  harness copied only company and statement tables into the disposable chat database. The Feature 129 product resolver
  reads `MonthlyReportLineItems` through the application database, so it found no products. Production does not have this
  problem: its application database holds the monthly rows.
- **Copy:** the harness now also copies `MonthlyReports` and `MonthlyReportLineItems` (readable columns only).
- **Disposable-only defaults:** the four required columns the reader cannot see get defaults in the disposable copy only
  (`Id`, `LastSynchronizedAt`, `SourcePayloadChecksum`, `RevisionFingerprint`). `RevisionFingerprint` needs a unique
  default because accepted and pending revisions share an `ExternalReportId`.
- **Result:** the binder now resolves گندله for کگل and کگهر (Found, key `…:TITLE:گندله|UNIT:تن`). That exposed U2.
- **Also added:** questions P22–P29, and کچاد in the pilot universe.
- **Not changed:** product code, catalog, billing, ingestion, authorization and statement mapping. No production data was
  written. The real database was accessed only as `feature141_app_reader`.

### U8. Correction needed (not made: not a small change)

- **Change:** make Feature 141 match the Feature 129 product identity instead of `ProductCode`. The binder already returns
  the resolved title and unit. The compiler would filter on the row's (title, unit) spellings that form that identity,
  passed from the binder, instead of `ProductCode`. Fixtures must then store real-shaped per-row `ProductCode` hashes.
- **Why it is not made here:**
  - It changes the binding contract (binder → plan → compiler) and every monthly fixture.
  - Feature 129's key gives precedence to `ProviderProductCode` and `ProviderProductId`. The read-only role cannot read
    those columns, and whether they are filled in production could not be checked. Whether the title+unit identity is
    authoritative when they are present is an owner decision.
- **Also needed before GO:**
  - Surface the navigator's missing-requirement list for P22/P25 (U6).
  - Fix the ranking period (U5).
  - Approve `CompanySymbol` for labels (U5).

### U9. GO / NO-GO for monthly functionality

**NO-GO.**

- **Ready:** company mapping, `OutputType` safety, discovery, Jalali periods and units.
- **Blocking:** single-company production and sales answers, which are the main monthly use case, are wrong (false NoData)
  or refused on real data.
- **Safe meanwhile:** keep `Feature141:Enabled = false`. The existing specialist routes answered P05 and P24 correctly.
- **Next step:** re-run this verification after U8.

---

## Previous section: mapping fix (before the GRANTs)

Verdict: **FEATURE141_MONTHLY_MAPPING_PARTIAL**

- **Fixed:**
  - Monthly queries now find Noavaran reports through the provider company identity `(ProviderName, ExternalCompanyId)`. They no longer use `MonthlyReports.CompanyId`.
  - Only single-month reports (`OutputType = 0`) are ever read as monthly values.
  - A fiscal-year-to-date question is refused with the existing typed Unsupported outcome. It is never answered with one month's value.
  - Verified by unit, offline and live chat tests on production-shaped data (`CompanyId = NULL`).
- **Not done:** verification against the real database. The read-only principal `feature141_app_reader` still cannot read
  `MonthlyReports` or `MonthlyReportLineItems` (`permission denied`). The required GRANT needs privileged credentials and
  operational authorization, so it was prepared but not run (section 9).

The statement mapping from the previous task is unchanged.

## 1. Root cause

- **Ingestion.** The Noavaran monthly normalizer (`NadpcoApiMonthlyActivityNormalizer`, provider `NoavaranCurrentApi`)
  writes `ProviderName` and `ExternalCompanyId` but never `CompanyId`. Real Noavaran monthly reports therefore have
  `CompanyId = NULL`, the same as Noavaran statements.
- **Single company and time series.** `CompileMonthly` filtered and grouped on `r."CompanyId"`, using the monthly
  measure's `CompanyIdColumnCatalogId`, which the snapshot builder hard-binds to `MonthlyReports.CompanyId`. Affected:
  the company filter, the latest-month and bounded-month CTEs, the common-month CTE, the duplicate-row partition, and the
  output `CompanyId`.
- **Producer discovery and producer sales ranking.** `CompileMonthlyCompanyDiscovery` and
  `CompileMonthlyProducerSalesRanking` joined through the catalog relationship `rel_4e54d40aee8b45e4`,
  `MonthlyReports.CompanyId → Companies.Id`.
- **Different from the statement fix.** Monthly SQL did not go through the statement binder, so last task's fix did not
  cover it.

**Company resolution was already correct.** The canonical resolver matches the user's symbol against
`Companies.CompanySymbol` (and other symbol columns), prefers `NoavaranCurrentApi` rows, and returns `Companies.Id`.
The product binder already looks up product keys by the company's `ExternalCompanyId` (Feature 129 product catalog).

**About `NoavaranEligibleCompanies`.** The view is `Companies` filtered to `NoavaranCurrentApi`, `PrecedencyRight = 0`
and four markets. `pg_get_viewdef` shows its `Id` is `c."Id"`, which differs from the contract's statement that the Id is
independent.

The read-only principal cannot read the view, but it can read the base columns. Feature 141 therefore joins through the
view's base identity `Companies.(ProviderName, ExternalCompanyId)`, which is unique (`IX_Companies_ProviderName_ExternalCompanyId`).
`Companies.Id` is only the resolved key that the policies and renderer use. It is never compared with
`MonthlyReports.CompanyId`.

**`OutputType` was already enforced.** Every catalog monthly measure declares `providerPolicy.outputType: 0`, and the
compiler always binds `r."OutputType" = @output_type`. What was missing is item 3 below.

## 2. Relationship correction

```sql
FROM "public"."MonthlyReportLineItems" li
JOIN "public"."MonthlyReports" r ON li."MonthlyReportId" = r."Id"
WHERE (r."ProviderName", r."ExternalCompanyId") IN
      (SELECT mc."ProviderName", mc."ExternalCompanyId" FROM "public"."Companies" mc WHERE mc."Id" = @company_id)
-- company key of a row:
(SELECT mc."Id" FROM "public"."Companies" mc WHERE mc."ExternalCompanyId" = r."ExternalCompanyId" AND mc."ProviderName" = r."ProviderName")
```

1. **Catalog.**
   - New runtime relationship `rel_71eca79d3b7d6aa2`: `MonthlyReports.ExternalCompanyId → Companies.ExternalCompanyId`.
     Compiled SQL always adds the `ProviderName` equality. Provenance:
     `product-owner-confirmed:Feature141-noavaran-monthly-company-mapping-2026-10-29`.
   - The physical FK record `rel_4e54d40aee8b45e4` is kept and marked `runtimeJoinable: false`.
   - `MonthlyReports.ExternalCompanyId` is approved for runtime query.
   - The hierarchy was regenerated and `catalog.py validate` passes (hash `c4abd7f4…`).
2. **Compiler.** A small `MonthlyCompanyIdentity` is bound from the runtime catalog relationship. It provides:
   - `Key(alias)`: `Companies.Id` of a report row;
   - `Filter(alias, predicate)`: an index-friendly filter on the report's `(ProviderName, ExternalCompanyId)`;
   - `Join(report, company)`.

   `CompileMonthly`, discovery and producer sales ranking use it. Without a provider-identity relationship (old catalogs),
   the report's `CompanyId` is used as before. Product filtering, accepted-report and revision filters, period rules,
   duplicate detection and units are unchanged. All values are still bound parameters, and the LLM generates no SQL.
3. **Reporting scope.** `Feature141ChatQueryService.RequestsCumulativeMonthlyScope` sits next to the existing
   threshold guard. A production or sales measure requested on a `monthlyCumulative` basis (fiscal year to date) returns
   the existing typed Unsupported outcome (`monthly_cumulative_reporting_scope_not_supported`) before any SQL runs.
   `OutputType` 1–4 are never read as monthly values, and cumulative values are never derived by subtraction.

## 3. Changed production files

| File | Change |
|---|---|
| `src/backend/FinancialCopilot.Application/FinancialData/Semantics/Navigation/StructuredQueryCompilation.cs` | `MonthlyCompanyIdentity` and `ResolveMonthlyCompanyIdentity`; `CompileMonthly`, `CompileMonthlyCompanyDiscovery`, `CompileMonthlyProducerSalesRanking` and `BuildCompiledMonthly` (the identity's columns and relationship are authorized like the others) |
| `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/Feature141ChatQueryService.cs` | Cumulative monthly scope refused as typed Unsupported |
| `specs/141-…/SemanticCatalog/relationships/rel_71eca79d3b7d6aa2.yaml` (new), `rel_4e54d40aee8b45e4.yaml`, `tables/tbl_8e759d70dada6bd8.yaml` (MonthlyReports), `catalog.yaml` | Provider-identity relationship; FK not runtime-joinable; column approval; index entry |
| `specs/141-…/SemanticCatalog/manifest.yaml`, `artifacts/navigation-hierarchy.{yaml,md}` | Regenerated |
| `tools/SemanticCatalog/sql/feature141-read-role-{setup,verify,revoke}.sql` | Least-privilege column grants for the monthly tables and the provider identity columns (prepared, not executed) |

Tests changed:
- **Unit.** `Feature141ChatThresholdGuardTests` gets the cumulative-scope theory. In `StructuredQueryCompilationTests`,
  the synthetic snapshots now enable the runtime relationship.
- **Integration.** Monthly and statement fixtures in `Feature141EngineVerticalSliceTests`, `Feature141ChatMvpTests`,
  `MonthlyStructuredQueryPostgreSqlTests`, `Feature141CompanyDiscoverySemanticsPostgreSqlTests` and
  `Feature141MonthlyProductionSemanticsPostgreSqlTests` now have `CompanyId = NULL`. The independent truth queries join
  through the provider identity.
- **Chat cases.** `Feature141ChatMvpTests` gets C10 (fiscal-year-to-date) and C11 (only `OutputType = 3` exists).

Contract-driven fixture change:
- **Old scenarios.** Three scenarios modeled a "company alias": a second `ExternalCompanyId` linked to the same company
  only through `CompanyId`. They were M6, D3 and the April duplicate in `MonthlyStructuredQuery`. Under the confirmed
  contract such a report belongs to a different, unknown company. `IX_MonthlyReports_LogicalPeriod` also forbids two
  accepted reports of one identity for the same month.
- **M6 and the April duplicate.** These now use a second accepted report of the same identity with a different
  `PeriodStart`. Same expectation: Ambiguous, `monthly_product_period_has_multiple_source_rows`.
- **D3 (explicit-month discovery).** Restated as "a second report whose window is not the requested month is not
  counted". The answer is شستا with 5, not 6 or 11.

## 4. SQL and parameters

Live slice F141-E07 «تولید گندله کگل در شهریور ۱۴۰۵», on a fixture whose reports have `CompanyId = NULL`
(`artifacts/engine-e2e-vertical-slice-2026-10-29-155607-F141-E07-answered.json`):

```sql
WITH eligible_monthly_rows AS (SELECT (SELECT mc."Id" FROM "public"."Companies" mc WHERE mc."ExternalCompanyId" = r."ExternalCompanyId" AND mc."ProviderName" = r."ProviderName") AS "CompanyId",
  r."PeriodStart" AS "PeriodStart", r."PeriodEnd" AS "PeriodEnd", li."ProductCode" AS "ProductCode", li."Title" AS "ProductTitle",
  li."ProductionQuantity" AS "Value", li."Unit" AS "SourceUnit",
  COUNT(*) OVER (PARTITION BY (SELECT mc."Id" FROM … same key …), r."PeriodEnd", li."ProductCode") AS "SamePeriodCandidateCount",
  r."ProviderName" AS "ProviderName", r."ReportType" AS "ReportType", r."OutputType" AS "OutputType", r."RevisionStatus" AS "RevisionStatus",
  r."Id" AS "ReportIdentity", r."ExternalReportId" AS "ExternalReportId", li."SourceRowFingerprint" AS "SourceRowFingerprint", li."SourceMultiplicity" AS "SourceMultiplicity"
  FROM "public"."MonthlyReportLineItems" li JOIN "public"."MonthlyReports" r ON li."MonthlyReportId" = r."Id"
  WHERE (r."ProviderName", r."ExternalCompanyId") IN (SELECT mc."ProviderName", mc."ExternalCompanyId" FROM "public"."Companies" mc WHERE mc."Id" = @company_id)
    AND r."ProviderName" = @provider_name AND r."ReportType" = @report_type AND r."OutputType" = @output_type
    AND r."IsAccepted" = @is_accepted AND r."RevisionStatus" = @revision_status AND li."ProductCode" = @product_code
    AND r."PeriodStart" >= @period_start AND r."PeriodEnd" <= @period_end),
projected_monthly_rows AS (SELECT * FROM eligible_monthly_rows)
SELECT *, "ReportIdentity" AS "ReportId" FROM projected_monthly_rows ORDER BY "CompanyId", "PeriodEnd" ASC, "ProductCode", "SourceMultiplicity" LIMIT @row_limit OFFSET @row_offset
```

Parameters: `provider_name=NoavaranCurrentApi`, `report_type=ProductSales`, `output_type=0`, `is_accepted=true`,
`revision_status=Accepted`, `company_id=<Companies.Id of کگل>`,
`product_code=NoavaranCurrentApi:feature141-vs-kgol:TITLE:گندله|UNIT:ton`, `period_start=2026-08-23`,
`period_end=2026-09-22` (شهریور ۱۴۰۵, from the deterministic Persian month normalizer), `row_limit=2`.

For مرداد ۱۴۰۵ the normalizer gives 2026-07-23..2026-08-22. Discovery and producer ranking use
`JOIN "Companies" c ON c."ExternalCompanyId" = r."ExternalCompanyId" AND c."ProviderName" = r."ProviderName"` with
`c."Id" = ANY(@company_ids)`. The real tables are small (about 41.6k `MonthlyReports` and 439k line items) and
`MonthlyReports` is indexed on `(ProviderName, ExternalCompanyId, …)`.

## 5. Real database evidence

**Blocked.** The owner's diagnostic query cannot run as the read-only principal:

```
psql -U feature141_app_reader -d financial_copilot -c 'select 1 from "MonthlyReports" limit 1;'
ERROR:  permission denied for table MonthlyReports
```

- **Not read:** the role has no SELECT on `MonthlyReports`, `MonthlyReportLineItems` or `NoavaranEligibleCompanies`.
- **Owner account:** not used. The session's safety policy blocks it, and the brief requires read-only access.
- **Not checked:** the owner's sample rows (321 یخچال `OutputType` 3; 1436 گروه دارویی آنتی‌بیوتیک `OutputType` 4) and
  any real `OutputType = 0` observation.

The only real-data facts available are:
- the table sizes (`pg_class.reltuples`);
- the indexes (`pg_indexes`);
- the ingestion code that never sets `MonthlyReports.CompanyId` for Noavaran.

## 6. OutputType verification

| Check | Evidence | Result |
|---|---|---|
| Ordinary month question binds `OutputType = 0` | Catalog measures declare `outputType: 0`; every compiled monthly query binds `output_type=0` (E07 parameters above) | ✓ |
| Month with no `OutputType = 0` report gives no cumulative or corrected fallback | Chat C11 «تولید گندله کگهر در مرداد ۱۴۰۵ چقدر بود؟»: the fixture's only کگهر Mordad report is `OutputType = 3` with گندله 777. Feature 141 ran the query, got `Status=NoData`, and the answer contains neither 777 nor the Shahrivar 40 | ✓ (live) |
| Cumulative report in the window is ignored | `MonthlyStructuredQuery` fixture `cumulative-april` (`OutputType = 1`) is never returned (offline) | ✓ |
| Fiscal-year-to-date request is refused, not answered with one month | Chat C10 «تولید تجمعی گندله کگل از ابتدای سال مالی تا پایان شهریور ۱۴۰۵ چقدر بود؟»: typed `Unsupported` / `monthly_cumulative_reporting_scope_not_supported`, 0 queries, no 25 or 22 in the answer | ✓ (live) |
| Corrections (`OutputType` 2) and to-previous-month (3, 4) never used as monthly values | Excluded by the bound `output_type=0` filter in every monthly query | ✓ |

## 7. Actual vs expected (production-shaped fixtures, live model; reports and statements with `CompanyId = NULL`)

| Case | Question | Expected | Actual | Result |
|---|---|---|---|---|
| E07 (slice) | تولید گندله کگل در شهریور ۱۴۰۵ | fixture row 25 ton, OutputType 0, accepted revision | 25 ton «در شهریور ۱۴۰۵», SQL above; independent read matches | PASS |
| E08, E09 (slices) | monthly sales quantity and sales amount questions | fixture values and units | as expected, independent read matches | PASS |
| Monthly production slice | named company, product, month | fixture value | as expected | PASS |
| Discovery slice | which companies produced گندله in a month | producer set | as expected, partial alias coverage disclosed | PASS |
| C2 (chat) | تولید گندله کگل در شهریور ۱۴۰۵ چقدر بود؟ | «۲۵ تن … در شهریور ۱۴۰۵» | same | PASS |
| C3 (chat) | مبلغ فروش گندله کگهر در شهریور ۱۴۰۵ | 700 million rial (specialist may answer first) | specialist `product_sales_value` answer in its own unit | PASS |
| C8 (chat) | producers of گندله in Shahrivar (approved alias «گندله») | کگل 25, کگهر 40, partial disclosure | same | PASS |
| C9 (chat) | production threshold «بالای ۵۰۰ تن» | typed Unsupported, no query | same | PASS |
| C10 (chat) | fiscal-year-to-date production | typed Unsupported, no query | same | PASS (on re-run, after aligning the expected refusal text with chat's standard Unsupported message, as in C9) |
| C11 (chat) | month with only an OutputType 3 report | NoData, no 777 | same | PASS |
| Real data | the owner's samples and real OutputType 0 rows | — | not readable | **BLOCKED** |

## 8. Focused test results

| Run | Result |
|---|---|
| Unit, full (2,504) | 2,484 passed, 20 failed: the same 20 pre-existing failures. Monthly compiler tests and guard tests pass, including the new cumulative-scope theory. |
| Offline integration: `Feature141*` and `MonthlyStructuredQuery*` (81) | 38 passed, 41 skipped (live only), 2 failed. `CompiledMonthlyRetrievalAndTimeSeriesExecuteThroughRestrictedReadOnlyExecutor` is the known baseline failure, at the same `QueryAuthorized` assertion (line 782; 780 before 2 comment lines were added). `UsageBasedCreditBillingHookTests.SemanticNoDataThenFeature141Fallback…` fails with `OptionsValidationException` for `Billing:UsageBasedCredits`. That test belongs to the in-progress usage-based billing work in the working tree, which this task did not touch. Monthly production semantics, company discovery semantics, cross-company, latest-period and statement suites pass. |
| Live: `Feature141EngineVerticalSliceTests` (30) + `Feature141ChatMvpTests` (12) | 38/42 in the full run. On re-run C5 passed (in the full run the router chose `stock_screening`, which failed with `provider_or_tool_failure`; Feature 141 was not invoked) and C10 passed (text expectation corrected). C1 and C4 return correct, Jalali answers but fail `Assert.NotNull(run.Usage)`, as in the previous report: the working tree's usage-based billing returns no usage for Feature 141 answers. |
| Catalog | `catalog.py validate`: valid (`c4abd7f4…`) |

Feature 129 monthly tools, routing and chat fallback were not changed.

## 9. Remaining blockers

1. **Read permissions on the monthly tables.** This blocks the real-data verification and any monthly Feature 141 answer
   in production. Prepared least-privilege grants for the configured principal (also added to the reviewed scripts
   `feature141-read-role-{setup,verify,revoke}.sql`); to be run by the database owner with operational authorization:

   ```sql
   GRANT SELECT ("Id", "ProviderName", "ExternalCompanyId", "PeriodStart", "PeriodEnd", "ReportType", "OutputType",
                 "IsAccepted", "RevisionStatus", "ExternalReportId")
     ON TABLE public."MonthlyReports" TO feature141_app_reader;
   GRANT SELECT ("MonthlyReportId", "ProductCode", "Title", "Unit", "ProductionQuantity", "SalesQuantity", "SalesAmount",
                 "SourceRowFingerprint", "SourceMultiplicity")
     ON TABLE public."MonthlyReportLineItems" TO feature141_app_reader;
   ```

   Not needed at runtime: `NoavaranEligibleCompanies`, because Feature 141 joins its base identity in `Companies`, which
   the role already reads. To run the owner's diagnostic query as this role, the database owner would also grant
   `GRANT SELECT ON public."NoavaranEligibleCompanies" TO feature141_app_reader;` and add `"SalesRate"` to the line-item
   grant. The SalesRate measure is not queryable in Feature 141.
2. **Billing in the working tree.** Feature 141 answers return no usage, and one billing hook test fails options
   validation. This belongs to the concurrent usage-based billing work.
3. **Unchanged from the pilot.** Single-company statement answers without a stated basis are ambiguous. Population labels
   come from the empty `Companies.Ticker`. No reviewed company universe is configured for real tenants.

## 10. Production-pilot readiness of the monthly part

**Not ready yet; it is ready to verify.** The mapping and reporting-scope logic match the confirmed data contract and pass
on production-shaped data. The real-data check needs the two grants above. After they are applied:
1. Run the owner's diagnostic query.
2. Pick accepted `OutputType = 0` rows with non-null quantities.
3. Re-run the production pilot's monthly questions (P04, P05, P09) plus the C10/C11 patterns on real companies.

Those are the only remaining monthly steps.
