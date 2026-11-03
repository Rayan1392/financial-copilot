# Feature 141 — Phase 0/1 execution report

Date: 2026-10-08  
Scope: repository/schema inventory, live PostgreSQL metadata discovery, EF reconciliation, initial catalog generation, and bounded financial profiling.

This report records the work completed in this execution. Owner review of candidate scope and a staging/replica audit remain exit gates. No nonempty environment or `.env` connection override was available, so the CLI used the repository API development-settings connection; the snapshot is verified only against that configured target and is not certified as staging or production schema.

## Completed

- Added `tools/SemanticCatalog`, a .NET 10 discovery CLI using the existing infrastructure/Npgsql dependencies. It discovers all seven EF Core contexts offline; the `--live` path queries PostgreSQL catalogs in a read-only session with a 5-second connection timeout and 10-second command timeouts.
- Captured PostgreSQL objects, tables/views/materialized views, columns/types/nullability/defaults, primary and foreign keys, indexes, constraints, and applied EF migration history. No migrations or writes were run.
- Generated a JSON-syntax YAML 1.2 subset catalog in `SemanticCatalog/`. Physical metadata is separated from semantic status. The live package includes 162 physical objects, 97 foreign-key relationships, 73 definitions sourced from Feature 015, and four unapproved dimension proposals. New objects and joins are not runtime-queryable.
- Generated `catalog-scope.yaml` with the five initial table candidates and explicit default exclusions. It keeps company, industry, monthly report, and line-item surfaces pending review, with provider, accepted-report, output-type, identity, and unit boundaries.
- Generated a bounded data profile and company/provider-scoped product-value observations from recent accepted `NoavaranCurrentApi` `ProductSales` reports with `OutputType=0`. The selection is capped at 500 report headers and 5,000 line items. The sample includes 323 distinct external report IDs, 96 observed unit labels, and 4,934 scoped product-value entries. Alias and cross-company category proposals are separate, pending, and non-executable; no products were automatically merged.
- Repeated live generation produced identical `physicalSchemaHash` and identical hashes over all catalog YAML files (342 files).

## Current PostgreSQL vs EF findings

The live snapshot reports six differences:

| Difference | Classification | Follow-up |
|---|---|---|
| `public.NoavaranEligibleCompanies` exists as a database view without a mapped EF entity | DB-only inventory | Keep excluded until its view definition and intended ownership are reviewed. |
| `CompanyPsSeriesSyncStates.CompanyId` index is expected by EF but absent in PostgreSQL | Review | Confirm whether this is unapplied migration state or an intentional model/database difference. |
| `FinancialStatementLineItems(FinancialStatementId, MetricCode)` is modeled unique by EF, while the live index is non-unique | Review | Resolve the uniqueness mismatch before publishing any related query surface. |
| PostgreSQL also has a non-unique `FinancialStatementLineItems(FinancialStatementId, MetricCode)` index not modeled as such by EF | DB-only inventory | Reconcile index intent with the EF unique-index definition above. |
| `FundPortfolioAnalyticsSnapshots` is mapped by EF but absent in PostgreSQL | Breaking | Determine migration/deployment status; do not publish the object. |
| `FundPortfolioSignals` is mapped by EF but absent in PostgreSQL | Breaking | Determine migration/deployment status; do not publish the object. |

PostgreSQL exposed 107 applied EF migration records. The catalog retains two prior EF-only table files from the initial offline pass and reports both as absent from the verified live source; it does not delete them silently.

## Profile and supported-question assessment

The profile sample covers 1,000 company labels, 110 industry labels, up to 500 recent report headers, and 5,000 matching line items. Production quantity, sales quantity, amount, and rate fields were profiled for null coverage and sample ranges. These are diagnostics only: quantity ranges cannot be compared across the 96 source unit labels, and sales values/rates retain source meaning without generic normalization.

The sample period range is 2026-06-22 through 2026-10-22. The latest date is later than the execution date (2026-10-08), so source period/date quality must be checked before describing this sample as current coverage.

