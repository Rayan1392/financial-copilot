using System.Text.Json;

namespace FinancialCopilot.Application.FinancialData.Semantics.Navigation;

/// <summary>Builds compiler mappings from the canonical JSON-subset YAML and locked hierarchy.</summary>
public sealed class StructuredQueryCatalogSnapshotBuilder
{
    public StructuredQueryCatalogSnapshot Build(string catalogDirectory, string hierarchyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(hierarchyPath);

        using var hierarchy = JsonDocument.Parse(File.ReadAllBytes(hierarchyPath));
        var catalogHash = CanonicalSemanticCatalogIntegrityValidator.Validate(catalogDirectory, hierarchy.RootElement);
        using var catalogDocument = ReadJson(Path.Combine(catalogDirectory, "catalog.yaml"));
        using var manifestDocument = ReadJson(Path.Combine(catalogDirectory, "manifest.yaml"));
        var root = catalogDocument.RootElement;
        var manifest = manifestDocument.RootElement;
        var version = manifest.GetProperty("formatVersion").ToString();
        var hierarchyVersion = hierarchy.RootElement.GetProperty("catalogVersion").GetString();
        if (!string.Equals(version, hierarchyVersion, StringComparison.Ordinal))
            throw new SchemaNavigationOutputException("catalog_snapshot_version_mismatch");

        var tableIds = IndexIds(root, "tables");
        var relationshipIds = IndexIds(root, "relationships");
        var tables = new List<StructuredSqlTable>();
        var columns = new List<StructuredSqlColumn>();
        foreach (var id in tableIds)
        {
            var record = ReadIndexedRecord(catalogDirectory, "tables", id);
            var physical = record.GetProperty("physical");
            var schema = RequiredString(physical, "Schema");
            var name = RequiredString(physical, "Name");
            var semantic = OptionalObject(record, "semantic");
            var semanticApproval = OptionalObject(semantic, "semanticApproval");
            var queryAuthorization = OptionalObject(semantic, "queryAuthorization");
            var tableQueryable = Boolean(semantic, "queryable") && Boolean(queryAuthorization, "runtimeQueryable");
            var approvedObjects = queryAuthorization.ValueKind == JsonValueKind.Object
                && queryAuthorization.TryGetProperty("approvedObjects", out var approvedArray)
                && approvedArray.ValueKind == JsonValueKind.Array
                    ? approvedArray.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String)
                        .Select(item => item.GetString()!).ToHashSet(StringComparer.Ordinal)
                    : new HashSet<string>(StringComparer.Ordinal);
            tables.Add(new StructuredSqlTable(id, schema, name, tableQueryable,
                Boolean(semanticApproval, "approved")));
            foreach (var column in physical.GetProperty("Columns").EnumerateArray())
            {
                var columnName = RequiredString(column, "Name");
                // Column IDs are deterministic namespaced references derived from a verified table ID
                // and its physical column name; the canonical physical snapshot does not assign IDs.
                var columnId = ColumnId(id, columnName);
                columns.Add(new StructuredSqlColumn(columnId, id, columnName,
                    tableQueryable && approvedObjects.Contains(columnId)));
            }
        }
        EnsureUnique(tables.Select(x => x.CatalogId), "catalog_snapshot_duplicate_table_id");
        EnsureUnique(tables.Select(x => $"{x.Schema}.{x.Name}"), "catalog_snapshot_duplicate_physical_table");
        EnsureUnique(columns.Select(x => x.CatalogId), "catalog_snapshot_duplicate_column_id");
        var tableByName = tables.ToDictionary(x => $"{x.Schema}.{x.Name}", StringComparer.Ordinal);
        var columnIds = columns.Select(x => x.CatalogId).ToHashSet(StringComparer.Ordinal);

