# Fix: Monthly-Trend Reply Bypass (BUG-019)

Status: Gate A: Approved by the owner on 2026-11-10

| Field | Value |
|---|---|
| Layers / Area | backend · AI, FinancialData, Conversations |
| Change of | `specs/158-monthly-sales-coverage-and-entity-resolution` |
| Defect | BUG-019 |
| Constraint | The owner's absolute rule: the success path must not change |

Line numbers are at `86500b9`.

## Problem

`FinancialCopilotWorkflowDefinition.cs:397-398` enters the semantic route only when `LooksLikeMonthlyActivityTrendQuery` is false. An explicit monthly-trend question with a frame therefore takes the legacy branch (`:862`, not-found text at `:912`), and the Feature 158 replies never run for it. The plan in `tasks.md` said this branch runs only in rollback, legacy or shadow mode; that was wrong.

Reproduced by the whole-path tests in this change: a no-route company, an outside-scope company and two eligible rows with one symbol all get the legacy not-found sentence on such a question. The two-company clarification is lost on phrasings that match the trend rule and do not read as a product trend (`نمودار فروش کگل و شپنا`); phrasings such as `روند فروش ماهانه کگل/شپنا` read as a product trend and already reach the semantic route.

## Fix

1. **Success path untouched.** When the legacy use case returns a result, nothing changes: same call, chart content, gauge, status, billing, usage and fields. The coverage reader is not called and no new branch is added before the use case.
2. **No data with a frame.** Only when the legacy branch finds no data and the request has a `SemanticFrame`, it calls `IMonthlyReportCoverageReader` with the symbol the branch used and shows the Feature 158 reply. `HasRoute` and `Unknown` keep the legacy sentence. The turn is recorded with the matching outcome and reason. Billing stays the existing zero-charge `NoData`.
3. **Two companies.** A frame whose `CompanyOrSymbol` slot carries `multiple_companies_not_supported` enters the semantic route. The bypass condition changes only for that case.
4. **One owner.** `AiDialogueOutcomePolicy.ComposeMonthlyReportNoData` holds the coverage-kind-to-reply mapping for both the executor and this branch.

With no `SemanticFrame` (Rollback, Legacy modes) nothing changes.

## Out of scope

Removing the bypass itself; every other effect of the bypass (see Findings); `LooksLikeMonthlyProductTrendQuery`.

## Findings: what else sits behind the bypass (reported, not changed)

- The legacy branch re-extracts the symbol from the text and ignores the frame, so for trend-phrase questions frame results are not used: an `Ambiguous` or `entity_not_found` company slot, canonical priority (D3), a company carried over from an earlier turn, and the frame's requested period, operations, unsupported parts and presentation.
- D5 is not affected: it runs inside the use case.
- The greeting reply is not affected: its whole-message phrases contain no trend phrase.
- The snapshot card is not affected: a bare ticker does not match the trend rule. Its monthly-trend suggestion does take this branch.

## Testing

Whole-path, in-memory endpoint tests in `tests/FinancialCopilot.IntegrationTests/MonthlyTrendBypass158EndpointTests.cs`, seen failing first; guards pin the success path and Rollback mode against values captured before the fix.

## Risks

- Turns that showed the legacy sentence as a completed answer are now recorded with their true outcome, so any report reading the persisted outcome sees them differently. No acceptance claim is made about the Feature 155 gap rate (OQ-030).
