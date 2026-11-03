using System.Security.Cryptography;
using System.Text;
using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.Application.AI.Orchestration;

/// <summary>
/// Feature 138. Deterministic company-context follow-ups for a successful, resolved
/// <see cref="MonthlyProductTrendResult"/>. Pure and synchronous: it reads only the typed result,
/// the capability registry and the in-memory interpreter; it never executes a capability.
/// </summary>
public interface IMonthlyProductTrendFollowUpSuggestionService
{
    IReadOnlyCollection<SuggestedAction> Build(MonthlyProductTrendResult result);
}

public sealed class MonthlyProductTrendFollowUpSuggestionService(
    IConversationalCapabilityRegistry registry,
    ICapabilityInterpreter interpreter) : IMonthlyProductTrendFollowUpSuggestionService
{
    public const string RelevanceReason = "monthly_product_trend_follow_up";
    private const int MaximumSymbolLength = 120;

    private sealed record Candidate(
        string CapabilityCode,
        string Prefix,
        Func<string, bool> LooksLike,
        Func<string, string?> ExtractSymbol);

    // Priority order is the order of this list (D-A: only these two company-context follow-ups).
    private static readonly Candidate[] Policy =
    [
        new("product_revenue_mix", "ترکیب فروش محصولات",
            ProductRevenueMixIntentRules.LooksLikeProductRevenueMixQuery,
            ProductRevenueMixIntentRules.ExtractCompanySymbol),
        new("monthly_activity_trend", "روند فروش ماهانه",
            query => MonthlyActivityTrendIntentRules.LooksLikeMonthlyActivityTrendQuery(query),
            query => MonthlyActivityTrendIntentRules.ExtractCompanySymbol(query))
    ];

    public IReadOnlyCollection<SuggestedAction> Build(MonthlyProductTrendResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var symbol = result.CompanySymbol?.Trim();
        if (!result.IsResolved || string.IsNullOrWhiteSpace(symbol) || symbol.Length > MaximumSymbolLength)
            return [];

        var actions = new List<SuggestedAction>(Policy.Length);
        foreach (var candidate in Policy)
        {
            if (registry.Find(candidate.CapabilityCode) is not { Enabled: true }) continue;
            var message = $"{candidate.Prefix} {symbol}";
            if (!RoundTrips(candidate, message, symbol)) continue;
            actions.Add(Create(candidate, message, symbol, result));
        }
        return actions;
    }

    private bool RoundTrips(Candidate candidate, string message, string symbol)
    {
        var expected = Canonical(symbol);
        if (!candidate.LooksLike(message) ||
            MonthlyProductTrendIntentRules.LooksLikeMonthlyProductTrendQuery(message) ||
            Canonical(candidate.ExtractSymbol(message)) != expected)
            return false;

        try
        {
            var interpretation = interpreter.Interpret(message);
            return interpretation.CapabilityCandidates.FirstOrDefault()?.CapabilityCode == candidate.CapabilityCode &&
                   interpretation.MissingSlots.Count == 0 &&
                   interpretation.EntityMentions.Count == 1 &&
                   Canonical(interpretation.EntityMentions[0].Text) == expected;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static string Canonical(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : QueryNormalization.Normalize(value).Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace("‌", string.Empty, StringComparison.Ordinal);

    private SuggestedAction Create(Candidate candidate, string message, string symbol, MonthlyProductTrendResult result)
    {
        var idInput = string.Join('\n', "feature138", RelevanceReason, candidate.CapabilityCode,
            registry.Version, result.ExternalCompanyId ?? Canonical(symbol), result.ProductKey ?? string.Empty);
        var id = "feature138:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(idInput))).ToLowerInvariant();
        return new SuggestedAction(
            id,
            SuggestedActionKind.RunRelatedCapability,
            message,
            message,
            candidate.CapabilityCode,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["symbol"] = symbol },
            RelevanceReason,
            registry.Version);
    }
}
