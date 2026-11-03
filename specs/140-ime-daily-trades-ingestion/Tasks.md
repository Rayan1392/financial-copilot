# Feature 140 — Implementation Tasks

## Execution rule

T140-01 repository discovery is complete. The approved identity decision is manual association from an IME producer to one explicitly selected provider-scoped `NormalizedCompanyRow` in `FinancialIngestionDbContext.Companies`. Do not create a cross-provider canonical company model as part of this feature. Do not start implementation until the implementation task is authorized.

## Slice S1 — Repository discovery

### T140-01 — Audit existing architecture — COMPLETE

Discovery findings and the approved follow-up decision are recorded in `RepositoryDiscoveryReport.md` and `RevisionReport.md`. The selected mapping target is a concrete existing provider-scoped `Companies.Id`, not a canonical legal-company identity.

**Acceptance:** completed repository evidence maps Feature 140 concepts to concrete types/paths; provider-scope semantics and identity decision are explicit.

## Slice S2 — IME ingestion foundation

### T140-02 — Provider fixtures and contracts

Keep/confirm request and response fixtures, including all 34 fields in `source-fields.json`. Confirm real response casing, field types, optionality, and the three example product symbols through fixtures.

### T140-03 — Date and HTTP client

Implement the approved Jalali-date validation, typed HTTP client, raw-response retention, two-stage `{ d }` parsing, requested-date integrity, and bounded retry rules for transient failures only.

### T140-04 — Raw trade persistence and fingerprint

Persist every provider field unchanged and retain raw response evidence. Add database-level idempotency using a deterministic source-only fingerprint and the approved natural-key/date scope. Producer registry, mapping, conflict, and resolved-company metadata must not participate in `SourceFingerprint`.

## Slice S3 — Producer identity and registry

### T140-05 — Add producer-code extraction

Extract the normalized non-empty prefix before the first hyphen:

```text
IALC-ALHYD-00 -> IALC
SHPC-PBR1220WWP-00 -> SHPC
SHPC-OAVAMB-00 -> SHPC
```

The full `Symbol` remains a product/instrument identity and is not the producer mapping key. Handle whitespace, null, empty, and malformed symbols safely.

### T140-06 — Add IME producer registry and observation upsert

Add conceptual `ImeProducers` persistence with normalized `ProducerCode`, latest observed display/comparison name, first/last seen timestamps, last full `Symbol`, status, and audit timestamps. Enforce one registry identity per normalized producer code. Discover a producer during ingestion; subsequent product observations update last-seen metadata without duplicating the registry entry. Preserve raw names/symbols on each immutable trade observation.

## Slice S4 — Manual company-row mapping

### T140-07 — Add manual producer-company mapping model

Create `ImeProducerCompanyMappings` with `ImeProducerId`, `CompanyId -> NormalizedCompanyRow.Id`, status/source, active flag, and `MappedBy`/`MappedAtUtc`/`UpdatedBy`/`UpdatedAtUtc` audit fields. The mapping source is `Manual`. Do not describe the selected row as cross-provider canonical and do not copy its symbol/name into mapping identity.

### T140-08 — Enforce producer and active mapping uniqueness

Enforce one producer registry record per normalized producer code and at most one active mapping per `ImeProducerId`. Use existing PostgreSQL filtered unique-index conventions for active mappings. Preserve disabled mapping history.

### T140-09 — Add company-row FK and lookup indexes

Add the FK to `FinancialIngestionDbContext.Companies.Id` (the selected `NormalizedCompanyRow`), set explicit delete behavior, and add repository-appropriate producer/status/CompanyId lookup indexes. Add company candidate indexes only where justified by actual search patterns and existing schema conventions.

## Slice S5 — Conflict-aware resolver and trade linkage

### T140-10 — Implement producer/company resolver

Resolve only a non-conflicted producer with exactly one active manual mapping. Do not use exact-name or fuzzy company matching for automatic mapping. Persist unmapped or conflicted trades with null company linkage.

### T140-11 — Add producer-name conflict detection and review

Compare separately normalized names for the same code. Treat configured Unicode, whitespace, Arabic/Persian character, and harmless punctuation variants as equivalent. Mark materially different names `Conflict`, preserve raw variants through trade observations, block automatic resolution for new trades, and provide explicit audited Admin conflict review. Do not make fuzzy legal-identity decisions.

### T140-12 — Link imported trades

Store `ImeProducerId`, nullable `ProducerMappingId`, nullable `ResolvedCompanyId`, and resolution timestamp, or use an equivalent deterministic join. Unmapped/conflicted rows remain valid observations. All linkage fields are derived metadata and excluded from the observation fingerprint.

### T140-13 — Historical re-resolution

After an authorized mapping change or conflict review, re-resolve affected historical observations. Update linkage only; preserve all raw source fields, raw response evidence, and `SourceFingerprint`. Process in bounded, resumable batches.

## Slice S6 — Admin API and company search

### T140-14 — List/search producer mapping state

Add DataAdmin-protected operations to search/list producers and to list unmapped producers and conflicts with observed name variants and current mapping summary.

