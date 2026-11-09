# Spec-Driven Workflow (mini)

## Artifacts

The mandatory feature lifecycle and approval gates are defined in `specs/FEATURE_DEVELOPMENT_WORKFLOW.md`. The owner decides which feature is worked on and when; never pick a feature yourself. `specs/implementation-checklist.md` is a historical completion ledger, not a work queue.

| File | Purpose |
|---|---|
| `specs/NNN-name/design-proposal.md` | Proposed design for Gate A; carries the `Status:` line. |
| `specs/NNN-name/user-story.md` | Why and what: story, context, scope, out of scope, references, acceptance criteria, assumptions. |
| `specs/NNN-name/tasks.md` | How: ordered, checkable tasks; definition of done; carries the `Status:` line for Gate B. |
| `specs/NNN-name/changes/<kebab-change-name>/` | Changes and bug fixes to an existing feature (same three files). |
| `specs/backlog.md` | Ideas and deferred work not yet a spec. |
| `specs/bugs.md` | Defects found (id, spec, steps, expected, actual, status). Investigation notes may stay in `specs/bugs/`. |
| `specs/open-questions.md` | Decisions the owner must make. Never decide these silently. |
| `specs/README.md` | Index of feature specs and their coherence rules. |
| `specs/test-baseline.md` | Known pre-existing test and type-check failures; "green" means no failure outside this list. |
| `docs/` | Architecture, domain glossary, data model, provider and operations docs. |
| `docs/adr/` | Architecture decisions from 2026-11-09 onward (context, decision, consequences). |

## Folder naming and numbering

- One folder per feature: `NNN-<feature-name>` (for example `154-ai-balance-sheet-report`). This is the existing convention in this repository; layers are named in the spec header, not in the folder name.
- `NNN` is the next free three-digit feature number. Check `specs/` before choosing it; numbers are not reused.
- The folder number is not an execution order; the owner decides what is worked on and when.
- Older folders use `spec.md`, `design.md`, `discovery.md`, `plan.md`, `ImplementationReport.md`. Keep them; new work uses the templates in `specs/_template/`.
- Every spec header states its **Layers** and **Area**.

## Header of every user story

```markdown
## Feature Folder   `NNN-<feature-name>`
## Layers / Area    backend|database|frontend|telegram-gateway|ops · AI|Scanner|FinancialData|Billing|Authentication|Conversations|Memory|Notifications|Telegram|Administration|cross-cutting
## Priority         P0 | P1 | P2 | P3 | Reference | Dev
## Complexity       S | M | L | XL
## Depends On       NNN-other, ... | None
## References       docs, specs and code this spec relies on (docs/domain-glossary.md, docs/architecture.md, EF Core model, provider docs)
```

## Executing an approved plan

Implementation is permitted only after explicit Gate B approval of the detailed implementation plan. Gate A design approval alone is not implementation authorization. Existing `user-story.md`, `spec.md` or `tasks.md` files, and ticked rows in `specs/implementation-checklist.md`, do not prove that either gate was approved.

1. Check that the features in the spec's `Depends On` header are done (their `tasks.md` definition of done is ticked). If not, stop and say which are missing.
2. Read `user-story.md`, `tasks.md`, the references listed in the header, and the relevant `docs/` files.
3. If something is ambiguous or missing in the references, write it in `specs/open-questions.md` and the spec's *Assumptions*, ask the owner, and pause any work affected by the answer. Do not expand the approved scope.
4. Work task by task in order; tick `- [x]` in `tasks.md` immediately after each task is done and verified.
5. Keep the change inside the spec's scope. Extra ideas → `specs/backlog.md`.
6. Definition of done: build green, tests green (incl. architecture tests) with no failure outside `specs/test-baseline.md`, acceptance criteria met, reference docs updated, no new warnings.
7. Finish with a short summary: what was done, what is left (`[~]`), new open questions.

## Creating specifications

For a new feature, do not create finalized `user-story.md` or `tasks.md` during discovery or before explicit Gate A design approval. After Gate A, copy `specs/_template/` into `specs/NNN-<feature-name>/` (or `specs/<feature-folder>/changes/<kebab-change-name>/` for a change), fill every header field, link references by repository path (for example `docs/domain-glossary.md`), write testable acceptance criteria, and prepare the implementation plan. Present the plan and obtain explicit Gate B approval before production implementation. See `specs/FEATURE_DEVELOPMENT_WORKFLOW.md` for existing-feature changes, bug fixes, hotfixes, recording approval, and approval details.
