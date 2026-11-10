# Tasks: Fix Monthly-Trend Reply Bypass (BUG-019)

Status: Gate B: Approved by the owner on 2026-11-10

## Feature Folder

`158-monthly-sales-coverage-and-entity-resolution/changes/fix-monthly-trend-reply-bypass`

## Acceptance Criteria

- [x] AC-1: the success path must not change. A question with data keeps its content, status "Completed", usage and payload, and the coverage reader is never called.
- [x] AC-2: On a question matching `LooksLikeMonthlyActivityTrendQuery` with a frame and no data, a no-route company gets the not-covered reply, recorded `NoData` / `monthly_report_not_covered`.
- [x] AC-3: An outside-scope company gets the outside-scope sentence, recorded `NoData` / `data_stale_or_ineligible`.
- [x] AC-4: Two eligible rows with one symbol get the disambiguation reply, recorded `DisambiguationNeeded` / `entity_ambiguous`.
- [x] AC-5: `HasRoute` and `Unknown` keep the legacy sentence, recorded `NoData` / `supported_but_no_rows`.
- [x] AC-6: The clarification appears only when no data was found: a frame with `multiple_companies_not_supported` and no data gets it, and the pending action survives. A two-company question with data keeps today's single-company chart, text, credits and payload, by the owner's rule.
- [x] AC-7: All no-data and clarification turns above charge zero credits.
- [x] AC-8: Rollback mode keeps today's text and never calls the coverage reader.

## Tests first

File: `tests/FinancialCopilot.IntegrationTests/MonthlyTrendBypass158EndpointTests.cs`.

- [x] T1 Fail before the fix: `NoRouteCompany_GetsNotCoveredReply_RecordedAsNoData_FreeOfCharge`, `NonEligibleCompany_GetsOutsideScopeSentence_RecordedAsNoData_FreeOfCharge`, `TwoEligibleRowsWithOneSymbol_AskToDisambiguate_FreeOfCharge`, `NoRouteCompany_EnglishQuestion_GetsEnglishReply`, `TwoCompanies_GetTheClarification_FreeOfCharge` (`نمودار فروش کگل و شپنا`).
- [x] T2 Guards, pass before and after: `QuestionWithData_SuccessPathIsUnchanged_AndCoverageReaderIsNeverCalled`, `RollbackMode_KeepsTodaysTextAndStatus_AndCoverageReaderIsNeverCalled`, `HasRouteOrUnknown_KeepsLegacySentence_RecordedAsNoData_FreeOfCharge`, the two `TwoCompanies_...` phrasings that already reach the semantic route, and `TwoCompanies_FirstHasData_KeepsTodaysChart_AndCoverageReaderIsNeverCalled` (values captured with the workflow file at `86500b9`). `TwoCompanies_NoData_KeepsThePendingActionForTheReply` pins the pending action.

## Implementation

- [x] T3 Add `AiDialogueOutcomePolicy.ComposeMonthlyReportNoData`; the executor uses it, with no behaviour change.
- [x] T4 In the legacy branch, after the existing billing step and only for no data with a `SemanticFrame`, read coverage and return the reply and truthful outcome. The existing return stays as it is.
- [x] T5 In the same no-data branch, return the clarification for a frame carrying `multiple_companies_not_supported`, before any coverage read. The entry condition is identical to `86500b9`.

## Definition Of Done

- [x] Build; unit and architecture tests with no failure outside `specs/test-baseline.md`; new in-memory integration tests pass. No PostgreSQL.
- [x] Short dated correction note in `specs/158-monthly-sales-coverage-and-entity-resolution/tasks.md`.
