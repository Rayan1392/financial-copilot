# Feature 140 — Design: IME Daily Trades Ingestion and Producer Mapping

## 1. Design status

**READY_FOR_IMPLEMENTATION**. T140-01 is complete. The approved product decision is manual mapping from a discovered IME producer to one administrator-selected provider-scoped row in `FinancialIngestionDbContext.Companies`.

## 2. Identity decision and boundaries

`Companies` is persisted as `NormalizedCompanyRow`; its primary key is `Guid`. Uniqueness is provider-scoped by `(ProviderName, ExternalCompanyId)`, and the repository permits provider-specific rows for the same issuer. A Feature 140 mapping therefore means only:

> An administrator explicitly associated this IME producer with this one existing `NormalizedCompanyRow`.

It does not assert cross-provider canonical or legal-company identity. Feature 140 creates no general canonical-company entity, company merge, or MDM layer. Candidate rows must be individually visible and explicitly selected. Their names, symbols, provider names, and external IDs are read from `Companies` when displayed.

## 3. Producer identity model

The entire IME `Symbol` represents a product/instrument and is not the producer mapping key. The stable lookup key is the normalized prefix before the first hyphen:

```text
IALC-ALHYD-00       -> IALC
SHPC-PBR1220WWP-00  -> SHPC
SHPC-OAVAMB-00      -> SHPC
```

The producer identity is `ProducerCode` together with the observed producer-name identity used for conflict detection. Store raw `Symbol` and `ProducerName` unchanged in the trade observation. A deterministic normalization function produces separate code and name comparison values; it never overwrites source fields and never uses fuzzy matching to merge business identities.

## 4. Logical architecture

```text
IME API
  |
  v
shared IME daily importer <------------------- Admin historical trigger
  |
  +--> retain full raw response evidence
  |
  +--> parse trades; preserve all 34 provider fields
          |
          +--> source-only observation fingerprint / idempotent upsert
          |
          +--> extract normalized ProducerCode
                  |
                  +--> upsert ImeProducers registry and last-seen facts
                  +--> compare normalized ProducerName; surface conflict
                  +--> resolve one active Manual mapping only when non-conflicted
                          |
                          v
             ImeProducerCompanyMappings
                          |
                          v
     selected NormalizedCompanyRow.Id (Companies.Id)
```

No mapping or conflict never prevents raw response/trade persistence. Scheduled Worker and Admin imports invoke the same importer. Historical re-resolution updates linkage separately from source observations.

## 5. Registry and conflict model

Conceptual persisted table `ImeProducers`:

| Column | Meaning |
|---|---|
| `Id` | producer registry identity |
| `ProducerCode` | normalized prefix; stable lookup key |
| `ProducerName` | latest observed display name; derived registry metadata, not a substitute for raw trade evidence |
| `NormalizedProducerName` | deterministic name-comparison value |
| `FirstSeenAtUtc`, `LastSeenAtUtc` | first/latest observation instants |
| `LastObservedSymbol` | latest full product symbol, for operator context only |
| `Status` | `Unmapped`, `Mapped`, or `Conflict` (or equivalent) |
| `CreatedAtUtc`, `UpdatedAtUtc` | registry audit timestamps |

Enforce one registry row per normalized `ProducerCode`, so multiple products with the same prefix update one producer. On each observation, update last-seen fields and compare the observed normalized name with known identity evidence. Equivalent whitespace, Unicode, Arabic/Persian character, and configured harmless punctuation variants count as equivalent. A materially different normalized business name sets `Conflict`; source `ProducerName` values remain on their respective trades and are aggregated/displayed for review. Do not select a name/company by fuzzy score.

`Conflict` blocks automatic resolution of new trades through that producer until an authorized operator explicitly reviews and resolves the conflict. The review action and its audit evidence must be recorded. The one-code/one-registry identity remains stable; do not create a new producer row for each full Symbol or each spelling variant.

## 6. Manual mapping model and resolution

Conceptual table `ImeProducerCompanyMappings`:

| Column | Meaning |
|---|---|
| `Id` | mapping record identity |
| `ImeProducerId` | FK to `ImeProducers.Id` |
| `CompanyId` | FK to selected `NormalizedCompanyRow.Id` / `Companies.Id` |
| `MappingStatus` | active/confirmed or disabled state per repository naming |
| `MappingSource` | `Manual` for this workflow |
| `IsActive` | active mapping indicator |
| `MappedBy`, `MappedAtUtc` | initial administrator decision audit |
| `UpdatedBy`, `UpdatedAtUtc` | last mapping change audit |

Keep mapping changes auditable. Enforce at most one active mapping per producer with a PostgreSQL partial unique index following existing EF `.HasFilter(...)` conventions. A unique active producer-code rule is satisfied through the unique producer registry key plus one-active-mapping-per-`ImeProducerId` rule. Conflict status blocks resolution even if a mapping exists, until explicit conflict review clears/settles the producer identity. Mapping source is never inferred from names or seeded candidates.

Resolution behavior:

1. Upsert producer registry using normalized `ProducerCode`.
2. If producer is `Conflict`, retain the trade with no new company resolution.
3. If there is one active manual mapping, associate the trade with that exact `Companies.Id`.
4. Otherwise persist the trade with null company linkage and expose it as unmapped.

