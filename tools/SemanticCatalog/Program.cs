using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Domain.Financial.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;

// Feature 141 Phase 0/1 catalog discovery. This executable performs metadata-only PostgreSQL
// discovery; it never applies migrations or reads business rows.
if (args.Length > 0 && string.Equals(args[0], "evaluate-navigator", StringComparison.OrdinalIgnoreCase))
{
    await NavigatorEvaluationCli.RunAsync(args.Skip(1).ToArray());
    return;
}
if (args.Length == 0 || args.Contains("--help", StringComparer.OrdinalIgnoreCase) || args.Contains("-h", StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine("SemanticCatalog commands: validate-catalog | evaluate-navigator | export-source-item-snapshot | --self-test | --version");
    Console.WriteLine("Use 'evaluate-navigator --help' for live evaluation options. Help does not discover or write catalog artifacts.");
    return;
}
if (args.Contains("--version", StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine(typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown");
    return;
}
var root = FindRepositoryRoot(Directory.GetCurrentDirectory());
if (args.Length > 0 && string.Equals(args[0], "export-source-item-snapshot", StringComparison.OrdinalIgnoreCase))
{
    await FinancialSourceItemExportCli.RunAsync(root, args.Skip(1).ToArray());
    return;
}
if (args.Length > 0 && string.Equals(args[0], "validate-catalog", StringComparison.OrdinalIgnoreCase))
{
    var catalogPath = Path.GetFullPath(GetOption(args, "--catalog") ?? Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "SemanticCatalog"));
    var hierarchyPath = Path.GetFullPath(GetOption(args, "--hierarchy") ?? Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "artifacts", "navigation-hierarchy.yaml"));
    using var hierarchy = JsonDocument.Parse(await File.ReadAllBytesAsync(hierarchyPath));
    var catalogHash = FinancialCopilot.Application.FinancialData.Semantics.Navigation.CanonicalSemanticCatalogIntegrityValidator.Validate(catalogPath, hierarchy.RootElement);
    Console.WriteLine($"Canonical source and hierarchy hashes valid: {catalogHash}");
    return;
}
if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
{
    ProgramHelpers.RunSelfTests();
    return;
}
var output = GetOption(args, "--output") ?? Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "artifacts");
var catalogRoot = GetOption(args, "--catalog-root") ?? Path.Combine(root, "specs", "141-schema-aware-semantic-query-engine", "SemanticCatalog");
var liveRequested = args.Contains("--live", StringComparer.OrdinalIgnoreCase);
Directory.CreateDirectory(output);

var ef = DiscoverEfModels();
var database = liveRequested ? await DiscoverPostgresAsync(root) : null;
var snapshot = new DiscoverySnapshot(
    1,
    DateTimeOffset.UtcNow,
    liveRequested && database is not null,
    ef,
    database,
    database is null ? (liveRequested ? "No live PostgreSQL metadata was returned; see the console status. No live facts are inferred." : "Live PostgreSQL discovery was not requested.") : null);
var jsonOptions = new JsonSerializerOptions { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
var snapshotPath = Path.Combine(output, "schema-snapshot.json");
await File.WriteAllTextAsync(snapshotPath, JsonSerializer.Serialize(snapshot, jsonOptions) + "\n", new UTF8Encoding(false));

var reconciliation = Reconcile(ef, database);
await File.WriteAllTextAsync(Path.Combine(output, "reconciliation.json"), JsonSerializer.Serialize(reconciliation, jsonOptions) + "\n", new UTF8Encoding(false));
var profile = database is null ? null : await ProfilePostgresAsync(root);
await GenerateCatalogAsync(root, output, catalogRoot, ef, database, reconciliation, profile, jsonOptions);
Console.WriteLine($"Snapshot written: {Path.GetRelativePath(root, snapshotPath)}");
Console.WriteLine($"Live PostgreSQL verified: {snapshot.LiveVerified}");
Console.WriteLine($"EF entities discovered: {ef.Count}");
Console.WriteLine($"Reconciliation differences: {reconciliation.Differences.Count}");

static string FindRepositoryRoot(string start)
{
    var current = new DirectoryInfo(start);
    while (current is not null && !File.Exists(Path.Combine(current.FullName, "README.md"))) current = current.Parent;
    return current?.FullName ?? throw new InvalidOperationException("Repository root not found.");
}

static string? GetOption(string[] arguments, string name)
{
    var index = Array.IndexOf(arguments, name);
    return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
}

static List<EfObject> DiscoverEfModels()
{
    var contexts = typeof(FinancialIngestionDbContext).Assembly.GetTypes()
        .Where(t => !t.IsAbstract && typeof(DbContext).IsAssignableFrom(t))
        .OrderBy(t => t.FullName, StringComparer.Ordinal).ToArray();
    var objects = new List<EfObject>();
    foreach (var contextType in contexts)
    {
        using var context = CreateContext(contextType);
        foreach (var entity in context.Model.GetEntityTypes().OrderBy(e => e.GetSchema()).ThenBy(e => e.GetTableName()))
        {
            var table = StoreObjectIdentifier.Create(entity, StoreObjectType.Table);
            if (table is null) continue;
            var columns = entity.GetProperties().Where(p => !string.Equals(p.GetColumnName(table.Value), "xmin", StringComparison.OrdinalIgnoreCase)).Select(p => new EfColumn(
                p.GetColumnName(table.Value)!, p.GetColumnType(), p.IsNullable,
                entity.FindPrimaryKey()?.Properties.Contains(p) == true,
                p.GetDefaultValueSql() ?? (p.GetDefaultValue() is null ? null : Convert.ToString(p.GetDefaultValue())))).OrderBy(p => p.Name).ToList();
            var primaryKey = entity.FindPrimaryKey()?.Properties.Select(p => p.GetColumnName(table.Value)!).ToArray() ?? [];
            var alternateKeys = entity.GetKeys().Where(k => !k.IsPrimaryKey()).Select(k => k.Properties.Select(p => p.GetColumnName(table.Value)!).ToArray()).ToList();
            var foreignKeys = entity.GetForeignKeys().Select(f => new EfForeignKey(
                f.Properties.Select(p => p.GetColumnName(table.Value)!).ToArray(),
                f.PrincipalEntityType.GetSchema() ?? "public", f.PrincipalEntityType.GetTableName() ?? f.PrincipalEntityType.Name,
                f.PrincipalKey.Properties.Select(p => p.GetColumnName(StoreObjectIdentifier.Create(f.PrincipalEntityType, StoreObjectType.Table)!.Value)!).ToArray(), f.IsUnique, f.IsRequired)).ToList();
            var indexes = entity.GetIndexes().Select(i => new EfIndex(i.Properties.Select(p => p.GetColumnName(table.Value)!).ToArray(), i.IsUnique)).ToList();
            objects.Add(new EfObject(contextType.Name, table.Value.Schema ?? "public", table.Value.Name, columns, primaryKey, alternateKeys, foreignKeys, indexes));
        }
    }
    return objects.OrderBy(o => o.Schema).ThenBy(o => o.Name).ToList();
}

static DbContext CreateContext(Type contextType)
{
    var method = typeof(ProgramHelpers).GetMethod(nameof(ProgramHelpers.CreateGeneric), System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!
        .MakeGenericMethod(contextType);
    return (DbContext)method.Invoke(null, null)!;
}

static async Task<List<PgObject>?> DiscoverPostgresAsync(string root)
{
    var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__FinancialCopilot");
    if (string.IsNullOrWhiteSpace(connectionString)) connectionString = ReadDotEnvValue(Path.Combine(root, ".env"), "ConnectionStrings__FinancialCopilot");
    if (string.IsNullOrWhiteSpace(connectionString)) connectionString = ReadJsonConnection(Path.Combine(root, "src", "backend", "FinancialCopilot.API", "appsettings.Development.json"));
    if (string.IsNullOrWhiteSpace(connectionString)) connectionString = ReadJsonConnection(Path.Combine(root, "src", "backend", "FinancialCopilot.API", "appsettings.json"));
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        Console.WriteLine("Live discovery skipped: ConnectionStrings:FinancialCopilot was empty or absent from process environment, repository .env, and API development settings.");
        return null;
    }
    try
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Timeout = 5, CommandTimeout = 10, Pooling = false };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using (var readOnly = new NpgsqlCommand("SET SESSION CHARACTERISTICS AS TRANSACTION READ ONLY", connection)) await readOnly.ExecuteNonQueryAsync();
        var sql = """
            SELECT n.nspname AS schema_name, c.relname AS object_name,
                   CASE c.relkind WHEN 'r' THEN 'table' WHEN 'p' THEN 'partitioned_table' WHEN 'v' THEN 'view' WHEN 'm' THEN 'materialized_view' ELSE c.relkind::text END AS object_type,
                   a.attname AS column_name, format_type(a.atttypid, a.atttypmod) AS data_type,
                   NOT a.attnotnull AS is_nullable, pg_get_expr(ad.adbin, ad.adrelid) AS default_value,
                   a.attnum AS ordinal_position
            FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
            LEFT JOIN pg_attribute a ON a.attrelid=c.oid AND a.attnum>0 AND NOT a.attisdropped
            LEFT JOIN pg_attrdef ad ON ad.adrelid=c.oid AND ad.adnum=a.attnum
            WHERE n.nspname NOT IN ('pg_catalog','information_schema') AND c.relkind IN ('r','p','v','m')
            ORDER BY n.nspname,c.relname,a.attnum
            """;
        var objects = new Dictionary<string, PgObject>(StringComparer.Ordinal);
        await using (var command = new NpgsqlCommand(sql, connection))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var schema = reader.GetString(0); var name = reader.GetString(1); var key = schema + "." + name;
                if (!objects.TryGetValue(key, out var item)) objects[key] = item = new PgObject(schema, name, reader.GetString(2), [], [], [], []);
                if (!reader.IsDBNull(3)) item.Columns.Add(new PgColumn(reader.GetString(3), reader.GetString(4), reader.GetBoolean(5), reader.IsDBNull(6) ? null : reader.GetString(6)));
            }
        }
        await LoadPgConstraintsAsync(connection, objects);
        await LoadMigrationVersionsAsync(connection, objects);
        return objects.Values.OrderBy(o => o.Schema).ThenBy(o => o.Name).ToList();
    }
    catch (Exception ex) when (ex is NpgsqlException or TimeoutException or InvalidOperationException)
    {
        // Intentionally suppress provider messages which may include server/credential details.
        Console.WriteLine($"Live discovery unavailable ({ex.GetType().Name}); continuing with offline EF metadata.");
        return null;
    }
}

