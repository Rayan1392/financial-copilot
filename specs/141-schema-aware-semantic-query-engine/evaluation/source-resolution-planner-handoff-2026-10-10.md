# Feature 141 — Bounded Source Resolution to Planner Handoff

Date: 2026-10-10

## Boundary findings

- `CanonicalSemanticIntent` v2 is schema-independent: it retains requested measures, qualifiers, operation/comparison, period, entity scope, constraints, output and ambiguity, but intentionally carries no provider/catalog identity.
- `FinancialSourceItemTitleResolver` returns an exact approved identity reference. It does not create concept equivalence or authorization.
- The LLM Navigator outline is bounded and its source candidates omit UUID, effective-unit provenance, and authorization details. It is not a complete source-resolution/planning contract.
- Existing Planner input, `QuestionSchemaRequirements.FinancialValueReferences`, is a typed `(DirectSourceItem, CatalogId)` selection. `StructuredQueryPlanBuilder` enriches this using trusted `SchemaContext` into `PlanSourceReference` identity, unit, review and `Queryable` fields. The independent plan validator checks structural/catalog identity. The offline validator reports query authorization separately and never enables execution.

## Implemented handoff

Added `FinancialSourcePlannerHandoff`: a lossless typed wrapper retains the complete original intent and resolved reference, plus provider, statement type, source ID/UUID, official Persian title, verified concept only when an existing approved binding matches, original measure phrase, qualifiers, period, entity scope, operation/comparison, source dimension/unit evidence, requested result unit, and authorization state/evidence provenance. A narrow adapter creates the Planner’s existing typed requirements; it adds no aliases and grants no authorization.

The adapter supports only direct retrieval with no comparison, latest/unspecified period, reported value, unspecified consolidation/reporting-basis/grain, known dimension and effective unit, no requested conversion, and otherwise empty unsupported intent shapes. Missing or unsupported information returns typed `Incomplete`/`Incompatible`; it is not silently dropped. Source raw unit status (`unknown`) and the intent’s own unresolved effective-unit evidence are both retained alongside the catalog’s separately classified effective unit.

## Five controlled scenarios

The four exact-title cases executed: title resolution → typed handoff → existing `StructuredQueryPlanBuilder` → existing `StructuredQueryPlanValidator`. They produced structurally valid offline plans, but all retained `Queryable=false`, `QueryAuthorized=false`, and `ExecutionEligible=false`. The structurally-valid result is not an executable or authorized plan.

| Controlled input | Handoff / Planner outcome |
|---|---|
| `IncomeStatement/143` — سود خالص | Exact source identity; no verified canonical source binding; structural plan validated; unauthorized/non-executable |
| `IncomeStatement/161` — سود عملیاتی هر سهم | Exact identity and existing `operatingProfitPerShare` binding with per-share dimension/value form; structural plan validated; unauthorized/non-executable |
| `BalanceSheet/138` — جمع کل دارایی‌ها | Exact source identity; no verified canonical source binding; structural plan validated; unauthorized/non-executable |
| `CashFlow/1` — جریان وجه نقد عملیاتی | Exact source identity; no verified canonical source binding; structural plan validated; unauthorized/non-executable |
| `IncomeStatement/143` with explicit `Consolidated` qualifier | Typed `Incomplete` (`statement_scope_evidence_unavailable`); stopped before Planner because the source registry does not establish consolidation scope |

Inputs were controlled typed test records using exact official titles, except the one already-approved concept binding. This proves no arbitrary Persian-language understanding. The test entity scope is empty because no company was included in the specified scenarios; this proof does not establish an executable company query.

Identity, title, UUID, unit evidence, source qualifier values, intent period/entity/operation/comparison, and authorization state were preserved in the handoff. The existing plan retains its narrower projection, while the typed wrapper retains fields the Plan model does not represent. The unsupported explicit consolidation qualifier is rejected rather than lost.

## PostgreSQL and execution

**BLOCKED — not attempted.** Registry authorization is false for every source item, and no fixture-only authorization or restricted disposable PostgreSQL scope is established. Development/production databases were not accessed. No compiler or executor was invoked.

## Verification

- Focused `FinancialSourcePlannerHandoffTests`: 5 passed, 0 failed.
- Release solution build (`--no-restore`): passed, 0 warnings/errors.
- No LLM calls, broad evaluation, routing changes, or catalog authorization changes.

## Recommendation

**NO_GO** for an isolated end-to-end PostgreSQL shadow proof until explicit fixture-only authorization, a disposable database, and a company-scoped fixture are available. **LIMITED_GO** only for the demonstrated offline typed handoff and structural plan validation; this is not query execution readiness.
