# User Story — CodalDB Company & Symbol Sync

> Depends on `021-codaldb-provider-foundation`. Schema reference:
> [docs/codaldb-datasource.md](../../docs/codaldb-datasource.md).

## Story

As a scanner user,
I want every CodalDB company and its trading symbol normalized into the platform's
`NormalizedCompanyRow` / `NormalizedSymbolRow` tables with a stable canonical `SymbolCode` **and
the richer company attributes Codal provides** (English name, industry, group, market, ISINs),
so that Codal financial data joins correctly to the same symbols used by CyclicalWaves and by the
scanner, the ~65 companies without a clean instrument code still resolve, and the scanner can
filter/segment by industry, group, and market.

## Context

`Companies` in CodalDB (2,362 rows) is the issuer master keyed by `CoID` and carries **more
columns than the current `NormalizedCompanyRow` captures** — `CoNameEnglish`, `GroupID`/
`GroupName`, `IndustryID`/`IndustryName`, `MarketID`/`MarketName`, `TseCIsinCode`/`TseSIsinCode`,
`InstCode`, plus `ModifiedDateTime`. Today `NormalizedCompanyRow` only holds
`(ProviderName, ExternalCompanyId, Name, LastSynchronizedAt)`, which would discard this useful
data. This story **extends the normalized company model** so these attributes are retained and
queryable (enabling industry/market filters and segmentation).

The normalized model keys symbols by a canonical `SymbolCode` string. Because CodalDB
**coexists** with CyclicalWaves, the two providers must produce **aligned `SymbolCode` values**
for the same security, or their rows will not join in the scanner. CodalDB's `InstrumentRef`
column is a **constant placeholder** (one GUID for all rows) and must not be used as an
identifier.

## Acceptance Criteria

- `NormalizedCompanyRow` is **extended** (EF migration) with the richer Codal attributes:
  `NameEnglish`, `IndustryId`, `IndustryName`, `GroupId`, `GroupName`, `MarketId`, `MarketName`,
  `CompanyIsin` (`TseCIsinCode`), `SymbolIsin` (`TseSIsinCode`), `InstrumentCode` (`InstCode`),
  and `SourceModifiedAt` (`ModifiedDateTime`, used as the incremental-sync watermark in `027`).
  All new columns are nullable; existing providers (CyclicalWaves) simply leave them null.
- A `CodalDbSymbolNormalizer` (`ProviderName = "CodalDb"`, `Dataset = ProviderDataset.Symbols`)
  deserializes the symbols payload and upserts, per company:
  - `NormalizedCompanyRow` with `ProviderName = "CodalDb"`, `ExternalCompanyId = CoID` (string),
    `Name` = `CoName` (Persian), and the extended attributes above populated from the source
    columns (`CoNameEnglish`, industry/group/market ids+names, ISINs, `InstCode`,
    `ModifiedDateTime`), `LastSynchronizedAt` = sync time.
  - `NormalizedSymbolRow` with `ProviderName = "CodalDb"`, `ExternalSymbolId = CoID`,
    `SymbolCode` = the canonical symbol resolved by the linkage strategy below.
- **Canonical `SymbolCode` linkage strategy** (documented and applied in priority order, since
  symbols are reused across delisted/renamed issuers):
  1. `InstCode` (TSETMC instrument code) when present (859 companies, all distinct).
  2. `TseCIsinCode` / `TseSIsinCode` (ISIN) when present.
  3. `CoTSESymbol` (best coverage: 2,304 distinct of 2,362).
  4. `CompanySymbol` as final fallback.
  The chosen source is recorded so the value is reproducible and auditable.
- **Cross-provider alignment:** the canonical `SymbolCode` chosen for CodalDB must match the
  `SymbolCode` CyclicalWaves produces for the same security wherever both providers cover it.
  The spec defines a single documented canonical-symbol rule both providers honor (e.g. ISIN
  `enticker` ↔ CodalDB ISIN), and records the alignment basis on the symbol row. Where alignment
  is not yet possible, the divergence is logged as a data-quality warning rather than silently
  creating a duplicate symbol.
- `InstrumentRef` is **never** used as an identifier (it is a constant placeholder).
- Upserts are idempotent: re-running the symbol sync with an unchanged payload produces no
  duplicate company or symbol rows (matched on `(ProviderName, ExternalCompanyId)`).
- After successful normalization the existing pipeline behavior is preserved: the raw payload is
  stored before normalization, and the sync run is recorded in `DataSyncRunRow`.

## Technical Notes

- The `FetchSymbolsAsync` query in `021` should already project the linkage columns (`CoID`,
  `CompanySymbol`, `CoTSESymbol`, `InstCode`, `TseCIsinCode`, `TseSIsinCode`, `CoName`,
  `CoNameEnglish`, `IndustryName`, `MarketName`) so the normalizer has everything it needs
  without a second query.
- Industry/group/market/ISIN/instrument-code become **first-class nullable columns** on
  `NormalizedCompanyRow` (see the extension above) rather than being dropped or buried in
  evidence JSON, because the scanner needs to filter/segment by them. `IndustryName`/`GroupName`
  are Persian; keep ids alongside names for stable filtering.
- `SourceModifiedAt` (from `Companies.ModifiedDateTime`) is stored so the `027` nightly
  orchestrator can do incremental, watermark-based syncs instead of full reloads.
- Provider routing: introduce a way for `DataSyncRequest` / the processor to target a specific
  provider (e.g. add an optional `ProviderName` to the request, defaulting to the configured
  primary) so the same `Symbols` dataset can be synced from either CyclicalWaves or CodalDb.
  Keep the existing single-provider behavior working when `ProviderName` is omitted.

## Dependencies

- `021-codaldb-provider-foundation`.
- `003-financial-domain-model` / `005` (`NormalizedCompanyRow`, `NormalizedSymbolRow`,
  `IFinancialPayloadNormalizer`, upsert/idempotency conventions).
