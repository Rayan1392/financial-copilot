# User Story: Share and copy a product revenue mix answer as an image

## Feature Folder

`075-company-product-revenue-mix/changes/product-mix-answer-image-share`

## Layers / Area

Layers: frontend
Area: Conversations

## Priority

P2

## Complexity

S

## Depends On

`159-capability-source-map-comprehensive-analysis/changes/valuation-gauge-answer-image-share` (v1.35.0) and
`125-industry-relative-valuation-ranking/changes/comparison-answer-image-share` (v1.36.0).

## References

- `specs/075-company-product-revenue-mix/changes/product-mix-answer-image-share/design-proposal.md`
- `specs/159-capability-source-map-comprehensive-analysis/changes/valuation-gauge-answer-image-share/user-story.md` (actions, fallbacks, limits)
- Code: `src/frontend/src/components/app/message-list.tsx`, `markdown-message.tsx`, `answer-picture.ts`,
  `src/frontend/src/lib/chat/answer-image.ts`, `product-revenue-mix.ts`

## Standards Source Of Truth

Obey `.claude/rules/*`. Do not copy rule text into this spec.

## User Story

As a Sapio user,
I want to copy or share the product revenue mix table of a symbol as a picture,
so that I can send it to Telegram and other apps as I see it.

## Business Context

The third answer to get the picture actions, after the valuation gauges and the own-industry comparison.

## Scope

- «کپی تصویر» and image share for the answer of «ترکیب فروش محصولات ‹نماد›».

## Out Of Scope

- Other Markdown-table answers and other visual answers (→ `specs/backlog.md`, existing item).
- The backend, the API, the Telegram bot channel and the on-screen rendering of the table.

## Functional Requirements

1. **Which answers.** An answer whose text `MarkdownMessage` renders and in which `parseProductRevenueMix` finds the
   heading «### ترکیب درآمد محصولات — …» and the table header «| ردیف | محصول | فروش (تومان) | سهم (٪) | غالب |».
   Gauge and comparison answers keep their own pictures.
2. **The picture.** The answer's text rendered by `MarkdownMessage` off-screen: heading, «دوره», the table; then
   Sapio's name and address. Width 640 CSS px, pixel density 2, the current theme. Inside a picture the reading-width
   limit of `MarkdownMessage` does not apply.
3. **Actions, fallbacks, speed.** As in the gauge change (its requirements 3 to 7), unchanged.
4. **File name.** `ترکیب-فروش-<symbol>-<yyyy-mm-dd Jalali>.png`; the symbol is the last parenthesised part of the
   heading's company label, or the label itself.

## Data / Schema

None.

## API (if any)

None.

## AI Query Path (if any)

Unchanged.

## UI (if any)

- `message-list.tsx`: passes `image` for a product revenue mix answer.
- `markdown-message.tsx`: no `max-w-[64ch]` inside a picture (`useIsAnswerPicture`). On screen nothing changes.
- New `lib/chat/product-revenue-mix.ts`; `productMixImageFileName` in `lib/chat/answer-image.ts`.

## Non-Functional Requirements

The limits of the gauge change apply. No new dependency.

## Acceptance Criteria

- [x] AC-01: a product revenue mix answer shows «کپی تصویر» in a browser that can copy images; a plain text answer
  does not; gauge and comparison answers still produce their own pictures.
- [x] AC-02: in Chrome, «کپی تصویر» puts an `image/png` item on the clipboard that is 1280 px wide, at a 390 px and at
  a 1280 px viewport, and shows the heading, the period, every product row and Sapio's name and address.
- [x] AC-03: the picture matches the same content rendered on screen at the same width, in the dark and the light
  theme. Evidence PNGs are saved.
- [x] AC-04: an answer with 25 products and long names is drawn within the 15-second limit; names wrap and the table
  is not cut off.
- [x] AC-05: «اشتراک‌گذاری» shares one PNG file named `ترکیب-فروش-<symbol>-<date>.png` with the text caption where
  file sharing is supported. «کپی» still copies the text.
- [ ] AC-06 (owner, real devices): on iOS Safari and Android Chrome, sharing the answer to Telegram delivers the
  picture; in Telegram Desktop, pasting after «کپی تصویر» inserts it.

## Assumptions

Choices made without a design review (the owner approved both gates in the request); each follows the two earlier
image changes:

- The picture shows the whole answer, not the table alone.
- Width 640 CSS px (the table has five short columns; 800 px would leave it sparse).
- Only this answer gets the picture, not every Markdown table.
- AC-06 cannot be verified by the agent; it shares its outcome with AC-11 (gauges) and AC-08 (comparison).
