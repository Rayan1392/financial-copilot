# Tasks

## Application — Provider Routing (coexistence enabler)

- [ ] Add an optional `string? ProviderName` to `DataSyncRequest` (default null = configured
      primary provider). Update `FinancialDataSyncProcessor` to resolve the correct
      `ISymbolDataProvider` / `IFinancialStatementProvider` / `IMonthlyProductionSalesProvider`
      by provider name when supplied, preserving current behavior when null.
- [ ] Register CyclicalWaves and CodalDb provider clients so they can be resolved by provider
      name (keyed services or a small `IReadOnlyDictionary<string, …>` provider registry).

## Infrastructure — Company model extension

- [ ] Extend `NormalizedCompanyRow` with nullable columns: `NameEnglish`, `IndustryId`,
      `IndustryName`, `GroupId`, `GroupName`, `MarketId`, `MarketName`, `CompanyIsin`,
      `SymbolIsin`, `InstrumentCode`, `SourceModifiedAt`. Update
      `FinancialIngestionConfigurations`/`FinancialIngestionDbContext` and add an EF migration in
      `…/Ingestion/Persistence/Migrations`. Existing providers leave the columns null.

## Infrastructure — Symbol Linkage

- [ ] Add `CodalDbCanonicalSymbolResolver` (static/helper) implementing the priority rule:
      `InstCode` → ISIN (`TseCIsinCode`/`TseSIsinCode`) → `CoTSESymbol` → `CompanySymbol`.
      Returns the chosen `SymbolCode` plus the source basis (for evidence). Reject/skip the
      constant `InstrumentRef`.
- [ ] Document and implement the single canonical-symbol rule shared with CyclicalWaves so the
      same security yields the same `SymbolCode`; emit a data-quality warning when alignment
      cannot be established.

## Infrastructure — Normalizer

- [ ] Add `…/Ingestion/CodalDb/CodalDbSymbolNormalizer.cs`
      (`ProviderName = "CodalDb"`, `Dataset = Symbols`):
      - Deserialize the symbols payload (projected company rows).
      - Upsert `NormalizedCompanyRow` keyed on `(ProviderName, ExternalCompanyId = CoID)`,
        populating the extended attributes (English name, industry/group/market ids+names, ISINs,
        instrument code, `SourceModifiedAt`).
      - Upsert `NormalizedSymbolRow` with canonical `SymbolCode` from
        `CodalDbCanonicalSymbolResolver`; set `ExternalSymbolId = CoID`.
      - Idempotent on repeated identical payloads.
- [ ] Register `CodalDbSymbolNormalizer` as `IFinancialPayloadNormalizer` in the composition
      root.

## Tests

- [ ] `CodalDbCanonicalSymbolResolverTests` (unit, ~6 tests): each priority tier selected
      correctly; `InstrumentRef` never used; fallback chain when higher tiers are null.
- [ ] `CodalDbSymbolNormalizerTests` (unit, ~6 tests, EF Core in-memory): company + symbol rows
      created; extended attributes (industry/group/market/ISIN/instrument code/English name)
      populated; repeated payload is idempotent; canonical symbol matches the resolver; missing
      instrument code falls back to `CoTSESymbol`/`CompanySymbol`.
- [ ] Provider-routing test: a `DataSyncRequest` with `ProviderName = "CodalDb"` routes to the
      CodalDB client; with null it uses the configured primary.
