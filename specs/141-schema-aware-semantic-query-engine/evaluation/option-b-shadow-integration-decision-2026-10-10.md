# Option B Shadow Integration Decision — 2026-10-10

## Decision: NO_GO

The verified percentage-result validator defect is fixed and covered by focused tests. End-to-end evidence was not established: the required disposable PostgreSQL fixture could not start because the local Docker daemon did not respond, and the current Navigator boundary cannot carry the catalog-independent semantic intent losslessly. No mocked execution or production-route change was used to fill that gap.

## Implemented

`CanonicalSemanticIntentValidator` now validates a threshold on a `PercentageChange` measure against the percentage result dimension. It leaves the base measure dimension unchanged, and still rejects incompatible units for direct physical and monetary values. Unspecified threshold units remain unspecified; this change does not infer source units.

Focused regression coverage includes percentage growth over physical quantity and monetary sales, direct physical and monetary thresholds, incompatible dimensions, and missing/ambiguous units.

## Navigator compatibility finding

`SchemaNavigationRequest` carries the question and catalog identity, but no canonical semantic intent. `QuestionSchemaRequirements.MeasureIds`, `DimensionIds`, `FinancialValueSelection.CatalogId`, and `StructuredPlanShape` references are catalog identifiers. The Navigator seeds and validates these as exact required catalog references; `SchemaContextCompletenessValidator` checks them against catalog IDs rather than resolving business concepts from metadata. The contract has no typed representation for per-measure qualifiers, requested result unit, or source-unit uncertainty.

Consequently, directly placing canonical concept IDs in those reference fields would make them appear to be catalog IDs; passing empty references would silently drop required measure semantics. No adapter was added that would guess a mapping or weaken completeness validation. The next safe compatibility change must give the Navigator a catalog-independent semantic requirement input and validate its discovered references against governed metadata before planning. That contract change requires end-to-end verification and is outside the evidence available in this run.

## End-to-end matrix

The requested representative suite was not executed. Docker/Testcontainers did not become available, so each runtime stage below has zero observations; `0/0` is blocked evidence, not a pass.

| Stage / metric | Evidence |
|---|---:|
| Canonical intent correctness in E2E suite | 0/0 — not run |
| Navigator reference binding | 0/0 — not run |
| Completeness validation / planning | 0/0 — not run |
| Authorization and SQL compilation | 0/0 — not run |
| Isolated PostgreSQL execution | 0/0 — blocked; fixture requires unavailable Docker |
| Exact numeric, unit, and period correctness | 0/0 — not run |
| Persian rendering / end-to-end completion | 0/0 — not run |
| Unsafe or unauthorized execution | 0/0 — no E2E execution attempted |
| Untyped pipeline failures | 0/0 — not measurable |
| Model tokens / latency for this E2E suite | 0 calls — not run |

No 15–20-case suite predictions were inspected, and no expected labels were created or changed based on predictions. The existing isolated tests are not presented as end-to-end financial-answer evidence.

## Verification

- Release solution build: passed, 0 warnings and 0 errors.
- Unit tests: 2,071/2,071 passed.
- Architecture tests: 12/12 passed.
- Focused canonical-intent tests: 44/44 passed, including the new threshold cases.
- Catalog integrity validation: passed; catalog hash `91bd31549418bdbd870b878079efa2808f9122d02cb7725aab5d4bab8595d1a2`.
- PostgreSQL integration suite: blocked before execution because the Docker daemon was unresponsive; no live Development or production database was accessed.

## Remaining gaps

The binding adapter and E2E harness remain unimplemented. No evidence is available for Navigator binding, Planner correctness, restricted authorization, compiler behavior, PostgreSQL results, exact fixture values, or Persian answer rendering. The validator fix is unit-verified only.

`NO_GO` means the present evidence does not support enabling or expanding Option B, even in a restricted execution scope. It does not establish that the pipeline would fail with the required adapter and disposable database available. Reassess only after the semantic requirement contract is preserved through Navigator binding and the isolated PostgreSQL evaluation can run.