### T140-15 — Company search and mapping lifecycle

Add Admin search over existing `Companies` rows by at least `CompanySymbol` and `Name`, optionally `TseSymbol`, `Ticker`, and `InstrumentCode`. Return enough identity data to distinguish provider duplicates: `Name`, `CompanySymbol`, `ProviderName`, and `ExternalCompanyId` at minimum. Require an explicit concrete `Companies.Id` selection. Add create/change/disable manual mapping operations, validate target existence and uniqueness, and audit the operator. No automatic selection or mapping.

### T140-16 — Conflict review and re-resolution operation

Add authorized explicit conflict review/resolution and a trigger for historical re-resolution, with status/progress/outcome reporting as needed. Use existing background/Admin execution patterns; do not create a new queue solely for this feature.

## Slice S7 — Existing triggers and Admin Base Info

### T140-17 — Worker schedule and shared daily importer

Run Saturday–Wednesday at 17:00 `Asia/Tehran` through repository Worker conventions and shared coordination/idempotency as required by deployment topology. Worker uses the same importer as Admin. Report inserted/updated/skipped, mapped/unmapped/conflict counts and raw-response/import outcome facts.

### T140-18 — Historical Admin import

Keep one-day historical import behavior, future-date rejection, requested-date integrity, and shared importer behavior. Require `DataAdmin` authorization and report outcomes.

### T140-24 — Add Admin Base Info screen for IME producer mapping

Add a focused screen named `IME Producer Mapping` or approved Persian equivalent. Display producer code/name, last observed symbol/time, status, and mapped company name/symbol/provider. Support producer search, unmapped filter, producer detail, company candidate search, explicit company-row selection, mapping create/change/disable, conflict inspection/review, and historical re-resolution request. Keep the screen limited to Feature 140 mapping; do not build general company MDM or commodity normalization.

## Slice S8 — Tests

### T140-19 — Producer-code and registry tests

Cover IALC, SHPC multi-product identity, whitespace, null/empty/malformed symbols, normalized code uniqueness, first-seen discovery, and repeat-observation upsert.

### T140-20 — Mapping persistence and company search tests

Cover Company FK to a concrete normalized company row, active uniqueness, disabled mapping, no copied symbol truth, company search fields, provider-scoped duplicate visibility, and ambiguous candidate selection requiring an explicit ID.

### T140-21 — Resolver and conflict tests

Cover active manual mapping, unmapped ingestion, no automatic exact/fuzzy company mapping, equivalent producer-name normalization, materially different-name conflict, conflict blocking new resolution, audited conflict review, and several product symbols sharing one producer mapping.

### T140-22 — Re-resolution and immutability tests

Cover historical unmapped rows becoming linked after mapping, mapping changes updating only linkage, bounded/resumable processing, and unchanged provider fields/raw evidence/fingerprint.

### T140-23 — Regression and authorization tests

Run ingestion, schedule, Admin API/DataAdmin authorization, company search, idempotency, raw-response, date-integrity, retry, registry, mapping, and re-resolution regression suites.

## Acceptance-criteria traceability

| Task | ACs |
|---|---|
| T140-01 | AC-11 |
| T140-02 | AC-01, AC-02 |
| T140-03 | AC-01 |
| T140-04 | AC-02, AC-16, AC-18 |
| T140-05 | AC-03, AC-07 |
| T140-06 | AC-04, AC-05, AC-06 |
| T140-07 | AC-11, AC-13 |
| T140-08 | AC-13 |
| T140-09 | AC-11, AC-13 |
| T140-10 | AC-06, AC-12, AC-17 |
| T140-11 | AC-08, AC-12 |
| T140-12 | AC-12, AC-16, AC-18 |
| T140-13 | AC-15, AC-16 |
| T140-14 | AC-06, AC-08, AC-14 |
| T140-15 | AC-09, AC-10, AC-11, AC-13, AC-17 |
| T140-16 | AC-08, AC-13, AC-15 |
| T140-17 | AC-01, AC-05, AC-12 |
| T140-18 | AC-01 |
| T140-19 | AC-03, AC-04, AC-05, AC-07 |
| T140-20 | AC-09, AC-10, AC-11, AC-13 |
| T140-21 | AC-04, AC-08, AC-12, AC-17 |
| T140-22 | AC-15, AC-16, AC-18 |
| T140-23 | AC-01 through AC-20 |
| T140-24 | AC-14 |

## Required implementation report

Create `ImplementationReport.md` with:

1. outcome;
2. repository architecture reused;
3. provider-scoped `Companies` row/index findings and selected mapping semantics;
4. files changed;
5. migrations/schema;
6. provider ingestion results;
7. producer registry and mapping model;
8. candidate search and manual mapping evidence;
9. unmapped/conflict counts;
10. re-resolution behavior;
11. tests/build/regression;
12. operational smoke status;
13. remaining blockers.

Final successful marker:

```text
FEATURE_140_IME_INGESTION_AND_COMPANY_MAPPING_IMPLEMENTED
```
