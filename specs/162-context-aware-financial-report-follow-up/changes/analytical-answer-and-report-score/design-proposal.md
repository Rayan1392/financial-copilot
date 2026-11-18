# Change proposal — analytical answers, همت / میلیارد تومان units and a report score (Features 162 and 163)

Status: Gate A: Approved by the owner on 2026-11-18 (OQ-103 … OQ-106 answered: proposal)

## Feature Folder   `162-context-aware-financial-report-follow-up/changes/analytical-answer-and-report-score`
## Layers / Area    backend · AI (Conversations)
## Priority         to be set by the owner
## Complexity       M
## Depends On       162 (released), 163 (released, v1.17.0), 162 change `report-interpretation-suggestion`, 163 change `answer-readability`
## References       `src/backend/FinancialCopilot.Application/AI/ReportDiscussion/ReportDiscussionContextBuilder.cs`, `.../ReportDiscussion/GroundedAnswerLayout.cs`, `.../ReportDiscussion/NumericGroundingValidator.cs`, `.../ReportDiscussion/ReportDataSetBuilder.cs`, `.../ReportDiscussion/LlmReportDiscussionModel.cs`, `.../ReportDiscussion/ReportDiscussionHandler.cs`, `.../SymbolGroundedFallback/SymbolGroundedFallbackPromptBuilder.cs`, `.../SymbolGroundedFallback/LlmSymbolGroundedFallbackModel.cs`, `.../SymbolGroundedFallback/SymbolGroundedFallbackHandler.cs`, `src/backend/FinancialCopilot.Domain/Financial/CyclicalWavesV2/MonetaryAmountUnits.cs`, `specs/162-context-aware-financial-report-follow-up/user-story.md`, `specs/163-symbol-grounded-fallback-answer/user-story.md`

This change touches both features. It is recorded under 162 because 162 owns the shared building blocks
(`GroundedAnswerLayout`, `NumericGroundingValidator`, `ReportDiscussionContextBuilder.RenderReport`); 163 reuses them.

## 1. Request (owner, 2026-11-18)

The owner wants the model to receive an instruction of this shape:

> گزارش‌های فصلی نماد X هست. گزارش (آخرین ستون موجود در جدول) به‌طور مثال تابستان ۱۴۰۵ را تحلیل کن. نکات مهم را بگو
> و به گزارش از ۱۰۰ امتیاز بده.
> نکته: اعداد بالاتر از ۱۰۰۰ میلیارد تومان را به همت بگو و زیر این عدد به میلیارد تومان.
> همچنین بیشتر تحلیلی باشد، کمتر مقایسهٔ اعداد، چون اعداد را در جدول می‌شود دید.

Apply it in Feature 162 (Report Discussion), and in Feature 163 (symbol-grounded fallback) too.

## 2. Discovery

| # | Finding | Where |
|---|---|---|
| F1 | Both system prompts say "Never compute, estimate, convert or round a number yourself … Do not convert units." Facts are sent in the source unit, usually `million_rial` (میلیون ریال). Monthly sales are already in `میلیارد تومان`. | `ReportDiscussionContextBuilder.SystemPrompt`, `SymbolGroundedFallbackPromptBuilder.SystemPrompt`, `ReportDataSetBuilder.UnitLabel`, `MonthlyActivityTrendQueryUseCase` (`UnitLabelFa: "میلیارد تومان"`) |
| F2 | Every number in the answer must match a fact or calculation (with display-rounding tolerance) or a number in the period labels, company identity or question; otherwise the answer is rejected at 0 credits. A model-computed همت value, or a score such as «۷۲ از ۱۰۰», fails this check today. | `NumericGroundingValidator.Validate` |
| F3 | So asking the LLM to convert units is unsafe: it breaks the no-arithmetic rule (D5 of 162) and is rejected by grounding. The conversion must be deterministic and happen before the prompt. | — |
| F4 | The shared layout asks for a bold takeaway line plus 2–5 `- **label:** text` items, about 180 words; this already fits «نکات مهم». It also asks that periods and units be stated for values, which pushes the model toward listing numbers. | `GroundedAnswerLayout.PromptRules` |
| F5 | In 162 the user has the table on screen (the active report was shown). In 163 the user has **not** seen the statement (it is hidden in `GroundingReport`). "The numbers are visible in the table" is true only for 162. | 163 FR-10 |
| F6 | The report block lists periods newest first; the newest period is the last column of the shown table. Period labels come from the source (they may read «فصل ۲ ۱۴۰۵» rather than «تابستان ۱۴۰۵»); the model must not rename them. | `ReportDataSet.PeriodsNewestFirst` |
| F7 | Both prompts forbid investment recommendations, and 163 forbids fair price / intrinsic value / buy-sell-hold verdicts (OQ-091). A score out of 100 is a new judgement in the answer and can read as a rating. | both prompts |
| F8 | The interpretation suggestion («صورت سود و زیان X را تفسیر کن», «ترازنامه X را تفسیر کن», «روند فروش ماهانه X را تفسیر کن») already brings "analyse this report" turns into Report Discussion. | change `report-interpretation-suggestion` |
| F9 | `MonetaryAmountUnits` (Domain) already defines toman/rial and the همت scale (10¹² toman). | `MonetaryAmountUnits.cs` |

