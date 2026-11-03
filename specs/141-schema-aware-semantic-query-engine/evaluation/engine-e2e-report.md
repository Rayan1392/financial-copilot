# Feature 141 isolated engine E2E report — 2026-10-28 (vertical slice run 1)

**Final status: `FIRST_VERTICAL_SLICE_PASSED`.** One real Persian financial question ran through the production Feature 141 chain with the live OpenAI provider and an isolated PostgreSQL database, and returned the exact expected value. The 20-case release gate is still **NO GO** (`ENGINE_E2E_BLOCKED`: 1 pass, 19 blocked); nothing in this run authorizes Chat integration, and no Chat/API routing was changed.

Evidence: `evaluation/artifacts/engine-e2e-vertical-slice-2026-10-28.json` (full stage trace), `evaluation/engine-e2e-results.json` (`verticalSlice`, case `E141-02`). Runner: `tests/FinancialCopilot.IntegrationTests/Feature141EngineVerticalSliceTests.cs`.

## Blockers found and how they were resolved

| Blocker in the 2026-10-28 blocked report | Finding | Resolution |
|---|---|---|
| Docker Linux engine | `docker version` / `docker info` on the `desktop-linux` context still return Internal Server Error; Testcontainers cannot start. | Added `tools/SemanticCatalog/scripts/Feature141DisposablePostgres.ps1`: a throwaway PostgreSQL 18.1 server (own data directory, loopback only, port 55441, generated admin password in an ACL-restricted file). The existing Feature 131 fixture consumes it through `FINANCIAL_COPILOT_TEST_POSTGRES_CONNECTION_STRING` and refuses application databases. The application service on port 5432 was never touched. |
| No approved binding for a monthly sales query | `metric_monthly_sales`, `metric_monthly_sales_quantity` and `metric_monthly_production_quantity` are `review.semanticApproved=false`, `review.queryable=false` (pending-owner-review) in the canonical catalog. | **Substitution, no approval invented:** the only canonical concept with an approved binding is `operatingProfitPerShare` → `IncomeStatement/161` (`source_item_682beceadddc1458`), with owner-approved semantics, query authorization and effective unit `IRRPerShare`. Its four tables and three relationships are `queryable`/`semanticApproval.approved` in the catalog. |
| "Runner not built / governed bindings unavailable" | The chain existed as components; the chat orchestrator (`Feature141ChatQueryService`) wires them, but the handoff to the planner was incomplete (see production fixes). | Built the reusable runner and fixed the three defects below. |

## Production fixes required by the run (all covered by unit tests)

1. **`FinancialSourcePlannerHandoff`** now accepts the compiler's `StructuredSqlDirectSource` mapping and emits `dim_company`, the three verified join relationships and the `latest` period policy (an unspecified period on a single-value retrieval is answered by the latest reporting period, which the answer states explicitly). Before: the live planner returned `retrieval_requirements_incomplete` (`entity:company` missing) and the compiler would have rejected the empty relationship set. `Feature141ChatQueryService` passes the mapping.
2. **`CanonicalIntentCatalogBinding`** treats a requested unit equal to the approved effective unit as representable (the model emitted `RequestedUnit=IRRPerShare` for a question that named no unit); any other unit is still `Unsupported` (`requested_result_unit_conversion_not_supported`).
3. **`StructuredQueryCompiler`** carries the owner-approved effective unit from the catalog. Before: only provider-reported units were carried, so the answer rendered `۳٬۷۵۳ N/A`.

No governance flag was flipped by the test. Plan eligibility came from the packaged catalog: `QueryAuthorized=true`, `IdentityAuthorized=true`, `SemanticReviewApproved=false` (parent hierarchy nodes remain pending-review; the validator and executor accept exact approved source identity, as designed).

## Observed execution

