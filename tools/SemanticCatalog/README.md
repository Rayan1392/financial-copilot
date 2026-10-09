# Feature 141 Semantic Catalog Tool

This .NET 10 CLI discovers EF Core model metadata and, when `--live` is supplied, read-only PostgreSQL metadata through `pg_catalog`. It compares both sources, creates deterministic physical table/relationship YAML, carries forward existing table semantic sections, and writes limited market-data profile and product-value proposals.

```powershell
dotnet run --project tools/SemanticCatalog/SemanticCatalog.csproj -- --live
```

Connection lookup checks `ConnectionStrings__FinancialCopilot`, the repository `.env`, API development settings, then API default settings. The connection value is never printed or written to artifacts. Metadata/profile commands use a short connection timeout, a ten-second command timeout, and a PostgreSQL read-only session. The profile reads at most 500 recent accepted Noavaran `ProductSales` reports of output type 0 and at most 5,000 line items. It does not run migrations or issue writes.

`--output <directory>` redirects generated discovery and reconciliation reports. The canonical catalog is written under `specs/141-schema-aware-semantic-query-engine/SemanticCatalog/`.

`--catalog-root <directory>` redirects YAML catalog generation for isolated tests and review fixtures. `--self-test` exercises stable IDs, duplicate-ID rejection, approved-metadata preservation/staleness, invalid physical reference rejection, unapproved exclusion, secret redaction, and deterministic serialization.

Files use JSON syntax, which is a valid YAML 1.2 subset. This keeps serialization deterministic using the existing .NET runtime without introducing a YAML parser dependency. Table IDs derive from schema and object names; semantic sections remain separate and all generated objects default to non-queryable. Physical objects that disappear from a later source snapshot are retained as stale files and called out in `artifacts/catalog-drift.json` for review.

The live profile is a bounded discovery sample, not a completeness claim. It retains company/provider product identity, original title, unit, and report provenance. Alias/category proposals remain empty pending review. Existing specialized financial tools continue to own their respective calculations and request paths.

Run artifact checks with `python -m unittest discover -s tools/SemanticCatalog/tests -v` after a discovery run.

The initial reviewed-candidate overlay and its deterministic Markdown review are refreshed from the canonical YAML package with:

```powershell
python tools/SemanticCatalog/curate_catalog.py
```

Build the deterministic, versioned Vectorless navigation hierarchy and its Markdown index from the canonical catalog with:

```powershell
python tools/SemanticCatalog/catalog.py hierarchy build
```

The command validates package indexes, node IDs/references, reachability, and cycles before writing `artifacts/navigation-hierarchy.yaml` and `artifacts/navigation-hierarchy.md`. `python tools/SemanticCatalog/catalog.py validate` is read-only: it rebuilds the expected hierarchy in memory and checks both generated artifacts and the manifest lock without writing.

Hash contract (`feature141-semantic-catalog-v1`): each JSON-subset YAML source is parsed, strings are Unicode NFC normalized, object keys are sorted ordinally, and compact UTF-8 JSON is framed by its normalized `/` relative path in ordinal path order. The package covers `catalog.yaml`, `catalog-scope.yaml`, `manifest.yaml`, `navigation.yaml`, and every `*.yaml` in `tables/`, `relationships/`, `metrics/`, `dimensions/`, `dictionaries/`, `sources/`, and `policies/`. The `manifest.yaml` field `navigationHierarchyHash` is an integrity lock and is excluded from the source hash to avoid a cycle. `physicalSchemaHash` remains an independent physical discovery digest; neither hash substitutes for the other. The hierarchy hash is SHA-256 over the canonical generated hierarchy with its `hierarchyHash` property omitted.

The runtime loader requires both `Feature141:SchemaNavigator:HierarchyPath` and `Feature141:SchemaNavigator:CatalogPath`. Every load recomputes the source package hash, hierarchy hash, and manifest hierarchy lock, then checks the package index against actual source records. Missing, malformed, stale, or modified input fails closed; the loader never rebuilds artifacts. Use `dotnet run --project tools/SemanticCatalog -- validate-catalog` for the runtime integrity check. The navigator still exposes navigation review, semantic approval, and query authorization separately; generated candidate nodes remain pending review and non-queryable.

The curation command validates its selected tables against the captured live schema snapshot, refuses to overwrite approved/queryable table semantics, and writes `specs/141-schema-aware-semantic-query-engine/curated-catalog-review.md`. The .NET catalog generator preserves existing candidate semantics and scope extensions and includes YAML records in `SemanticCatalog/sources/` in the catalog index.

Reviewed semantic assertions may use `semantic.businessDefinitions` records with `field`, bilingual labels, `trueMeans`/`falseMeans`, `provenance`, and `status`. Keep provenance classes explicit (for example `business-owner-confirmed` versus source-code behavior), and leave unverified provider mappings null with a pending status. These assertion records and the scope overlay are documentation only: candidate entries remain non-queryable until a separate human approval action.

The source-item dictionary is built from the captured read-only persisted catalog snapshot (definitions and existing mappings only; no observation values):

```powershell
dotnet run --project tools/SemanticCatalog -- export-source-item-snapshot
python tools/SemanticCatalog/catalog.py source-items build
python tools/SemanticCatalog/catalog.py hierarchy build
python tools/SemanticCatalog/catalog.py validate
```

Refresh the snapshot only when explicitly authorized. It uses a read-only PostgreSQL session and never calls the provider API. Each catalog entry remains non-queryable until semantic and runtime authorization are independently approved. Financial value and calculation constraints are versioned in `SemanticCatalog/policies/financial-value-retrieval.yaml`.
