# Feature 154: visual sign-off package for AC-P03, AC-P04 and AC-P11 (E-7)

**Sign-off status: PENDING.** Owner: — · Date: — · Decision: — (accept / accept with changes / reject)

**What this needs:** a human review of the rendered report against the supplied Codal two-sided reference.

**What it does not need:** checking values or layout mechanics. The automated browser checks already cover those (`browser/browser-results.json`, 25 checks, all `ok`).

The images were **regenerated on 2026-11-05** (`npm run test:balance-sheet-browser`) from the current tree, after the review-round-3 fix F154-V-06. That fix gives every dated balance its own audit and restatement badge on the PNG and on mobile. They were regenerated again after F154-V-08: the PNG export now waits (bounded) for the Vazirmatn faces. All six PNGs were inspected and render in Vazirmatn, and the desktop and mobile comparison PNGs are byte-identical again.

All images use the وبملت **fixture** (`src/frontend/src/components/app/__tests__/fixtures/balance-sheet-154.json`). They do not use live Development data. Fixture values such as cash 409,287,621 are the supplied spec values, which is why they differ from Development (see `live-development-verification.md` §7).

## 1. Images to review

Paths are relative to this folder.

| View | Light | Dark |
|---|---|---|
| **Reference** (supplied, Codal two-sided comparison) | [`reference-codal-two-sided-comparison.png`](reference-codal-two-sided-comparison.png) | — |
| Desktop two-sided default, 1280 px | [`browser/desktop-1280-default.png`](browser/desktop-1280-default.png) | [`browser/desktop-1280-default-dark.png`](browser/desktop-1280-default-dark.png) |
| Desktop two-sided comparison, 1440 px (AC-P03, AC-P04) | [`browser/desktop-1440-comparison.png`](browser/desktop-1440-comparison.png) | [`browser/desktop-1440-comparison-dark.png`](browser/desktop-1440-comparison-dark.png) |
| Comparison with «گزارش قبلی» selected, 1440 px | [`browser/desktop-1440-comparison-previous-report.png`](browser/desktop-1440-comparison-previous-report.png) | — |
| Stacked comparison, 900 px (AC-P04, 768–1099 px band) | [`browser/desktop-900-comparison-stacked.png`](browser/desktop-900-comparison-stacked.png) | — |
| Mobile comparison, 320 px | [`browser/mobile-320-comparison.png`](browser/mobile-320-comparison.png) | — |
| Mobile comparison, 360 / 390 px (supporting) | [`browser/mobile-360-comparison.png`](browser/mobile-360-comparison.png), [`browser/mobile-390-comparison.png`](browser/mobile-390-comparison.png) | — |
| **PNG default** (single date, وبملت, 2400×2888) | [`browser/png-desktop-default.png`](browser/png-desktop-default.png) | — |
| **PNG comparison** (وبملت, 3200×3040; AC-P11) | [`browser/png-desktop-comparison.png`](browser/png-desktop-comparison.png) | — |
| PNG comparison exported from mobile (byte-identical to desktop) | [`browser/png-mobile-comparison.png`](browser/png-mobile-comparison.png) | — |
| **PNG comparison, restated baseline** (fixture variant `restated=baseline`) | [`browser/png-desktop-comparison-restated-baseline.png`](browser/png-desktop-comparison-restated-baseline.png) | — |
| PNG single date, restated anchor (`restated=anchor`) | [`browser/png-desktop-default-restated-anchor.png`](browser/png-desktop-default-restated-anchor.png) | — |
| Mobile comparison 320 px, restated baseline | [`browser/mobile-320-comparison-restated-baseline.png`](browser/mobile-320-comparison-restated-baseline.png) | — |

## 2. What to check, per AC

### AC-P03: wide comparison at ≥ 1100 px (spec §9.2)

Use `desktop-1440-comparison*.png` next to the reference.

- [ ] Assets are on the **right**; liabilities, then equity, are on the left.
- [ ] Each side has the four RTL columns «شرح | مانده در [تاریخ گزارش] | مانده در [تاریخ مبنا] | درصد تغییر». Each date column shows its own audit badge (حسابرسی‌شده / حسابرسی‌نشده).
- [ ] The baseline selector and «مبنای مقایسه: … (date)» sit above the table, and the chart sits above them.
- [ ] Each side flows on its own. **Only the final totals** («جمع کل دارایی‌ها» / «جمع بدهی‌ها و حقوق صاحبان سهام») align on one row.
- [ ] Absolute change is reachable by keyboard and pointer through the row's detail popover, and is in the row's accessible description. The automated check confirms Enter and Space; a human confirms it is discoverable.
- [ ] Typography, Persian digits, parenthesised negatives and the header style match Feature 144 conventions. Compare with the Feature 144 quarterly report.

### AC-P04: ratio row and stacking (spec §9.2)

Use `desktop-1440-comparison*.png` and `desktop-900-comparison-stacked.png`.

- [ ] There is **one** full-width yellow row below both final totals. It shows both dated ratios and «تغییر: … واحد درصد».
- [ ] At 900 px the sections stack in the order assets → liabilities → equity. All four columns are kept, and the ratio row is last.
- [ ] Dark theme: the yellow row and the subtotal bands stay legible. The automated minimum text contrast is 4.59:1 in the comparison view.

