# Tasks

1. Add a provider dataset and Application-facing fetch contract for fundamental indexes.
2. Add bounded DTOs and API calls for `/api/v2/CompanyFundamentalIndex/Values`.
3. Review index samples and define an allowlist mapping index ID to metric code, unit, aliases,
   and activation status.
4. Verify fraction-versus-percent and amount-scale conventions with sampled responses.
5. Register governed semantic definitions and English/Persian aliases for approved metrics.
6. Add `NadpcoApiFundamentalIndexNormalizer` with variant selection, source-marked
   `DerivedMetricRow` persistence, evidence, and idempotency.
7. Add tests for allowlisted and ignored indexes, scale handling, source policy evidence,
   variant selection, scanner filtering, and separation from engine-calculated metrics.

## Implementation Status

Implemented.

- Added `ProviderDataset.FundamentalIndexes` and processor/admin routing for NADPCO fundamental
  index sync while keeping CodalDB `FinancialRatios` separate.
- Added bounded NADPCO request DTOs and client calls for
  `/api/v2/CompanyFundamentalIndex/Values` using company-id batches, curated `companyIndexIds`,
  and optional year/period/variant filters.
- Added reviewed active mappings in `NadpcoApiFundamentalIndexMap` for ratio/amount/day metrics
  whose sampled values are clear enough to activate. Percentage-like metrics are documented as
  deferred until NADPCO scale is verified.
- Reused existing governed semantic definitions and aliases for approved metric codes.
- Added `NadpcoApiFundamentalIndexNormalizer` that selects canonical variants, converts Jalali
  fiscal/period dates with .NET `PersianCalendar`, persists source-marked `DerivedMetricRow`
  values under `nadpco-api-fundamental-index-source-v1`, records vendor title/group/unit
  evidence, and remains idempotent.
- Added tests for allowlisted/ignored indexes, evidence, Jalali period mapping, variant
  selection, policy separation from engine rows, malformed dates, processor routing, bounded
  provider requests, and scanner filtering through the existing `DerivedMetrics` path.