## 3. Proposed design

### D1 — Display units are computed by the server, not by the model

A new pure Application projection `ReportDisplayUnits.Apply(ReportDataSet)` runs on the prompt candidate before
`RenderReport` in both prompt builders:

- Only monetary amounts are converted: units `million_rial`, `million_irr`, `rial`, `irr`, and the monthly-sales
  label `میلیارد تومان`. Per-share values (`rial_per_share`), shares, percentages, `times` and percentage points are
  unchanged.
- Each value is converted to billion toman (1 toman = 10 rial, factor from `MonetaryAmountUnits`). If its absolute
  value is **≥ 1,000 billion toman**, it is shown in **همت** (÷ 1,000); otherwise in **میلیارد تومان**. The decision is
  per value, as requested.
- Rounding (proposal, OQ-103): همت with one decimal (trailing «.0» dropped); میلیارد تومان as a whole number from 10
  upward, one decimal below 10, two decimals below 1.
- The projected facts carry the converted value and a new unit code (`hemat`, `billion_toman`) with labels «همت» and
  «میلیارد تومان». Evidence ids, periods and calculations (ratios, growth) are unchanged; ratios are computed from
  the raw values as today.
- The grounding validator receives the projected data set, so it checks the numbers the model actually saw.
- The stored report (assistant payload, `GroundingReport`) is never changed. The shown table keeps its own units.

The prompt rule becomes: "Amounts are already in همت or میلیارد تومان. Write each amount exactly as given, with that
unit. Never write amounts in ریال or میلیون ریال and never convert or round." Per-share values keep «ریال».

### D2 — Analytical tone

A shared prompt section `GroundedAnswerLayout.AnalysisRules`, used by both prompts:

- Explain what the numbers mean: drivers of revenue and profit, margin direction, quality of earnings (operating vs.
  non-operating and one-off items), cost pressure, sustainability, and risks to watch.
- Do not walk through the table line by line and do not list figures one after another. Use a figure only where it
  supports a point, at most one or two per item.
- Feature 162 adds: "The user can see this report as a table."
- Feature 163 adds: "The user has not seen this statement; cite the two or three key figures (for example revenue,
  net profit and a margin) that support the conclusion." (F5, OQ-106.)

The existing layout (bold takeaway line, 2–5 list items, about 180 words, no tables or headings) is kept.

### D3 — Overall analysis of the newest period, important points, score

When the question asks for an overall analysis, interpretation or evaluation of the report or of the company's
performance (for example the interpretation suggestion, «گزارش را تحلیل کن», «سهم X را تحلیل کن» in 163), the prompt
asks the model to:

1. analyse the **newest period** (the last column of the table) and use earlier periods only as context;
2. give the important points as the list items (the takeaway line names that period once, using its label as given);
3. return a score from 0 to 100 for that period's report in a new structured field `reportScore` (integer or null).

The model decides whether the question is such a request (no keyword list, in line with 162 D3). For a narrow
question («سود خالص چقدر بود؟») `reportScore` is null and no score is shown.

Score rubric in the prompt, so scores are comparable across companies: revenue growth and its quality (25),
profitability and margin direction (30), quality of earnings — operating vs. non-operating and one-off items (25),
consistency with earlier periods (20). The model returns only the total.

The server, not the model, writes the score line after the grounding check:

> **امتیاز گزارش {دورهٔ جدیدترین}: {امتیاز} از ۱۰۰**
> این امتیاز ارزیابی عملکرد مالی همین گزارش است و توصیهٔ خرید یا فروش نیست.

- The model is told not to write the score in the answer text. The validator additionally accepts the score and 100,
  so a repeated score does not reject the whole answer.
- A missing, non-integer or out-of-range score is dropped (the answer is still returned); it is not a provider failure.
- No score for valuation, price or buy/sell questions in either feature (OQ-091 rules stay): the prompt requires
  `reportScore = null` for them. The server has no keyword rule for this; it relies on the model, and the live check
  covers it. In 163 the closing offer stays the last paragraph, after the score line.
