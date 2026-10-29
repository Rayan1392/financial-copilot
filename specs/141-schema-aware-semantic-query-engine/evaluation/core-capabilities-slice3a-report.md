# Feature 141 — Core Capabilities Slice 3a Report: Cross-Company Comparison and Ranking (2026-10-29)

Verdict: **CROSS_COMPANY_COMPARISON_PASSED**

The three required questions are answered end to end. The run uses the live model (`OpenAI / gpt-5.6-luna`), the production catalog, binding, navigator, planner, validator, SQL compiler, catalog authorizer, restricted read-only executor and Persian renderer, and an isolated PostgreSQL 18 database. Eight negative scenarios produce the expected typed outcome live, and 22 database scenarios pass without the model. Each database scenario is executed twice with identical results. The model never writes SQL. No question-specific handler, agent framework or authorization architecture was added. Production chat routing is not enabled.

Two items need the product owner:

- **Decision D1 was engineering-applied.** The display columns `Companies.Name` and `Companies.Ticker` are now query-approved under the existing capability directive, with per-object provenance and owner countersign pending. A ranking of the authorized population (F141-E14) cannot name its members without them.
- **Population answers need tenant configuration.** A real tenant sees a population ranking only after its discovery scope and reviewed company universe (decisions D2 and D3) are configured. Without them the engine refuses before SQL (`company_discovery_scope_not_authorized`); see scenario X19. The live E14 run uses a fixture-only tenant scope, exactly like F141-E10.

## Required scenarios (live model + isolated PostgreSQL)

| Case | Question | Live intent | Answer (both final passes) | Expected | Result |
|---|---|---|---|---|---|
| F141-E05 | سود خالص کگل و کگهر را مقایسه کن | compare / multiCompany / named [کگل, کگهر] | کگل ۱٬۲۰۰ میلیون ریال (rank 1); کگهر ۹۰۰ میلیون ریال (rank 2); 1405/01/01–1405/03/31 | کگل 1200, کگهر 900 MillionIRR | PASS |
| F141-E06 | کدام یک سود خالص بیشتری دارد، کگل یا کگهر؟ | compare / multiCompany / named [کگل, کگهر] | "بیشترین سود خالص متعلق به کگل است (۱٬۲۰۰ میلیون ریال)" | winner کگل, 1200 MillionIRR | PASS |
| F141-E14 | شرکت‌ها را بر اساس سود خالص از بیشترین به کمترین مرتب کن | rank descending / unbound population | ۱. فملی ۱٬۶۰۰؛ ۲. کگل ۱٬۲۰۰؛ ۳. کگهر ۹۰۰ میلیون ریال | فملی 1600, کگل 1200, کگهر 900 | PASS |

Each answered case was checked against an independent administrative read of every company's seeded value at the reported period. The fixture carries decoys that a wrong company, wrong item or wrong period would surface:

- فملی 1600 is seeded but not requested in E05 and E06.
- Older quarters carry different values (کگل 1000, کگهر 2000, فملی 1400).
- Every statement holds another item with the value 77,777.
- In E14, فولاد (9,999) is seeded outside the authorized universe.

None of these decoys appear in any answer. The writable administrative principal is refused (`database_read_only_identity_check_failed`) in every executed case. Each executed case makes exactly one executor call.

Persian answer, F141-E05:

```text
مقایسهٔ سود خالص در آخرین دورهٔ گزارش مشترک (دورهٔ سه‌ماهه ۱۴۰۵/۰۱/۰۱ تا ۱۴۰۵/۰۳/۳۱ شمسی؛ ۲۰۲۶-۰۳-۲۱ تا ۲۰۲۶-۰۶-۲۱ میلادی):
- کگل: ۱٬۲۰۰ میلیون ریال
- کگهر: ۹۰۰ میلیون ریال
بیشترین سود خالص متعلق به کگل است (۱٬۲۰۰ میلیون ریال)؛ کمترین متعلق به کگهر است (۹۰۰ میلیون ریال).
مبنا: صورت‌های مالی جداگانه (غیرتلفیقی)؛ واحد: میلیون ریال؛ منبع: NoavaranCurrentApi.
```

Persian answer, F141-E14:

