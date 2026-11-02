# Investigation: Kesaveh monthly sales double counting

**Verdict: CONFIRMED_ROOT_CAUSE**

The historical snapshot calculator included both accepted and rejected revisions. The chart reads persisted snapshots, so the existing inflated values remained until those snapshots were rebuilt. Production data is now verified and the affected snapshots have been recalculated.

## Reproduction and reconciliation

The production database confirmed two `ProductSales`, `OutputType=0` rows for each Kesaveh period: one `Accepted`, one `Pending` with `IsAccepted=false`. Both revisions have the same line-item sales sum. Retained original provider payloads independently reproduce those sums from their `productSaleValue` fields.

Amounts before conversion are million rial; chart/API values are billion toman using `raw * 0.0001`.

| Period | Rejected report / line items / sum | Accepted report / line items / sum | Snapshot before rebuild | Snapshot after rebuild | API amount after rebuild |
|---|---|---|---:|---:|---:|
| Shahrivar 1405, end 2026-09-22 | `14482821-5d97-4bc5-b7a7-cc70c66012f5`, 11 lines, 6,338,185 | `c80439b5-9c3c-4f8d-9ee4-3ec07d9ea579`, 15 lines, 6,338,185 | 12,676,370 | 6,338,185 | 633.8185 |
| Mehr 1405, end 2026-10-22 | `74b86d22-24a0-4c9f-97e9-d5f5f2315d45`, 15 lines, 6,088,388 | `d884e5a8-cbd3-4d88-a578-5a0a797607c7`, 11 lines, 6,088,388 | 12,176,776 | 6,088,388 | 608.8388 |
| Mordad 1405, end 2026-08-22, control | One accepted report `90a24f3e-13dd-4345-9f50-78fb78e964c0`, 15 lines, 4,978,010 | N/A | 4,978,010 | 4,978,010 | 497.8010 |

Raw provider payloads are retained for both revisions in each affected month. Their type-0 source row counts match persisted line-item counts, and summing provider `productSaleValue` reproduces the persisted `SalesAmount` for each revision. Their checksums match the report revision fingerprints:

- Shahrivar rejected/accepted: `5BDCFF0D81B39895424BB4B75E14D53551D01CE11E2372BCC91C93E157F4D5B5` / `18C7B7501F51D4625D14388D270C42F941C5BFFF6E88E764E7E1196D6B62BD7B`.
- Mehr rejected/accepted: `5880B55FD66D3BCA6A2C0ADAEDEA580EDCCB88D6CA326030836E414CED9B112E` / `358C1A2A48AE49B1969812200136CE7E0E12CC939D03CBB5F9D1FAA0FA53D1D5`.

Each revision independently totals the same month amount. The defect was summing the two report revisions together; repeated ingestion of the same checksum is skipped by the normalizer, while these revisions have different checksums. A subtotal row is not needed to explain the measured factor-of-two; whether provider product rows have any additional business-semantic subtotal issue is separate. Join multiplication, OutputType mixing, and unit conversion are excluded by the verified aggregation path and exact reconciliation.

## First incorrect transformation and chart path

1. `NadpcoApiMonthlyActivityNormalizer` retains rejected/pending revisions for history. It also recalculates trend snapshots when it processes monthly reports (`NadpcoApiMonthlyActivityNormalizer.cs:97-128, 187-196`).
2. Before commit `13430cf`, `CompanyMonthlyActivityTrendSnapshotCalculator` selected reports by provider/company/type/period without `IsAccepted`, then summed their line items. This was the first incorrect transformation. The current calculator requires accepted rows for monthly, YTD, and prior-month YTD inputs (`CompanyMonthlyActivityTrendSnapshotCalculator.cs:32-38, 103-110, 135-142`).
3. The chart query reads persisted `CompanyMonthlyActivityTrendSnapshots` and converts million rial to billion toman. It does not fetch or aggregate `MonthlyReports` at response time (`MonthlyActivityTrendQueryUseCase.cs:43-47, 102-114, 141-142, 189-192, 262-266`; `EfCoreCompanyMonthlyActivityTrendSnapshotRepository.cs:34-90`). Thus deploying the filter alone did not repair previously calculated rows.
4. The existing trend backfill skips existing snapshots unless `ForceRebuild` is enabled (`CompanyMonthlyActivityTrendSnapshotBackfillService.cs:109-137`).

