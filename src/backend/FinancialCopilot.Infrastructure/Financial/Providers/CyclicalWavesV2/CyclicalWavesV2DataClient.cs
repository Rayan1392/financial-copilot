using System.Globalization;
using System.Net;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.CyclicalWavesV2;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Domain.Financial.CyclicalWavesV2;

namespace FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWavesV2;

/// <summary>
/// The five documented CyclicalWaves v2 data endpoints (contracts: tahlilapp-api-client.pdf and
/// income-statement-api-client.pdf). Unknown fields are ignored and <c>null</c> stays <c>null</c>.
/// Only Application records leave this class.
/// </summary>
public sealed class CyclicalWavesV2DataClient(HttpClient httpClient) : ICyclicalWavesV2Client
{
    public async Task<IReadOnlyList<string>> GetTickersAsync(CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync("custom-filtering/tickers", notFoundIsNull: false, cancellationToken);
        return CyclicalWavesV2PayloadParser.ParseTickers(document!.RootElement);
    }

    public async Task<CyclicalWavesV2ScreenerSnapshot?> GetScreenerAsync(string ticker, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync(
            "custom-filtering/ticker/" + Uri.EscapeDataString(ticker),
            notFoundIsNull: true,
            cancellationToken);
        return document is null ? null : CyclicalWavesV2PayloadParser.ParseScreener(document.RootElement);
    }

    public async Task<CyclicalWavesV2IncomeStatementSet?> GetIncomeStatementsAsync(
        string ticker,
        int limit,
        CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync(
            $"income-statements/ticker/{Uri.EscapeDataString(ticker)}?limit={limit.ToString(CultureInfo.InvariantCulture)}",
            notFoundIsNull: true,
            cancellationToken);
        return document is null ? null : CyclicalWavesV2PayloadParser.ParseIncomeStatements(document.RootElement);
    }

    public async Task<IReadOnlyList<CyclicalWavesV2LineDefinition>> GetIncomeStatementLinesAsync(
        CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync("income-statements/lines", notFoundIsNull: false, cancellationToken);
        return CyclicalWavesV2PayloadParser.ParseLineDefinitions(document!.RootElement);
    }

    public async Task<CyclicalWavesV2AnalysisPage> GetAnalysesPageAsync(
        int page,
        int pageSize,
        DateOnly? fromDate,
        CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync(
            BuildAnalysesPath(page, pageSize, fromDate),
            notFoundIsNull: false,
            cancellationToken);
        return CyclicalWavesV2PayloadParser.ParseAnalysesPage(document!.RootElement);
    }

    /// <summary><c>filter[type]=analysis</c> is always present; bracket keys are percent-encoded.</summary>
    internal static string BuildAnalysesPath(int page, int pageSize, DateOnly? fromDate)
    {
        var path = "blogs?filter%5Btype%5D=analysis" +
                   $"&page={page.ToString(CultureInfo.InvariantCulture)}" +
                   $"&paginate={pageSize.ToString(CultureInfo.InvariantCulture)}";
        return fromDate is null
            ? path
            : path + "&filter%5Bfrom_date%5D=" + fromDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private async Task<JsonDocument?> GetJsonAsync(string path, bool notFoundIsNull, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(path, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound && notFoundIsNull)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new FinancialProviderException(
                response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => FinancialProviderErrorCode.Unauthorized,
                    >= HttpStatusCode.InternalServerError or HttpStatusCode.RequestTimeout => FinancialProviderErrorCode.RemoteUnavailable,
                    _ => FinancialProviderErrorCode.InvalidResponse
                },
                $"CyclicalWaves v2 answered HTTP {(int)response.StatusCode}.");
        }

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new FinancialProviderException(
                FinancialProviderErrorCode.InvalidResponse,
                "CyclicalWaves v2 response was not valid JSON.",
                exception);
        }
    }
}

/// <summary>Maps v2 JSON payloads to Application records. Throws <see cref="FinancialProviderException"/> on shape violations.</summary>
public static class CyclicalWavesV2PayloadParser
{
    private static readonly TimeSpan TehranOffset = TimeSpan.FromHours(3.5);

    public static IReadOnlyList<string> ParseTickers(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array)
        {
            throw Invalid("ticker catalog is not an array");
        }

