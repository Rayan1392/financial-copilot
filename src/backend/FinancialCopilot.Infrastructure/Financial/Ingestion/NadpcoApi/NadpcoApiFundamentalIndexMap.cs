namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

public sealed record NadpcoApiFundamentalIndexMapping(
    string MetricCode,
    string UnitKey,
    bool ScaleVerified,
    string ReviewNote);

/// <summary>
/// Reviewed NADPCO <c>companyIndexId</c> allowlist.
/// Percentage-like indicators are intentionally deferred until sampled scale is verified.
/// </summary>
public static class NadpcoApiFundamentalIndexMap
{
    public const string CalculationPolicyVersion = "nadpco-api-fundamental-index-source-v1";

    public static IReadOnlyDictionary<int, NadpcoApiFundamentalIndexMapping> IndexIdToMetric { get; } =
        new Dictionary<int, NadpcoApiFundamentalIndexMapping>
        {
            [65] = new("CURRENT_RATIO", "Ratio", true, "NADPCO sample value 1.03 is ratio-scale."),
            [4069] = new("NET_WORKING_CAPITAL", "Amount", true, "Source value is an amount; source unit is retained as evidence."),
            [4071] = new("CURRENT_ASSETS_TO_TOTAL_ASSETS", "Ratio", true, "NADPCO sample value 0.51 is ratio-scale."),
            [4100] = new("ASSET_TURNOVER", "Ratio", true, "NADPCO sample value 0.27 is ratio-scale."),
            [4101] = new("TANGIBLE_FIXED_ASSETS_TURNOVER", "Ratio", true, "NADPCO sample value 2.16 is ratio-scale."),
            [4106] = new("AVERAGE_COLLECTION_PERIOD", "Days", true, "Duration-style metric; vendor unit is retained as evidence."),
            [4117] = new("DEBT_TO_EQUITY", "Ratio", true, "NADPCO sample value 1.07 is ratio-scale."),
            [41105] = new("COMPREHENSIVE_LIQUIDITY_INDEX", "Ratio", true, "NADPCO sample value -0.56 is ratio-scale.")
        };

    public static IReadOnlyCollection<int> MappedIndexIds { get; } = IndexIdToMetric.Keys.ToArray();
}
