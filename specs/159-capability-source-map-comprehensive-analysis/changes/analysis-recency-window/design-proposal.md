# Change: TahlilApp analyses only from the last 30 days (Feature 159)

**Status: Gate A and Gate B approved by the owner on 2026-11-16; implemented the same day** (branch
`fix/159-analysis-recency-window`). OQ-099 answered (a). OQ-098 not answered: implemented as proposed (D3, any "no
analysis in the window" goes to 163); still open for the owner to reverse.

## Feature Folder   `159-capability-source-map-comprehensive-analysis/changes/analysis-recency-window`
## Layers / Area    backend · AI, FinancialData
## Priority         to be set by the owner (proposed: P1, user-visible wrong answer)
## Complexity       S
## Depends On       159 (done), 163 (merged into local develop 762c9cf)
## References       `src/backend/FinancialCopilot.Application/AI/Orchestration/SemanticCapabilityExecutors.cs` (`ComprehensiveAnalysisCapabilityExecutor`), `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/CyclicalWavesV2/CyclicalWavesV2ComprehensiveAnalysisQuery.cs`, `src/backend/FinancialCopilot.Application/FinancialData/Ingestion/ComprehensiveAnalysisCard.cs`, `src/backend/FinancialCopilot.Application/AI/SymbolGroundedFallback/SymbolGroundedFallbackHandler.cs`

## 1. Report (owner, 2026-11-16)

«سهم چدن را بررسی کن» answered with a TahlilApp analysis published 1402/08/21. Owner rule: TahlilApp analyses are valid
only within the last 30 days. Older ones are never shown; the question goes to the Feature 163 path instead.

## 2. Discovery

| # | Finding |
|---|---|
| F1 | The question routes to `comprehensive_analysis`. Its executor asks for the latest analysis (`limit = 1`) with no date bound unless the user names a period. |
| F2 | `CyclicalWavesV2ComprehensiveAnalysisQuery` (and the legacy `ComprehensiveAnalysisQueryUseCase`) filter by `PublishedAt >= FromDate` only when `FromDate` is set; otherwise the newest analysis of any age wins. So a 3-year-old analysis is "the latest". |
| F3 | The same use case serves four callers: the semantic executor, the agent tools (`ComprehensiveAnalysisToolAdapter`, v2 tool), the Telegram renderer's card, and the frozen V1 service. |
| F4 | No analysis found → `NoData` (free) with the card «برای نماد X تحلیلی در تحلیل‌اپ پیدا نشد» and the TahlilApp link. |
| F5 | Feature 163 runs only on `Unsupported` (OQ-092: `NoData` keeps its message), so today a `NoData` from this capability never reaches 163. |

## 3. Proposed design

- **D1 One owner of the rule.** An Application decorator `RecentComprehensiveAnalysisQueryUseCase` wraps whichever
  `IComprehensiveAnalysisQueryUseCase` is registered and sets the effective lower bound to
  `max(request.FromDate, now − MaxAgeDays)`. Every caller in F3 obeys it; no provider query changes.
- **D2 Window.** Rolling, on `PublishedAt`, from the current UTC time: 30 × 24 hours. Option
  `ComprehensiveAnalysis:MaxAgeDays`, default 30, validated 1–365 at start-up.
- **D3 No recent analysis.** When nothing is inside the window (an old analysis exists, or none at all), the executor
  returns `NoData` with a new reason code `comprehensive_analysis_not_recent` (still free). OQ-098 asks whether the
  "none at all" case also goes to 163 (proposal: yes — the owner's rule is "no usable answer → 163").
- **D4 Into Feature 163.** `SymbolGroundedFallbackEligibility` accepts `NoData` only with that one reason code; every
  other `NoData` keeps its message (OQ-092 unchanged). The 163 rules then apply as they are: one company, its
  quarterly income statement, 2 credits for an answer, free otherwise.
- **D5 When 163 does not answer** (flag off, no income statement, model failure) the user gets the card with a
  corrected sentence: «برای نماد X تحلیل تازه‌ای در ۳۰ روز اخیر در تحلیل‌اپ پیدا نشد.» plus the link (OQ-099).
- **D6 Technical analysis (BUG-020).** The "no technical analysis → latest analysis" retry is inside the window too.
- **D7 Billing.** No change: `NoData` is free; a 163 answer is 2 credits on its own reservation.
- **D8 V1.** The decorator also changes the frozen V1 service through DI. It is not new V1 work, only the same data
  rule; recorded here because `specs/POLICY-V1-FREEZE.md` applies.

Alternative rejected: filter inside each provider query (two implementations, the rule would live twice).

## 4. Implementation plan (for Gate B)

- [x] T1 `ComprehensiveAnalysisPresentationOptions.MaxAgeDays` (default 30) + validation; `appsettings.json`.
- [x] T2 `RecentComprehensiveAnalysisQueryUseCase` (Application, uses `TimeProvider`); DI decorates the registered use case in every provider mode (`CyclicalWavesV2ServiceCollectionExtensions.DecorateWithRecencyWindow`; the selected implementation stays registered as itself).
- [x] T3 Executor: reason code `comprehensive_analysis_not_recent` on `NoData` when no analysis is in the window.
- [x] T4 Card: `NoResultSentence` text per D5; the window travels on `ComprehensiveAnalysisQueryResponse.RecentWindowDays` to the web, agent-tool and Telegram cards.
- [x] T5 163 eligibility: `SymbolGroundedFallbackEligibility.MayReplace` (also used by the workflow pre-check); unit tests for the matrix.
- [~] T6 Tests: decorator, option range, sentence, eligibility (`AnalysisRecencyWindow159Tests`); endpoint `AnalysisRecencyWindow159EndpointTests` (old only → 163 answer, 2 credits, `FallbackOf` reason; fallback failure → free corrected card; recent analysis still shown). Updated the tests that pinned the old behaviour (stale-analysis evidence, 159 and 150 no-analysis texts, composition, 163 reason string). **Not done: a dedicated endpoint test for the BUG-020 technical-analysis retry inside the window** (the retry reuses the same decorated use case).
- [x] T7 Full suite compared with develop `762c9cf`: unit 32 failed / 3708, integration 194 failed / 1119, architecture 1 failed / 19 — every failing test also fails on develop. Docs and `CHANGELOG.md` updated.

## 4a. Defect found by the owner after merge (2026-11-16)

«سهم چدن را بررسی کن» answered «این قابلیت موقتاً در دسترس نیست». The context-metric lookup failed for چدن; before this
change the old analysis hid it (Partial), after it the empty analysis plus the failed lookup became `TemporarilyUnavailable`,
which neither shows the card nor reaches 163. Fixed: an empty analysis result is the no-recent-analysis `NoData` whatever
the lookup did; only a failed analysis query without metrics is `TemporarilyUnavailable`. Warnings `analysis_posts_failed`
and `live_metrics_failed` are now logged with the semantic route result. Regression test in `SemanticRouteExecutorTests`.
Why the lookup fails for چدن is not investigated here.

## 5. Open questions

- OQ-098: "no analysis at all" (not only "too old") also goes to 163? Proposal: yes.
- OQ-099: text of the no-recent-analysis sentence (D5).
