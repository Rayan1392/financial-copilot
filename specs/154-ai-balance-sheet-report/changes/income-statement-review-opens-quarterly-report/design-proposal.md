# Change: «صورت سود و زیان X را بررسی کن» opens the quarterly income statement (Feature 154, PD-27)

**Status: Proposed — not approved (Gate A and Gate B pending).** Created 2026-11-16. The owner chose the direction
(option (a): route to the Feature 144 quarterly report) on 2026-11-16; this proposal still needs Gate A and Gate B.

## Feature Folder   `154-ai-balance-sheet-report/changes/income-statement-review-opens-quarterly-report`
## Layers / Area    backend · AI
## Priority         to be set by the owner
## Complexity       S
## Depends On       154, 144, 162 (all released)
## References       `src/backend/FinancialCopilot.Application/AI/Orchestration/FinancialStatementTableIntentRules.V2.cs` (`LooksLikeV2QuarterlyReportTableQuery`, `IsPlainReviewRequest`), `FinancialStatementTableIntentRules.cs` (`LooksLikeQuarterlyReportTableQuery`), `FinancialStatementAnalysisIntentRules.cs` (Feature 081)

## 1. Report (owner, 2026-11-16)

«صورت سود و زیان قاسم را بررسی کن» was answered by the Feature 081 single-period analysis card (6-month period, almost
every item «موجود نیست», 1 credit) instead of the quarterly income statement.

## 2. Discovery

| # | Finding |
|---|---|
| F1 | PD-27 (owner, 2026-11-05) lets «بررسی» open a report: for a balance sheet always (`ResolveBalanceSheetRequest`), but for an income statement only when «فصلی» is also present (`LooksLikeV2QuarterlyReportTableQuery`, lines 95–100). |
| F2 | Without «فصلی» the quarterly rule needs a quarter cue (`LooksLikeQuarterlyReportTableQuery`), the table rule treats «بررسی» as analysis, and Feature 081 claims any message with «سود و زیان». |
| F3 | Every route uses the same function: deterministic interpreter, capability governance, semantic frame enrichment, the statement-table executor, the conversation task state and the legacy workflow branch. One change reaches all of them. |
| F4 | Today: «صورت سود و زیان قاسم» → quarterly table; «صورت سود و زیان فصلی قاسم را بررسی کن» → quarterly table; «صورت سود و زیان قاسم را بررسی کن» → Feature 081. |

## 3. Proposed design

- **D1.** A plain review request (`IsPlainReviewRequest`: «بررسی», exactly one statement family, no other analysis cue
  such as «تحلیل») of the income statement opens the Feature 144 quarterly report also without «فصلی».
- **D2. Kept on their current routes:** a named period length or year («۶ ماهه», «سالانه», «سال ۱۴۰۴», «۱۲ ماهه»);
  «تحلیل صورت سود و زیان X» and every other analysis cue (Feature 081); a review of two statements («بررسی ترازنامه و
  سود و زیان X»); a single item («سود خالص X چقدر است»).
- **D3.** Implemented inside `LooksLikeV2QuarterlyReportTableQuery` only (V2; the frozen V1 shared rule is not touched).
  `BuildV2QuarterlyQuery` keeps the company hint as today.
- **D4.** After the report is shown, a follow-up such as «تحلیلش کن» is answered by Report Discussion (162) from the same
  report, and the report carries the «… را تفسیر کن» suggestion. No change there.
- **D5.** Billing unchanged (the quarterly report's existing price).

## 4. Implementation plan (for Gate B)

- T1 Rule change in `LooksLikeV2QuarterlyReportTableQuery` per D1–D3.
- T2 Unit tests: the four F4 messages plus D2 exclusions («تحلیل صورت سود و زیان قاسم», «صورت سود و زیان ۶ ماهه قاسم را بررسی کن», «بررسی ترازنامه و سود و زیان قاسم», «سود خالص قاسم چقدر است»); company hint «قاسم» extracted.
- T3 Endpoint test: «صورت سود و زیان وبملت را بررسی کن» returns the quarterly result.
- T4 Run the Feature 081, 144, 154, 162 and routing suites, then the full suite compared with develop.
- T5 `CHANGELOG.md` `[Unreleased]` `Changed` line; note in `specs/154-ai-balance-sheet-report` decisions (PD-27 amended).

## 5. Not in this change

Why the Feature 081 card finds almost no items for قاسم (only operating revenue) — a separate defect, not investigated.
