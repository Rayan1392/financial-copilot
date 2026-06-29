# NADPCO API Fundamental Index Synchronization

## User Story

As a scanner user, I want curated NADPCO fundamental indexes persisted as scannable metrics so
I can filter companies by vendor-provided ratios and indicators with explicit provenance.

## Source Endpoint

```http
POST /api/v2/CompanyFundamentalIndex/Values
```

Requests accept bounded `companyIds`, optional `companyIndexIds`, year range, period type, and
variant filters. Responses contain company-period headers and nested indexes with index ID,
title, group ID, group title, value, and unit.

## Acceptance Criteria

1. Add a dedicated provider dataset for NADPCO fundamental indexes and fetch only bounded
   company/index batches.
2. Establish a reviewed allowlist mapping vendor `companyIndexId` values to canonical
   `MetricCode`, unit, semantic aliases, and source policy version.
3. Persist curated vendor-provided observations as `DerivedMetricRow` values with
   `CalculationPolicyVersion = "nadpco-api-fundamental-index-source-v1"` and source evidence.
4. Distinguish vendor-precomputed observations from engine-calculated metrics; never route
   source indexes through `IFinancialMetricCalculator`.
5. Apply deterministic canonical variant selection and idempotent upserts.
6. Retain vendor index group and title metadata as evidence.
7. Require sampled scale verification before activating percentage-like metrics.
8. Make curated indexes scannable without scanner-engine code changes.
9. Add a DataAdmin-only bulk admin endpoint for curated fundamental-index sync that does not
   accept `externalReference` and instead enumerates eligible companies from the
   `NoavaranEligibleCompanies` database view.
10. The bulk endpoint must enqueue the same `ProviderDataset.FundamentalIndexes` per-company sync
    flow already used by `POST /api/v1/admin/data-sync/fundamental-indexes`; it must not switch to
    the all-index coverage/catch-up path from spec `050`.
11. The eligible-company source for the bulk endpoint is `NoavaranEligibleCompanies`, using the
    view column `ExternalCompanyId` as the queued `externalReference` value.
12. The bulk endpoint route is `POST /api/v1/admin/data-sync/fundamental-indexes/eligible-companies`
    with request body:

```json
{
  "providerName": null,
  "idempotencyKey": null,
  "maxItems": null,
  "dryRun": false
}
```

13. `providerName` follows the curated single-company endpoint convention: the effective provider is
    `NoavaranCurrentApi`; null/blank uses that default, and non-blank values must resolve to the
    same provider or be rejected.
14. If `idempotencyKey` is null, the bulk endpoint generates one batch idempotency key. Each
    per-company child enqueue must derive a deterministic child key from the batch key and
    `externalReference`.
15. `dryRun=true` must read from `NoavaranEligibleCompanies` and return the same counts/item list
    but enqueue nothing.
16. `maxItems` limits the number of eligible companies processed for admin/testing and applies after
    deterministic ordering by `ExternalCompanyId`.
17. One enqueue failure must not hide successful items. The response must report partial results
    with item-level status and aggregate `eligibleCount`, `queuedCount`, `skippedCount`, and
    `failedCount`.
18. The bulk endpoint must log batch start, eligible-company count, per-company enqueue failures,
    and completion summary.

## Bulk Admin Response Shape

```json
{
  "requestId": "00000000-0000-0000-0000-000000000000",
  "dataset": "FundamentalIndexes",
  "source": "NoavaranEligibleCompanies",
  "requestedAt": "2026-06-28T08:34:50.992137+00:00",
  "idempotencyKey": "admin-data-sync:FundamentalIndexes:eligible-companies:...",
  "status": "Queued",
  "eligibleCount": 123,
  "queuedCount": 123,
  "skippedCount": 0,
  "failedCount": 0,
  "items": [
    {
      "externalReference": "4",
      "status": "Queued",
      "idempotencyKey": "admin-data-sync:FundamentalIndexes:eligible-companies:...:4",
      "error": null
    }
  ]
}
```

## Out Of Scope

- Importing every vendor index automatically.
- Treating vendor titles as stable identifiers.
- Overwriting deterministic calculations.
- Replacing the existing single-company curated endpoint behavior.
- Replacing spec `050` all-index catch-up coverage with curated metric sync.
