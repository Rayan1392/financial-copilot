# NADPCO API Company Catalog Synchronization

## User Story

As a scanner user, I want the NADPCO company catalog synchronized into normalized PostgreSQL
company and symbol rows so API-sourced statements, indexes, and monthly activity resolve to
the same canonical securities used by existing providers.

## Source Endpoint

```http
GET /api/v3/BaseInfo/Companies
```

The attached payload includes `coID`, Persian and English company names, Persian and English
symbols, floor and industry identifiers, TSE instrument code, company and symbol ISINs,
market metadata, listing dates, registration metadata, and exchange state.

## Acceptance Criteria

1. Fetch the company catalog through `NadpcoApi` and store the raw payload before normalization.
2. Upsert normalized company rows keyed by `(ProviderName, ExternalCompanyId)` using `coID`.
3. Resolve canonical symbols using the established priority: instrument code, ISIN, exchange
   symbol, then vendor symbol fallback. Reuse shared linkage rules where possible.
4. Populate supported normalized metadata such as English name, industry, market, ISINs, and
   instrument code without overwriting stronger cross-provider evidence.
5. Retain unsupported source fields as evidence or document their deferral.
6. Keep synchronization idempotent and record data-quality warnings for incomplete or
   ambiguous linkage.
7. Do not create duplicate canonical securities when `CodalDb` already synchronized the same
   company.

## Out Of Scope

- Statements, fundamental indexes, and monthly activity.
- Treating vendor-local IDs as canonical symbols.

