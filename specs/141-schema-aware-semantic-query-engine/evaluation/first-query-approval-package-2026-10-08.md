# First Feature 141 Query: Narrow Approval Package

Status: **review requested; no approval recorded or applied**. This package is limited to one direct-source retrieval for one approved tenant/company pair. It does not authorize other providers, statement types, source items, operations, companies, or routes.

## Requested observation

- User question: “آخرین سود عملیاتی هر سهم کگل چقدر است؟”
- Company mention: `کگل` (company must still resolve from the persisted canonical `Companies` catalog at run time)
- Provider: `NoavaranCurrentApi`
- Statement type: `IncomeStatement`
- Provider item: `SourceItemId = 161`
- Source reference: `DirectSourceItem`
- Operation: `DirectValueRetrieval`; return the persisted `Value` as stored. No local calculation or metric substitution.
- Statement scope: `IsComposing = false` (the generic-path default is recorded as business-owner-confirmed)
- Period: maximum reporting `PeriodEnd` among otherwise eligible observations; ties remain typed `Ambiguous`.

## Source identity and financial meaning

The catalog key `(NoavaranCurrentApi, IncomeStatement, 161)` resolves to:

- Catalog reference: `source_item_682beceadddc1458`
- Persisted source-item UUID: `3cafc7d3-b0ab-4341-85ef-ebcbc9d60528`
- Provider title, preserved exactly: `سود عملیاتی هر سهم ` (there is trailing whitespace in the persisted title)
- Title meaning: “operating profit per share”; this is a provider label, not proof of a governed metric mapping.
- Identity status: `verified-persisted-source-key`
- `MetricCode`: null; no governed metric mapping is asserted.
- Source `Unit`: `N/A`; `sourceUnitStatus` is `unknown`; normalized unit is null.

**Reviewer decision required:** approve or reject using this provider-labeled source item as a direct reportable financial observation. If approved while the unit remains unknown, any result must expose that the unit is unknown and must not present a currency/per-share scale as verified. Do not derive operating profit per share from another value.

## Minimal physical object and field scope

The runtime compiler currently requires these four tables and only these columns. Column IDs are deterministic `<table-catalog-id>_<column-name>` references. Proposed table-level `approvedObjects` must contain exactly the following column IDs.

| Table | Table catalog ID | Columns required by the compiled query | Why needed |
|---|---|---|---|
| `public.FinancialStatementSourceItems` | `tbl_36036602dbf76362` | `Id`, `ProviderName`, `StatementType`, `SourceItemId`, `Unit` | Match the provider-scoped source UUID and return original source unit |
| `public.FinancialStatementLineItems` | `tbl_4f2ef2d8dd59b46a` | `Value`, `FinancialStatementId`, `SourceItemCatalogId` | Return stored value and join the statement/source identities |
| `public.FinancialStatements` | `tbl_53d51e26e1ca6929` | `Id`, `CompanyId`, `ProviderName`, `StatementType`, `PeriodEnd`, `PeriodType`, `IsComposing`, `IsAudited`, `IsRepresented`, `ExternalStatementId`, `PeriodStart`, `SourcePayloadChecksum`, `PublishedAt`, `LastSynchronizedAt`, `WarningsJson`, `LogicalVendor`, `SourceMode` | Scope by company/provider/type/consolidation, select latest period, preserve revision and provenance evidence |
| `public.Companies` | `tbl_8646d83cdb350f92` | `Id` | Join the resolved canonical company UUID |

No approval is requested for the other 14 curated tables or for unlisted columns. The separate PostgreSQL role script grants only these columns.

## Required relationships

All three relationships are currently semantically unapproved and runtime non-joinable. Proposed approval is limited to these exact catalog IDs:

| Relationship | Join | Cardinality / null behavior | Evidence and risk |
|---|---|---|---|
| `rel_cd8a77a2717ac71f` | `FinancialStatementLineItems.SourceItemCatalogId -> FinancialStatementSourceItems.Id` | many-to-one; line-item key is nullable; a physical FK uses `ON DELETE SET NULL` | Live catalog records FK. Null source IDs do not match the requested UUID and are excluded by the inner join. |
| `rel_2588cf95d01a651a` | `FinancialStatementLineItems.FinancialStatementId -> FinancialStatements.Id` | many-to-one; both keys are non-null | **Application-level association only; no physical PostgreSQL FK.** Orphan line items remain possible. A partial unique index limits a non-null source item to one line per statement, but no live duplicate/orphan scan was performed. |
| `rel_e4fa0fe989156533` | `FinancialStatements.CompanyId -> Companies.Id` | many-to-one; statement company key is nullable; a physical FK uses `ON DELETE SET NULL` | Live catalog records FK. Null company IDs do not match the bound canonical company UUID and are excluded by the inner join. |

**Reviewer decision required:** accept the logical line/header join and its orphan behavior for this scoped query. Metadata verification does not itself approve semantics or runtime access.

## Statement and revision policy

Confirmed policy already present in the catalog: `IsComposing=false` means parent-company/separate statement; `IsComposing=true` means consolidated. Audit and restatement are separate dimensions. No global audited-versus-unaudited or restated-versus-original preference is approved for this generic path. The query pins the provider and statement type, filters to `IsComposing=false`, chooses the maximum `PeriodEnd`, preserves `PeriodType` and revision/provenance fields, and returns `Ambiguous` if multiple eligible rows remain at that latest period. It does not break ties with `LastSynchronizedAt`, UUID order, or an invented audit/restatement preference.

**Reviewer decision required:** affirm that an unresolved same-period revision tie should be reported as ambiguous. If one row must win, document a Noavaran-specific precedence rule before changing the compiler or catalog.

## Separate approval states

1. **Metadata verified:** source identity and physical-schema provenance above have catalog evidence. The line/header relationship is application-verified, not a physical constraint. No current live observation, duplicate, orphan, or unit check was run.
2. **Semantics approved:** still pending. Requires reviewer acceptance of item 161's label as the intended direct value, the unknown unit treatment, the three relationship meanings, and ambiguity behavior.
3. **Query execution authorized:** still pending. Requires explicit per-source authorization and approved physical objects/columns/relationships, followed by a separately provisioned restricted Development role and an exact tenant/company test binding.

Business-owner-confirmed policy already recorded: the meaning/default of `IsComposing`. Not confirmed: the source item's financial equivalence/unit, any `MetricCode`, any revision precedence, and whether a tie should select an audit/restatement variant.

## Proposed catalog changes

The companion [JSON proposal](proposed-approval-changes-first-query-2026-10-08.json) enumerates the exact proposed source, four table/column, and three relationship edits. It is a review artifact only. No catalog file, approval flag, manifest, hierarchy, or hash has been changed for approval. The current `source-items` generation resets source approval to pending, so regenerate the source dictionary before applying any later recorded reviewer decision. After separately recorded semantic and authorization edits, rebuild the navigation hierarchy and lock, build the runtime snapshot, and validate all hashes. Do not add an approval signature on behalf of the reviewer.
