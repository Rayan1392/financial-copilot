# Implementation Report — Feature 162 Report Discussion

Date: 2026-11-15. Branch `feature/162-report-discussion`, worktree `D:/wt/f162`, from `develop` `ff741c6`. Uncommitted. `ReportDiscussion:Enabled=false` in every settings file.

## Done

- Application `AI/ReportDiscussion/`: contracts and options, `ActiveReportResolver` (active report from persisted payloads, D6), `ReportDataSetBuilder` (facts with evidence ids, deterministic calculations, D5), `ReportDiscussionContextBuilder` (prompt, 5,000-token budget, oldest-period trimming, narrow result), `NumericGroundingValidator` (FR-7), `LlmReportDiscussionModel` (one `ExplanationGeneration` call, structured output, timeout, no retry), `ReportDiscussionHandler` (state checks only, D3).
- Infrastructure: `ReportDiscussionMessageReader` (bounded, owner-scoped); V2 workflow wiring (decision before `ConversationDialogueGate`, reply short-circuits step 3, step 4 records outcome without lexical intent detection or chips, step 6 persists the marker and skips the task-state write); hand-back marker carried on `AiQueryRequest.ReportDiscussionHandBack`; DI and settings (V2 only, validated on start).
- Contracts: `AssistantMessagePayload.ReportDiscussion` (optional, old JSON unchanged), `DetectedIntent.ReportDiscussion` (appended last), `AiQueryRequest.ReportDiscussionHandBack` (excluded from the fingerprint). No schema change, no migration.
- Docs: `docs/architecture.md`, `docs/domain-glossary.md`, `specs/README.md`, `specs/backlog.md`, `specs/open-questions.md` (OQ-088, OQ-089).

## Verification

| Suite | Result on this branch | Baseline | New failures |
|---|---|---|---|
| Unit (`tests/FinancialCopilot.UnitTests`) | 3,624 total, 32 failed (29 methods) | the 29 methods in `specs/test-baseline.md` | none |
| Architecture | 17 total, 1 failed | BUG-003 | none |
| Integration | 1,102 total, 188 failed, 230 skipped | 1,094 total, 188 failed at `ff741c6` (same run on worktree `D:/wt/f162-base`) | none; identical failure set (Docker/PostgreSQL and other pre-existing) |

New tests: `ReportDiscussion162Tests` (33 unit cases), `ReportDiscussion162EndpointTests` (8 endpoint cases through the real V2 endpoint with Feature 154 seed data and a scripted discussion model), `ReportDiscussion162ArchitectureTests` (2). All pass. Build: 0 warnings, 0 errors.

Acceptance criteria status (see `tasks.md` Implementation Notes for detail): AC-1, 2, 3, 4, 5, 6, 7 (1- and 0-credit paths), 9, 10, 12, 13, 14 verified by tests; AC-8 holds by construction; AC-11 billing-idempotent only (a retry re-runs and re-persists, as every existing route does); AC-15 unit-tested only; AC-16 deferred with the cash-flow family.

## Left (`[~]`)

- Cash-flow family and its 2-credit status: after Feature 161 meets its definition of done (Gate B option (a)).
- T18 live local check with a real model and disposable PostgreSQL; calibrate `CharsPerToken` from provider token counts.
- CHANGELOG line: added when the owner enables the flag anywhere (nothing is user-visible while it is off).
- Commit, merge, push, release and enabling the flag need the owner's separate go-ahead.

## Deviations From The Plan

- T08 uses a role-sized port `IReportDiscussionMessageReader` instead of a new `IMessageRepository` member.
- T09 lives in Application (like the other `IAiModelExecutionService` users); no Infrastructure adapter.
- T11 also skips suggestion-chip clicks and insight/alert actions (state check `structured_action`).
- Test infrastructure: `BalanceSheetReport154ApiFactory` is no longer `sealed`, so the Feature 162 factory can reuse its seed data.

## Risks To Watch When Enabled

- Every message in a conversation with an active report costs one discussion call before any hand-back (latency and absorbed tokens); watch the hand-back rate in the `Report discussion evaluated` log.
- A wrong `Answered` disposition without numbers is not caught by the grounding check; prompt rules and endpoint cases cover named companies, report types and periods.
- If usage-based billing moves to `Enforced` for `AiQuery.Scanner`, the discussion call's tokens enter the usage-based charge (existing mechanism).
