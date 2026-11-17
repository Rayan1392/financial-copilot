# Change proposal — readable layout for the symbol-grounded answer (Feature 163)

**Status: Gate A and Gate B approved by the owner on 2026-11-17** («با پیشنهادات موافقم Gate A and B approved»; OQ-101 =
yes, OQ-102 = all Markdown answers), branch `change/163-answer-readability`.

## Feature Folder   `163-symbol-grounded-fallback-answer/changes/answer-readability`
## Layers / Area    backend · frontend · telegram-gateway (renderer in the API) · AI
## Priority         to be set by the owner (proposed: P2, user-visible readability)
## Complexity       M
## Depends On       163 (done), BUG-032 (released in 1.15.1)
## References       `src/backend/FinancialCopilot.Application/AI/SymbolGroundedFallback/SymbolGroundedFallbackPromptBuilder.cs`, `src/backend/FinancialCopilot.Application/AI/SymbolGroundedFallback/SymbolGroundedFallbackHandler.cs`, `src/backend/FinancialCopilot.Application/AI/ReportDiscussion/NumericGroundingValidator.cs`, `src/backend/FinancialCopilot.Application/AI/ReportDiscussion/ReportDiscussionContextBuilder.cs`, `src/backend/FinancialCopilot.Infrastructure/Authentication/TelegramAssistantResponseRenderer.cs`, `src/frontend/src/components/app/markdown-message.tsx`, `specs/163-symbol-grounded-fallback-answer/user-story.md`

## 1. Report (owner, 2026-11-17, production v1.15.1)

«سهم فولاد را تحلیل کن» now gets the 163 answer (BUG-032 fixed). The copied text has paragraphs, but on the web page the
paragraphs look merged and the text is hard to read. The owner asks for a layout that is easy to read, with the
important parts in bold, set through the LLM prompt and polished as UI/UX.

## 2. Discovery

| # | Finding |
|---|---|
| F1 | The prompt gives the model no layout rules. It asks for "concise" text within about 250 words and requires the period and unit for every value, so every sentence repeats «فصل ۲ سال مالی ۱۴۰۵» and «میلیون ریال». The result is 3 long paragraphs of dense prose. |
| F2 | Large numbers come without thousands separators («۱۶۷۵۲۵۵۳۸۵ میلیون ریال», «۱۹۳۵۰۰۰۰۰۰ هزار سهم»). The prompt allows separators but the model did not use them. This is the biggest single readability problem. |
| F3 | The web renders the answer as Markdown (`MarkdownMessage`: react-markdown + GFM + sanitize), so `**bold**` and `- ` lists already work. Paragraph spacing is `space-y-2` (8 px) with `leading-relaxed` (1.625) at 15 px; for Persian text in Vazirmatn this makes paragraph breaks hard to see. Lists use `list-inside`, so a wrapped line starts under the bullet. |
| F4 | `NumericGroundingValidator` accepts `**` around numbers, Latin `,` and Persian `٬` thousands separators, and negative signs (compared by magnitude). Bold markers and separators do not break grounding. |
| F5 | Telegram: the generic path escapes the whole text for MarkdownV2, so `**…**` would appear as literal asterisks. Today only the industry-valuation path strips `**`. |
| F6 | The output cap is 1600 tokens (`SymbolGroundedFallback:MaxOutputTokens`); Markdown markers add few tokens. BUG-031's truncation (650 tokens) does not apply here. |
| F7 | Report Discussion (162) uses a separate prompt with the same "about 250 words" style and the same web/Telegram rendering, so its answers have the same problem (OQ-101). |

## 3. Proposed design

### D1 Prompt: a fixed answer layout (LLM)

A short, fixed layout section in the system prompt; the rules stay data-safe and grounded:

1. **First line, the takeaway:** one sentence that starts with «بر اساس صورت سود و زیان فصلی {نماد}» and states the
   main conclusion; the conclusion is bold.
2. **Comparison periods once:** when all values compare the same periods, name them once in the first line
   (for example «فصل ۲ ۱۴۰۵ در مقایسه با فصل ۱ ۱۴۰۵») instead of in every sentence. Each value still carries its unit.
3. **Body as a short list:** 3 to 5 items `- **برچسب:** متن`, one topic per item (فروش، سودآوری و حاشیه‌ها، سود خالص و
   سود هر سهم، اقلام غیرعملیاتی), at most two short sentences per item.
4. **Bold sparingly:** only the item label and the one or two key figures or changes of each item; never a whole
   sentence.
5. **Thousands separators** on every number of five or more digits.
6. **No** headings (`#`), tables, links, emojis or code; no evidence ids (unchanged).
7. The valuation/advice sentence, when needed, is its own last line, not bold.
8. Word limit lowered from about 250 to about 180 words (scannable answer).

The model output contract (`disposition`, `answer`, `closingOffer`, `usedEvidenceIds`) does not change.

### D2 Deterministic safety net (server, no LLM)

A pure Application formatter `GroundedAnswerLayout.Normalize(text)`, applied to the answer after grounding passes:

- Inserts thousands separators into digit runs of five or more integer digits that have none (Persian `٬` for Persian
  digits, `,` for Latin). Years and 4-digit numbers are unchanged; decimals keep their decimal part. The value does
  not change, so grounding stays valid (the validator also accepts the separators, F4).
- Removes Markdown the layout forbids (heading markers `#`, table pipes rows, links → plain text), keeps `**` and
  `- ` lists, and collapses three or more line breaks into one blank line.
- Puts the closing offer on its own paragraph (already `\n\n`, kept).