| Example class | Phase 0/1 finding |
|---|---|
| Company/industry inventory | Physical tables and their company `IndustryId` are present; bounded labels were sampled. No generic runtime field is approved yet. |
| Cross-company “which firms produce iron ore pellets?” | Product labels exist, but exact-label/unit repetitions only yield pending category candidates. There is no approved cross-company product category. Unsupported for generic execution. |
| Company or product production/sales growth | Accepted single-month line items are present. Calculations still require company-scoped product identity, compatible units, period/missing-data rules, and reviewed metric policy. Existing specialized routes remain authoritative. |
| Compare raw quantities across companies/products | Not safe from this sample: source units vary and labels have no approved cross-company equivalence. |

Feature 076’s single-month `OutputType=0` boundary and Features 129/136 product-identity and accepted-revision constraints remain in force. Feature 015 definitions were copied as source-authoritative metadata, but none received generic-query execution approval.

## Unresolved review items

- Explain the two missing EF-mapped fund tables and the two index mismatches against the connected database/migration state.
- Review the view `NoavaranEligibleCompanies` before deciding whether it has any analytical use.
- Resolve company mapping for product observations that only carry provider `ExternalCompanyId`; canonical `CompanyId` was not attached in the bounded sample.
- Review the 96 unit labels, future-dated period observation, pending exact-label category candidates, and source-key title variations.
- Approve dimensions, relationships, product identities, aliases/categories, period rules, and any generic calculations before runtime use.

## Validation run

- `dotnet build tools/SemanticCatalog/SemanticCatalog.csproj --no-restore` — passed with zero warnings/errors.
- `dotnet run --project tools/SemanticCatalog/SemanticCatalog.csproj --no-build -- --self-test` — passed stable ID, duplicate ID, reviewed metadata preservation/staleness, invalid physical reference, unapproved exclusion, redaction, and serialization checks.
- `dotnet run --project tools/SemanticCatalog/SemanticCatalog.csproj --no-build -- --live` — completed against the configured database; metadata and capped profile were captured without printing configuration values.
- `python -m unittest discover -s tools/SemanticCatalog/tests -v` — 9 tests passed, including live artifact references, review-state boundaries, credential-output scan, bounded profile, and offline EF-only generation.

No schema, financial, identity, billing, or operational records were modified. No AI routing, query planner, generic SQL execution, chart rendering, or runtime activation was added.

## Catalog curation addendum — 2026-10-08

- Repeated bounded live metadata discovery against the configured development-settings database (read-only PostgreSQL session) verified 162 EF/PG objects and the TradingInstruments columns, PK, unique indexes, and optional `NormalizedCompanyId -> Companies.Id` FK. EF and PostgreSQL agree for this table and all four required market observation FKs to `TradingInstruments.Id`.
- Curated overlay now contains 18 physical table candidates plus the existing Noavaran `api/v2/CompanyFundamentalIndex/Values` external-source record. `TradingInstruments` was already present in the generated 162-object physical catalog; it is now included in the semantic candidate overlay with its existing stable ID.
- The business owner confirmed `IsRepresented=true` means Restated. Codal selection still prefers this flag after audited status; that selection policy remains pending financial-owner approval. `IsComposing` is passed through as a provider flag, but upstream consolidated/separate semantics remain pending verification.
- TSETMC-to-Companies linking is an application mapping, not an additional declared relation: it matches source InsCode to `Companies.InstrumentCode`, selects most recently synchronized then Id, and does not filter Company.ProviderName. This may be ambiguous across provider rows; no resolver behavior changed.
- Current source code contains a direct TSETMC instrument sync service and end-of-day worker path. Repository defaults enable TSETMC polling and disable StockMarketDB polling; effective deployment overrides were not verified. StockMarketDbSyncService does not write TradingInstruments.
- Latest read-only discovery still reports six unrelated EF/PostgreSQL reconciliation differences documented above. No DDL, migrations, or financial data mutations were run.
- Updated validation: catalog build passed with zero warnings/errors; 11 catalog tests passed; curated YAML/Markdown regeneration is deterministic. All 18 candidates remain `candidate-pending-review` and `queryable: false`.
