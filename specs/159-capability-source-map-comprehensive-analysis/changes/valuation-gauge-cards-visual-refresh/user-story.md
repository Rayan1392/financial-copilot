# User Story: Valuation gauge cards — number format and visual refresh

## Feature Folder

`159-capability-source-map-comprehensive-analysis/changes/valuation-gauge-cards-visual-refresh`

## Layers / Area

Layers: frontend | backend
Area: AI

## Priority

P1

## Complexity

M

## Depends On

`159-capability-source-map-comprehensive-analysis/changes/standalone-valuation-gauge-cards` (released in v1.29.2 and enabled in production on 2026-11-26; its open staging items do not block this presentation change)

## References

- `specs/159-capability-source-map-comprehensive-analysis/changes/valuation-gauge-cards-visual-refresh/design-proposal.md` (approved at Gate A on 2026-11-26), including `mockup/prototype.html` and its screenshots
- `specs/159-capability-source-map-comprehensive-analysis/changes/standalone-valuation-gauge-cards/user-story.md` (AC-03, AC-04 and AC-10 are amended by this change for display only)
- `docs/providers/cyclicalwaves-v2/README.md`, section «Standalone valuation gauge cards»
- Code:
  - Backend: `src/backend/FinancialCopilot.Application/FinancialData/ValuationGauges/` (`ValuationGaugeContracts.cs`, `ValuationGaugeProjector.cs`) and `src/backend/FinancialCopilot.Application/AI/Orchestration/ValuationGauges/ValuationGaugeTextComposer.cs`
  - Frontend: `src/frontend/src/lib/chat/valuation-gauges.ts` and `src/frontend/src/components/app/valuation-gauge-cards.tsx`
- Open questions: OQ-133, OQ-134 and OQ-135 (answered 2026-11-26); OQ-136 (open, out of scope)

## Standards Source Of Truth

Obey `.claude/rules/*`. Do not copy rule text into this spec.

## User Story

As a Sapio user asking for a P/E, P/S or equilibrium gauge,
I want short, correctly rounded numbers and a gauge that tells me at a glance where today's value sits and how often the stock has been there,
so that I can judge the valuation faster and more clearly than on the source site.

## Business Context

The first production card (2026-11-26, «گیج P/E شاراک») showed 13–16 decimals, an ambiguous separator and a duplicate table. The owner compared it with the TahlilApp gauge and asked for a design that is clearly better, not a weaker copy. The change applies to the web chat (desktop and iPhone) and to the text answer used by Telegram.

## Scope

- Display rule:
  - P/E and P/S with exactly two decimals.
  - Share price and equilibrium bounds as whole rials.
  - Applies to live answers, stored history and the text answer.
- Derived figures (OQ-133), computed in the backend and added to the stored card as optional fields:
  - distance to the average;
  - share of days below today's band and inside it;
  - price vs the equilibrium midpoint and vs the lower bound.
- Value range of each of the six bands, added to the stored sectors as optional fields.
- New ratio card per the approved design:
  - hero value in the dial;
  - raised active band;
  - notch and blade pointer with a reduced-motion-aware sweep;
  - coloured bound markers (lower green, average foreground, upper red);
  - three insight tiles;
  - «تاریخچه در یک نگاه» strip;
  - «راهنمای خواندن گیج و جزئیات عددی» disclosure;
  - state chip only when the card is incomplete;
  - footer «تحلیل‌اپ · دریافت ‹date›» without the hour.
- Equilibrium card as a linear price band with a price pin (OQ-134).
- Three-card answer:
  - P/E and P/S side by side with the band below on wide messages;
  - an iOS segmented control on narrow widths (OQ-135).

## Out Of Scope

- Forward P/E and P/S needle (OQ-136: the provider does not supply it).
- Routing, billing, the feature flag, the source map, snapshot reads, the stale and timeout rules.
- The P/S visualization PNG and other cards.
- Any table, migration or tariff change.

## Functional Requirements

1. **Ratio formatting.** Format every P/E and P/S number for display from its exact string, with exactly two decimals, rounded half away from zero. This covers current, min, average, max, axis ends and band edges. The position label and the needle angle keep using the exact value.
2. **Rial formatting.** Format the share price and the equilibrium lower, midpoint and upper bounds as whole rials, rounded half away from zero, with «٬» thousands separators and a correctly placed minus sign for negative bounds.
3. **Day shares.** Keep the day-share percentages at `PercentageDecimals` (default 1), with largest-remainder totals of 100%.
4. **Derived ratio figures.** The projector adds an optional `Insights` object to ratio cards:
   - **`DistanceToAveragePercent`:** `(current − avg) / avg × 100`, 1 decimal. Only when current is present and avg > 0.
   - **`CurrentBand`:** the 1-based band order of the needle, `min(6, floor(angle / 30) + 1)`. Only for an `Inside` needle.
   - **`DaysBelowBandPercent`:** the sum of the displayed shares of the bands before `CurrentBand`.
   - **`DaysInBandPercent`:** the displayed share of `CurrentBand`.
   - Both day figures are present only with an `Inside` needle and a nonzero day total. They are absent for a pinned needle or no current value.
