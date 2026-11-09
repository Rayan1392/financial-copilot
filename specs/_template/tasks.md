# Tasks: <Title>

Status: Gate A: Approved by <owner> on <YYYY-MM-DD> — plan proposed, Gate B not approved

## Feature Folder

`NNN-<feature-name>`  (or `NNN-<feature-name>/changes/<kebab-change-name>`)

## Before Starting

- [ ] This file's `Status:` line records Gate B approval by the owner (see `specs/FEATURE_DEVELOPMENT_WORKFLOW.md`, "Recording Approval").
- [ ] The features in `Depends On` are done (their `tasks.md` definition of done is ticked).
- [ ] Read `user-story.md`, the references it lists, and the relevant `docs/` files.
- [ ] No open question in `specs/open-questions.md` blocks this spec.

## Implementation Tasks

### 1. <Group>

- [ ] Task

## Tests

- [ ] Unit / integration / architecture tests for the above (`tests/FinancialCopilot.UnitTests`, `tests/FinancialCopilot.IntegrationTests`, `tests/FinancialCopilot.ArchitectureTests`).
- [ ] Frontend tests for UI work (`src/frontend`, Vitest).

## Definition Of Done

- [ ] `dotnet test src/backend/FinancialCopilot.sln --configuration Release` green (builds the solution and runs unit, integration and architecture tests): no failure outside `specs/test-baseline.md`.
- [ ] For database work: `dotnet ef migrations has-pending-model-changes --context <DbContext>` reports no pending model changes for every touched DbContext.
- [ ] For frontend work, in `src/frontend`: `npm run lint`, `npm run test` and `npm run build` green, and `npm run typecheck` adds no type error beyond `specs/test-baseline.md`.
- [ ] Acceptance criteria in `user-story.md` all checked.
- [ ] Reference docs updated: `specs/README.md` index entry, affected `docs/*.md` and `README.md` sections.
- [ ] User-visible change: a Persian, user-facing line under `## [Unreleased]` in `CHANGELOG.md` (see `AGENTS.md`, "Versions and Releases").
- [ ] New questions → `specs/open-questions.md`; deferred work → `specs/backlog.md`; defects found → `specs/bugs.md`.
