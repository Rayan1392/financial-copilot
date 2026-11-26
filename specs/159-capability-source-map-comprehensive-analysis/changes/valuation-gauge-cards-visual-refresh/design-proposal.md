# Design proposal — valuation gauge cards: number format and visual refresh

Status: Gate A: Approved by the owner on 2026-11-26 (OQ-133, OQ-134 and OQ-135 answered as proposed; OQ-136 stays open and out of scope)
Change of: `specs/159-capability-source-map-comprehensive-analysis/changes/standalone-valuation-gauge-cards/` (released v1.29.2, enabled in production 2026-11-26)
Layers / Area: frontend, backend (presentation text only) · AI
Requested by the owner on 2026-11-26 after the first production card («گیج P/E شاراک»), compared with the TahlilApp gauge for the same ticker.

## 1. Owner requirements

1. Show every P/E and P/S number with exactly two decimals.
2. Show the share price and the equilibrium range bounds as whole rials (no decimals).
3. The Sapio card must be clearly more beautiful and more creative than the TahlilApp gauge, not a weaker copy.
4. Review the iPhone presentation as a mobile UI/UX specialist.

## 2. Findings in the production card (1.29.2)

| # | Problem | Evidence | Cause |
|---|---|---|---|
| F1 | Ratios show 13–16 decimals («۵٫۸۸۰۸۶۶۱۱۷۴۵۸۲۶۳»), so the card cannot be read. | Owner screenshot | AC-03 «preserve the exact observed ratio»; `formatExactDecimal` prints the provider string. |
| F2 | The separator «·» between min, average and max looks like the Persian zero «۰», so «۴ · ۶ · ۸» reads «۸۰۶۰۴». In RTL the values also come out in reverse order. | `evidence/browser/all-390-light.png` | Three numbers joined in one inline string. |
| F3 | Dial labels collide: the long min/max labels cover the band percentages. | Owner screenshot | Long numbers (F1) at radius 140. |
| F4 | The six-row «بازهٔ ۱…۶» table repeats the dial and does not say what each band covers. | Owner screenshot | Initial text-equivalent design. |
| F5 | Title repeated: message heading «گیج P/E شاراک · پتروشیمی شازند» and card heading «گیج P/E · شاراک». The «کامل» chip means nothing to a user. | Owner screenshot | Heading rule plus the state chip shown in every state. |
| F6 | No answer to the user's real question, "is it expensive now?": no distance to the average and no historical context. TahlilApp at least shows «فاصله تا میانگین ‎+۱۹٫۰٪». | Screenshot 2 | Facts-only card. |
| F7 | Axis ends (۲٫۱۱ and ۵۹٫۶۱) sit beside the real bounds with the same weight, so the dial looks linear when it is not. | Owner screenshot | All markers drawn the same way. |
| F8 | Mobile: a three-card answer is about 1,600 px tall at 390 px, and each card ends in a 6-row table. | `evidence/browser/all-390-light.png` | Stacked cards, no grouping control. |

## 3. Proposed design

Prototype (not production code): `mockup/prototype.html`. The screenshots `mockup/single-desktop-*.png`, `mockup/trio-desktop-*.png`, `mockup/single-phone-*.png` and `mockup/trio-phone-*.png` cover light and dark themes, desktop at 1100 px and iPhone at 390 pt. The P/E values are the real شاراک production values; the P/S and equilibrium values are illustrative.

### 3.1 Number format (presentation only)

| Value | Rule | Example |
|---|---|---|
| P/E and P/S: current, min, average, max, axis ends, band edges | Exactly 2 decimals, rounded half away from zero. | 6.999044944 → «۷٫۰۰»; 5.880866117458263 → «۵٫۸۸» |
| Share price, equilibrium lower, midpoint and upper bounds | Whole rials, rounded half away from zero, «٬» thousands separator. | 4120.4 → «۴٬۱۲۰ ریال» |
| Day shares | Unchanged: 1 decimal, largest-remainder rounding that totals 100% (`PercentageDecimals`). | «۲۰٫۱٪» |
| Derived percentages (3.3) | 1 decimal; a sign for a distance. | «‎+۱۹٫۰٪» |

- The persisted payload keeps the exact provider strings, so the history contract is unchanged.
- Rounding happens where the value is shown: in the web card and in the text answer (`ValuationGaugeTextComposer`, used for Telegram and the text fallback).
- Cards already stored in history therefore also appear rounded.
- Position labels and needle angles still use the exact values, so 6.996 rounds to «۷٫۰۰» without moving the needle.
- This replaces the "preserve the exact observed ratio" clause of AC-03 for display only.

### 3.2 Ratio card (P/E, P/S)

