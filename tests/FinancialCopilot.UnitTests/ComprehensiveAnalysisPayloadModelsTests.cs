using System.Text.Json;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;

namespace FinancialCopilot.UnitTests;

public sealed class ComprehensiveAnalysisPayloadModelsTests
{
    [Fact]
    public void Deserializes_current_single_category_shape()
    {
        const string json = """
            {
              "data": [{
                "id": 48978,
                "title": "title",
                "summary": "summary",
                "user_id": 1,
                "created_at": "2026-08-22T00:00:00Z",
                "pcreate": "1405/05/31",
                "categories": { "id": 1, "name": "author" },
                "tags": []
              }],
              "meta": { "current_page": 1, "last_page": 1, "total": 1, "per_page": 10 }
            }
            """;

        var result = JsonSerializer.Deserialize<ComprehensiveAnalysisPagedResponse>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(result);
        Assert.Single(result!.Data);
        Assert.Single(result.Data[0].Categories);
        Assert.Equal("author", result.Data[0].Categories[0].Name);
    }

    [Fact]
    public void Deserializes_legacy_category_array_shape()
    {
        const string json = """
            {
              "data": [{
                "id": 1,
                "title": "title",
                "summary": "summary",
                "user_id": 1,
                "created_at": "2026-08-22T00:00:00Z",
                "pcreate": "1405/05/31",
                "categories": [{ "id": 1, "name": "author" }],
                "tags": []
              }],
              "meta": { "current_page": 1, "last_page": 1, "total": 1, "per_page": 10 }
            }
            """;

        var result = JsonSerializer.Deserialize<ComprehensiveAnalysisPagedResponse>(
            json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(result);
        Assert.Single(result!.Data[0].Categories);
    }
}
