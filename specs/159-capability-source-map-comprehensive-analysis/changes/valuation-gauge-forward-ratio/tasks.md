# Tasks: Sapio forward P/E and P/S estimate on the valuation gauge cards

Status: Gate B: Approved by the owner on 2026-11-26 (Gate A on the same day; OQ-148 answered as proposed). Implementation is handed to another agent.

## Feature Folder

`159-capability-source-map-comprehensive-analysis/changes/valuation-gauge-forward-ratio`

## Before Starting

- [ ] This file's `Status:` line records Gate B approval by the owner (see `specs/FEATURE_DEVELOPMENT_WORKFLOW.md`, "Recording Approval").
- [ ] The features in `Depends On` are done (their `tasks.md` definition of done is ticked).
- [ ] Read `user-story.md`, the references it lists, and the relevant `docs/` files.
- [ ] No open question in `specs/open-questions.md` blocks this spec (OQ-148 was answered on 2026-11-26: industry 31 is excluded as a whole).

## Implementation Tasks

### 1. Estimator (Application, pure)

- [ ] 1.1 In `src/backend/FinancialCopilot.Application/FinancialData/ValuationGauges/` add `ForwardRatioContracts.cs`:
  `ForwardRatioInputs` (monthly sales as `(DateOnly MonthEnd, decimal Sales)` oldest first; quarters as
  `(DateOnly PeriodEnd, decimal Revenue, decimal NetProfit)` oldest first; `IndustryExternalId`),
  `ForwardRatioEstimate` (forecast sales, growth factor, margin, forward P/S, forward P/E, latest month, latest
  quarter; each ratio nullable with its reason), `ForwardRatioReasonCodes`, and the port `IForwardRatioInputsReader`
  (`Task<ForwardRatioInputs?> ReadAsync(ValuationGaugeSecurity, string vendorTicker, CancellationToken)`).
- [ ] 1.2 Add `ForwardRatioEstimator` (static, pure): the formula and every guard of user-story requirement 3 that
  depends on the inputs. Constants 12 / 4 / 8 are named and private. Consecutive months and quarters are checked by
  the gap between period ends. Takes the thresholds and `now` as arguments; no clock, no options type inside.
- [ ] 1.3 Add `ValuationGaugeForwardOptions` nested in `ValuationGaugeCardsOptions` (`ForwardEstimate`) with the
  defaults of the user story, and extend `IsValid` (growth range 0 < min < max ≤ 20, age 1–365 days, timeout
  100–30000 ms).

### 2. Card contract and projection (Application)

- [ ] 2.1 `ValuationGaugeContracts.cs`: add `ValuationGaugeForwardEstimate` (`Value`, `Angle`, `Side`,
  `MethodVersion`, `SalesGrowthFactor`, `MarginPercent?`, `LatestSalesMonthEnd`, `LatestQuarterEnd`) and the optional
  `Forward` member at the end of `ValuationGaugeCard`.
- [ ] 2.2 `ValuationGaugeProjector`: given a displayable ratio card and an estimate, compute the forward needle with
  the existing angle and pin logic and attach `Forward`. Nothing else on the card changes.
- [ ] 2.3 `ValuationGaugeCardsUseCase`: when the flag is on and a P/E or P/S card is displayable, read the inputs once
  with `WithBudget(ForwardEstimate.ReadTimeoutMilliseconds)`, run the estimator, pass the result to the projector.
  Timeout or exception → no estimate and one observer call; caller cancellation rethrown. The reader is an optional
  constructor dependency so the flag-off path needs no registration.
- [ ] 2.4 `IValuationGaugeObserver` and `ValuationGaugeTelemetry`: `ForwardEstimated(kind, shown, reasonCode, elapsed)`.
- [ ] 2.5 `ValuationGaugeTextComposer`: the forward line under the current value, only when `Forward` exists.

### 3. Input reader (Infrastructure)

- [ ] 3.1 `ForwardRatioInputsReader` in `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/CyclicalWavesV2/`:
  resolve the ticker as `ValuationGaugeSnapshotReader` does (reuse its matching; extract it if needed, no copy), read
  the last 8 statements' revenue and net profit (discrete quarter) with the Feature 148 line keys, map the ticker ISIN
  to exactly one monthly-report company, and read its last 16 accepted monthly reports summed per month (month itself
  only). `AsNoTracking`, bounded `Take`, no business decision.
- [ ] 3.2 Register it in `CyclicalWavesV2ServiceCollectionExtensions` only when `ForwardEstimate:Enabled` is true.
- [ ] 3.3 Read the company's industry external id for the exclusion guard (default list `["31"]`, OQ-148).
- [ ] 3.4 `appsettings.json`: add the `ForwardEstimate` section with `Enabled: false`. Production reads only
  `appsettings.Production.json` and the server `.env`; the flag is not turned on by this change.

