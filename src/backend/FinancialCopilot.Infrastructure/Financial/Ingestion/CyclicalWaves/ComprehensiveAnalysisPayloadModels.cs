using System.Text.Json;
using System.Text.Json.Serialization;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;

internal sealed record ComprehensiveAnalysisPagedResponse(
    [property: JsonPropertyName("data")] List<ComprehensiveAnalysisItemDto> Data,
    [property: JsonPropertyName("meta")] ComprehensiveAnalysisPageMeta Meta);

internal sealed record ComprehensiveAnalysisItemDto(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("user_id")] int UserId,
    [property: JsonPropertyName("created_at")] string CreatedAt,
    [property: JsonPropertyName("pcreate")] string Pcreate,
    [property: JsonPropertyName("categories")]
    [property: JsonConverter(typeof(SingleOrArrayJsonConverter<ComprehensiveAnalysisCategoryDto>))]
    List<ComprehensiveAnalysisCategoryDto> Categories,
    [property: JsonPropertyName("tags")] List<ComprehensiveAnalysisTagDto> Tags);

internal sealed record ComprehensiveAnalysisCategoryDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name);

internal sealed record ComprehensiveAnalysisTagDto(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("slug")] string Slug,
    [property: JsonPropertyName("type_id")] int TypeId,
    [property: JsonPropertyName("analytic")] int Analytic);

internal sealed record ComprehensiveAnalysisPageMeta(
    [property: JsonPropertyName("current_page")] int CurrentPage,
    [property: JsonPropertyName("last_page")] int LastPage,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("per_page")] int PerPage);

/// <summary>
/// The upstream blog API historically returned categories as an array, but currently returns
/// the single category object directly. Accept both shapes so a provider representation change
/// does not abort the entire synchronization at page one.
/// </summary>
internal sealed class SingleOrArrayJsonConverter<T> : JsonConverter<List<T>>
{
    public override List<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.StartArray => JsonSerializer.Deserialize<List<T>>(ref reader, options) ?? [],
            JsonTokenType.StartObject => [JsonSerializer.Deserialize<T>(ref reader, options)!],
            JsonTokenType.Null => [],
            _ => throw new JsonException($"Expected an object or array for {typeToConvert.Name}.")
        };
    }

    public override void Write(Utf8JsonWriter writer, List<T> value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, options);
}
