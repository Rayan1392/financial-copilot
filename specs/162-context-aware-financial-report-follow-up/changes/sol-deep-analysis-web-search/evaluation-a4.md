# Amendment A4: live check of the analysis prompts (2026-11-20)

The owner's steps 4–6 for amendment A4 (`tasks.md` §8b). This is a measurement only; prices are not an output of it.

## Setup

- **Model and data:** real `gpt-6.2-sol` with the Development key. The data is the real quarterly income statement of
  فولاد (فولاد مبارکه, شرکت اصلی) from the Development database; the newest period is فصل 2 سال مالی 1405.
- **Harness:** a temporary harness on the real Development host (hosted services removed), deleted after the run.
  - 163: `SymbolGroundedFallbackHandler.RunConfirmedAsync`, the production path from the prompt to grounding.
  - 162: `ReportDiscussionContextBuilder.BuildAnalysis` and `LlmReportDiscussionAnalysisModel.AnalyzeAsync`, followed by
    the handler's own steps: score block, `Normalize`, external evidence and numeric grounding.
  - No conversation was written and no credit was reserved.
- **Questions:**
  - 163: «عملکرد فولاد مبارکه را در آخرین گزارش فصلی تحلیل کن».
  - 162: «این گزارش را تحلیل کن» (an overall analysis, with no earlier messages).
- **Prompts compared:**
  - "Old" is the prompt before A4. Its sections were copied from commit `d62b8f6c`, and every replaced section was
    checked to be present.
  - "New" is the prompt with A4.
  - Profiles, schemas, model settings and validators were the same in both.

## Results

| Feature, prompt, mode | Runs | Answered and grounded | Runs that searched | Answers with a valid cited source | Words | Latency (s) | Input / output / reasoning tokens | Estimated cost (USD) |
|---|---:|---:|---:|---:|---|---|---|---|
| 163 new, Deep + web | 3 (+1 smoke) | 4/4 | 4/4 (3 calls each) | 4/4 | 668–708 | 41–52 | 23.5k / 1.8–2.0k / 450–570 | 0.095–0.097 |
| 163 old, Deep + web | 3 | 3/3 | 2/3 | 0/3 | 465–528 | 30–38 | 10.6–19.1k / 1.2–1.3k / 265–387 | 0.033–0.080 |
| 162 new, Deep + web | 2 | 2/2 | 2/2 (3 calls each) | 1/2 | 638–700 | 48–54 | 21.3k / 1.6–1.8k / 420–450 | 0.089–0.091 |
| 162 old, Deep + web | 2 | 2/2 | **0/2** | 0/2 | 406–437 | 21 | 10.4k / 0.9k / 53–83 | 0.029–0.030 |
| 163 new, Deep, no web | 2 | 2/2 | — | — | 685–699 | 34–37 | 8.0k / 1.3k / 84–108 | 0.029 |
| 163 old, Deep, no web | 2 | 2/2 | — | — | 515–528 | 25–26 | 7.5k / 1.0–1.1k / 0–103 | 0.025–0.026 |

How the numbers were taken:
- **Cost:** input at $2 and output at $10 per 1M tokens, plus $0.01 per search; cached input is not separated, so this
  is an upper bound.
- **Grounding:** every answer passed numeric grounding, with 9–11 report numbers checked and 0 unverified. No external
  evidence item was removed.
- **Scores:** new 77–81, old 79–83.

What the table shows:

- **The owner's observation reproduces with the old prompt.** In 162 Deep + web, the model did not search in either
  run, and no old answer, in 162 or 163, cited a source.
- **With the new prompt the model searched in every Deep + web run.** 5 of 6 answers cited a source that passed
  validation.
- **Few of the consulted pages are cited.** Each answer cites one source, out of 16–30 pages consulted. The rest are
  used as background, which the rules allow, because only a cited number needs a source.
- **Search count against the cap.** `web_search_call` items were 3 against `max_tool_calls` 2. The count includes page
  opens, so the cap does not limit them one to one (see `spike-notes.md`).
- **Cost and limits.** Cost and latency rose, but both stay inside the design: a typical Deep + web estimate of about
  $0.12, the `CostAlertUsd` of 0.20, and the Deep timeout of 90 s. Output stays far below `MaxOutputTokens` 8,000, so
  truncation was not seen.

## Quality (same data, same question)

Counts in the answer body:

| | Hypothesis wording («ممکن است», «احتمالاً», «می‌تواند», «محتمل») | Industry factors (energy, power and gas, raw materials, exports, pricing) | Indicators for the next report |
|---|---|---|---|
| New, Deep + web | 3–4 | 3–9 | 0–3 |
| Old, Deep + web | 1–2 | 0 | 0–2 |
| New, Deep, no web | 4 | 2–3 | 1 |
| Old, Deep, no web | 2 | 0 | 0 |

What the new answers do that the old ones don't:
- They separate the base effect («جهش از پایه‌ای ضعیف») from real growth.
- They give explicit alternative explanations for the margin change (rate, product mix, fixed-cost absorption) and
  name the data that would decide between them.
- They bring in industry factors, such as power and gas limits on production and input and energy costs.
- They warn about reading the falling finance-cost ratio as cheaper funding.
- They test earnings against cash: «تبدیل سود به نقد» and receivables.
- They name what to watch in the next report.

The old answers mostly describe the same figures with little interpretation and no outside context. Neither version
gives a target price or a buy or sell verdict. No answer claims a web check when the web is off.

## Limitations found (not changed by A4)

1. **BUG-036, citation remnant.** Every answer that cites a source keeps a «(domain)» remnant of OpenAI's inline
   citation before `[W1]`, because `Normalize` keeps a link's text. This is a display defect, recorded in
   `specs/bugs.md`; fixed later on 2026-11-20 in `GroundedAnswerLayout.Normalize` (unit tests only).
2. **Source quality.** The cited pages were regional or secondary sites (esfahanemrooz, esfahaniya, irrena, shakhesban)
   and never codal.ir. The validator checks that a source was consulted and dated, not how authoritative it is.
   `GroundedAnalysis:WebSearch:AllowedDomains` could restrict the search, but changing it is an owner decision and was
   not tried.
3. **Verbal comparisons are not checked by the validator.** «بیش از دو برابر شده» has no number, so numeric grounding
   does not check it. The A1 rule allows such wording only when both figures are in the report.
4. **Digit style.** One new 162 answer wrote report numbers in Latin digits («167.5 همت»). The old answers mixed Latin
   digits in period labels too, so this is model variance, not A4.
5. **Untested modes.** Simple depth was not run live, because the owner's request was about Deep. Telegram renders the
   same answer text.

Result files (not in the repository) are in the session scratchpad, folder `live-a4`: `runs.jsonl`, plus the six system
prompts, old and new, for each mode.
