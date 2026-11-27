# Tasks: Gauge fetch time is refreshed when the vendor values are unchanged (BUG-052)

Status: Gate A: Approved by the owner on 2026-11-27 («با طرح رفع دائمی موافقم») — Gate B: Approved by the owner on 2026-11-27 («Gate B مورد تایید است»)

## Feature Folder

`148-cyclicalwaves-v2-independent-financial-data-provider/changes/fix-gauge-fetch-time-on-unchanged-values`

## Before Starting

- [x] This file's `Status:` line records Gate B approval by the owner.
- [x] Read `design-proposal.md` and BUG-052 in `specs/bugs.md`.

## Implementation Tasks

### 0. Production check (owner; does not block the fix)

- [x] Run on production and record the result in BUG-052 (owner, 2026-11-27 ~20:35 UTC; result in the BUG-052 row):
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

## Verification (2026-11-27)

- The two `GaugeStore_*` tests of `CyclicalWavesV2PostgreSqlIntegrationTests` ran on PostgreSQL (Testcontainers, not
  skipped) and pass. With the new line in `UpsertGaugeAsync` commented out, both fail; the fix was then restored.
- `dotnet test src/backend/FinancialCopilot.sln --configuration Release`:
  - Architecture tests: 24 of 24 pass.
  - Unit tests: 4,261 total, 31 failed. Outside `specs/test-baseline.md`:
    `AnalyticalAnswerAndReportScoreTests.FallbackPrompt_IsAnalytical_KeepsKeyFigures_AndScores` (BUG-050),
    `TelegramMonthlyReportRecognizer133Tests.Corrected_channel_caption_extracts_kaveh_and_ignores_fiscal_year_end` and
    `TelegramChannelFinancialStatementHandler143Tests` (`Telegram_delivery_schedules_fetch_until_configured_delay_and_suppresses_duplicates`,
    `Targeted_recovery_never_replays_completed_or_in_flight_checkpoints`). None of them uses the gauge store; they
    were not compared with a clean worktree.
  - Integration tests: 1,275 total, 869 passed, 247 failed, 159 skipped. There is no integration baseline. In the
    classes around this change (`CyclicalWavesV2*`, `IndustryRelativeValuation*`, `ValuationGauge*`, `IndustryComparison*`:
    80 tests) 58 pass, 3 are skipped and 19 fail; the 19 are the gauge E2E tests that fail at host start-up (BUG-053).
- No entity, configuration or migration is touched.

## Definition Of Done

- [x] `dotnet test src/backend/FinancialCopilot.sln --configuration Release` run; results above. No failure is traced to
  this change; the failures outside the baseline are listed, not compared with a clean worktree.
- [x] No pending model change (no entity or configuration is touched).
- [x] Tasks 1–3 ticked; BUG-052 updated; BUG-053 recorded.
- [x] Task 0 (production check) — run by the owner on 2026-11-27; see BUG-052.

## After release (owner)

- The fix takes effect at the first sync after the deploy: the scheduled one at 23:30 UTC or an Admin-requested run.
  The calculation worker publishes within its 5-minute cadence after that.
- If `IndustryRelativeValuation__SourceFreshnessHours` was raised as a workaround, set it back to 26 only after the
  first full gauge sync that follows the deploy, not right after the deploy: until that sync the stored fetch times are
  still the old ones. On 2026-11-27 it was set back before any sync and every group became `Inconclusive` again; 96
  restored it. Check first:
  `SELECT "GaugeType", count(*) FILTER (WHERE "FetchedAtUtc" > now() - interval '20 hours') AS fresh, count(*) FROM cyclicalwaves."GaugeDetails" GROUP BY 1;`