static async Task<ProfileResult?> ProfilePostgresAsync(string root)
{
    var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__FinancialCopilot");
    if (string.IsNullOrWhiteSpace(connectionString)) connectionString = ReadDotEnvValue(Path.Combine(root, ".env"), "ConnectionStrings__FinancialCopilot");
    if (string.IsNullOrWhiteSpace(connectionString)) connectionString = ReadJsonConnection(Path.Combine(root, "src", "backend", "FinancialCopilot.API", "appsettings.Development.json"));
    if (string.IsNullOrWhiteSpace(connectionString)) connectionString = ReadJsonConnection(Path.Combine(root, "src", "backend", "FinancialCopilot.API", "appsettings.json"));
    if (string.IsNullOrWhiteSpace(connectionString)) return null;
    try
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Timeout = 5, CommandTimeout = 10, Pooling = false };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using (var readOnly = new NpgsqlCommand("SET SESSION CHARACTERISTICS AS TRANSACTION READ ONLY", connection)) await readOnly.ExecuteNonQueryAsync();
        var companies = await SampleLabelsAsync(connection, "SELECT \"Id\", \"Name\", \"Ticker\", \"IndustryId\" FROM public.\"Companies\" ORDER BY \"Id\" LIMIT 1000");
        var industries = await SampleLabelsAsync(connection, "SELECT \"Id\", \"Name\", \"ParentId\" FROM public.\"Industries\" ORDER BY \"Id\" LIMIT 1000");
        var recentMix = await SampleLabelsAsync(connection, "SELECT \"ProviderName\", \"ReportType\", \"OutputType\", \"IsAccepted\" FROM public.\"MonthlyReports\" ORDER BY \"PeriodEnd\" DESC NULLS LAST, \"Id\" DESC LIMIT 500");
        var profiles = recentMix.GroupBy(row => string.Join(" / ", row.Values.Select(v => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? "null")), StringComparer.Ordinal)
            .Select(g => new ReportProfile(g.First().Values.Select(v => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? "null").ToArray(), g.Count())).OrderByDescending(g => g.Count).ToList();
        foreach (var item in profiles.Take(8)) Console.WriteLine($"Recent report profile: {string.Join(" / ", item.Values)} ({item.Count} sampled)");
        const string sql = """
            WITH recent AS (
                SELECT "Id", "CompanyId", "ExternalCompanyId", "ExternalReportId", "ProviderName", "PeriodStart", "PeriodEnd", "OutputType", "ReportType", "IsAccepted"
                FROM public."MonthlyReports"
                WHERE "ProviderName" = 'NoavaranCurrentApi' AND "ReportType" = 'ProductSales' AND "OutputType" = 0 AND "IsAccepted" = TRUE
                ORDER BY "PeriodEnd" DESC NULLS LAST, "Id" DESC LIMIT 500
            )
            SELECT r."CompanyId", c."Name", c."Ticker", r."ExternalCompanyId", r."ExternalReportId", r."PeriodStart", r."PeriodEnd",
                   r."ProviderName", r."ReportType", r."OutputType", r."IsAccepted",
                   l."ProviderProductId", l."ProviderProductCode", l."ProductCode", l."Title", l."Unit",
                   l."ProductionQuantity", l."SalesQuantity", l."SalesAmount", l."SalesRate"
            FROM recent r JOIN public."MonthlyReportLineItems" l ON l."MonthlyReportId"=r."Id"
            LEFT JOIN public."Companies" c ON c."Id"=r."CompanyId"
            ORDER BY r."PeriodEnd" DESC NULLS LAST, r."Id", l."Id" LIMIT 5000
            """;
        var rows = new List<ProfileLineItem>();
        await using (var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 10 })
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                rows.Add(new ProfileLineItem(
                    reader.IsDBNull(0) ? null : reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetFieldValue<DateOnly>(5), reader.IsDBNull(6) ? null : reader.GetFieldValue<DateOnly>(6),
                    reader.GetString(7), reader.GetString(8), reader.GetInt32(9), reader.GetBoolean(10),
                    reader.IsDBNull(11) ? null : reader.GetString(11), reader.IsDBNull(12) ? null : reader.GetString(12), reader.IsDBNull(13) ? null : reader.GetString(13),
                    reader.IsDBNull(14) ? null : reader.GetString(14), reader.IsDBNull(15) ? null : reader.GetString(15),
                    reader.IsDBNull(16) ? null : reader.GetDecimal(16), reader.IsDBNull(17) ? null : reader.GetDecimal(17),
                    reader.IsDBNull(18) ? null : reader.GetDecimal(18), reader.IsDBNull(19) ? null : reader.GetDecimal(19)));
            }
        }
        return new ProfileResult(companies, industries, rows, "NoavaranCurrentApi/ProductSales/OutputType=0/IsAccepted=true", 500, 5000, profiles);
    }
    catch (Exception ex) when (ex is NpgsqlException or TimeoutException or InvalidOperationException)
    {
        var state = (ex as PostgresException)?.SqlState ?? (ex as NpgsqlException)?.SqlState ?? "n/a";
        Console.WriteLine($"Bounded financial profiling unavailable ({ex.GetType().Name}, SQLSTATE {state}): {ProgramHelpers.RedactDiagnostic(ex.Message)}. No product values were generated.");
        return null;
    }
}

