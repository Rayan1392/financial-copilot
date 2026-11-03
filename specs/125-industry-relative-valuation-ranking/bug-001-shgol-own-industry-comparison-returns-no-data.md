# BUG-001 — Own-industry-group comparison returns NoData despite usable persisted data

## Status

`OPEN — ROOT CAUSE CONFIRMED; GROUP-COHORT CORRECTION ADDED`

- Severity: High
- Priority: P1
- Component: Feature 125 cohort identity, publication/read path, and Feature 127 acquisition handoff
- Detected: 2026-08-17
- Reproduction symbol: `شگل`
- Affected capability: `symbol_vs_industry_relative_valuation`

## Summary

The query below reaches the supported Feature 125 capability but returns the generic `NoData`
response:

```text
نماد شگل را با صنعت خودش مقایسه کن
```

```text
درخواست شما پشتیبانی می‌شود، اما داده قابل استفاده‌ای برای آن در بازه موجود پیدا نشد.
```

The response is incorrect for the inspected database state. `شگل` is a canonical, eligible company,
has persisted P/E, P/S, and equilibrium inputs, and is present in the latest broad-industry
calculation. The intended comparison cohort is not every company in `محصولات شیمیایی`; it is the
eligible `تولید محصولات آرایشی و بهداشتی` group identified by exact `GroupId`. The read also fails
because there is no calculation that is both `Published` and `IsSelectedCurrent`.

This is not primarily a prompt-recognition or symbol-resolution failure. The generic sentence is
produced after the semantic capability is accepted and its read repository returns `null`.

## Reproduction

1. Run the API and Worker with the repository's current default configuration.
2. Submit `نماد شگل را با صنعت خودش مقایسه کن` through `POST /api/ai/v1/query`.
3. Observe the supported-but-no-data response above.

## Expected result

Feature 125 should derive `شگل`'s canonical `GroupId`, read the latest selected published calculation
for that group, return the persisted row for `شگل`, and present its rank and P/E, P/S, and
equilibrium comparisons against benchmarks calculated only from eligible members of that group.

## Actual result

The Feature 125 executor returns `CapabilityExecutionStatus.NoData`; the dialogue policy renders the
generic supported-but-no-data sentence.

## Confirmed database evidence

Evidence was collected read-only from the configured development PostgreSQL database on
2026-08-17.

### Canonical identity and input availability

- `شگل` resolves to company `گلتاش`, provider `NoavaranCurrentApi`.
- Broader industry metadata: `محصولات شیمیایی` (external industry id `11`).
- Authoritative comparison group:
  - `GroupId`: `97ac765e-c5d6-4e5d-b9de-eb9e0b4e806c`;
  - `GroupTitle`: `تولید محصولات آرایشی و بهداشتی`.
- The company is present in `NoavaranEligibleCompanies`.
- Persisted snapshots exist for all required metric types:
  - Equilibrium: 1 snapshot;
  - P/E: 2 snapshots;
  - P/S: 2 snapshots.
- Successful `Changed`/`NoChange` acquisition checks exist for all three metric types.

### Calculation state

The latest legacy calculation for the broad `محصولات شیمیایی` industry is dated 2026-08-15 and has:

- status `Inconclusive`;
- `IsSelectedCurrent = false`;
- 210 company result rows, including `شگل`;
- 176 selected source-evidence entries;
- all three benchmarks ready:
  - Equilibrium: `Ready`, clean count 49;
  - P/E: `Ready`, clean count 57;
  - P/S: `Ready`, clean count 55.

The canonical `Companies` table contains 210 companies in this broad industry, while the
authoritative `NoavaranEligibleCompanies` view contains 74. Sixty-five companies in the industry
have a persisted CyclicalWaves snapshot. These broad-industry counts are not the correct comparison
population.

The corrected candidate cohort contains exactly 10 eligible rows with the same `GroupId`:

| Symbol | Company |
|---|---|
| `پاکشو` | گروه صنعتی پاکشو |
| `ساینا` | صنایع بهداشتی ساینا |
| `شپارس` | بین المللی محصولات پارس |
| `شپاکسا` | پاکسان |
| `شتولی` | تولی پرس |
| `شکف` | کف |
| `شگل` | گلتاش |
| `شوینده` | مدیریت صنعت شوینده توسعه صنایع بهشهر |
| `قرن` | پدیده شیمی قرن |
| `کیمیاتک` | دارویی آرایشی و بهداشتی آریان کیمیا تک |

The authoritative diagnostic query is:

```sql
SELECT "IndustryId", "IndustryTitle", "GroupId", "GroupTitle",
       "CompanyIsin", "CompanySymbol", "Name"
FROM "NoavaranEligibleCompanies"
WHERE "GroupId" = '97ac765e-c5d6-4e5d-b9de-eb9e0b4e806c';
```

