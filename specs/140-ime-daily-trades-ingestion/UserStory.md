# Feature 140 — IME Daily Trades Ingestion and Company Mapping

## Status

Planned. Specification package revised to include deterministic mapping between IME producers and FinancialCopilot `Companies`.

Repository-specific class names, routes, scheduler technology, EF conventions, and authorization policies must still be validated during implementation.

## Product intent

FinancialCopilot needs a durable daily history of Iran Mercantile Exchange (IME) transactions and a reliable identity bridge from IME producer identities to listed companies already stored in `Companies`.

The feature now has two bounded responsibilities:

1. ingest and preserve daily IME trade observations;
2. resolve each IME producer identity to a `Companies.Id` through an explicit mapping layer.

The strategic purpose is to make future company-level commodity-exchange analytics possible without query-time scraping or fuzzy identity guesses.

Future features may then aggregate IME sales by company and estimate monthly sales before Codal publication.

## Companies contract

The existing `Companies` table is the canonical FinancialCopilot company identity source.

The mapping must reference:

```text
Companies.Id
```

and may expose/use existing descriptive columns such as:

```text
Name
CompanySymbol
TseSymbol
InstrumentCode
SymbolsIn
ProviderName
ExternalCompanyId
```

`CompanySymbol` is the expected stock/company symbol for most user-facing company identity purposes, but Feature 140 must not duplicate it into the mapping as the canonical value. The mapping points to `Companies.Id`; symbol/name values are read from `Companies`.

## Source contract

IME rows contain, among other fields:

```text
Symbol
ProducerName
ArzehKonandeh
```

Observed examples:

```text
KRPT-AMMONIAB-00
ProducerName = پتروشیمی کرمانشاه
```

```text
SHPC-PBR1220WWP-00
ProducerName = پتروشیمی شازند
```

```text
SHPC-OAVAMB-00
ProducerName = پتروشیمی شازند
```

The leading segment of `Symbol` is treated as an IME producer code candidate:

```text
KRPT-AMMONIAB-00 -> KRPT
SHPC-PBR1220WWP-00 -> SHPC
```

This producer code is useful for deterministic lookup but is not assumed to be globally correct without an explicit mapping record.

## Scope

Feature 140 includes:

1. IME daily HTTP ingestion.
2. Raw response retention.
3. Parsed trade persistence.
4. Deterministic observation fingerprint/idempotency.
5. Worker schedule and Admin manual import.
6. Extraction of `ImeProducerCode` from IME `Symbol`.
7. Explicit IME producer → `Companies.Id` mapping persistence.
8. Mapping resolution for ingested observations.
9. Detection/reporting of unmapped or conflicting producers.
10. Admin/service operations required to review/create/update producer mappings.
11. Tests for mapping resolution and mapping safety.

Feature 140 still does NOT include:

- monthly aggregation;
- sales prediction;
- Codal comparison;
- alerts;
- AI/LLM tools;
- chat answers;
- automated investment conclusions.

## Functional requirements

### FR-01 — Shared importer

Worker and Admin triggers must use the same import service.

### FR-02 — Raw preservation

All IME provider fields remain preserved exactly as provider facts. Company identity resolution is additive metadata and must not overwrite `ProducerName`, `Symbol`, or `ArzehKonandeh`.

### FR-03 — Producer-code extraction

For each non-empty IME `Symbol`, derive `ImeProducerCode` from the segment before the first `-`.

Examples:

```text
KRPT-AMMONIAB-00 -> KRPT
SHPC-OAVAMB-00   -> SHPC
```

The derivation must:

- trim surrounding whitespace;
- use an invariant normalization suitable for lookup;
- preserve the original `Symbol`;
- fail safely when the symbol is null/empty/malformed;
- not invent a code when no safe segment exists.

### FR-04 — Mapping table

Create a dedicated mapping model conceptually equivalent to:

```text
ImeProducerCompanyMappings
--------------------------
Id
ImeProducerCode
ImeProducerName
NormalizedProducerName
CompanyId              -> FK Companies.Id
MappingStatus
MappingSource
IsActive
CreatedAtUtc
UpdatedAtUtc
```

Exact column naming must follow repository conventions.

`CompanyId` is canonical. Do not duplicate `CompanySymbol` as an authoritative mapping value.

### FR-05 — Mapping identity and uniqueness

An active IME producer code should resolve to at most one active company mapping.

The database must prevent ambiguous active mappings for the same normalized producer code.

If repository patterns support filtered unique indexes, prefer an active-only unique constraint. Otherwise use an equivalent repository-approved design.

### FR-06 — Mapping status/source

At minimum distinguish:

```text
Confirmed
Unmapped
Conflict
Disabled
```

and mapping provenance such as:

```text
Manual
ExactProducerName
Seeded
ReviewedAutomatic
```

Do not silently promote a fuzzy name match to a confirmed mapping.

### FR-07 — Resolution order

For an ingested row:

