# Feature 119 Tasks — Canonical Query Entity and Slot Resolution

## [x] Task 1 — Audit Existing Resolvers and Extractors

Map every symbol/company extraction and canonical-resolution path in V1, V2, tools, and deterministic routes.

Acceptance:

- Duplicate/local extraction behavior is documented.
- Canonical identity authority and provider adapters are identified.
- Migration risks and compatibility requirements are recorded.

## [x] Task 2 — Define Typed Resolution Contracts

Add `Resolved`, `Ambiguous`, `NotFound`, and `Missing` results plus bounded candidate/evidence models.

Acceptance:

- No normal resolution state depends on `null`.
- Contracts are provider/storage/framework-neutral.

## [x] Task 3 — Build the Canonical Company Resolver Adapter

Unify ticker, company name, approved alias, and normalized variants over canonical identity data.

Acceptance:

- Resolution order is deterministic.
- Provider catalogs cannot become query-time identity authority.
- No outbound provider call occurs.

## [x] Task 4 — Add Bounded Ambiguity Candidates

Return safe fuzzy/alias candidates for disambiguation without silently executing.

Acceptance:

- Candidate count and scoring are bounded.
- Exact/unambiguous matches outrank fuzzy candidates.
- Candidate labels are localized and stable.

## [x] Task 5 — Define Reusable Slot Contracts

Represent required slot types, provenance, confidence, validation status, and capability compatibility.

Acceptance:

- Slot values reference governed canonical identifiers.
- User-explicit and inferred/defaulted values remain distinguishable.

## [x] Task 6 — Implement Slot Validation and Priority

Validate extracted slots against Feature 118 definitions and choose the next clarification slot.

Acceptance:

- The system asks one focused question at a time.
- Unsupported slot combinations retain all correctly understood fields.

## [x] Task 7 — Add Legacy Route Adapters

Provide a staged adapter so existing use cases can consume resolved canonical identity without immediate full migration.

Acceptance:

- No second business-rule path is created.
- Feature flags permit controlled rollout and rollback.

## [x] Task 8 — Integrate Outcome Mapping

Map missing, ambiguous, not-found, and resolved-no-data states into Feature 117 outcomes/reasons.

Acceptance:

- No-data is emitted only after successful identity resolution.
- Technical failures remain separate.

## [x] Task 9 — Add Resolver and Slot Test Matrix

Cover exact ticker, company name, aliases, character variants, ZWNJ, punctuation, ambiguous names, typos, unknowns, and semantic distractors.

Acceptance:

- `چارت`, `نمودار`, periods, metrics, and verbs never become symbol candidates.
- V1/V2 deterministic equivalence is asserted.

## Completion Gate

Keep the feature unchecked until typed resolution is used by the first migrated routes, ambiguity never executes silently, and false no-data behavior is eliminated for entity failures.
