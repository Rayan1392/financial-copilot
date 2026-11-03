using System.Security.Cryptography;
using System.Text;
using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.Application.AI.Orchestration;

/// <summary>
/// Feature 075 extension. Builds deterministic follow-ups from the already returned revenue-mix
/// rows. This service is pure: it performs no provider, repository, or downstream use-case calls.
/// </summary>
public interface IProductRevenueMixFollowUpSuggestionService
{
    IReadOnlyCollection<SuggestedAction> Build(ProductRevenueMixResponse result);
}

public sealed class ProductRevenueMixFollowUpSuggestionService(
    IConversationalCapabilityRegistry registry,
    ICapabilityInterpreter interpreter) : IProductRevenueMixFollowUpSuggestionService
{
    public const string RelevanceReason = "product_revenue_mix_follow_up";
    private const int MaximumProductActions = 2;
    private const int MaximumValueLength = 120;
    private const int MaximumMessageLength = 500;
    private const string ProductActionCapability = "monthly_product_trend";
    private const string GovernedProductTrendCapability = "product_sales_trend";
    private const string CompanyTrendCapability = "monthly_activity_trend";

    private static readonly HashSet<string> SyntheticTitles = new(StringComparer.Ordinal)
    {
        "other", "others", "total", "aggregate", "summary",
        "سایر", "ساير", "جمع", "مجموع", "کل", "كلي", "کلی", "بدون عنوان"
    };

    public IReadOnlyCollection<SuggestedAction> Build(ProductRevenueMixResponse result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var symbol = result.CompanySymbol?.Trim();
        if (string.IsNullOrWhiteSpace(symbol) || symbol.Length > MaximumValueLength)
            return [];

        var actions = new List<SuggestedAction>(MaximumProductActions + 1);
        if (registry.Find(GovernedProductTrendCapability) is { Enabled: true })
        {
            var rows = result.Products
                .Select(row => new ProductRow(row, Normalize(row.ProductName)))
                .ToArray();
            var ambiguousTitles = rows
                .GroupBy(row => row.NormalizedTitle, StringComparer.Ordinal)
                .Where(group => group.Key.Length == 0 || group.Skip(1).Any())
                .Select(group => group.Key)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var row in rows)
            {
                if (actions.Count == MaximumProductActions)
                    break;
                if (!IsEligible(row, ambiguousTitles))
                    continue;

                var title = row.Item.ProductName.Trim();
                var message = $"روند فروش {title} {symbol}";
                if (!ProductRoundTrips(message, title, symbol))
                    continue;

                actions.Add(CreateProductAction(message, title, row.NormalizedTitle, symbol));
            }
        }

        if (registry.Find(CompanyTrendCapability) is { Enabled: true })
        {
            var message = $"روند فروش ماهانه {symbol}";
            if (message.Length <= MaximumMessageLength && CompanyRoundTrips(message, symbol))
                actions.Add(CreateCompanyAction(message, symbol));
        }

        return actions;
    }

    private static bool IsEligible(ProductRow row, IReadOnlySet<string> ambiguousTitles)
    {
        var title = row.Item.ProductName?.Trim();
        return !string.IsNullOrWhiteSpace(title) &&
               title.Length <= MaximumValueLength &&
               row.NormalizedTitle.Length > 0 &&
               !ambiguousTitles.Contains(row.NormalizedTitle) &&
               !SyntheticTitles.Contains(row.NormalizedTitle);
    }

    private bool ProductRoundTrips(string message, string title, string symbol)
    {
        if (message.Length > MaximumMessageLength ||
            !MonthlyProductTrendIntentRules.LooksLikeMonthlyProductTrendQuery(message))
            return false;

        var parsed = MonthlyProductTrendIntentRules.BuildQuery(message);
        if (parsed.UnsupportedTimeWindow ||
            Normalize(parsed.CompanyText) != Normalize(symbol) ||
            Normalize(parsed.ProductText) != Normalize(title))
            return false;

        try
        {
            var interpretation = interpreter.Interpret(message);
            return interpretation.CapabilityCandidates.FirstOrDefault()?.CapabilityCode == GovernedProductTrendCapability &&
                   interpretation.MissingSlots.Count == 0 &&
                   interpretation.EntityMentions.Any(entity =>
                       entity.EntityType == "product" && Normalize(entity.Text) == Normalize(title)) &&
                   interpretation.EntityMentions.Any(entity =>
                       entity.EntityType != "product" && Normalize(entity.Text) == Normalize(symbol));
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private bool CompanyRoundTrips(string message, string symbol)
    {
        if (!MonthlyActivityTrendIntentRules.LooksLikeMonthlyActivityTrendQuery(message) ||
            Normalize(MonthlyActivityTrendIntentRules.ExtractCompanySymbol(message)) != Normalize(symbol))
            return false;

        try
        {
            var interpretation = interpreter.Interpret(message);
            return interpretation.CapabilityCandidates.FirstOrDefault()?.CapabilityCode == CompanyTrendCapability &&
                   interpretation.MissingSlots.Count == 0 &&
                   interpretation.EntityMentions.Count(entity => entity.EntityType != "product") == 1 &&
                   interpretation.EntityMentions.Any(entity => Normalize(entity.Text) == Normalize(symbol));
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private SuggestedAction CreateProductAction(
        string message,
        string title,
        string normalizedTitle,
        string symbol) =>
        new(
            StableId(ProductActionCapability, symbol, normalizedTitle),
            SuggestedActionKind.RunRelatedCapability,
            message,
            message,
            ProductActionCapability,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["company"] = symbol,
                ["symbol"] = symbol,
                ["product"] = title
            },
            RelevanceReason,
            registry.Version);

    private SuggestedAction CreateCompanyAction(string message, string symbol) =>
        new(
            StableId(CompanyTrendCapability, symbol, string.Empty),
            SuggestedActionKind.RunRelatedCapability,
            message,
            message,
            CompanyTrendCapability,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["company"] = symbol,
                ["symbol"] = symbol
            },
            RelevanceReason,
            registry.Version);

    private string StableId(string capability, string symbol, string productIdentity)
    {
        var input = string.Join('\n', "feature075", RelevanceReason, capability, registry.Version,
            Normalize(symbol), productIdentity);
        return "feature075:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
    }

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : MonthlyProductTrendCalculator.NormalizeProductText(value)
                .Trim(' ', ',', '،', ';', '؛', ':')
                .ToLowerInvariant();

    private sealed record ProductRow(ProductRevenueMixProductItem Item, string NormalizedTitle);
}
