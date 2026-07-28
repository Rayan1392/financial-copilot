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
4. Persist all NADPCO company-catalog fields into `Companies` or an appropriate related
   normalized table such as `Symbols`, `Industries`, `Markets`, provider metadata, or a dedicated
   catalog-detail table. Raw payload storage is required for provenance but is not sufficient as
   the only persistence for any field listed in the NADPCO response contract.
5. Add database columns or related normalized tables for NADPCO fields that do not currently fit
   the schema; do not defer fields merely because the previous company/symbol rows lacked a
   column.
6. Keep synchronization idempotent and record data-quality warnings for incomplete or
   ambiguous linkage.
7. Do not create duplicate canonical securities when `CodalDb` already synchronized the same
   company.

## Out Of Scope

- Statements, fundamental indexes, and monthly activity.
- Treating vendor-local IDs as canonical symbols.

## Change Request - 2026-06-05

NADPCO `/api/v3/BaseInfo/Companies` is now the authoritative source for the PostgreSQL
`Companies` catalog. Before the implementation work begins, the implementation plan must include
a controlled clean-slate refresh:

1. Delete all existing rows from the PostgreSQL `Companies` table through an explicit migration,
   maintenance command, or DataAdmin operation with documented dependency handling.
2. Repopulate `Companies` from `https://data3.nadpco.com/api/v3/BaseInfo/Companies` using the
   NADPCO payload shape shown in the request sample. Every field in the service response must be
   persisted in `Companies` or a related normalized table:
   - identity and naming: `coID`, `coCode`, `coTitle`, `coTitleEnglish`;
   - symbols: `coSymbol`, `coSymbolEnglish`, `coSymbolPinglish`;
   - classification: `floorID`, `floorTitle`, `industryID`, `industryTitle`, `marketID`,
     `marketTitle`, `marketBoard`;
   - exchange identifiers: `tseCode`, `tseCIsinCode`, `tseSIsinCode`;
   - status and rights: `precedencyRight`, `inExchange`, `fundTypeID`, `fundTypeTitle`;
   - dates in both Jalali and Gregorian forms: `acceptionDate`, `acceptionDateGre`,
     `enlistedDate`, `enlistedDateGre`, `ipoDate`, `ipoDateGre`, `establishmentDate`,
     `establishmentDateGre`, `businessStartDate`, `businessStartDateGre`, `registrationDate`,
     `registrationDateGre`;
   - registration and legal identifiers: `nationalID`, `registrationNumber`,
     `registrationProvince`, `registrationCity`.
3. If the current schema has no place for a response field, add the required column or related
   normalized table as part of the implementation. No NADPCO company response field may remain
   only in raw payload evidence.
4. Treat `coID` as the NADPCO external company id and keep the sync idempotent after the initial
   clean-slate load.
5. Ensure future `Companies` catalog writes come from NADPCO, not CyclicalWaves.
6. Add verification that a known sample row such as `coID = 13226`, `coSymbol = "آبین"`,
   `coTitle = "کشت و صنعت آبشیرین"` is normalized correctly.