- Families (proposal, OQ-104): the score applies to the **quarterly income statement** (162 and 163). The
  balance sheet and monthly sales trend get D1 and D2 but no score until the owner extends the rubric.

### D4 — What does not change

Pricing (2 credits answered, 0 otherwise), dispositions, hand-back rules, billing, timeouts, token budgets,
persistence shape (no new payload field: the score is part of the answer text), routing, V1, frontend and Telegram
code. `MaxOutputTokens` (1,600) is enough for about 180 words plus a score; no change.

## 4. Alternatives considered

- **Let the model convert units.** Rejected: breaks the 162 no-arithmetic rule and fails grounding (F2, F3).
- **Send raw and converted values side by side.** Rejected: doubles the report block and invites the model to quote
  میلیون ریال values.
- **Model returns four sub-scores; the server sums them.** More stable and explainable, but a larger contract. Kept as
  a follow-up if live checks show unstable totals.
- **Score on every answer.** Rejected: meaningless for a narrow question.

## 5. Risks

| Risk | Mitigation |
|---|---|
| Converted values are small numbers (for example ۱۲٫۴ همت), so a wrong number is more likely to match some fact by accident; grounding gets weaker. | Tolerance stays at display rounding; live check set; accept for this change and record in the implementation report. |
| A score reads as an investment rating. | Fixed server sentence under the score; no score on valuation/advice questions; no score in 163 valuation answers. |
| The same report gets different scores on different runs. | Rubric in the prompt; live check repeats the same question three times and records the spread; sub-score alternative (§4). |
| Mixed units inside one comparison (۹۸۰ میلیارد تومان vs ۱٫۲ همت). | This is what the owner asked (per-value rule); the model writes each value with its own unit. OQ-103 can change it to one unit per row. |
| Less numeric answers in 163, where the table is hidden, may feel vague. | 163 keeps two or three key figures (D2, OQ-106). |
| Period label is «فصل ۲ ۱۴۰۵», not «تابستان ۱۴۰۵». | The model uses the label as given; mapping to seasons depends on each company's fiscal year and is out of scope. |

## 6. Open questions (added to `specs/open-questions.md`)

- **OQ-103** Rounding of همت / میلیارد تومان and per-value vs per-row unit choice.
- **OQ-104** Which families get the score: quarterly income statement only (proposal), or balance sheet and monthly sales too.
- **OQ-105** Wording of the score line and its disclaimer; should the score also be withheld from 162 answers whose question asks about price or buying.
- **OQ-106** In 163 (table not shown): keep two or three key figures (proposal) or follow the 162 "few numbers" rule exactly.

## 7. Implementation plan outline (for Gate B after Gate A)

`tasks.md` is written after Gate A. Planned tasks:

1. **T01 `ReportDisplayUnits`** (Application/AI/ReportDiscussion, pure): projection, unit labels, rounding; unit tests
   for thresholds (999.9 / 1,000 billion toman), negatives, rial vs million rial, monthly `میلیارد تومان`, untouched
   non-monetary units.
2. **T02 Prompt builders**: apply the projection to each budget candidate in `ReportDiscussionContextBuilder.Build`
   and `SymbolGroundedFallbackPromptBuilder.Build`; `prompt.Data` is the projected set, so grounding checks the same
   numbers. Stored payloads unchanged (test).
3. **T03 Prompt text**: `GroundedAnswerLayout.AnalysisRules` (D2, D3 rubric, units rule) and the per-feature lines;
   replace the "do not convert units" sentence. Prompt-content tests.
4. **T04 Model contracts**: `reportScore` (integer | null) in both structured-output schemas and parsers; invalid
   score → null. Parsing tests.
5. **T05 Handlers**: append the score line (162 after the answer; 163 before the closing offer); validator accepts the
   score and 100; drop the score on the 163 valuation/advice path. Handler tests.
6. **T06 Regression**: `ReportDiscussion162Tests`, `SymbolGroundedFallback163Tests`, `GroundedAnswerLayoutTests`,
   `ReportDiscussion162EndpointTests`, `SymbolGroundedFallback163EndpointTests` green against `specs/test-baseline.md`.
7. **T07 Live local check** (Development, real model, flags on locally): the owner's example (newest quarter of one
   symbol), one narrow question, one balance-sheet and one monthly-sales interpretation, two 163 questions (general and
   «ارزش خرید»), the same overall question three times for score spread. Record units, score, grounding result, tokens.
8. **T08 Docs**: 162 and 163 user-story notes pointing to this change, `docs/domain-glossary.md` («همت», «امتیاز
   گزارش»), `CHANGELOG.md` `[Unreleased]` Persian line through the release manager.

No migration, no new configuration, no frontend change. Gate B does not authorize a release or deploy.