5. **Derived equilibrium figures.** The projector adds `PriceVsMidpointPercent` and `PriceVsLowerPercent`, each `(price − x) / |x| × 100` with 1 decimal. They are present only with a quote and x ≠ 0.
6. **Band ranges.** Each ratio sector carries optional exact `From` and `To` values: start, min, `(min+avg)/2`, avg, `(avg+max)/2`, max, end. This is the geometry already used for the angles.
7. **Ratio card.** The web ratio card shows the approved layout:
   - hero value and «P/E امروز» / «P/S امروز» in the centre of the arc;
   - the position pill under the dial, warm above the average and cool below it;
   - the active band raised;
   - bound markers and labels «حد پایین P/E»، «میانگین P/E»، «حد بالا P/E» in green, foreground and red;
   - small muted axis ends at the dial's feet;
   - tiles «میانگین تاریخی», «فاصله تا میانگین» and «روزهای ارزان‌تر»;
   - the strip with segment widths proportional to the day shares, a marker over the current band, coloured min, average and max labels, and the sentence «در X٪ روزها ‹P/E› پایین‌تر از بازهٔ امروز بوده و Y٪ روزها در همین بازه.»;
   - the disclosure, closed by default, with a short reading guide and a table of band range, days and share;
   - a disclaimer line;
   - the footer «تحلیل‌اپ · دریافت ‹Persian date›».
8. **Missing values in the ratio card.** Tiles and sentences whose figure is absent are not shown. A card without a current value shows «—» in the centre, no pointer and no position-dependent figures. A pinned needle shows its pinned text.
9. **Equilibrium card.** The equilibrium card shows:
   - a gradient track from the lower to the upper bound, with a midpoint tick and labels;
   - a price pin with the bubble «‹price› ریال»;
   - tiles «جایگاه», «تا میانهٔ بازه» and «بالای حد پایین»;
   - the price line with its own source, date and time;
   - the range line «تحلیل‌اپ · دریافت ‹date›».
   Out of range, the pin rests at the matching end with a dashed stem and the out-of-range label. Without a quote, the band shows no pin and keeps the range-only note.
10. **Three-card answer.** On a message ≥ 900 px wide, P/E and P/S appear side by side with the equilibrium band full width below. Below that width, a segmented control «P/E | P/S | قیمت تعادلی» with tab semantics shows one card at a time; P/E is selected first. Each card drops the company subtitle under the single answer heading.
11. **Stored cards.** Stored cards without `Insights` or sector ranges, created before this change, render with the new layout: they show the rounded numbers and simply omit the derived tiles, the sentence and the band ranges.
12. **Text answer.** The text answer (`ValuationGaugeTextComposer`, used by Telegram and the text fallback) uses the same rounding, names the source «تحلیل‌اپ» with the date only, and adds the distance to the average and the days sentence when present.

## Data / Schema

No table, column or migration. The persisted assistant payload `valuationGaugeCards` (version 1) gains only optional members:

- `ValuationGaugeCard.Insights`
- `ValuationGaugeSector.From` / `To`

Exact decimal strings stay on the wire.

## API (if any)

No new route. The existing chat response and history field gain the optional members above.

## AI Query Path (if any)

Unchanged: V2 `valuation_gauges` route, deterministic, no model call.

## UI (if any)

- **Files:** `src/frontend/src/components/app/valuation-gauge-cards.tsx` (rewritten) and `src/frontend/src/lib/chat/valuation-gauges.ts` (formatting, types). The theme tokens for the six bands and the three bound colours are defined for both themes in the gauge component, not with Tailwind `dark:`.
- **RTL:** the dial and strip keep the low-to-high direction from left to right.
- **iPhone 390–430 pt:**
  - touch targets of at least 44 pt;
  - band tap shows its range and share;
  - native `<details>`;
  - spoken summary on the dial;
  - layout holds up to 200% text size without horizontal scroll.

## Non-Functional Requirements

