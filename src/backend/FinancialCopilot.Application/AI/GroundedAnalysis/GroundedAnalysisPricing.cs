using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FinancialCopilot.Application.AI.GroundedAnalysis;

/// <summary>
/// The four fixed credit amounts (PO-6, approved product decision): Simple without web 8, Simple with web 12, Deep
/// without web 16, Deep with web 25. Changing them is an owner decision recorded in the operations runbook; no code
/// path changes them.
/// </summary>
public sealed class GroundedAnalysisCreditsOptions
{
    public decimal SimpleNoWeb { get; set; }
    public decimal SimpleWeb { get; set; }
    public decimal DeepNoWeb { get; set; }
    public decimal DeepWeb { get; set; }
}

public sealed class GroundedAnalysisPricingOptions
{
    public const string SectionName = "GroundedAnalysisPricing";

    public GroundedAnalysisCreditsOptions Credits { get; set; } = new();

    public void Validate()
    {
        if (Credits is null) throw new InvalidOperationException("GroundedAnalysisPricing:Credits is required.");
        foreach (var (name, value) in new[]
                 {
                     ("SimpleNoWeb", Credits.SimpleNoWeb), ("SimpleWeb", Credits.SimpleWeb),
                     ("DeepNoWeb", Credits.DeepNoWeb), ("DeepWeb", Credits.DeepWeb)
                 })
        {
            if (value is <= 0m or > 1000m || value != decimal.Truncate(value))
                throw new InvalidOperationException($"GroundedAnalysisPricing:Credits:{name} must be a whole number between 1 and 1000.");
        }
    }
}

/// <summary>One row of the price list sent to clients (PO-9: the backend is the only source of prices).</summary>
public sealed record GroundedAnalysisPrice(AnalysisDepth Depth, bool WebSearch, decimal Credits);

/// <summary>
/// The one owner of analysis prices (FR-3). Inputs: depth and web switch only — never tokens, reasoning tokens, search
/// calls, a benchmark or the actual API cost (PO-6, PO-7). It has no access to the technical profiles.
/// </summary>
public sealed class GroundedAnalysisPricing
{
    private readonly GroundedAnalysisCreditsOptions _credits;

    public GroundedAnalysisPricing(GroundedAnalysisPricingOptions options)
    {
        _credits = options.Credits;
        Prices = AnalysisChoice.All.Select(choice => new GroundedAnalysisPrice(choice.Depth, choice.WebSearch, CreditsFor(choice))).ToArray();
        PricingVersion = ComputeVersion(Prices);
    }

    public IReadOnlyList<GroundedAnalysisPrice> Prices { get; }

    /// <summary>
    /// A short hash of the four amounts. A client echoes it so a page showing old prices is detected before anything
    /// runs (PO-9); it carries no amount and cannot set one.
    /// </summary>
    public string PricingVersion { get; }

    public decimal CreditsFor(AnalysisDepth depth, bool webSearch) => CreditsFor(new AnalysisChoice(depth, webSearch));

    public decimal CreditsFor(AnalysisChoice choice) => (choice.Depth, choice.WebSearch) switch
    {
        (AnalysisDepth.Simple, false) => _credits.SimpleNoWeb,
        (AnalysisDepth.Simple, true) => _credits.SimpleWeb,
        (AnalysisDepth.Deep, false) => _credits.DeepNoWeb,
        (AnalysisDepth.Deep, true) => _credits.DeepWeb,
        _ => throw new ArgumentOutOfRangeException(nameof(choice))
    };

    /// <summary>True when the client saw these prices, or sent no version (a client that never saw prices).</summary>
    public bool IsCurrent(string? clientPricingVersion) =>
        string.IsNullOrWhiteSpace(clientPricingVersion) ||
        string.Equals(clientPricingVersion.Trim(), PricingVersion, StringComparison.OrdinalIgnoreCase);

    private static string ComputeVersion(IEnumerable<GroundedAnalysisPrice> prices)
    {
        var canonical = string.Join('|', prices.Select(price =>
            $"{price.Depth}:{(price.WebSearch ? 1 : 0)}:{price.Credits.ToString("0.####", CultureInfo.InvariantCulture)}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..12].ToLowerInvariant();
    }
}

/// <summary>
/// The four completion statuses of an answered analysis (FR-11). They settle at the configured fixed amount and are
/// excluded from usage-based settlement in every billing mode (PO-7). Every non-answer keeps its existing zero-charge
/// status.
/// </summary>
public static class GroundedAnalysisBillingPolicy
{
    public const string AnsweredStatusPrefix = "GroundedAnalysisAnswered.";

    /// <summary>The operation of a confirmed 163 analysis reservation (keyed by the offer).</summary>
    public const string AnalysisOperationCode = "AiQuery.GroundedAnalysis";

    /// <summary>The zero-priced operation that claims a cancelled 163 offer (design-proposal §6).</summary>
    public const string OfferCancelOperationCode = "AiQuery.GroundedAnalysisOfferCancel";

    /// <summary>The zero-charge status a cancelled offer is settled with.</summary>
    public const string OfferCancelledCompletionStatus = "CancelledBeforeExecution";

    public static string AnsweredStatus(AnalysisChoice choice) => AnsweredStatusPrefix + choice.Tier;

    public static IReadOnlyList<string> AnsweredStatuses { get; } = AnalysisChoice.All.Select(AnsweredStatus).ToArray();

    public static bool TryParseAnsweredStatus(string? completionStatus, out AnalysisChoice choice)
    {
        foreach (var candidate in AnalysisChoice.All)
        {
            if (string.Equals(completionStatus, AnsweredStatus(candidate), StringComparison.OrdinalIgnoreCase))
            {
                choice = candidate;
                return true;
            }
        }

        choice = default;
        return false;
    }

    /// <summary>The fixed amount for an answered-analysis status; null for every other status.</summary>
    public static decimal? FixedCredits(string? completionStatus, GroundedAnalysisPricing pricing) =>
        TryParseAnsweredStatus(completionStatus, out var choice) ? pricing.CreditsFor(choice) : null;
}
