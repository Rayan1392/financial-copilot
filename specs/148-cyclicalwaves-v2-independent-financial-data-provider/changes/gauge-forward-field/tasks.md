# Tasks: ingest and store the provider `forward` value of the P/E and P/S gauges

Status: Gate B: Approved by the owner on 2026-11-28 («Gate A و Gate B تأیید است»; Gate A in the same message)

## Feature Folder

`148-cyclicalwaves-v2-independent-financial-data-provider/changes/gauge-forward-field`

## Before Starting

- [x] This file's `Status:` line records Gate B approval by the owner.
- [x] Read `design-proposal.md` (the plan is its section 9; no `user-story.md` because no user-visible behaviour changes).

## Implementation Tasks

### 1. Contract and client

- [x] `CyclicalWavesV2Gauge` gains `ExactDecimal? Forward` (optional, default null).
- [x] `CyclicalWavesV2GaugeDataClient.ReadGauge` reads the optional `forward` member; `null` and absent give null, a
  non-number makes the payload invalid.

### 2. Storage

- [x] `CyclicalWavesV2GaugeDetailRow`: `ForwardHigh`, `ForwardMiddle`, `ForwardLow`, `ForwardSign`, `ForwardScale`
  (nullable) and the unmapped `Forward`.
- [x] Configuration: numeric column type and the check constraints `CK_CyclicalWavesV2GaugeDetails_ForwardParts` and
  `CK_CyclicalWavesV2GaugeDetails_ForwardRanges` (one constraint for sign and scale instead of editing the existing
  `_Scales` and `_Signs` constraints).
- [x] Migration `20261128090000_AddCyclicalWavesV2GaugeForwardValue` (additive, nullable, no backfill) and the model
  snapshot.
- [x] `EfCoreCyclicalWavesV2GaugeStore.UpsertGaugeAsync`: `Forward` is compared and written; the BUG-052 rule is kept.

### 3. Tests

- [x] `CyclicalWavesV2GaugeClientTests`: forward present (exact), null and absent, not a number (two forms).
- [x] `CyclicalWavesV2PostgreSqlIntegrationTests.GaugeStore_PersistsTheProviderForwardValueAndTreatsItsChangeAsAnUpdate`:
  no value → value → unchanged → negative value and cleared value; the two constraints exist.
- [x] `Migration_CreatesOnlyTheCyclicalWavesSchemaAndRollsBackCleanly`: exact-decimal part columns 27 → 30.

### 4. Records

- [x] `docs/providers/cyclicalwaves-v2/gauge-equilibrium-api-contract.md`: `forward`, marked as coming from the live
  response of 2026-11-28 and not from the PDF.
- [x] `docs/providers/cyclicalwaves-v2/README.md`: provider forward value is stored, not shown yet.
- [x] `specs/open-questions.md` OQ-136 and `specs/README.md`.
- [x] CHANGELOG: no entry (nothing user-visible).

### 5. Verification

- [x] Backend solution build: 0 warnings, 0 errors.
- [x] Unit tests (CyclicalWavesV2, architecture, valuation gauge): 379 passed.
- [x] `CyclicalWavesV2PostgreSqlIntegrationTests`: 8 passed (local test databases only).
- [x] Architecture test project: 24 passed.
- [x] Gauge card, relative valuation and Feature 148 chat integration tests: 23 passed, 19 failed. The 19 are the known BUG-053 (`specs/bugs.md`): the test host fails at startup with "Capability overrides are allowed only while FinancialData:ChatProvider is NoavaranAmin", before any gauge code runs. Not caused by this change.

### 6. Owner steps (not done by the agent)

- [ ] Commit, release and the production migration.
- [ ] After the first production gauge sync, a read-only count:
  `SELECT "GaugeType", count(*) AS gauges, count("ForwardHigh") AS with_forward, count(*) FILTER (WHERE "ForwardSign" < 0) AS negative FROM cyclicalwaves."GaugeDetails" GROUP BY 1;`

## Deviations

- The plan named the migration `AddGaugeForwardValue`; it is `AddCyclicalWavesV2GaugeForwardValue`, following the
  naming of the previous migration of this schema.
- The migration is hand-written without a `.Designer.cs`, like `20261122090000_AddCyclicalWavesV2RelativeValuation`.

## Verification results

Run on 2026-11-28, local machine, PostgreSQL test databases only. No live provider call. The gauge card E2E classes could not exercise the new column because of BUG-053; the store, the migration and the client are covered by the tests of section 3.
