# Feature 141 — Product Identity and Cross-Company Discovery Report — 2026-10-29

**Final verdict: `PRODUCT_DISCOVERY_APPROVAL_REQUIRED`.** The governed discovery query for «کدام شرکت‌ها در شهریور ۱۴۰۵ گندله تولید کرده‌اند؟» is implemented and executed end to end with the live provider and the isolated PostgreSQL database, returning exactly the expected producer set {کگل, کگهر} with provenance and a partial-coverage disclosure. It ran under **fixture-only authorization** (a fixture-only discovery scope, a fixture-only reviewed company universe and a fixture-only approval of two `Companies` display columns), so F141-E10 stays BLOCKED in the suite until the product owner takes the decisions in `governance/company-discovery-decision-request-2026-10-29.json`. The five (now six with F141-E07) previously passing E2E cases still pass. No production Chat routing was enabled.

## 1. Product identity findings (Phase 1)

| Layer | Identity | Evidence |
|---|---|---|
| Ingestion (Noavaran monthly) | one `MonthlyReportLineItems` row per product line; `ProductCode = provider:externalCompany:TITLE:<title>|UNIT:<unit>`; `Title` and `Unit` kept verbatim | `NadpcoApiMonthlyActivityNormalizer`, row model |
| Feature 129 resolver (`CompanyScopedProductResolver`) | company-scoped; candidates keyed by product key; a title group with one normalized title is resolved to one key **regardless of unit** (groups by title only) | resolver lines 56–72 |
| Feature 141 catalog | `dim_product_identity` is the company-scoped product key; `product_iron_ore_pellets` is the owner-approved semantic concept with aliases گندله / گندله سنگ‌آهن, coverage `approved-aliases-only` | `product-catalog-v1.json`, `Feature141:ProductCatalog` |
| Canonical intent / binding | the intent carries the user product label; the planner binds it either to one company-scoped key (direct value) or to the concept (unbound population) | planner lines 680–880 |
| Planner / compiler | direct value: `li.ProductCode = @product_code` (source observation identity); discovery: `BTRIM(li.Title) = ANY(@product_aliases)` (semantic identity, unit independent) with the row's unit projected | `CompileMonthly`, `CompileMonthlyCompanyDiscovery` |

Collision: the same title in two units is **two source observation identities** (two product keys) but **one semantic product**. The Feature 129 resolver collapses the title group to one key, so a direct-value question silently took one unit (observed on 2026-10-28, M10). Correction placed in the **Feature 141 adapter**, not in Feature 129: `StructuredQueryProductBinder` now consults the company product catalog and returns `Ambiguous monthly_product_units_incompatible` when the resolved title exists in more than one unit. Without catalog access the binder is a pure pass-through, so Feature 129 callers and the resolver itself are unchanged (regression evidence: `Feature141ProductIdentityAndDiscoveryGrantTests`, the Feature 128/129 unit tests show the same pre-existing results as before, and `StructuredQueryPlanningTests` 12/12).

## 2. Feature 129 compatibility assessment

- `CompanyScopedProductResolver`, `EfCoreMonthlyProductComparisonRepository` and `MonthlyProductTrendCalculator` are untouched.
- The only behavioral change is additive and local to Feature 141 plans: a multi-unit title is refused instead of resolved. The direct-value happy path (F141-E07) and all monthly negatives still pass (M10 is now asserted, no longer observed-only).
- The discovery grant restoration in `CatalogStructuredQueryAuthorizer` affects only population discovery queries; direct queries keep the 2026-10-28 behavior.

## 3. Unit handling policy (Phase 2) — owner rules applied

1. Semantic identity unit independent: discovery matches by approved alias titles and lists each producer with its own reported unit (D1: کگل 25 ton, کگهر 47 ton, شپنا 12,000 kg all listed).
2. Observations keep their unit and source key: `SourceUnit`, `ProductCode`, report identity are projected and rendered as provenance.
3. Equivalence only through the approved concept: `گندله سنگ‌آهن` resolves to the same concept (D2); an unapproved product (`کنسانتره`, D8) is not discoverable.
4–6. No raw sum or comparison across units: discovery never aggregates (no `مجموع` in any answer); a direct value from a multi-unit title is `Ambiguous`; `monthly_quantity_unit_missing` and `UnitCompatibilityCount` guards remain.

## 4. Company discovery scope (Phase 3)

