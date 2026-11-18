# Design Proposal: Dastkhat Typography for Web and Mobile

Status: Gate A: Approved by the owner on 2026-11-17

## Context

SapioAI serves desktop web and the `/m` mobile PWA from the shared React frontend. Both currently use a self-hosted Vazirmatn variable font. The mobile UI refresh also preloads the font and includes it in the service-worker precache so the PWA retains its typography offline.

The Dastkhat project’s [Part 1 usage guide](https://github.com/dastyar-io/dastkhat-font/blob/main/AGENTS.md#part-1-using-dastkhat-in-a-project) recommends Dastkhat Main for Persian and Latin UI text, its FaNum build for Persian-digit financial values, self-hosted WOFF2 webfonts, a preload for the main face, `font-display: swap`, RTL and Persian language metadata, logical CSS properties, Persian punctuation and half-spaces, isolation for mixed-direction text, and no letter spacing on Persian text. The font is licensed under SIL OFL 1.1; redistributed copies must retain the license and attribution.

## Proposed scope

- Replace the current Persian UI face with self-hosted Dastkhat Main across the shared web application, including desktop pages and `/m`.
- Use Dastkhat FaNum for financial values and other user-facing price, percentage, and dashboard figures where Persian digits are intended. Keep stored data, calculations, IDs, code, and URLs unchanged; use LTR isolation for Latin-only snippets and identifiers.
- Keep JetBrains Mono for code-like and monospaced content.
- Continue serving fonts from the app origin, preload the primary Dastkhat WOFF2 face, retain `font-display: swap`, and precache the required Dastkhat faces in the PWA for offline use.
- Apply the guide’s Persian typography rules in shared web and mobile UI styles and fix directly affected copy or styles encountered during the implementation: `lang="fa"`/`dir="rtl"`, body line-height starting at 1.8 with tighter headings, Persian punctuation and half-spaces, bidi isolation for mixed Latin/Persian text, logical CSS properties for touched directional layout rules, and no letter-spacing or text-transform on Persian text.
- Retain existing layout, colors, component behavior, mobile type scale, and theme behavior except for typography-related adjustments required to prevent clipping or wrapping regressions.
- Preserve the Dastkhat OFL and copyright/attribution text alongside the redistributed font assets and in the project’s third-party notices.

## Out of scope

- Backend, API, database, financial calculations, and stored-number changes.
- A native iOS/Android client; “mobile” means SapioAI’s existing responsive `/m` PWA.
- A broader visual redesign or general copy rewrite unrelated to Persian typography.
- Changes to routing, PWA rollout behavior, or the existing mobile type scale.

## Implementation areas

- `src/frontend/src/styles.css`: font faces, font-family tokens, body and Persian text defaults, numeric styling, line-height and affected directional rules.
- `src/frontend/src/routes/__root.tsx`: primary font preload; current root already declares `lang="fa"` and `dir="rtl"`.
- `src/frontend/public/fonts/`: required Dastkhat WOFF2 files and OFL/attribution text; remove or retain legacy Vazirmatn assets based on verified references and licensing needs.
- `src/frontend/public/sw.js` and its tests: replace the current Vazirmatn precache entries with the Dastkhat files needed for offline rendering.
- Shared financial value components and targeted desktop/mobile routes: apply FaNum to the rendered financial figures and audit mixed-direction content and spacing.
- Frontend browser/unit verification and screenshots at desktop and phone widths, including offline PWA font loading.

## Acceptance criteria

1. Persian and Latin UI text render in Dastkhat Main on desktop web and `/m`; no external font host is requested.
2. User-facing financial values selected for Persian-digit display render in FaNum while underlying values, arithmetic, IDs, and URLs remain unchanged.
3. Both web and `/m` retain correct RTL layout, readable Persian line-height, and isolated Latin snippets.
4. The mobile PWA renders with Dastkhat after installation and when offline after the service worker has cached its assets.
5. No Persian copy or typography uses letter-spacing or relies on `text-transform`; touched mixed-direction content remains legible.
6. Font licensing and attribution accompany every redistributed Dastkhat asset.
7. Existing desktop and mobile layouts remain usable at the project’s supported breakpoints and in light and dark themes.

## Verification approach

- Inspect the Dastkhat stylesheet and selected font builds before choosing exact WOFF2 files and CSS declarations.
- Run the frontend checks selected in the approved implementation plan.
- Verify desktop and `/m` in a browser at representative desktop and phone widths, in both themes; inspect Persian joins, digits, mixed Latin/Persian text, financial tables, and chart labels.
- Confirm the network panel has no external font requests, then verify the installed PWA renders the selected font while offline after precache.

## Risks and decisions

- Dastkhat Main and FaNum add font assets to the frontend and PWA precache. The approved plan should record their sizes and confirm the offline cache budget remains acceptable.
- FaNum is a font-level display choice. Its use must be limited to user-facing financial values; account identifiers, timestamps or values intended to remain Latin must retain an appropriate Main/LTR style.
- The exact set of financial value components to receive FaNum needs an implementation-time audit of current frontend formatting and bidi wrappers.
- Existing mobile refresh acceptance references Vazirmatn. After design approval, the implementation plan should update the affected acceptance and evidence language without rewriting unrelated historical criteria.

## Source and repository references

- Dastkhat Part 1: https://github.com/dastyar-io/dastkhat-font/blob/main/AGENTS.md#part-1-using-dastkhat-in-a-project
- Current shared font setup: `src/frontend/src/styles.css`, `src/frontend/src/routes/__root.tsx`
- Current mobile PWA font precache: `src/frontend/public/sw.js`, `src/frontend/src/lib/pwa/__tests__/service-worker.test.ts`
- Existing mobile UI refresh: `specs/153-mobile-pwa-workspace/changes/mobile-ui-refresh/`
- Feature workflow and required approval gates: `specs/FEATURE_DEVELOPMENT_WORKFLOW.md`
