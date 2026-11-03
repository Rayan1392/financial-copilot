# Feature 141 — Core Capabilities Implementation Plan (2026-10-29)

Objective: natural-language Persian retrieval and analysis over persisted financial statements and monthly production/sales, prioritizing query capability, semantic correctness, numeric accuracy and generality; reuse the catalog, planner, compiler and executor; no new auth/tenant architecture; no LLM-generated SQL; no Chat routing changes yet. Each slice is executable and verified by the live runner (`Feature141EngineVerticalSliceTests`) plus offline PostgreSQL scenario tests.

## Slice status

| # | Slice | Capability | Status |
|---|---|---|---|
| 0 | Baseline | one statement item (161), typed rejections, latest-period semantics, monthly production (E07), discovery readiness (E10) | done 2026-10-28/29 |
| 1 | **Core retrieval breadth** | statement items by canonical concept or exact registered title (net profit, operating profit, EPS, operating profit per share, any registered title), monthly production/sales quantity/sales amount for one company, product and explicit month or latest month | **done 2026-10-29** (F141-E01, E02, E03, E04, E07, E08, E09 live; see `core-capabilities-slice1-report.md`) |
| 2 | Discovery breadth | "which companies produce X", "which companies sold X in period", "what products does company Y produce" from persisted observations; approved equivalence; provenance | E10 implemented (readiness, two owner decisions pending); sold-X and products-of-company planner paths next |
| 3 | Query algebra | filter, sort, top N, group by, aggregation, period comparison, MoM/YoY growth, cross-company comparison, multiple conditions composed through `StructuredQueryPlan` | **3a done 2026-10-29**: statement-item cross-company comparison, sort, top N, deterministic ties over named companies or the authorized population (E05, E06, E14 + negatives E21–E28; `core-capabilities-slice3a-report.md`); monthly MoM/YoY and bounded series exist; 3b filters (E15, E17), 3c aggregation (E16) and statement period comparison (E12, E13) next |
| 4 | Cross-domain | statement metric + monthly sales/production + company/industry dimensions with compatibility checks and explicit limitations | after slice 3 |
| 5 | Evaluation | suite v1 extended per slice with metric/company/product/period/value/unit/filter/answer checks on production components and isolated PostgreSQL | continuous (`engine-e2e-suite-v1.json`) |

## Slice 1 design (this turn)

- Catalog enablement under the capability directive (provenance `engineering-applied:Feature141-core-capabilities-directive-2026-10-29`, owner countersign pending): canonical bindings `netProfit→IncomeStatement/143`, `operatingProfit→140`, `earningsPerShare→160` with effective units from the statement-unit policy; `metric_monthly_sales_quantity` and `metric_monthly_sales` approved with verified unit policies; `SalesQuantity`/`SalesAmount` added to the line-items column allowlist. `SalesRate` stays unapproved (unit unverified). Catalog hash `93acd370…`.
- Engine: `StructuredQueryCompiler` carries a directive-approved effective unit like an owner-approved one; no other engine change was needed, because the handoff (exact title or approved concept), monthly planner, compiler, authorizer and executor already generalize.
- Runner generalization: a case declares its statement item (type, id, title) or its monthly measure column; fixtures are seeded from the case; source-item identities are read from the catalog, never typed into the fixture; one generic live assertion covers every answered case.

## Next slice recommendation

Slice 3a: statement-item cross-company comparison and ranking (E05, E06, E14) on top of the existing `MultiCompanyComparison` plan mode: bind N companies, same source item, same latest period, compile a bounded multi-company SELECT, render a comparison. Then 3b: threshold filters (E15, E17) through `PlanFilter` on the projected value, and 3c: sum aggregation (E16).

## Slice 3a outcome (2026-10-29)

Verdict CROSS_COMPANY_COMPARISON_PASSED; design, evidence and limits in `core-capabilities-slice3a-report.md`. Next slice
recommendation: 3b threshold filters on the projected statement value (`PlanFilter` on the compared value, reusing the
latest-common-period candidate query), then 3c sum aggregation.