```text
رتبه‌بندی شرکت‌های دامنهٔ مجاز شما (۳ شرکت دارای داده) بر اساس سود خالص از بیشترین به کمترین، در آخرین دورهٔ گزارش مشترک (دورهٔ سه‌ماهه ۱۴۰۵/۰۱/۰۱ تا ۱۴۰۵/۰۳/۳۱ شمسی؛ ۲۰۲۶-۰۳-۲۱ تا ۲۰۲۶-۰۶-۲۱ میلادی):
۱. فملی: ۱٬۶۰۰ میلیون ریال
۲. کگل: ۱٬۲۰۰ میلیون ریال
۳. کگهر: ۹۰۰ میلیون ریال
مبنا: صورت‌های مالی جداگانه (غیرتلفیقی)؛ واحد: میلیون ریال؛ منبع: NoavaranCurrentApi.
این رتبه‌بندی همهٔ شرکت‌های دامنهٔ مجاز شما را که در این دوره دادهٔ معتبر دارند پوشش می‌دهد.
```

## Negative scenarios (live model + isolated PostgreSQL)

| Case | Scenario | Question / fixture | Typed outcome | Diagnostic | SQL executed |
|---|---|---|---|---|---|
| F141-E21 | Missing company | سود خالص کگل و زاگرس را مقایسه کن (زاگرس not a company) | Incomplete | `statement_comparison_company_binding_unresolved` | no |
| F141-E22 | Missing metric | هزینه استهلاک کگل و کگهر را مقایسه کن (reviewed concept, no approved binding) | Incomplete | `approved_catalog_binding_missing:depreciationExpense` | no |
| F141-E23 | Different reporting periods | E05 question; کگل has 1405-Q1 (1300) and 1404-Q4 (1100), کگهر only 1404-Q4 (800) | Answered at the latest common period: کگل 1100, کگهر 800, 1404/09/30–1404/12/29 | warning `newer_period_available:کگل:2026-06-21`, disclosed in the answer | yes |
| F141-E24 | Incompatible units | سود خالص کگل و کگهر را به میلیارد ریال مقایسه کن | Unsupported | `requested_result_unit_conversion_not_supported` | no |
| F141-E25 | Duplicate statements | E05 question; two valid کگل statements for the same quarter (1200, 1250) | Ambiguous | `requested_item_values_conflict` | yes |
| F141-E26 | Consolidated vs non-consolidated | E05 question; کگل consolidated only, کگهر standalone only | Ambiguous (asks for تلفیقی or جداگانه) | `cross_company_consolidation_basis_mismatch` | yes |
| F141-E27 | Equal metric values | E06 question; کگل 1200, کگهر 1200 | Answered: both rank 1, tied; "مقدار سود خالص همهٔ این شرکت‌ها برابر است" | — | yes |
| F141-E28 | Empty result set | E05 question; both companies exist without statements | Incomplete | `comparison_companies_without_statements` | yes (0 rows) |

No non-answer carries a comparison verdict or ranking.

## How it works

The capability reuses the existing chain end to end and adds no agent framework, no new authorization architecture and no question-specific handler. The model never produces SQL.

| Stage | Component (production code) | Slice 3a change |
|---|---|---|
| Canonical intent | `LlmCanonicalSemanticIntentExtractor` + `CanonicalSemanticIntentCanonicalizer` | Prompt states the documented latest-period and statement-basis policies; canonicalizer drops company-identity requested fields and company names repeated as fields in multi-company results, and an inferred `valueGrain=companyTotal` constraint on statement items. |
| Shape | `StatementQueryShapeClassifier` (new, single owner) | Classifies a statement intent as one company value, a comparison of named companies (also multi-company retrieval), or a ranking of named companies or the authorized population; rejects everything else with a typed diagnostic. |
| Catalog binding | `CanonicalIntentCatalogBinding` | Uses the classifier; explicit standalone/consolidated scope is now representable. Missing metric and unit conversion keep their typed rejections. |
| Handoff | `FinancialSourcePlannerHandoff.CreateCrossCompany` (new entry point) | Builds planner requirements: `compare` scope, `latest` period policy, `MultiCompanyComparison` mode, sort on the compared value and top-N for rankings, population flag for an unbound scope. `Create`, used by chat, is unchanged. |
| Planner and validator | `OfflineStructuredQueryPlanningService`, `StructuredQueryPlanValidator` | An unbound population resolves the tenant's authorized discovery scope (`statement-comparison-authorized-company-universe-v1`) or is refused (`company_discovery_scope_not_authorized`). Statement comparisons require no aggregation, a value-bound sort and a verified binding for every named company (`statement_comparison_company_binding_unresolved`); monthly comparisons keep the governed-Sum rule. |
| SQL compiler | `StructuredQueryCompiler.CompileStatementComparison` (new) | One SELECT-only query from catalog identifiers with bound parameters; refuses an unresolved effective unit and, for populations, unapproved company label columns. Shared mapping checks and physical binding were extracted from the single-company path without changing its SQL. |
| Authorization and execution | `CatalogStructuredQueryAuthorizer`, `NpgsqlReadOnlyStructuredQueryExecutor` | Unchanged authorization model: named companies are the compiled company set; a population uses the existing reviewed tenant universe and discovery scope. The executor hands rows to the application policy. |
| Result policy | `CrossCompanyStatementComparisonPolicy` + `LatestStatementObservationPolicy` (moved from the executor) | Per-company statement resolution, cross-company comparability, deterministic ranking (below). |
| Persian answer | `PersianStatementComparisonRenderer` | Company labels, values, units, Jalali and Gregorian period, basis, verdict or ranking, disclosures; typed non-answers never present a verdict. |
| Chat | `Feature141ChatQueryService` + `StatementComparisonPlans` | Statement comparison plans are refused in chat before compilation. Production chat routing is not enabled. |

