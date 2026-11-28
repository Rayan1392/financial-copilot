# Design Proposal: Share and copy a product revenue mix answer as an image

Status: Gate A: Approved by the owner on 2026-11-28 («گیت A و B مورد تایید است. پیاده سازی را شروع کن.»)

## Feature Folder   `075-company-product-revenue-mix/changes/product-mix-answer-image-share`
## Layers / Area    frontend · Conversations
## Priority         P2
## Complexity       S
## Depends On       `159-capability-source-map-comprehensive-analysis/changes/valuation-gauge-answer-image-share` (v1.35.0), `125-industry-relative-valuation-ranking/changes/comparison-answer-image-share` (v1.36.0)
## References       `specs/125-industry-relative-valuation-ranking/changes/comparison-answer-image-share/user-story.md`, `src/frontend/src/components/app/message-list.tsx`, `src/frontend/src/components/app/markdown-message.tsx`, `BuildProductRevenueMixContent` in `FinancialCopilotWorkflowDefinition.cs`

## Request

Owner, 2026-11-28, with a screenshot of the answer: add the screenshot copy to the answer of «ترکیب فروش محصولات
‹نماد› را بررسی کن», which is a table. The owner approved both gates in the same request, before a design was shown;
the design below follows the two approved image changes and its choices are listed under Assumptions for review.

## Discovery

- The answer reaches the web app as Markdown only: the heading «### ترکیب درآمد محصولات — ‹شرکت› (‹نماد›)», the line
  «دوره: ‹سال/ماه›» and a five-column table (ردیف, محصول, فروش (تومان), سهم (٪), غالب). The server writes it in
  `BuildProductRevenueMixContent`; the frontend has no typed payload for it.
- `MarkdownMessage` renders it with the generic Markdown table, inside the reading-width limit (`max-w-[64ch]`) and a
  horizontally scrollable container.
- The image mechanism (`AnswerActions` `image` prop, `useAnswerImage`, `AnswerSharePicture`) takes any content.

## Design

1. **Buttons:** as for the gauge and comparison answers — «کپی» text, «کپی تصویر», «اشتراک‌گذاری» with the PNG and the
   text caption, the same fallbacks.
2. **Recognition:** by the heading and the table header the server writes (`lib/chat/product-revenue-mix.ts`). The
   "no data" answer has no table and gets no picture.
3. **Picture:** the whole answer (heading, period, table) rendered by `MarkdownMessage` off-screen at a fixed width
   of 640 CSS px, then Sapio's name and address. The same picture on phone and desktop.
4. **Reading width:** inside a picture `MarkdownMessage` does not apply the 64ch limit, so the table fills the
   picture instead of leaving an empty band.
5. **File name:** `ترکیب-فروش-<نماد>-<Jalali date>.png`; the symbol is read from the heading.

## Alternatives considered

| Alternative | Why not |
|---|---|
| A typed product-mix payload from the API | A backend and contract change for a table the screen already renders from the text. |
| Offer the picture for every answer with a Markdown table | Not asked; each answer type needs its own width and check (backlog item of the gauge change). |

## Risks

| Risk | Treatment |
|---|---|
| Recognition depends on the server's exact heading and header text | One frontend module owns both strings; a Vitest case pins them. If the server text changes, the picture is no longer offered and the text actions remain. |
| Long product names or many products | The browser check covers 25 products with long names: names wrap, nothing is cut off, drawn in about 3 s. |
| Real devices are still unchecked for all three pictures | Same owner check as AC-11 (gauges) and AC-08 (comparison). |
