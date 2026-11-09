# Feature 155 — Tasks

Status: Draft — written before Gate A; not approved. Adapt to `specs/_template/tasks.md` after Gate A on `design.md`, then present for Gate B (OQ-003, 2026-11-09).

## 155-T00 — Baseline run and reference SQL (done 2026-11-09)

- [x] Read-only production snapshot of all question turns; method prototyped and themes defined.
- [x] `queries.sql` (classifier v1) written and validated on a disposable local PostgreSQL loaded with the snapshot: aggregates identical to the prototype.
- [x] Baseline report produced outside the repository (it contains customer question text).

## 155-T01 — Close open questions before implementation

- [ ] Check whether `MissingAnswerFeedbacks` (Feature 028) has production rows. If it does, add a "query-time feedback" section rather than duplicating its data.
- [ ] Confirm the channel rule with the owner: the Telegram gateway client is `automation`, and roles containing `admin` are `internal`.
- [ ] Owner decides the policy for T04 (buy/sell advice): support, refuse with explanation, or redirect.

## 155-T02 — Domain policies (Application, pure, test-first)

- [ ] `QuestionCoveragePolicy`, `DemandThemeTaxonomy` (with `ClassifierVersion = 1`), `ChannelPolicy`.
- [ ] Unit tests covering every row of design §4 and every theme in §5, including legacy payloads, Arabic-letter normalization, and first-match-wins order. Use synthetic questions only.
- [ ] Unit test pinning the `DialogueOutcome` and `DetectedIntent` ordinals used by persisted payloads (design §9.1).

## 155-T03 — Turn source and use case

- [ ] `IUnansweredQuestionTurnSource` port, and `EfCoreUnansweredQuestionTurnSource` built on the `turns`/`facts` CTEs of `queries.sql`.
- [ ] `GetUnansweredQuestionDemandReport` use case: window validation (≤ 180 days), 50 000-turn cap, aggregation Q1–Q6.
- [ ] Migration adding `IX_Messages_Role_CreatedAt`.
- [ ] Parity integration test: seed a fixture DB, run `queries.sql` and the use case, and assert identical bucket and per-theme counts.

## 155-T04 — API

- [ ] Summary, examples (audited, fail-closed), and CSV export endpoints under `/api/v1/admin/analytics/unanswered-questions`, with policy `admin.users.read`.
- [ ] Contract tests for authorization, window validation, the audit record carrying no question text, and that no identities appear in any response.

## 155-T05 — Admin UI

- [ ] New tab in admin analytics: headline cards, bucket bar, theme ranking table (sortable), example questions on demand, weekly trend, date-range picker, CSV export, and the classifier version shown.
- [ ] Vitest model and component tests; RTL; mobile, tablet, and desktop.

## 155-T06 — Review and implementation report

- [ ] Code review, `ImplementationReport.md`, CHANGELOG entry via the release-manager.
