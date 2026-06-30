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