        var relationships = new List<StructuredSqlRelationship>();
        foreach (var id in relationshipIds)
        {
            var record = ReadIndexedRecord(catalogDirectory, "relationships", id);
            var from = ReadEndpoint(record.GetProperty("from"));
            var to = ReadEndpoint(record.GetProperty("to"));
            if (from.Columns.Length != 1 || to.Columns.Length != 1
                || !tableByName.TryGetValue($"{from.Schema}.{from.Table}", out var fromTable)
                || !tableByName.TryGetValue($"{to.Schema}.{to.Table}", out var toTable))
                throw new SchemaNavigationOutputException("catalog_snapshot_relationship_endpoint_invalid");
            var fromColumn = ColumnId(fromTable.CatalogId, from.Columns[0]);
            var toColumn = ColumnId(toTable.CatalogId, to.Columns[0]);
            if (!columnIds.Contains(fromColumn) || !columnIds.Contains(toColumn))
                throw new SchemaNavigationOutputException("catalog_snapshot_relationship_column_missing");
            var semantic = OptionalObject(record, "semantic");
            var verification = OptionalObject(record, "verification");
            var provenance = OptionalObject(record, "provenance");
            var nullable = OptionalObject(record, "nullableBehavior");
            var physicalConstraint = record.TryGetProperty("physicalConstraint", out var constraint)
                && constraint.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(constraint.GetString());
            var physicalPresent = Boolean(record, "physicalConstraintPresent", physicalConstraint);
            if (physicalPresent != physicalConstraint)
                throw new SchemaNavigationOutputException("catalog_snapshot_physical_constraint_claim_mismatch");
            relationships.Add(new StructuredSqlRelationship(id, fromTable.CatalogId, fromColumn,
                toTable.CatalogId, toColumn, RequiredString(record, "inferredCardinality"),
                Boolean(semantic, "runtimeJoinable"),
                Boolean(provenance, "verified") || string.Equals(StringOrNull(verification, "status"),
                    "verified-application-logical-relationship", StringComparison.Ordinal),
                physicalPresent, Boolean(semantic, "approved"), Boolean(OptionalObject(semantic, "queryAuthorization"), "queryable"),
                JsonText(record, "joinGrain") ?? StringOrNull(record, "grain"),
                Boolean(nullable, "fromKeyNullable", true), Boolean(nullable, "toKeyNullable", true),
                Boolean(nullable, "referentialIntegrityEnforced", physicalPresent)));
        }
        EnsureUnique(relationships.Select(x => x.CatalogId), "catalog_snapshot_duplicate_relationship_id");

        var dictionaryPath = Path.Combine(catalogDirectory, "dictionaries", "financial-statement-source-items.yaml");
        using var dictionary = JsonDocument.Parse(File.ReadAllBytes(dictionaryPath));
        var dictionaryRoot = dictionary.RootElement;
        var entries = dictionaryRoot.GetProperty("entries").EnumerateArray().ToArray();
        EnsureUnique(entries.Select(x => RequiredString(x, "catalogId")), "catalog_snapshot_duplicate_source_id");
        var sourceTable = FindTable(tableByName, "FinancialStatementSourceItems");
        var lineTable = FindTable(tableByName, "FinancialStatementLineItems");
        var statementTable = FindTable(tableByName, "FinancialStatements");
        var companyTable = FindTable(tableByName, "Companies");
        var sources = new List<StructuredSqlDirectSource>();
        foreach (var entry in entries)
        {
            if (!string.Equals(StringOrNull(entry, "referenceType"), "DirectSourceItem", StringComparison.Ordinal)) continue;
            if (!Guid.TryParse(StringOrNull(entry, "sourceItemCatalogId"), out var sourceItemId) || sourceItemId == Guid.Empty
                || !entry.TryGetProperty("sourceItemId", out var itemNumberElement)
                || !itemNumberElement.TryGetInt32(out var itemNumber) || itemNumber <= 0)
                throw new SchemaNavigationOutputException("catalog_snapshot_source_identity_invalid");
            var sourceRelationships = new[]
            {
                SingleRelationship(relationships, sourceTable.CatalogId, lineTable.CatalogId),
                SingleRelationship(relationships, lineTable.CatalogId, statementTable.CatalogId),
                SingleRelationship(relationships, statementTable.CatalogId, companyTable.CatalogId)
            };
            if (sourceRelationships.Any(x => !x.Verified))
                throw new SchemaNavigationOutputException("catalog_snapshot_direct_source_relationships_incomplete");
            var authorization = OptionalObject(entry, "queryAuthorization");
            sources.Add(new StructuredSqlDirectSource(RequiredString(entry, "catalogId"), sourceItemId,
                RequiredString(entry, "providerName"), RequiredString(entry, "statementType"), itemNumber,
                sourceTable.CatalogId, lineTable.CatalogId, statementTable.CatalogId, companyTable.CatalogId,
                sourceRelationships.Select(x => x.CatalogId).Order(StringComparer.Ordinal).ToArray(),
                Boolean(authorization, "queryable"), StringOrNull(entry, "sourceUnit"),
                StringOrNull(entry, "sourceUnitStatus"), StringOrNull(entry, "normalizedUnit"),
                StringOrNull(entry, "mappedMetricCode"),
                string.Equals(StringOrNull(entry, "identityStatus"), "verified-persisted-source-key", StringComparison.Ordinal),
                string.Equals(StringOrNull(entry, "semanticReviewStatus"), "approved", StringComparison.Ordinal),
                StringOrNull(entry, "effectiveUnit"), StringOrNull(entry, "effectiveUnitStatus"),
                StringOrNull(entry, "effectiveUnitProvenance"), StringOrNull(entry, "titleFa")?.Trim()));
        }