1. **Question executed:** `سود عملیاتی هر سهم کگهر چقدر است؟`
2. **Canonical intent (live, gpt-5.6-luna, 1 call):** measure `operatingProfitPerShare`, Canonicalized, `FinancialStatementItem`, dimension `CurrencyPerShare`, value form `ReportedValue`, requested unit `IRRPerShare`; operation `Retrieve`; comparison `None`; period `Unspecified`; entity scope `NamedCompanies` [`کگهر`]; output `Scalar`; no clarification.
3. **Catalog binding:** typed `CanonicalIntentCatalogBinding` → `Bound`; chat-path `CanonicalSourceItemBindingResolver` → `Bound`; handoff → `ReadyForOfflineValidation` (`source_identity_ready_for_planner_validation`). Source `source_item_682beceadddc1458` = `NoavaranCurrentApi` / `IncomeStatement` / item 161, `sourceItemCatalogId 3cafc7d3-b0ab-4341-85ef-ebcbc9d60528`.
4. **Navigator + planner (live, 1 model call):** 12 context nodes, no missing context; plan structurally valid and execution eligible; company `کگهر` bound by exact ticker to the fixture company; relationships `rel_2588cf95d01a651a`, `rel_cd8a77a2717ac71f`, `rel_e4fa0fe989156533`; period policy `latest`.
5. **SQL and parameters:** the compiler's sealed `WITH latest_period … SELECT … LIMIT @row_limit OFFSET @row_offset` statement (fingerprint `dd03b72a…c12cec`, full text in the artifact) with parameters `company_id=<fixture company>`, `source_item_catalog_id=3cafc7d3-b0ab-4341-85ef-ebcbc9d60528`, `provider_name=NoavaranCurrentApi`, `statement_type=IncomeStatement`, `source_item_id=161`, `row_limit=101`, `row_offset=0`.
6. **Authorization:** `CatalogStructuredQueryAuthorizer` issued a grant for the isolated database and the SELECT-only fixture role, provenance `feature141-isolated-vertical-slice-fixture`.
7. **PostgreSQL execution evidence:** `NpgsqlReadOnlyStructuredQueryExecutor` → `Succeeded`, 1 row in 37 ms, `transaction_read_only=on`, role identity verified. Row: `Value=3753`, `PeriodStart=2026-03-21`, `PeriodEnd=2026-06-21`, `StatementType=IncomeStatement`, `SourceItemId=161`, `IsComposing=false`, `CompanyId=<fixture company>`. The same line item re-read with the administrative connection = 3753; the four seeded line items (expected row, other item 162 = 5555, older period = 3001, other company `کگل` = 9999) produced exactly one returned row.
8. **Expected vs actual:** 3753 IRRPerShare, period 2026-03-21..2026-06-21 = Persian calendar 1405-01-01..1405-03-31 — all equal.
9. **Final Persian response (Answered):** `کگهر در بازهٔ ۲۰۲۶-۰۳-۲۱ تا ۲۰۲۶-۰۶-۲۱، مقدار مالی گزارش‌شده: ۳٬۷۵۳ IRRPerShare.` + `منبع: NoavaranCurrentApi.`
10. **No unauthorized access:** fixture role is `NOSUPERUSER NOCREATEDB NOCREATEROLE`, SELECT on the four reviewed tables only, no INSERT/UPDATE/DELETE, no SELECT on `MonthlyReports`; the same sealed query under the writable administrative principal was refused (`Unauthorized`, `database_read_only_identity_check_failed`).
11. **LLM cost:** 2 model calls, 7,426 input / 1,242 output tokens; intent 8.3 s, navigator 6.9 s; whole chain 17.2 s.

## Test command and exit code

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/SemanticCatalog/scripts/Feature141DisposablePostgres.ps1 -Action start
$env:FINANCIAL_COPILOT_TEST_POSTGRES_CONNECTION_STRING = (Get-Content "$env:LOCALAPPDATA\feature141-disposable-postgres\connection.txt" -Raw).Trim()
$env:FEATURE141_RUN_LIVE_LLM_POSTGRES = "1"
dotnet test tests/FinancialCopilot.IntegrationTests/FinancialCopilot.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~Feature141EngineVerticalSliceTests"
```

Result: `Passed … [22 s]`, `Total tests: 1`, exit code 0 (run at 2026-10-28T17:19Z after two diagnostic runs that exposed fixes 1–3).

Supporting checks: `CanonicalIntentCatalogBindingTests` 8/8, `StructuredQueryDirectSourceEffectiveUnitTests` 6/6, new `FinancialSourcePlannerHandoffTests` cases 2/2, `StructuredQueryPlanningTests` 12/12 pass. Twelve focused failures in `FinancialSourcePlannerHandoffTests` (5), `StructuredQueryCompilationTests` (5) and `Feature141DeveloperUatAuthorizationTests` (2), and eight unrelated failures elsewhere, fail identically on a pristine `HEAD` worktree; they assert the older unauthorized catalog state or predate this work and were not touched.

## Cosmetic observations (not fixed here)

- The generator prints the unit code `IRRPerShare` and Gregorian dates in Persian digits; a Persian unit label (`ریال به ازای هر سهم`) and a Jalali period would read better. The numeric fact, unit and period are correct.
- The model inferred `RequestedUnit=IRRPerShare` although the question named no unit; harmless here because it equals the approved unit, but the extractor prompt should prefer `Unspecified` when no unit is stated.

## Remaining blockers for the other 19 cases

| Cases | Blocker | Owner | Minimal action |
|---|---|---|---|
| E01, E02 (MillionIRR variant), E03, E05, E06, E14–E17 | No approved canonical binding for `netProfit`, `operatingProfit`, `earningsPerShare`, `totalAssets` (only `operatingProfitPerShare` is bound). | Product owner / catalog governance | Add approved `canonicalConceptBindings` with dimension, value form and effective unit to the relevant `financial-statement-source-items.yaml` entries and hierarchy references; regenerate the hash. |
| E07–E13 | Monthly measures are `pending-owner-review` (`review.semanticApproved=false`, `queryable=false`); monthly sales rate unit unverified. | Product owner | Approve the four monthly measure entries (and their relationship/table flags) in the catalog. |
| E10, E11 | Company discovery scopes are empty in configuration; product equivalence `گندله`/`گندله سنگ‌آهن` is alias-only. | Product owner | Configure `Feature141:CompanyDiscoveryScopes` and an approved equivalence group. |
| E05, E06, E12–E17 | Planner/compiler algebra for cross-company comparison, ranking, filters, aggregation and period comparison on statement items is not implemented (`CanonicalIntentCatalogBinding` rejects them as `Unsupported`). | Engineering | Extend the algebra operator by operator with tests, after the bindings above exist. |
| E18–E20 | Typed rejections (Ambiguous/Unsupported/Unauthorized) need live runs through the same runner. | Engineering | Add the three cases to the runner; no governance needed. |
| All | Docker Desktop Linux engine is broken on this host. | Host owner | Repair Docker Desktop, or keep the disposable script as the supported path. |

The release gate remains **NO GO**; Chat integration stays as it was (not enabled by this work).
