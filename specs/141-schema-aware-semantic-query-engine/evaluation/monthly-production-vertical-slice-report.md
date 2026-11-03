# Feature 141 — Monthly Production Quantity (F141-E07) Governance and Slice Report — 2026-10-28

**Final status: `MONTHLY_PRODUCTION_APPROVAL_REQUIRED`.** The data contract is verified end to end in code and fixtures, the governance record is prepared, and the chain is proven to work under test-only authorization. The canonical catalog still marks the measure, both monthly tables and both relationships as unapproved and non-queryable, and no product-owner decision exists in the repository. Per the task's constraint, implementation stopped before any authorization change; no catalog flag, production data, chat routing or unrelated file was touched.

## 1. Verified data contract (Step 1)

| Aspect | Verified fact | Where |
|---|---|---|
| Source | NoavaranCurrentApi monthly `ProductSales` reports, `OutputType 0` (monthly, non-cumulative). One `MonthlyReports` row per logical report revision (`LogicalReportKey`, `RevisionFingerprint`, `RevisionStatus`, `IsAccepted`), one `MonthlyReportLineItems` row per product line. | `FinancialIngestionRows.cs` (rows 315–410), catalog `providerPolicy`, Features 024/042/059 ingestion |
| Value | `MonthlyReportLineItems.ProductionQuantity` (decimal), persisted as reported; no formula or conversion. | catalog `physicalColumn`, compiler `CompileMonthly` |
| Company identity | `MonthlyReports.CompanyId -> Companies.Id` (`rel_4e54d40aee8b45e4`). Plans bind the company through `CanonicalQueryEntityResolver` (exact ticker/name); `ExternalCompanyId` is never used as identity. | `StructuredQueryCompanyBinder`, compiled `@company_id` filter |
| Product identity | Company-scoped `ProductCode` resolved by `CanonicalQueryProductResolver` via `StructuredQueryProductBinder`; key shape `provider:externalCompany:TITLE:<title>|UNIT:<unit>`. Concept `product_iron_ore_pellets` (aliases گندله, گندله سنگ‌آهن) is owner-approved for alias matching only. No hardcoded company-product lookup exists or is proposed. | `StructuredQueryProductBinder.cs`, `product-catalog-v1.json`, 2026-10-09 artifact (`productKey = NoavaranCurrentApi:feature141-kgehr:TITLE:گندله|UNIT:ton`) |
| Period | `MonthlyReports.PeriodStart/PeriodEnd` are Gregorian bounds of the Persian report month; compiled filter `r.PeriodStart >= @period_start AND r.PeriodEnd <= @period_end`; Shahrivar 1405 = 2026-08-23..2026-09-22 (fixture rows in `MonthlyStructuredQueryPostgreSqlTests`). The Persian-month to Gregorian window conversion is produced by the model (`structuredShape.reportingPeriodStart/End`, prompt line 97 of the navigator), not by deterministic code. | compiler lines 557–559, navigator prompt/schema |
| Unit | `unitPolicy: quantity / source-quantity-unit / compatibleOnlyWithinSameUnit`; the answer's unit is the returned row's `Unit` (rendered through `QuantityUnitDisplayCatalog`); mixed-unit aggregation rejected (`UnitCompatibilityCount`). | catalog entry, compiler, executor |
| Revision and duplicates | Compiled filters `ProviderName`, `ReportType`, `OutputType`, `IsAccepted=true`, `RevisionStatus='Accepted'`; `SamePeriodCandidateCount>1`, `DuplicateProductCount>0` or `SourceMultiplicity<>1` → executor `Ambiguous` (`monthly_product_period_has_multiple_source_rows`). Superseded/rejected revisions are excluded by the filter, never chosen. | compiler parameters lines 459–461, executor monthly checks |
| Authorized objects | None today: measure `review.semanticApproved=false, queryable=false`; both tables `runtimeQueryable=false`, `approvedObjects=[]`; `rel_31e410fb3ae11956` and `rel_4e54d40aee8b45e4` not approved / not joinable. | catalog YAML (inspected 2026-10-28) |

Readiness evidence (not a pass): on 2026-10-09 the live question `مقدار تولید گندله کگهر در شهریور ۱۴۰۵ چقدر بوده؟` ran through the real extractor, navigator, planner, product/company binders, compiler and restricted executor against an isolated fixture with an **in-memory test-only approved snapshot** and returned 47 = fixture (`artifacts/monthly-production-sales-live-llm-postgresql-readiness-v2-diagnostic-2026-10-09-postfix-2026-10-09.json`, provenance `test-only-isolated-catalog-approval`, 2 model calls, 8,014/1,150 tokens). That proves the mechanism, not the governance.

## 2. Governance decision (Step 2)