The model is asked for the layout (D1); the formatter guarantees separators and a clean Markdown subset even when the
model does not follow it.

### D3 Web typography (UI/UX)

In `MarkdownMessage`, for RTL prose messages:

- Paragraph rhythm: `space-y-3` (12 px) between blocks and `leading-7` (≈1.87 at 15 px) for Persian text.
- Lists: `list-outside` with start padding (`ps-5`), `space-y-2` between items, muted emerald markers, so wrapped
  lines align with the text and not under the bullet.
- `strong` keeps `font-semibold text-foreground`; numbers inside bold use `tabular-nums` for stable digit widths.
- Reading width stays `max-w-[64ch]`.

Scope question OQ-102: apply these to every Markdown chat answer (recommended, one consistent typography) or only to
163/162 answers.

### D4 Telegram

In the generic text path of `TelegramAssistantResponseRenderer`: balanced `**…**` becomes MarkdownV2 bold `*…*` (inner
text escaped); unbalanced markers stay escaped as today. `- ` list items become `• `. Other paths are unchanged.

### Alternatives

- Ask the model for structured JSON sections and render them with a dedicated component. Rejected for now: a new
  contract for the web, Telegram and stored messages; the Markdown subset gives most of the benefit with no contract
  change.
- Convert values to larger units (همت، میلیارد ریال) for readability. Rejected here: the model may not convert units
  (grounding), and a deterministic conversion changes the facts' presentation across reports; candidate for
  `specs/backlog.md`.

### Risks

- The model may not follow the layout every time → D2 keeps numbers readable and Markdown clean; the layout is
  checked in the live verification.
- Global typography (if OQ-102 = global) changes the look of all answers → visual check of other answer types
  (scanner tables, analysis card, monthly ranking) at the required widths.
- Telegram MarkdownV2 is strict; a malformed bold would fail the send → only balanced pairs on one line are converted,
  with a unit test for unbalanced input.

## 4. Open questions (also in `specs/open-questions.md`)

- **OQ-101:** apply the same layout rules to Report Discussion (162) answers? Proposal: yes, through one shared
  layout-rules text used by both prompts and the same D2 formatter.
- **OQ-102:** web typography for all Markdown chat answers or only 163/162? Proposal: all.

## 5. Implementation plan (for Gate B)

- [x] T1 Shared layout rules (D1) as one constant in `Application/AI/ReportDiscussion` (used by the 163 prompt; also by
  the 162 prompt if OQ-101 = yes). Word limit about 180.
- [x] T2 `GroundedAnswerLayout.Normalize` (D2) + call in `SymbolGroundedFallbackHandler` after grounding (and in the 162
  answer path if OQ-101 = yes). Done for both; the formatter runs before grounding, so the stored text is the verified text.
- [x] T3 Unit tests (`GroundedAnswerLayoutTests`; `ReportDiscussion162Tests` prompt test updated from 250 to 180 words): separators (Persian/Latin digits, 4-digit years untouched, decimals, negatives), forbidden
  Markdown removal, grounding still passes on a bold + separator answer, prompt contains the layout rules.
- [x] T4 Web (D3) in `markdown-message.tsx` + Vitest; browser check at the six required widths, light/dark, with the
  فولاد answer and with other answer types. Browser check `src/frontend/scripts/grounded-answer-layout.browser.mjs`: 1440,
  1280, 1024, 768, 390, 360, 320 px × dark/light — RTL, line height 1.87, 12 px block gap, outside list markers with
  20 px start padding, no page or table overflow; PNGs in `evidence/`.
- [x] T5 Telegram (D4) + unit tests (balanced, unbalanced, list items) — `TelegramBoldRenderingTests`.
- [x] T6 Full suites compared with `develop`; `CHANGELOG.md` `[Unreleased]` `Changed` line; 163 docs updated.
- [~] T7 (owner approval given, 2 credits) one live answer for «سهم فولاد را تحلیل کن» on Development to check the
  layout with the real model. **Not done:** no Development API was running and Docker was not started on this machine
  (2026-11-17). The prompt layout is therefore not yet verified with the real model.

### Results (2026-11-17)

- Backend vs `develop` (same machine, test-only overrides `Billing__TahlilAppTopUps__Enabled=false`,
  `Authentication__Email__Enabled=false`): unit 32 failed / 3774 (develop 33 / 3724), architecture 0 / 19, integration
  181 / 1127 (develop 179 / 1125). Branch-only names were re-run alone: `Health_ReturnsHealthyResponseAndCorrelationId`
  and `TelegramFinancialStatement143…auto_backfill_disabled…` pass; the `TahlilAppTopUpEndpointTests.CreditBalance*`
  tests fail the same way without this change (they conflict with the top-up override; order-dependent).
- Frontend Vitest: 3 failed / 544, the same 3 failures without this change (`markdown-message` quarterly table in an RTL
  message, `monthly-product-trend-chart` failed message, `fa-terminology`) plus 3 browser-script files that Vitest
  picks up; none is caused by this change. ESLint clean on the changed files. The local `node_modules` was stale
  (`@phosphor-icons/react` missing) and was reinstalled with `npm ci`.

## 6. Acceptance criteria

- The 163 answer starts with a one-sentence bold takeaway, followed by a short labelled list, then the closing offer.
- Every number of five or more digits has thousands separators; grounding passes; credits unchanged (2).
- On the web, paragraphs and list items are visibly separated and wrapped list lines align with the text.
- On Telegram, bold shows as bold and no literal `**` appears.
- No other answer type loses content or layout (visual check).
