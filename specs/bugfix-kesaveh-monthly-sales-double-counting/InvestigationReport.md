# Investigation: کساوه monthly sales double-counting

**Verdict: CONFIRMED_ROOT_CAUSE**

## Summary

The chart values are explained by duplicate revision inclusion in snapshot aggregation. The user supplied a read-only query result showing two output-type-0 reports for each affected period, one `IsAccepted=false` and one `IsAccepted=true`, plus monthly line-item totals of 6,338,185 million rial for Shahrivar and 6,088,388 million rial for Mehr. Snapshot selection does not filter acceptance, so it includes both revisions. Applying both reported period totals yields 1,267.637bn and 1,217.6776bn toman, matching the chart values (approximately 1,268bn and 1,218bn). The cause is confirmed from the supplied database evidence and repository code. The connected database available to this workspace is stale, so I could not independently retrieve the target row GUIDs or compare each revision checksum.

The mechanism is verified: the trend snapshot calculator selects monthly type-0 reports without filtering `IsAccepted`, then sums every matching report's line items. The normalizer intentionally retains superseded and pending revisions. A bounded read-only scan found 30 company-period groups with two type-0 report rows and one nonaccepted row in the connected dataset. For the target periods, the user-supplied headers and totals align exactly with the duplicate-revision result; target row GUIDs remain unavailable in this workspace database.

## Reproduction and access evidence

- Reproduction supplied in the request: کساوه (company 212), Shahrivar 1405 ≈ 1,268bn toman and Mehr 1405 ≈ 1,218bn toman, both reported as doubled. The screenshot itself was not surfaced as an inspectable attachment in this session.
- The supplied report ID returned no row in the configured database. The user subsequently reported two output-type-0 rows for each target period end (2026-09-22 and 2026-10-22), with `IsAccepted=f` and `IsAccepted=t` in each pair. The listed one-report line totals reproduce the chart when the calculator includes both reports.
- That database's newest retained monthly-activity payload for company 212 was received **2026-07-30 09:44:22 UTC**. Its latest normalized product-sales month is **1405/04** (Tir). No matching Shahrivar/Mehr report, line items, snapshot, or source payload were available. The database is therefore behind the periods in the reproduction.
- The user later provided the main database host name `postgres`. It does not resolve from this execution host (it is a service name scoped to its container network). The only reachable PostgreSQL listener is `localhost:5432`, which identifies as PostgreSQL 17.2 on Windows. A transaction explicitly set to `READ ONLY` confirmed that this is the stale dataset above: 16 company-212 output-type-0 reports, latest period end 2026-07-22, zero target-period rows, and zero rows for the supplied report ID. The Docker engine was unavailable, so I could not enter the network where `postgres` resolves. No database writes were made.
- The provider's existing `FetchMonthlyReportsAsync` path stores its response through `StorePayloadAsync`. I did not call it because that would write a raw-payload record and conflict with the task's read-only database requirement. No synchronization or repair was run.

## Reconciliation

Persisted monetary values below are million rial. The chart/API conversion is `raw × 0.0001 = billion toman`.

| Period | Provider payload | Stored reports / selected rows | Pre-conversion aggregation | Snapshot/API result | Chart value |
|---|---|---|---:|---:|---:|
| Shahrivar 1405 (1405/06; 2026-08-23–2026-09-22) | User-provided line-item total: **6,338,185** million rial | User reports two type-0 reports for this period, one accepted and one nonaccepted; both are selected by the calculator because it omits the acceptance filter. Per-report totals/checksums are not independently available here. | 6,338,185 × 2 = **12,676,370** million rial | ×0.0001 = **1,267.637bn toman** | Matches reported ≈1,268bn |
| Mehr 1405 (1405/07; 2026-09-23–2026-10-22) | User-provided line-item total: **6,088,388** million rial | User reports two type-0 reports for this period, one accepted and one nonaccepted; both are selected by the calculator because it omits the acceptance filter. Per-report totals/checksums are not independently available here. | 6,088,388 × 2 = **12,176,776** million rial | ×0.0001 = **1,217.6776bn toman** | Matches reported ≈1,218bn |
| Tir 1405 (1405/04; 2026-06-22–2026-07-22), available control | Retained payload checksum `FE9767934FAC4B4BB8E3A96B43C51EF039D9C367E09D3D99BBCFF96ACC26A4D7`, received 2026-07-30. Provider type-0 label is «دوره یک ماهه»; 15 source rows for 1405/04 sum to 4,073,454. | One accepted ProductSales report, output type 0, report ID `b1b80ba1-6bcc-4641-af36-f8be7101bf49`, external ID `ProductSales:212:1405-04:output-0`; 15 persisted line items, all `SourceMultiplicity=1`. | **4,073,454**; persisted line-item sum equals the single report sum. | Snapshot stores **4,073,454**. API conversion: 4,073,454 × 0.0001 = **407.3454bn toman**. | 407.3454bn before display rounding |

