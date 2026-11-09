using System.Net;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.CyclicalWavesV2;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Domain.Financial.CyclicalWavesV2;

namespace FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWavesV2;

/// <summary>Gauge and equilibrium v2 routes, hosted separately from the original data API.</summary>
public sealed class CyclicalWavesV2GaugeDataClient(HttpClient httpClient) : ICyclicalWavesV2GaugeClient
{
    public async Task<IReadOnlyList<CyclicalWavesV2GaugeCatalogItem>> GetGaugeCatalogAsync(CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync("pe-ps-gauges/tickers", false, cancellationToken);
        if (document!.RootElement.ValueKind != JsonValueKind.Array) throw Invalid("gauge catalog is not an array");
        return document.RootElement.EnumerateArray().Select(item => new CyclicalWavesV2GaugeCatalogItem(
            RequiredString(item, "ticker"), ReadString(item, "enticker"), RequiredBool(item, "pe"), RequiredBool(item, "ps"))).ToArray();
    }

    public async Task<CyclicalWavesV2GaugeDetails?> GetGaugeDetailsAsync(string identifier, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync("pe-ps-gauges/ticker/" + Uri.EscapeDataString(identifier), true, cancellationToken);
        if (document is null) return null;
        var root = document.RootElement;
        return new CyclicalWavesV2GaugeDetails(RequiredString(root, "ticker"), ReadString(root, "enticker"),
            ReadGauge(root, "pe"), ReadGauge(root, "ps"));
    }

    public async Task<IReadOnlyList<CyclicalWavesV2EquilibriumCatalogItem>> GetEquilibriumCatalogAsync(CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync("equilibrium-ranges/tickers", false, cancellationToken);
        if (document!.RootElement.ValueKind != JsonValueKind.Array) throw Invalid("equilibrium catalog is not an array");
        return document.RootElement.EnumerateArray().Select(item => new CyclicalWavesV2EquilibriumCatalogItem(
            RequiredString(item, "ticker"), ReadString(item, "enticker"))).ToArray();
    }

    public async Task<CyclicalWavesV2EquilibriumRange?> GetEquilibriumRangeAsync(string identifier, CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync("equilibrium-ranges/ticker/" + Uri.EscapeDataString(identifier), true, cancellationToken);
        return document is null ? null : new CyclicalWavesV2EquilibriumRange(
            RequiredDecimal(document.RootElement, "min"), RequiredDecimal(document.RootElement, "avg"), RequiredDecimal(document.RootElement, "max"));
    }

    private async Task<JsonDocument?> GetJsonAsync(string path, bool notFoundIsNull, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(path, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound && notFoundIsNull) return null;
        if (!response.IsSuccessStatusCode)
        {
            throw new FinancialProviderException(response.StatusCode switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => FinancialProviderErrorCode.Unauthorized,
                >= HttpStatusCode.InternalServerError or HttpStatusCode.RequestTimeout => FinancialProviderErrorCode.RemoteUnavailable,
                _ => FinancialProviderErrorCode.InvalidResponse
            }, $"CyclicalWaves v2 gauge API answered HTTP {(int)response.StatusCode}.");
        }

        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new FinancialProviderException(FinancialProviderErrorCode.InvalidResponse,
                "CyclicalWaves v2 gauge response was not valid JSON.", exception);
        }
    }

    private static CyclicalWavesV2Gauge? ReadGauge(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var gauge) || gauge.ValueKind == JsonValueKind.Null) return null;
        if (gauge.ValueKind != JsonValueKind.Object) throw Invalid($"{name} gauge is not an object or null");
        var a = RequiredInt(gauge, "a");
        var b = RequiredInt(gauge, "b");
        var c = RequiredInt(gauge, "c");
        var d = RequiredInt(gauge, "d");
        var e = RequiredInt(gauge, "e");
        var f = RequiredInt(gauge, "f");
        var status = OptionalInt(gauge, "status");
        if (a < 0 || b < 0 || c < 0 || d < 0 || e < 0 || f < 0)
            throw Invalid("a through f must be non-negative day counts");
        if (status is < 1 or > 4)
            throw Invalid("status must be between 1 and 4 or null");
        return new CyclicalWavesV2Gauge(a, b, c, d, e, f, OptionalDecimal(gauge, "close"),
            RequiredDecimal(gauge, "start"), RequiredDecimal(gauge, "end"), RequiredDecimal(gauge, "min"),
            RequiredDecimal(gauge, "avg"), RequiredDecimal(gauge, "max"), status);
    }

    private static ExactDecimal RequiredDecimal(JsonElement root, string name) => OptionalDecimal(root, name)
        ?? throw Invalid($"{name} is missing or null");

    private static ExactDecimal? OptionalDecimal(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Number || !ExactDecimal.TryParse(value.GetRawText(), out var number))
            throw Invalid($"{name} is not an exact finite decimal");
        return number;
    }

    private static int RequiredInt(JsonElement root, string name) => OptionalInt(root, name)
        ?? throw Invalid($"{name} is missing or null");

    private static int? OptionalInt(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number)) throw Invalid($"{name} is not an integer");
        return number;
    }

    private static bool RequiredBool(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean() : throw Invalid($"{name} is not a boolean");

    private static string RequiredString(JsonElement root, string name) => ReadString(root, name) is { Length: > 0 } value
        ? value.Trim() : throw Invalid($"{name} is missing");

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : null;

    private static FinancialProviderException Invalid(string reason) =>
        new(FinancialProviderErrorCode.InvalidResponse, "CyclicalWaves v2 gauge payload is invalid: " + reason + ".");
}