- No predefined ticker list: the plan binds no company; producers are selected from persisted `MonthlyReportLineItems`/`MonthlyReports` rows (`ProductionQuantity > 0`, accepted revision, exact Persian-month window) and deduplicated per company by `DiscoveryRank = 1`.
- Bounded, not a scanner: `c.Id = ANY(@company_ids)` where `@company_ids` is the execution grant's reviewed company universe; the authorizer refuses discovery without a matching `tenant|scope|approval` entry **and** at least one reviewed `tenant:company` pair for the tenant (restored on 2026-10-29; this is also what made the pre-existing population test fail with `Unauthorized`).
- Approved equivalence: `@product_aliases = [گندله, گندله سنگ‌آهن]` from the owner-approved concept.
- Coverage is disclosed, never claimed: `CompanyCoverageComplete=false` or `approved-aliases-only` make the executor return `Incomplete` and the answer says the list is partial.
- Deterministic Persian month: `1405-06 → 2026-08-23..2026-09-22`; when the navigator misses the monthly domain, the planner now expands the catalog context to the production measure deterministically and keeps the explicit window instead of falling back to a trailing-12-month window.

## 5. Exact SQL and authorization evidence (live run, `artifacts/engine-e2e-vertical-slice-2026-10-29-044051-F141-E10-incomplete.json`)

Intent (gpt-5.6-luna): `UnboundCompanyPopulation`, product `گندله`, operation `DiscoverPopulation`, period `ExplicitMonth شهریور ۱۴۰۵`. Plan eligible from the real catalog (`f5024d57…`); concept `product_iron_ore_pellets`; scope `feature141-isolated-fixture-company-discovery-scope` (fixture-only, coverage incomplete).

```sql
WITH eligible_observations AS (
  SELECT c."Id" AS "CompanyId", c."Name" AS "CompanyName", c."Ticker" AS "CompanySymbol", li."Title" AS "ProductTitle",
         li."ProductCode" AS "ProductCode", li."ProductionQuantity" AS "Value", li."Unit" AS "SourceUnit",
         r."PeriodStart", r."PeriodEnd", r."ProviderName", r."Id" AS "ReportId", r."ExternalReportId",
         li."SourceRowFingerprint", li."SourceMultiplicity",
         COUNT(*) OVER (PARTITION BY c."Id", r."PeriodEnd") AS "SamePeriodCandidateCount",
         ROW_NUMBER() OVER (PARTITION BY c."Id" ORDER BY r."PeriodEnd" DESC, r."PeriodStart" DESC, r."Id" ASC, li."ProductCode" ASC) AS "DiscoveryRank"
  FROM "public"."MonthlyReportLineItems" li
  JOIN "public"."MonthlyReports" r ON li."MonthlyReportId" = r."Id"
  JOIN "public"."Companies" c ON r."CompanyId" = c."Id"
  WHERE c."Id" = ANY(@company_ids) AND r."ProviderName" = @provider_name AND r."ReportType" = @report_type
    AND r."OutputType" = @output_type AND r."IsAccepted" = @is_accepted AND r."RevisionStatus" = @revision_status
    AND r."PeriodStart" = @period_start AND r."PeriodEnd" = @period_end
    AND li."ProductionQuantity" > 0 AND BTRIM(li."Title") = ANY(@product_aliases))
SELECT *, 'partial' AS "ProductMappingCoverageStatus", @company_coverage_complete AS "CompanyCoverageComplete"
FROM eligible_observations WHERE "DiscoveryRank" = 1 ORDER BY "CompanyName" ASC, "CompanyId" ASC LIMIT @row_limit
```

Parameters: `provider_name=NoavaranCurrentApi`, `report_type=ProductSales`, `output_type=0`, `is_accepted=true`, `revision_status=Accepted`, `period_start=2026-08-23`, `period_end=2026-09-22`, `product_aliases=[گندله, گندله سنگ‌آهن]`, `company_coverage_complete=false`, `row_limit=21`, `company_ids` = grant universe (6 seeded companies; the unauthorized producer is absent). Grant: SELECT-only fixture role, provenance `feature141-isolated-vertical-slice-fixture`; writable admin principal refused.

Fixture-only approvals used (recorded in the trace as `CompiledWithFixtureOnlyColumnApproval=true`): `tbl_8646d83cdb350f92_Name`, `tbl_8646d83cdb350f92_Ticker` (decision D1).

## 6. Expected versus actual company set

| | Expected | Actual |
|---|---|---|
| Producers (Shahrivar 1405, positive گندله/گندله سنگ‌آهن, inside the reviewed universe) | کگل, کگهر | کگل (گندله, 25 ton), کگهر (گندله سنگ‌آهن, 47 ton) |
| Excluded by rule | فملی (zero), کچاد (NULL), وبملت (Mordad only), فولاد (other product), UNAUTH (outside the universe) | none returned |
| Independent count of positive producers in the window (admin SQL) | 3 (includes UNAUTH) | 2 returned → scope boundary enforced |
| Status | Incomplete (coverage disclosed) | Incomplete `population_discovery_authorized_company_coverage_incomplete` |
| Persian answer | both companies, each with product title, value, unit, window and report provenance, plus the partial-list sentence | `… Fixture کگل (کگل): تولید ثبت‌شدهٔ گندله ۲۵ تن …؛ Fixture کگهر (کگهر): تولید ثبت‌شدهٔ گندله ۴۷ تن …. این فهرست جزئی است …` |