### Query algebra

The plan is the existing `StructuredQueryPlan`: one `DirectSourceItem` source, company entities (named) or a `CompanyDiscoveryScope` (population), `Period.SelectLatestByReportingPeriod`, `ComparisonScopes=["compare"]`, `FinancialOperation.ComparisonMode=MultiCompanyComparison`, `Sort=[(source, direction)]` for a ranking, `Pagination.Limit` as top N, presentation `table` or `ranked-table`. Filters and aggregations are rejected for statement comparisons in this slice.

### Reporting-period policy (`latest-common-reporting-period-v1`)

The compiled query reads candidate statements of the source's provider and statement type (and the requested basis, when stated) for the compared companies, then selects the latest period key (period start, period end, period type) that every company with statements reports. Every company is read at that one period; no company is compared at a different period. A company whose newest statement is newer is disclosed in the answer. If no common period exists the result is `Incomplete` (`no_common_reporting_period`) and the answer lists each company's latest period. A different period type ending on the same date is not a common period.

### Comparability, missing values and duplicates

- Per company, `LatestStatementObservationPolicy` applies the existing single-company rules: competing statements with the same value are one observation (all statement ids kept); conflicting values are `Ambiguous` (`requested_item_values_conflict`); never an arbitrary pick.
- Consolidation basis: without a stated basis, a company that reports on more than one basis is `Ambiguous` (`statement_scope_unspecified_across_consolidation_bases`) and companies on different bases are `Ambiguous` (`cross_company_consolidation_basis_mismatch`); the answer asks for تلفیقی or جداگانه. A stated basis filters every company's statements to that basis.
- Units: the compiler refuses a source without a resolved effective unit (`statement_comparison_requires_resolved_effective_unit`); the policy refuses rows whose source unit differs (`cross_company_units_incompatible`); a requested display unit other than the source unit is refused at binding (`requested_result_unit_conversion_not_supported`). No conversion is performed.
- Missing values are never zero. A named company without statements (`comparison_company_statement_missing`) or without the item at the common period (`comparison_company_value_missing`) makes the comparison `Incomplete`; the known values are listed for information without a verdict. In a population, members without a value are excluded from the ranking and disclosed.

### Ranking and ties

Values are ordered by the requested direction (comparison default: descending), then by company label (ordinal), then by company id. Equal values share a competition rank (1, 1, 3) and are flagged as tied. Top N keeps every company whose rank is at most N, so a tie at the boundary is never broken arbitrarily (the answer says so). The order is independent of row order from the database (unit-tested by reversing the input).

### Company populations

Explicitly named companies are bound through the existing company binder; an unknown or ambiguous name stops the comparison before SQL. An unbound population ("شرکت‌ها") is the tenant's reviewed company universe, using the existing discovery scope and `ReviewedTenantCompanyBindings` authorization: the compiled query has no company id parameters, the executor binds the grant's universe, and a company outside it is never read (F141-E14 seeds فولاد with a larger value outside the universe). Coverage that is not declared complete makes the ranking explicitly partial (`Incomplete`). Reviewed named populations from the existing population resolver become named company sets and use the same path.