**Dial.** The approved geometry is unchanged: six equal 30° sectors, `min` at 30°, `avg` at 90°, `max` at 150°.
- **Hero value.** The current value sits large in the centre of the arc, with «P/E امروز» under it. The position pill («بین میانگین و حد بالا») sits below the dial and takes its colour from the zone (warm above the average, cool below it).
- **Active band.** The band that holds today's value is raised 4 px and outlined, so the eye goes straight to "where we are". All six bands keep their day-share percentage.
- **Pointer.** An outer notch on a fine instrument scale plus a short inner blade with a ring. Neither covers the band labels, which fixes F3. The pointer sweeps to its angle on first render, in 700 ms, and does not move under `prefers-reduced-motion`.
- **Labels.** Only the three real bounds get a name and value: «حد پایین P/E»، «میانگین P/E»، «حد بالا P/E». Each has a short stem with a dot outside the scale and carries its own colour (owner, 2026-11-26): green for the lower bound, the foreground colour (black in light, white in dark) for the average, and red for the upper bound. The same colours mark the three values under the history strip. The axis ends stay small and muted at the dial's feet (F7).
- **Palette.** A diverging green-to-red palette with two light middle bands. It is defined as theme tokens for light and dark, not with Tailwind `dark:`. Text inside the bands stays at a contrast of at least 4.5:1.

**Insight row.** Three tiles replace the current definition list:
- «میانگین تاریخی» ۵٫۸۸
- «فاصله تا میانگین» ‎+۱۹٫۰٪, warm or cool by sign, with «گران‌تر/ارزان‌تر از میانگین»
- «روزهای ارزان‌تر» ۵۷٫۴٪, with «زیر بازهٔ امروز»

**«تاریخچه در یک نگاه».**
- **Strip.** A horizontal strip whose segment widths are proportional to the day shares, unlike the dial, which shows value bands at equal angles. A marker sits over today's band.
- **Labels.** The real min, average and max are labelled at their cumulative positions.
- **Sentence.** One plain line: «در ۵۷٫۴٪ روزها P/E پایین‌تر از بازهٔ امروز بوده و ۲۰٫۱٪ روزها در همین بازه.»
- **Creative difference.** This is the main creative difference from TahlilApp. The dial answers "where is the value?", and the strip answers "how often has it been like this?".

**«جزئیات عددی بازه‌ها».** A native `<details>`, closed by default. It replaces the always-visible table (F4) and lists each band's value range (for example «۵٫۸۸ تا ۷٫۷۹»), its day count and its share, plus the axis ends.

**Header and footer.**
- **Header.** «گیج P/E» with «شاراک · پتروشیمی شازند» as a muted subtitle, and the message heading removed for a single card (F5).
- **State chip.** Shown only when the card is not complete: ناقص، قدیمی، موقتاً در دسترس نیست.
- **Footer.** «تحلیل‌اپ · دریافت ۵ آذر ۱۴۰۵»: the provider is named تحلیل‌اپ (not CyclicalWaves), and only the fetch date is shown, without the hour (owner, 2026-11-26). The equilibrium card's price line keeps its own source and time (AC-04), because an intraday quote and an end-of-day quote differ.
- **Help.** No standalone «؟» button: in the first prototype it was meant to open a short "how to read this gauge" note, but the owner could not tell what it was for. That note now sits in the disclosure «راهنمای خواندن گیج و جزئیات عددی». A one-line disclaimer like TahlilApp's: «P/E یک نسبت بنیادی است، نه توصیهٔ خرید یا فروش.»

### 3.3 Derived figures (new; same-source only)

| Figure | Formula | Shown when |
|---|---|---|
| Distance to the average | `(current − avg) / avg` | current and avg present, avg > 0 |
| Days below today's band | sum of shares of the bands fully below the needle's band | current present and day total > 0 |
| Days in today's band | share of the needle's band | same |
| Price vs midpoint and vs lower bound | `(price − mid) / |mid|`, `(price − lower) / |lower|` | quote present, denominator ≠ 0 |

- These figures are computed in the backend projection from exact values, as optional new members of the stored card (version stays 1; older cards simply omit them).
- The wording claims only what the band data shows: "below today's band", never "below today's value", because days inside a band are not ordered.

### 3.4 Equilibrium card

- **Price band.** A linear price band replaces the semicircle: a gradient track from the lower to the upper bound, a midpoint tick, and a pin with a bubble «۴٬۱۲۰ ریال» at today's price.
- **Tiles.** «جایگاه»، «تا میانهٔ بازه ‎−۶٫۴٪»، «بالای حد پایین ‎+۸٫۴٪».
- **Out of range.** Outside the range the pin rests at the end, with a dashed stem and the label «زیر/بالاتر از بازهٔ تعادلی».
- **No quote.** Without a quote, the band is shown without a pin, with the existing «فقط بازه» note.
- **Sources.** The price source and time and the range source and time stay separate (AC-04).
- **Why linear.** A price inside a range reads best on a straight scale, and it visibly sets the rial-based card apart from the two ratio dials.

### 3.5 Three-card answer («گیج‌های ارزش‌گذاری ‹نماد›»)

- **Desktop** (message width ≥ 900 px): P/E and P/S side by side, with the equilibrium band full width below. One heading «گیج‌های ارزش‌گذاری شاراک · پتروشیمی شازند»; cards drop the repeated company subtitle.
- **Narrow widths and iPhone:** see 3.6.

