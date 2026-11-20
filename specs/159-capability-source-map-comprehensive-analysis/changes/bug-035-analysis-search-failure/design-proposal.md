# Bug fix proposal — a failed analysis search must not answer «تحلیلی پیدا نشد» (BUG-035)

**Status: Gate A and Gate B approved by the owner on 2026-11-19** («تأیید Gate A و Gate B»); OQ-107 answered (a) the
same day. Branch `fix/bug-035-analysis-search-failure`. Implemented; see §8.

## Feature Folder   `159-capability-source-map-comprehensive-analysis/changes/bug-035-analysis-search-failure`
## Layers / Area    backend · AI (touches Billing)
## Priority         to be set by the owner (proposed: P1, user-visible wrong answer and wrong charge)
## Complexity       S
## Depends On       159 `analysis-recency-window` (done), 159 BUG-032 change (done, released in v1.15.1), 163 (done)
## References       `specs/bugs.md` BUG-035, BUG-032; `specs/159-capability-source-map-comprehensive-analysis/user-story.md` FR 7–8; `specs/159-capability-source-map-comprehensive-analysis/changes/analysis-recency-window/design-proposal.md`; `specs/163-symbol-grounded-fallback-answer/user-story.md`; `specs/open-questions.md` OQ-098, OQ-099, OQ-107; `specs/156-symbol-snapshot-default-card/design-proposal.md` ("Sequential reads in v1"); `src/backend/FinancialCopilot.Application/AI/Orchestration/SemanticCapabilityExecutors.cs` (`ComprehensiveAnalysisCapabilityExecutor`); `src/backend/FinancialCopilot.Application/AI/SymbolGroundedFallback/SymbolGroundedFallbackHandler.cs` (`MayReplace`)

## 1. Report (owner, 2026-11-19, production v1.18.0)

«شسپا را بررسی کن» → «برای نماد شسپا تحلیلی در تحلیل‌اپ پیدا نشد.», the «تحلیل‌های جامع تحلیل‌اپ» link, a metrics
table (last price, daily change, sales; P/E, P/S, EPS «Missing»), «اطمینان ۵۹٪», 1 credit. Owner: the question should
have been answered by Feature 163.

## 2. Discovery (from code; production logs were not read — ports 22 and 5432 are closed from the dev machine)

| # | Finding |
|---|---|
| F1 | The BUG-032 fix is live (in every tag since v1.15.1). «شسپا را بررسی کن» leaves «شسپا» after `AnalysisRequestCompanyText`, so the deterministic analysis route applies when «شسپا» resolves. `specs/bugs.md` still lists BUG-032 as "not yet released". |
| F2 | The sentence without «۳۰ روز اخیر» is produced only by `ComprehensiveAnalysisCard.NoResultSentence` with `recentWindowDays = null`. Every analysis search goes through `RecentComprehensiveAnalysisQueryUseCase` (DI decorator, all provider branches), which always sets `RecentWindowDays`; the agent-tool card keeps it since BUG-032. The only response without it is the one `ComprehensiveAnalysisCapabilityExecutor` builds itself when the search **threw**: `analysisAttempt.Value ?? new ComprehensiveAnalysisQueryResponse([], symbols)`. |
| F3 | With the search failed and the metrics lookup successful, the executor returns `Partial` / `partial_evidence`: the "no analysis found" card, the metrics table, 1 credit. All reported symptoms match. Feature 163 replaces only `Unsupported` and `NoData` + `comprehensive_analysis_not_recent`, so it never runs. |
| F4 | Probable cause of the throw: the executor runs the analysis search and the metrics lookup **in parallel** (`Task.WhenAll`). Both are scoped services that receive the same scoped `FinancialIngestionDbContext` (`CyclicalWavesV2ComprehensiveAnalysisQuery(db)`, `EfCoreSymbolMetricLookupService(dbContext)`; `AddDbContext` default lifetime). EF Core does not allow two operations on one context at once and throws `InvalidOperationException`. Which side fails depends on timing, so the defect is intermittent: when the lookup fails instead, the turn correctly becomes `NoData` and reaches 163. The parallel call exists since 2026-08 (Features 122/133). Feature 156 already chose sequential reads for the same reason. |
| F5 | `TryExecuteAsync` swallows every exception without logging, metric, or exception type. Only the warning codes `analysis_posts_failed` / `live_metrics_failed` remain, so production cannot tell the race from a DB timeout or another error. |
| F6 | Even apart from the race, a failed search is reported as a fact that is not true («تحلیلی … پیدا نشد») and charged 1 credit. Feature 159 FR 7 reserves that sentence for "no analysis" and bills it as `NoData` (free). The code comment beside the rule says the context metrics "are shown only beside an analysis". |
| F7 | Not verified: the exact exception in production (F4 is the probable cause, not a proven one); whether «شسپا» has an income statement for 163 to answer from. |

## 3. Proposed fix

D1–D3 restore approved behaviour (no material design change). D4 changes what a failed search returns, so the owner
decides it (OQ-107).

- **D1 Sequential reads.** The executor awaits the analysis search, then the metrics lookup, one after the other on the
  same scope. No new scope factory and no change to the use cases. Expected cost: the turn's latency grows by the
  lookup time (one indexed read); the BUG-020 technical-analysis retry stays where it is.
- **D2 Diagnosis.** A failed part increments a counter on the executor's existing `FinancialCopilot.ComprehensiveAnalysis`
  meter (`comprehensive_analysis_part_failure_total`, tags `part` = `analysis|lookup` and `exception_type`). The
  Application layer keeps no logging dependency; no question text, symbol or exception message is recorded.
- **D3 Truthful text.** When the search failed, the reply never says «تحلیلی … پیدا نشد». Under OQ-107 (a) it gets the
  existing `TemporarilyUnavailable` / `provider_or_tool_failure` system message.
