namespace FinancialCopilot.Application.AI.Orchestration;

/// <summary>Which financial data provider answers Chat questions (Feature 148).</summary>
public enum FinancialDataChatProvider
{
    NoavaranAmin,
    CyclicalWaves
}

public sealed class FinancialDataOptions
{
    public const string SectionName = "FinancialData";

    // C1/C1-M exclusion of legacy and mixed CyclicalWaves-derived DerivedMetrics is permanent and has no setting.
    public FinancialDataChatProvider ChatProvider { get; set; } = FinancialDataChatProvider.NoavaranAmin;

    /// <summary>
    /// Feature 162: capability route code → provider name, for routes in <see cref="ChatCapabilitySourceMap.OverridableRoutes"/>
    /// only. Empty values mean "not overridden". Validated by <see cref="ChatCapabilitySourceMap"/>.
    /// </summary>
    public Dictionary<string, string?> CapabilitySources { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public enum ChatCapabilityAvailabilityStatus
{
    Available,
    Retired,
    UnavailableForProvider
}

public sealed record ChatCapabilityAvailabilityOutcome(
    ChatCapabilityAvailabilityStatus Status,
    string? ReasonCode,
    string? PersianMessage,
    string? EnglishMessage)
{
    public bool IsAvailable => Status == ChatCapabilityAvailabilityStatus.Available;

    public static ChatCapabilityAvailabilityOutcome Available { get; } =
        new(ChatCapabilityAvailabilityStatus.Available, null, null, null);
}

public interface IChatCapabilityAvailability
{
    /// <summary>The base provider mode (<c>FinancialData:ChatProvider</c>).</summary>
    FinancialDataChatProvider Provider { get; }

    /// <summary>The provider that answers <paramref name="executionRoute"/>: its override, otherwise <see cref="Provider"/>.</summary>
    FinancialDataChatProvider SourceFor(string executionRoute);

    /// <param name="executionRoute">A <see cref="CapabilityExecutionRoutes"/> code.</param>
    ChatCapabilityAvailabilityOutcome Evaluate(string executionRoute);
}

/// <summary>
/// The single owner of which Chat capabilities each provider mode may answer (design §10.1).
/// There is no fallback between providers: an unavailable capability gets an explicit answer.
/// </summary>
public sealed class ChatCapabilityAvailability(FinancialDataChatProvider provider, ChatCapabilitySourceMap sources)
    : IChatCapabilityAvailability
{
    public ChatCapabilityAvailability(FinancialDataChatProvider provider)
        : this(provider, ChatCapabilitySourceMap.Empty)
    {
    }

    public const string RetiredReasonCode = "capability_retired";
    public const string UnavailableForProviderReasonCode = "capability_unavailable_for_provider";
    public const string ComprehensiveAnalysisUnavailableReasonCode = "comprehensive_analysis_unavailable";

    public const string RetiredMessageFa = "این قابلیت به‌دلیل توقف منبع دادهٔ قبلی غیرفعال شده است.";
    public const string UnavailableForProviderMessageFa = "این پرسش با دادهٔ منبع فعال (CyclicalWaves) قابل پاسخ نیست.";
    public const string ComprehensiveAnalysisUnavailableMessageFa = "تحلیل جامع در منبع دادهٔ فعال در دسترس نیست.";

    public const string RetiredMessageEn = "This capability was disabled because its former data source was retired.";
    public const string UnavailableForProviderMessageEn = "This question cannot be answered from the active data source (CyclicalWaves).";
    public const string ComprehensiveAnalysisUnavailableMessageEn = "Comprehensive analysis is not available from the active data source.";

    /// <summary>Legacy-only capabilities retired in both modes (Features 115 and 125).</summary>
    public static readonly IReadOnlySet<string> RetiredRoutes = new HashSet<string>(StringComparer.Ordinal)
    {
        CapabilityExecutionRoutes.PsGaugeVisualization,
        CapabilityExecutionRoutes.IndustryRelativeValuation
    };

    /// <summary>Routes answered from the CyclicalWaves v2 tables in CyclicalWaves mode.</summary>
    public static readonly IReadOnlySet<string> CyclicalWavesSupportedRoutes = new HashSet<string>(StringComparer.Ordinal)
    {
        CapabilityExecutionRoutes.SymbolLookup,
        CapabilityExecutionRoutes.FinancialStatementTable,
        CapabilityExecutionRoutes.ComprehensiveAnalysis,
        CapabilityExecutionRoutes.Scanner,
        CapabilityExecutionRoutes.SymbolSnapshot,
        CapabilityExecutionRoutes.AssistantHelp
    };

    private static readonly ChatCapabilityAvailabilityOutcome Retired = new(
        ChatCapabilityAvailabilityStatus.Retired,
        RetiredReasonCode,
        RetiredMessageFa,
        RetiredMessageEn);

    private static readonly ChatCapabilityAvailabilityOutcome UnavailableForCyclicalWaves = new(
        ChatCapabilityAvailabilityStatus.UnavailableForProvider,
        UnavailableForProviderReasonCode,
        UnavailableForProviderMessageFa,
        UnavailableForProviderMessageEn);

    private static readonly ChatCapabilityAvailabilityOutcome ComprehensiveAnalysisUnavailable = new(
        ChatCapabilityAvailabilityStatus.UnavailableForProvider,
        ComprehensiveAnalysisUnavailableReasonCode,
        ComprehensiveAnalysisUnavailableMessageFa,
        ComprehensiveAnalysisUnavailableMessageEn);

    public FinancialDataChatProvider Provider { get; } = provider;

    public FinancialDataChatProvider SourceFor(string executionRoute) =>
        sources.TryGetSource(executionRoute, out var source) ? source : Provider;

    public ChatCapabilityAvailabilityOutcome Evaluate(string executionRoute)
    {
        if (RetiredRoutes.Contains(executionRoute))
        {
            return Retired;
        }

        // Feature 162: an overridden capability is judged by its own source's matrix.
        return SourceFor(executionRoute) switch
        {
            // C2: while comprehensive analysis is sourced from NoavaranAmin, neither the legacy nor the v2 analysis tables are read.
            FinancialDataChatProvider.NoavaranAmin when executionRoute == CapabilityExecutionRoutes.ComprehensiveAnalysis =>
                ComprehensiveAnalysisUnavailable,
            FinancialDataChatProvider.NoavaranAmin => ChatCapabilityAvailabilityOutcome.Available,
            FinancialDataChatProvider.CyclicalWaves when CyclicalWavesSupportedRoutes.Contains(executionRoute) =>
                ChatCapabilityAvailabilityOutcome.Available,
            _ => UnavailableForCyclicalWaves
        };
    }

    /// <summary>Maps an availability reason code to its fixed message, or <c>null</c> for other codes.</summary>
    public static string? MessageForReasonCode(string? reasonCode, bool persian) => reasonCode switch
    {
        RetiredReasonCode => persian ? RetiredMessageFa : RetiredMessageEn,
        UnavailableForProviderReasonCode => persian ? UnavailableForProviderMessageFa : UnavailableForProviderMessageEn,
        ComprehensiveAnalysisUnavailableReasonCode => persian
            ? ComprehensiveAnalysisUnavailableMessageFa
            : ComprehensiveAnalysisUnavailableMessageEn,
        _ => null
    };
}
