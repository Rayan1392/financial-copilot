# Tasks: Sapio forward P/E and P/S estimate on the valuation gauge cards

Status: Gate B: Approved by the owner on 2026-11-26 (Gate A on the same day; OQ-148 answered as proposed). Implementation is handed to another agent.

## Feature Folder

`159-capability-source-map-comprehensive-analysis/changes/valuation-gauge-forward-ratio`

## Before Starting

- [x] This file's `Status:` line records Gate B approval by the owner (see `specs/FEATURE_DEVELOPMENT_WORKFLOW.md`, "Recording Approval").
- [~] The features in `Depends On` are done (their `tasks.md` definition of done is ticked).
  148 and `valuation-gauge-cards-visual-refresh` are done. `standalone-valuation-gauge-cards` still has two unticked
  items (staging/rollback evidence, release confirmation), but it is released and enabled in production; reported to
  the owner (2026-11-27), not treated as a blocker.
- [x] Read `user-story.md`, the references it lists, and the relevant `docs/` files.
- [x] No open question in `specs/open-questions.md` blocks this spec (OQ-148 was answered on 2026-11-26: industry 31 is excluded as a whole).

## Implementation Tasks

### 1. Estimator (Application, pure)

- [x] 1.1 In `src/backend/FinancialCopilot.Application/FinancialData/ValuationGauges/` add `ForwardRatioContracts.cs`:
  `ForwardRatioInputs` (monthly sales as `(DateOnly MonthEnd, decimal Sales)` oldest first; quarters as
  `(DateOnly PeriodEnd, decimal Revenue, decimal NetProfit)` oldest first; `IndustryExternalId`),
  `ForwardRatioEstimate` (forecast sales, growth factor, margin, forward P/S, forward P/E, latest month, latest
  quarter; each ratio nullable with its reason), `ForwardRatioReasonCodes`, and the port `IForwardRatioInputsReader`
  (`Task<ForwardRatioInputs?> ReadAsync(ValuationGaugeSecurity, string vendorTicker, CancellationToken)`).
- [x] 1.2 Add `ForwardRatioEstimator` (static, pure): the formula and every guard of user-story requirement 3 that
  depends on the inputs. Constants 12 / 4 / 8 are named and private. Consecutive months and quarters are checked by
  the gap between period ends. Takes the thresholds and `now` as arguments; no clock, no options type inside.
- [x] 1.3 Add `ValuationGaugeForwardOptions` nested in `ValuationGaugeCardsOptions` (`ForwardEstimate`) with the
  defaults of the user story, and extend `IsValid` (growth range 0 < min < max ≤ 20, age 1–365 days, timeout
  100–30000 ms).

### 2. Card contract and projection (Application)

- [x] 2.1 `ValuationGaugeContracts.cs`: add `ValuationGaugeForwardEstimate` (`Value`, `Angle`, `Side`,
  `MethodVersion`, `SalesGrowthFactor`, `MarginPercent?`, `LatestSalesMonthEnd`, `LatestQuarterEnd`) and the optional
  `Forward` member at the end of `ValuationGaugeCard`.
- [x] 2.2 `ValuationGaugeProjector`: given a displayable ratio card and an estimate, compute the forward needle with
  the existing angle and pin logic and attach `Forward`. Nothing else on the card changes.
- [x] 2.3 `ValuationGaugeCardsUseCase`: when the flag is on and a P/E or P/S card is displayable, read the inputs once
  with `WithBudget(ForwardEstimate.ReadTimeoutMilliseconds)`, run the estimator, pass the result to the projector.
  Timeout or exception → no estimate and one observer call; caller cancellation rethrown. The reader is an optional
  constructor dependency so the flag-off path needs no registration.
- [x] 2.4 `IValuationGaugeObserver` and `ValuationGaugeTelemetry`: `ForwardEstimated(kind, shown, reasonCode, elapsed)`.
- [x] 2.5 `ValuationGaugeTextComposer`: the forward line under the current value, only when `Forward` exists.

### 3. Input reader (Infrastructure)

- [x] 3.1 `ForwardRatioInputsReader` in `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/CyclicalWavesV2/`:
  resolve the ticker as `ValuationGaugeSnapshotReader` does (reuse its matching; extract it if needed, no copy), read
  the last 8 statements' revenue and net profit (discrete quarter) with the Feature 148 line keys, map the ticker ISIN
  to exactly one monthly-report company, and read its last 16 accepted monthly reports summed per month (month itself
  only). `AsNoTracking`, bounded `Take`, no business decision.
- [x] 3.2 Register it in `CyclicalWavesV2ServiceCollectionExtensions` only when `ForwardEstimate:Enabled` is true.
- [x] 3.3 Read the company's industry external id for the exclusion guard (default list `["31"]`, OQ-148).
- [x] 3.4 `appsettings.json`: add the `ForwardEstimate` section with `Enabled: false`. Production reads only
  `appsettings.Production.json` and the server `.env`; the flag is not turned on by this change.

### 4. Frontend