### Freshness and acquisition state

- At inspection time, there were zero successful acquisition checks in the configured 26-hour
  freshness window.
- The latest successful check for `شگل` was 2026-08-15 05:44:04 +03:30.
- The latest successful check for any company was 2026-08-15 15:20:56 +03:30.
- `CyclicalWavesDataAcquisition.Enabled` is `false` in both API and Worker base configuration.
- The Worker registers separate acquisition and calculation hosted services, but the disabled
  acquisition service cannot refresh the persisted evidence consumed by the calculation service.

## Root cause

The defect is a chain of three blocking conditions.

### RC-0 — Feature 125 uses the wrong cohort key

The existing design and implementation group calculations, benchmarks, ranks, publication pointers,
watch state, semantic same-cohort validation, and reads by `IndustryId`/`IndustryTitle`. That level is
too broad for the requested comparison. For `شگل`, it mixes multiple industry groups inside
`محصولات شیمیایی` instead of limiting the calculation to
`97ac765e-c5d6-4e5d-b9de-eb9e0b4e806c` / `تولید محصولات آرایشی و بهداشتی`.

The authoritative Feature 125 cohort key must be `GroupId`, with `GroupTitle` used for presentation.
`IndustryId` and `IndustryTitle` remain broader metadata only. Group values must not be stored in
industry-named fields because existing industry-keyed history would become ambiguous or corrupted.

### RC-1 — The published-snapshot gate used full-catalog completeness

Within the already incorrect broad-industry cohort, the baseline calculation input was built from
every canonical `Companies` row in the industry. The
baseline SourceBarrier then required one usable selection for every company and every one of the
three metrics:

```text
required selections = canonical industry members × 3
                    = 210 × 3
                    = 630
```

Only 176 selections participated. Although the P/E, P/S, and equilibrium benchmarks were all
independently `Ready`, `SourceBarrier.IsComplete` was false. The snapshot writer defines publication
as:

```text
SourceBarrier.IsComplete AND every benchmark is available
```

It therefore persisted an `Inconclusive` calculation and did not set `IsSelectedCurrent`. This
coverage gate contradicts the corrected Feature 125 rules: eligibility must begin at
`NoavaranEligibleCompanies`, a company is admitted when at least one metric is usable, benchmark
populations are metric-specific, and SourceBarrier represents selected provenance rather than
full-catalog completeness.

### RC-2 — The pending remediation cannot self-heal without fresh acquisition

The working tree contains uncommitted remediation that changes eligibility and SourceBarrier
semantics, but it still groups by `IndustryId` and therefore does not implement RC-0. Even if its
coverage changes are built locally, the calculation worker cannot publish a replacement snapshot
from the current database because every successful acquisition check is outside the 26-hour
freshness window. The persisted acquisition owner is disabled by default, so no fresh check is
created and the input builder produces no calculable group input.

Consequently, the old `Inconclusive` row remains the latest calculation and no row becomes selected
current. The read repository deliberately queries only rows where `Status == "Published"` and
`IsSelectedCurrent == true`, so it returns `null`, which becomes the observed `NoData` response.

## Why the earlier fix was insufficient

The current regression test for the exact “own industry” phrasing verifies only that:

- the query is classified as `symbol_vs_industry_relative_valuation`;
- `شگل`-like input resolves to one canonical company and its broad industry;
- no clarification is requested.

The test actually locks in the wrong expectation by asserting an `Industry` slot derived from
`IndustryId`; it does not assert `GroupId` or the 10-symbol cohort. It also does not execute
`IndustryRelativeValuationReadRepository`, does not seed or assert a selected
`Published` calculation, does not run the API endpoint, and does not exercise acquisition freshness.
The targeted Feature 125 tests therefore pass while the user-visible request still returns
`NoData`.

There is also a documentation/runtime mismatch. The final acceptance review describes an Option B
`NadpcoScheduledSyncWorker` trigger, while the current Worker registers separate CyclicalWaves
acquisition and Feature 125 calculation workers. The acquisition worker is disabled in base
configuration. Operational readiness was therefore accepted without proving the currently active
trigger/configuration combination.

## Relevant code and documentation

- The input builder currently constructs and groups membership by `IndustryId`:
  `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/IndustryRelativeValuationCalculationInputBuilder.cs:42`
  and `:113`.
- The semantic adapter currently validates same-cohort membership through `IndustryId`:
  `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Adapters/IndustryRelativeValuationSemanticResolver.cs:65`.
