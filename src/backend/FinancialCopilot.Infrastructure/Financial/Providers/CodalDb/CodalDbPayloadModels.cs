using System.Text.Json.Serialization;

namespace FinancialCopilot.Infrastructure.Financial.Providers.CodalDb;

/// <summary>
/// Provider-local projection of one CodalDB <c>Companies</c> row, as serialized into the JSON
/// <c>ProviderRawPayload</c> for the <c>Symbols</c> dataset. This is the contract the read-only
/// SQL gateway (spec 021) produces and the <c>CodalDbSymbolNormalizer</c> consumes; it is kept
/// inside the provider/normalization layers so CodalDB specifics never leak into Application code.
/// Property names mirror the source columns exactly so the serialized payload is unambiguous.
/// </summary>
public sealed record CodalDbCompanyRecord(
    [property: JsonPropertyName("CoID")] int CoID,
    [property: JsonPropertyName("CoName")] string CoName,
    [property: JsonPropertyName("CoNameEnglish")] string? CoNameEnglish,
    [property: JsonPropertyName("CompanySymbol")] string? CompanySymbol,
    [property: JsonPropertyName("CoTSESymbol")] string? CoTSESymbol,
    [property: JsonPropertyName("GroupID")] int? GroupID,
    [property: JsonPropertyName("GroupName")] string? GroupName,
    [property: JsonPropertyName("IndustryID")] int? IndustryID,
    [property: JsonPropertyName("IndustryName")] string? IndustryName,
    [property: JsonPropertyName("InstCode")] string? InstCode,
    [property: JsonPropertyName("TseCIsinCode")] string? TseCIsinCode,
    [property: JsonPropertyName("TseSIsinCode")] string? TseSIsinCode,
    [property: JsonPropertyName("MarketID")] int? MarketID,
    [property: JsonPropertyName("MarketName")] string? MarketName,
    // NON-IDENTIFYING: constant GUID across all rows. Retained for provenance only.
    [property: JsonPropertyName("InstrumentRef")] string? InstrumentRef,
    [property: JsonPropertyName("ModifiedDateTime")] DateTimeOffset? ModifiedDateTime);
