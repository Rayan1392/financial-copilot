using System.Text.Json;
using System.Text.Json.Serialization;

namespace FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWaves;

internal sealed record CyclicalWavesPeGaugePayload(
    [property: JsonPropertyName("a")] decimal? A,
    [property: JsonPropertyName("b")] decimal? B,
    [property: JsonPropertyName("c")] decimal? C,
    [property: JsonPropertyName("d")] decimal? D,
    [property: JsonPropertyName("e")] decimal? E,
    [property: JsonPropertyName("f")] decimal? F,
    [property: JsonPropertyName("close")] decimal? Close,
    [property: JsonPropertyName("start")] decimal? Start,
    [property: JsonPropertyName("end")] decimal? End,
    [property: JsonPropertyName("min")] decimal? Min,
    [property: JsonPropertyName("max")] decimal? Max,
    [property: JsonPropertyName("avg")] decimal? Average);

internal sealed record CyclicalWavesEquilibriumGaugePayload(
    [property: JsonPropertyName("enticker")] string? EnTicker,
    [property: JsonPropertyName("ticker")] string? Ticker,
    // The provider returns this as a period code such as "d" (and has returned
    // numeric values in other payload variants). It is not used for valuation.
    [property: JsonPropertyName("per")] JsonElement? Per,
    [property: JsonPropertyName("lastcaldate")] string? LastCalculationDate,
    [property: JsonPropertyName("close")] decimal? Close,
    [property: JsonPropertyName("balance")] decimal? Balance,
    [property: JsonPropertyName("maxbalance")] decimal? MaxBalance,
    [property: JsonPropertyName("minbalance")] decimal? MinBalance,
    [property: JsonPropertyName("volume")] decimal? Volume,
    [property: JsonPropertyName("date")] string? Date,
    [property: JsonPropertyName("growth")] decimal? Growth,
    [property: JsonPropertyName("a")] decimal? A,
    [property: JsonPropertyName("b")] decimal? B,
    [property: JsonPropertyName("c")] decimal? C,
    [property: JsonPropertyName("d")] decimal? D,
    [property: JsonPropertyName("e")] decimal? E,
    [property: JsonPropertyName("f")] decimal? F);