1. extract `ImeProducerCode`;
2. resolve an active confirmed mapping by producer code;
3. if no code mapping exists, optionally produce a name-based candidate using normalized `ProducerName`;
4. name-based candidates are suggestions unless repository/business rules explicitly mark an exact deterministic rule as safe;
5. unresolved rows remain ingested successfully as raw observations.

Unmapped identity must never cause loss of valid IME trade data.

### FR-08 — Mapping target

The mapping target is `Companies.Id`.

Once resolved, future company-level queries obtain:

```text
Companies.CompanySymbol
Companies.Name
...
```

through the company relation.

Do not map directly to a copied stock symbol string.

### FR-09 — Trade linkage

Do not mutate provider fields.

Preferred model:

```text
ImeDailyTrade
  Raw provider fields
  ImeProducerCode
  ResolvedCompanyId?   (nullable FK Companies.Id)
  MappingId?           (nullable FK)
```

If existing architecture strongly prefers resolution through joins rather than denormalized nullable FKs, repository discovery may choose that implementation, but the semantics must remain equivalent.

### FR-10 — Historical remapping

Changing a producer mapping must not rewrite or destroy the raw provider observation.

The implementation must support future re-resolution of historical rows.

Preferred behavior:

- raw observation remains immutable;
- mapping metadata may be recomputed;
- mapping changes are auditable;
- no raw source field is modified.

### FR-11 — Conflict detection

If one producer code appears with materially different producer names suggesting multiple identities, record/report a conflict rather than auto-binding to multiple companies.

Examples of differences caused only by whitespace/Unicode normalization may be normalized; business-distinct names must not be collapsed without review.

### FR-12 — Admin mapping operations

Provide backend operations, following existing Admin API patterns, for at least:

```text
list unmapped producer identities
list mapping conflicts
create/update/disable a mapping
resolve mapping to Companies.Id
```

No Admin UI is required in Feature 140.

### FR-13 — Mapping seed/bootstrap

Repository implementation may seed obvious mappings only when evidence is explicit and reviewable.

Examples may include:

```text
KRPT -> company whose CompanySymbol is کرماشا
SHPC -> company whose CompanySymbol is شاراک
```

but implementation must verify the actual `Companies` records before inserting seed mappings.

Do not hardcode `Companies.Id`.

### FR-14 — Existing ingestion requirements

All previously approved ingestion requirements remain:

- two-stage `{ d }` parsing;
- raw response evidence retention;
- requested-date integrity;
- idempotent observation persistence;
- Saturday-Wednesday 17:00 `Asia/Tehran`;
- Admin historical import;
- future-date rejection;
- bounded retries for transient provider failures only;
- database-level uniqueness.

## Acceptance criteria

1. **AC-01:** IME daily ingestion behavior remains compatible with the previously approved Feature 140 contract.
2. **AC-02:** Every parsed row preserves original `Symbol`, `ProducerName`, and all other provider fields.
3. **AC-03:** `ImeProducerCode` is deterministically extracted from valid IME symbols such as `KRPT-AMMONIAB-00 -> KRPT`.
4. **AC-04:** Null/empty/malformed symbols do not cause import failure and do not produce invented producer codes.
5. **AC-05:** A dedicated mapping persistence model links IME producer identity to `Companies.Id`.
6. **AC-06:** `CompanyId` is the canonical mapping target; `CompanySymbol` is read from `Companies`, not duplicated as mapping truth.
7. **AC-07:** One active producer code cannot resolve to multiple active companies.
8. **AC-08:** A confirmed code mapping resolves ingested rows to the expected `Companies.Id`.
9. **AC-09:** An unmapped producer does not block ingestion and remains queryable as unmapped.
10. **AC-10:** A conflicting producer identity is surfaced as conflict and is not silently auto-mapped.
11. **AC-11:** Fuzzy/name similarity alone cannot create a confirmed mapping without explicit review or an approved deterministic rule.
12. **AC-12:** Mapping changes do not modify raw IME provider fields.
13. **AC-13:** Historical rows can be re-resolved after a mapping is created/changed.
14. **AC-14:** Backend Admin operations can list unmapped/conflicting producers.
15. **AC-15:** Backend Admin operations can create/update/disable a mapping to `Companies.Id`.
16. **AC-16:** Mapping operations follow existing authorization and API conventions.
17. **AC-17:** Mapping seed data, if used, resolves by existing company records and never hardcodes `Companies.Id`.
18. **AC-18:** Tests cover `KRPT` and `SHPC` style producer-code extraction and multi-product reuse of the same producer code.
19. **AC-19:** Tests prove two different IME product symbols with the same producer prefix resolve to the same company mapping.
20. **AC-20:** Tests cover producer-name normalization without collapsing materially different business identities.
21. **AC-21:** Existing raw-response, date-integrity, retry, scheduling, and idempotency ACs continue to pass.
22. **AC-22:** No monthly aggregation, forecasting, alerts, AI tools, or user-facing IME analytics are introduced.

## Out of scope

- Monthly/company sales aggregation.
- Sales prediction.
- Codal reconciliation.
- Price/volume alerts.
- LLM tools.
- User-facing analytical responses.
- Automated fuzzy entity resolution that confirms mappings without review.