Compact record: `evaluation/governance/metric_monthly_production_quantity-decision-request-2026-10-28.json` (status `APPROVAL_REQUIRED`). Summary of the proposed decision for the product owner:

- **Meaning (A1):** quantity of the named product produced by the company in the report month as reported in its monthly activity report, in the line's own unit; not YTD, not derived.
- **Mapping (A2):** `MonthlyReportLineItems.ProductionQuantity` ⋈ `MonthlyReports` (`rel_31e410fb3ae11956`) ⋈ `Companies` (`rel_4e54d40aee8b45e4`), `Noavaran / ProductSales / OutputType 0`.
- **Revision (A3):** only `IsAccepted` and `RevisionStatus='Accepted'`; two accepted candidates for one company/product/month are ambiguous, never resolved by choice.
- **Unit (A4):** report the row unit verbatim; never convert; a missing unit is incomplete, not "ton".
- **Time (A5):** named Persian month → report whose window lies inside it; no interpolation; "latest" = latest accepted month for that company/product.
- **Authorization boundary (A6):** SELECT on the listed 10 + 7 columns and two relationships, `DirectValueRetrieval`, one tenant/company; sales measures excluded.
- **Engineering item (A7):** deterministic `PersianCalendar` normalization of the model-provided window before planning.

Verified facts and assumptions are listed separately in the record. Nothing was marked approved: there is no owner approval reference for any monthly object in the catalog, `ProductCatalog/reviewer-decisions/`, or the monthly evaluation report (which states "Monthly objects and the logical relationship remain unapproved and non-queryable").

## 3. Implementation and verification (Steps 3–4)

Not executed, by the task's own gate ("Once the required governance approval exists"). Prepared plan, to be run unchanged once the record is approved and the catalog regenerated:

1. Add `F141-E07` as an executable case to `Feature141VerticalSliceCases` (question `تولید گندله کگل در شهریور ۱۴۰۵ چقدر بود؟`, expected 25 ton, 1405-06 = 2026-08-23..2026-09-22) and extend the runner's seed with a monthly fixture block (accepted Shahrivar 1405 report for کگل with a `گندله` line, 25 ton; decoys: another product, another company, Mordad 1405, a rejected revision of the same month).
2. Chain: live extractor → `requiresGeneralPlanner` path (as in `Feature141ChatQueryService`) → live navigator → planner with `IStructuredQueryProductBinder` and `IReviewedProductConceptResolver` from the composition root → compiler → `CatalogStructuredQueryAuthorizer` → executor → Persian answer with value, `تن`, and the month.
3. Negative scenarios (a `Feature141MonthlyProductionSemanticsPostgreSqlTests` class mirroring the latest-period test, no LLM): missing product → `NotFound` product binding, no SQL; missing report month → `NoData`; duplicate accepted reports for the same month → `Ambiguous`; superseded revision (`RevisionStatus='Superseded'`) → excluded, accepted value returned; wrong company → decoy rows never returned; wrong month → `NoData`; missing/empty row unit → `Incomplete`/unit-unknown rendering, mixed units across rows → `Ambiguous`; restricted role without SELECT on `MonthlyReports` → executor `Unauthorized` (`database_query_failed`/identity check), and the catalog authorizer refusing the plan while the catalog flags are false.
4. Keep the four passing suite scenarios passing (rerun `Feature141EngineVerticalSliceTests`).

## 4. Test results in this task

| Check | Result |
|---|---|
| Catalog inspection of measure, tables, relationships | all unapproved / non-queryable (facts above) |
| Suite invariants (`Feature141EngineE2ESuiteTests`) after updating F141-E07's blocker to the governance record | see section 6 |
| Existing four passing cases | unchanged (no code change in this task) |

Expected vs actual value for F141-E07: **expected 25 ton for کگل / گندله / 1405-06; actual: not executed (approval required).**

## 5. Remaining blockers

| Blocker | Owner | Action |
|---|---|---|
| Product-owner approval of the governance record (A1–A6) | Product owner | Approve or amend; engineering then applies `proposedCatalogChange` and regenerates the hierarchy and hash. |
| Deterministic Persian month → window conversion (A7) | Engineering | Normalize `reportingPeriodStart/End` with `PersianCalendar` and reject windows that are not a whole Persian month. |
| `rel_4e54d40aee8b45e4` has only pg_catalog provenance, no application-model verification record | Engineering | Add the verification entry (EF model `MonthlyReports.CompanyId`) before approval. |

## 6. Next recommendation

Obtain the owner decision on the record above (one approval covers all four catalog objects for one measure and one operation). Immediately after approval, run the prepared slice and negative scenarios; the mechanism has already produced the correct fixture value under test-only authorization, so the remaining risk is governance, not engineering.
