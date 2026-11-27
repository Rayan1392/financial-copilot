# User Story: Share and copy a valuation gauge answer as an image

## Feature Folder

`159-capability-source-map-comprehensive-analysis/changes/valuation-gauge-answer-image-share`

## Layers / Area

Layers: frontend
Area: AI

## Priority

P2

## Complexity

M

## Depends On

- `159-capability-source-map-comprehensive-analysis/changes/valuation-gauge-cards-visual-refresh` (released in v1.30.0)
- `159-capability-source-map-comprehensive-analysis/changes/valuation-gauge-forward-ratio` (released in v1.34.0)

## References

- `specs/159-capability-source-map-comprehensive-analysis/changes/valuation-gauge-answer-image-share/design-proposal.md`
  (approved at Gate A on 2026-11-27) and `evidence/spike/`
- `specs/open-questions.md`: OQ-153 to OQ-155 (answered at Gate A), OQ-156 (answered at Gate B)
- `src/frontend/src/components/app/answer-actions.tsx`, `src/frontend/src/lib/chat/answer-text.ts`
- `src/frontend/src/components/app/message-list.tsx` (used by desktop `/chat`, `/c/$threadId` and mobile `/m/c/$threadId`)
- `src/frontend/src/components/app/valuation-gauge-cards.tsx`
- `src/frontend/src/components/app/png-brand.ts` (`safeFileSymbol`, `exportDate`)
- `src/frontend/scripts/valuation-gauge-cards.browser.mjs` and its fixture (pattern for the browser check)

## Standards Source Of Truth

Obey `.claude/rules/*`. Do not copy rule text into this spec.

## User Story

As a user who received a valuation gauge answer,
I want to share or copy it as a picture of the cards,
so that the people I send it to in Telegram see the gauges and not a text summary.

## Business Context

Gauge answers are visual. Today «کپی» and «اشتراک‌گذاری» send the text summary that the screen does not even show, so
a shared answer loses the dial, the bands and Sapio's look. A shared picture also carries Sapio's name to new people.

## Scope

- A PNG of the gauge cards of one answer, produced in the browser.
- A new «کپی تصویر» action and an image-carrying «اشتراک‌گذاری» for gauge answers, on desktop and mobile/PWA.
- Text fallbacks where the browser cannot handle images.

## Out Of Scope

- Image sharing for other answers (tables, reports, analyses). The mechanism accepts any content; opting other answers
  in is a later change (→ `specs/backlog.md`).
- A «دانلود تصویر» button (→ `specs/backlog.md`).
- The Telegram bot channel, the backend, the API, billing and the gauge cards' on-screen design.
- Read-only answers (Feature 164 admin view), which show no actions.

## Functional Requirements

1. **Which answers.** An answer gets the image actions when it shows valuation gauge cards (`valuationGauges` in
   `message-list.tsx`). Every other answer keeps today's actions unchanged.
2. **The picture.**
   - Content: the same `ValuationGaugeCards` component with the answer's data, rendered a second time off-screen.
   - Width: 800 CSS px when the answer has more than one card, so the wide layout applies (P/E and P/S side by side,
     equilibrium below, no segmented control). 480 CSS px for a single card. Pixel density 2.
   - Theme: the theme in use when the action is taken, with the page background behind the cards.
   - Footer: «ساپیو | دستیار هوشمند بازار سرمایه» and `sapioai.ir`, from the same constants as the text signature.
   - The pointer is at its final position (no animation frame is captured).
   - OQ-156: the collapsed «راهنمای خواندن گیج و جزئیات عددی» line is left out of the picture.
3. **«کپی تصویر».** Shown for a gauge answer when the browser supports writing images to the clipboard. It puts the
   PNG on the clipboard. Labels: «کپی تصویر» → «در حال ساخت…» → «تصویر کپی شد» or «تصویر کپی نشد», returning to idle
   after 2 seconds like «کپی».
4. **«کپی».** Unchanged: text with Sapio's name and link.
5. **«اشتراک‌گذاری».** For a gauge answer, when the browser can share files, it opens the share sheet with the PNG as
   a file, the title «پاسخ ساپیو» and today's text as the caption. File name:
   `گیج-ارزش‌گذاری-<symbol>-<yyyy-mm-dd Jalali>.png`.
