# Feature 125 — Industry Relative Valuation Ranking

## Workflow status

This feature follows the gated delivery sequence used for feature work:

1. **Design plan** — completed.
2. **Architecture / design review gate** — next/current gate; result must be `APPROVED` or `NEEDS_CHANGES`.
3. **User story and acceptance criteria** — created only after the design gate is approved.
4. **Implementation task breakdown** — created from the approved design/story.
5. **Implementation and migration review/apply**.
6. **Release verification** — build, tests, integration/regression evidence.
7. **Completion gate** — only then update the global implementation ledger.

Current status: **GROUP_COHORT_CORRECTION_READY_FOR_REVIEW — the comparison cohort is corrected from
`IndustryId`/`IndustryTitle` to `GroupId`/`GroupTitle`; implementation is not authorized by this
documentation-only amendment**.

## Active correction

For Feature 125, “compare a symbol with its industry” means compare it only with eligible companies
that have the same `GroupId`. `GroupTitle` is the displayed cohort title. `IndustryId` and
`IndustryTitle` remain broader classification metadata and must not determine benchmark membership,
rank population, publication identity, or same-cohort validation.

For `شگل`, the authoritative group is:

```text
GroupId:    97ac765e-c5d6-4e5d-b9de-eb9e0b4e806c
GroupTitle: تولید محصولات آرایشی و بهداشتی
```

The active implementation authority is `design.md`, `user-story.md`, and `tasks.md` as amended by
this correction. Earlier design/review/acceptance documents are historical evidence and their
`IndustryId`-based cohort statements are superseded.

## Current files

- `design.md` — completed Stage 1 design plan with locked product decisions and Stage 2 review inputs.
- `design-review.md` — post-remediation strict architecture review with the approval verdict.
- `user-story.md` — approved Stage 3 user story and acceptance criteria.
- `user-story-review.md` — approved independent Stage 3.1 review.
- `tasks.md` — Stage 4 implementation task breakdown, ready for implementation review.
- `bug-001-shgol-own-industry-comparison-returns-no-data.md` — open root-cause report, amended with
  the authoritative group-based comparison requirement.

The corrected task breakdown requires a new architecture and persistence-impact review before
implementation. Production code, tests, database rows, and migrations remain unchanged and
unauthorized by this amendment.
