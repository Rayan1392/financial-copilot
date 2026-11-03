# Feature 140 — IME Daily Trades Ingestion and Producer Mapping

This package specifies:

- daily IME trade ingestion with immutable preservation of all 34 provider fields and raw response evidence;
- producer-code extraction from the IME product `Symbol` prefix;
- automatic discovery/upsert in a single `ImeProducers` registry row per normalized code;
- producer-name conflict detection and explicit Admin review;
- manual mapping from an IME producer to one selected provider-scoped `NormalizedCompanyRow` in `FinancialIngestionDbContext.Companies`;
- Admin APIs and a focused Admin Base Info mapping screen;
- historical re-resolution that changes linkage metadata only.

## Company mapping rule

```text
normalized IME ProducerCode
  -> one ImeProducers registry entry
  -> one active manual mapping
  -> administrator-selected NormalizedCompanyRow.Id (Companies.Id)
```

`Companies` rows are provider-scoped by `(ProviderName, ExternalCompanyId)`. The selected `Companies.Id` means that the administrator associated this producer with that specific existing row. It does not mean cross-provider canonical or legal-company identity. Admin candidate results must show at least `Name`, `CompanySymbol`, `ProviderName`, and `ExternalCompanyId`, and the operator must explicitly select one concrete row.

## Producer and product identity

`IALC-ALHYD-00 -> IALC`; `SHPC-PBR1220WWP-00` and `SHPC-OAVAMB-00` both yield `SHPC`. The full `Symbol` identifies a product/instrument, not the producer mapping key. Preserve `Symbol`, `ProducerName`, `GoodsName`, `Category`, and all other source fields exactly as received. Name normalization is separate metadata for equivalence/conflict checks; materially different names create a reviewable conflict, never a fuzzy auto-merge.

Unmapped/conflicted producers do not block trade ingestion. Mapping/registry/linkage fields are excluded from the raw observation fingerprint. Historical re-resolution updates linkage metadata only.

## Status and scope

T140-01 repository discovery is complete. The approved identity decision and revision details are in `RevisionReport.md`; detailed implementation requirements are in `UserStory.md`, `Design.md`, and `Tasks.md`.

Feature 140 does not build a canonical company model, company MDM, provider-row merging, generic alias management, commodity/product normalization, monthly aggregation, forecasting, Codal reconciliation, alerts, AI tools, or user-facing IME analytics.
