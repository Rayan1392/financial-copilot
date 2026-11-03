# Feature 140 — IME Daily Trades Ingestion and Producer Mapping

## Status

**READY_FOR_IMPLEMENTATION** after T140-01 repository discovery and the approved manual-mapping decision recorded in `RevisionReport.md`.

## Product intent

FinancialCopilot needs a durable daily history of Iran Mercantile Exchange (IME) trades and an operator-reviewed way to associate an IME producer with one existing company row in the repository's `Companies` table.

Feature 140 has two responsibilities:

1. ingest and preserve daily IME trade observations and raw response evidence;
2. discover IME producers and let an authorized administrator manually map a producer to one specific existing `NormalizedCompanyRow` (`FinancialIngestionDbContext.Companies`) selected from search results.

The selected row is a provider-scoped company record. This association does not declare that `Companies.Id` is a cross-provider canonical or legal-company identity. Feature 140 does not create a canonical company model or merge provider rows.

## Company-row identity contract

`FinancialIngestionDbContext.Companies` exposes `NormalizedCompanyRow`, persisted in PostgreSQL table `Companies`. Its `Id` is a `Guid`. A normalized row is provider-scoped and keyed for uniqueness by `(ProviderName, ExternalCompanyId)`; different provider rows for the same issuer may have different IDs.

An administrator must explicitly select one concrete row. Mapping data stores its `CompanyId` FK to `NormalizedCompanyRow.Id`. Candidate display and search use current values from that selected row, including `Name`, `CompanySymbol`, `ProviderName`, and `ExternalCompanyId`; do not copy `CompanySymbol` into mapping data as identity truth. `CompanySymbol`, `TseSymbol`, `Ticker`, and `InstrumentCode` are nullable, and `SymbolsIn` is not a `NormalizedCompanyRow` field.

## IME source and producer identity

IME trade records include provider-owned fields such as `GoodsName`, `Symbol`, `ProducerName`, `Category`, `ArzehKonandeh`, prices, quantities, dates, warehouse, broker, settlement, and lot/report identifiers. Preserve the 34 fields in `source-fields.json` exactly as received. Derived metadata must be stored separately.

The complete IME `Symbol` identifies a product/instrument. It is never the producer mapping key. Extract the normalized segment before the first hyphen as `ProducerCode`:

```text
IALC-ALHYD-00       -> IALC
SHPC-PBR1220WWP-00  -> SHPC
SHPC-OAVAMB-00      -> SHPC
```

Producer identity is primarily the stable `ProducerCode` plus observed `ProducerName`. Preserve raw `ProducerName` as provider evidence. Normalize a separate comparison value for registry deduplication and conflict detection only.

## Scope

Feature 140 includes daily IME HTTP ingestion, full raw response evidence, two-stage `{ d }` parsing, requested-date integrity, deterministic observation fingerprint/idempotency, all 34 source fields, producer-code extraction, automatic producer discovery/upsert, manual producer-to-company-row mappings, unmapped/conflict visibility, Admin APIs and an Admin Base Info screen, historical re-resolution, scheduled ingestion, and tests.

Feature 140 does not include:

- creation of a cross-provider canonical company identity or general company MDM;
- cross-provider company merging or generic alias management;
- monthly IME sales aggregation, forecasting, or Codal reconciliation;
- price/volume alerts, AI tools, chat answers, or user-facing IME analytics;
- commodity/product semantic normalization.

`GoodsName`, `Symbol`, and `Category` remain exact raw provider values. A raw IME product identity is not a canonical commodity concept; any commodity normalization belongs to a future separate feature/layer.

## Functional requirements

### FR-01 — Shared importer and schedule

Worker and Admin historical-import triggers use the same importer. Scheduled execution is Saturday–Wednesday at 17:00 `Asia/Tehran`. Admin historical import supports a requested day, rejects future dates, and applies the same parsing, preservation, idempotency, and result behavior.

### FR-02 — Raw response and trade preservation

Retain the complete raw provider response evidence, including request date, endpoint/request facts, HTTP status, attempt count, completion time, and success/failure outcome. Parse the provider's two-stage `{ d }` response form. Preserve all 34 provider fields exactly; never overwrite them with normalized or mapping values.

### FR-03 — Producer-code extraction

For each safe non-empty `Symbol`, trim surrounding whitespace, take the non-empty segment before its first `-`, normalize it invariantly for lookup, and keep the complete original symbol unchanged. Null, empty, or malformed symbols produce no invented code and do not fail an otherwise valid trade import.

