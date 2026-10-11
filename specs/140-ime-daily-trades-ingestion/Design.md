# Feature 140 — Design: IME Daily Trades Ingestion + Company Mapping

## 1. Design status

**READY_FOR_REPOSITORY_VALIDATION**

This revision extends Feature 140 with deterministic IME producer → FinancialCopilot company mapping.

## 2. Design principles

1. Raw provider facts remain immutable.
2. Mapping is a separate identity layer, not a mutation of source data.
3. `Companies.Id` is the canonical company target.
4. IME symbol prefix is a lookup key, not self-proving identity.
5. Unmapped rows are valid ingestion results.
6. Conflicts are surfaced, not guessed away.
7. Historical observations must be re-resolvable after mapping updates.

## 3. Logical architecture

```text
IME API
  |
  v
IME Daily Trade Importer
  |
  +--> Raw response evidence
  |
  +--> ImeDailyTrades
          |
          +--> derive ImeProducerCode
          |
          v
     IME Producer Resolver
          |
          v
ImeProducerCompanyMappings
          |
          v
      Companies
      Id (canonical)
      CompanySymbol
      Name
      TseSymbol
      InstrumentCode
```

## 4. Mapping model

Conceptual table:

```text
ImeProducerCompanyMappings
```

Suggested columns:

| Column | Purpose |
|---|---|
| Id | mapping identity |
| ImeProducerCode | normalized prefix such as KRPT / SHPC |
| ImeProducerName | observed producer display value |
| NormalizedProducerName | normalized lookup value |
| CompanyId | FK -> Companies.Id |
| MappingStatus | Confirmed / Conflict / Disabled |
| MappingSource | Manual / Seeded / ExactProducerName / ReviewedAutomatic |
| IsActive | active mapping flag |
| CreatedAtUtc | audit |
| UpdatedAtUtc | audit |

Optional audit fields should reuse repository conventions.

### 4.1 Why not map directly to CompanySymbol?

`CompanySymbol` is descriptive business data and may change or have aliases.

The stable relation is:

```text
ImeProducer -> Companies.Id
```

Then user-facing symbol is obtained from:

```text
Companies.CompanySymbol
```

### 4.2 Producer-code extraction

For:

```text
KRPT-AMMONIAB-00
```

derive:

```text
KRPT
```

For:

```text
SHPC-PBR1220WWP-00
SHPC-OAVAMB-00
```

derive:

```text
SHPC
```

Algorithm:

```text
trim Symbol
find first '-'
take non-empty prefix
normalize invariantly
store original Symbol unchanged
```

If a safe prefix cannot be extracted, `ImeProducerCode = null`.

### 4.3 Resolution semantics

Resolution order:

```text
1. confirmed active code mapping
2. optional exact normalized ProducerName candidate
3. unresolved
```

A candidate is not automatically a confirmed mapping unless an existing deterministic repository rule explicitly permits it.

### 4.4 Conflict semantics

Conflict examples:

```text
same ImeProducerCode
-> multiple materially different ProducerName values
-> candidate companies differ
```

Result:

```text
MappingStatus = Conflict
ResolvedCompanyId = null
```

until reviewed.

## 5. Trade model changes

Keep all original source fields.

Add derived/link metadata:

```text
ImeProducerCode       string?
ResolvedCompanyId     uuid? FK Companies.Id
ProducerMappingId     uuid? FK ImeProducerCompanyMappings
MappingResolvedAtUtc  timestamp?
```

If the repository prefers join-time resolution, `ResolvedCompanyId` may be omitted from the trade row, but mapping semantics must remain deterministic and efficient for future company aggregation.

## 6. Historical re-resolution

Raw observations are immutable.

When a new mapping is created or corrected:

```text
mapping change
   ->
re-resolve affected historical rows
   ->
update only mapping metadata/linkage
   ->
never modify raw provider fields/fingerprint
```

If `SourceFingerprint` currently includes only provider source fields, mapping changes must not change the fingerprint.

## 7. Mapping lifecycle

Recommended service boundaries:

```text
IImeProducerCodeExtractor
IImeProducerCompanyResolver
IImeProducerMappingService
```

Conceptual responsibilities:

- extract producer code;
- resolve current mapping;
- list unmapped producer identities;
- detect conflicts;
- create/update/disable mappings;
- re-resolve historical observations.

Reuse existing application/service patterns rather than creating parallel frameworks.

## 8. Companies lookup

Repository discovery must inspect `Companies` and confirm:

- canonical PK type for `Id`;
- indexes on `CompanySymbol`, `Name`, `TseSymbol`, `InstrumentCode`;
- provider-specific duplicates;
- whether multiple company rows can represent the same legal entity/provider.

Mapping creation must always target a concrete `Companies.Id`.

If duplicate candidate company rows exist, require explicit selection.

## 9. Seed strategy

Do not hardcode company GUIDs.

Optional seed candidates:

```text
KRPT -> Companies row for پتروشیمی کرمانشاه / کرماشا
SHPC -> Companies row for پتروشیمی شازند / شاراک
```

The implementation must query/verify actual rows before persisting mappings.

If no unique company candidate exists, leave unmapped/conflict.

## 10. Admin API additions

Follow existing Admin conventions.

Conceptual endpoints:

```text
GET  /api/admin/ime/producer-mappings/unmapped
GET  /api/admin/ime/producer-mappings/conflicts
GET  /api/admin/ime/producer-mappings
POST /api/admin/ime/producer-mappings
PUT  /api/admin/ime/producer-mappings/{id}
POST /api/admin/ime/producer-mappings/{id}/disable
POST /api/admin/ime/producer-mappings/{id}/re-resolve
```

Exact routes are repository-dependent.

No UI is required.

## 11. Import flow with mapping

```text
Receive IME response
  ->
retain raw response
  ->
parse rows
  ->
validate target date
  ->
compute observation fingerprint
  ->
extract ImeProducerCode
  ->
resolve confirmed mapping if available
  ->
persist raw observation + mapping metadata
  ->
report inserted/skipped/unmapped/conflict counts
```

Suggested additional import counters:

```text
RecordsMapped
RecordsUnmapped
MappingConflicts
```

## 12. Idempotency interaction

Mapping metadata must not participate in `SourceFingerprint`.

Reason:

```text
provider observation identity != company mapping state
```

A later mapping correction must not create a duplicate raw observation.

## 13. Query-readiness

Feature 140 does not aggregate sales, but after mapping the data becomes suitable for a future query:

```text
ImeDailyTrades
  where ResolvedCompanyId = X
  and date between ...
```

or equivalent join through the mapping table.

## 14. Tests

Add coverage for:

- producer-code extraction;
- two SHPC symbols -> same SHPC mapping;
- KRPT symbol -> KRPT mapping;
- unresolved producer;
- conflict detection;
- mapping uniqueness;
- mapping to Companies.Id;
- re-resolution after new mapping;
- mapping changes not affecting SourceFingerprint;
- no raw field mutation;
- Admin mapping authorization;
- duplicate/ambiguous company candidates.

All previously approved ingestion tests remain required.

## 15. Explicit non-goals

No implementation of:

- monthly sales aggregation;
- sales forecasting;
- Codal reconciliation;
- price/volume alerts;
- AI tools;
- user-facing analytical IME questions.
