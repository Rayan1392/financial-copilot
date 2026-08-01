using System.Text.Json.Serialization;

namespace FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWaves;

internal sealed record CyclicalWavesPsGaugePayload(
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

internal sealed record CyclicalWavesPsCurrentPayload(
    [property: JsonPropertyName("symbol")] string? Symbol,
    [property: JsonPropertyName("ticker")] string? Ticker,
    [property: JsonPropertyName("ps_ratio")] decimal? PsRatio,
    [property: JsonPropertyName("close")] decimal? Close,
    [property: JsonPropertyName("date")] DateOnly? Date);

internal sealed record CyclicalWavesPsCurrentEnvelope(
    [property: JsonPropertyName("data")] CyclicalWavesPsCurrentPayload? Data);

internal sealed record CyclicalWavesPsForwardPayload(
    [property: JsonPropertyName("symbol")] string? Symbol,
    [property: JsonPropertyName("ps")] decimal? Ps);

internal sealed record CyclicalWavesPsForwardEnvelope(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("data")] CyclicalWavesPsForwardPayload? Data);

internal sealed record CyclicalWavesPsHistoryPayload(
    [property: JsonPropertyName("data")] List<CyclicalWavesPsHistoryPointPayload>? Data,
    [property: JsonPropertyName("first_date")] DateOnly? FirstDate,
    [property: JsonPropertyName("last_date")] DateOnly? LastDate,
    [property: JsonPropertyName("data_count")] long? DataCount);

internal sealed record CyclicalWavesPsHistoryPointPayload(
    [property: JsonPropertyName("_id")] string? Id,
    [property: JsonPropertyName("date")] DateOnly? Date,
    [property: JsonPropertyName("ps")] decimal? Ps);
