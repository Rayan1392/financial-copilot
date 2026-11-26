# Design Proposal: Light-theme contrast of the emerald starter text (BUG-049)

Status: Gate A: Approved by the owner on 2026-11-26 («BUG-049 را رفع کن», approving the fix proposed in `specs/bugs.md`)

## Feature Folder

`159-capability-source-map-comprehensive-analysis/changes/fix-starter-emerald-text-contrast`

## Layers / Area

frontend · cross-cutting

## Defect

BUG-049. The browser check of change `valuation-gauge-starter-card` found small emerald text below 4.5:1 in the light
theme (WCAG 1.4.3):

- the coverage badge «پوشش فعلی: …» on the desktop new-chat page `/chat`: `text-emerald` on `bg-emerald-soft/60`,
  3.97:1;
- the sales topics of the phone starter rows on `/m` («ترکیب فروش», «فروش ماهانه», …): `text-emerald` on the row
  surface, 4.07:1.

Both classes were the same on clean `HEAD` before that change.

## Correction

No material design change. The text keeps its emerald colour. In the light theme it uses the darker emerald ink that the
desktop starter card topics already use, `oklch(0.45 0.12 160)`:

- `src/frontend/src/styles.css`: one utility class `.emerald-ink-text` (`var(--emerald)`, and the darker ink under
  `.light`), next to the existing `.mobile-gold-text`.
- `src/frontend/src/routes/_app.chat.tsx`: the badge uses `emerald-ink-text` instead of `text-emerald`.
- `src/frontend/src/components/mobile/mobile-starter-list.tsx`: the sales tone topic uses `emerald-ink-text`.

The dark theme is unchanged (7.90:1 and 8.33:1 before the fix).

## Verification

- `scripts/starter-cards.browser.mjs` no longer measures these elements apart: every text element in the fixture must
  reach 4.5:1 in both themes, at 1280, 768, 430, 375 and 320 px. Evidence goes to this folder (`--evidence`).
- A Vitest regression test checks the phone sales topic class.

## Risks

- Other emerald small text elsewhere in the app is out of scope. Only the two elements in BUG-049 change.
