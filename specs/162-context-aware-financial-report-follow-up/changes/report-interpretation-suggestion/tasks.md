# Tasks: Report interpretation suggestion under shown reports

Status: Gate B: Approved by the project owner on 2026-11-16

**Feature folder:** `162-context-aware-financial-report-follow-up/changes/report-interpretation-suggestion`

**Layers / Area:** backend · AI, Conversations, Telegram

**Depends on:** Feature 162 (released in v1.13.0 behind `ReportDiscussion:Enabled`), BUG-031 fix (v1.13.1).

## Acceptance criteria

- AC-1 With Report Discussion enabled, an answered quarterly income statement, balance sheet or monthly sales trend gets the interpretation suggestion **first**, with the texts in the design, followed by the existing suggestions in their existing order.
- AC-2 At most 5 suggestions are returned; the existing ones are cut only after the fourth.
- AC-3 With Report Discussion disabled, or for an unavailable/empty report, or a non-answered outcome, suggestions are exactly as before.
- AC-4 Clicking the suggestion (same text, `suggestedActionId` of a `DiscussReport` suggestion on the active report, at most 30 minutes old) is answered by Report Discussion. Any other suggestion id, a changed text or an expired suggestion is still skipped as `structured_action`.
- AC-5 Telegram lists up to 5 suggestions.
- AC-6 Release build without new warnings; unit, architecture and integration tests show no failure outside `specs/test-baseline.md`.

## Tasks

- [x] T1 `SuggestedActionKind.DiscussReport`; shared «can this report be discussed» predicates in `ActiveReportResolver` (used by `TryDescribe` and the new suggestion). Implemented as `ActiveReportResolver.Discussable` returning `DiscussableReport`.
- [x] T2 `ReportInterpretationFollowUp` (Application, FollowUps): builds the suggestion for the three families; deterministic id (`report-interpretation:` + SHA-256); unit tests.
- [x] T3 `FollowUpSuggestionPipeline`: optional `ReportInterpretationFollowUp`, puts the suggestion first, caps the result at 5; DI wiring from `ReportDiscussion:Enabled`; unit tests (AC-1, AC-2, AC-3).
- [x] T4 `ReportDiscussionHandler`: accept a valid `DiscussReport` selection; `TimeProvider` for the 30-minute rule; unit tests (AC-4).
- [x] T5 Telegram renderer shows up to 5 suggestions; unit test (AC-5).
- [x] T6 Build and run Report Discussion, follow-up and Telegram tests, then the full unit, architecture and integration suites (AC-6). Build: 0 warnings, 0 errors. Changed test classes: 81/81 passed. Unit: 29 failing methods, all in `specs/test-baseline.md`. Architecture: BUG-003 only. Integration: 172 failing methods. The comparison run on the main checkout (`44a9c71`) was not clean, because it carried another session's uncommitted 2-credit pricing work. Against it, the only extra failure, `MonthlyTrendBypass158RollbackEndpointTests.RollbackMode_KeepsTodaysTextAndStatus_AndCoverageReaderIsNeverCalled`, passes in isolation (9/9 with the Report Discussion endpoint tests).
- [~] T7 Update the Feature 162 user story (Scope) and `docs/architecture.md` (Report Discussion and follow-up suggestions): done. Commit, merge, push and release only on the owner's request.
