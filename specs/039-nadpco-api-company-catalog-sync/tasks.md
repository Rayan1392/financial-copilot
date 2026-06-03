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
- Unsupported catalog attributes (listing/IPO/registration/fund/exchange/national-id/market-board)
  remain in raw payload evidence and are logged as deferred fields because company/symbol rows do not
  currently include a persisted catalog-evidence column.
- Added unit coverage for idempotency, instrument-code priority, fallback, duplicate/conflicting
  identifier warning, missing identifier handling, and raw-before-normalization via the sync
  processor.
