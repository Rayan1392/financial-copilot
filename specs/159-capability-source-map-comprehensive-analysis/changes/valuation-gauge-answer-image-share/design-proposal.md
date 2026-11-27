# Design Proposal: Share and copy a valuation gauge answer as an image

Status: Gate A: Approved by the owner on 2026-11-27

## Feature Folder

`159-capability-source-map-comprehensive-analysis/changes/valuation-gauge-answer-image-share`

## Request

Owner, 2026-11-27: sharing a gauge answer (P/E, P/S, equilibrium price, «گیج‌های ارزش‌گذاری ‹نماد›») to Telegram
sends only the text summary. The owner wants the answer to leave the app as a picture of the cards, by «اشتراک‌گذاری»
and by copy and paste.

## Discovery

- `src/frontend/src/components/app/answer-actions.tsx` owns «کپی» and «اشتراک‌گذاری». Both send
  `answerTextWithSource(text)`: plain text plus Sapio's name and link.
- For a gauge answer the screen shows `ValuationGaugeCards` and hides the text
  (`src/frontend/src/components/app/message-list.tsx`). The text is the summary written by
  `ValuationGaugeTextComposer` for Telegram and history. So what is shared today is not what the user sees.
- On a narrow answer the cards sit behind a segmented control and only one is visible
  (`@[720px]` container query in `valuation-gauge-cards.tsx`).
- The existing PNG exports (monthly trend, quarterly table, balance sheet, cash flow) redraw the report by hand on a
  canvas (`png-brand.ts`). The gauge card component is about 1,000 lines of SVG and HTML.
- The frontend has no DOM-capture dependency. The fonts are self-hosted (`/fonts/*.woff2`), and the theme is a class
  on `<html>` (dark by default, `.light`).
- No Content-Security-Policy is set for the web app in this repository.

## Approved design

1. **Image source: the real card component.** The image is produced in the browser from a second, off-screen render
   of `ValuationGaugeCards`, captured to PNG with a DOM-capture library. Nothing is redrawn by hand, so the picture
   cannot drift from the cards.
2. **Layout of the image (owner decision 3).** The off-screen render has a fixed width, so the picture is the same on
   a phone and on a desktop and shows every card of the answer: P/E and P/S side by side with the equilibrium band
   below. A single-card answer gets a narrower picture. The picture uses the user's current theme and ends with
   Sapio's name and address.
3. **Buttons (owner decision 2).** «کپی» keeps copying text. A new «کپی تصویر» button puts the PNG on the clipboard.
   «اشتراک‌گذاری» sends the PNG as a file with the current text as its caption.
4. **Fallback.** Where the browser cannot share files, «اشتراک‌گذاری» sends the text as today. Where it cannot copy
   images, «کپی تصویر» is not shown.
5. **Scope (owner decision 1).** Gauge answers only. The mechanism (frame, capture, actions) takes any React content,
   so other answers can opt in later by passing their own content.

## Alternatives considered

| Alternative | Why not |
|---|---|
| Redraw the cards on a canvas, as the existing PNG exports do | A second representation of a large component; it would lag behind every visual change and would not be "the answer as shown". |
| Capture the on-screen DOM node | On a phone it holds one visible card and the segmented control; the width differs per device. |
| Render the image on the server | New backend rendering stack for a picture the browser can already draw. |

## Risks

| Risk | Treatment |
|---|---|
| iOS Safari renders DOM captures unreliably (fonts missing on the first draw) | Wait for `document.fonts.ready`; verify on a real device before release; the text paths stay available. |
| The share sheet must open soon after the tap, and a three-card image takes seconds to draw | Start drawing on pointer-down or focus, cache the result per answer, and let a second tap send the cached image when the browser refuses the first. |
| Telegram may drop the caption of a shared file on some devices | Accepted; the picture carries Sapio's name and address itself. |
| Telegram's in-app browser has no share sheet | Unchanged from today: the button is hidden there. |
| New runtime dependency | One small MIT library, loaded only when an image is requested, imported in one adapter file. |

## Feasibility evidence

A spike outside the repository (`evidence/spike/`) captured the existing browser fixture with `modern-screenshot`
4.7.0 in Chrome. The output matches a real browser screenshot of the same frame in both themes, for one card (480 px)
and three cards (800 px), once CSS animations are disabled inside the frame. Without that the pointer is captured at
the first frame of its sweep animation. The spike proves the Chromium rendering path only; it does not prove iOS
Safari, the clipboard or the share sheet.

## Owner decisions (Gate A, 2026-11-27)

Recorded in `specs/open-questions.md` as OQ-153 to OQ-155.