static async Task<List<Dictionary<string, object?>>> SampleLabelsAsync(NpgsqlConnection connection, string sql)
{
    var result = new List<Dictionary<string, object?>>();
    await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 10 };
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var row = new Dictionary<string, object?>();
        for (var i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
        result.Add(row);
    }
    return result;
}

static async Task LoadPgConstraintsAsync(NpgsqlConnection connection, Dictionary<string, PgObject> objects)
{
    const string sql = """
        SELECT ns.nspname, tbl.relname, con.conname, con.contype,
               array_agg(att.attname ORDER BY k.ord),
               refns.nspname, ref.relname, array_agg(refatt.attname ORDER BY k.ord), pg_get_constraintdef(con.oid)
        FROM pg_constraint con JOIN pg_class tbl ON tbl.oid=con.conrelid JOIN pg_namespace ns ON ns.oid=tbl.relnamespace
        LEFT JOIN pg_class ref ON ref.oid=con.confrelid LEFT JOIN pg_namespace refns ON refns.oid=ref.relnamespace
        LEFT JOIN LATERAL unnest(con.conkey) WITH ORDINALITY k(attnum,ord) ON true
        LEFT JOIN pg_attribute att ON att.attrelid=tbl.oid AND att.attnum=k.attnum
        LEFT JOIN pg_attribute refatt ON refatt.attrelid=ref.oid AND refatt.attnum=con.confkey[k.ord]
        WHERE con.contype IN ('p','f','u','c')
        GROUP BY ns.nspname,tbl.relname,con.conname,con.contype,refns.nspname,ref.relname,con.oid
        ORDER BY ns.nspname,tbl.relname,con.conname
        """;
    await using var command = new NpgsqlCommand(sql, connection);
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var key = reader.GetString(0) + "." + reader.GetString(1);
        if (!objects.TryGetValue(key, out var target)) continue;
        var type = reader.GetChar(3);
        var cols = reader.IsDBNull(4) ? [] : reader.GetFieldValue<string[]>(4);
        var definition = reader.GetString(8);
        target.Constraints.Add(new PgConstraint(reader.GetString(2), type.ToString(), cols, definition));
        if (type == 'p') target.PrimaryKeys.AddRange(cols);
        if (type == 'f') target.ForeignKeys.Add(new PgForeignKey(cols,
            reader.IsDBNull(5) ? "" : reader.GetString(5), reader.IsDBNull(6) ? "" : reader.GetString(6),
            reader.IsDBNull(7) ? [] : reader.GetFieldValue<string[]>(7), definition));
    }
    await reader.DisposeAsync();
    const string indexSql = """
        SELECT ns.nspname,t.relname,i.relname,ix.indisunique,pg_get_indexdef(ix.indexrelid),
               array_agg(a.attname ORDER BY k.ord) FILTER (WHERE a.attnum IS NOT NULL AND k.ord <= ix.indnkeyatts)
        FROM pg_index ix JOIN pg_class t ON t.oid=ix.indrelid JOIN pg_namespace ns ON ns.oid=t.relnamespace
        JOIN pg_class i ON i.oid=ix.indexrelid
        LEFT JOIN LATERAL unnest(ix.indkey) WITH ORDINALITY k(attnum,ord) ON k.ord <= ix.indnkeyatts
        LEFT JOIN pg_attribute a ON a.attrelid=t.oid AND a.attnum=k.attnum
        GROUP BY ns.nspname,t.relname,i.relname,ix.indisunique,ix.indexrelid,ix.indnkeyatts
        ORDER BY ns.nspname,t.relname,i.relname
        """;
    await using var indexCommand = new NpgsqlCommand(indexSql, connection);
    await using var indexReader = await indexCommand.ExecuteReaderAsync();
    while (await indexReader.ReadAsync())
        if (objects.TryGetValue(indexReader.GetString(0) + "." + indexReader.GetString(1), out var target))
            target.Indexes.Add(new PgIndex(indexReader.GetString(2), indexReader.GetBoolean(3), indexReader.GetString(4), indexReader.IsDBNull(5) ? [] : indexReader.GetFieldValue<string[]>(5)));
}

static async Task LoadMigrationVersionsAsync(NpgsqlConnection connection, Dictionary<string, PgObject> objects)
{
    await using var command = new NpgsqlCommand("SELECT to_regclass('public.\"__EFMigrationsHistory\"') IS NOT NULL", connection);
    var exists = (bool)(await command.ExecuteScalarAsync() ?? false);
    if (!exists) return;
    await using var history = new NpgsqlCommand("SELECT \"MigrationId\", \"ProductVersion\" FROM public.\"__EFMigrationsHistory\" ORDER BY 1", connection);
    await using var reader = await history.ExecuteReaderAsync();
    var list = new List<string>();
    while (await reader.ReadAsync()) list.Add(reader.GetString(0) + " (EF " + reader.GetString(1) + ")");
    if (objects.Count > 0) objects.Values.First().MigrationHistory.AddRange(list);
}

static string? ReadDotEnvValue(string path, string key)
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

static string? ReadJsonConnection(string path)
{
    if (!File.Exists(path)) return null;
    try { return JsonDocument.Parse(File.ReadAllText(path)).RootElement.GetProperty("ConnectionStrings").GetProperty("FinancialCopilot").GetString(); }
    catch { return null; }
}