## Database scenarios without the model (`Feature141CrossCompanyComparisonSemanticsPostgreSqlTests`)

Production handoff, planner, validator, compiler, authorizer, restricted executor and renderer run over isolated PostgreSQL. Each scenario has its own database and executes twice; both results are identical. Artifacts are in `artifacts/cross-company-comparison-semantics-2026-10-29/<id>.json`.

| Id | Scenario | Outcome |
|---|---|---|
| X01 | Named comparison; third company and older quarters as decoys | Succeeded: کگل 1200 (1), کگهر 900 (2) |
| X02 | Descending ranking of three named companies | فملی 1600, کگل 1200, کگهر 900 |
| X03 | Equal values | کگل 1200 (1, tied), کگهر 1200 (1, tied), فملی 900 (3) |
| X04 | Ascending ranking, top 1 | کگهر 900 |
| X05 | Tie at the top-N boundary (top 2) | three rows, warning `top_n_extended_by_ties` |
| X06 | Missing value for one company | Incomplete `comparison_company_value_missing` (not zero) |
| X07 | Different latest periods | read at the latest common period, newer period disclosed |
| X08 | No common period | Incomplete `no_common_reporting_period` |
| X09 | Same end date, different period type | Incomplete `no_common_reporting_period` |
| X10 | Duplicate statements, same value | one observation, both statement ids kept |
| X11 | Duplicate statements, conflicting values | Ambiguous `requested_item_values_conflict` |
| X12 | Consolidated vs standalone companies | Ambiguous `cross_company_consolidation_basis_mismatch` |
| X13 | One company on both bases, scope unstated | Ambiguous `statement_scope_unspecified_across_consolidation_bases` |
| X14 | Same data, consolidated stated | Succeeded with consolidated values only |
| X15 | Named companies without statements | Incomplete `comparison_companies_without_statements` |
| X16 | Population ranking; company outside the universe | outside company never read |
| X17 | Population member without statements | excluded, `population_members_without_statements:1` disclosed |
| X18 | Population coverage not complete | Incomplete, explicitly partial ranking |
| X19 | Population without authorized scope | refused before SQL: `company_discovery_scope_not_authorized` |
| X20 | Empty population | NoData `comparison_population_without_statements` |
| X21 | Unknown named company | refused before SQL: `statement_comparison_company_binding_unresolved` |
| X22 | Source without resolved effective unit | refused at compilation: `statement_comparison_requires_resolved_effective_unit` |

## Generated SQL and parameters

F141-E05 (named companies; artifact `engine-e2e-vertical-slice-2026-10-29-065110-F141-E05-answered.json`). Every identifier comes from the integrity-checked catalog snapshot; every value is a bound parameter.