- **D4 Outcome of a failed search (OQ-107, answered (a)).** Proposal (a): a failed search is `TemporarilyUnavailable`, free, whatever
  the metrics lookup returned, and the metrics table is not shown. Alternative (b): keep `Partial`, show the metrics,
  1 credit, with a "the analysis search is unavailable now" line instead of the no-analysis sentence.
- **Unchanged:** routing, the BUG-032 phrase rule, the recency window, the 163 eligibility rule (`NoData` +
  `comprehensive_analysis_not_recent` only), prices, texts of OQ-099.
- **Ledger fix:** BUG-032 status in `specs/bugs.md` → "Released in v1.15.1".

## 4. Implementation plan (for Gate B)

- [x] T1 Regression tests first (`tests/FinancialCopilot.UnitTests/SemanticRouteExecutorTests.cs`):
      (a) a fake analysis use case and a fake lookup that fail when entered while the other is still running → the
      executor never overlaps them; (b) the search throws, the lookup returns rows → OQ-107 outcome, no
      «تحلیلی … پیدا نشد» text, zero credits under (a); (c) no analysis, lookup rows → `NoData` +
      `comprehensive_analysis_not_recent` (unchanged); (d) the counter of D2 is incremented with the right tags.
- [x] T2 D1: sequential awaits in `ComprehensiveAnalysisCapabilityExecutor.ExecuteAsync`.
- [x] T3 D2: counter on the existing meter in `TryExecuteAsync`'s failure branch (part name passed by the caller).
- [x] T4 D3/D4: failed-search branch returns the OQ-107 outcome before the `Partial` rule.
- [x] T5 Integration regression: one test in the integration suite with the real `FinancialIngestionDbContext` (no
      analysis rows, metrics rows present) runs the executor ten times → always `NoData` +
      `comprehensive_analysis_not_recent`, never `Partial`.
- [x] T6 Run the 159, BUG-022, BUG-032, analysis-recency-window, 163 and billing suites, the full unit and architecture
      suites; compare with `specs/test-baseline.md` and the develop baseline recorded in the 163 report.
- [x] T7 Update `specs/bugs.md` (BUG-035 status, BUG-032 status) and the 159 user story only if OQ-107 (a) replaces
      the FR 8 billing note for this case; CHANGELOG line through the release manager.

## 5. Acceptance criteria

- AC-1 The analysis search and the metrics lookup of one turn never run at the same time.
- AC-2 A turn whose search throws never shows «تحلیلی … پیدا نشد» and gets the OQ-107 outcome and price.
- AC-3 With the search succeeding and finding nothing, the turn is `NoData` + `comprehensive_analysis_not_recent`
  in every run, so Feature 163 can answer it (when enabled and an income statement exists).
- AC-4 Every failed part increments `comprehensive_analysis_part_failure_total` with `part` and `exception_type`; no
  question text or symbol is recorded.
- AC-5 Existing suites stay green against `specs/test-baseline.md`.

## 6. Risks

- F4 is the probable, not the proven cause. D2 makes the real exception visible after release; D3/D4 remove the wrong
  answer and charge whatever the cause is.
- Sequential reads add the lookup latency to the turn (no provider call; one DB read).
- Under OQ-107 (a) a turn that today shows metrics when the analysis store is down shows a "temporarily unavailable"
  message instead.

## 7. Verification after release

Ask «شسپا را بررسی کن» and «سهم چدن را بررسی کن» several times in production: the reply is either the 163 answer
(2 credits) or the free OQ-099 card «… تحلیل تازه‌ای در ۳۰ روز اخیر …», never the sentence without the window.

## 8. Implementation report (2026-11-19)

- `ComprehensiveAnalysisCapabilityExecutor`: the analysis search (and the BUG-020 retry) runs first; a failed search returns
  `TemporarilyUnavailable` / `provider_or_tool_failure` with no payload and the warnings `analysis_posts_unavailable`,
  `analysis_posts_failed`; the metrics lookup is not called then. Otherwise the lookup runs after the search; the
  `NoData` / `Executed` / `Partial` rules for a successful search are unchanged.
- Counter `comprehensive_analysis_part_failure_total` (tags `part`, `exception_type`) on the existing meter.
- Deviation (minor, follows from OQ-107 (a)): search and lookup both failing was `Failed`; it is now
  `TemporarilyUnavailable` because the lookup is not called after a failed search. Both bill as `ProviderFailed` (free);
  the Persian text is the "temporarily unavailable" message instead of "an error occurred".
- Tests: 3 new unit tests in `SemanticRouteExecutorTests` (no overlap, failed search, counter), all three failing before
  the fix. New PostgreSQL test `Bug035AnalysisSearchSharedContextPostgreSqlTests`: with the old executor it fails on
  real PostgreSQL (the shared-context lookup threw, so the metrics were lost: the race of F4 reproduced, here on the
  lookup side); with the fix it passes 10 of 10 runs.
- Verification: Release build 0 warnings, 0 errors. Unit 32 failed / 3813, all 29 failing methods listed in
  `specs/test-baseline.md`. Architecture 19 / 19. Integration (159, comprehensive analysis, recency window, 163, 150,
  BUG-035; with the BUG-034 overrides `Billing__TahlilAppTopUps__Enabled=false`, `Authentication__Smtp__Password` and a
  local PostgreSQL): 84 passed, 1 failed, 3 skipped. The failure,
  `V2ComprehensiveAnalysisProductionIncidentEvidenceTests.ShadowDefaultMode_RunsLegacyRoute_AndReportsValidSymbolAsUnresolved`,
  fails the same way on unchanged `develop`.
- Not done: production check of §7 (after release); CHANGELOG line (release manager, after merge to `develop`).
