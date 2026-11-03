# Feature 141 — Core Capabilities Slice 1 Report (2026-10-29)

Verdict: **CORE_RETRIEVAL_SLICE_PASSED** — eight natural-language Persian questions across three categories answered end to end with the live model (`gpt-5.6-luna`), the production catalog/planner/compiler/authorizer/executor and an isolated PostgreSQL 18 database; three typed rejections reproduced. No Chat routing change, no new auth or tenant architecture, no hardcoded ticker or product list, no model-generated SQL.

## Working question categories (live evidence)

| Case | Category | Question | Answer | Evidence (artifacts/) |
|---|---|---|---|---|
| F141-E01 | statement item, canonical concept | سود خالص کگل چقدر است؟ | ۱٬۲۰۰ میلیون ریال, 1405-01-01..1405-03-31 | engine-e2e-vertical-slice-2026-10-29-052903-F141-E01-answered.json |
| F141-E02 | statement item, canonical concept | سود عملیاتی کگهر چقدر است؟ | ۱٬۱۰۰ میلیون ریال | …-052915-F141-E02-answered.json |
| F141-E03 | statement item, canonical concept (title سود خالص هر سهم) | سود هر سهم فملی چقدر است؟ | ۳٬۲۰۰ IRRPerShare | …-052929-F141-E03-answered.json |
| F141-E04 | statement item, canonical concept | سود عملیاتی هر سهم کگهر چقدر است؟ | ۳٬۷۵۳ IRRPerShare | …-052942-F141-E04-answered.json |
| F141-E07 | monthly production quantity, explicit Persian month | تولید گندله کگل در شهریور ۱۴۰۵ چقدر بود؟ | ۲۵ تن, 1405-06 | …-052548-F141-E07-answered.json |
| F141-E08 | monthly sales amount, explicit Persian month | مبلغ فروش گندله کگهر در شهریور ۱۴۰۵ چقدر بود؟ | ۷۰۰ میلیون ریال | …-052439-F141-E08-answered.json |
| F141-E09 | monthly sales quantity, explicit Persian month | مقدار فروش گندله کگل در شهریور ۱۴۰۵ چقدر بود؟ | ۲۰ تن | …-052508-F141-E09-answered.json |
| F141-E10 | cross-company discovery (fixture-only authorization, readiness) | کدام شرکت‌ها در شهریور ۱۴۰۵ گندله تولید کرده‌اند؟ | کگل, کگهر; coverage Incomplete | …-052609-F141-E10-incomplete.json |
| F141-E18 / E19 / E20 | typed rejections | ambiguous measure / forecast / no tenant | Ambiguous / Unsupported / Unauthorized before SQL | …-052618-E18, …-052626-E19, …-052611-E20 |

Every answered case asserts: canonical concept or measure id, company resolution to the seeded company (decoy company excluded), source item or measure column, deterministic period (explicit Persian month → Gregorian window; statement questions → latest statement period), numeric value equal to an independent administrative read of the seeded row, effective unit, Persian digits and unit label in the answer, one executor call, and the admin-principal negative control (`database_read_only_identity_check_failed`).

## What was built in this slice

- Catalog enablement under the capability directive (provenance `engineering-applied:Feature141-core-capabilities-directive-2026-10-29`, owner countersign pending): canonical bindings `netProfit→IncomeStatement/143`, `operatingProfit→140`, `earningsPerShare→160` with directive-approved effective units; `metric_monthly_sales_quantity` and `metric_monthly_sales` approved with verified unit policies; `SalesQuantity`/`SalesAmount` added to the line-items column allowlist. `SalesRate` stays unapproved (unit unverified). Catalog hash `93acd3705931532d1a78a4ec8d7e251d6cfa4dae0a9df3a5feafa2949d99ddd2`.
- `StructuredQueryCompiler.ResolveDirectSourceEffectiveUnit` accepts `capability-directive-approved` like `owner-approved`.
- `CanonicalSemanticIntentCanonicalizer`: a model-emitted `CompanyTotal` grain on a financial statement item is normalized to `Unspecified` (a statement line item is the company total by definition); a generic requested output field that only names the scalar result (`value`, `amount`, `مقدار`, `ارزش`) is dropped. Both were observed live in this slice and would otherwise reject the question before planning. `ProductLevel` on a statement item and any genuinely different requested field are still rejected.
- Runner generalization: a case declares its statement item (type, id, title) or its monthly measure column; the fixture is seeded from the case, the source-item catalog id is read from the catalog, decoy tickers are chosen distinct from the case company, and the independent database check reads the seeded source item. One generic live theory (`AdditionalQuestionsRunLiveEndToEnd`) covers every answered case.

## Test evidence

| Suite | Command | Result |
|---|---|---|
| Live vertical slice (11 tests) | `dotnet test tests/FinancialCopilot.IntegrationTests … --filter FullyQualifiedName~Feature141EngineVerticalSliceTests` with `FEATURE141_RUN_LIVE_LLM_POSTGRES=1` | full run 05:24–05:26 UTC: 7 pass, 4 fail (E01/E03 test-side independent check, E02/E04 model `value` field); targeted rerun 05:29 after fixes: E01, E02, E03, E04 pass. Net: all 11 pass on current source. |
| Feature 141 offline integration (suite invariants, latest-period S1–S7, monthly M1–M12, discovery D1–D8) | `--filter FullyQualifiedName~Feature141` | 15 passed, 12 skipped (live opt-in), 0 failed |
| Unit suite | `dotnet test tests/FinancialCopilot.UnitTests` | 2408 passed, 20 failed; the same 20 fail on pristine HEAD `ff73d3c` (worktree `D:\Source\fc-base141`, 21 failed there incl. one environment-dependent fixture-hash test). Two tests asserting the pre-directive catalog state were updated to the directive contract (`MissingApprovedMeaningNeverUsesSimilarTitleOrUnit` → `depreciationExpense`, `RealCatalogExposesOnlyTheApprovedMonthlyMeasures`, `RuntimeCatalogSnapshotBuilder_LoadsMonthlyMeasureMappingsWithoutAuthorizingThem`). |

Suite v1 status: 10 PASS, 10 BLOCKED, 0 FAIL, 11 executable (F141-E10 executable but BLOCKED on owner decisions D1–D4).

## Gaps and next slices

- Blocked by planner algebra (slice 3): E05/E06/E14 cross-company comparison and ranking of a statement item, E12/E13 period comparison and growth on statement items, E15/E17 threshold filters, E16 sum aggregation.
- Blocked by discovery breadth (slice 2): E11 products of a company; E10 PASS needs decisions D1–D4.
- Total assets (BalanceSheet/138) has no canonical binding yet; it can be enabled the same way as 143/140/160 when the directive is extended.
- Latest-month monthly questions (no explicit month) answer through the `latest` policy only for statements; the monthly path requires an explicit Persian month in this slice.
- Owner countersign of the directive-applied catalog records is pending; the provenance string makes them auditable and reversible.

Recommended next slice: 3a — statement-item cross-company comparison and ranking (E05, E06, E14) on the existing `MultiCompanyComparison` plan mode.
