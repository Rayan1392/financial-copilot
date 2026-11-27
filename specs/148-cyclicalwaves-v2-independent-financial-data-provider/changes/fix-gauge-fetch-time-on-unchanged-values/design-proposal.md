# Design Proposal: Gauge fetch time is refreshed when the vendor values are unchanged (BUG-052)

Status: Gate A: Approved by the owner on 2026-11-27 («با طرح رفع دائمی موافقم»)

## Feature Folder   `148-cyclicalwaves-v2-independent-financial-data-provider/changes/fix-gauge-fetch-time-on-unchanged-values`
## Layers / Area    backend · FinancialData
## Priority         P1
## Complexity       S
## Depends On       None
## References       `specs/bugs.md` (BUG-052), `docs/providers/cyclicalwaves-v2/README.md`, `EfCoreCyclicalWavesV2GaugeStore.cs`, `CyclicalWavesV2RelativeValuationInputReader.cs`, `IndustryRelativeValuationReadRepository.cs`, `ValuationGaugeProjector.cs`

## Defect

On 2026-11-27 (Friday) the owner reported that «شگل را با صنعت خودش مقایسه کن» returns the generic no-data answer on
production. It worked before and stopped around the v1.34.0 deploy. That release did not change the comparison path.

Chain found in the code:

1. `IndustryRelativeValuationReadRepository` returns no result unless the group's latest v2 calculation is `Published`
   and its `ValidUntilUtc` is in the future.
2. `CyclicalWavesV2RelativeValuationInputReader` gives each P/E and P/S input the expiry
   `GaugeDetails.FetchedAtUtc + SourceFreshnessHours` (26 h) and drops expired inputs.
3. `EfCoreCyclicalWavesV2GaugeStore.UpsertGaugeAsync` returns early when the fetched gauge equals the stored one, so
   `FetchedAtUtc` keeps the time of the last *change*, not of the last *fetch*.

The daily sync runs at 23:30 UTC. After a closed market day the vendor returns the same gauges, the timestamp is not
moved, and about 26 hours after the last trading day's sync every P/E and P/S input expires for every group. The same
timestamp drives the gauge cards' stale rule (`ValuationGaugeCards:MaxFetchAgeHours`, 48 h), so two closed days are
expected to make the P/E and P/S cards stale.

The equilibrium range had the same early return; it was removed on 2026-11-22 (`136a131a`) when v2 relative valuation
was added. The P/E and P/S gauges were missed.

## Proposed correction

`UpsertGaugeAsync` sets `FetchedAtUtc = now` on every successful fetch of a gauge, changed or not. The returned
outcome stays `Unchanged` when no value differs, so sync run counts do not change.

`GaugeDetails.FetchedAtUtc` then means "the last time the vendor confirmed these values", which is what both readers
already assume and what `EquilibriumRanges.DetailsFetchedAtUtc` already means. No material design change: this restores
the specified behavior of Feature 125 on v2 and of the gauge cards.

## Not changed

- No schema change and no migration.
- `SourceFreshnessHours`, `MaxFetchAgeHours` and the calculation worker stay as they are.
- A failed or skipped sync still ages the data out, as designed.

## Effects and risks

- The «دریافت ‹تاریخ›» line on the P/E and P/S cards shows the date of the latest sync instead of the date of the last
  value change. The equilibrium card already behaves this way.
- One extra row update per ticker and gauge type in each sync, inside the transaction the store already opens.
- Fetch age keeps measuring sync lag only. If the vendor serves old values, the age does not reveal it; that is true
  today as well and is out of scope.

## Assumptions

- The vendor returns identical P/E and P/S gauges on non-trading days. Not yet confirmed on production; the owner can
  confirm it with the two queries in the plan (`tasks.md`, task 0). If the production data contradicts it, the
  investigation of BUG-052 reopens, but the correction is still right on its own.

## Alternatives considered

- Raise `SourceFreshnessHours` only: hides the defect up to the configured length and shows data that old when the sync
  really fails. Usable as a temporary production workaround, not as the fix.
- Use `GaugeAvailability.LastFetchedAtUtc` in the readers: two readers and the card text would need the join, and the
  per-gauge timestamp would stay misleading.
