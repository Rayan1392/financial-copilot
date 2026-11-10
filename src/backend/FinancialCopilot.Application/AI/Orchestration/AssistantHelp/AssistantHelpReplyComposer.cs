namespace FinancialCopilot.Application.AI.Orchestration;

public sealed record AssistantHelpReply(string Text, IReadOnlyCollection<SuggestedAction> Actions);

public interface IAssistantHelpReplyComposer
{
    AssistantHelpReply Compose(string replyLanguage);
}

/// <summary>The single owner of the assistant help text, its sample questions and their suggested actions.</summary>
public sealed class AssistantHelpReplyComposer(
    IConversationalCapabilityRegistry registry,
    IChatCapabilityAvailability? availability = null) : IAssistantHelpReplyComposer
{
    public const string ExampleSymbol = "کگل";

    private const string IntroductionFa =
        "سلام! من ساپیو، دستیار هوشمند بازار هستم و می‌توانم فروش ماهانه، گزارش فصلی، ترازنامه و ترکیب فروش شرکت‌های بورسی را نشان بدهم. برای نمونه بپرسید:";

    private const string IntroductionEn =
        "Hello! I am Sapio, your market assistant. I can show monthly sales, quarterly reports, balance sheets and product sales mix for listed companies. For example, ask:";

    private sealed record Sample(string Id, string Capability, string LabelFa, string LabelEn, string MessageFa, string MessageEn);

    private static readonly IReadOnlyList<Sample> Samples =
    [
        new("assistant_help:monthly_sales", "monthly_activity_trend", "روند فروش ماهانه", "Monthly sales trend",
            $"روند فروش ماهانه {ExampleSymbol} را نشان بده", $"monthly sales trend for {ExampleSymbol}"),
        new("assistant_help:quarterly_report", "financial_statement_table", "گزارش فصلی", "Quarterly report",
            $"گزارش فصلی {ExampleSymbol} را نشان بده", $"show {ExampleSymbol} quarterly income statement"),
        new("assistant_help:balance_sheet", "financial_statement_table", "ترازنامه", "Balance sheet",
            $"ترازنامه {ExampleSymbol} را نشان بده", $"show {ExampleSymbol} balance sheet"),
        new("assistant_help:product_mix", "product_revenue_mix", "ترکیب فروش محصولات", "Product sales mix",
            $"ترکیب فروش محصولات {ExampleSymbol}", $"product revenue mix for {ExampleSymbol}")
    ];

    public AssistantHelpReply Compose(string replyLanguage)
    {
        var persian = string.Equals(replyLanguage, "fa", StringComparison.OrdinalIgnoreCase);
        var available = Samples.Where(IsAvailable).ToArray();
        var lines = available.Select(sample => $"• {(persian ? sample.MessageFa : sample.MessageEn)}");
        var text = string.Join("\n", new[] { persian ? IntroductionFa : IntroductionEn }.Concat(lines));
        var actions = available
            .Select(sample => new SuggestedAction(
                sample.Id,
                SuggestedActionKind.ShowCapabilityHelp,
                persian ? sample.LabelFa : sample.LabelEn,
                persian ? sample.MessageFa : sample.MessageEn,
                sample.Capability,
                new Dictionary<string, string>(StringComparer.Ordinal),
                AssistantHelpCapability.RelevanceReason,
                registry.Version))
            .ToArray();
        return new AssistantHelpReply(text, actions);
    }

    private bool IsAvailable(Sample sample) =>
        registry.Find(sample.Capability) is { Enabled: true } definition &&
        availability?.Evaluate(definition.ExecutionRoute).IsAvailable != false;
}
