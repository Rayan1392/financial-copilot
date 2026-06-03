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

## Acceptance Criteria

1. Fetch statement data in bounded company batches and with curated item allowlists; never
   request unrestricted historical payloads in one call.
2. Store each raw response before normalization under `ProviderName = "NadpcoApi"`.
3. Normalize income statement, balance sheet, and cash flow as distinct
   `FinancialStatementType` values using the corrected schema from `029`.
4. Map source item IDs to governed `MetricCode` values through reviewed dictionaries, not
   controller branches or title matching at runtime.
5. Use Gregorian source dates for normalized periods and retain Jalali dates plus variant
   flags as evidence.
6. Apply a deterministic canonical-variant selection policy for audited, represented, and
   composing variants.
7. Upserts are idempotent and publish recalculation requests for affected source metrics.
8. Existing `CodalDb` rows remain valid and coexist with NADPCO API provenance.

## Out Of Scope

- Query-time vendor calls.
- Unreviewed automatic registration of every vendor item.
- Replacing deterministic derived-metric calculations.

