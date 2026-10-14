# Feature 140 — Manual Producer Mapping Revision Report

## 1. Revision outcome

The product/architecture revision is complete. T140-01 remains **COMPLETE**. The report `RepositoryDiscoveryReport.md` found that the physical `Companies` table stores provider-scoped `NormalizedCompanyRow` records identified by `(ProviderName, ExternalCompanyId)`, so a row's `Id` is not a cross-provider canonical or legal-company identity. It also found a domain `Company` type but no persisted cross-provider company target usable by the Feature 140 ingestion context.

The approved product decision resolves this blocker without creating a new identity model: an administrator manually associates an IME producer with one concrete existing `NormalizedCompanyRow` selected from `FinancialIngestionDbContext.Companies`. Specifications now describe this as a provider-scoped row association and explicitly avoid canonical-company claims.

## 2. Chosen manual Admin mapping solution

The importer derives normalized `ProducerCode` from the IME `Symbol` prefix, discovers/upserts the producer registry, and resolves only an active manual mapping after conflict checks. An authorized administrator searches existing company rows and selects one exact `Companies.Id`. Candidate display shows `Name`, `CompanySymbol`, `ProviderName`, and `ExternalCompanyId` at minimum. Duplicate/provider-specific results are not silently resolved.

Mapping source is `Manual`; there is no producer-name/company-name auto-map, seed-based mapping, or fuzzy matching. The selected ID is the target of this one reviewed association only. Feature 140 does not add cross-provider canonical identity, company merging, or general MDM.

## 3. IME producer identity and registry

The full IME `Symbol` is a product/instrument identity and is not the producer mapping key. The normalized non-empty segment before its first hyphen is the producer-code lookup key:

```text
IALC-ALHYD-00 -> IALC
SHPC-PBR1220WWP-00 -> SHPC
SHPC-OAVAMB-00 -> SHPC
```

Added conceptual `ImeProducers` registry requirements:

- one row per normalized producer code;
- first observation creates/discovers the row;
- subsequent observations update last-seen time and last observed Symbol/name metadata;
- multiple product Symbols sharing a code resolve to that same registry identity;
- preserve raw names and symbols on immutable trade observations;
- compare separately normalized producer names for conflict detection;
- status supports `Unmapped`, `Mapped`, and `Conflict` (or equivalent).

## 4. Mapping table semantics

`ImeProducerCompanyMappings` records `ImeProducerId`, concrete selected `CompanyId -> NormalizedCompanyRow.Id`, `MappingStatus`, `MappingSource=Manual`, active state, and administrator audit fields (`MappedBy`, `MappedAtUtc`, `UpdatedBy`, `UpdatedAtUtc`). Enforce at most one active mapping per producer, retain disabled history, and read company name/symbol/provider/external ID from the selected `Companies` row. Never copy a company symbol as authoritative mapping identity.

Trade linkage such as `ImeProducerId`, `ProducerMappingId`, `ResolvedCompanyId`, and `MappingResolvedAtUtc` is derived metadata and excluded from the raw observation fingerprint. Unmapped or conflicted trades remain stored with null company linkage.

## 5. Admin Base Info workflow

Added a Feature 140 Admin Base Info screen named **IME Producer Mapping** (or a product-consistent Persian equivalent such as **نگاشت تولیدکنندگان بورس کالا**). The screen lists discovered producers with code/name, last observed Symbol/time, state, and mapped company details. It supports producer search, unmapped filtering, opening a producer, candidate company search, explicit selection of one provider-scoped row, mapping create/change/disable, conflict visibility/review, and a historical re-resolution request.

The screen is limited to IME producer mapping. It does not build company MDM, cross-provider merging, generic alias management, or commodity normalization.

## 6. Conflict behavior

Name variants that normalize equivalently (including configured whitespace, Unicode, Arabic/Persian character, or harmless punctuation differences) remain equivalent. Materially different normalized business names for one producer code surface as `Conflict`; preserve raw variants on trade observations and expose them to Admin. Conflicted producers do not automatically resolve new trades even if a mapping exists. An explicit authorized, audited review action is required before resolution resumes. Fuzzy identity comparison never makes or resolves a mapping automatically.

## 7. Historical re-resolution behavior

When an administrator creates/changes a mapping or resolves a producer conflict, the system can re-resolve affected historical trades through a bounded, resumable Admin/background operation using existing repository execution patterns. It updates linkage metadata only. It never changes original `Symbol`, original `ProducerName`, any of the 34 source fields, raw response evidence, or source fingerprint. Mapping metadata is not part of observation identity.

## 8. Affected specification files, tasks, and acceptance criteria

Updated:

- `UserStory.md`: company-row semantics, producer registry/conflicts, manual mapping, company search, Admin screen, ingestion/re-resolution behavior, and acceptance criteria.
- `Design.md`: registry/mapping architecture, provider-scoped target, conflict handling, immutable observations, APIs/UI, and product boundary.
- `Tasks.md`: T140-01 remains complete; T140-02 through T140-23 retain their task numbers with adjusted scope; T140-24 was appended for the Admin Base Info screen.
- `SpecReview.md`: revised product/architecture verdict and remaining implementation checks.
- `README.md`: corrected mapping semantics and feature boundaries.
- `RevisionReport.md`: this decision record.

Acceptance criteria were revised to prove shared product-prefix identity, automatic producer discovery/upsert, unmapped persistence, company search and explicit selection, provider identity visibility, deterministic manual mapping, conflicts, DataAdmin authorization, Admin screen behavior, historical re-resolution, and unchanged source fields/evidence/fingerprint. `source-fields.json` is unchanged.

## 9. Remaining implementation checks

1. T140-02 must validate an actual IME request/response fixture for casing, field types, and optionality of all 34 fields.
2. Implementation must apply the repository's `FinancialIngestionDbContext`/EF migration and filtered unique-index conventions.
3. Confirm Worker deployment topology and use existing shared coordination/idempotency if multiple Worker instances may run the schedule.
4. Implement and test DataAdmin APIs, company candidate search, Base Info screen, conflict review, and bounded re-resolution.

These are implementation tasks, not unresolved specification decisions. No production code or schema was changed by this revision.

FEATURE_140_MANUAL_PRODUCER_MAPPING_REVISION_COMPLETE