The available control month reconciles from provider payload through persisted line items, snapshot, and conversion. It does not establish what happened in 1405/06 or 1405/07.

## OutputType meaning and selection

The values were checked against retained provider response labels and repository provider/metric contracts; `0` is not inferred from its numeric value alone:

| OutputType | Verified provider meaning | Monthly chart treatment |
|---:|---|---|
| 0 | «دوره یک ماهه» (single-month period) | Selected as the monthly bar source |
| 1 | «از ابتدای سال مالی تا کنون» (fiscal-year-to-date) | Excluded from monthly bar; stored separately as YTD |
| 2 | «اصلاحات» (corrections) | Excluded from monthly bar |
| 3 | «از ابتدای سال مالی تا ماه گذشته اصلاح شده» (corrected fiscal-year-to-previous-month) | Excluded from monthly bar |
| 4 | «از ابتدای سال مالی تا ماه گذشته» (fiscal-year-to-previous-month) | Excluded from monthly bar; stored separately |

The available response exposed the exact labels for all five slots. Spec 057 also defines type 1 as fiscal-year-to-date and type 4 as fiscal-year-to-previous-month. The current monthly snapshot selection accepts ProductSales output type 0 (and null for legacy rows); it does not combine types 1–4 into the current-month amount.

## First code-visible incorrect transformation

The first stage that turned retained revisions into an inflated monthly amount was **snapshot report selection**, before chart/API conversion. The working-tree fix now filters this input and the related YTD report selections to accepted rows:

1. `NadpcoApiMonthlyActivityNormalizer.NormalizeAsync` creates report revisions and tracks acceptance. On a newer revision it marks the previous row `IsAccepted=false` / `RevisionStatus="Superseded"`; equal or missing revision evidence creates a `Pending` row. Rows are retained for provenance and review.
2. `CompanyMonthlyActivityTrendSnapshotCalculator.RecalculateAsync` queries type-0 ProductSales reports for the company and period but does not require `IsAccepted=true`. It then builds the monthly line-item collection from every selected report and sums every non-null `SalesAmount`.
3. `MonthlyActivityTrendQueryUseCase` reads the snapshot and converts million rial to billion toman with the documented factor `0.0001`. The conversion is correct. The chart maps the API point values; it does not add another copy of an individual month's value.

For each affected period, the user reports an accepted and a nonaccepted type-0 report; the supplied monthly total doubled and converted with the API factor reproduces the chart amount. This confirms the period-specific mechanism. No acceptance predicate is present in the calculator candidate-report query, so both report rows are eligible.

Other hypotheses checked against code/evidence:

- **YTD/current-period mixing or wrong OutputType:** not in the chart path; type 0 is selected for the monthly amount, and types 1/4 are handled separately. Types 2/3 are also excluded.
- **Different provider reports combined:** snapshot candidates are scoped to `NoavaranCurrentApi`; providers are not summed together here.
- **Repeated line items from replay:** normalizer skips an already-seen report revision/checksum, and it collapses duplicate source identities/fingerprints. The control month's 15 stored rows each have multiplicity 1. This does not rule out a provider payload containing an aggregate row for the unavailable target months.
- **Subtotal/grand-total product rows:** the calculator sums all sales line items without classifying subtotal rows. No target payload was available to determine whether either period contains such rows.
- **Date boundaries:** the report periods are resolved from the Jalali year/month in `JalaliDateResolver`; the available Tir control's persisted dates match 1405/04. Target-period headers were absent, so their stored date mapping cannot be checked.
- **NoavaranEligibleCompanies join multiplication:** the snapshot calculator does not join this view. The configured database has one eligible-company row for external ID 212; it is not the chart aggregation path.
- **Unit conversion:** `0.0001` matches million rial to billion toman and is validated by the control month.

## Exact records and bounded impact check