        return root.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()!.Trim())
            .Where(ticker => ticker.Length > 0)
            .ToList();
    }

    public static CyclicalWavesV2ScreenerSnapshot ParseScreener(JsonElement root)
    {
        RequireObject(root, "screener");
        var values = new Dictionary<string, decimal?>(StringComparer.Ordinal);
        foreach (var definition in ScreenerMetricCatalog.All)
        {
            values[definition.FieldKey] = ReadDecimal(root, definition.FieldKey);
        }

        return new CyclicalWavesV2ScreenerSnapshot(
            ReadString(root, "ticker") ?? throw Invalid("screener has no ticker"),
            ReadString(root, "enticker"),
            values,
            ReadCompactDate(root, "lastMonthSaleDate"),
            ReadCompactDate(root, "lastQuarterDate"),
            ReadInstant(root, "updatedAt"));
    }

    public static CyclicalWavesV2IncomeStatementSet ParseIncomeStatements(JsonElement root)
    {
        RequireObject(root, "income statements");
        var statements = new List<CyclicalWavesV2IncomeStatement>();
        if (root.TryGetProperty("statements", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                var period = ReadCompactDate(item, "periodEndingDate") ?? throw Invalid("statement has no periodEndingDate");
                var lines = new List<CyclicalWavesV2StatementLine>();
                if (item.TryGetProperty("lines", out var lineItems) && lineItems.ValueKind == JsonValueKind.Array)
                {
                    foreach (var line in lineItems.EnumerateArray())
                    {
                        if (!line.TryGetProperty("key", out var key) || !key.TryGetInt32(out var lineKey))
                        {
                            throw Invalid("statement line has no integer key");
                        }

                        lines.Add(new CyclicalWavesV2StatementLine(
                            lineKey,
                            ReadString(line, "name") ?? string.Empty,
                            ReadString(line, "nameFa") ?? string.Empty,
                            ReadDecimal(line, "quarter"),
                            ReadDecimal(line, "yearToDate"),
                            ReadDecimal(line, "trailingYear")));
                    }
                }

                statements.Add(new CyclicalWavesV2IncomeStatement(
                    period,
                    ReadInt(item, "fiscalYear") ?? throw Invalid("statement has no fiscalYear"),
                    ReadInt(item, "fiscalMonth") ?? throw Invalid("statement has no fiscalMonth"),
                    ReadBool(item, "audited"),
                    ReadLocalTehranInstant(item, "publishDate"),
                    ReadString(item, "codalUrl"),
                    lines));
            }
        }

        return new CyclicalWavesV2IncomeStatementSet(
            ReadString(root, "ticker") ?? throw Invalid("income statements have no ticker"),
            ReadString(root, "enticker"),
            ReadInt(root, "total") ?? statements.Count,
            statements);
    }

    /// <summary>Live evidence (2026-10-31): a bare array of <c>{key,name,nameFa}</c>; a wrapped array is also accepted.</summary>
    public static IReadOnlyList<CyclicalWavesV2LineDefinition> ParseLineDefinitions(JsonElement root)
    {
        var items = root.ValueKind switch
        {
            JsonValueKind.Array => root,
            JsonValueKind.Object when root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array => data,
            JsonValueKind.Object when root.TryGetProperty("lines", out var lines) && lines.ValueKind == JsonValueKind.Array => lines,
            _ => throw Invalid("line definitions are not an array")
        };

        return items.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Object)
            .Select(item => new CyclicalWavesV2LineDefinition(
                ReadInt(item, "key") ?? throw Invalid("line definition has no key"),
                ReadString(item, "name") ?? string.Empty,
                ReadString(item, "nameFa") ?? string.Empty))
            .ToList();
    }

    public static CyclicalWavesV2AnalysisPage ParseAnalysesPage(JsonElement root)
    {
        RequireObject(root, "analysis page");
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            throw Invalid("analysis page has no data array");
        }

        var articles = new List<CyclicalWavesV2AnalysisArticle>();
        foreach (var item in data.EnumerateArray())
        {
            var id = ReadLong(item, "id") ?? throw Invalid("analysis has no id");
            articles.Add(new CyclicalWavesV2AnalysisArticle(
                id,
                ReadString(item, "title") ?? string.Empty,
                ReadString(item, "summary"),
                ReadInstant(item, "published_at") ?? throw Invalid("analysis has no published_at"),
                ReadArray(item, "categories")
                    .Select(category => new CyclicalWavesV2AnalysisCategory(
                        ReadLong(category, "id") ?? 0,
                        ReadString(category, "title") ?? string.Empty))
                    .ToList(),
                ReadArray(item, "tags")
                    .Select(tag => new CyclicalWavesV2AnalysisTag(ReadLong(tag, "id") ?? 0, ReadString(tag, "name") ?? string.Empty))
                    .Where(tag => tag.Name.Length > 0)
                    .ToList()));
        }

        var meta = root.TryGetProperty("meta", out var metaElement) && metaElement.ValueKind == JsonValueKind.Object
            ? metaElement
            : default;
        var currentPage = meta.ValueKind == JsonValueKind.Object ? ReadInt(meta, "current_page") ?? 1 : 1;
        var lastPage = meta.ValueKind == JsonValueKind.Object ? ReadInt(meta, "last_page") ?? currentPage : currentPage;
        var total = meta.ValueKind == JsonValueKind.Object ? ReadInt(meta, "total") ?? articles.Count : articles.Count;
        return new CyclicalWavesV2AnalysisPage(articles, currentPage, lastPage, total);
    }

    private static IEnumerable<JsonElement> ReadArray(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object)
            : [];

    private static decimal? ReadDecimal(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number;
        }

        throw Invalid($"field {name} is not a number");
    }

    private static int? ReadInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    private static long? ReadLong(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number
            : null;

    private static bool? ReadBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>Dates arrive as <c>"YYYYMMDD"</c> strings in the screener and as <c>YYYYMMDD</c> integers in statements.</summary>
    private static DateOnly? ReadCompactDate(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };

        return DateOnly.TryParseExact(text, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : throw Invalid($"field {name} is not a YYYYMMDD date");
    }

    private static DateTimeOffset? ReadInstant(JsonElement element, string name)
    {
        var text = ReadString(element, name);
        return text is not null &&
               DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var instant)
            ? instant
            : null;
    }

    /// <summary>
    /// <c>publishDate</c> ("2026-07-25 16:54:53") carries no offset. It is the Codal publication time, which is
    /// interpreted as Iran Standard Time (UTC+03:30, no daylight saving since 2022).
    /// </summary>
    private static DateTimeOffset? ReadLocalTehranInstant(JsonElement element, string name)
    {
        var text = ReadString(element, name);
        return text is not null &&
               DateTime.TryParseExact(text, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local)
            ? new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TehranOffset).ToUniversalTime()
            : null;
    }

    private static void RequireObject(JsonElement root, string what)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw Invalid($"{what} is not an object");
        }
    }

    private static FinancialProviderException Invalid(string reason) =>
        new(FinancialProviderErrorCode.InvalidResponse, "CyclicalWaves v2 payload is invalid: " + reason + ".");
}
