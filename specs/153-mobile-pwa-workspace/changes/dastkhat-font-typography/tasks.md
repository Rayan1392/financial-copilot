# Tasks: Dastkhat Typography for Web and Mobile

Status: Gate B: Approved by the owner on 2026-11-17

## Feature Folder

`153-mobile-pwa-workspace/changes/dastkhat-font-typography`

## Before Starting

- [x] This file's `Status:` line records Gate B approval by the owner (see `specs/FEATURE_DEVELOPMENT_WORKFLOW.md`, "Recording Approval").
- [x] `153-mobile-pwa-workspace` and its mobile UI refresh dependency are complete.
- [x] Read `user-story.md`, the approved design proposal, `docs/deployment/pwa-service-worker.md`, and the Dastkhat Part 1 usage guide.
- [x] Confirm the exact Dastkhat upstream revision and selected variable WOFF2 assets before downloading them.
- [x] No open question in `specs/open-questions.md` blocks this change.

## Implementation Plan

Expected area: `src/frontend` and this feature’s documentation. No backend, database, API, route, or financial calculation changes.

### 1. Baseline and asset selection

- [x] Record current frontend verification baseline and current sizes of the Vazirmatn and JetBrains Mono font assets.
- [x] Pin the reviewed upstream Dastkhat repository revision (`f6fbd18804a587f9fdf5cccd429174f8686101e0`); verify the Main (`Dastkhat-Variable.woff2`) and FaNum (`DastkhatFaNum-Variable.woff2`) variable builds, weights, source declarations, copyright notices, and OFL 1.1 text against that revision. The main asset is named `dastkhat-main.woff2` locally to avoid a case-only filename collision on Windows.
- [x] Record selected asset sizes and confirm Main plus FaNum fit the PWA cache budget; include no unneeded families or static builds.
- [x] Add the selected Dastkhat font files and complete required license/attribution text to `src/frontend/public/fonts/`.

### 2. Shared typography and font use

- [x] In `src/frontend/src/styles.css`, define the Dastkhat Main and FaNum variable faces with weights 100–900, `font-display: swap`, and fallback stacks; keep JetBrains Mono unchanged.
- [x] Set the shared app font token to Dastkhat Main and add a dedicated FaNum token/class for financial display values.
- [x] Set Persian body line-height to at least 1.8 and use tighter heading values without changing the existing mobile type-size tokens.
- [x] Inventory financial value renderers used by desktop and `/m`; apply FaNum only to selected prices, percentages, and dashboard/financial values intended for Persian digits. Preserve raw values and use Main/LTR styling for identifiers, code, and content intended to remain Latin.
- [x] Review shared styles and user-visible strings in the changed shared/mobile surfaces for Persian half-spaces and punctuation, bidi isolation, logical CSS properties in touched directional layout rules, and accidental `letter-spacing` or `text-transform` on Persian content; fix relevant issues within approved scope.
- [x] Verify the root remains `lang="fa"` and `dir="rtl"`; add the main Dastkhat WOFF2 preload in `src/frontend/src/routes/__root.tsx`.

### 3. PWA offline assets

- [x] Replace the Vazirmatn entry in `src/frontend/public/sw.js` precache with the Main and FaNum Dastkhat assets; leave JetBrains Mono caching intact.
- [x] Check the service-worker cache version convention (the static cache name is tied to the build id); remove obsolete font references from service-worker tests and documentation.
- [x] Update `docs/deployment/pwa-service-worker.md` to list the new cached font files and offline behavior.
- [x] Remove old Vazirmatn assets and license only after verifying no remaining frontend references and preserving required third-party notices.

### 4. Verification and evidence

- [x] Update existing frontend tests for the font precache and add focused coverage for font selection in the mobile browser checks.
- [x] Run `npm run lint`, `npm run test`, `npm run typecheck`, and `npm run build` from `src/frontend`; record baseline-related failures separately.
- [x] Run the existing mobile browser checks and inspect desktop/mobile pages in both themes at representative widths, including 320px and 360px phone widths.
- [x] Verify rendered Persian joins, Dastkhat FaNum digits, mixed-direction identifiers, financial tables, charts, and text wrapping; confirm no external font requests or horizontal overflow.
- [x] Install/update the PWA, wait for precache completion, take it offline, and confirm both main Persian text and selected financial figures still render with Dastkhat.
- [x] Record selected asset sizes, browser evidence, offline result, and any deviations in `ImplementationReport.md`.

### 5. Project records

- [x] Add a Persian user-facing change line under `## [Unreleased]` in `CHANGELOG.md`.
- [x] Add this change to `specs/README.md` and update any deployment/font references affected by the implementation.
- [x] Update the related mobile UI refresh acceptance/evidence wording that currently names Vazirmatn; retain unrelated historical criteria.
- [x] Review questions, backlog, and bug records; no additional entries were needed.

## Tests

- [x] Frontend unit tests for service-worker precache contents and scoped font-family behavior.
- [x] Existing `npm run test:mobile-browser` and relevant desktop browser checks (see report for two failures).
- [x] `npm run lint`, `npm run test`, `npm run typecheck`, and `npm run build` in `src/frontend`.
- [x] Browser visual and offline PWA checks described in section 4.

## Definition Of Done

- [ ] Frontend checks were run and failures are documented in `ImplementationReport.md`, but the existing baseline is not clean enough to confirm there are no new non-font failures.
- [ ] Acceptance criteria in `user-story.md` were checked through code review and browser verification; desktop overflow/wrapping remains unverified because the desktop fixture stopped at its missing-legend assertion.
- [x] Desktop web and `/m` use the approved Dastkhat assets and FaNum on the selected financial display surfaces.
- [x] PWA fonts work offline after precache; no external font requests or stale Vazirmatn app/service-worker references remain where the old font was removed.
- [x] License and copyright attribution accompany the redistributed fonts.
- [x] `ImplementationReport.md`, `CHANGELOG.md`, `specs/README.md`, and deployment documentation are updated as planned.
- [ ] No commit to `develop`, push, release, or production deploy without the owner’s separate go-ahead.