- **Target reports:** the user-provided query identifies two type-0 rows ending 2026-09-22 and two ending 2026-10-22, each pair with `IsAccepted=f` and `IsAccepted=t`. The request also supplied report ID `d884e5a8-cbd3-4d88-a578-5a0a797607c7`, but that ID is absent from the configured workspace database; exact target row GUIDs, checksums, and per-revision line sums could not be independently retrieved.
- **Available control report:** `b1b80ba1-6bcc-4641-af36-f8be7101bf49`, accepted, output type 0, 15 line items, raw line-item sum and snapshot both 4,073,454 million rial. Source checksum links to retained provider payload listed above.
- **Same-company bounded scan:** company 212 has 16 monthly ProductSales output-type-0/legacy-null reports in the available dataset; all 16 are accepted, with no nonaccepted revisions. Its snapshots stop before 1405/06.
- **Cross-company bounded scan:** among 6,991 Noavaran ProductSales company-period groups with output type 0/null, 30 groups have an accepted and nonaccepted report with line items; all have two report rows. In the reachable local database, the affected companies are **کچاد (ExternalCompanyId 3)** and **کگل (ExternalCompanyId 4)**, with 15 affected periods each, period ends from 2025-04-20 through 2026-07-22. The accepted and nonaccepted line sums match in 29 groups; one کچاد period differs, but both rows are still included by the old selector. This is a list from the reachable, stale local database only; it is not a complete scan of the main `postgres` database and excludes کساوه's later periods, which are absent here.

## Recommendation

**Code fix applied:** added `IsAccepted` filtering to the monthly snapshot candidate reports and its output-type-1/output-type-4 YTD selections. Also applied the acceptance filter in `MonthlyReportAggregateInputSource.SelectAuthoritativeNoavaranMonthlyReports`, which feeds other monthly financial aggregates. Revision rows and line-item history remain intact.

**Data repair:** do not alter or delete report history. Once the authoritative source reports for 1405/06 and 1405/07 are available and inspected, compare their source payloads, accepted/revision states, and line sums. If superseded/pending reports were included, recalculate only those two snapshots after the code fix, from accepted reports. If the provider payload itself contains duplicated totals, repair should follow the confirmed source semantics instead; no repair is justified yet.

**Focused regression tests recommended (not run):**

1. Seed same-company/month/output-type-0 accepted and superseded reports with identical line totals; assert the snapshot equals the accepted report only.
2. Repeat with a `Pending` report and assert it is not included; ensure revision history remains stored.
3. Verify types 1–4 cannot contribute to `MonthlySalesAmount` (retain the existing type-1/type-4 test and extend it for types 2/3 and revisions).
4. Verify ingestion of a newer accepted revision recalculates the snapshot to the new accepted total without summing the superseded total.
5. Keep an integration-level contract asserting monthly amount → snapshot → API conversion for an accepted type-0 report, including a negative adjustment row.

## Relevant code and contract references

- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/CompanyMonthlyActivityTrendSnapshotCalculator.cs:32` — candidate reports now require `IsAccepted`; `:57` onward chooses current report IDs; `:71` sums every selected accepted sales line.
- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NormalizedMetricInputSources.cs:220-245` — Noavaran monthly aggregate selection now also requires accepted ProductSales/ServiceSales report rows.
- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoApiMonthlyActivityNormalizer.cs:60-70` — replay identity uses provider/report/revision fingerprint; `:99-122` retains revisions while marking old/new rows accepted, superseded, rejected, or pending; `:159-196` recalculates monthly snapshots.
- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoApiMonthlyActivityNormalizer.cs:230-265` — duplicate source rows are collapsed according to provider row identity/fingerprint.
- `src/backend/FinancialCopilot.Infrastructure/Financial/Providers/NadpcoApi/NadpcoApiDataProviderClient.cs:149-230` — bounded company-specific monthly provider request and payload storage call; `:570` persists the raw payload.
- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/MonthlyActivityTrendQueryUseCase.cs:12` and `:262-266` — unit conversion factor and implementation; `:141`/`:191` converts chart snapshot values.
- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/EfCoreCompanyMonthlyActivityTrendSnapshotRepository.cs:13-26` — one snapshot row per company/month upsert.
- `src/frontend/src/components/app/monthly-activity-trend-chart-view-model.ts:127-144` — API values map directly into chart point values.
- `specs/057-nadpco-monthly-activity-freshness-and-sales-lookup/tasks.md:84-86` — type 0 monthly, type 1 YTD, type 4 through previous month contract.
- `tests/FinancialCopilot.UnitTests/CompanyMonthlyActivityTrendSnapshot076Tests.cs:63-89` — existing type-1/type-4 monthly-bar exclusion test.

## Remaining uncertainty

The cause is confirmed by the supplied target-period headers/totals and the verified selection/aggregation code. Remaining evidence unavailable to this workspace is the exact target report GUIDs, both revision checksums and line-item sets, and original provider payloads. Those would establish whether both revisions contain identical rows and allow a same-database end-to-end replay. The configured database itself remains behind the reported periods.


