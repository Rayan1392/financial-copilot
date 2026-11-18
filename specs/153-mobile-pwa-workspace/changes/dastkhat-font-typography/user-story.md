# User Story: Dastkhat Typography for Web and Mobile

## Feature Folder

`153-mobile-pwa-workspace/changes/dastkhat-font-typography`

## Layers / Area

Layers: frontend
Area: cross-cutting

## Priority

P2

## Complexity

M

## Depends On

153-mobile-pwa-workspace

## References

- `specs/153-mobile-pwa-workspace/changes/dastkhat-font-typography/design-proposal.md` (Gate A approved 2026-11-17)
- `specs/153-mobile-pwa-workspace/changes/mobile-ui-refresh/user-story.md` and `tasks.md`
- `docs/deployment/pwa-service-worker.md`
- Dastkhat Part 1: https://github.com/dastyar-io/dastkhat-font/blob/main/AGENTS.md#part-1-using-dastkhat-in-a-project
- Dastkhat webfont declarations: https://github.com/dastyar-io/dastkhat-font/blob/main/Webfonts/dastkhat.css

## Standards Source Of Truth

Obey `.claude/rules/*`. Do not copy rule text into this spec.

## User Story

As a SapioAI user reading Persian financial information on desktop or phone,
I want consistent, readable Persian typography and Persian financial digits,
so that the web app and mobile PWA are easier to read and remain usable offline.

## Business Context

The desktop web app and `/m` mobile PWA share one React frontend and currently use self-hosted Vazirmatn. Dastkhat provides a Persian and Latin UI face and a FaNum build for Persian digit display. The PWA must continue to render its typography when offline.

## Scope

- Replace the current shared Persian UI face with self-hosted Dastkhat Main for desktop web and `/m`.
- Use Dastkhat FaNum for user-facing financial values whose display should use Persian digits. Keep underlying values, calculations, identifiers, code, and URLs unchanged.
- Keep JetBrains Mono for monospaced and code-like content.
- Keep font delivery same-origin, preload the main Dastkhat face, use `font-display: swap`, and cache the required Dastkhat font files in the PWA service worker.
- Apply Part 1’s Persian typography guidance to shared web and mobile styles and user-facing Persian copy: RTL and Persian language metadata, line height suitable for Persian, half-spaces and punctuation, isolation of Latin snippets in RTL content, logical properties for directional rules, and no letter spacing or text transformation on Persian text.
- Preserve current page structure, colors, themes, responsive behavior, mobile type scale, financial formatting data, and app routing except for typography adjustments needed to prevent regressions.
- Keep the Dastkhat OFL and copyright notices with redistributed font assets.

## Out Of Scope

- Backend, API, database, financial calculations, or stored-number changes.
- A native iOS/Android client; mobile means the existing responsive `/m` PWA.
- A broad visual redesign, changes to the mobile type scale, or copy edits unrelated to the typography rules.
- PWA rollout, routing, or caching-policy changes beyond replacing the font assets and required cache version update.

## Functional Requirements

1. The shared app uses Dastkhat Main as its primary Persian and Latin UI face, with a system fallback stack.
2. Dastkhat FaNum is scoped to selected user-facing financial values; nonfinancial identifiers and content intended to remain Latin are excluded.
3. Font assets are served from the app origin. The main face is preloaded and uses `font-display: swap`.
4. The service worker precaches the font files needed by desktop and `/m`; it does not keep obsolete font entries or add external font origins.
5. The existing document root remains `lang="fa"` and `dir="rtl"`.
6. Persian body copy uses a readable line height starting at 1.8; headings may use a tighter line height. Existing mobile size tokens remain authoritative.
7. Mixed Persian and Latin text is isolated where direction can become ambiguous. Directional CSS touched by this change uses logical properties.
8. Persian text does not receive letter spacing or `text-transform`. Visible Persian copy reviewed as part of the changed shared and mobile surfaces uses half-spaces and Persian punctuation where appropriate.
9. Dastkhat copyright attribution and SIL OFL 1.1 are retained with the redistributed font files.

## Data / Schema

None. Font choice affects rendered glyphs only; financial values and data contracts are unchanged.

## API (if any)

None.

## AI Query Path (if any)

None.

## UI (if any)

Shared desktop web pages and `/m` routes in `src/frontend`. Keep current RTL direction and responsive layouts. Apply FaNum only to selected financial display elements; preserve LTR isolation for Latin identifiers and code.

## Non-Functional Requirements

- No external font requests.
- The installed PWA renders the selected Dastkhat face offline after the service worker has precached its assets.
- Font assets remain WOFF2 variable builds for the selected families; no unused Dastkhat families are added.
- Check layout at representative desktop widths and phone widths, including the existing 320px and 360px mobile checks, in both light and dark themes.
- Record selected asset sizes and compare their total with the current font precache footprint.

## Acceptance Criteria

- [x] Desktop web and `/m` use Dastkhat Main for Persian and Latin UI text, with no request to an external font host.
- [x] Selected financial price, percentage, and dashboard figures display Persian digits through FaNum; values, computations, IDs, and URLs remain unchanged.
- [x] The main font is preloaded, both selected font builds use `font-display: swap`, and fallback stacks are declared.
- [x] The PWA precache includes the required Dastkhat files and no obsolete Vazirmatn file; browser checks confirmed both fonts in the static cache and successful offline reload.
- [x] Shared document metadata continues to declare Persian language and RTL direction.
- [x] Persian line-height and punctuation/half-space usage were reviewed in the changed shared/mobile surfaces; browser evidence confirms Persian text renders with the selected font.
- [x] No Persian text in the reviewed frontend surfaces uses letter spacing or relies on text transformation.
- [ ] No horizontal overflow, clipping, or unusable wrapping is introduced at the supported desktop and mobile checks in either theme. Mobile checks passed at 320px, 360px, and 390px in both themes; the desktop answer fixture stopped at its existing missing-legend assertion.
- [x] Dastkhat copyright notices and SIL OFL 1.1 accompany redistributed font files.

## Assumptions

- FaNum is appropriate for user-facing financial values already intended to appear with Persian digits; it does not change financial data or formatting logic.
- The `/m` PWA is the project’s mobile version for this change; no native mobile app is in scope.
- The upstream font source will be pinned to a reviewed upstream revision when assets are added so future builds are reproducible.
