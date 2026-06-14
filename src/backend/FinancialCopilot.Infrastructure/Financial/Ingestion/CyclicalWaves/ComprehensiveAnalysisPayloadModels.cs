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
    [property: JsonPropertyName("categories")] List<ComprehensiveAnalysisCategoryDto> Categories,
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