### 4. Frontend

- [ ] 4.1 `src/frontend/src/lib/chat/valuation-gauges.ts`: optional `forward` type and parsing (absent → undefined;
  malformed → ignored, card still renders).
- [ ] 4.2 `valuation-gauge-cards.tsx`: dashed needle with a ring, the «(برآورد ساپیو)» chip, the method sentence in
  «جزئیات», accessible name; nothing drawn without `forward`.
- [ ] 4.3 Fixtures in `__tests__/valuation-gauge-fixtures.ts` and the browser fixture: with forward inside the axis,
  pinned above, and absent.

### 5. Records

- [ ] 5.1 `docs/providers/cyclicalwaves-v2/README.md`: replace «Forward P/E … is never estimated» with the approved
  definition, guards, label and flag. `docs/domain-glossary.md`: «Forward P/E / Forward P/S (Sapio estimate)».
- [ ] 5.2 `specs/README.md` index entry; `specs/backlog.md`: refineries, companies without monthly sales, filling a
  missing quarter from year-to-date, precomputed estimates.
- [ ] 5.3 `CHANGELOG.md` `[Unreleased]` → `### Added`: «برآورد ساپیو از P/E و P/S آینده‌نگر روی کارت‌های گیج ارزش‌گذاری (با عقربهٔ خط‌چین).»
- [ ] 5.4 `specs/open-questions.md`: close OQ-136 when the change ships. (The research folder
  `tools/research/forward-pe-reverse-engineering/` was committed with these specs, OQ-147.)

### 6. Rollout (owner actions, after implementation)

- [ ] 6.1 Read-only coverage count on production (a SQL file added under the research folder): gauge companies with 16
  months and 8 quarters, and how many pass the guards. Report the numbers before the flag is turned on.
- [ ] 6.2 Turn on `ValuationGaugeCards__ForwardEstimate__Enabled` on the server only on the owner's instruction.
  Rollback: turn it off.

## Tests

- [ ] T1 Unit `ForwardRatioEstimatorTests`: formula on a hand-computed case; every guard and reason code; zero and
  negative denominators; margin ≤ 0 keeps the forward P/S; non-consecutive months and quarters; boundary values of the
  growth range and the report age.
- [ ] T2 Unit, reference fixtures: copy `tools/research/forward-pe-reverse-engineering/results/estimator-fixtures.json`
  (25 symbols: exact inputs, thresholds, `as_of`, expected values and reason codes; produced by
  `export_estimator_fixtures.py`) into the unit-test project as a test resource. Assert every `expected` member to two
  decimals, including the three guarded symbols (فگستر `growth_out_of_range`, شپنا `excluded_industry`, آپ
  `insufficient_quarters`). Do not compare with the `tahlilapp_*` members (AC-03).
- [ ] T3 Unit `ValuationGaugeCardsTests`: flag off → payload identical (AC-01); forward inside and pinned (AC-08);
  reader timeout and exception (AC-07); caller cancellation; P/S shown while P/E is not (AC-05); JSON round trip of a
  stored card without `forward` (AC-10).
- [ ] T4 Unit, text composer: the forward line present and absent (AC-09).
- [ ] T5 Integration `ForwardRatioInputsReaderTests` on the test database: month-itself rows only, accepted revision
  only, unique ISIN match, ambiguous and missing match, row bounds.
- [ ] T6 Integration `ValuationGaugeCardsE2ETests`: one answer with the flag on (payload and text), and existing gauge
  and billing tests unchanged (AC-11).
- [ ] T7 Architecture tests green (AC-12).
- [ ] T8 Frontend Vitest `valuation-gauge-cards.test.tsx`: needle, chip, method sentence, absent case, malformed
  `forward`. Run from the uppercase `C:` path.
- [ ] No live OpenAI or provider call in any test.

## Definition Of Done

- [ ] `dotnet test src/backend/FinancialCopilot.sln --configuration Release`: no failure outside `specs/test-baseline.md`.
- [ ] No migration: `dotnet ef migrations has-pending-model-changes --context FinancialIngestionDbContext` reports none.
- [ ] In `src/frontend`: lint of the changed files, `npm run test` and `npm run build` green; `npm run typecheck` adds no error beyond the baseline.
- [ ] Acceptance criteria in `user-story.md` all checked.
- [ ] Reference docs updated (5.1, 5.2) and the CHANGELOG line added (5.3).
- [ ] New questions → `specs/open-questions.md`; deferred work → `specs/backlog.md`; defects found → `specs/bugs.md`.
- [ ] Commit, release, tag, deploy and turning the flag on only after the owner's separate confirmation.
