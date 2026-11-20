# Tasks: Fix — Cash Flow Report Uses a Fixed Light Surface (BUG-038)

Status: Gate A and Gate B: Approved by the owner on 2026-11-21 («Gate A و B تأیید است، پیاده‌سازی کن»)

## Feature Folder

`161-ai-cash-flow-statement-report/changes/fix-fixed-light-report-surface`

## Before Starting

- [x] This file's `Status:` line records Gate A and Gate B approval by the owner.
- [x] Read `design-proposal.md` in this folder and AC-U07 in `../../design-proposal-ux.md`.

## Implementation Tasks

### 1. Component

- [x] 1.1 Remove all `dark:` classes from `src/frontend/src/components/app/cash-flow-report/cash-flow-report.tsx`.
- [x] 1.2 Replace `text-muted-foreground` there with `text-slate-600`.

### 2. Tests

- [x] 2.1 Vitest (`src/frontend/src/components/app/__tests__/cash-flow-report.test.tsx`): the rendered
      report (wide and compact, with warnings and evidence open) has no `dark:` class and no theme token.
- [x] 2.2 Browser check (`src/frontend/scripts/cash-flow-report.browser.mjs`): replace the
      `emulateMedia` dark screenshot with OS-dark + site `.dark` and OS-dark + site `.light`; assert the
      card background is light in both and text contrast is ≥ 4.5:1; save the screenshots.
      Done: 46 text nodes, min 4.76:1 in both; the Recharts legend is excluded (BUG-039, 2.15:1 in both
      themes, outside this fix). The check fails on the old component (card luminance 0.009).

### 3. Records

- [x] 3.1 `specs/bugs.md` BUG-038 status; `specs/backlog.md` entry for the other `dark:` components.
- [ ] 3.2 `CHANGELOG.md` `[Unreleased]` entry via release-manager.

## Definition of Done

- [x] ESLint (`endOfLine: auto`) clean on the touched files; `tsc --noEmit` reports no error in cash-flow files (remaining errors are pre-existing, other files); cash-flow Vitest 10/10. `src/components/app` Vitest: 2 failures that also fail on unmodified `develop` and are outside the baseline → BUG-040.
- [x] `npm run test:cash-flow-browser` passes; screenshots show the same light card in both site themes with the OS set to dark, matching the PNG export.
- [x] No new warnings.