static Reconciliation Reconcile(List<EfObject> ef, List<PgObject>? db)
{
    if (db is null) return new Reconciliation(false, [new Difference("LIVE_UNAVAILABLE", "Live database metadata was not verified; EF-only objects cannot be reconciled.", "warning")]);
    var live = db.ToDictionary(o => o.Schema + "." + o.Name, StringComparer.Ordinal);
    var diffs = new List<Difference>();
    foreach (var item in ef)
    {
        var key = item.Schema + "." + item.Name;
        if (!live.TryGetValue(key, out var deployed)) { diffs.Add(new Difference("EF_ONLY_OBJECT", key, "breaking")); continue; }
        var dbCols = deployed.Columns.ToDictionary(c => c.Name, StringComparer.Ordinal);
        foreach (var col in item.Columns)
        {
            if (!dbCols.TryGetValue(col.Name, out var actual)) diffs.Add(new Difference("EF_ONLY_COLUMN", key + "." + col.Name, "breaking"));
            else if (actual.Nullable != col.Nullable) diffs.Add(new Difference("NULLABILITY_MISMATCH", key + "." + col.Name, "breaking"));
            else if (!string.Equals(NormalizeType(actual.Type), NormalizeType(col.StoreType), StringComparison.Ordinal)) diffs.Add(new Difference("TYPE_MISMATCH", key + "." + col.Name + " (EF " + col.StoreType + "; PostgreSQL " + actual.Type + ")", "review"));
        }
        foreach (var col in deployed.Columns)
            if (item.Columns.All(c => c.Name != col.Name)) diffs.Add(new Difference("DB_ONLY_COLUMN", key + "." + col.Name, "warning"));
        if (!item.PrimaryKey.SequenceEqual(deployed.PrimaryKeys, StringComparer.Ordinal)) diffs.Add(new Difference("PRIMARY_KEY_MISMATCH", key, "breaking"));
        var efFks = item.ForeignKeys.Select(f => ForeignKeySignature(f.Columns, f.PrincipalSchema, f.PrincipalTable, f.PrincipalColumns)).ToHashSet(StringComparer.Ordinal);
        var dbFks = deployed.ForeignKeys.Select(f => ForeignKeySignature(f.Columns, f.PrincipalSchema, f.PrincipalTable, f.PrincipalColumns)).ToHashSet(StringComparer.Ordinal);
        foreach (var fk in efFks.Except(dbFks, StringComparer.Ordinal)) diffs.Add(new Difference("EF_ONLY_FOREIGN_KEY", key + "." + fk, "breaking"));
        foreach (var fk in dbFks.Except(efFks, StringComparer.Ordinal)) diffs.Add(new Difference("DB_ONLY_FOREIGN_KEY", key + "." + fk, "review"));
        var efIndexSignatures = item.Indexes.Select(i => string.Join('|', string.Join(',', i.Columns), i.Unique)).ToHashSet(StringComparer.Ordinal);
        if (item.PrimaryKey.Length > 0) efIndexSignatures.Add(string.Join('|', string.Join(',', item.PrimaryKey), true));
        foreach (var alternate in item.AlternateKeys) efIndexSignatures.Add(string.Join('|', string.Join(',', alternate), true));
        var dbIndexSignatures = deployed.Indexes.Select(i => string.Join('|', string.Join(',', i.Columns), i.Unique)).ToHashSet(StringComparer.Ordinal);
        foreach (var index in efIndexSignatures.Except(dbIndexSignatures, StringComparer.Ordinal)) diffs.Add(new Difference("EF_ONLY_INDEX", key + "." + index, "review"));
        foreach (var index in dbIndexSignatures.Except(efIndexSignatures, StringComparer.Ordinal)) diffs.Add(new Difference("DB_ONLY_INDEX", key + "." + index, "inventory"));
    }
    foreach (var item in db)
        if (item.Name != "__EFMigrationsHistory" && ef.All(e => e.Schema + "." + e.Name != item.Schema + "." + item.Name)) diffs.Add(new Difference("DB_ONLY_OBJECT", item.Schema + "." + item.Name, "inventory"));
    return new Reconciliation(true, diffs.OrderBy(d => d.Code).ThenBy(d => d.Object).ToList());
}