### FR-04 — Producer registry

Persist an `ImeProducers` registry conceptually equivalent to:

```text
Id
ProducerCode
ProducerName
NormalizedProducerName
FirstSeenAtUtc
LastSeenAtUtc
LastObservedSymbol
Status
CreatedAtUtc
UpdatedAtUtc
```

The exact naming follows repository conventions. A normalized producer code identifies one registry entry. First observation creates the entry; later observations update last-seen metadata rather than creating duplicates. Multiple product symbols sharing a code refer to the same producer. Keep the latest display/raw name only as registry metadata; immutable trade observations remain the source evidence for every original `ProducerName` and `Symbol`.

### FR-05 — Name conflict detection

Compare a separately normalized producer name within each producer code. Whitespace, Unicode normalization, Arabic/Persian character variants, and harmless punctuation may normalize to the same textual identity under a deterministic rule. If a code is observed with materially different normalized business names, set/surface `Conflict`; do not merge names by fuzzy similarity or map new trades through the conflicted producer automatically. Preserve each raw name on its trade observation so Admin can review the variants. Conflict resolution requires an explicit administrative decision; no automatic legal-identity judgment is made.

Registry status must support at least `Unmapped`, `Mapped`, and `Conflict` (or equivalent). A conflict takes precedence over mapping for new resolution until reviewed. Historical linkage already written is changed only through the explicit re-resolution operation.

### FR-06 — Manual mapping persistence

Persist a separate mapping conceptually equivalent to:

```text
ImeProducerCompanyMappings
--------------------------
Id
ImeProducerId
CompanyId                  FK -> NormalizedCompanyRow.Id
MappingStatus
MappingSource              Manual
IsActive
MappedBy
MappedAtUtc
UpdatedBy
UpdatedAtUtc
```

Mapping source for this workflow is `Manual`. `CompanyId` identifies the specific provider-scoped row selected by the administrator. Do not store a copied company symbol/name as authoritative mapping data. Retain mapping audit history according to repository conventions.

### FR-07 — Mapping uniqueness and resolution

An IME producer code may have at most one active manual mapping to one selected `Companies.Id`. Enforce this with a database uniqueness constraint consistent with repository EF/PostgreSQL patterns. Disabled mappings do not resolve trades. Conflicted producers do not resolve automatically even if an active mapping exists until the conflict receives explicit administrative review.

During ingestion, resolve only a non-conflicted producer with one active manual mapping. Do not auto-map by producer name, company name, symbol similarity, seed guess, or fuzzy matching. Unmapped or conflicted trades are still persisted with null company linkage.

### FR-08 — Company candidate search and explicit selection

Admin can search existing `Companies` rows by `CompanySymbol` and `Name`; where useful, also support `TseSymbol`, `Ticker`, and `InstrumentCode`. Each result must show at least `Name`, `CompanySymbol`, `ProviderName`, and `ExternalCompanyId`, plus `TseSymbol`, `InstrumentCode`, `Ticker`, or other available fields where they help distinguish provider-scoped rows. When several rows match, require the administrator to choose a concrete `Companies.Id`; never select one silently.

### FR-09 — Ingestion and registry upsert flow

```text
receive IME response
  -> retain raw response evidence
  -> parse trade and preserve provider fields
  -> validate requested date and compute source-only fingerprint
  -> extract ProducerCode
  -> upsert/discover ImeProducer and observation metadata
  -> detect normalized-name conflict
  -> resolve one active manual mapping only when producer is non-conflicted
  -> persist trade with selected CompanyId or null
  -> return mapped / unmapped / conflict counts
```

Absence of a producer code, mapping, or company match must never block valid trade persistence. Registry and trade updates must be idempotent under repeated imports.

### FR-10 — Trade linkage and immutable observation identity

Trade storage may include `ImeProducerId`, `ProducerMappingId`, nullable `ResolvedCompanyId`, and `MappingResolvedAtUtc`, or an equivalent deterministic join. These are derived mutable linkage fields. The source fingerprint covers provider-owned observation facts and its documented natural-key scope only; it excludes all producer registry status, mapping data, and resolution timestamps.

### FR-11 — Historical re-resolution

After mapping creation/change or explicit conflict resolution, support bounded, resumable re-resolution of affected historical observations. Re-resolution may update only derived/linkage metadata. It must not modify any of the 34 source fields, original `Symbol`, original `ProducerName`, raw response evidence, or source fingerprint. Use established Admin/background execution patterns; do not add a new queue solely for Feature 140.

