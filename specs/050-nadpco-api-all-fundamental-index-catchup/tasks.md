# Tasks

- [ ] Confirm current `041` curated fundamental-index behavior and document why `companyIndexIds: []`
      is a separate all-index coverage mode.
- [ ] Define the persistence model for all returned vendor indexes that are not yet governed scanner
      metrics.
- [ ] Add/plan DataAdmin orchestration to enumerate all local NADPCO company ids and enqueue bounded
      catch-up requests for `fromYear=1403` and `toYear=1405`.
- [ ] Ensure the request body uses batched `companyIds` and an empty `companyIndexIds` array:

      ```json
      {
        "companyIds": [4],
        "companyIndexIds": []
      }
      ```

- [ ] Preserve raw payloads, checksums, source endpoint, query params, and idempotency keys for each
      batch.
- [ ] Parse and persist company-period header fields:
      `comBS_ID`, `comId`, `comTitle`, `periodType`, `jalaliFiscalYearEnd`, `jalaliPeriodEnd`,
      `jalaliAnouncementDate`, `isAudited`, `isRepresented`, and `isComposing`.
- [ ] Parse and persist every returned index item:
      `companyIndexId`, `companyIndexTitle`, `companyIndexGroupId`, `companyIndexGroupTitle`,
      `companyIndexValue`, and `companyIndexUnit`.
- [ ] Keep existing governed promotion behavior from `041`: only reviewed/mapped indexes may be
      written as scannable `DerivedMetrics`.
- [ ] Add deterministic variant selection and idempotent upserts for duplicate company/index/period
      observations.
- [ ] Add telemetry for companies considered, companies enqueued, request batches, failed company
      ids, processed observations, ignored/promoted indexes, and duration.
- [ ] Add tests for:
      - all-index request body with empty `companyIndexIds`;
      - batching all NADPCO company ids;
      - sample response parsing;
      - unknown index persistence without scanner exposure;
      - reviewed index promotion still working through the existing curated path;
      - idempotent reprocessing;
      - failure isolation by company batch.
- [ ] Update provider/operator documentation after implementation to explain when to run all-index
      catch-up versus curated fundamental-index sync.

## Implementation Status

Not implemented. This spec is for planning only.