### 3.6 iPhone review

| Topic | Proposal |
|---|---|
| Three-card answer | An iOS segmented control «P/E | P/S | قیمت تعادلی» under the heading shows one card at a time (40 pt control, thumb animated 200 ms). The answer drops from about 1,600 px to about 900 px. Swipe paging was rejected because it conflicts with the chat's vertical scroll and the iOS back-swipe. |
| Dial size | Full content width (up to 380 pt). Hero value 34–44 pt. Labels at least 11 pt, values 15 pt. |
| Touch | Each band and tile has a hit area of at least 44 pt. Tapping a band shows its value range and share in a small popover, so hover is never required. |
| Insight tiles | 3 columns at 390 pt with short sublabels; 2 + 1 below 340 pt. |
| Details and table | Collapsed by default; native `<details>` keeps VoiceOver semantics. |
| Accessibility | The dial is one image with a full spoken summary. The segmented control uses tab roles. Contrast is at least 4.5:1 in both themes. Dynamic Type up to 200% keeps the layout without horizontal scroll. Motion is reduced under the system setting. |

## 4. What stays unchanged

- Routing, billing (one credit), the flag, the source map, snapshot reads, the stale and timeout rules, and the API and history contract shape (additions only).
- The angle geometry of the ratio dials and the no-needle rule when there is no current value.
- No forward needle in this change: see 4.1.

### 4.1 Forward P/E (owner question, 2026-11-26)

TahlilApp draws a second, dashed needle for «P/E آینده‌نگر» (۲٫۹۶ for شاراک). Sapio does not show it because the data does not reach Sapio:

- `GET /pe-ps-gauges/ticker/{ticker}` returns, per ratio, only `close`, `start`, `end`, `min`, `avg`, `max`, the six day counts `a`–`f` and `status` (`docs/providers/cyclicalwaves-v2/gauge-equilibrium-api-contract.md`). There is no forward value.
- The screener `GET /custom-filtering/ticker/{ticker}` carries 29 metrics; its only valuation ratios are the current P/E and P/S (`ScreenerMetricCatalog`, ids 28 and 29).
- Feature 148 recorded this when the routes were added: "forward data and calculations are not supplied by these four routes" (`specs/148-…/impact-analysis.md`).
- Sapio holds no forecast EPS or sales from which it could compute one. The approved rule is never to invent a forward needle.

The design is ready for it. If تحلیل‌اپ adds the forward ratio to the gauge response, the dial gets a dashed needle with a ring plus a «P/E آینده‌نگر» chip next to «P/E امروز». That needs a provider-contract update, ingestion and storage of one more value (a migration), and a projection field. It is a separate change and is recorded as OQ-136.

## 5. Alternatives considered

- **Copy the TahlilApp layout** (dial plus four boxed tiles). Rejected by the owner's goal, and it carries no historical insight.
- **Keep the semicircle for equilibrium.** Consistent with the ratio cards, but a weaker read of a price inside a range. Kept as a fallback if the owner prefers the three-dial look.
- **A table instead of the strip.** It is accessible but adds no meaning beyond the dial. It remains available inside «جزئیات».

## 6. Risks

- The derived figures are new statements to users. A wrong formula would mislead, so unit fixtures will cover signs, zero or negative denominators, and needles pinned at either end.
- A rounding mismatch between the web card and Telegram text is possible. One formatting rule per layer will be enforced by tests on the same fixtures.
- The visual change touches only the gauge components; other cards and the PNG renderer are unaffected.

## 7. Open questions

Recorded as OQ-133 to OQ-136 in `specs/open-questions.md`.

- **OQ-133.** Approve the derived figures in 3.3, or ship the visual refresh without them.
- **OQ-134.** Linear price band for equilibrium (proposed), or keep the semicircle.
- **OQ-135.** Mobile three-card answer: segmented control (proposed), or stacked compact cards.
- **OQ-136.** Forward P/E and P/S: ask تحلیل‌اپ to add the forward ratio to the gauge response and plan a separate change, or leave it out.

## 8. Outline plan (for Gate B after Gate A)

1. **Formatting rule.** Add `formatRatio2` and `formatRial0` in `src/frontend/src/lib/chat/valuation-gauges.ts` with unit tests, and apply them everywhere the cards print values. Add the same rule to `ValuationGaugeTextComposer` with unit tests.
2. **Derived figures.** Add optional members in `ValuationGaugeProjector` and the contracts (additive, version 1). Add fixture tests covering the edge cases in section 6.
3. **Frontend redesign.** Rebuild `valuation-gauge-cards.tsx` into dial, insight tiles, history strip, details, price band and segmented control, using theme tokens. Update the mapper and component tests, and keep the old stored payloads rendering.
4. **Browser check.** Extend `scripts/valuation-gauge-cards.browser.mjs` to cover iPhone 390/430, the segmented control, label collisions, contrast and reduced motion, and save new evidence.
5. **Release.** Docs, a CHANGELOG «Changed» entry, then a release through the normal two-confirmation process.
