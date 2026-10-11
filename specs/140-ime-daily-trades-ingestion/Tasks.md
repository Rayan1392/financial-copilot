# Feature 140 — Implementation Tasks

## Execution rule

Before production changes, inspect repository conventions and the existing `Companies` model.

## Slice S1 — Repository discovery

### T140-01 — Audit existing architecture

Identify:

- scheduler/timezone infrastructure;
- Admin auth/routes;
- HTTP resilience;
- import lifecycle/raw payload persistence;
- EF/PostgreSQL conventions;
- `Companies` entity/table ownership;
- `Companies.Id` type;
- indexes/uniqueness around `CompanySymbol`, `Name`, `TseSymbol`, `InstrumentCode`;
- whether duplicate company rows exist across providers.

**Acceptance:** implementation note maps Feature 140 concepts to concrete repository types/paths.

## Slice S2 — IME ingestion foundation

### T140-02 — Provider fixtures and contracts

Keep all previously approved IME request/response fixtures and all 34 source fields.

### T140-03 — Date and HTTP client

Keep previously approved Jalali-date validation, raw-response retention, two-stage parsing, retry rules, and requested-date integrity.

### T140-04 — Raw trade persistence and fingerprint

Keep raw observation persistence and database idempotency.

**Important:** mapping fields must not participate in `SourceFingerprint`.

## Slice S3 — Producer identity extraction

### T140-05 — Add producer-code extraction

Implement deterministic extraction:

```text
KRPT-AMMONIAB-00 -> KRPT
SHPC-PBR1220WWP-00 -> SHPC
SHPC-OAVAMB-00 -> SHPC
```

Handle null/empty/malformed symbols safely.

### T140-06 — Persist producer-code metadata

Add `ImeProducerCode` as derived ingestion metadata or equivalent indexed resolution key without altering original `Symbol`.

## Slice S4 — Company mapping persistence

### T140-07 — Add IME producer-company mapping model

Create/reuse persistence equivalent to:

```text
ImeProducerCompanyMappings
ImeProducerCode
ImeProducerName
NormalizedProducerName
CompanyId -> Companies.Id
MappingStatus
MappingSource
IsActive
audit timestamps
```

### T140-08 — Enforce active mapping uniqueness

Prevent one active producer code from resolving to multiple active companies.

### T140-09 — Add company FK/indexes

Add FK to `Companies.Id` and repository-appropriate lookup indexes.

Do not duplicate `CompanySymbol` as canonical mapping data.

## Slice S5 — Resolver

### T140-10 — Implement producer-company resolver

Resolution priority:

1. active confirmed code mapping;
2. optional exact normalized-name candidate;
3. unresolved.

Do not confirm fuzzy matches automatically.

### T140-11 — Add conflict detection

Detect materially inconsistent producer identities for the same code and surface `Conflict` rather than guessing.

### T140-12 — Link imported trades

Store `ResolvedCompanyId` / `ProducerMappingId` or implement equivalent deterministic join-time linkage according to repository conventions.

Unmapped rows must still ingest successfully.

### T140-13 — Historical re-resolution

When mapping is created/changed, re-resolve affected historical observations without changing raw fields or `SourceFingerprint`.

## Slice S6 — Mapping Admin operations

### T140-14 — List mapping state

Add authorized operations to list:

- mappings;
- unmapped producer identities;
- conflicts.

### T140-15 — Create/update/disable mapping

Add authorized operations to map an IME producer identity to `Companies.Id`.

Validate company existence and mapping uniqueness.

### T140-16 — Re-resolution operation

Support re-resolution of affected historical rows after mapping changes using existing background/admin execution patterns.

Do not create a new queue solely for this feature.

## Slice S7 — Existing triggers

### T140-17 — Worker schedule and daily importer

Keep Saturday-Wednesday 17:00 `Asia/Tehran` and shared importer behavior.

Extend import result/telemetry with:

```text
RecordsMapped
RecordsUnmapped
MappingConflicts
```

### T140-18 — Historical Admin import

Keep previously approved one-day historical import behavior and future-date rejection.

## Slice S8 — Tests

### T140-19 — Producer-code tests

Cover:

- KRPT;
- SHPC with multiple product symbols;
- whitespace;
- null/empty;
- malformed symbols.

### T140-20 — Mapping persistence tests

Cover:

- FK to Companies;
- active uniqueness;
- disabled mapping behavior;
- no duplicated CompanySymbol truth;
- ambiguous company candidate rejection.

### T140-21 — Resolver tests

Cover:

- confirmed code mapping;
- unmapped producer;
- exact-name candidate;
- fuzzy match not auto-confirmed;
- conflict;
- two SHPC products resolving to one company;
- KRPT resolving to one company.

### T140-22 — Re-resolution tests

Cover:

- historical unmapped row becomes resolved after mapping creation;
- changed mapping updates linkage only;
- raw fields unchanged;
- SourceFingerprint unchanged.

### T140-23 — Regression tests

Run all ingestion, schedule, Admin, idempotency, raw-response, date-integrity, retry, and mapping tests.

## Acceptance-criteria traceability

| Task | ACs |
|---|---|
| T140-01 | AC-05, AC-06, AC-07, AC-16, AC-17 |
| T140-02 | AC-01, AC-02, AC-21 |
| T140-03 | AC-01, AC-21 |
| T140-04 | AC-01, AC-02, AC-12, AC-13, AC-21 |
| T140-05 | AC-03, AC-04, AC-18, AC-19 |
| T140-06 | AC-02, AC-03, AC-04 |
| T140-07 | AC-05, AC-06 |
| T140-08 | AC-07 |
| T140-09 | AC-05, AC-06 |
| T140-10 | AC-08, AC-09, AC-11, AC-19 |
| T140-11 | AC-10, AC-20 |
| T140-12 | AC-08, AC-09, AC-12 |
| T140-13 | AC-12, AC-13 |
| T140-14 | AC-14, AC-16 |
| T140-15 | AC-15, AC-16, AC-17 |
| T140-16 | AC-13, AC-16 |
| T140-17 | AC-01, AC-08, AC-09, AC-10, AC-21 |
| T140-18 | AC-01, AC-16, AC-21 |
| T140-19 | AC-03, AC-04, AC-18, AC-19 |
| T140-20 | AC-05, AC-06, AC-07, AC-17 |
| T140-21 | AC-08, AC-09, AC-10, AC-11, AC-18, AC-19, AC-20 |
| T140-22 | AC-12, AC-13 |
| T140-23 | AC-01 through AC-22 |

## Required implementation report

Create `ImplementationReport.md` with:

1. outcome;
2. repository architecture reused;
3. Companies model/index findings;
4. files changed;
5. migrations/schema;
6. provider ingestion results;
7. mapping model;
8. mapping seed/candidate evidence;
9. unresolved/conflict counts;
10. re-resolution behavior;
11. tests/build/regression;
12. operational smoke status;
13. remaining blockers.

Final successful marker:

```text
FEATURE_140_IME_INGESTION_AND_COMPANY_MAPPING_IMPLEMENTED
```
