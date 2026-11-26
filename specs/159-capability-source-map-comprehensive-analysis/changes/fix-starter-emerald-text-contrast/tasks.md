# Tasks: Light-theme contrast of the emerald starter text (BUG-049)

Status: Gate A and Gate B: Approved by the owner on 2026-11-26 («BUG-049 را رفع کن»)

## Feature Folder

`159-capability-source-map-comprehensive-analysis/changes/fix-starter-emerald-text-contrast`

## Tasks

- [x] 1. `styles.css`: `.emerald-ink-text` with the light-theme ink `oklch(0.45 0.12 160)`.
- [x] 2. The `/chat` coverage badge and the phone sales topic use `emerald-ink-text`; the browser fixture repeats the
  badge classes.
- [x] 3. `scripts/starter-cards.browser.mjs`: remove the BUG-049 exclusion so all text is asserted; add `--evidence <dir>`
  so a later change does not overwrite the starter card's evidence.
- [x] 4. Vitest regression test in `starter-card-grid.test.tsx`: the phone sales topic has `emerald-ink-text`.
- [x] 5. `specs/bugs.md` BUG-049 status; CHANGELOG `[Unreleased]` entry; `specs/README.md` index entry.

## Verification (2026-11-26)

- `node scripts/starter-cards.browser.mjs --evidence specs/159-capability-source-map-comprehensive-analysis/changes/fix-starter-emerald-text-contrast/evidence/browser`
  passes with every text element asserted. The lowest text contrast is 5.79:1 (light) and 5.30:1 (dark), so the badge
  and the phone sales topics are now at least 5.79:1 in the light theme (before: 3.97:1 and 4.07:1).
- Vitest: `starter-card-grid.test.tsx`, `ui-refresh.test.tsx`, `starter-prompts.test.ts` and `src/routes` pass
  (37 tests, plus the new regression test).
- Lint clean on the changed files; `npm run typecheck` 37 errors (the baseline); `npm run build` green.

## Definition Of Done

- [x] Build green; changed-file lint clean; typecheck no new error; relevant tests green.
- [x] BUG-049 contrast at least 4.5:1 in both themes, verified in the browser.
- [x] Records updated (bugs, CHANGELOG, README).
- [ ] Release, tag and deploy only after the owner's separate confirmation.
