# Tasks: the provider's forward P/E and P/S on the valuation gauge cards

Status: Gate B: Approved by the owner on 2026-11-28 («Gate A and Gate B را تایید میکنم»; Gate A in the same message)

## Feature Folder

`159-capability-source-map-comprehensive-analysis/changes/valuation-gauge-provider-forward`

## Before Starting

- [x] This file's `Status:` line records Gate B approval by the owner. The plan approved with it is sections 3 to 5
  of `design-proposal.md`; the owner approved both gates in one message after that proposal was presented.
- [x] Dependencies: `gauge-forward-field` is implemented locally (not committed); `valuation-gauge-forward-ratio` is
  released.
- [x] OQ-160 to OQ-163 answered as proposed on 2026-11-28.

## Implementation Tasks

### 1. Application

- [x] `RatioGaugeSnapshot.Forward` (optional text).
- [x] `ValuationGaugeForwardEstimate`: `Source`; `MethodVersion`, `SalesGrowthFactor`, `LatestSalesMonthEnd`,
  `LatestQuarterEnd` become optional and are omitted from JSON when null; `IsProviderValue()`.
  `ValuationGaugeForwardSources`, `ProviderForwardReasonCodes`.
- [x] `ValuationGaugeProjector.WithProviderForward`; the placement (rounding, angle, pin) is shared with the estimate.
  The estimate now writes `source: sapio`.
- [x] `ValuationGaugeCardsOptions.ProviderForward` (`Enabled`, default true), validated.
- [x] `ValuationGaugeCardsUseCase`: provider value attached while projecting a ratio card; the estimate skips cards
  that already have a forward value and reads no input when none is left.
- [x] `IValuationGaugeObserver.ProviderForwardProjected`.
- [x] `ValuationGaugeTextComposer`: wording by source.

### 2. Infrastructure and configuration

- [x] `ValuationGaugeSnapshotReader` fills `Forward` from `GaugeDetails`.
- [x] `ValuationGaugeTelemetry`: tag `source` on `valuation_gauge.forward_estimates`.
- [x] `appsettings.json`: `ValuationGaugeCards:ProviderForward:Enabled` true.

### 3. Frontend

- [x] `valuation-gauges.ts`: `source` on the forward type, optional method members, `forwardLabel`,
  `forwardDetailsText`, `PROVIDER_FORWARD_LABEL`, `PROVIDER_FORWARD_NOTE`.
- [x] `valuation-gauge-cards.tsx`: chip, needle name, dial summary and «جزئیات» use the wording of the source;
  `data-source` on the chip and the sentence; the dial summary names the forward value also without a current value.

### 4. Tests

- [x] `ValuationGaugeProviderForwardTests` (18 cases): AC-01 to AC-08, AC-10, payload round trip, stored value without
  a source, defaults.
- [x] `valuation-gauge-cards.test.tsx`: three cases for AC-09.
- [x] Existing `ValuationGaugeForwardEstimateTests` and `ForwardRatioEstimatorTests` unchanged except the new observer
  member in a test double.

### 5. Records

- [x] `docs/providers/cyclicalwaves-v2/README.md`, `docs/domain-glossary.md`, `specs/README.md`.
- [x] `specs/open-questions.md`: OQ-160 to OQ-163 answered; OQ-136 answered, closes when released.
- [x] CHANGELOG `[Unreleased]`.

### 6. Owner steps (not done by the agent)

- [ ] Commit, release and deployment, after `gauge-forward-field` and its migration.
- [ ] After the first production gauge sync: compare the stored forward values with TahlilApp's page for a few
  symbols (assumption A1), and run the count in `148-…/changes/gauge-forward-field/tasks.md`.
- [ ] Decide the production value of `ValuationGaugeCards__ForwardEstimate__Enabled` (the fallback estimate).

## Verification results (2026-11-28, local)

- Backend solution build: 0 warnings, 0 errors.
- Backend unit tests, full run: 4,284 total, 4,256 passed, 28 failed. All 28 are outside this change: 23 test methods
  listed in `specs/test-baseline.md` and BUG-050 (`AnalyticalAnswerAndReportScoreTests.FallbackPrompt_…`).
  Gauge-related subset (ValuationGauge, ForwardRatio, CyclicalWavesV2): 452 passed.
- Architecture tests: 24 passed.
- Frontend: `valuation-gauge-cards.test.tsx` 49 passed; `src/lib/chat` and the card tests together 159 passed.
  ESLint clean on the three changed files. `tsc --noEmit`: 37 errors, the baseline number, none in the changed files.
- Not run: the gauge card E2E classes, which fail at host startup because of BUG-053 before any gauge code runs. No
  live provider or LLM call. Not checked on a real device or in a browser.

## Deviations

- The proposal said a provider value needs "a displayable ratio card with a dial". The implementation does not also
  require a current value, so a loss-making company with a provider forward P/E shows the dashed needle alone (AC-05).
- `marginPercent` stays in the payload as null for a provider value (it was already nullable); the other method
  members are omitted.
- The use case reports the provider value through a new observer member instead of a new parameter on
  `ForwardEstimated`, so existing observers keep their signature.