Provider output contracts map type 0 to a one-month report, type 1 to fiscal YTD, and type 4 to fiscal YTD through the previous month. The monthly chart uses type 0; YTD types are stored separately. The target-period source payloads and persisted reports both confirmed type 0 for these rows.

## Production snapshot rebuild performed

Using the user-provided production connection, a read-only preflight found 976 `IsAccepted=false` Noavaran report rows across 202 companies and 215 unique company-month keys. Every key had exactly one existing trend snapshot and an accepted monthly source with line items. No target lacked accepted input, and no duplicate snapshot key was found.

I recalculated the 215 unique snapshots, in period order, by invoking the repository's existing `CompanyMonthlyActivityTrendSnapshotCalculator` and EF snapshot repository against accepted persisted report data. This did not call the provider or modify report headers/line items. Results:

- 215 snapshots processed; 215 succeeded; 0 failed.
- All 215 resulting `MonthlySalesAmount` values matched the independently computed accepted-only report-line sums; 0 mismatches.
- A read-only verification after the rebuild still found all 976 rejected report rows, confirming their history was retained.
- Kesaveh snapshots now contain 6,338,185 (1405/06) and 6,088,388 (1405/07) million rial, calculated at 2026-11-01 12:45 UTC.

The company-level inventory is in [AffectedCompanies.csv](AffectedCompanies.csv). It lists 202 companies with rejected report rows and their affected month counts; presence in this inventory does not mean every rejected row independently inflated a monthly bar (for example, YTD revisions are not part of the monthly amount).

## Bounded impact and verification

The database query `MonthlyReports WHERE IsAccepted=false` returned only `NoavaranCurrentApi` rows, with rejected rows for ProductSales output types 0-4 and ServiceSales with null output type. Their 215 unique company-months were the bounded rebuild scope. All had existing snapshots and accepted source line items. Other providers were not found in the rejected-row result.

**Timeline:** the snapshot calculator was introduced without an acceptance predicate in commit `c04612c6` on 2026-06-24; the predicate was added in commit `13430cfb` on 2026-11-01. Production deployment timing was not available. In production, the earliest synchronized rejected ProductSales type-0 row with an accepted counterpart is company 14, period ending 2026-09-22 (1405/06), synchronized 2026-10-05 05:50:45 UTC. Both rows have 10 line items and total 1,034,141 million rial. The earliest historical period with an accepted/rejected type-0 pair is 1404/11, ending 2026-02-19; its rejected row was synchronized on 2026-10-12. Since the rebuild replaced the old snapshots, the exact first timestamp when a wrong snapshot was written cannot now be recovered.

The unaffected Mordad control has one accepted ProductSales type-0 report; its 15 line items and stored snapshot both equal 4,978,010 million rial. Noavaran eligible-company joins are not used by the trend calculator. The monthly chart calculation uses the expected unit factor and does not add the value a second time.

## Code changes and regression coverage

The accepted-only trend calculator filter is present in commit `13430cf`. This work also applies `IsAccepted=true` to product revenue mix calculation, monthly disclosure feed selection, and the related snapshot/revenue-mix/monthly-backfill candidate discovery paths:

- `CompanyProductRevenueMixCalculator.cs:26-33`
- `CompanyDisclosureFeedRepository.cs:161-172`
- `CompanyMonthlyActivityTrendSnapshotBackfillService.cs:51-60`
- `ProductRevenueMixBackfillService.cs:31-40`
- `MonthlyActivityBackfillCoordinator.cs:554-580`

The infrastructure project built successfully with zero warnings and zero errors. Tests were not run. No deployment was performed.

Focused regression tests should cover: accepted plus pending/superseded type-0 revisions; rejected rows with different line sums excluded from snapshots and product revenue mix; separation of OutputTypes 0/1/4; ingestion and forced-backfill recalculation; and one-time snapshot-to-chart unit conversion.

## Remaining limitations

Production report headers, line items, snapshots, and retained raw payloads were verified. The application code was not deployed from this workspace, so a live API response after the database update was not exercised here. Refresh the chart/API request to read the rebuilt snapshots. If the old values still appear, check for an API instance or cache serving stale data.