## 7. Test results (Phase 4)

```powershell
$env:FINANCIAL_COPILOT_TEST_POSTGRES_CONNECTION_STRING = (Get-Content "$env:LOCALAPPDATA\feature141-disposable-postgres\connection.txt" -Raw).Trim()
$env:FEATURE141_RUN_LIVE_LLM_POSTGRES = "1"
dotnet test tests/FinancialCopilot.IntegrationTests/FinancialCopilot.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~Feature141"
# -> Passed 21, Skipped 1 (local-reader probe), Failed 0 of 22, exit 0 (2026-10-29 04:40–04:41 UTC)
#    live: E04 answered, E07 answered, E10 discovery (readiness), E18/E19/E20 typed rejections; offline: monthly negatives (12),
#    latest-period (11), discovery scenarios (8), suite invariants, legacy matrix
dotnet test tests/FinancialCopilot.UnitTests/FinancialCopilot.UnitTests.csproj -c Release
# -> 2403 passed / 20 failed of 2423; the 20 are the same pre-existing failures attributed to HEAD on 2026-10-28
python tools/SemanticCatalog/catalog.py hierarchy build ; python tools/SemanticCatalog/catalog.py validate   # valid, hash f5024d57…
```

Discovery scenarios (`Feature141CompanyDiscoverySemanticsPostgreSqlTests`, artifact `artifacts/company-discovery-semantics-2026-10-29-044128.json`, 0 failures):

| Scenario | Outcome |
|---|---|
| D1 exact label, multiple companies, same product in different units | Incomplete (partial coverage); {شپنا 12,000 kg, کگل 25 ton, کگهر 47 ton}; zero, NULL, other month, other product, unauthorized excluded |
| D2 equivalent label «گندله سنگ‌آهن» | same set and concept |
| D3 duplicate accepted reports (company alias) | Ambiguous `monthly_product_period_has_multiple_source_rows`, no list |
| D4 no matching companies | Incomplete, 0 rows, no-match answer |
| D5 incorrect reporting period | Incomplete, 0 rows |
| D6 no approved scope | plan refused `company_discovery_scope_not_authorized`, no SQL |
| D7 unauthorized producer inside vs outside the universe | listed only when inside the reviewed universe |
| D8 unapproved product | no concept, not eligible, no SQL |

Unit collision: `Feature141MonthlyProductionSemanticsPostgreSqlTests` M10 now asserts `Ambiguous monthly_product_units_incompatible` with no SQL; `Feature141ProductIdentityAndDiscoveryGrantTests` (5) cover the binder and the discovery grant.

## 8. Catalog and code changes

- Catalog: `metric_monthly_production_quantity.unitPolicy.verified=true` under the 2026-10-29 unit policy (item 5); the discovery compiler requires a verified quantity unit policy. Hash `29a100de…` → `f5024d57…`, hierarchy regenerated and validated. No other catalog object changed; `Companies.Name/Ticker` remain unapproved pending D1.
- `CatalogStructuredQueryAuthorizer` / options validator: discovery grant = tenant's reviewed company universe (required).
- `OfflineStructuredQueryPlanningService`: deterministic context expansion to the production measure for unbound-product production questions; explicit normalized month preserved through the discovery rewrite.
- `StructuredQueryProductBinder`: unit-collision detection via the product catalog (optional dependency, DI-registered).
- Runner: `CompanyDiscovery` case kind with fixture scope, reviewed universe and fixture-only display-column approval recorded in the trace.

## 9. Remaining blockers

| Blocker | Owner | Action |
|---|---|---|
| D1 `Companies.Name`/`Ticker` query authorization for discovery output | Product owner | approve the two columns (decision request) |
| D2/D3 tenant discovery scope and reviewed company universe | Product owner + operations | configure per tenant through the secure configuration source; decide `CompanyCoverageComplete` |
| D4 alias coverage | Product owner | keep `approved-aliases-only` (answers stay partial) or confirm complete mapping |
| Stale unit tests (12 Feature 141, 8 unrelated) | Product owner + engineering | unchanged since the baseline report |

After D1–D3 are applied, rerun `CompanyDiscoveryQuestionRunsLiveLlmToIsolatedPostgreSqlAndReturnsTheProducerSet` without the fixture-only snapshot and mark F141-E10 PASS.
