# Feature 140 — Product and Architecture Self-Review

## Verdict

**READY_FOR_IMPLEMENTATION**. T140-01 repository discovery is complete, and the former mapping-target blocker has an approved product decision documented in `RevisionReport.md`.

## Approved company-row semantics

Feature 140 manually associates an IME producer with one administrator-selected existing `NormalizedCompanyRow` in `FinancialIngestionDbContext.Companies`. The selected `Companies.Id` is provider-scoped row identity. It is not cross-provider canonical identity or legal-company master identity. Feature 140 does not create or imply a canonical company model, merge provider rows, or implement general MDM.

Admin company search must expose `Name`, `CompanySymbol`, `ProviderName`, and `ExternalCompanyId` at minimum. An administrator explicitly selects one concrete row; no ambiguous first-match behavior or automatic matching is permitted.

## Producer identity and registry decision

The normalized segment before the first hyphen in IME `Symbol` is a producer lookup key. The complete `Symbol` remains product/instrument identity and raw provider evidence. A single `ImeProducers` row per normalized producer code is discovered/upserted by ingestion. Raw trade `ProducerName`/`Symbol` values remain immutable; normalization exists only in separate registry/conflict metadata.

Equivalent textual name variants may share a producer identity after deterministic normalization. Materially different names for one code set `Conflict`, remain visible from preserved raw observations, block new automatic linkage, and require an audited explicit Admin review. Fuzzy identity resolution is prohibited.

## Mapping and ingestion safety

`ImeProducerCompanyMappings` stores a separate manual association (`MappingSource=Manual`) from `ImeProducerId` to the selected `Companies.Id`. There is at most one active mapping per producer. Unmapped/conflicted producers do not block trade ingestion. Linkage values and registry state are derived metadata and excluded from observation fingerprint inputs.

Historical re-resolution updates linkage only. It must not change any of the 34 IME provider fields, raw response evidence, or source fingerprint. The daily Worker and Admin historical import share the importer and existing repository execution patterns.

## Admin product scope

Feature 140 includes a focused Admin Base Info screen for IME producer mapping, plus DataAdmin-protected `/api/v1/admin/...` operations for producer lists/search, unmapped/conflict review, company candidate search, manual mapping lifecycle, conflict review, and historical re-resolution. This is not general company MDM or a commodity/product normalization feature.

## Risks and controls

| Risk | Control |
|---|---|
| Same code appears with distinct business names | Registry status `Conflict`, preserve raw variants, block new automatic linkage pending review |
| Multiple provider rows match company name/symbol | Show provider and external ID, require explicit `Companies.Id` selection |
| Mapping changes mutate observation identity | Exclude all mapping/registry data from source fingerprint |
| Historical observations remain unlinked | Authorized bounded/resumable re-resolution |
| Product symbol prefix is malformed | Safe null extraction; ingest observation without mapping |
| Product normalization is conflated with producer mapping | Preserve `GoodsName`, `Symbol`, and `Category`; commodity semantics remain out of scope |

## Remaining implementation checks

1. T140-02 must verify a real IME response fixture for types, optionality, and casing of all 34 fields.
2. Implementation must use the repository's `FinancialIngestionDbContext` migration and filtered unique-index conventions.
3. Confirm Worker deployment topology and use existing shared coordination/idempotency if more than one instance can run the schedule.
4. Build and test the manual mapping, Admin Base Info, DataAdmin authorization, conflict, and re-resolution flows.

No cross-provider canonical company identity work is required for Feature 140. No production implementation has begun.
