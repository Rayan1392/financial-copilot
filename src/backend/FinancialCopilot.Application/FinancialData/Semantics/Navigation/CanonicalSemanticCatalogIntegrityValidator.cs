using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace FinancialCopilot.Application.FinancialData.Semantics.Navigation;

/// <summary>Validates the canonical JSON-subset YAML package and its generated hierarchy without rebuilding either.</summary>
public static class CanonicalSemanticCatalogIntegrityValidator
{
    private static readonly string[] RootFiles = ["catalog.yaml", "catalog-scope.yaml", "manifest.yaml", "navigation.yaml", "source-item-snapshot.json"];
    private static readonly string[] PackageFolders = ["tables", "relationships", "metrics", "dimensions", "dictionaries", "sources", "policies"];
    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Validate(string catalogDirectory, JsonElement hierarchy)
    {
        var actualHash = ComputeSourceHash(catalogDirectory);
        var manifestPath = Path.Combine(catalogDirectory, "manifest.yaml");
        using var manifestDocument = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        var manifest = manifestDocument.RootElement;
        var versionElement = manifest.GetProperty("formatVersion");
        var version = versionElement.ValueKind == JsonValueKind.String ? versionElement.GetString() : versionElement.GetRawText();
        var declaredVersion = hierarchy.TryGetProperty("catalogVersion", out var hierarchyVersion) ? hierarchyVersion.GetString() : null;
        var declaredHash = hierarchy.TryGetProperty("catalogHash", out var catalogHash) ? catalogHash.GetString() : null;
        if (!string.Equals(version, declaredVersion, StringComparison.Ordinal) ||
            !string.Equals(actualHash, declaredHash, StringComparison.Ordinal))
            throw new SchemaNavigationOutputException("catalog_source_hash_mismatch");

        if (!hierarchy.TryGetProperty("hierarchyHash", out var hierarchyHashElement) || hierarchyHashElement.ValueKind != JsonValueKind.String)
            throw new SchemaNavigationOutputException("catalog_hierarchy_hash_missing");
        var actualHierarchyHash = ComputeHierarchyHash(hierarchy);
        if (!string.Equals(actualHierarchyHash, hierarchyHashElement.GetString(), StringComparison.Ordinal))
            throw new SchemaNavigationOutputException("catalog_hierarchy_hash_mismatch");
        if (!manifest.TryGetProperty("navigationHierarchyHash", out var lockElement) ||
            !string.Equals(lockElement.GetString(), actualHierarchyHash, StringComparison.Ordinal))
            throw new SchemaNavigationOutputException("catalog_hierarchy_lock_mismatch");
        return actualHash;
    }

