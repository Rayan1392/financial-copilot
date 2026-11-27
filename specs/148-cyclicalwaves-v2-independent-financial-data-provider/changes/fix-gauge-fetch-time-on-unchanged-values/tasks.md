# Tasks: Gauge fetch time is refreshed when the vendor values are unchanged (BUG-052)

Status: Gate A: Approved by the owner on 2026-11-27 («با طرح رفع دائمی موافقم») — Gate B: Approved by the owner on 2026-11-27 («Gate B مورد تایید است»)

## Feature Folder

`148-cyclicalwaves-v2-independent-financial-data-provider/changes/fix-gauge-fetch-time-on-unchanged-values`

## Before Starting

- [x] This file's `Status:` line records Gate B approval by the owner.
- [x] Read `design-proposal.md` and BUG-052 in `specs/bugs.md`.

## Implementation Tasks

### 0. Production check (owner; does not block the fix)

- [ ] Run on production and record the result in BUG-052:
  `SELECT "GaugeType", min("FetchedAtUtc"), max("FetchedAtUtc"), count(*) FROM cyclicalwaves."GaugeDetails" GROUP BY 1;`
  and
  `SELECT "Status", count(*), max("CalculatedAtUtc"), max("ValidUntilUtc") FROM "IndustryRelativeValuationCalculations" WHERE "InputSourceVersion" = 'cyclicalwaves-v2' AND "CalculatedAtUtc" > now() - interval '1 day' GROUP BY 1;`

### 1. Store

- [x] `EfCoreCyclicalWavesV2GaugeStore.UpsertGaugeAsync`: when the fetched gauge equals the stored row, set
  `FetchedAtUtc = now` and still return `false` (outcome `Unchanged`). Changed and removed gauges behave as today.

### 2. Tests

- [x] `CyclicalWavesV2PostgreSqlIntegrationTests.GaugeStore_PersistsFullRangeExactValuesAndDoesNotDuplicateUnchangedRows`:
  after the second, unchanged `ApplyGaugeDetailsAsync` the outcome is `Unchanged`, there is still one row and its
  `FetchedAtUtc` is the second call's time.
- [x] Regression test for the reported failure:
  `CyclicalWavesV2PostgreSqlIntegrationTests.GaugeStore_UnchangedFetchKeepsRelativeValuationInputsFresh` — gauges
  applied at T, applied again unchanged at T + 24 h, `CyclicalWavesV2RelativeValuationInputReader` read at T + 30 h with a
  26 h freshness returns the P/E and P/S observations, valid until T + 24 h + 26 h. Placed next to the store test (it
  needs the store and the reader only), not in `IndustryRelativeValuationSnapshotConsumptionPostgreSqlTests`.

### 3. Documents

- [x] `docs/providers/cyclicalwaves-v2/README.md`: state that `GaugeDetails.FetchedAtUtc` is the time of the last
  successful fetch, also when the values did not change.
- [x] `specs/bugs.md` BUG-052 status; `specs/README.md` index entry for this change.
- [x] `CHANGELOG.md` `[Unreleased]`: a Persian user-facing line (industry comparison and gauge cards stay available on
  days the market is closed).

## Definition Of Done

- [ ] `dotnet test src/backend/FinancialCopilot.sln --configuration Release`: no failure outside `specs/test-baseline.md`
  (PostgreSQL tests are skippable; report whether they ran).
- [ ] No pending model change (no entity or configuration is touched).
- [ ] Tasks 1–3 ticked; BUG-052 updated.

## After release (owner)

- The fix takes effect at the first sync after the deploy: the scheduled one at 23:30 UTC or an Admin-requested run.
  The calculation worker publishes within its 5-minute cadence after that.
- If `IndustryRelativeValuation__SourceFreshnessHours` was raised as a workaround, set it back to 26.
