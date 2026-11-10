# Tasks: Feature 162 change — latest analysis with a partial preview

Status: Gate B: Approved by the owner on 2026-11-10

Approval record: «Gate B Approved with above answers» (2026-11-10), with the answers recorded in `design-proposal.md`.

## Feature Folder

`162-capability-source-map-comprehensive-analysis/changes/latest-analysis-teaser` (branch
`feature/162-latest-analysis-teaser` from `develop` 24bd17e)

## Acceptance Criteria

- [x] AC1 The answer shows one analysis, the newest by publish date for the ticker.
- [x] AC2 The preview is at most 30% of the text and at most 300 characters, ends with «…», never ends inside a number, and does not repeat the title; a text under 150 characters shows no preview.
- [x] AC3 The card shows the title, «تاریخ انتشار: <Persian date>», the preview, «متن کامل این تحلیل را در بخش تحلیل جامع، تحلیل اپ بخوانید» and the link.
- [x] AC4 The whole text appears nowhere in the response (API JSON, text answer, Telegram).
- [x] AC5 Invalid teaser settings stop start-up.

## Implementation Tasks

- [x] 1 Application: `ComprehensiveAnalysisTeaserPolicy` (pure) and the three settings with validation on `ComprehensiveAnalysisPresentationOptions`.
- [x] 2 `CyclicalWavesV2ComprehensiveAnalysisQuery` returns the preview instead of the whole text (replaces the 6,000-character bound).
- [x] 3 `ComprehensiveAnalysisCapabilityExecutor` asks for one analysis; `ComprehensiveAnalysisCardComposer` shows the latest item with date, preview, read-more line and link (markdown and plain text).
- [x] 4 Tests: policy cases (decimal point, title repeat, short text, cap, percent), card, executor limit, query, Telegram, Feature 162 and Feature 148 E2E expectations, live PostgreSQL check with real analyses.
- [x] 5 Docs: `appsettings.json` defaults, provider README (`summary` is the whole text), `AGENTS.md`, `CHANGELOG.md` line, Feature 162 notes.

## Definition Of Done

- [x] Build with no warnings; unit, architecture and related integration tests with no failure outside the known pre-existing set.
- [x] Acceptance criteria checked; docs updated.

## Result (2026-11-10)

Implemented on `feature/162-latest-analysis-teaser` (uncommitted). Unit: 32 failures, all in `specs/test-baseline.md`.
Architecture: BUG-003 only. Integration (Feature 148/162/analysis and live PostgreSQL with 200 real analyses): only the
9 failures that also fail on clean `develop`. Three existing tests changed on purpose because seeded texts are shorter
than the preview minimum: Feature 148 Q08, `V2ComprehensiveAnalysisProductionPathTests` (6 cases), and the executor
limit test.