- [x] 4.1 `src/frontend/src/lib/chat/valuation-gauges.ts`: optional `forward` type and parsing (absent → undefined;
  malformed → ignored, card still renders).
- [x] 4.2 `valuation-gauge-cards.tsx`: dashed needle with a ring, the «(برآورد ساپیو)» chip, the method sentence in
  «جزئیات», accessible name; nothing drawn without `forward`.
- [x] 4.3 Fixtures in `__tests__/valuation-gauge-fixtures.ts` and the browser fixture: with forward inside the axis,
  pinned above, and absent.

### 5. Records

- [x] 5.1 `docs/providers/cyclicalwaves-v2/README.md`: replace «Forward P/E … is never estimated» with the approved
  definition, guards, label and flag. `docs/domain-glossary.md`: «Forward P/E / Forward P/S (Sapio estimate)».
- [x] 5.2 `specs/README.md` index entry; `specs/backlog.md`: refineries, companies without monthly sales, filling a
  missing quarter from year-to-date, precomputed estimates.
- [x] 5.3 `CHANGELOG.md` `[Unreleased]` → `### Added`: «برآورد ساپیو از P/E و P/S آینده‌نگر روی کارت‌های گیج ارزش‌گذاری (با عقربهٔ خط‌چین).»
- [ ] 5.4 `specs/open-questions.md`: close OQ-136 when the change ships. (The research folder
  `tools/research/forward-pe-reverse-engineering/` was committed with these specs, OQ-147.)

### 6. Rollout (owner actions, after implementation)

- [ ] 6.1 Read-only coverage count on production (a SQL file added under the research folder): gauge companies with 16
  months and 8 quarters, and how many pass the guards. Report the numbers before the flag is turned on.
  The query is `tools/research/forward-pe-reverse-engineering/sql/14_forward_estimate_coverage.sql` (added
  2026-11-27, read-only, mirrors the reader and the guards). Run read-only on the local database on 2026-11-27 (owner's
  permission; local data, not production): 656 gauge tickers, 470 with 16 months and 8 quarters, 428 pass the guards,
  forward P/S on 427 and forward P/E on 398. Not estimated: 121 insufficient monthly sales, 41 company unmatched, 37
  growth out of range, 15 insufficient quarters, 12 excluded industry, 1 stale, 1 no comparison sales. The guarded
  fixture symbols get the estimator's reasons (فگستر growth, شپنا industry, آپ quarters). Production still to run.
- [ ] 6.2 Turn on `ValuationGaugeCards__ForwardEstimate__Enabled` on the server only on the owner's instruction.
  Rollback: turn it off.

## Tests

- [x] T1 Unit `ForwardRatioEstimatorTests`: formula on a hand-computed case; every guard and reason code; zero and
  negative denominators; margin ≤ 0 keeps the forward P/S; non-consecutive months and quarters; boundary values of the
  growth range and the report age.
- [x] T2 Unit, reference fixtures: copy `tools/research/forward-pe-reverse-engineering/results/estimator-fixtures.json`
  (25 symbols: exact inputs, thresholds, `as_of`, expected values and reason codes; produced by
  `export_estimator_fixtures.py`) into the unit-test project as a test resource. Assert every `expected` member to two
  decimals, including the three guarded symbols (فگستر `growth_out_of_range`, شپنا `excluded_industry`, آپ
  `insufficient_quarters`). Do not compare with the `tahlilapp_*` members (AC-03).
- [x] T3 Unit `ValuationGaugeCardsTests`: flag off → payload identical (AC-01); forward inside and pinned (AC-08);
  reader timeout and exception (AC-07); caller cancellation; P/S shown while P/E is not (AC-05); JSON round trip of a
  stored card without `forward` (AC-10).
- [x] T4 Unit, text composer: the forward line present and absent (AC-09).
- [x] T5 Integration `ForwardRatioInputsReaderTests` on the test database: month-itself rows only, accepted revision
  only, unique ISIN match, ambiguous and missing match, row bounds.
- [x] T6 Integration `ValuationGaugeCardsE2ETests`: one answer with the flag on (payload and text), and existing gauge
  and billing tests unchanged (AC-11).
- [x] T7 Architecture tests green (AC-12).
- [x] T8 Frontend Vitest `valuation-gauge-cards.test.tsx`: needle, chip, method sentence, absent case, malformed
  `forward`. Run from the uppercase `C:` path.
- [x] No live OpenAI or provider call in any test. The estimator, use case and composer tests use fakes; the E2E test asserts no outbound HTTP call and no tool call (`Feature148Chat.NoOutboundCalls`).

## Definition Of Done

- [x] `dotnet test src/backend/FinancialCopilot.sln --configuration Release`: no failure outside `specs/test-baseline.md`.
- [x] No migration: `dotnet ef migrations has-pending-model-changes --context FinancialIngestionDbContext` reports none.
- [x] In `src/frontend`: lint of the changed files, `npm run test` and `npm run build` green; `npm run typecheck` adds no error beyond the baseline.
- [x] Acceptance criteria in `user-story.md` all checked.
- [x] Reference docs updated (5.1, 5.2) and the CHANGELOG line added (5.3).
- [x] New questions → `specs/open-questions.md`; deferred work → `specs/backlog.md`; defects found → `specs/bugs.md`.
- [ ] Commit, release, tag, deploy and turning the flag on only after the owner's separate confirmation.