static string NormalizeType(string? value)
{
    var normalized = string.Join(' ', (value ?? "").Trim().ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    return normalized.StartsWith("char(", StringComparison.Ordinal) ? "character" + normalized[4..] : normalized;
}
static NumericCoverage Summarize(IEnumerable<decimal?> values)
{
    var observed = values.ToArray();
    var present = observed.Where(v => v.HasValue).Select(v => v!.Value).ToArray();
    return new NumericCoverage(observed.Length, present.Length,
        present.Length == 0 ? null : present.Min().ToString(System.Globalization.CultureInfo.InvariantCulture),
        present.Length == 0 ? null : present.Max().ToString(System.Globalization.CultureInfo.InvariantCulture));
}
static string ForeignKeySignature(string[] columns, string schema, string table, string[] principalColumns) =>
    string.Join('|', string.Join(',', columns), schema, table, string.Join(',', principalColumns));

static async Task GenerateCatalogAsync(string root, string output, string catalogRoot, List<EfObject> ef, List<PgObject>? db, Reconciliation reconciliation, ProfileResult? profile, JsonSerializerOptions options)
{
    foreach (var folder in new[] { "tables", "relationships", "metrics", "dimensions", "dictionaries" }) Directory.CreateDirectory(Path.Combine(catalogRoot, folder));
    var physical = db is not null
        ? db.Select(o => new CatalogPhysical(o.Schema, o.Name, o.ObjectType, o.Columns, o.PrimaryKeys, o.ForeignKeys, o.Indexes, o.Constraints)).ToList()
        : ef.Select(o => new CatalogPhysical(o.Schema, o.Name, "ef_table",
            o.Columns.Select(c => new PgColumn(c.Name, c.StoreType ?? "unknown", c.Nullable, c.DefaultValue)).ToList(),
            o.PrimaryKey.ToList(), [], o.Indexes.Select(i => new PgIndex("", i.Unique, string.Join(',', i.Columns), i.Columns)).ToList(), [])).ToList();
    var physicalColumns = physical.ToDictionary(o => o.Schema + "." + o.Name, o => o.Columns.Select(c => c.Name).ToHashSet(StringComparer.Ordinal), StringComparer.Ordinal);
    // Small, bounded initial analytical candidate scope. The rest of the discovered schema remains
    // physical inventory only and is not promoted to queryable metadata.
    var candidates = new HashSet<string>(StringComparer.Ordinal)
    {
        "Companies", "Markets", "Industries", "IndustryGroups",
        "MonthlyReports", "MonthlyReportLineItems",
        "FinancialStatements", "FinancialStatementSourceItems", "FinancialStatementLineItems",
        "FinancialStatementSourceItemMetricMappings", "DerivedMetrics",
        "CompanyIndustryRelativeValuations", "CompanyMonthlyActivityTrendSnapshots",
        "IntradayTradeSnapshots", "DailyInstrumentTrades", "IntradayIndexSnapshots", "DailyIndexSnapshots"
    };
    var tables = new List<object>();
    var ids = new List<string>();
    var relationshipIds = new List<string>();
    var metricIds = new List<string>();
    var catalogDrift = new List<Difference>();
    foreach (var obj in physical.OrderBy(o => o.Schema).ThenBy(o => o.Name))
    {
        var stableId = ProgramHelpers.StableId(obj.Schema, obj.Name);
        ids.Add(stableId);
        var selected = candidates.Contains(obj.Name) && obj.Schema == "public";
        var tablePath = Path.Combine(catalogRoot, "tables", stableId + ".yaml");
        var generatedSemantic = JsonSerializer.SerializeToElement(new { status = selected ? "unapproved" : "discovered", queryable = false, approval = (string?)null, description = (string?)null }, options);
        var semantic = generatedSemantic;
        if (File.Exists(tablePath))
        {
            try
            {
                using var previous = JsonDocument.Parse(await File.ReadAllTextAsync(tablePath));
                if (previous.RootElement.TryGetProperty("semantic", out var existingSemantic))
                {
                    var physicalChanged = false;
                    if (previous.RootElement.TryGetProperty("physical", out var existingPhysical))
                    {
                        var existingNode = JsonNode.Parse(existingPhysical.GetRawText()) as JsonObject;
                        existingNode?.Remove("MigrationHistory");
                        var generatedNode = JsonNode.Parse(JsonSerializer.Serialize(obj, options));
                        physicalChanged = !JsonNode.DeepEquals(existingNode, generatedNode);
                        if (physicalChanged)
                            catalogDrift.Add(new Difference("CATALOG_PHYSICAL_DRIFT", obj.Schema + "." + obj.Name, "review"));
                    }
                    semantic = ProgramHelpers.PreserveSemantic(existingSemantic, physicalChanged);
                }
            }
            catch (JsonException) { catalogDrift.Add(new Difference("UNREADABLE_EXISTING_METADATA", obj.Schema + "." + obj.Name, "blocking")); }
        }
        var record = new { id = stableId, physical = obj, semantic, provenance = new { source = db is null ? "ef-core-model" : "postgres-pg_catalog", verified = db is not null } };
        tables.Add(record);
        await File.WriteAllTextAsync(tablePath, JsonSerializer.Serialize(record, options) + "\n", new UTF8Encoding(false));
    }
    var activeIds = ids.ToHashSet(StringComparer.Ordinal);
    foreach (var oldFile in Directory.EnumerateFiles(Path.Combine(catalogRoot, "tables"), "*.yaml").OrderBy(p => p, StringComparer.Ordinal))
    {
        var oldId = Path.GetFileNameWithoutExtension(oldFile);
        if (!activeIds.Contains(oldId)) catalogDrift.Add(new Difference("CATALOG_OBJECT_MISSING_FROM_SOURCE", oldId, "breaking"));
    }
    if (db is not null)
    {
        foreach (var owner in db.OrderBy(o => o.Schema).ThenBy(o => o.Name))
        foreach (var fk in owner.ForeignKeys.OrderBy(f => string.Join(',', f.Columns), StringComparer.Ordinal))
        {
            var referenced = db.FirstOrDefault(o => o.Schema == fk.PrincipalSchema && o.Name == fk.PrincipalTable);
            if (!ProgramHelpers.HasPhysicalReference(owner.Schema + "." + owner.Name, fk.Columns, physicalColumns) || referenced is null || !ProgramHelpers.HasPhysicalReference(fk.PrincipalSchema + "." + fk.PrincipalTable, fk.PrincipalColumns, physicalColumns))
                throw new InvalidDataException("Foreign key references missing physical metadata.");
            var unique = owner.PrimaryKeys.SequenceEqual(fk.Columns, StringComparer.Ordinal) || owner.Constraints.Any(c => c.Type == "u" && c.Columns.SequenceEqual(fk.Columns, StringComparer.Ordinal)) || owner.Indexes.Any(i => i.Unique && i.Columns.SequenceEqual(fk.Columns, StringComparer.Ordinal));
            var identity = string.Join('.', owner.Schema, owner.Name, string.Join(',', fk.Columns), fk.PrincipalSchema, fk.PrincipalTable, string.Join(',', fk.PrincipalColumns));
            var relationship = new { id = "rel_" + ProgramHelpers.StableId(owner.Schema + "." + owner.Name, identity)[4..], from = new { schema = owner.Schema, table = owner.Name, columns = fk.Columns }, to = new { schema = fk.PrincipalSchema, table = fk.PrincipalTable, columns = fk.PrincipalColumns }, physicalConstraint = fk.Definition, inferredCardinality = unique ? "one-to-one" : "many-to-one", semantic = new { approved = false, status = "discovered-unapproved", runtimeJoinable = false }, provenance = new { source = "postgres-pg_catalog", verified = referenced is not null } };
            relationshipIds.Add(relationship.id);
            await File.WriteAllTextAsync(Path.Combine(catalogRoot, "relationships", relationship.id + ".yaml"), JsonSerializer.Serialize(relationship, options) + "\n", new UTF8Encoding(false));
        }
    }
    foreach (var definition in PhaseOneFinancialSemanticCatalog.Definitions.OrderBy(d => d.Code.Value, StringComparer.Ordinal))
    {
        var metric = new { id = "metric_" + definition.Code.Value.ToLowerInvariant(), sourceDefinition = definition, provenance = new { source = "src/backend/FinancialCopilot.Domain/Financial/Metrics/PhaseOneFinancialSemanticCatalog.cs", authority = "Feature 015 existing governed definition", verified = true }, genericQuery = new { approved = false, executable = false, reviewRequired = true } };
        metricIds.Add(metric.id);
        await File.WriteAllTextAsync(Path.Combine(catalogRoot, "metrics", metric.id + ".yaml"), JsonSerializer.Serialize(metric, options) + "\n", new UTF8Encoding(false));
    }
    var dimensionProposals = new[]
    {
        new { id = "dim_company", physicalTableId = ProgramHelpers.StableId("public", "Companies"), columns = new[] { "Id", "Name", "Ticker", "IndustryId" }, status = "unapproved", runtimeQueryable = false },
        new { id = "dim_industry", physicalTableId = ProgramHelpers.StableId("public", "Industries"), columns = new[] { "Id", "Name", "ParentId" }, status = "unapproved", runtimeQueryable = false },
        new { id = "dim_monthly_period", physicalTableId = ProgramHelpers.StableId("public", "MonthlyReports"), columns = new[] { "PeriodStart", "PeriodEnd" }, status = "unapproved", runtimeQueryable = false },
        new { id = "dim_product_identity", physicalTableId = ProgramHelpers.StableId("public", "MonthlyReportLineItems"), columns = new[] { "ProviderProductId", "ProviderProductCode", "ProductCode", "Title", "Unit" }, status = "unapproved", runtimeQueryable = false }
    };
    foreach (var dimension in dimensionProposals)
    {
        var tableName = dimension.id switch { "dim_company" => "public.Companies", "dim_industry" => "public.Industries", "dim_monthly_period" => "public.MonthlyReports", _ => "public.MonthlyReportLineItems" };
        if (!ProgramHelpers.HasPhysicalReference(tableName, dimension.columns, physicalColumns))
            throw new InvalidDataException("Dimension references a missing physical table or column: " + dimension.id);
        await File.WriteAllTextAsync(Path.Combine(catalogRoot, "dimensions", dimension.id + ".yaml"), JsonSerializer.Serialize(dimension, options) + "\n", new UTF8Encoding(false));
    }
    var scopeCandidates = new[]
    {
        new { table = "public.Companies", id = ProgramHelpers.StableId("public", "Companies"), purpose = "Company identity and industry/market dimensions", classification = "public listed-company reference data", rowOwnership = "global company rows; canonical identity resolution required", sourcePolicy = "read company catalog columns only; sensitive national/registration attributes require separate review", owner = "Financial data domain", queryable = false, status = "candidate-pending-review" },
        new { table = "public.Industries", id = ProgramHelpers.StableId("public", "Industries"), purpose = "Industry dimension", classification = "public market classification", rowOwnership = "global industry rows", sourcePolicy = "only verified parent relationship; no inferred sector classification", owner = "Financial data domain", queryable = false, status = "candidate-pending-review" },
        new { table = "public.IndustryGroups", id = ProgramHelpers.StableId("public", "IndustryGroups"), purpose = "Industry-group dimension proposal", classification = "public market classification", rowOwnership = "global industry-group rows", sourcePolicy = "mapping to companies/industries requires verified FK or reviewed mapping", owner = "Financial data domain", queryable = false, status = "candidate-pending-review" },
        new { table = "public.MonthlyReports", id = ProgramHelpers.StableId("public", "MonthlyReports"), purpose = "Monthly report header, period, revision and acceptance provenance", classification = "public financial disclosure metadata", rowOwnership = "company + provider + logical report/revision", sourcePolicy = "company/product sales sources must retain provider and accepted revision; NoavaranCurrentApi/ProductSales/output type 0 for monthly actuals", owner = "Financial ingestion domain", queryable = false, status = "candidate-pending-review" },
        new { table = "public.MonthlyReportLineItems", id = ProgramHelpers.StableId("public", "MonthlyReportLineItems"), purpose = "Product-scoped production/sales observations", classification = "public financial disclosure data", rowOwnership = "company-scoped provider product identity and source report row", sourcePolicy = "must join accepted ProductSales report; output type 0 for single-month facts; never merge product labels across companies; retain original unit and source IDs", owner = "Financial ingestion domain", queryable = false, status = "candidate-pending-review" }
    };
    var excludedObjects = physical.Where(o => !candidates.Contains(o.Name) || o.Schema != "public").Select(o => new
    {
        physical = o.Schema + "." + o.Name,
        id = ProgramHelpers.StableId(o.Schema, o.Name),
        mappedContexts = ef.Where(e => e.Schema == o.Schema && e.Name == o.Name).Select(e => e.Context).Distinct().OrderBy(v => v, StringComparer.Ordinal).ToArray(),
        queryable = false,
        reason = "Not in the approved Phase 1 candidate surface; metadata-only inventory, no business rows exposed."
    }).OrderBy(o => o.physical, StringComparer.Ordinal).ToArray();
    var scopePath = Path.Combine(catalogRoot, "catalog-scope.yaml");
    var scopeNode = JsonSerializer.SerializeToNode(new { generatedFrom = db is null ? "EF Core offline model" : "live PostgreSQL metadata and EF Core comparison", liveVerified = db is not null, includedCandidates = scopeCandidates, excludedByDefault = excludedObjects, dataPolicy = new { rowDataAccess = "limited bounded profile only", defaultRuntimeQuerying = false, allowlistRequired = true, secretData = "excluded" }, reason = "All listed candidates are unapproved and non-queryable until owner review of physical relationships, semantics, provider/report filters, units and identity policy." }, options)!.AsObject();
    if (File.Exists(scopePath))
    {
        try
        {
            var existingScope = JsonNode.Parse(await File.ReadAllTextAsync(scopePath)) as JsonObject;
            if (existingScope is not null)
            {
                // Keep reviewed/proposed scope and source-policy fields across physical refreshes.
                if (existingScope["includedCandidates"] is JsonArray existingCandidates)
                    scopeNode["includedCandidates"] = existingCandidates.DeepClone();
                foreach (var property in existingScope)
                    if (!scopeNode.ContainsKey(property.Key)) scopeNode[property.Key] = property.Value?.DeepClone();
            }
        }
        catch (JsonException)
        {
            catalogDrift.Add(new Difference("UNREADABLE_EXISTING_SCOPE", scopePath, "blocking"));
        }
    }
    await File.WriteAllTextAsync(scopePath, scopeNode.ToJsonString(options) + "\n", new UTF8Encoding(false));
    var companyIndustryProfile = profile is null
        ? new { companiesInspected = 0, companyNamesObserved = 0, companyTickersObserved = 0, companiesWithIndustryId = 0, industriesInspected = 0, industryNamesObserved = 0, industriesWithParentId = 0, sampleCap = 1000 }
        : new { companiesInspected = profile.Companies.Count, companyNamesObserved = profile.Companies.Count(r => r.TryGetValue("Name", out var value) && value is string text && !string.IsNullOrWhiteSpace(text)), companyTickersObserved = profile.Companies.Count(r => r.TryGetValue("Ticker", out var value) && value is string text && !string.IsNullOrWhiteSpace(text)), companiesWithIndustryId = profile.Companies.Count(r => r.TryGetValue("IndustryId", out var value) && value is not null), industriesInspected = profile.Industries.Count, industryNamesObserved = profile.Industries.Count(r => r.TryGetValue("Name", out var value) && value is string text && !string.IsNullOrWhiteSpace(text)), industriesWithParentId = profile.Industries.Count(r => r.TryGetValue("ParentId", out var value) && value is not null), sampleCap = 1000 };
    var profileReport = profile is null
        ? new { liveDataProfiled = false, source = "none", boundedCompanySample = 0, boundedIndustrySample = 0, companyIndustryProfile, sampledAcceptedReports = 0, sampledLineItems = 0, unitCounts = new Dictionary<string, int>(), providers = new Dictionary<string, int>(), recentReportProfile = Array.Empty<string>(), periodRange = new { earliest = (string?)null, latest = (string?)null }, numericFields = new Dictionary<string, NumericCoverage>(), numericComparability = "Not profiled", supportAssessment = new Dictionary<string, string> { ["company-and-industry-inventory"] = "not verified", ["monthly-company-sales-production"] = "not verified", ["product-specific-growth-and-cross-company-product-categories"] = "not supported without reviewed product/category and calculation policies" }, reason = "Live data profiling was unavailable; no values are fabricated." }
        : new { liveDataProfiled = true, source = profile.SourceFilter, boundedCompanySample = profile.Companies.Count, boundedIndustrySample = profile.Industries.Count, companyIndustryProfile, sampledAcceptedReports = profile.Items.Select(i => i.ExternalReportId).Distinct().Count(), sampledLineItems = profile.Items.Count, unitCounts = profile.Items.Where(i => !string.IsNullOrWhiteSpace(i.Unit)).GroupBy(i => i.Unit!).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal), providers = profile.Items.GroupBy(i => i.ProviderName).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal), recentReportProfile = profile.RecentReportProfiles.Select(p => string.Join(" / ", p.Values) + ": " + p.Count).ToArray(), periodRange = new { earliest = (string?)profile.Items.Where(i => i.PeriodStart.HasValue).Select(i => i.PeriodStart!.Value).DefaultIfEmpty().Min().ToString("yyyy-MM-dd"), latest = (string?)profile.Items.Where(i => i.PeriodEnd.HasValue).Select(i => i.PeriodEnd!.Value).DefaultIfEmpty().Max().ToString("yyyy-MM-dd") }, numericFields = new Dictionary<string, NumericCoverage> { ["productionQuantity"] = Summarize(profile.Items.Select(i => i.ProductionQuantity)), ["salesQuantity"] = Summarize(profile.Items.Select(i => i.SalesQuantity)), ["salesAmount"] = Summarize(profile.Items.Select(i => i.SalesAmount)), ["salesRate"] = Summarize(profile.Items.Select(i => i.SalesRate)) }, numericComparability = "Do not aggregate across product identities or mixed units; these are coverage/range diagnostics only.", supportAssessment = new Dictionary<string, string> { ["company-and-industry-inventory"] = "bounded labels sampled; full coverage is not claimed", ["monthly-company-sales-production"] = "accepted NoavaranCurrentApi ProductSales single-month line items observed; generic aggregation remains unapproved and existing specialized trend capability takes precedence", ["product-specific-growth-and-cross-company-product-categories"] = "raw labels observed but product equivalence/category and unit-compatible growth require review" }, reason = "Read-only bounded sample; 500 recent reports and at most 5000 accepted single-month line items; provider/report/output filters follow Features 076/129." };
    await File.WriteAllTextAsync(Path.Combine(output, "profiling-report.json"), JsonSerializer.Serialize(profileReport, options) + "\n", new UTF8Encoding(false));
    await File.WriteAllTextAsync(Path.Combine(output, "catalog-drift.json"), JsonSerializer.Serialize(catalogDrift.OrderBy(d => d.Code).ThenBy(d => d.Object).ToList(), options) + "\n", new UTF8Encoding(false));
    var productEntries = profile?.Items.Where(i => (i.CompanyId.HasValue || !string.IsNullOrWhiteSpace(i.ExternalCompanyId)) && (!string.IsNullOrWhiteSpace(i.ProviderProductId) || !string.IsNullOrWhiteSpace(i.ProviderProductCode) || !string.IsNullOrWhiteSpace(i.ProductCode) || !string.IsNullOrWhiteSpace(i.Title)))
        .GroupBy(i => string.Join("|", i.CompanyId?.ToString() ?? "external:" + i.ExternalCompanyId, i.ProviderName, i.ProviderProductId ?? "", i.ProviderProductCode ?? "", i.ProductCode ?? "", i.Title ?? "", i.Unit ?? ""), StringComparer.Ordinal)
        .Select(g =>
        {
            var i = g.First();
            var companyScope = i.CompanyId?.ToString() ?? "external:" + i.ExternalCompanyId;
            var identity = ProgramHelpers.StableId("product", string.Join("|", companyScope, i.ProviderName, i.ProviderProductId ?? i.ProviderProductCode ?? i.ProductCode, i.Title, i.Unit));
            return new { id = identity, companyId = i.CompanyId, externalCompanyId = i.ExternalCompanyId, companyResolution = i.CompanyId.HasValue ? "canonical-company-id" : "external-company-only-pending-resolution", companyName = i.CompanyName, companyTicker = i.CompanyTicker, provider = i.ProviderName, providerProductId = i.ProviderProductId, providerProductCode = i.ProviderProductCode, productCode = i.ProductCode, sourceLabel = i.Title, unit = i.Unit, provenance = new { externalReportId = i.ExternalReportId, periodStart = i.PeriodStart, periodEnd = i.PeriodEnd, reportType = i.ReportType, outputType = i.OutputType, accepted = i.IsAccepted }, review = new { status = "pending", executable = false, aliases = Array.Empty<string>(), categoryProposal = (string?)null } };
        }).OrderBy(e => e.companyId).ThenBy(e => e.sourceLabel, StringComparer.Ordinal).ToArray() ?? [];
    var aliasProposals = productEntries.Where(e => !string.IsNullOrWhiteSpace(e.providerProductId) || !string.IsNullOrWhiteSpace(e.providerProductCode) || !string.IsNullOrWhiteSpace(e.productCode))
        .GroupBy(e => string.Join('|', e.companyId?.ToString() ?? "external:" + e.externalCompanyId, e.provider, e.providerProductId ?? e.providerProductCode ?? e.productCode), StringComparer.Ordinal)
        .Select(g => new { members = g.ToArray(), labels = g.Select(e => e.sourceLabel).Where(v => !string.IsNullOrWhiteSpace(v)).Distinct(StringComparer.Ordinal).OrderBy(v => v, StringComparer.Ordinal).ToArray() })
        .Where(g => g.labels.Length > 1)
        .Select(g => new { id = "aliasprop_" + ProgramHelpers.StableId("product-alias", string.Join('|', g.members[0].companyId?.ToString() ?? "external:" + g.members[0].externalCompanyId, g.members[0].provider, g.members[0].providerProductId ?? g.members[0].providerProductCode ?? g.members[0].productCode, string.Join('|', g.labels)))[4..], companyId = g.members[0].companyId, externalCompanyId = g.members[0].externalCompanyId, provider = g.members[0].provider, providerProductId = g.members[0].providerProductId, providerProductCode = g.members[0].providerProductCode, observedLabels = g.labels, productEntryIds = g.members.Select(e => e.id).Distinct().OrderBy(v => v, StringComparer.Ordinal).ToArray(), status = "pending-review", executable = false, evidenceRule = "same company and source product key with multiple observed labels; continuity is not asserted" }).OrderBy(p => p.id, StringComparer.Ordinal).ToArray();
    var categoryProposals = productEntries.Where(e => !string.IsNullOrWhiteSpace(e.sourceLabel) && e.sourceLabel!.Trim().Length > 2)
        .GroupBy(e => string.Join('|', e.sourceLabel!.Trim().ToLowerInvariant(), e.unit ?? ""), StringComparer.Ordinal)
        .Select(g => new { members = g.ToArray(), companies = g.Select(e => e.companyId?.ToString() ?? "external:" + e.externalCompanyId).Distinct(StringComparer.Ordinal).ToArray() })
        .Where(g => g.companies.Length > 1)
        .Select(g => new { id = "catprop_" + ProgramHelpers.StableId("product-category", g.members[0].sourceLabel!.Trim() + "|" + g.members[0].unit)[4..], candidateLabel = g.members[0].sourceLabel, sourceUnit = g.members[0].unit, companyScopedProductEntryIds = g.members.Select(e => e.id).Distinct().OrderBy(v => v, StringComparer.Ordinal).ToArray(), companyCount = g.companies.Length, status = "pending-review", executable = false, evidenceRule = "exact source-label and unit repetition across companies; product equivalence is not asserted" }).OrderBy(p => p.id, StringComparer.Ordinal).ToArray();
    ProgramHelpers.ValidateUniqueIds(ids.Concat(relationshipIds).Concat(metricIds).Concat(dimensionProposals.Select(d => d.id)).Concat(productEntries.Select(e => e.id)));
    ProgramHelpers.ValidateUniqueIds(aliasProposals.Select(p => p.id).Concat(categoryProposals.Select(p => p.id)));
    var dictionary = new { formatVersion = 1, entries = productEntries, aliasProposals, crossCompanyCategoryProposals = categoryProposals, discovery = new { status = profile is null ? "unavailable" : "bounded-observation", verified = profile is not null, scope = "company-scoped product value identities", sampledLineItems = profile?.Items.Count ?? 0 }, approvalRequired = true };
    await File.WriteAllTextAsync(Path.Combine(catalogRoot, "dictionaries", "product-values.yaml"), JsonSerializer.Serialize(dictionary, options) + "\n", new UTF8Encoding(false));
    var physicalSchemaHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(physical, options)))).ToLowerInvariant();
    var manifest = new { formatVersion = 1, liveVerified = db is not null, physicalSchemaHash, physicalObjectCount = tables.Count, relationshipCount = relationshipIds.Count, financialMetricDefinitionCount = metricIds.Count, dimensionProposalCount = dimensionProposals.Length, dictionaryEntryCount = productEntries.Length, aliasProposalCount = aliasProposals.Length, categoryProposalCount = categoryProposals.Length, reconciliationDifferenceCount = reconciliation.Differences.Count, semanticApprovalRequired = true, runtimeQueryingEnabled = false, source = db is null ? "ef-core" : "postgresql" };
    var sourcesDirectory = Path.Combine(catalogRoot, "sources");
    var sourceIds = Directory.Exists(sourcesDirectory)
        ? Directory.GetFiles(sourcesDirectory, "*.yaml")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => name is not null)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray()
        : [];
    var catalog = new { formatVersion = 1, manifest = "manifest.yaml", navigation = "navigation.yaml", tables = ids, relationships = relationshipIds, metrics = metricIds, dimensions = dimensionProposals.Select(d => d.id).ToArray(), dictionaries = new[] { "product-values" }, sources = sourceIds, scope = "Only explicitly reviewed semantic entries may become queryable." };
    await File.WriteAllTextAsync(Path.Combine(catalogRoot, "manifest.yaml"), JsonSerializer.Serialize(manifest, options) + "\n", new UTF8Encoding(false));
    await File.WriteAllTextAsync(Path.Combine(catalogRoot, "catalog.yaml"), JsonSerializer.Serialize(catalog, options) + "\n", new UTF8Encoding(false));
}

