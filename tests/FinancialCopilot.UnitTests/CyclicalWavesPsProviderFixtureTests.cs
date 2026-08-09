using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FinancialCopilot.UnitTests;

public sealed class CyclicalWavesPsProviderFixtureTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip
    };

    [Fact]
    public void Circle_chart_fixture_preserves_the_gauge_contract_and_ignores_additive_fields()
    {
        var json = ReadFixture("circle-chart-data.json");
        var response = JsonSerializer.Deserialize<CircleChartDataFixture>(json, JsonOptions);

        Assert.NotNull(response);
        Assert.Equal(369L, response.A);
        Assert.Equal(1963L, response.B);
        Assert.Equal(645L, response.C);
        Assert.Equal(862L, response.D);
        Assert.Equal(397L, response.E);
        Assert.Equal(540L, response.F);
        Assert.Equal(0.5816000300100752m, response.Close);
        Assert.Equal(0.12652417m, response.Start);
        Assert.Equal(3.28752923m, response.End);
        Assert.Equal(0.26810213403234034m, response.Min);
        Assert.Equal(1.1549267525274725m, response.Max);
        Assert.Equal(0.5595874176997108m, response.Average);

        var trimmedJson = json.TrimEnd();
        var withAdditiveField = trimmedJson[..^1] + ",\n  \"future_provider_field\": \"ignored\"\n}";
        var additiveResponse = JsonSerializer.Deserialize<CircleChartDataFixture>(withAdditiveField, JsonOptions);
        Assert.Equal(response, additiveResponse);
    }

    [Fact]
    public void Ps_data_fixture_preserves_current_values_and_explicit_zero()
    {
        var response = JsonSerializer.Deserialize<PsDataEnvelopeFixture>(ReadFixture("ps-data.json"), JsonOptions);

        Assert.NotNull(response);
        Assert.Equal("غگلپا", response.Data.Symbol);
        Assert.Equal("IRO3PGPZ0001", response.Data.Ticker);
        Assert.Equal(0.4088225224044161m, response.Data.PsRatio);
        Assert.Equal(0m, response.Data.Close);
        Assert.Equal(new DateOnly(2026, 7, 29), response.Data.Date);
    }

    [Fact]
    public void History_sample_preserves_same_date_provider_points()
    {
        var response = JsonSerializer.Deserialize<PsHistoryFixture>(ReadFixture("ps-history.sample.json"), JsonOptions);

        Assert.NotNull(response);
        Assert.Equal(6, response.DataCount);
        Assert.Equal(6, response.Data.Count);
        Assert.Equal(new DateOnly(2021, 3, 27), response.FirstDate);
        Assert.Equal(new DateOnly(2026, 7, 29), response.LastDate);
        Assert.Equal(6, response.Data.Select(point => point.Id).Distinct(StringComparer.Ordinal).Count());

        var duplicateDates = response.Data
            .GroupBy(point => point.Date)
            .Where(group => group.Count() > 1)
            .ToArray();

        Assert.Equal(2, duplicateDates.Length);
        Assert.Contains(duplicateDates, group =>
            group.Key == new DateOnly(2021, 10, 23) &&
            group.Select(point => point.Ps).Order().SequenceEqual([1.374m, 1.4829m]));
        Assert.Contains(duplicateDates, group =>
            group.Key == new DateOnly(2023, 5, 15) &&
            group.Select(point => point.Ps).Order().SequenceEqual([1.1529m, 1.2753m]));
    }

    [Fact]
    public void Manifest_freezes_full_history_capture_facts_and_fixture_hashes()
    {
        var manifest = JsonSerializer.Deserialize<FixtureManifest>(ReadFixture("fixture-manifest.json"), JsonOptions);

        Assert.NotNull(manifest);
        Assert.Equal("CyclicalWaves", manifest.Provider);
        Assert.Equal(3, manifest.Fixtures.Count);
        foreach (var fixture in manifest.Fixtures)
        {
            Assert.Equal(fixture.Sha256, CalculateSha256(ReadFixture(fixture.File)));
        }

        Assert.Equal(1124, manifest.FullHistoryCapture.PointCount);
        Assert.Equal(1124, manifest.FullHistoryCapture.UniqueProviderPointIdCount);
        Assert.Equal(1116, manifest.FullHistoryCapture.UniqueObservationDateCount);
        Assert.Equal(8, manifest.FullHistoryCapture.DuplicateObservationDateGroupCount);
        Assert.Equal(new DateOnly(2021, 3, 27), manifest.FullHistoryCapture.FirstObservationDate);
        Assert.Equal(new DateOnly(2026, 7, 29), manifest.FullHistoryCapture.LastObservationDate);
        Assert.Equal("e5b0e0a72d0bd10f91762c75bfc2e08f38fd8c2c5f9458bc9e3c0df6de3503ad",
            manifest.FullHistoryCapture.Sha256);
    }

    [Fact]
    public void Fixtures_are_free_of_credentials_and_browser_request_headers()
    {
        foreach (var file in Directory.EnumerateFiles(FixtureDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            var contents = File.ReadAllText(file);
            Assert.DoesNotContain("Bearer ", contents, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"authorization\"", contents, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("sec-ch-", contents, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"origin\"", contents, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"referer\"", contents, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\"user-agent\"", contents, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("eyJ", contents, StringComparison.Ordinal);
        }
    }

    private static string FixtureDirectory => Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        "CyclicalWaves",
        "Ps");

    private static string ReadFixture(string fileName) =>
        File.ReadAllText(Path.Combine(FixtureDirectory, fileName));

    private static string CalculateSha256(string value) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed record CircleChartDataFixture(
        [property: JsonPropertyName("a")] long A,
        [property: JsonPropertyName("b")] long B,
        [property: JsonPropertyName("c")] long C,
        [property: JsonPropertyName("d")] long D,
        [property: JsonPropertyName("e")] long E,
        [property: JsonPropertyName("f")] long F,
        [property: JsonPropertyName("close")] decimal Close,
        [property: JsonPropertyName("start")] decimal Start,
        [property: JsonPropertyName("end")] decimal End,
        [property: JsonPropertyName("min")] decimal Min,
        [property: JsonPropertyName("max")] decimal Max,
        [property: JsonPropertyName("avg")] decimal Average);

    private sealed record PsDataEnvelopeFixture(
        [property: JsonPropertyName("data")] PsDataFixture Data);

    private sealed record PsDataFixture(
        [property: JsonPropertyName("symbol")] string Symbol,
        [property: JsonPropertyName("ticker")] string Ticker,
        [property: JsonPropertyName("ps_ratio")] decimal PsRatio,
        [property: JsonPropertyName("close")] decimal Close,
        [property: JsonPropertyName("date")] DateOnly Date);

    private sealed record PsHistoryFixture(
        [property: JsonPropertyName("data")] IReadOnlyList<PsHistoryPointFixture> Data,
        [property: JsonPropertyName("first_date")] DateOnly FirstDate,
        [property: JsonPropertyName("last_date")] DateOnly LastDate,
        [property: JsonPropertyName("data_count")] int DataCount);

    private sealed record PsHistoryPointFixture(
        [property: JsonPropertyName("_id")] string Id,
        [property: JsonPropertyName("date")] DateOnly Date,
        [property: JsonPropertyName("ps")] decimal Ps);

    private sealed record FixtureManifest(
        [property: JsonPropertyName("provider")] string Provider,
        [property: JsonPropertyName("fixtures")] IReadOnlyList<FixtureHash> Fixtures,
        [property: JsonPropertyName("fullHistoryCapture")] FullHistoryCapture FullHistoryCapture);

    private sealed record FixtureHash(
        [property: JsonPropertyName("file")] string File,
        [property: JsonPropertyName("endpointTemplate")] string EndpointTemplate,
        [property: JsonPropertyName("sha256")] string Sha256);

    private sealed record FullHistoryCapture(
        [property: JsonPropertyName("sha256")] string Sha256,
        [property: JsonPropertyName("pointCount")] int PointCount,
        [property: JsonPropertyName("uniqueProviderPointIdCount")] int UniqueProviderPointIdCount,
        [property: JsonPropertyName("uniqueObservationDateCount")] int UniqueObservationDateCount,
        [property: JsonPropertyName("duplicateObservationDateGroupCount")] int DuplicateObservationDateGroupCount,
        [property: JsonPropertyName("firstObservationDate")] DateOnly FirstObservationDate,
        [property: JsonPropertyName("lastObservationDate")] DateOnly LastObservationDate);
}