```sql
WITH candidate AS (
  SELECT c_st."CompanyId" AS "CompanyId", c_st."PeriodStart" AS "PeriodStart", c_st."PeriodEnd" AS "PeriodEnd", c_st."PeriodType" AS "PeriodType", c_st."IsComposing" AS "IsComposing"
  FROM "public"."FinancialStatements" c_st
  WHERE c_st."CompanyId" = ANY(@company_ids)
    AND c_st."ProviderName" = @provider_name
    AND c_st."StatementType" = @statement_type
),
company_coverage AS (
  SELECT c."CompanyId", MAX(c."PeriodEnd") AS "CompanyLatestPeriodEnd", COUNT(DISTINCT c."IsComposing") AS "CompanyBasisCount"
  FROM candidate c
  GROUP BY c."CompanyId"
),
common_period AS (
  SELECT c."PeriodStart", c."PeriodEnd", c."PeriodType"
  FROM candidate c
  GROUP BY c."PeriodStart", c."PeriodEnd", c."PeriodType"
  HAVING COUNT(DISTINCT c."CompanyId") = (SELECT COUNT(*) FROM company_coverage)
),
target_period AS (
  SELECT cp."PeriodStart", cp."PeriodEnd", cp."PeriodType"
  FROM common_period cp
  WHERE cp."PeriodEnd" = (SELECT MAX(cp_latest."PeriodEnd") FROM common_period cp_latest)
)
SELECT cc."CompanyId" AS "CompanyId", cc."CompanyLatestPeriodEnd" AS "CompanyLatestPeriodEnd", cc."CompanyBasisCount" AS "CompanyBasisCount", li."Value" AS "Value", st."PeriodStart" AS "PeriodStart", st."PeriodEnd" AS "PeriodEnd", st."PeriodType" AS "PeriodType", st."Id" AS "StatementId", st."ProviderName" AS "ProviderName", st."StatementType" AS "StatementType", @source_item_id AS "SourceItemId", (SELECT si."Unit" FROM "public"."FinancialStatementSourceItems" si WHERE si."Id" = @source_item_catalog_id) AS "SourceUnit", @source_item_catalog_id AS "SourceItemCatalogId", st."IsComposing" AS "IsComposing", st."IsAudited" AS "IsAudited", st."IsRepresented" AS "IsRepresented", st."ExternalStatementId" AS "ExternalStatementId", st."SourcePayloadChecksum" AS "SourcePayloadChecksum", st."PublishedAt" AS "PublishedAt", st."LastSynchronizedAt" AS "LastSynchronizedAt", st."WarningsJson" AS "WarningsJson", st."LogicalVendor" AS "LogicalVendor", st."SourceMode" AS "SourceMode"
FROM company_coverage cc
JOIN "public"."Companies" co ON co."Id" = cc."CompanyId"
LEFT JOIN "public"."FinancialStatements" st ON st."CompanyId" = cc."CompanyId"
  AND st."ProviderName" = @provider_name AND st."StatementType" = @statement_type
  AND EXISTS (SELECT 1 FROM target_period tp WHERE tp."PeriodStart" = st."PeriodStart" AND tp."PeriodEnd" = st."PeriodEnd" AND tp."PeriodType" = st."PeriodType")
LEFT JOIN "public"."FinancialStatementLineItems" li ON li."FinancialStatementId" = st."Id" AND li."SourceItemCatalogId" = @source_item_catalog_id
WHERE co."Id" = ANY(@company_ids)
  AND EXISTS (SELECT 1 FROM "public"."FinancialStatementSourceItems" si WHERE si."Id" = @source_item_catalog_id AND si."ProviderName" = @provider_name AND si."StatementType" = @statement_type AND si."SourceItemId" = @source_item_id)
ORDER BY cc."CompanyId", st."PeriodType", st."Id"
LIMIT @row_limit
```

| Parameter | E05 / E06 | E14 (population) |
|---|---|---|
| `company_ids` | the two bound company ids of کگل, کگهر (compiled) | not compiled; injected by the executor from the grant's reviewed universe (3 ids: فملی, کگل, کگهر) |
| `source_item_catalog_id` | `bd36909d-abf8-44c2-b7db-5e53dd678b53` (catalog identity of IncomeStatement/143) | same |
| `provider_name` / `statement_type` / `source_item_id` | `NoavaranCurrentApi` / `IncomeStatement` / `143` | same |
| `is_composing` | absent (basis unstated) | absent |
| `row_limit` | 101 (100 rows + truncation probe) | 101 |

E14 additionally projects `co."Ticker" AS "CompanySymbol", co."Name" AS "CompanyName"`; those are the two columns approved by D1.

## Unit and period correctness

