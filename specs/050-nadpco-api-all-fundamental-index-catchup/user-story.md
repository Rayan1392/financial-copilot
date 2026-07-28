# NADPCO API All Fundamental Index Catch-up

## User Story

As a data administrator, I want to fetch `CompanyFundamentalIndex` data for every NADPCO company
id from Shamsi year 1403 through 1405, with `companyIndexIds` left empty so the vendor returns all
available indexes, so we can build a complete local coverage dataset before deciding which indexes
are safe to promote into governed scanner metrics.

## Existing Scope Check

Spec `041-nadpco-api-fundamental-index-sync` already implements curated NADPCO fundamental index
sync for reviewed `companyIndexId` values. That service intentionally does not import every vendor
index automatically. This story covers the missing all-company/all-index catch-up mode.

## Source Endpoint

```http
POST https://data3.nadpco.com/api/v2/CompanyFundamentalIndex/Values?fromYear={shamsiYear}&toYear=1405
```

Initial requested range:

```text
shamsiYear = 1403
toYear = 1405
```

Request body shape:

```json
{
  "companyIds": [4],
  "companyIndexIds": []
}
```

For production catch-up, `companyIds` must be batched from the local NADPCO-backed company catalog.
`companyIndexIds: []` means "return all indexes available from the vendor" and must not be replaced
with the existing curated allowlist.

## Response Shape

The attached sample response is an array of company-period records. Each record includes:

- `comBS_ID`
- `comId`
- `comTitle`
- `periodType`
- `jalaliFiscalYearEnd`
- `jalaliPeriodEnd`
- `jalaliAnouncementDate`
- `isAudited`
- `isRepresented`
- `isComposing`
- `indexes[]`

Each `indexes[]` item includes:

- `companyIndexId`
- `companyIndexTitle`
- `companyIndexGroupId`
- `companyIndexGroupTitle`
- `companyIndexValue`
- `companyIndexUnit`

## Acceptance Criteria

1. Add a DataAdmin-only catch-up workflow that iterates all local NADPCO company ids and enqueues
   bounded `CompanyFundamentalIndex` requests for `fromYear=1403` through `toYear=1405`.
2. Keep each remote call bounded by `NadpcoApi:BatchSize`/orchestration batch settings; never send
   an unbounded all-company request.
3. Send `companyIndexIds: []` for this catch-up mode so NADPCO returns every available index.
4. Store the complete vendor response with raw-payload provenance and idempotency.
5. Persist all returned vendor index observations into a local coverage/staging model, or another
   explicitly designed persistence path, without pretending every vendor index is a governed
   scanner metric.
6. Continue to promote only reviewed/mapped indexes into `DerivedMetrics` as governed metrics,
   preserving the existing `041` safety rule.
7. Preserve source evidence for company id/title, statement id, period, Jalali dates, announcement
   date, audited/represented/composing flags, index id/title/group/unit, and original provider
   payload checksum.
8. Use deterministic variant selection when multiple rows exist for the same company, index,
   period type, and period end.
9. Record run progress, failed company ids, request counts, and retry-safe idempotency keys.
10. Add documentation explaining the difference between curated fundamental-index sync and all-index
    catch-up coverage sync.

## Out Of Scope

- Exposing every returned vendor index to scanner users automatically.
- Creating new metric aliases, formulas, or calculators from vendor titles without review.
- Query-time calls to NADPCO.
- Replacing the curated `041` sync path.

## Open Design Questions

- Should all-index observations use a new table such as `NadpcoFundamentalIndexObservations`, or
  should they be stored as source-only rows with a non-scannable status?
- Should catch-up be a one-off DataAdmin endpoint only, or also selectable through
  `NadpcoScheduledSync.DatasetSelection` after initial backfill?
- Which index ids from the attached sample should be candidates for later governed promotion?
