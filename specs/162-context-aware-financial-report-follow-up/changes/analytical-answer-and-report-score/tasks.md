# Tasks: Analytical answers, همت / میلیارد تومان units and a report score

Status: Gate B: Approved by the owner on 2026-11-18 (Gate A approved the same day)

## Feature Folder

`162-context-aware-financial-report-follow-up/changes/analytical-answer-and-report-score`

Paths: backend relative to `src/backend/`, tests relative to `tests/`. Requirements: [user-story.md](user-story.md).

## Before Starting

- [x] This file's `Status:` line records Gate B approval by the owner.
- [x] Depends On: 162 and 163 are released (v1.17.0); `report-interpretation-suggestion` and `answer-readability` are on `develop`.
- [x] Branch `change/162-analytical-answer-and-report-score` from `develop`.
- [x] No open question blocks this change (OQ-103 … OQ-106 answered 2026-11-18).

## Implementation Tasks

- [x] T01 `ReportDisplayUnits` (Application/AI/ReportDiscussion, pure): FR-1 projection, unit codes `hemat` / `billion_toman` and labels; unit tests (AC-1, AC-2).
- [x] T02 Prompt builders: apply the projection to each budget candidate in `ReportDiscussionContextBuilder.Build` and `SymbolGroundedFallbackPromptBuilder.Build`, so `prompt.Data` (used by grounding) is the projected set (AC-3).
- [x] T03 Prompt text: shared `GroundedAnswerLayout.AnalysisRules` + per-feature lines; score rubric only when the report is a quarterly income statement; remove "Do not convert units" (AC-8).
- [x] T04 Model contracts: `reportScore` (integer | null) in both structured-output schemas and parsers; invalid → null (AC-5).
- [x] T05 Handlers: `ReportScoreLine` composer (FR-5); 162 appends after the answer, 163 before the closing offer; quarterly family only; validator accepts score and 100 (AC-4, AC-6, AC-7).
- [x] T06 Regression: unit suites `ReportDiscussion162Tests`, `SymbolGroundedFallback163Tests`, `GroundedAnswerLayoutTests` and integration `ReportDiscussion162EndpointTests`, `SymbolGroundedFallback163EndpointTests` (AC-9).
- [~] T07 Live local check (Development, real model): owner's example, a narrow question, balance-sheet and monthly-sales interpretation, two 163 questions, the same overall question three times (score spread).
- [x] T08 Docs: notes in 162 and 163 user stories, `docs/domain-glossary.md` («همت», «امتیاز گزارش»), `CHANGELOG.md` `[Unreleased]` line.

## Definition Of Done

- [~] `dotnet test src/backend/FinancialCopilot.sln --configuration Release`: no failure outside `specs/test-baseline.md`.
- [x] No migration (no persistence change).
- [~] Acceptance criteria in `user-story.md` checked.
- [x] Docs updated (T08).
- [ ] Gate B does not authorize a release, tag or deploy.

## Implementation Notes (2026-11-18)

- T01: rounding refinement inside OQ-103: a non-zero amount below 0.005 billion toman keeps up to four decimals instead of
  becoming 0; the همت/میلیارد تومان choice is made on the rounded value (999.7 billion toman → 1 همت).
- T03: shared rules live in `ReportDiscussion/GroundedAnalysis.cs`; 162 uses `SystemPromptFor(family)` (score rubric only
  for the quarterly income statement, `reportScore: always null` otherwise).
- T04: structured contracts renamed `ReportDiscussionReply_v2` / `SymbolGroundedFallbackReply_v2` (`reportScore` is
  required and nullable for strict structured output).
- T05: the composer is `GroundedAnalysis.ScoreBlock` (not a separate `ReportScoreLine` class); the score line is composed before `GroundedAnswerLayout.Normalize` and grounding; only the score and 100 are added
  to the allowed numbers, so a score never excuses another ungrounded number (unit test).
- T06: new `AnalyticalAnswerAndReportScoreTests` (34 cases), 4 handler tests in `ReportDiscussion162Tests`, 1 endpoint test
  in `SymbolGroundedFallback163EndpointTests`. Focused unit run 129/129. Focused integration run (162, 163, 159, usage
  billing, interpretation suggestion) 42 passed, 7 PostgreSQL skipped, **only with** `Billing__TahlilAppTopUps__Enabled=false`
  and `Authentication__Smtp__Password=…` set, because of BUG-034 (pre-existing on `develop`).
- Full solution run: architecture 19/19; unit 3,778 passed, 33 failed results: 32 are the baseline, plus
  `SchemaNavigatorTests.HierarchyProvider_RequiredCatalogAncestorCannotCauseNonTerminatingBudgetTrim`, which passed 3/3
  in isolation (flaky, unrelated). Integration: 262 failed on this branch vs 260 on clean `develop` (`3e417944`) with the
  same environment; the two extra (`FoundationEndpointTests.Health_…`, `TelegramFinancialStatement143EndToEndTests.Gateway_post_with_auto_backfill_disabled_…`)
  passed 2/2 on rerun. No test fails here that passes on `develop`. Integration failures are not covered by
  `specs/test-baseline.md`, so the DoD item stays `[~]`.
- `[~]` T07: live local check not run (needs the Development stack, PostgreSQL with statements and a real model key).
  AC-1 … AC-9 are covered by unit/endpoint tests; model behaviour (overall vs narrow, score spread, no score on price
  questions) is only verifiable live.
- CHANGELOG line added directly under `[Unreleased]` → `Changed`; the release manager decides the version at release time.
