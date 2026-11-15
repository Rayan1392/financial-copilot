# Feature 161 browser evidence

Run: `node scripts/cash-flow-report.browser.mjs` from `src/frontend` (2026-11-14).

The report rendered with RTL direction and no document-level horizontal overflow at 320, 360, 390, 768, 1280, and 1440 px. The 320/360/390 px cases use compact density; wider cases use the default table layout. The 768 px screenshot uses dark color-scheme emulation. Desktop and mobile image exports completed through the report's download action.

Evidence files:

- `cash-flow-desktop-1440.png`
- `cash-flow-desktop-1280.png`
- `cash-flow-tablet-dark.png`
- `cash-flow-mobile-390.png`
- `cash-flow-desktop-1280.png-export.png`
- `cash-flow-mobile-390.png-export.png`

The browser fixture intentionally has five independent-quarter headers but no historical quarterly values. This evidence signs off layout, RTL behavior, responsive overflow, theme rendering, and PNG export only; it does not verify live financial data or the financial values for datasets A and B. The fixture itself displays this limitation.

## Accounting and analytics enhancement preview (2026-11-15)

The updated browser fixture was inspected after the report contract and presentation changes. RTL rendering, no horizontal overflow, the standalone comparison column, separate validation summary, compact mobile disclosure, and the downloadable PNG were checked. The PNG was inspected after content-based cropping to avoid excess blank space.

The browser script passed at 320, 360, 390, 768, 1024, 1280, and 1440 CSS pixels. The 768 px dark-theme screenshot and desktop/mobile screenshots and PNG exports are recorded as `cash-flow-ui-redesign-*` in this directory. The fixture is synthetic and validates presentation only; it is not live company accounting evidence.
