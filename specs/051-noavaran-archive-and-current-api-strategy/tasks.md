# Tasks - Noavaran Amin Archive and Current API Source Strategy

## Domain and Configuration

- Introduce a logical vendor/source model:
  - `LogicalVendor`: `NoavaranAmin`, `CyclicalWaves`, `Tsetmc`.
  - `PhysicalSource`: `NoavaranArchiveSql`, `NoavaranCurrentApi`, `CyclicalWavesApi`, `StockMarketDb`, `TsetmcWebService`.
  - `SourceMode`: `ArchiveOneTime`, `CurrentIncremental`, `ExternalSnapshot`, `MigrationBridge`.
- Add configuration for dataset-level source priority.
- Add configuration for Shamsi date boundary around 1403 where current API coverage starts.
- Add source provenance metadata to normalized ingestion contracts.

## Refactor Existing Specs / Implementation

- Mark legacy NADPCO/CodalDB archive sync as one-time import.
- Prevent any scheduled worker from repeatedly syncing archive-only data by default.
- Keep manual maintenance re-import possible with explicit admin confirmation.
- Update provider health reporting to show archive freshness differently from current API freshness.

## Identity Resolution

- Add canonical company/security mapping rules across archive and current API sources.
- Prefer stable identifiers such as `CoID`, `CompanySymbol`, `InstCode`, ISIN, TSETMC instrument code, and normalized symbol.
- Add conflict logging when identifiers disagree.

## Testing

- Unit tests for source-priority resolution.
- Integration tests for archive rows and current API rows coexisting without duplication.
- Regression tests proving scanner queries read from canonical normalized data, not physical source names.
