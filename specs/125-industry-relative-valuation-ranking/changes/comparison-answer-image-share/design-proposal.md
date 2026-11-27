# Design Proposal: Share and copy an own-industry comparison answer as an image

Status: Gate A: Approved by the owner on 2026-11-27 («با طرح و سه پیشنهاد بالا موافقم»)

## Feature Folder   `125-industry-relative-valuation-ranking/changes/comparison-answer-image-share`
## Layers / Area    frontend · Conversations
## Priority         P2
## Complexity       S
## Depends On       `159-capability-source-map-comprehensive-analysis/changes/valuation-gauge-answer-image-share` (released in v1.35.0)
## References       `specs/125-industry-relative-valuation-ranking/changes/responsive-comparison-table/design-proposal.md`, `specs/159-capability-source-map-comprehensive-analysis/changes/valuation-gauge-answer-image-share/user-story.md`, `src/frontend/src/components/app/answer-actions.tsx`, `src/frontend/src/lib/chat/use-answer-image.tsx`, `src/frontend/src/components/app/industry-comparison/`

## Request

Owner, 2026-11-27, after the gauge picture shipped in v1.35.0: «کپی اسکرین شات خیلی شیک شده … میتونی برای مقایسه
نماد با صنعت خودش هم پیاده سازی کنی؟»

## Discovery

- The image mechanism of the gauge change takes any React content: `AnswerActions` has an optional `image` prop
  (`content`, `width`, `fileName`), `useAnswerImage` renders it off-screen in `AnswerShareFrame` and captures it.
  Extending it to other answers is already a backlog item of that change.
- The own-industry comparison has no typed payload in the frontend. `MarkdownMessage` recognises it in the answer's
  Markdown (`isIndustryComparisonMarkdown`, `parseIndustryComparison`) and renders the text before the table, the
  `IndustryComparison` component, and the text after it.
- The text before the table carries the heading, «گروه صنعتی» and «اندازه گروه»; the text after it is «این اطلاعات
  توصیه به سرمایه گذاری نیست». Without them a picture does not say which industry it shows.
- `IndustryComparison` picks its layout from the width of the answer (container queries: six-column table from 40rem,
  four-column table from 26rem, cards below) and from the answer density: the phone workspace (`compact`) always
  shows cards.
- The follow-up buttons («‹نماد› را با صنعت خودش مقایسه کن») are rendered by `message-list.tsx` outside
  `MarkdownMessage`.
- Answers stored before the trade-evidence change have four columns (no rank, no price).

## Approved design

1. **Buttons: the same as for a gauge answer.** «کپی» keeps copying text; «کپی تصویر» is added; «اشتراک‌گذاری» sends
   the PNG with the text as caption. The fallbacks of the gauge change apply unchanged.
2. **Image content (owner decision 1): the whole answer.** Heading, «گروه صنعتی», «اندازه گروه», the table, the ▼/▲
   legend, the disclaimer line, then Sapio's name and address. It is the same `MarkdownMessage` rendering as on
   screen, rendered a second time off-screen; nothing is redrawn by hand.
3. **Image layout (owner decision 2): always the full table.** The picture has a fixed width and the default answer
   density, so a phone that shows cards on screen still shares the six-column table. A four-column (older) answer
   gets a narrower picture with the four-column table.
4. **Follow-up buttons (owner decision 3): not in the picture.** They are outside the rendered content, so nothing
   has to be hidden.
5. **Scope.** Frontend only. No backend, API, database, Telegram or dependency change. The comparison is recognised
   by the same parser the screen uses, so the picture is offered exactly when the screen shows the comparison table.

## Alternatives considered

| Alternative | Why not |
|---|---|
| Picture of the table only | The picture would not name the industry group (owner decision 1). |
| Picture of what the phone shows (cards) | Tall and narrow, and different per device (owner decision 2). |
| A typed comparison payload from the API to decide and render | A backend and contract change for a decision the existing parser already makes on screen. |

## Risks

| Risk | Treatment |
|---|---|
| The real-device check of the gauge picture (AC-11 of that change: iOS Safari, Android Chrome, Telegram Desktop paste) is still open, and this change uses the same path | Same owner check, extended to a comparison answer. The agreed iOS fallback (hide the image actions, keep text) would cover both. |
| A large industry group makes a tall picture and a slow draw | The browser check measures a large group (40 rows) against the 15-second limit; the result is reported. |
| The comparison is recognised from Markdown text, not a typed payload | Same rule as the on-screen table (one parser); a Vitest case pins the wiring. |

## Owner decisions (Gate A, 2026-11-27)

Recorded in `specs/open-questions.md` as OQ-157 to OQ-159.