    public static string ComputeSourceHash(string catalogDirectory)
    {
        var paths = new List<string>();
        foreach (var name in RootFiles)
        {
            var path = Path.Combine(catalogDirectory, name);
            if (!File.Exists(path)) throw new SchemaNavigationOutputException("catalog_source_file_missing");
            paths.Add(path);
        }
        foreach (var folder in PackageFolders)
        {
            var directory = Path.Combine(catalogDirectory, folder);
            if (!Directory.Exists(directory)) continue;
            paths.AddRange(Directory.EnumerateFiles(directory, "*.yaml", SearchOption.TopDirectoryOnly));
        }
        paths.Sort((left, right) => StringComparer.Ordinal.Compare(
            Path.GetRelativePath(catalogDirectory, left).Replace('\\', '/'),
            Path.GetRelativePath(catalogDirectory, right).Replace('\\', '/')));
        var covered = paths.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (Directory.EnumerateFiles(catalogDirectory, "*.yaml", SearchOption.AllDirectories)
            .Any(path => !covered.Contains(Path.GetFullPath(path))))
            throw new SchemaNavigationOutputException("catalog_unclassified_source_file");

        var files = new List<(string RelativePath, JsonDocument Document)>();
        try
        {
            foreach (var path in paths)
            {
                JsonDocument document;
                try { document = JsonDocument.Parse(File.ReadAllBytes(path)); }
                catch (JsonException) { throw new SchemaNavigationOutputException("catalog_source_not_json_yaml_subset"); }
                files.Add((Path.GetRelativePath(catalogDirectory, path).Replace('\\', '/'), document));
            }

            var catalog = files.Single(file => file.RelativePath == "catalog.yaml").Document.RootElement;
            foreach (var category in new[] { "tables", "relationships", "metrics", "dimensions", "dictionaries", "sources", "policies" })
            {
                if (!catalog.TryGetProperty(category, out var declared) || declared.ValueKind != JsonValueKind.Array)
                    throw new SchemaNavigationOutputException("catalog_source_index_missing");
                var declaredIds = declared.EnumerateArray().Select(value => value.GetString() ?? "").ToArray();
                var sourceIds = files.Where(file => file.RelativePath.StartsWith(category + "/", StringComparison.Ordinal))
                    .Select(file => file.Document.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                        ? id.GetString()! : Path.GetFileNameWithoutExtension(file.RelativePath)).ToArray();
                if (declaredIds.Distinct(StringComparer.Ordinal).Count() != declaredIds.Length ||
                    sourceIds.Distinct(StringComparer.Ordinal).Count() != sourceIds.Length ||
                    !declaredIds.ToHashSet(StringComparer.Ordinal).SetEquals(sourceIds))
                    throw new SchemaNavigationOutputException("catalog_source_index_mismatch");
            }
            if (catalog.GetProperty("manifest").GetString() != "manifest.yaml" || catalog.GetProperty("navigation").GetString() != "navigation.yaml")
                throw new SchemaNavigationOutputException("catalog_source_reference_invalid");

            var material = new StringBuilder("feature141-semantic-catalog-v1\n");
            for (var index = 0; index < files.Count; index++)
            {
                var (relativePath, document) = files[index];
                if (index > 0) material.Append('\n');
                material.Append(relativePath).Append('\0').Append(CanonicalSourceFile(relativePath, document.RootElement));
            }
            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material.ToString())));
        }
        finally
        {
            foreach (var (_, document) in files) document.Dispose();
        }
    }

    public static string ComputeHierarchyHash(JsonElement hierarchy)
    {
        using var hierarchyWithoutHash = JsonDocument.Parse(RemoveHierarchyHash(hierarchy));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(hierarchyWithoutHash.RootElement))));
    }

    private static string CanonicalSourceFile(string relativePath, JsonElement value)
    {
        if (relativePath != "manifest.yaml") return Canonical(value);
        using var withoutLock = RemoveProperty(value, "navigationHierarchyHash");
        return Canonical(withoutLock.RootElement);
    }

    private static JsonDocument RemoveProperty(JsonElement root, string excludedProperty)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            foreach (var property in root.EnumerateObject().Where(property => property.Name != excludedProperty))
            {
                writer.WritePropertyName(property.Name);
                WriteCanonical(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        return JsonDocument.Parse(stream.ToArray());
    }

    private static string RemoveHierarchyHash(JsonElement root)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions))
        {
            writer.WriteStartObject();
            foreach (var property in root.EnumerateObject().Where(property => property.Name != "hierarchyHash"))
            {
                writer.WritePropertyName(property.Name);
                WriteCanonical(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string Canonical(JsonElement element)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, WriterOptions)) WriteCanonical(writer, element);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                var properties = element.EnumerateObject().ToArray();
                var names = properties.Select(property => property.Name.Normalize(NormalizationForm.FormC)).ToArray();
                if (names.Distinct(StringComparer.Ordinal).Count() != names.Length)
                    throw new SchemaNavigationOutputException("catalog_duplicate_json_key");
                foreach (var property in properties.OrderBy(property => property.Name.Normalize(NormalizationForm.FormC), StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name.Normalize(NormalizationForm.FormC));
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray()) WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString()!.Normalize(NormalizationForm.FormC));
                break;
            case JsonValueKind.Number:
                if (element.TryGetInt64(out var integer)) writer.WriteNumberValue(integer);
                else if (element.TryGetDecimal(out var number)) writer.WriteRawValue(number.ToString("G29", CultureInfo.InvariantCulture));
                else throw new SchemaNavigationOutputException("catalog_number_out_of_range");
                break;
            case JsonValueKind.True: writer.WriteBooleanValue(true); break;
            case JsonValueKind.False: writer.WriteBooleanValue(false); break;
            case JsonValueKind.Null: writer.WriteNullValue(); break;
            default: throw new SchemaNavigationOutputException("catalog_source_json_value_invalid");
        }
    }
}