## Deviations From This Plan (implementation, 2026-11-27)

None changes the approved design; each keeps the plan's intent.

- 1.1 `ForwardQuarter.Revenue` and `NetProfit` are nullable. The reader must report a printed-but-empty net profit as
  missing rather than zero; the estimator then treats the quarter as unusable (no estimate), never as a zero quarter.
- 1.3 `ExcludedIndustryExternalIds` has its default `["31"]` in code only, not in `appsettings.json`: the configuration
  binder appends to a default-initialised list, so a copy in `appsettings.json` would read `["31", "31"]` and an
  override could not remove `31`.
- 2.3 One more reason code, `axis_not_renderable`, for a displayable ratio card without geometry (no needle can be
  drawn). Today is the UTC calendar date of the injected clock (`DateOnly.FromDateTime(now.UtcDateTime)`), used for
  the 75-day age guard.
- 2.4 The observer has a second method, `ForwardReadFailed(reasonCode, elapsed, error)`, so a timed-out or failed read
  is logged once per request (warning, no values) while `ForwardEstimated` is still counted once per ratio card.
- 3.1 There were no named line-key constants in Feature 148 code, so `CyclicalWavesV2IncomeStatementLines` holds them
  (revenue 44 → 46 → 124 → 412 → 599, net profit 66, the research `REV_LINES` order; checked against the local line
  definitions). The ticker matching was extracted to `ValuationGaugeTickerMatcher` and is shared with the snapshot reader.
- T2 `forecast_sales` is compared at relative 1e-15 after rounding to whole rials; every other `expected` member is
  exact. The reference rounds a float sum half-to-even and is 1 rial off the exact decimal result for خزامیا, فملی,
  فولاد and فخوز (checked with exact fractions). The fixture is unchanged. Owner decision 2026-11-27 (OQ-152).
- T3/T4 The use-case and composer tests are in the new `ValuationGaugeForwardEstimateTests` instead of
  `ValuationGaugeCardsTests`, so the existing gauge tests stay byte-for-byte unchanged (AC-01). T6 is the new
  `ValuationGaugeForwardEstimateE2ETests` with its own API factory (flag on); the existing E2E and billing suites run
  with the flag off.
- 4.3 The browser check (`scripts/valuation-gauge-cards.browser.mjs`) gained the `forward` and `forward-pinned`
  fixtures, a forward-needle count per fixture (0 for every existing fixture), a check that the ring stays inside the
  dial, and an `--evidence <dir>` argument. Evidence: `evidence/browser/`.

## Verification (2026-11-27, working tree on `develop` `c3457a6e`, uncommitted)

- Backend Release build: 0 warnings, 0 errors in the changed projects.
- Architecture tests: 24 of 24 passed (AC-12).
- Unit tests, full run: 4,257 total, 35 failed. 27 are in `specs/test-baseline.md`. Of the other 8, 7 are timing tests
  (timeouts, regex timeouts, concurrency) that failed while another session's checks loaded the machine and pass when
  run alone; the eighth, `AnalyticalAnswerAndReportScoreTests.FallbackPrompt_IsAnalytical_KeepsKeyFigures_AndScores`,
  fails the same way on clean `HEAD` `c3457a6e` (recorded as BUG-050). Gauge and estimator filter: 205 passed.
- Integration tests, full run: 1,274 total, 254 failed, 159 skipped (integration is not in the baseline). The failing
  classes were run again on clean `HEAD` `c3457a6e`: 176 of the failures fail there too; the remaining 50 (55 cases)
  all pass on this tree when run alone. Gauge, forward and billing suites alone: 42 passed, 1 skipped (the PostgreSQL
  reader test without the connection variable; it passed against the local PostgreSQL container earlier the same day).
- `dotnet ef migrations has-pending-model-changes --context FinancialIngestionDbContext`: "No changes have been made to
  the model since the last migration" (design-time placeholder connection string; no database contacted).
- Frontend: changed-file lint green (`endOfLine: auto`); `npm run typecheck` 37 errors in the 17 baseline files, none in
  the changed files; `npm run build` green; `npm run test` 717 tests, 4 failed in 7 files: `install-card` (timeout under
  load, passes alone) and 6 files that fail the same way on clean `HEAD` (`fa-terminology`, `markdown-message`,
  `monthly-product-trend-chart`, three `quarterly-financial-*.browser.test.mjs`). Gauge and chat-lib tests: 145 passed.
- Browser check `scripts/valuation-gauge-cards.browser.mjs`: every existing fixture (single, all, range-only, stale,
  missing, pinned, sharak, legacy) and the new `forward` and `forward-pinned` pass in all layouts and both themes;
  existing fixtures draw 0 forward needles; reduced motion has no needle animation. Evidence: `evidence/browser/`.
