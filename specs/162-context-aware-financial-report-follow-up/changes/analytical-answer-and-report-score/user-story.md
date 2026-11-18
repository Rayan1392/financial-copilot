# User Story: Analytical answers, همت / میلیارد تومان units and a report score

## Feature Folder   `162-context-aware-financial-report-follow-up/changes/analytical-answer-and-report-score`
## Layers / Area    backend · AI (Conversations)
## Priority         to be set by the owner
## Complexity       M
## Depends On       162, 163, 162 change `report-interpretation-suggestion`, 163 change `answer-readability`
## References       [design-proposal.md](design-proposal.md) (Gate A 2026-11-18, OQ-103 … OQ-106 = proposal), `specs/162-context-aware-financial-report-follow-up/user-story.md`, `specs/163-symbol-grounded-fallback-answer/user-story.md`, `docs/domain-glossary.md`

## User Story

As a Sapio user reading a quarterly report (or asking about a symbol),
I want the assistant to interpret the newest period, name the important points, score the report out of 100, and
write amounts in همت or میلیارد تومان,
so that I get an analysis instead of a re-reading of the numbers I can already see.

## Scope

Report Discussion (162) and the symbol-grounded fallback (163) prompts, their structured outputs and answer composition.

## Out Of Scope

Pricing, routing, dispositions, persistence shape, migrations, frontend and Telegram code, period-label renaming
(«فصل ۲» → «تابستان»), scores for balance sheet and monthly sales (OQ-104).

## Functional Requirements

1. **FR-1 Display units (D1, OQ-103).** Before the prompt is rendered, monetary amounts (`million_rial`,
   `million_irr`, `rial`, `irr`, monthly `میلیارد تومان`) are converted per value: |value| ≥ 1,000 billion toman →
   همت with one decimal; below → میلیارد تومان, whole number from 10, one decimal from 1 to 10, two decimals below 1.
   Trailing zeros are dropped. Other units are unchanged. The grounding check uses the converted values. Stored
   payloads are not changed.
2. **FR-2 Analytical tone (D2, OQ-106).** Both prompts ask for interpretation over listing figures (at most one or
   two figures per item). 162 tells the model the table is visible; 163 tells it the table is not shown and to cite
   two or three key figures.
3. **FR-3 Overall analysis (D3).** For an overall analysis/interpretation/evaluation request the answer analyses the
   newest period, uses earlier periods as context and lists the important points.
4. **FR-4 Score (D3, OQ-104).** Only for the quarterly income statement: the model returns `reportScore` (integer
   0–100 or null) using the rubric (growth 25, profitability 30, earnings quality 25, consistency 20). Null for narrow
   questions and for price, valuation or buy/sell questions. Other families: the prompt does not ask for a score and
   any returned score is ignored. An invalid score is dropped without failing the answer.
5. **FR-5 Score line (OQ-105).** The server appends, after the answer (163: before the closing offer):
   «**امتیاز گزارش {جدیدترین دوره}: {امتیاز} از ۱۰۰**» and on the next line «این امتیاز ارزیابی عملکرد مالی همین
   گزارش است و توصیهٔ خرید یا فروش نیست.». The grounding check also accepts the score and 100.

## Acceptance Criteria

- [x] AC-1 A 12,345,678 million-rial fact is rendered as `1.2 | همت`; 9,876,543 million rial as `988 | میلیارد تومان`; 54,321 million rial as `5.4 | میلیارد تومان`; percentages and EPS unchanged.
- [x] AC-2 Monthly sales of 2,500 میلیارد تومان render as `2.5 | همت`.
- [x] AC-3 An answer quoting a converted value passes grounding; one quoting the raw million-rial value is rejected.
- [x] AC-4 A quarterly answer with `reportScore = 72` ends with the score line and disclaimer and passes grounding even if the answer text repeats 72.
- [x] AC-5 `reportScore` null, out of range or non-integer → no score line, answer still returned.
- [x] AC-6 A balance-sheet or monthly-sales answer never shows a score line.
- [x] AC-7 163: score line before the closing offer; closing offer still last.
- [x] AC-8 Both system prompts contain the analysis rules and no longer say "Do not convert units".
- [~] AC-9 Existing 162/163 unit and endpoint suites stay green against `specs/test-baseline.md`.

## Assumptions

- The newest period label is used as given by the source.
- The model decides overall vs narrow and valuation questions; there is no keyword rule (162 D3).
