using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Npgsql;

internal static class FinancialSourceItemExportCli
{
    private sealed record SourceItem(string SourceItemCatalogId, string ProviderName, string StatementType,
        int SourceItemId, string? TitleFa, string? TitleEn, string? Unit, string? MetricCode);
    private sealed record Snapshot(int FormatVersion, string Source, IReadOnlyList<SourceItem> Items);

    public static async Task RunAsync(string root, string[] args)
    {
        var output = Option(args, "--output") ?? Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine",
            "SemanticCatalog", "source-item-snapshot.json");
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__FinancialCopilot")
            ?? ReadDotEnv(Path.Combine(root, ".env"), "ConnectionStrings__FinancialCopilot")
            ?? ReadJson(Path.Combine(root, "src", "backend", "FinancialCopilot.API", "appsettings.Development.json"))
            ?? ReadJson(Path.Combine(root, "src", "backend", "FinancialCopilot.API", "appsettings.json"));
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Financial Copilot connection settings are required for a read-only source-item export.");

        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Timeout = 5, CommandTimeout = 30, Pooling = false, ApplicationName = "Feature141SourceItemCatalogExport"
        };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using (var readOnly = new NpgsqlCommand("SET SESSION CHARACTERISTICS AS TRANSACTION READ ONLY", connection))
            await readOnly.ExecuteNonQueryAsync();

        const string sql = """
            SELECT source_item."Id"::text, source_item."ProviderName", source_item."StatementType",
                   source_item."SourceItemId", source_item."TitleFa", source_item."TitleEn", source_item."Unit",
                   mapping."MetricCode"
            FROM public."FinancialStatementSourceItems" AS source_item
            LEFT JOIN public."FinancialStatementSourceItemMetricMappings" AS mapping
                ON mapping."SourceItemCatalogId" = source_item."Id"
            ORDER BY source_item."ProviderName", source_item."StatementType", source_item."SourceItemId", source_item."Id"
            """;
        var items = new List<SourceItem>();
        await using (var command = new NpgsqlCommand(sql, connection))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                items.Add(new SourceItem(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt32(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7)));
            }
        }

        var snapshot = new Snapshot(1, "public.FinancialStatementSourceItems; optional MetricCode from FinancialStatementSourceItemMetricMappings",
            items.OrderBy(item => item.ProviderName, StringComparer.Ordinal)
                .ThenBy(item => item.StatementType, StringComparer.Ordinal).ThenBy(item => item.SourceItemId)
                .ThenBy(item => item.SourceItemCatalogId, StringComparer.Ordinal).ToArray());
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(snapshot, options) + "\n", new UTF8Encoding(false));
        Console.WriteLine($"Exported {items.Count} source-item definitions and persisted metric mappings to {Path.GetFullPath(output)} (read-only; no observation values exported).");
    }

    private static string? Option(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string? ReadDotEnv(string path, string key)
    {
        if (!File.Exists(path)) return null;
        foreach (var line in File.ReadLines(path))
        {
            if (!line.StartsWith(key + "=", StringComparison.Ordinal)) continue;
            var value = line[(key.Length + 1)..].Trim().Trim('"', '\'');
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        return null;
    }

    private static string? ReadJson(string path)
    {
        if (!File.Exists(path)) return null;
        try { return JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("ConnectionStrings").GetProperty("FinancialCopilot").GetString(); }
        catch { return null; }
    }
}
