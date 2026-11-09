using System.Security.Cryptography;
using System.Text;

namespace FinancialCopilot.Application.AI.Orchestration;

public interface ISymbolSnapshotFollowUpSuggestionService
{
    IReadOnlyCollection<SuggestedAction> Build(SymbolSnapshotResult result);
}

/// <summary>Next-step buttons for a Symbol Snapshot. A button appears only if its text really routes to its capability.</summary>
public sealed class SymbolSnapshotFollowUpSuggestionService(
    IConversationalCapabilityRegistry registry,
    ICapabilityInterpreter interpreter,
    IChatCapabilityAvailability? availability = null) : ISymbolSnapshotFollowUpSuggestionService
{
    public const string RelevanceReason = "symbol_snapshot_follow_up";
    private const int MaxActions = 4;

    public IReadOnlyCollection<SuggestedAction> Build(SymbolSnapshotResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var symbol = result.Symbol.Trim();
        var hasMonthly = result.MonthlySales.Status == SymbolSnapshotSectionStatus.Available;
        var hasStatements = result.QuarterlyReport.Status == SymbolSnapshotSectionStatus.Available;
        var candidates = new (bool Allowed, string Capability, string Label, string Message)[]
        {
            (hasMonthly, "monthly_activity_trend", "روند فروش ماهانه", $"روند فروش ماهانه {symbol} را نشان بده"),
            (hasStatements, "financial_statement_table", "گزارش فصلی کامل", $"گزارش فصلی {symbol} را نشان بده"),
            (hasStatements, "financial_statement_table", "ترازنامه", $"ترازنامه {symbol} را نشان بده"),
            (hasMonthly, "product_revenue_mix", "ترکیب فروش محصولات", $"ترکیب فروش محصولات {symbol}")
        };

        return candidates
            .Where(candidate => candidate.Allowed && Routes(candidate.Capability, candidate.Message, symbol))
            .Take(MaxActions)
            .Select(candidate => new SuggestedAction(
                ActionId(candidate.Capability, candidate.Message),
                SuggestedActionKind.RunRelatedCapability,
                candidate.Label,
                candidate.Message,
                candidate.Capability,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["symbol"] = symbol },
                RelevanceReason,
                registry.Version))
            .ToArray();
    }

    private bool Routes(string capability, string message, string symbol)
    {
        if (registry.Find(capability) is not { Enabled: true } definition ||
            availability?.Evaluate(definition.ExecutionRoute).IsAvailable == false)
            return false;

        try
        {
            var interpretation = interpreter.Interpret(message);
            var key = SymbolSnapshotIntentRules.CompactKey(symbol);
            return interpretation.CapabilityCandidates.FirstOrDefault()?.CapabilityCode == capability &&
                   interpretation.MissingSlots.Count == 0 &&
                   interpretation.EntityMentions.Count(entity => entity.EntityType != "product") == 1 &&
                   interpretation.EntityMentions.Any(entity => SymbolSnapshotIntentRules.CompactKey(entity.Text) == key);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private string ActionId(string capability, string message)
    {
        var input = string.Join('\n', RelevanceReason, capability, registry.Version, message);
        return "symbol-snapshot:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
    }
}