### AC-P11: comparison PNG (spec §9.5)

Use `png-desktop-comparison.png`.

- [ ] The brand header sits at the top. The footer reads «ترازنامه (صورت وضعیت مالی) | ساپیو - دستیار هوشمند بازار». The font is Vazirmatn at 2× DPR.
- [ ] The chart is above the table. Both dates, the baseline label, the percentage changes and the full-width yellow ratio row with the pp difference are all present.
- [ ] **Badges (spec §8.3 item 1, §8.5; F154-V-06).** Each date column header carries that balance's own audit status under «مانده در [تاریخ]». In `png-desktop-comparison.png` the anchor shows «حسابرسی‌نشده» and the baseline «حسابرسی‌شده». In `png-desktop-comparison-restated-baseline.png` the baseline shows «حسابرسی‌شده · تجدید ارائه‌شده» and the anchor stays unchanged. The single-date PNG header lists the anchor's status, plus «تجدید ارائه‌شده» when it is restated (`png-desktop-default-restated-anchor.png`).
- [ ] The longest registry labels and the widest وبملت amounts are not clipped (the automated check confirms this; labels may wrap to two lines).
- [ ] The mobile export equals the desktop export (automated: byte-identical).

## 3. Notes for the reviewer

### Chart axes (F154-V-03)

- **The PNG mirrors the on-screen axes.**
  - On screen, the percent axis is on the **left** and the amount axis on the **right**.
  - In the PNG they are reversed: amounts on the left, percent on the right.
- **PNG amount ticks are not rounded.** They divide the anchor/maximum stacked total into quarters, so tick labels are non-round (for example ۱۴٬۷۷۷٬۵۷۶٬۱۷۰). On-screen ticks are rounded (۱۵٬۰۰۰٬۰۰۰٬۰۰۰ …).
- Neither affects any value. Accept both, or request a change.

### Mobile comparison badges (spec §9.4, F154-V-06)

Use `mobile-320-comparison.png` and `mobile-320-comparison-restated-baseline.png`.

- [ ] The default mobile header lists the anchor's audit status, plus «تجدید ارائه‌شده» when it is restated.
- [ ] In comparison, the column strip «مانده در [تاریخ گزارش]» carries the anchor's status underneath.
- [ ] A baseline line «مانده در [تاریخ مبنا] (مبنا)» carries the baseline's own status, for example «حسابرسی‌شده · تجدید ارائه‌شده». Each item's secondary line «مانده در [تاریخ مبنا]: …» refers to that line.
- [ ] Nothing scrolls horizontally at 320 px (the automated check confirms this).

### Mobile date labels

- At 320, 360 and 390 px the chart x-axis uses **two-line date ticks**: month/day on the first line and year on the second (for example «۰۶/۳۱» over «۱۴۰۵»). This avoids overlap of the full Jalali dates.
- The automated check confirms 5 labels with no overlap at each width.
- On desktop and in the PNG the ticks are single-line «۱۴۰۵/۰۶/۳۱».

### Intentional differences from the Codal reference

These follow the spec and should not be logged as defects. Confirm or override each one.

1. **Neutral direction colors.** The reference colors positive changes green and negative changes red, in parentheses. Sapio shows «+۲۲٫۹۱٪» or «-۹۹٫۰۵٪» with a ▲/▼ glyph in a neutral color. Per spec §8.4, color must not imply good or bad, and green and red are not used. Parentheses are kept for negative **amounts** only.
2. **«مانده در [تاریخ]» column labels.** The reference uses «پایان عملکرد واقعی منتهی به …» and «پایان سال مالی قبل …». Sapio labels each amount column «مانده در [تاریخ]» with its own badge, and states the baseline kind once above the table in «مبنای مقایسه: …» (spec §8.5).
3. **A single full-width yellow ratio row.** The reference has no ratio row; its yellow bands are subtotal rows. Sapio adds one full-width yellow row for «نسبت حقوق صاحبان سهام به کل دارایی‌ها» below both totals. Subtotal rows use a light band instead (spec §9.1, §9.2).
4. **Selected items, not every source row.** The reference lists every Codal line, including zero rows. Sapio shows the registry's selected rows («اقلام منتخب»), reported subtotals and totals (spec §6).
5. **A chart above the table**, which the reference does not have (spec §10, PD-25).

### Observed, for the reviewer to decide (not spec requirements either way)

- The negative treasury-shares cells «(۱۸۱٬۳۵۴٬۵۳۸)» get a light pink background, the same negative-cell treatment Feature 144 uses. Check it reads as "negative amount" and not as "bad".
- The PNG comparison omits the ▲/▼ glyphs that the screen shows; the percentage sign is present. Per-column audit and restatement badges are now present on the PNG (F154-V-06).
- «سود (زیان) انباشته» shows «—» in the fixture because the fixture does not supply item 71. With Development data it is populated (2,021,921,399 for 409865).

## 4. Sign-off record

| Field | Value |
|---|---|
| Reviewer (owner) | — |
| Date | — |
| AC-P03 | PENDING |
| AC-P04 | PENDING |
| AC-P11 | PENDING |
| Requested changes | — |

When this is signed, record the outcome in `ImplementationReport.md` (§4 and §7) and in `specs/implementation-checklist.md`. That closes E-7.
