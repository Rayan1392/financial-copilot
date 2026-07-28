# Tasks

1. Add internal DTOs for `/api/v3/BaseInfo/Companies`.
2. Add `NadpcoApi` symbol fetching and raw-payload capture.
3. Add `NadpcoApiCompanyNormalizer` for normalized company and symbol upserts.
4. Reuse or generalize canonical linkage helpers from `022-codaldb-company-symbol-sync`.
5. Define merge precedence when `NadpcoApi`, `CodalDb`, and StockMarketDB identify the same
   instrument.
6. Preserve unsupported catalog attributes in provenance evidence or document deferral.
7. Add idempotency, cross-provider alignment, missing-code fallback, ambiguity-warning, and
   raw-before-normalization tests.

## Implementation Status

Implemented on 2026-06-03.

Notes:

- Added provider-local `/api/v3/BaseInfo/Companies` DTOs and `NadpcoApiCompanyNormalizer`.
- Normalizes `coID`, company names, Persian/English symbols, `tseCode`, company/share ISINs,
  industry, floor, and market metadata into existing provider-scoped ingestion rows.
- Generalized `CanonicalSymbolLinkageResolver` with an instrument-code-first mode for NADPCO while
  preserving the existing ISIN-first CodalDB behavior.
- 2026-06-05 order `45` remediation added schema-backed persistence for the previously deferred
  NADPCO catalog attributes: listing/IPO/registration dates, fund fields, exchange state,
  national id, Pinglish symbol, market board, and related registration metadata.
- Added unit coverage for idempotency, instrument-code priority, fallback, duplicate/conflicting
  identifier warning, missing identifier handling, and raw-before-normalization via the sync
  processor.

## Change Request Tasks - 2026-06-05

- [x] Add a controlled clean-slate operation for the PostgreSQL `Companies` table before the
      NADPCO company catalog backfill. The operation must document FK/dependency handling,
      transaction boundaries, and rollback/retry behavior.
- [ ] Re-run the NADPCO company catalog sync against
      `https://data3.nadpco.com/api/v3/BaseInfo/Companies` after the clean-slate delete.
- [x] Extend or verify NADPCO company DTO coverage for the sample payload fields:
      `coID`, `coCode`, `coTitle`, `coTitleEnglish`, `coSymbol`, `coSymbolEnglish`, `floorID`,
      `floorTitle`, `industryID`, `industryTitle`, `tseCode`, `tseCIsinCode`, `tseSIsinCode`,
      `marketID`, `marketTitle`, `precedencyRight`, Jalali/Gregorian listing dates,
      fund type fields, Pinglish symbol, national id, exchange state, establishment,
      registration, and market board fields.
- [x] Persist every NADPCO company response field into `Companies` or an appropriate related
      normalized table. Raw payload evidence is still required but is not sufficient as the only
      persistence for any field.
- [x] Add schema support for currently unsupported fields instead of deferring them. Required
      fields include: `precedencyRight`, all Jalali and Gregorian acceptance/enlistment/IPO/
      establishment/business-start/registration dates, `fundTypeID`, `fundTypeTitle`,
      `coSymbolPinglish`, `nationalID`, `inExchange`, `registrationNumber`,
      `registrationProvince`, `registrationCity`, and `marketBoard`.
- [x] Add tests using the provided `coID = 13226` / `آبین` sample to verify company name,
      English name, symbol, English symbol, industry, floor, market, TSE code, ISINs, and
      registration/listing fields that are supported by the schema.
- [x] Add an idempotency test proving a second NADPCO company sync updates existing `coID` rows
      without duplicating companies or symbols.
- [ ] Add a regression test proving CyclicalWaves symbol sync no longer inserts or updates
      PostgreSQL `Companies` rows.

Order `45` implementation notes:

- `NadpcoCompanyCatalogCleanSlateService` removes stale metric recalculation requests,
  company-linked feature jobs, feature snapshots, derived metrics, and symbols, nulls
  StockMarketDB trading-instrument company links, then deletes `Companies`. EF `SaveChangesAsync`
  provides the transactional write boundary for relational databases; failed runs roll back the
  unit of work and can be retried before the NADPCO catalog is reprocessed.
- The `coID = 13226` sample test now verifies persisted company identity, English symbol,
  Pinglish symbol, listing dates, fund fields, national id, exchange state, establishment and
  registration dates, and registration number. The existing idempotency test still proves repeated
  NADPCO syncs do not duplicate companies or symbols.
- The live destructive delete and NADPCO backfill were not run from this checklist item; exposing
  and executing that operator path is tracked by order `46`.
- CyclicalWaves company-write removal remains tracked by order `48`.