- The routing regression explicitly asserts an `Industry` slot rather than a group identity:
  `tests/FinancialCopilot.UnitTests/Feature125SemanticRoutingRegressionTests.cs:36`.
- The read repository accepts only selected published calculations:
  `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/IndustryRelativeValuationReadRepository.cs:21`.
- A missing selected published calculation returns `null`:
  `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/IndustryRelativeValuationReadRepository.cs:24`.
- Publication requires both a complete SourceBarrier and all benchmarks:
  `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/IndustryRelativeValuationCalculationSnapshotWriter.cs:63`.
- Only published rows become selected current:
  `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/IndustryRelativeValuationCalculationSnapshotWriter.cs:102`.
- The source freshness window is 26 hours:
  `src/backend/FinancialCopilot.Worker/appsettings.json:56`.
- Persisted CyclicalWaves acquisition is disabled:
  `src/backend/FinancialCopilot.Worker/appsettings.json:276-277`.
- The current hosted-service registrations are separate:
  `src/backend/FinancialCopilot.Worker/Program.cs:124-125`.
- The routing-only regression test starts at:
  `tests/FinancialCopilot.UnitTests/Feature125SemanticRoutingRegressionTests.cs:27`.
- The stale Option B runtime statement is at:
  `specs/125-industry-relative-valuation-ranking/final-acceptance-review.md:139`.

## Required remediation

No product-code remediation is included in this bug record. A complete fix must address the entire
chain, not only semantic routing:

1. Replace the Feature 125 cohort identity with explicit `GroupId`/`GroupTitle` across input
   assembly, calculation identity, persistence/current selection, correction lineage, watch identity,
   semantic same-cohort validation, read contracts, and presentation.
2. Preserve historical industry-keyed rows and use an additive, reviewed migration/contract; never
   alias group values into `IndustryId`/industry-title fields.
3. Land and review the corrected eligibility, admitted-membership, metric-specific benchmark, and
   provenance-only SourceBarrier behavior within each group.
4. Define and enable the actual persisted acquisition owner for the target environment, including
   first-run/backfill behavior and health visibility.
5. Refresh all required persisted acquisition evidence and run Feature 125 calculation after the
   refresh.
6. Ensure a `Published`, `IsSelectedCurrent = true` calculation is created for the cosmetics and
   hygiene products group and contains `شگل`.
7. Align the operational documentation and release gate with the currently registered worker flow.
8. Add an end-to-end regression that crosses routing, canonical company/group resolution,
   same-`GroupId` isolation, repository execution,
   publication selection, and final API response.

## Acceptance criteria

- [ ] The exact query `نماد شگل را با صنعت خودش مقایسه کن` returns an executed Feature 125 response,
      not `NoData`, when the persisted inputs satisfy the feature's freshness policy.
- [ ] The result identifies `GroupTitle = تولید محصولات آرایشی و بهداشتی` and the persisted `شگل`
      member comparison.
- [ ] The candidate cohort is derived by exact
      `GroupId = 97ac765e-c5d6-4e5d-b9de-eb9e0b4e806c` and contains only the 10 listed eligible
      symbols before usable-metric admission.
- [ ] Eligible companies with the same `IndustryId` but another `GroupId` do not affect benchmarks,
      ranks, SourceBarrier, publication, watch state, or reads.
- [ ] A calculation with all three ready benchmarks publishes even when eligible non-members or
      partially covered members lack some metrics.
- [ ] Canonical `Companies` rows outside `NoavaranEligibleCompanies` do not expand calculation
      membership or SourceBarrier requirements.
- [ ] `GroupId`/`GroupTitle` are persisted and transported explicitly; industry-named fields retain
      their historical meaning.
- [ ] SourceBarrier selected/required counts describe only materialized participating provenance.
- [ ] Startup/deployment configuration guarantees that acquisition refresh precedes or feeds the
      calculation, or explicitly blocks readiness with an actionable health error.
- [ ] The integration fixture includes a selected published calculation and fails if the read
      repository returns `null` for an admitted member.
- [ ] An API-level regression asserts the final Persian response, not only the validated semantic
      frame.
- [ ] Release evidence includes a database assertion that the target group has exactly one
      selected current published calculation after refresh/recalculation.

## Verification performed during investigation

The following targeted unit groups passed in the current dirty working tree:

```text
Feature125SemanticRoutingRegressionTests
IndustryRelativeValuationSemanticTests
IndustryRelativeValuationSemanticAdapterTests
IndustryRelativeValuationSnapshotConsumptionTests
IndustryRelativeValuationSourceBarrierTests

35 passed, 0 failed
```

This passing result is evidence of the test-coverage gap, not evidence that the user-visible defect
is fixed. No product code or database rows were modified during this investigation.
