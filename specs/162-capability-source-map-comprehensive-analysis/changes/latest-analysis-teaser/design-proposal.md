# Change to Feature 162 — latest analysis with a partial preview

Status: Gate A: Approved by the owner on 2026-11-10

Approval record: on 2026-11-10 the owner answered the three questions of this proposal («1. بله / 2. بله تاریخ نمایش
داده شود / 3- «متن کامل این تحلیل را در بخش تحلیل جامع، تحلیل اپ بخوانید»») and wrote «Gate B Approved with above
answers», which approves this design and the plan in `tasks.md`.

Supersedes: Feature 162 `design-proposal.md` §6 (whole summary, up to three analyses) and `user-story.md` FR4.

## Problem

The TahlilApp field `summary` is the whole analysis text, not a summary (owner, 2026-11-10). Feature 162 shows it
verbatim, so the paid TahlilApp content is given away. A ticker can also have many analyses; the owner wants only the
latest by publish date.

Measured on 200 live analyses (2026-11-10): median 305 characters, 97 of 196 non-empty texts are 300 characters or
fewer, longest about 4,000. A fixed-length cut would show half of the analyses whole.

## Design (approved)

1. **Latest only.** One analysis: the newest `PublishedAt` whose title contains the ticker as a whole word; ties by
   larger provider id.
2. **Partial preview.** Drop the title when the text repeats it at the start. Show at most 30% of the text and never
   more than 300 characters, cut at the last sentence end (a «.» inside a number such as 0.88 is not one) when it lies
   in the later part of the cut, otherwise at the last word boundary, then «…». A text shorter than 150 characters shows
   no preview. The three numbers are configuration (`ComprehensiveAnalysis:TeaserPercent`, `TeaserMaxCharacters`,
   `TeaserMinimumSourceCharacters`). On the 200 analyses: 21% shown on average, at most 30%, 4 with no preview.
3. **Cut at the source.** The query returns only the preview, so the whole text never reaches the API response,
   conversation history, Telegram or a model prompt.
4. **Card:**

```text
### شگل، بخش سوم - نسبت P/s
تاریخ انتشار: 1405/08/18
همانطور که در تصویر مشخص است عقربه کمی از میانگین بلند مدتی پایین تر است و عدد فعلی 0.88 واحد است که نسبت به کلیت بازار که …

متن کامل این تحلیل را در بخش تحلیل جامع، تحلیل اپ بخوانید
[ادامهٔ این تحلیل در تحلیل‌اپ](https://tahlilapp.com/app/analysis)
```

   None found stays: «برای نماد X تحلیلی در تحلیل‌اپ پیدا نشد.» plus the link.

## Rejected

Fixed length (half the analyses shown whole); LLM summary (cost, varies per run, may give away the paid conclusion);
first paragraph (paragraphs are not reliable after HTML stripping).

## Out of scope

Database schema, API contract shape, billing, matching rule.
