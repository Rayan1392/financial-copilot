# Implementation Report: Dastkhat Typography for Web and Mobile

## Summary

Implemented the approved Dastkhat Main and Dastkhat FaNum typography change in the shared frontend used by desktop web and the `/m` PWA. The UI now uses self-hosted Dastkhat Main, selected financial displays use FaNum, and both faces are included in the PWA's static precache. JetBrains Mono remains unchanged.

## Assets and source

- Upstream repository revision: `f6fbd18804a587f9fdf5cccd429174f8686101e0`.
- Added `dastkhat-main.woff2` (150,584 bytes) and `dastkhat-fanum-variable.woff2` (150,704 bytes), plus the Dastkhat OFL/copyright notice.
- The Main asset uses a distinct local filename to avoid a case-only filename collision on Windows with upstream `Dastkhat-Variable.woff2`.
- Kept `jetbrains-mono-latin-variable.woff2` (40,404 bytes) and its license. The new font precache total is 341,692 bytes, up 190,136 bytes from the previous Vazirmatn + JetBrains Mono total of 151,556 bytes. It remains within the service worker's 200-entry static asset limit.
- The font files use self-hosted WOFF2 variable faces. Main is preloaded, both faces use `font-display: swap`, and their CSS weight range is 100–900.

## Implementation

- Replaced Vazirmatn with Dastkhat Main in the shared font token and canvas/PDF-like image renderers.
- Added a `font-fanum` utility and applied it to selected financial figures across desktop and mobile surfaces while preserving stored values and identifiers.
- Set Persian body line height to 1.8 and heading line height to 1.45; left mobile text-size tokens unchanged.
- Preserved `lang="fa"` and `dir="rtl"`, and updated the PWA precache, service-worker test, deployment documentation, related mobile UI refresh wording, specs index, and Unreleased changelog.
- Removed the obsolete Vazirmatn font and its license after checking frontend references.

## Verification

- `npm run build` — passed for the production client and Worker/SSR bundles.
- `npm run test -- src/lib/pwa/__tests__/service-worker.test.ts --reporter=dot` — passed, 8 tests.
- `npm run test:mobile-browser` — 94/96 checks passed. Dastkhat font loading, FaNum selection, no external font requests, PWA font cache, offline reload, both themes, and 320px/360px/390px mobile layouts passed. Two broader existing checks failed: the desktop answer fixture did not expose a Recharts legend, and the signed-out flow did not expose the password field. The prior Feature 153 baseline recorded 78/91 mobile browser checks; these failures are outside the font assertions and require separate baseline comparison to attribute conclusively.
- `npm run typecheck` — 37 errors, matching the known frontend baseline recorded in `specs/test-baseline.md`; no new type errors were identified.
- `npm run test -- --reporter=dot` — 52 test files passed and 6 failed; 406 tests passed and 3 failed, with 16 worker errors. The known baseline includes 3 browser suites that Vitest cannot run under its current TextEncoder environment and 2 failing tests. The full run was noisier than baseline, so the additional failures are recorded as unresolved rather than attributed to this change.
- `npm run lint -- --quiet` — failed with 51,870 Prettier errors, mostly CRLF normalization. The existing frontend baseline was already 33,064 errors, predominantly CRLF-related. A changed-files-only run still reported CRLF formatting errors and two non-Prettier findings in touched chart files (`no-useless-escape` and the missing `react/forbid-dom-props` rule); the latter files also appear in the recorded baseline. No automatic formatting was applied because that would rewrite large portions of the frontend.
- `git diff --check` — passed.

## Limitations

The offline PWA/browser checks passed. The existing test, lint, typecheck, and two browser-suite failures remain documented above; the repository's recorded baseline is not clean enough to assert that every non-font failure is pre-existing without a clean baseline checkout. No commit, push, release, or deployment was performed.