6. **Fallbacks.**
   - Browser cannot share files: «اشتراک‌گذاری» shares the text exactly as today.
   - Browser cannot copy images: «کپی تصویر» is not rendered.
   - The picture cannot be produced (error or timeout): copy shows «تصویر کپی نشد»; share falls back to the text.
   - The browser refuses the share because too much time passed since the tap: the picture is kept and the button
     reads «ارسال تصویر», so the next tap shares it at once.
   - The user closes the share sheet: nothing is reported, as today.
7. **Speed.** Drawing starts when the pointer or focus reaches an image action, and the finished picture is cached for
   that answer until the theme changes or the answer leaves the page. A second copy or share does not redraw.

## Data / Schema

None.

## API (if any)

None.

## AI Query Path (if any)

Unchanged.

## UI (if any)

- `answer-actions.tsx`: optional `image` prop (`content`, `width`, `fileName`). Without it the component behaves
  exactly as today.
- New `answer-share-frame.tsx`: a portal to `document.body`, fixed off-screen, `aria-hidden` and `inert`, mounted only
  while a picture is drawn. It must not cause scrollbars or layout shift.
- New `lib/chat/answer-image.ts`: the only module that imports the capture library (`modern-screenshot`), loaded on
  demand. It also owns the browser capability checks.
- `styles.css`: inside `[data-share-frame]`, animations and transitions are off and `[data-share-hide]` is not shown.
- Persian labels as in requirements 3, 5 and 6. Buttons keep the existing action style and focus ring.

## Non-Functional Requirements

- Drawing a picture has a 15-second limit; a slow or failed draw ends in the visible failure state.
- One draw at a time per answer; concurrent taps share the same result.
- No network request leaves the app origin while drawing (fonts are same-origin and precached).
- The capture library is in its own lazily loaded chunk; the initial bundle does not grow by it.
- The off-screen frame is removed after each draw, also on failure.
- One new dependency: `modern-screenshot` (MIT). No other.

## Acceptance Criteria

- [x] AC-01: a gauge answer shows «کپی تصویر» in a browser that can copy images; a non-gauge answer shows the same
  actions as before this change.
- [x] AC-02: in Chrome, «کپی تصویر» puts an `image/png` item on the clipboard. For a three-card answer the picture is
  1600 px wide and shows all three cards and no segmented control, at a 390 px viewport and at a 1280 px viewport. For
  a single-card answer it is 960 px wide.
- [x] AC-03: the picture matches the on-screen cards rendered at the same width (pointer positions included), in the
  dark and the light theme, and ends with Sapio's name and address. Evidence PNGs are saved.
- [x] AC-04: with file sharing supported, «اشتراک‌گذاری» calls the share sheet with one PNG file, the title and the
  text caption. With file sharing unsupported it shares the text exactly as today.
- [x] AC-05: «کپی» still copies the text with Sapio's name and link.
- [x] AC-06: when drawing fails or exceeds 15 seconds, copy shows «تصویر کپی نشد», share falls back to the text, and
  the off-screen frame is gone.
- [x] AC-07: when the browser refuses a late share, the button reads «ارسال تصویر» and the next tap shares the kept
  picture without drawing again.
- [x] AC-08: a second copy or share of the same answer in the same theme does not draw again; after a theme change it
  does.
- [x] AC-09: while the frame is mounted the page has no horizontal scroll and assistive technology does not see the
  frame. The image actions have accessible names and a visible focus ring.
- [x] AC-10: the capture library is not part of the initial bundle (`npm run build` output).
- [ ] AC-11 (owner, real devices): on iOS Safari and Android Chrome, sharing a three-card answer to Telegram delivers
  the picture with correct Persian text; in Telegram Desktop, pasting after «کپی تصویر» inserts the picture.

## Assumptions

- The web app is served without a Content-Security-Policy that blocks `data:` or `blob:` images. None is set in this
  repository. If the production proxy adds one, AC-11 fails visibly and the policy must allow them.
- Telegram may show or drop the caption of a shared file depending on the device. The picture's footer is the
  guaranteed attribution.
- AC-11 cannot be verified by the agent. The change is not announced as working on iOS until the owner confirms it.
  If iOS Safari cannot draw the picture reliably, the image actions are hidden there and the text actions remain; that
  outcome is reported, not hidden.
- Firefox supports copying images from version 127 and has no file share sheet on desktop; it gets the text fallback.