- Rendering adds no network call.
- The figures are computed once in the projector, the single owner, so web and Telegram cannot disagree.
- No `dark:` classes.
- Contrast is at least 4.5:1 for text in both themes.
- The sweep animation (≤ 700 ms) runs only under `prefers-reduced-motion: no-preference`.

## Acceptance Criteria

- [ ] AC-01 The شاراک fixture (current 6.999044944, min 3.3487316736139543, avg 5.880866117458263, max 9.693612328874726, start 2.1059510085, end 59.613521843) shows «۷٫۰۰»، «۳٫۳۵»، «۵٫۸۸»، «۹٫۶۹»، «۲٫۱۱»، «۵۹٫۶۱». No P/E or P/S number anywhere on the card or in the text answer has more or fewer than two decimals.
- [ ] AC-02 Prices and equilibrium bounds show as whole rials («۴٬۱۲۰ ریال»). 4120.5 rounds to «۴٬۱۲۱» and −12.5 to «‎−۱۳».
- [ ] AC-03 For the شاراک fixture with day counts 414/1483/1412/1161/634/662, the projector returns:
  - `DistanceToAveragePercent` = 19.0
  - `CurrentBand` = 4
  - `DaysBelowBandPercent` = 57.4
  - `DaysInBandPercent` = 20.1

  The card shows «‎+۱۹٫۰٪», «۵۷٫۴٪» and the sentence with «۵۷٫۴٪» and «۲۰٫۱٪».
- [ ] AC-04 A pinned needle, a missing current value, avg ≤ 0 and a zero day total each omit exactly the figures FR-4 excludes, and the card renders without them.
- [ ] AC-05 The equilibrium fixture (3800/4400/5000, price 4120) returns `PriceVsMidpointPercent` = −6.4 and `PriceVsLowerPercent` = 8.4. The card shows the pin between the lower bound and the midpoint. Out-of-range and no-quote fixtures show the pinned end and the range-only band.
- [ ] AC-06 Bound markers and their labels use the low-bound, foreground and high-bound colours in light and dark themes. The strip's three labels use the same colours.
- [ ] AC-07 The footer shows «تحلیل‌اپ · دریافت ‹date›» without an hour on ratio cards and on the equilibrium range line. The equilibrium price line keeps its source, date and time.
- [ ] AC-08 The «کامل» chip is gone for complete cards. The chip appears for Partial, Stale and TemporarilyUnavailable.
- [ ] AC-09 Three-card answer: side by side at ≥ 900 px. At 390 px a segmented control with tab roles shows one card at a time, and switching shows the other cards.
- [ ] AC-10 A stored payload from v1.29.2, without the new fields, renders with rounded values and without the derived tiles, the sentence or the band ranges, and throws no error.
- [ ] AC-11 The browser check passes at 320, 360, 390, 430, 768 and 1280 px (also beside the sidebar) and at 200% zoom, in light and dark, with:
  - no horizontal scroll;
  - no dial label colliding with another label or with a band percentage;
  - contrast of at least 4.5:1;
  - the sweep disabled under reduced motion.

  Screenshots are saved as evidence.
- [ ] AC-12 The Telegram text for the شاراک fixture contains «۷٫۰۰», «‎+۱۹٫۰٪», «تحلیل‌اپ» and no number with more than two decimals.

## Assumptions

- Rounding is half away from zero (Persian financial convention). Exact values remain in the payload, so the rule can change later without data loss.
- "Below today's band" is the honest claim. The day counts do not order the days inside a band, so the sentence never says "below today's value".
- The source label «تحلیل‌اپ» names the provider product for users. The payload keeps the internal source code `CyclicalWavesV2`.

## Deviations From The Approved Plan

Recorded during implementation on 2026-11-26.

- **FR-10 / AC-09 width threshold: 720 px instead of 900 px.**
  - The threshold is measured on the answer's own width (a container query), not on the window.
  - The chat column is `max-w-4xl` with 16 px padding, so an answer is at most about 864 px wide. A 900 px threshold would never show the cards side by side.
  - At 720 px each ratio card is about 355 px wide, the width the dial was designed for.
  - Behaviour is otherwise unchanged: side by side above the threshold, the segmented control below it.
- **Formatter name.** The date-only formatter is `formatTehranDate`, next to the existing `formatTehranDateTime`, instead of `formatPersianDate`.
- **One insights record.** Task 1 named two records (`ValuationRatioInsights`, `ValuationEquilibriumInsights`). The card has one optional `insights` member, so one `ValuationGaugeInsights` record carries all six optional figures:
  - ratio cards fill the four ratio members;
  - equilibrium cards fill the two price members.

  The wire names are as planned.