public sealed class ProgramHelpers
{
    public static string StableId(string schema, string name) => "tbl_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(schema + "." + name)).AsSpan(0, 8)).ToLowerInvariant();
    public static void ValidateUniqueIds(IEnumerable<string> ids)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
            if (!seen.Add(id)) throw new InvalidDataException("Duplicate catalog ID: " + id);
    }
    public static bool IsRuntimeQueryable(string status, bool queryable) => queryable && status.Equals("approved", StringComparison.OrdinalIgnoreCase);
    public static JsonElement PreserveSemantic(JsonElement existing, bool physicalChanged)
    {
        var node = JsonNode.Parse(existing.GetRawText()) as JsonObject ?? throw new InvalidDataException("Existing semantic metadata must be a YAML/JSON object.");
        var status = node["status"]?.GetValue<string>();
        if (physicalChanged && (IsRuntimeQueryable(status ?? "", node["queryable"]?.GetValue<bool>() == true) || status?.Equals("reviewed", StringComparison.OrdinalIgnoreCase) == true))
        {
            node["status"] = "stale-review-required";
            node["queryable"] = false;
        }
        return JsonSerializer.SerializeToElement(node);
    }
    public static bool HasPhysicalReference(string table, IEnumerable<string> columns, IReadOnlyDictionary<string, HashSet<string>> objects) =>
        objects.TryGetValue(table, out var knownColumns) && columns.All(knownColumns.Contains);
    public static string RedactDiagnostic(string message) => System.Text.RegularExpressions.Regex.Replace(
        message, "(?i)(password|username|user id|host|server|database)\\s*=\\s*[^;\\s]+", "$1=[REDACTED]");
    public static void RunSelfTests()
    {
        if (StableId("public", "Companies") != StableId("public", "Companies")) throw new InvalidOperationException("Stable ID check failed.");
        var duplicateRejected = false;
        try { ValidateUniqueIds(["same", "same"]); } catch (InvalidDataException) { duplicateRejected = true; }
        if (!duplicateRejected) throw new InvalidOperationException("Duplicate ID check failed.");
        if (IsRuntimeQueryable("unapproved", true) || IsRuntimeQueryable("approved", false)) throw new InvalidOperationException("Unapproved metadata exclusion check failed.");
        if (HasPhysicalReference("public.Companies", ["MissingColumn"], new Dictionary<string, HashSet<string>> { ["public.Companies"] = ["Id", "Name"] })) throw new InvalidOperationException("Invalid physical reference check failed.");
        using var semanticJson = JsonDocument.Parse("{\"status\":\"approved\",\"queryable\":true,\"description\":\"reviewed definition\"}");
        var preserved = PreserveSemantic(semanticJson.RootElement, physicalChanged: false);
        if (preserved.GetProperty("description").GetString() != "reviewed definition") throw new InvalidOperationException("Reviewed metadata preservation check failed.");
        var stale = PreserveSemantic(semanticJson.RootElement, physicalChanged: true);
        if (stale.GetProperty("status").GetString() != "stale-review-required" || stale.GetProperty("queryable").GetBoolean()) throw new InvalidOperationException("Stale approval check failed.");
        var safe = RedactDiagnostic("connection failed Host=db.local;Username=alice;Password=hunter2");
        if (safe.Contains("hunter2", StringComparison.Ordinal) || safe.Contains("alice", StringComparison.Ordinal)) throw new InvalidOperationException("Secret redaction check failed.");
        var json = JsonSerializer.Serialize(new { z = 1, a = "stable" });
        if (json != JsonSerializer.Serialize(new { z = 1, a = "stable" })) throw new InvalidOperationException("Deterministic JSON serialization check failed.");
        Console.WriteLine("Self-test passed: stable IDs, duplicate IDs, reviewed-metadata preservation and staleness, invalid physical references, unapproved exclusion, redaction, deterministic serialization.");
    }
    public static DbContext CreateGeneric<T>() where T : DbContext
    {
        var builder = new DbContextOptionsBuilder<T>();
        builder.UseNpgsql("Host=localhost;Database=semantic_catalog_model_only;Username=offline;Password=offline;Timeout=1");
        return (DbContext)Activator.CreateInstance(typeof(T), builder.Options)!;
    }
}

