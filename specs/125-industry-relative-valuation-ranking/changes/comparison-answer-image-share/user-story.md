# User Story: Share and copy an own-industry comparison answer as an image

## Feature Folder

`125-industry-relative-valuation-ranking/changes/comparison-answer-image-share`

## Layers / Area

Layers: frontend
Area: Conversations

## Priority

P2

## Complexity

S

## Depends On

`159-capability-source-map-comprehensive-analysis/changes/valuation-gauge-answer-image-share` (released in v1.35.0;
its real-device check AC-11 is open and is shared with this change, see Assumptions).

## References

- `specs/125-industry-relative-valuation-ranking/changes/comparison-answer-image-share/design-proposal.md` (approved at Gate A)
- `specs/159-capability-source-map-comprehensive-analysis/changes/valuation-gauge-answer-image-share/user-story.md` (the image mechanism, its fallbacks and limits)
- `specs/125-industry-relative-valuation-ranking/changes/responsive-comparison-table/design-proposal.md` (the layouts)
- Code: `src/frontend/src/components/app/message-list.tsx`, `answer-actions.tsx`, `markdown-message.tsx`,
  `industry-comparison/`, `answer-density.tsx`, `src/frontend/src/lib/chat/answer-image.ts`, `use-answer-image.tsx`

## Standards Source Of Truth

Obey `.claude/rules/*`. Do not copy rule text into this spec.

## User Story

As a Sapio user,
I want to copy or share the own-industry comparison of a symbol as a picture,
so that I can send the table to Telegram and other apps as I see it, not as plain text.

## Business Context

The gauge picture (v1.35.0) made a visual answer shareable. The own-industry comparison is the next answer whose text
form loses the table, the colours and the ▼/▲ markers when it is copied or shared.

## Scope

- «کپی تصویر» and image share for an answer that shows the own-industry comparison table.
- A fixed-width picture of the whole answer with the full table, the same on phone and desktop.

## Out Of Scope

- Other visual answers (reports, charts, the symbol snapshot card) → `specs/backlog.md`, existing item.
- «دانلود تصویر» → `specs/backlog.md`, existing item.
- The backend, the API, the Telegram bot channel, billing, and the on-screen design of the comparison.
- Read-only answers (Feature 164 admin view), which show no actions.

## Functional Requirements

1. **Which answers.** An answer gets the image actions when its text is rendered by `MarkdownMessage` and
   `parseIndustryComparison` recognises the comparison table in it — the same condition under which the screen shows
   `IndustryComparison`. Gauge answers keep their own picture. Every other answer is unchanged.
2. **The picture.**
   - Content: the answer's text rendered by `MarkdownMessage`, a second time, off-screen: heading, «گروه صنعتی»,
     «اندازه گروه», the comparison table, the ▼/▲ legend and the disclaimer line.
   - Layout: inside a picture the comparison renders only its table, regardless of the workspace density, so the
     picture never shows the card layout.
   - Width: 800 CSS px for an answer with rank and trade evidence (six-column table); 560 CSS px for an older
     four-column answer (four-column table). Pixel density 2.
   - Theme, background and footer (Sapio's name and `sapioai.ir`): as in the gauge picture.
   - The follow-up buttons are not part of the picture.
3. **Actions, fallbacks, speed.** «کپی تصویر», «اشتراک‌گذاری» with a file, their labels, the fallbacks and the
   warm-up and caching are those of the gauge change (its requirements 3 to 7), unchanged.
4. **File name.** `مقایسه-با-صنعت-<yyyy-mm-dd Jalali>.png`.

## Data / Schema

None.

## API (if any)

None.

## AI Query Path (if any)

Unchanged.

## UI (if any)

- `message-list.tsx`: passes `image` to `AnswerActions` for a comparison answer.
- `lib/chat/answer-image.ts`: `comparisonImageFileName()`.
- Deviation recorded in `tasks.md` (Implementation Notes): `answer-share-frame.tsx` provides
  `AnswerPictureContext` (`answer-picture.ts`) and `industry-comparison.tsx` renders one table inside a picture, so
  large answers are drawn within the time limit. `answer-actions.tsx` and `use-answer-image.tsx` are unchanged.

## Non-Functional Requirements

- The limits of the gauge change apply: 15-second draw limit, one draw at a time per answer, frame removed after
  each draw, no request outside the app origin, capture library loaded on demand.
- No new dependency.

## Acceptance Criteria

- [x] AC-01: a comparison answer shows «کپی تصویر» in a browser that can copy images. A plain text answer shows the
  same actions as before; a gauge answer still produces the gauge picture.
- [x] AC-02: in Chrome, «کپی تصویر» on a six-column comparison puts an `image/png` item on the clipboard that is
  1600 px wide, at a 390 px viewport in the phone density and at a 1280 px viewport. The picture shows the
  six-column table (not cards) with every member row and the group-average row.
- [x] AC-03: the picture shows the heading, «گروه صنعتی», «اندازه گروه», the legend, the disclaimer line and Sapio's
  name and address, and no follow-up button. It matches the same content rendered on screen at the same width, in
  the dark and the light theme. Evidence PNGs are saved.
- [x] AC-04: a four-column (older) comparison answer produces a 1120 px wide picture with the four-column table.
- [x] AC-05: «اشتراک‌گذاری» on a comparison answer shares one PNG file named `مقایسه-با-صنعت-<date>.png` with the
  text caption where file sharing is supported, and the text where it is not. «کپی» still copies the text.
- [x] AC-06: a 40-row comparison is drawn within the 15-second limit in Chrome; the measured time is recorded
  (about 5 s; `evidence/browser/browser-check.log`).
- [x] AC-07: while the frame is mounted the page has no horizontal scroll and the frame is `inert`; it is removed
  afterwards.
- [ ] AC-08 (owner, real devices): on iOS Safari and Android Chrome, sharing a comparison answer to Telegram
  delivers the picture with correct Persian text; in Telegram Desktop, pasting after «کپی تصویر» inserts it.

## Assumptions

- The file name carries no symbol: the frontend has no typed field for the asked symbol of a comparison, and the
  first table row is not guaranteed to be it.
- AC-08 cannot be verified by the agent. It shares its outcome with AC-11 of the gauge change: if iOS Safari cannot
  draw pictures reliably, the image actions are hidden there for both answers and the text actions remain.
- The largest industry group in production is not known to the agent. AC-06 uses 40 rows as a generous size; a
  larger real group that exceeds the limit ends in the visible «تصویر کپی نشد» state and the text share.