There is no exact-name company fallback. Company candidate similarity is for operator search only and cannot confirm a mapping.

## 7. Trade observation and immutability

Persist all 34 raw provider fields unchanged. Derived columns may include:

```text
ImeProducerId          Guid?
ProducerMappingId      Guid?
ResolvedCompanyId       Guid?  // selected Companies.Id
MappingResolvedAtUtc   timestamp with time zone?
SourceFingerprint      source-only deterministic observation identity
```

The physical shape may instead use a deterministic join. Provider fields and raw response evidence must be isolated from mutable producer/mapping metadata. Fingerprint inputs are canonicalized provider facts plus the approved natural-key/date scope; exclude registry state, mapping IDs/status, resolved company ID, and resolution timestamp. Database uniqueness backs idempotency.

Re-resolution selects observations by producer code/registry ID and changes only linkage metadata. It never changes the source fields, raw body, or fingerprint. A mapping update does not create a new observation.

## 8. Company candidate search and selection

Admin candidate search reads `FinancialIngestionDbContext.Companies` and supports `CompanySymbol` and `Name`; it may also search `TseSymbol`, `Ticker`, and `InstrumentCode` when available. Show enough fields to distinguish provider-scoped rows. Minimum display:

```text
Name | CompanySymbol | ProviderName | ExternalCompanyId
```

Also display `TseSymbol`, `Ticker`, `InstrumentCode`, and the selected row's ID when useful. Search results are candidates, not decisions. The administrator selects one concrete `Companies.Id`; ambiguous results are never resolved by first-match ordering or similarity scoring.

## 9. Admin Base Info screen

Feature 140 includes an Admin Base Info screen named **IME Producer Mapping** or a product-consistent localized equivalent such as **نگاشت تولیدکنندگان بورس کالا**. It is limited to this producer-mapping workflow and does not become a general company master-data tool.

The producer list displays code, observed name, last observed product symbol, last-seen time, mapping/conflict status, and mapped company name/symbol/provider. Operators can search producers, filter unmapped producers, inspect name variants/conflicts, open a producer, search candidate company rows, select exactly one row, create/change/disable mapping, explicitly review a conflict, and request historical re-resolution.

No auto-mapping and no generic company merge, alias manager, or commodity normalization UI is included.

## 10. Admin API

Use the repository route prefix `/api/v1/admin/...` and `AuthorizationPolicies.DataAdmin`. Routes are conceptual; final route/DTO names follow `AdminDataOperationsController` and neighboring API conventions. Required operations:

```text
GET producer list/search
GET unmapped producers
GET producer conflicts and observed name variants
GET company candidates by symbol/name and supported identity fields
POST/PUT manual producer mapping
POST disable mapping
POST explicit conflict review/resolution
POST request historical re-resolution
GET re-resolution run/status/progress as needed
```

Validate producer and selected `Companies.Id`, reject duplicate active mappings, and return repository-standard validation/error responses. Mutations and re-resolution triggers require DataAdmin authorization. Large re-resolution work uses existing Admin/background execution patterns, not a new queue.

## 11. Import result and raw response evidence

Retain complete raw provider response evidence and request outcome metadata separately from parsed observations. Record requested date, endpoint/request facts, status, attempts, request/completion times, result/failure, and parsed count using the existing provider acquisition/run conventions. Result summaries include inserted/updated/skipped counts plus mapped, unmapped, and conflicted producer counts.

Unmapped/conflicted producer counts are outcomes, not importer failures. A valid trade remains durable regardless of mapping state.

## 12. Scheduler and historical import

Use the existing Worker host and Tehran timezone-aware recurring-worker convention for Saturday–Wednesday at 17:00 `Asia/Tehran`. Use repository-supported shared idempotency/coordination if multiple Worker instances can schedule concurrently. Admin can request a historical day through the shared importer; reject future dates, retain requested-date integrity, and do not bypass normal validation or fingerprint behavior.

## 13. Product boundary

`GoodsName`, `Symbol`, `Category`, and other IME provider fields are immutable observations. A raw IME product/instrument identifier is not a canonical commodity concept. Feature 140 does not normalize commodity names or conflate product normalization with producer/company mapping. Any future commodity concept mapping is separate.

## 14. Test coverage

Cover: symbol-prefix parsing and malformed values; shared-code product symbols; producer registry upsert; normalized equivalent names; material conflicts and explicit review; unmapped ingestion; manual mapping FK to a selected provider row; company search identity display and ambiguous explicit selection; one active mapping; disabled mapping behavior; DataAdmin authorization; deterministic resolution; historical re-resolution; immutable raw source fields/response/fingerprint; retry/date/idempotency regressions; and Base Info workflow contracts.

## 15. Explicit non-goals

- Cross-provider canonical company identity, legal-company master identity, general MDM, or provider-row merging.
- Generic alias management or automatic legal-entity reconciliation.
- Automatic producer/company mapping by exact or fuzzy name.
- Commodity/product semantic normalization.
- Monthly IME sales aggregation, forecasting, Codal reconciliation, alerts, AI tools, or user-facing analytics.