| Case | Effective unit (compiled = executed) | Reported period | Independent read equals engine value |
|---|---|---|---|
| E05, E06 | MillionIRR (`capability-directive-approved`) | 2026-03-21..2026-06-21 = 1405/01/01..1405/03/31, ThreeMonths, standalone | yes, both companies |
| E14 | MillionIRR | same | yes, all three |
| E23 | MillionIRR | 2025-12-21..2026-03-20 = 1404/09/30..1404/12/29 (latest common period, not کگل's newer quarter) | yes |
| E27 | MillionIRR | 2026-03-21..2026-06-21 | yes |

Every answered case states both the Jalali and Gregorian period, the basis (جداگانه/تلفیقی), the unit and the provider.

## Live-model traces (final passes)

| Case | LLM calls | Input tokens | Output tokens | Executor calls | Total ms (pass 1 / pass 2) |
|---|---|---|---|---|---|
| E05 | 2 | 7,746 | 979 / 921 | 1 | 12,497 / 11,277 |
| E06 | 2 | 7,642 | 1,068 / 753 | 1 | 12,772 / 9,576 |
| E14 | 2 | 7,611 | 995 / 980 | 1 | 11,067 / 13,267 |
| E21 | 2 | 7,748 | 646 / 643 | 0 | 8,649 / 8,078 |
| E22 | 1 | 2,591 | 345 / 360 | 0 | 6,087 / 5,108 |
| E23 | 2 | 7,746 | 820 / 668 | 1 | 10,154 / 9,104 |
| E24 | 1 | 2,594 | 385 / 446 | 0 | 4,417 / 5,288 |
| E25 | 2 | 7,746 | 683 / 865 | 1 | 8,622 / 11,480 |
| E26 | 2 | 7,746 | 662 / 647 | 1 | 9,082 / 8,368 |
| E27 | 2 | 7,642 | 1,003 / 759 | 1 | 12,041 / 9,513 |
| E28 | 2 | 7,746 | 733 / 702 | 1 | 9,392 / 9,134 |

The two calls are canonical intent extraction and the navigator; typed rejections at binding stop after one call. Full traces (canonical intent, binding, navigator, plan, SQL, parameters, grant, rows, answer) are in the per-run artifacts listed in `engine-e2e-suite-v1.json` and `engine-e2e-results.json#crossCompanySlice3a`.

## Run history and test exit codes

| Run | Scope | Result | Exit code | Note |
|---|---|---|---|---|
| Live cross-company run 1 | 11 cases | 2 passed, 9 failed | 1 | Test fixture defect: the fixture role name was 64 bytes and PostgreSQL truncates at 63, so the executor's identity check refused every query (fail-closed). Prefix shortened. |
| Live cross-company run 2 | 11 cases | 7 passed, 4 failed | 1 | Live-model quirks: company names repeated as requested output fields (E25, E26, E28); clarification asked only about statement basis (E06). Fixed deterministically in the canonicalizer and by stating the documented policy in the extractor prompt. |
| Live cross-company run 3 | 11 cases | 11 passed | 0 | |
| Live full class, pre-fix pass | 22 tests | 21 passed, 1 failed | 1 | E27: the model added an inferred `valueGrain=companyTotal` constraint; now normalized like the same qualifier in slice 1. Second pass aborted to apply the fix. |
| **Live full class, final pass 1** | 22 tests (all Feature 141 live cases) | **22 passed** | **0** | slice 1 cases E01–E04, E07–E09, typed rejections E18–E20 and E10 readiness unchanged |
| **Live full class, final pass 2** | 22 tests | **22 passed** | **0** | stability rerun |
| Feature 141 integration (no live opt-in) | `FullyQualifiedName~Feature141` | 37 passed, 23 skipped (live opt-in), 0 failed | 0 | includes the 22 cross-company scenarios, latest-period S1–S7, monthly M1–M12, discovery D1–D8, suite invariants |
| Unit suite | `tests/FinancialCopilot.UnitTests` | 2,431 passed, 20 failed | 1 | exactly the 20 failures recorded before this slice (baseline captured at the start of this session); none new, none fixed |
| Full integration suite | `tests/FinancialCopilot.IntegrationTests` | 592 passed, 61 failed, 70 skipped (723) | 1 | the same 61 test names fail on pristine HEAD `ff73d3c` (worktree `D:\Source\fc-base141`: 569 passed, 61 failed, 53 skipped); none new, none fixed; no failing test is a Feature 141 test |

Commands (PowerShell; the disposable instance comes from `tools/SemanticCatalog/scripts/Feature141DisposablePostgres.ps1 -Action start`):

```powershell
$env:FINANCIAL_COPILOT_TEST_POSTGRES_CONNECTION_STRING = (Get-Content "$env:LOCALAPPDATA\feature141-disposable-postgres\connection.txt" -Raw).Trim()
$env:FEATURE141_RUN_LIVE_LLM_POSTGRES = "1"
dotnet test tests/FinancialCopilot.IntegrationTests --filter "FullyQualifiedName~Feature141EngineVerticalSliceTests"
Remove-Item Env:FEATURE141_RUN_LIVE_LLM_POSTGRES
dotnet test tests/FinancialCopilot.IntegrationTests --filter "FullyQualifiedName~Feature141"
dotnet test tests/FinancialCopilot.UnitTests
```

## Suite status (`engine-e2e-suite-v1.json`, version 1.1.0)

| Total | PASS | BLOCKED | FAIL | Executed live |
|---|---|---|---|---|
| 28 | 21 | 7 | 0 | 22 |

E05, E06 and E14 moved from BLOCKED to PASS. E21–E28 are new and PASS. E10 is still executable but BLOCKED on decisions D2–D4. E11, E12, E13, E15, E16 and E17 remain BLOCKED on discovery breadth and the next algebra slices. The suite invariant test now takes the case count from `summary.totalCases`, so the suite can grow but never drop below the original 20 cases.

## Governance and boundaries

- **Catalog.** D1 added `tbl_8646d83cdb350f92_Name` and `tbl_8646d83cdb350f92_Ticker` to Companies' approved objects, with per-object provenance `engineering-applied:Feature141-core-capabilities-directive-2026-10-29 (decision request D1-companies-output-columns; owner countersign pending)`. `Id` keeps its owner provenance. The catalog hash changed from `93acd370…` to `2fb37d4a615338ef95a47a3406c96c495b1a8b22ff2dc1a4b916fc3d54cb9a28`, and `catalog.py validate` passes. The decision request records the change and how to reverse it.
- **Authorization.** No new authorization architecture was added. Named companies flow through the existing company binder and compiled company set. Populations use the existing discovery scope and reviewed tenant universe. The restricted role has SELECT on four tables only, and the writable principal is refused.
- **Chat.** `Feature141ChatQueryService` refuses statement comparison plans before compilation, through the `StatementComparisonPlans` predicate. The chat route keeps the single-company `FinancialSourcePlannerHandoff.Create`, whose contract is unchanged (unit-tested).
- **Extractor prompt.** It now states two documented engine policies: the latest reporting period, and that consolidation basis is decided from data. With them, a missing period or basis alone is not a clarification. Every previously passing live case was re-run twice after this change and passed.

## Limits and follow-ups

- **Population row cap.** A population ranking is computed over at most 100 candidate statement rows. A larger universe returns `Truncated`, an explicit non-answer, rather than a partial ranking. Raising this needs paging, or a SQL-side per-company reduction, in slice 3b.
- **Stale population members.** In a population, one company whose newest statement is older than the others moves the common period back for everyone; the answer discloses newer periods. Choosing a different population period policy, such as the most widely reported recent period, is a product decision.
- **No explicit periods yet.** Only the latest common period is supported. An explicit period or period comparison of statement items (E12, E13) is the next algebra slice, as are threshold filters (E15, E17) and aggregation (E16).
- **Model variance.** Three live-model quirks appeared during development and are handled deterministically. New variants may still appear; the typed non-answer then prevents a wrong number, as the failed runs above show.
- **Pending owner actions.** Countersign D1 and the slice 1 directive records. Configure D2/D3 per tenant before enabling population answers. Decide on chat enablement.

## Files changed in this slice

- Application:
  - `Navigation/CrossCompanyStatementComparison.cs` (new: shape classifier, comparison policy, latest-statement policy, plan predicate)
  - `Navigation/PersianStatementComparisonRenderer.cs` (new)
  - `StructuredQueryCompilation.cs`
  - `StructuredQueryPlanning.cs`
  - `FinancialSourcePlannerHandoff.cs`
  - `CanonicalIntentCatalogBinding.cs`
  - `PersianStructuredQueryResponseGenerator.cs`
  - `Feature141OperationRegistry.cs`
  - `Intent/CanonicalBusinessMeasureConceptRegistry.cs` (canonicalizer)
  - `Intent/LlmCanonicalSemanticIntentExtractor.cs` (prompt)
- Infrastructure:
  - `StructuredQueryExecutor.cs` (delegates to the application policies)
  - `Feature141ChatQueryService.cs` (chat guard)
- Catalog:
  - `SemanticCatalog/tables/tbl_8646d83cdb350f92.yaml`
  - regenerated `manifest.yaml`, `artifacts/navigation-hierarchy.yaml/.md`
- Tests:
  - `Feature141CrossCompanyStatementComparisonTests.cs` (unit, new)
  - `Feature141CrossCompanyComparisonSemanticsPostgreSqlTests.cs` (new)
  - `Feature141EngineVerticalSliceTests.cs` (runner kind, 11 cases, live theory)
  - `Feature141EngineE2ESuiteTests.cs`
- Evaluation:
  - `engine-e2e-suite-v1.json`
  - `engine-e2e-results.json`
  - `engine-e2e-test-plan.md`
  - `core-capabilities-implementation-plan.md`
  - `governance/company-discovery-decision-request-2026-10-29.json`
  - `../tasks.md`
