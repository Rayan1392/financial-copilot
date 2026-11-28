# Tasks: equilibrium card price from today's intraday trades

Status: Gate B: Approved by the owner on 2026-11-28 («با هر دو مورد موافقم، گیت A و B تأیید است»; Gate A in the same message)

## Feature Folder

`159-capability-source-map-comprehensive-analysis/changes/equilibrium-intraday-price`

## Before Starting

- [x] This file's `Status:` line records Gate B approval by the owner.
- [x] Read `design-proposal.md` and `user-story.md` in this folder.
- [x] OQ-187 and OQ-188 are answered.

## Implementation Tasks

- [x] 1. `ValuationGaugeCardsOptions.DailyPriceReadyTehran` ("19:00") with validation; API `appsettings.json`.
- [x] 2. `ValuationGaugeQuoteSelection` (Application, pure): requirements 4 to 7.
- [x] 3. `PersistedMarketDataProvider.GetSessionTradeCandidatesAsync(isin, now)`: requirements 2 and 3.
- [x] 4. `ValuationGaugeQuoteReader`: read the candidates, map them to `ValuationGaugeQuote` (requirement 8), return the
  policy's choice; no longer uses `MarketViewOptions`.

## Tests

- [x] 5. Unit tests of `ValuationGaugeQuoteSelection`: AC-01 to AC-05, the exact threshold minute, no candidate.
- [x] 6. Unit tests of `ValuationGaugeQuoteReader` over the EF Core in-memory provider: today's old intraday row beats
  yesterday's daily before the threshold; today's daily row wins after it; late Wednesday; Thursday; rows after `now`
  ignored; zero prices ignored.
- [x] 7. Options validation test (AC-06).

## Records

- [x] 8. `docs/providers/cyclicalwaves-v2/README.md`: price rule of the equilibrium card.
- [x] 9. `specs/README.md` index, `specs/open-questions.md` OQ-187 and OQ-188, `specs/backlog.md` Feature 125 entry,
  `CHANGELOG.md` `[Unreleased]`.

## Definition Of Done

- [x] Build green; unit tests green outside `specs/test-baseline.md` and open bugs (2026-11-28: 4,423 passed, the same
  29 pre-existing failures as before the change; architecture 24/24).
- [~] Integration tests of the gauge cards with Docker available (AC-07), or noted as not run. 2026-11-28: the 19 gauge
  E2E tests fail at host start (`CapabilitySources` override with `ChatProvider` `CyclicalWaves`), the open BUG-053, so
  they verify nothing here; the other 62 tests of the run (CyclicalWaves v2 admin and PostgreSQL, relative valuation)
  pass.
- [x] Acceptance criteria AC-01 to AC-07 checked: AC-01 to AC-06 by `ValuationGaugeEquilibriumPriceTests` (policy and
  reader over the trade tables); AC-07: `GetCanonicalTradePricesAsync` is unchanged and its callers' tests pass.
