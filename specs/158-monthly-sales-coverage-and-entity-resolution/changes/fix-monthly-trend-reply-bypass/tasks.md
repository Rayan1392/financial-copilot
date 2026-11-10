# Tasks: Fix Monthly-Trend Reply Bypass (BUG-019)

Status: Gate B: Approved by the owner on 2026-11-10

## Feature Folder

`158-monthly-sales-coverage-and-entity-resolution/changes/fix-monthly-trend-reply-bypass`

## Acceptance Criteria

- [ ] AC-1: the success path must not change. A question with data keeps its content, status "Completed", usage and payload, and the coverage reader is never called.
- [ ] AC-2: On a question matching `LooksLikeMonthlyActivityTrendQuery` with a frame and no data, a no-route company gets the not-covered reply, recorded `NoData` / `monthly_report_not_covered`.
- [ ] AC-3: An outside-scope company gets the outside-scope sentence, recorded `NoData` / `data_stale_or_ineligible`.
- [ ] AC-4: Two eligible rows with one symbol get the disambiguation reply, recorded `DisambiguationNeeded` / `entity_ambiguous`.
- [ ] AC-5: `HasRoute` and `Unknown` keep the legacy sentence, recorded `NoData` / `supported_but_no_rows`.
- [ ] AC-6: A frame with `multiple_companies_not_supported` shows the clarification instead of the legacy branch.
- [ ] AC-7: All no-data and clarification turns above charge zero credits.
- [ ] AC-8: Rollback mode keeps today's text and never calls the coverage reader.

## Tests first

File: `tests/FinancialCopilot.IntegrationTests/MonthlyTrendBypass158EndpointTests.cs`.

- [ ] T1 Fail before the fix: `NoRouteCompany_GetsNotCoveredReply_RecordedAsNoData_FreeOfCharge`, `NonEligibleCompany_GetsOutsideScopeSentence_RecordedAsNoData_FreeOfCharge`, `TwoEligibleRowsWithOneSymbol_AskToDisambiguate_FreeOfCharge`, `NoRouteCompany_EnglishQuestion_GetsEnglishReply`, `TwoCompanies_GetTheClarification_FreeOfCharge` (`نمودار فروش کگل و شپنا`).
- [ ] T2 Guards, pass before and after: `QuestionWithData_SuccessPathIsUnchanged_AndCoverageReaderIsNeverCalled`, `RollbackMode_KeepsTodaysTextAndStatus_AndCoverageReaderIsNeverCalled`, `HasRouteOrUnknown_KeepsLegacySentence_RecordedAsNoData_FreeOfCharge`, and the two `TwoCompanies_...` phrasings that already reach the semantic route.

## Implementation

- [ ] T3 Add `AiDialogueOutcomePolicy.ComposeMonthlyReportNoData`; the executor uses it, with no behaviour change.
- [ ] T4 In the legacy branch, after the existing billing step and only for no data with a `SemanticFrame`, read coverage and return the reply and truthful outcome. The existing return stays as it is.
- [ ] T5 Enter the semantic route for a frame carrying `multiple_companies_not_supported`.

## Definition Of Done

- [ ] Build; unit and architecture tests with no failure outside `specs/test-baseline.md`; new in-memory integration tests pass. No PostgreSQL.
- [ ] Short dated correction note in `specs/158-monthly-sales-coverage-and-entity-resolution/tasks.md`.
