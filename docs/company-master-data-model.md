# Company Master Data Model

How the platform represents company identity, symbols, industry/group/market classification, and
ISIN codes in its canonical, provider-agnostic read model. This model is what the scanner, AI
explanations, ranking, company search, and cross-provider symbol resolution read.

> Introduced to preserve the richer business metadata that CodalDB exposes (English name,
> classification, multiple identifiers, ISINs) instead of discarding it. Implements the
> company-master-data slice of [specs/022-codaldb-company-symbol-sync](../specs/022-codaldb-company-symbol-sync/user-story.md).
> The live CodalDB SQL gateway is spec `021` (deferred); the normalizer here is exercised against
> JSON payloads.

## Storage shape

Normalized rows live in `FinancialIngestionDbContext`
([FinancialIngestionRows.cs](../src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/Persistence/FinancialIngestionRows.cs)).
All enrichment columns are **nullable** and additive — existing providers (CyclicalWaves,
ConfiguredFinancialProvider) simply leave them null, so the change is backward compatible.

### `Companies` (`NormalizedCompanyRow`)

| Column | Source (CodalDB) | Notes |
|---|---|---|
| `ProviderName` + `ExternalCompanyId` | — / `CoID` | Idempotency key. `CoID` is the Codal company id. |
| `Name` | `CoName` | Persian company name. |
| `NameEnglish` | `CoNameEnglish` | English company name. |
| `CompanySymbol` | `CompanySymbol` | Trading symbol. |
| `TseSymbol` | `CoTSESymbol` | TSE symbol (best raw coverage). |
| `InstrumentCode` | `InstCode` | TSETMC instrument code. |
| `CompanyIsin` | `TseCIsinCode` | Company ISIN. |
| `SymbolIsin` | `TseSIsinCode` | Symbol/share ISIN. |
| `InstrumentRefPlaceholder` | `InstrumentRef` | **Non-identifying.** See below. |
| `IndustryId` / `GroupId` / `MarketId` | `IndustryID` / `GroupID` / `MarketID` | Nullable FKs to dimension tables. |
| `SourceModifiedAt` | `ModifiedDateTime` | Incremental-sync watermark (spec 027). |

### `Symbols` (`NormalizedSymbolRow`)

| Column | Notes |
|---|---|
| `SymbolCode` | The canonical code used to join securities across providers (see linkage below). |
| `LinkageBasis` | Which identifier produced `SymbolCode` (e.g. `SymbolIsin`) — for auditability/explanations. |

### Classification dimensions

`Industries`, `IndustryGroups`, `Markets` (`NormalizedIndustryRow`, `NormalizedIndustryGroupRow`,
`NormalizedMarketRow`) are provider-scoped dimension tables keyed by `(ProviderName, ExternalId)`,
referenced by nullable FKs from `Companies` (indexed for filtering/segmentation, `OnDelete =
SetNull`). `Industries.ParentId` is reserved for future hierarchy expansion. Industry/group/market
names are Persian; the source id is kept alongside the name for stable filtering. Classification
values are never hardcoded — they are ingested as data.

## Identity & symbol resolution

A company carries **several** identifiers (`CompanySymbol`, `TseSymbol`, `InstrumentCode`,
`CompanyIsin`, `SymbolIsin`), all retained for future provider mapping and symbol-resolution
services. The single **canonical `SymbolCode`** is chosen by a pure domain policy,
[`CanonicalSymbolLinkageResolver`](../src/backend/FinancialCopilot.Domain/Financial/Services/CanonicalSymbolLinkageResolver.cs),
in priority order:

1. `SymbolIsin` (`TseSIsinCode`)
2. `CompanyIsin` (`TseCIsinCode`)
3. `InstrumentCode` (`InstCode`)
4. `TseSymbol` (`CoTSESymbol`)
5. `CompanySymbol`

**Why ISIN first — cross-provider alignment.** Securities join across providers by `SymbolCode`
equality. CyclicalWaves' canonical code is its `enticker`, which is the share **ISIN** (e.g.
`IRO7SHLP0001`). Preferring ISIN makes CodalDB's `SymbolCode` line up with CyclicalWaves'. The
chosen identifier is recorded as `LinkageBasis`. When **no ISIN** is available, the code still
resolves deterministically from the next-best identifier, and the normalizer logs a
cross-provider-alignment data-quality warning (the row may not yet join CyclicalWaves). A company
with no usable identifier gets a company row but no symbol row.

### `InstrumentRef` is **not** an identifier

CodalDB's `InstrumentRef` is a single constant GUID shared by every row (verified in
[docs/codaldb-datasource.md](codaldb-datasource.md) §3.1). It is retained verbatim as
`InstrumentRefPlaceholder` for provenance only — it is never indexed and never used as a join or
resolution key. Use `InstrumentCode`, the ISINs, or `TseSymbol` instead.

## Pipeline & ownership

- Provider-specific mapping is confined to the provider/normalization layers:
  [`CodalDbCompanyRecord`](../src/backend/FinancialCopilot.Infrastructure/Financial/Providers/CodalDb/CodalDbPayloadModels.cs)
  (the JSON `Symbols` payload contract) and
  [`CodalDbSymbolNormalizer`](../src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/CodalDb/CodalDbSymbolNormalizer.cs).
  No CodalDB specifics or switch statements leak into Application code.
- `DataSyncRequest.ProviderName` (optional) lets a request target a specific provider; it is
  persisted on the sync-run record. Selecting the *fetch* provider by name in the processor is
  deferred to spec `021`, where the live CodalDB SQL client lands. Normalizer selection already
  keys on `(payload.ProviderName, dataset)`.
- Mapping the source integer `IndustryID` to the canonical domain `Industry` aggregate (a `Guid`
  entity) is a later concern; the read model stores the provider's source ids/names.
- Scanner condition/column support and ranking over industry/market are owned by specs `007`/`008`;
  this change makes the data available and queryable.