record DiscoverySnapshot(int FormatVersion, DateTimeOffset GeneratedAtUtc, bool LiveVerified, List<EfObject> EfObjects, List<PgObject>? PostgreSqlObjects, string? LiveDiscoveryNote);
record EfObject(string Context, string Schema, string Name, List<EfColumn> Columns, string[] PrimaryKey, List<string[]> AlternateKeys, List<EfForeignKey> ForeignKeys, List<EfIndex> Indexes);
record EfColumn(string Name, string? StoreType, bool Nullable, bool PrimaryKey, string? DefaultValue);
record EfForeignKey(string[] Columns, string PrincipalSchema, string PrincipalTable, string[] PrincipalColumns, bool Unique, bool Required);
record EfIndex(string[] Columns, bool Unique);
record PgObject(string Schema, string Name, string ObjectType, List<PgColumn> Columns, List<string> PrimaryKeys, List<PgForeignKey> ForeignKeys, List<PgIndex> Indexes, List<PgConstraint> Constraints, List<string> MigrationHistory)
{
    public PgObject(string schema, string name, string type, List<PgColumn> columns, List<string> pks, List<PgForeignKey> fks, List<PgIndex> indexes) : this(schema, name, type, columns, pks, fks, indexes, [], []) { }
}
record PgColumn(string Name, string Type, bool Nullable, string? DefaultValue);
record PgForeignKey(string[] Columns, string PrincipalSchema, string PrincipalTable, string[] PrincipalColumns, string Definition);
record PgIndex(string Name, bool Unique, string Definition, string[] Columns);
record PgConstraint(string Name, string Type, string[] Columns, string Definition);
record CatalogPhysical(string Schema, string Name, string ObjectType, List<PgColumn> Columns, List<string> PrimaryKeys, List<PgForeignKey> ForeignKeys, List<PgIndex> Indexes, List<PgConstraint> Constraints);
record Difference(string Code, string Object, string Severity);
record Reconciliation(bool LiveVerified, List<Difference> Differences);
record ProfileLineItem(int? CompanyId, string? CompanyName, string? CompanyTicker, string? ExternalCompanyId, string? ExternalReportId, DateOnly? PeriodStart, DateOnly? PeriodEnd, string ProviderName, string ReportType, int OutputType, bool IsAccepted, string? ProviderProductId, string? ProviderProductCode, string? ProductCode, string? Title, string? Unit, decimal? ProductionQuantity, decimal? SalesQuantity, decimal? SalesAmount, decimal? SalesRate);
record ReportProfile(string[] Values, int Count);
record NumericCoverage(int Observations, int NonNull, string? Minimum, string? Maximum);
record ProfileResult(List<Dictionary<string, object?>> Companies, List<Dictionary<string, object?>> Industries, List<ProfileLineItem> Items, string SourceFilter, int MaxReports, int MaxLineItems, List<ReportProfile> RecentReportProfiles);