### FR-12 — Admin API and Base Info screen

Backend routes follow `/api/v1/admin/...` and `AuthorizationPolicies.DataAdmin`. Required capabilities: list/search producers, list unmapped producers, list conflicts and name variants, search company candidates, create/change/disable a mapping, perform explicit conflict review, and request re-resolution.

Provide an Admin Base Info screen named `IME Producer Mapping` or a product-consistent Persian equivalent (for example `نگاشت تولیدکنندگان بورس کالا`). It lists discovered producers with producer code/name, last symbol/seen time, status, and mapped company name/symbol/provider. The operator can filter unmapped records, search/open a producer, search and explicitly select a company row, create/change/disable the mapping, review conflicts, and request historical re-resolution. No automatic mapping is required.

### FR-13 — Source contract and retry behavior

Retain all 34 fields specified in `source-fields.json`; use provider fixtures for request/response and parser contracts. Apply bounded retries only to transient provider failures, preserve requested-date integrity, and enforce database-level observation idempotency.

## Acceptance criteria

1. **AC-01:** Scheduled and Admin imports use the same importer; existing date validation, raw evidence, two-stage parsing, bounded transient retry, and requested-date rules remain satisfied.
2. **AC-02:** All 34 IME source fields, including `GoodsName`, `Symbol`, `Category`, and `ProducerName`, are preserved unchanged.
3. **AC-03:** A valid symbol yields the normalized segment before the first hyphen, e.g. `IALC-ALHYD-00 -> IALC` and both `SHPC` product symbols yield `SHPC`.
4. **AC-04:** Multiple product symbols sharing a producer code and equivalent normalized producer name refer to one `ImeProducers` registry identity.
5. **AC-05:** The first observation automatically discovers/upserts a producer; repeat observations update last-seen metadata without creating duplicate producers.
6. **AC-06:** Newly discovered producers appear `Unmapped` until manually mapped; unmapped trades still persist successfully with null company linkage.
7. **AC-07:** Null/empty/malformed symbols neither fail valid trade ingestion nor produce an invented code.
8. **AC-08:** Same-code producer-name variants that normalize equivalently remain one identity; materially different names surface `Conflict` with reviewable raw variants and are not guessed together.
9. **AC-09:** Admin can search company rows by `CompanySymbol` and `Name` (and supported additional fields); results show at least name, symbol, provider, and external company ID.
10. **AC-10:** Ambiguous company search results require explicit administrator selection; no matching row is silently selected.
11. **AC-11:** A manual mapping stores one concrete `CompanyId` FK to a selected provider-scoped `NormalizedCompanyRow.Id`; its provider and external ID remain visible to the operator. No claim of cross-provider canonical identity is made.
12. **AC-12:** A non-conflicted producer with an active manual mapping deterministically links trades to the selected row; disabled/unmapped/conflicted producers do not auto-resolve.
13. **AC-13:** One producer code cannot have multiple active mappings; mapping create/change/disable and conflict-review actions require `DataAdmin`.
14. **AC-14:** The Admin Base Info screen lists/searches producers and supports unmapped filtering, company selection, mapping lifecycle, conflict visibility, and re-resolution.
15. **AC-15:** Creating/changing a mapping and requesting re-resolution can link affected historical trades; processing is bounded/resumable and reports progress/outcomes.
16. **AC-16:** Re-resolution changes only derived linkage; raw provider fields, raw response evidence, and source fingerprint are unchanged.
17. **AC-17:** Producer/company name or symbol similarity never creates a confirmed mapping automatically.
18. **AC-18:** Observation fingerprint and database uniqueness are deterministic and exclude producer registry/mapping/linkage metadata.
19. **AC-19:** Tests cover producer normalization, shared-code products, producer upsert, equivalent and conflicting names, manual mapping, explicit company selection, authorization, disabled mapping, and historical re-resolution immutability.
20. **AC-20:** No cross-provider company identity, MDM, company merge, monthly aggregation, forecasting, Codal reconciliation, alerts, AI tools, user-facing IME analytics, or commodity/product normalization is added.

## Explicit non-goals

- Cross-provider canonical company identity, legal-company master data, company merging, or general MDM.
- Generic alias management or automatic legal-entity reconciliation.
- Commodity/product semantic normalization or fuzzy `GoodsName` processing.
- Monthly aggregation, forecasting, Codal reconciliation, alerts, AI tools, or user-facing analytics.
