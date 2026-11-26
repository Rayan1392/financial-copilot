# Tasks: Valuation gauge cards — number format and visual refresh

Status: Gate B: Approved by the owner on 2026-11-26

## Feature Folder

`159-capability-source-map-comprehensive-analysis/changes/valuation-gauge-cards-visual-refresh`

## Before Starting

- [x] This file's `Status:` line records Gate B approval by the owner.
- [x] `standalone-valuation-gauge-cards` is released (v1.29.2) and enabled in production; the work starts from a clean `develop`.
- [x] Read `user-story.md`, `design-proposal.md` and `mockup/prototype.html`.
- [x] No open question blocks this change (OQ-136 is out of scope).

## Implementation Tasks

### 1. Backend: derived figures and band ranges (Application)

- [x] Add the optional `ValuationRatioInsights` record (`DistanceToAveragePercent`, `CurrentBand`, `DaysBelowBandPercent`, `DaysInBandPercent`) and the optional `ValuationEquilibriumInsights` record (`PriceVsMidpointPercent`, `PriceVsLowerPercent`) to `ValuationGaugeContracts.cs`. Add the optional `From`/`To` members to `ValuationGaugeSector`. All of these are additive; `CurrentVersion` stays 1.
- [x] Compute them in `ValuationGaugeProjector` from exact values and the displayed largest-remainder shares, with the omission rules of FR-4 and FR-5. Use one helper for 1-decimal half-away-from-zero rounding.

### 2. Backend: text answer

- [x] Add `Ratio2` (two decimals) and `Rial0` (whole rials) formatters next to `Exact` in `ValuationGaugeTextComposer`. Use them for every ratio and price, label the source «تحلیل‌اپ» with the date only, and add the distance and days sentence when present. Keep the quote time.

### 3. Frontend: formatting and types

- [x] Add to `src/frontend/src/lib/chat/valuation-gauges.ts`:
  - the `formatRatio2` and `formatRial0` formatters, using string-based half-away-from-zero rounding (no float drift on long decimals);
  - `formatPersianDate` (date without hour);
  - the optional `insights` and sector `from`/`to` types;
  - a source label mapping (`CyclicalWavesV2` → «تحلیل‌اپ»).

  Remove the display use of `formatExactDecimal` for ratios and prices.

### 4. Frontend: components (`valuation-gauge-cards.tsx`)

- [x] **Ratio dial:**
  - six bands with theme tokens and the active band raised;
  - percentages inside the bands;
  - coloured bound stems, dots and labels;
  - muted axis ends;
  - notch and blade pointer with a reduced-motion-aware sweep;
  - hero value and caption;
  - spoken summary;
  - band tap or focus popover with range and share (44 pt targets).
- [x] **Ratio card body:**
  - position pill;
  - insight tiles;
  - «تاریخچه در یک نگاه» strip and sentence;
  - `<details>` with the guide and the band table;
  - disclaimer;
  - footer;
  - state chip only when incomplete;
  - «—» centre without a current value;
  - pinned text.
- [x] **Equilibrium price band:** track, midpoint, pin and bubble, out-of-range and range-only states, tiles, and the two source lines.
- [x] **Answer layout:**
  - single-card header without duplication;
  - three-card answer side by side at a container width ≥ 900 px;
  - a segmented control (tab roles, keyboard arrows, 44 pt) below that width;
  - company subtitle dropped inside the three-card answer.
- [x] Keep `message-list.tsx` and the mapper unchanged except for the new optional fields. Stored v1.29.2 payloads must render.

### 5. Docs

- [x] Update `docs/providers/cyclicalwaves-v2/README.md` (display rule, derived figures, source label) and add a line to `specs/README.md` if the index lists changes.

## Tests

- [x] **Unit** (`tests/FinancialCopilot.UnitTests/ValuationGaugeCardsTests.cs`):
  - شاراک insights (AC-03);
  - omissions for pinned, null current, avg ≤ 0 and zero total (AC-04);
  - equilibrium insights, including negative and zero denominators (AC-05);
  - sector ranges;
  - text composer rounding, source and sentence (AC-01, AC-02, AC-12).
- [x] **Integration:** the existing `ValuationGaugeCardsE2ETests` still pass. One case asserts the new optional members in the API response and in replayed history.
- [x] **Frontend (Vitest):**
  - formatter edge cases (long decimals, .5 ties, negatives, integers);
  - card rendering of tiles, sentence, details, chip rules and footer;
  - stored v1.29.2 payload (AC-10);
  - segmented control switching and roles (AC-09);
  - the existing MessageList suppression test.
- [x] **Browser:** extend `scripts/valuation-gauge-cards.browser.mjs` with 430 px, the three-card segmented control, label-collision checks (dial labels against each other and against band text), marker colours (AC-06) and reduced motion. Save screenshots under `evidence/browser/` of this change folder.

## Definition Of Done

- [x] `dotnet test src/backend/FinancialCopilot.sln --configuration Release` green: no failure outside `specs/test-baseline.md` and the known pre-existing failures recorded in the previous change.
- [x] In `src/frontend`:
  - `npm run test` and `npm run build` green, no new lint error in touched files;
  - `npm run typecheck` adds no error beyond the baseline;
  - `npm run test:valuation-gauges-browser` green.
- [x] AC-01 … AC-12 checked.
- [x] Reference docs updated.
- [x] `CHANGELOG.md` `[Unreleased]`:
  - an «Added» line announcing the valuation gauges, which became visible when the flag was enabled;
  - a «Changed» line for the new design.
- [x] New questions go to `specs/open-questions.md`, deferred work to `specs/backlog.md`.
- [ ] Release, tag and deploy only through the separately confirmed release process.

## Implementation Notes

Deviations are recorded in `user-story.md` under *Deviations From The Approved Plan*:

- the three-card threshold is a 720 px answer width, not 900 px;
- the date formatter is named `formatTehranDate`;
- one `ValuationGaugeInsights` record replaces the two planned records.

Browser evidence is in `evidence/browser/`: 104 screenshots and `browser-check.txt`.

### Verification (2026-11-26)

**Backend.** Compared by TRX test names against the commit before this change (`864b7344`).

- Unit tests: 28 failures, none new.
- Integration tests: six failures appeared only in the change run: four valuation-gauge cases and two Telegram cases.
  - That run overlapped the browser and Vitest checks. The gauge failures were read and quote timeouts: a range-only card, «موقتاً در دسترس نیست», and no cards.
  - Run alone, the same 20 tests (all gauge cases plus the two Telegram cases) passed three times in a row.
  - All other integration failures also fail on the base commit.

**Frontend.**

- Vitest: the gauge suite has 36 tests, all passing, and release notes pass. The remaining full-suite failures fail identically on clean HEAD:
  - `fa-terminology` (profile editor and another feature's fixture);
  - `markdown-message` 16-row table;
  - `monthly-product-trend-chart` failed message;
  - three `scripts/*.browser.test.mjs` suites (esbuild `TextEncoder` invariant under jsdom).
- `npm run build`: green.
- Type-check: the 37-error baseline, unchanged.
- Lint: clean on the touched files.
- Browser check: green, 130 lines including reduced motion.

**Copy fix after review.** The third equilibrium tile and the text answer use the approved label «بالای حد پایین». The first implementation had «نسبت به حد پایین». The negative case is OQ-137.
