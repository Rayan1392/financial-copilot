# NADPCO API Financial Statement Synchronization

## User Story

As a scanner user, I want NADPCO API financial statements normalized into PostgreSQL so
fundamental metrics can be queried with absolute periods and vendor provenance.

## Source Endpoints

```http
POST /api/v2/FS/BalanceSheet/Values
POST /api/v2/FS/IncomeStatement/Values
POST /api/v2/FS/CashFlow/Values
```

Requests accept bounded company IDs and item filters. Some endpoints also accept Jalali year
ranges, period type, audited, represented, and composing filters. Responses include statement
ID, company ID, symbol, Gregorian and Jalali fiscal dates, announcement date, variant flags,
and item collections.

## Follow-Up Scope Note

This spec delivered the first governed NADPCO financial-statement ingestion path with curated
metric coverage. It is no longer the final source-of-truth for two areas:

- full vendor line-item persistence when the financial-statement endpoints are called with
  `items: []`
- separate persistence of same-period standalone vs consolidated variants

Those gaps are expanded and superseded by spec
`082-noavaran-financial-statement-full-item-and-variant-persistence`. Spec `040` remains the
foundation for bounded endpoint integration, raw-payload capture, and governed statement metric
normalization.

## Acceptance Criteria

1. Fetch statement data in bounded company batches; year/period/variant filters must remain
   bounded and must never request unrestricted historical payloads in one call.
2. Store each raw response before normalization under `ProviderName = "NadpcoApi"`.
3. Normalize income statement, balance sheet, and cash flow as distinct
   `FinancialStatementType` values using the corrected schema from `029`.
4. Map governed source item IDs to governed `MetricCode` values through reviewed dictionaries,
   not controller branches or title matching at runtime. Full vendor-item persistence beyond the
   governed subset is handled by spec `082`.
5. Use Gregorian source dates for normalized periods and retain Jalali dates plus variant
   flags as evidence.
6. Preserve vendor variant facts in persisted evidence. Canonical query-time selection rules are
   owned by downstream query specs; pre-persistence collapse of standalone vs consolidated
   variants is superseded by spec `082`.
7. Upserts are idempotent and publish recalculation requests for affected source metrics.
8. Existing `CodalDb` rows remain valid and coexist with NADPCO API provenance.

## Out Of Scope

- Query-time vendor calls.
- Unreviewed automatic registration of every vendor item as a governed semantic metric.
- Replacing deterministic derived-metric calculations.

## Period-Scoped Admin Synchronization (2026-10)

The original `POST /api/v1/admin/data-sync/financial-statements` contract remains supported:
`externalReference`, `idempotencyKey`, and `providerName` continue to have their existing
meanings. A separate DataAdmin endpoint provides a Persian fiscal-year and reporting-period
scope:

```http
POST /api/v1/admin/data-sync/financial-statements/by-period
```

```json
{
  "companyId": "00000000-0000-0000-0000-000000000000",
  "fiscalYear": 1405,
  "period": 6,
  "idempotencyKey": null
}
```

`companyId` is the internal `Companies.Id` (`Guid`), not the provider identifier. The endpoint
resolves it through `NoavaranEligibleCompanies`, then uses that row's numeric
`ExternalCompanyId` as the Noavaran `companyIds` request value. With a null company ID it reads all
rows from the same view. Fiscal years are accepted from 1400 through 1500; period is null or one
of 3, 6, 9, and 12 months. A null period queues one company request that fetches each supported
period. The endpoint uses the existing DataAdmin authorization policy and RabbitMQ data-sync
queue. Its response reports eligible, queued, and failed companies; each enqueue failure is
isolated and identified by internal company ID.

The Noavaran FS endpoints support `fromYear`, `toYear`, and `perTId`. Period-scoped queued
requests set both year bounds to the requested Persian fiscal year and use the provider's
`perTId` values 3, 6, 9, or 12. For all-period requests the provider makes bounded requests for
each of those four values. This is provider-side filtering; the sync worker stores only the
returned payload in the existing raw-payload store and sends it through the existing normalizer.
The normalizer keeps the provider's Jalali fiscal-year-end evidence and Gregorian fiscal-period
mapping. It does not infer fiscal year from publication date or assume an Esfand year end.
Full-item persistence, corrections, variant identity, and idempotent upserts remain governed by
spec `082` and the existing normalized persistence pipeline.

The queued response is an enqueue summary, not a completion report. Runtime processing and
failures remain visible through the existing data-sync run/activity monitoring surfaces. No
database migration is needed because this change only extends the request envelope and query
scope.
