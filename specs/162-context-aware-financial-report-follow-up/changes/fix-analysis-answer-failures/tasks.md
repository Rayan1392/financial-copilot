# Tasks: Fix Report Discussion analysis answer failures (BUG-031)

Status: Gate B: Approved by the project owner on 2026-11-15

**Feature folder:** `162-context-aware-financial-report-follow-up/changes/fix-analysis-answer-failures`

**Layers / Area:** backend · AI

**Depends on:** Feature 162 (merged in `develop`, released in v1.13.0 behind `ReportDiscussion:Enabled`).

## Acceptance criteria

- AC-1 An answer that cites `F45`, `C14` or `(F۴۵)` inline, with otherwise grounded numbers, passes the grounding check. A real ungrounded number is still rejected.
- AC-2 `ReportDiscussionOptions` defaults: `MaxOutputTokens = 1600`, `ModelTimeoutSeconds = 30`. `appsettings.json` and FR-4 agree.
- AC-3 The system prompt forbids evidence ids in the answer text, asks for about 250 words at most, and asks for words, not new numbers, when a figure is not provided.
- AC-4 A provider failure logs `Reason` as `<Status>:<Code>`, for example `InvalidStructuredOutput:ModelOutputTruncated`. No report values or answer text are logged.
- AC-5 Existing Report Discussion unit, architecture and integration tests stay green. No new failures outside `specs/test-baseline.md`.
- AC-6 Live check on local Development: show the فاذر balance sheet and ask the two questions from BUG-031. Each answer is either a grounded analysis or a fallback whose logged reason explains it.

## Tasks

- [x] T1 `NumericGroundingValidator`: strip evidence-id tokens from the answer before extraction. Unit tests for AC-1 (Latin and Persian digits, ids above 12, a real ungrounded number still failing). The three new id cases failed before the fix and pass after it.
- [x] T2 `ReportDiscussionOptions` and `appsettings.json`: new defaults (AC-2). Update FR-4 in `user-story.md` (650 → 1,600).
- [x] T3 `ReportDiscussionContextBuilder.SystemPrompt`: the three instructions of AC-3, with a unit test on the prompt text.
- [x] T4 `LlmReportDiscussionModel`: failure reason `<Status>:<Code>` (AC-4), with a unit test using a fake execution service.
- [x] T5 Run the Release build, Report Discussion tests and the full unit and architecture suites. Compare against `specs/test-baseline.md` (AC-5). Build: no warnings or errors. Report Discussion unit: 39/39 passed (33 existing + 6 new). Integration `ReportDiscussion`: 8/8 passed. Architecture: 1 failure, BUG-003 (baseline). Unit: 33 failures in 30 methods. 29 are in the baseline. The extra one, `TelegramAssistantResponseRenderer089Tests.Monthly_trend_is_rendered_as_a_deterministic_png_with_a_concise_caption`, passes in isolation on this branch and on unchanged `67b831c`. It is flaky under the full parallel run and unrelated to this change.
- [ ] T6 Live check on local Development (AC-6). The owner runs it or approves a run with the local OpenAI key.
- [~] T7 Record the out-of-scope items in `specs/backlog.md` (done). Set BUG-031 to Fixed after T6. Merge, push and release only on the owner's request.