        // Every source definition must be discoverable in the generated locked hierarchy, with
        // its identity and independent authorization state preserved.
        var hierarchySources = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in hierarchy.RootElement.GetProperty("nodes").EnumerateArray())
        {
            if (!node.TryGetProperty("sourceItemReferences", out var refs) || refs.ValueKind != JsonValueKind.Array) continue;
            foreach (var reference in refs.EnumerateArray()) hierarchySources.Add(RequiredString(reference, "catalogId"));
        }
        if (sources.Any(source => !hierarchySources.Contains(source.CatalogId)))
            throw new SchemaNavigationOutputException("catalog_snapshot_hierarchy_source_reference_missing");

        var monthlyMeasures = ReadMonthlyMeasures(catalogDirectory, tableByName, columns, relationships);
        return new StructuredQueryCatalogSnapshot(version, catalogHash, true, tables, columns, relationships, sources, monthlyMeasures);
    }

    private static IReadOnlyList<StructuredSqlMonthlyMeasure> ReadMonthlyMeasures(string catalogDirectory,
        IReadOnlyDictionary<string, StructuredSqlTable> tableByName, IReadOnlyList<StructuredSqlColumn> columns,
        IReadOnlyList<StructuredSqlRelationship> relationships)
    {
        var path = Path.Combine(catalogDirectory, "dictionaries", "monthly-measures.yaml");
        using var dictionary = ReadJson(path);
        var entries = dictionary.RootElement.GetProperty("entries").EnumerateArray().ToArray();
        EnsureUnique(entries.Select(x => RequiredString(x, "catalogId")), "catalog_snapshot_duplicate_monthly_measure_id");
        var tableById = tableByName.Values.ToDictionary(x => x.CatalogId, StringComparer.Ordinal);
        var columnIds = columns.Select(x => x.CatalogId).ToHashSet(StringComparer.Ordinal);
        var measures = new List<StructuredSqlMonthlyMeasure>();
        foreach (var entry in entries)
        {
            var lineTableId = RequiredString(entry, "physicalTableId");
            var reportTableId = RequiredString(entry, "reportTableId");
            if (!tableById.TryGetValue(lineTableId, out var lineTable)
                || !tableById.TryGetValue(reportTableId, out var reportTable))
                throw new SchemaNavigationOutputException("catalog_snapshot_monthly_measure_table_missing");
            string Column(string tableId, string name)
            {
                var id = ColumnId(tableId, name);
                if (!columnIds.Contains(id)) throw new SchemaNavigationOutputException("catalog_snapshot_monthly_measure_column_missing");
                return id;
            }
            var unitPolicy = OptionalObject(entry, "unitPolicy");
            var providerPolicy = OptionalObject(entry, "providerPolicy");
            var revisionPolicy = OptionalObject(entry, "revisionPolicy");
            var review = OptionalObject(entry, "review");
            var relationIds = entry.GetProperty("relationshipIds").EnumerateArray()
                .Select(x => x.GetString() ?? string.Empty).ToArray();
            if (relationIds.Length != 1 || relationIds.Any(id => relationships.SingleOrDefault(x => x.CatalogId == id) is not { Verified: true }))
                throw new SchemaNavigationOutputException("catalog_snapshot_monthly_measure_relationship_unverified");
            if (!Connects(relationships.Single(x => x.CatalogId == relationIds[0]), lineTableId, reportTableId))
                throw new SchemaNavigationOutputException("catalog_snapshot_monthly_measure_relationship_mismatch");
            if (!Boolean(revisionPolicy, "isAccepted") || StringOrNull(revisionPolicy, "revisionStatus") is not { Length: > 0 } revisionStatus)
                throw new SchemaNavigationOutputException("catalog_snapshot_monthly_revision_policy_missing");
            if (!entry.GetProperty("providerPolicy").TryGetProperty("outputType", out var outputTypeElement)
                || !outputTypeElement.TryGetInt32(out var outputType))
                throw new SchemaNavigationOutputException("catalog_snapshot_monthly_output_type_missing");
            var unitColumn = StringOrNull(unitPolicy, "sourceUnitColumn") ?? "Unit";
            measures.Add(new StructuredSqlMonthlyMeasure(
                RequiredString(entry, "catalogId"), RequiredString(entry, "metricCode"), lineTableId, reportTableId,
                Column(lineTableId, RequiredString(entry, "physicalColumn")),
                Column(lineTableId, "Title"), Column(lineTableId, unitColumn),
                Column(reportTableId, "PeriodStart"), Column(reportTableId, "PeriodEnd"),
                Column(reportTableId, "ProviderName"), Column(reportTableId, "ReportType"),
                Column(reportTableId, "OutputType"), Column(reportTableId, "CompanyId"),
                Column(reportTableId, "IsAccepted"), Column(reportTableId, "RevisionStatus"),
                Column(reportTableId, "ExternalReportId"), Column(lineTableId, "SourceRowFingerprint"),
                Column(lineTableId, "SourceMultiplicity"), Array.AsReadOnly(relationIds),
                RequiredString(providerPolicy, "providerName"), RequiredString(providerPolicy, "reportType"), outputType,
                Boolean(revisionPolicy, "isAccepted"), revisionStatus, RequiredString(unitPolicy, "kind"), StringOrNull(unitPolicy, "code"),
                unitColumn, Boolean(unitPolicy, "verified"), Boolean(unitPolicy, "recomputeAllowed"),
                Boolean(review, "queryable"), Boolean(review, "semanticApproved")));
        }
        return measures;
    }

    public static string ColumnId(string tableCatalogId, string columnName) => $"{tableCatalogId}_{columnName}";

    private static bool Connects(StructuredSqlRelationship relationship, string first, string second) =>
        (relationship.FromTableCatalogId == first && relationship.ToTableCatalogId == second)
        || (relationship.FromTableCatalogId == second && relationship.ToTableCatalogId == first);

    /// <summary>
    /// The one join path between two tables. When the catalog records several (a physical FK and the
    /// runtime join, e.g. FinancialStatements.CompanyId and the provider identity), the runtime-joinable one is used.
    /// </summary>
    private static StructuredSqlRelationship SingleRelationship(IEnumerable<StructuredSqlRelationship> relationships,
        string first, string second)
    {
        var connecting = relationships.Where(x => Connects(x, first, second)).ToArray();
        if (connecting.Length > 1) connecting = connecting.Where(x => x.Queryable).ToArray();
        return connecting.Length == 1
            ? connecting[0]
            : throw new SchemaNavigationOutputException("catalog_snapshot_direct_source_relationships_incomplete");
    }

    private static StructuredSqlTable FindTable(IReadOnlyDictionary<string, StructuredSqlTable> tables, string name) =>
        tables.Values.SingleOrDefault(table => table.Schema == "public" && table.Name == name)
        ?? throw new SchemaNavigationOutputException("catalog_snapshot_required_table_missing");

    private static (string Schema, string Table, string[] Columns) ReadEndpoint(JsonElement endpoint) =>
        (RequiredString(endpoint, "schema"), RequiredString(endpoint, "table"),
            endpoint.GetProperty("columns").EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToArray());

    private static string[] IndexIds(JsonElement root, string category) =>
        root.GetProperty(category).EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToArray();

    private static JsonElement ReadIndexedRecord(string directory, string category, string id)
    {
        var path = Path.Combine(directory, category, id + ".yaml");
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var element = document.RootElement.Clone();
        if (!string.Equals(RequiredString(element, "id"), id, StringComparison.Ordinal))
            throw new SchemaNavigationOutputException("catalog_snapshot_indexed_id_mismatch");
        return element;
    }

    private static JsonDocument ReadJson(string path)
    {
        try { return JsonDocument.Parse(File.ReadAllBytes(path)); }
        catch (FileNotFoundException) { throw new SchemaNavigationOutputException("catalog_snapshot_source_file_missing"); }
        catch (JsonException) { throw new SchemaNavigationOutputException("catalog_snapshot_source_file_invalid"); }
    }

    private static JsonElement OptionalObject(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value : default;

    private static string RequiredString(JsonElement parent, string name) =>
        StringOrNull(parent, name) is { Length: > 0 } value
            ? value : throw new SchemaNavigationOutputException("catalog_snapshot_required_value_missing");

    private static string? StringOrNull(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? JsonText(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value)
            && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? value.GetRawText() : null;

    private static bool Boolean(JsonElement parent, string name, bool fallback = false) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : fallback;

    private static void EnsureUnique(IEnumerable<string> values, string code)
    {
        var items = values.ToArray();
        if (items.Any(string.IsNullOrWhiteSpace) || items.Distinct(StringComparer.Ordinal).Count() != items.Length)
            throw new SchemaNavigationOutputException(code);
    }
}
