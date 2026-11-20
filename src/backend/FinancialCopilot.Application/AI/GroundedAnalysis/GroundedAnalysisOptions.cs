namespace FinancialCopilot.Application.AI.GroundedAnalysis;

// Change «sol-deep-analysis-web-search» (162 and 163, owner decisions PO-1 … PO-16): the analysis of a report (162)
// or of a symbol's quarterly income statement (163) by gpt-6.2-sol, at a chosen depth and with or without web search.
// This file owns the technical budgets only (PO-8). Prices live in GroundedAnalysisPricingOptions (PO-6); neither
// reads the other.

/// <summary>How deep the analysis goes (PO-4). Both depths use the same model; only the technical budgets differ.</summary>
public enum AnalysisDepth
{
    Simple,
    Deep
}

/// <summary>What the user chose for one analysis: the only inputs of the price (PO-6).</summary>
public readonly record struct AnalysisChoice(AnalysisDepth Depth, bool WebSearch)
{
    /// <summary>SimpleNoWeb, SimpleWeb, DeepNoWeb or DeepWeb: the name of the tier in configuration, billing and logs.</summary>
    public string Tier => $"{Depth}{(WebSearch ? "Web" : "NoWeb")}";

    public static IReadOnlyList<AnalysisChoice> All { get; } =
    [
        new(AnalysisDepth.Simple, false),
        new(AnalysisDepth.Simple, true),
        new(AnalysisDepth.Deep, false),
        new(AnalysisDepth.Deep, true)
    ];
}

/// <summary>The technical budget of one depth (PO-8). Never an input of the price.</summary>
public sealed class GroundedAnalysisProfileOptions
{
    public string ReasoningEffort { get; set; } = "medium";
    /// <summary>Includes reasoning tokens (OpenAI <c>max_output_tokens</c>).</summary>
    public int MaxOutputTokens { get; set; } = 8000;
    public int InputTokenTarget { get; set; } = 10000;
    public int TimeoutSeconds { get; set; } = 90;
    /// <summary>Hard per-response cap on hosted search calls when web search is on.</summary>
    public int MaxWebSearchCalls { get; set; } = 2;

    internal void Validate(string name)
    {
        if (!GroundedAnalysisOptions.ReasoningEfforts.Contains(ReasoningEffort))
            throw new InvalidOperationException($"GroundedAnalysis:Profiles:{name}:ReasoningEffort must be one of {string.Join(", ", GroundedAnalysisOptions.ReasoningEfforts)}.");
        if (MaxOutputTokens is < 500 or > 32000)
            throw new InvalidOperationException($"GroundedAnalysis:Profiles:{name}:MaxOutputTokens must be between 500 and 32000.");
        if (InputTokenTarget is < 4000 or > 30000)
            throw new InvalidOperationException($"GroundedAnalysis:Profiles:{name}:InputTokenTarget must be between 4000 and 30000.");
        // The provider HTTP timeout (120 s) must stay above the model timeout (design-proposal §10).
        if (TimeoutSeconds is < 5 or > 115)
            throw new InvalidOperationException($"GroundedAnalysis:Profiles:{name}:TimeoutSeconds must be between 5 and 115.");
        if (MaxWebSearchCalls is < 1 or > 5)
            throw new InvalidOperationException($"GroundedAnalysis:Profiles:{name}:MaxWebSearchCalls must be between 1 and 5.");
    }
}

public sealed class GroundedAnalysisProfilesOptions
{
    public GroundedAnalysisProfileOptions Simple { get; set; } = new()
    {
        ReasoningEffort = "low",
        MaxOutputTokens = 3000,
        InputTokenTarget = 6000,
        TimeoutSeconds = 45,
        MaxWebSearchCalls = 1
    };

    public GroundedAnalysisProfileOptions Deep { get; set; } = new();
}

public sealed class GroundedAnalysisWebSearchOptions
{
    public string SearchContextSize { get; set; } = "low";
    public string? UserLocationCountry { get; set; } = "IR";
    /// <summary>Empty = open web with source priority in the prompt (OQ-109 default).</summary>
    public List<string> AllowedDomains { get; set; } = [];
}

/// <summary>The 162 disposition step (PO-14): one light structured call, no tools, before the analysis call.</summary>
public sealed class GroundedAnalysisDispositionOptions
{
    /// <summary>Null = the existing workload model.</summary>
    public string? Model { get; set; }
    public int MaxOutputTokens { get; set; } = 200;
    public int TimeoutSeconds { get; set; } = 15;
}

/// <summary>Monitoring only (PO-7, FR-12): never read by billing.</summary>
public sealed class GroundedAnalysisTelemetryOptions
{
    /// <summary>OpenAI list price of one web search call (USD 10 per 1,000 calls).</summary>
    public decimal WebSearchCallUsd { get; set; } = 0.01m;
    /// <summary>An analysis whose estimated cost exceeds this logs a warning (alert threshold).</summary>
    public decimal CostAlertUsd { get; set; } = 0.20m;
}

public sealed class GroundedAnalysisOptions
{
    public const string SectionName = "GroundedAnalysis";

    /// <summary>gpt-6.2-sol rejects <c>none</c> (spike T00).</summary>
    public static readonly IReadOnlySet<string> ReasoningEfforts =
        new HashSet<string>(["low", "medium", "high", "xhigh", "max"], StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> SearchContextSizes =
        new HashSet<string>(["low", "medium", "high"], StringComparer.Ordinal);

    public bool Enabled { get; set; }
    public string ProviderKey { get; set; } = "OpenAI";
    public string Model { get; set; } = "gpt-6.2-sol";
    public AnalysisDepth DefaultDepth { get; set; } = AnalysisDepth.Deep;
    public bool DefaultWebSearch { get; set; } = true;
    public int OfferLifetimeMinutes { get; set; } = 30;
    public GroundedAnalysisProfilesOptions Profiles { get; set; } = new();
    public GroundedAnalysisWebSearchOptions WebSearch { get; set; } = new();
    public GroundedAnalysisDispositionOptions Disposition { get; set; } = new();
    public GroundedAnalysisTelemetryOptions Telemetry { get; set; } = new();
    /// <summary>Same conservative estimate as Report Discussion.</summary>
    public decimal CharsPerToken { get; set; } = 2.5m;

    public AnalysisChoice DefaultChoice => new(DefaultDepth, DefaultWebSearch);

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ProviderKey)) throw new InvalidOperationException("GroundedAnalysis:ProviderKey is required.");
        if (string.IsNullOrWhiteSpace(Model)) throw new InvalidOperationException("GroundedAnalysis:Model is required.");
        if (!Enum.IsDefined(DefaultDepth)) throw new InvalidOperationException("GroundedAnalysis:DefaultDepth must be Simple or Deep.");
        if (OfferLifetimeMinutes is < 1 or > 1440) throw new InvalidOperationException("GroundedAnalysis:OfferLifetimeMinutes must be between 1 and 1440.");
        if (Profiles?.Simple is null || Profiles.Deep is null) throw new InvalidOperationException("GroundedAnalysis:Profiles:Simple and Deep are required.");
        Profiles.Simple.Validate(nameof(AnalysisDepth.Simple));
        Profiles.Deep.Validate(nameof(AnalysisDepth.Deep));
        if (WebSearch is null || !SearchContextSizes.Contains(WebSearch.SearchContextSize))
            throw new InvalidOperationException("GroundedAnalysis:WebSearch:SearchContextSize must be low, medium or high.");
        if (WebSearch.UserLocationCountry is { Length: > 0 } country && (country.Length != 2 || !country.All(char.IsAsciiLetterUpper)))
            throw new InvalidOperationException("GroundedAnalysis:WebSearch:UserLocationCountry must be a two-letter ISO country code.");
        if (WebSearch.AllowedDomains.Count > 100 || WebSearch.AllowedDomains.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException("GroundedAnalysis:WebSearch:AllowedDomains must hold at most 100 non-empty domains.");
        if (Disposition is null || Disposition.MaxOutputTokens is < 50 or > 1000)
            throw new InvalidOperationException("GroundedAnalysis:Disposition:MaxOutputTokens must be between 50 and 1000.");
        if (Disposition.TimeoutSeconds is < 1 or > 60)
            throw new InvalidOperationException("GroundedAnalysis:Disposition:TimeoutSeconds must be between 1 and 60.");
        if (Telemetry is null || Telemetry.WebSearchCallUsd < 0m || Telemetry.CostAlertUsd <= 0m)
            throw new InvalidOperationException("GroundedAnalysis:Telemetry values must be non-negative, and CostAlertUsd positive.");
        if (CharsPerToken is < 1m or > 6m) throw new InvalidOperationException("GroundedAnalysis:CharsPerToken must be between 1 and 6.");
    }
}

/// <summary>The technical budget of one analysis call, read from <see cref="GroundedAnalysisOptions"/> (FR-2).</summary>
public sealed record GroundedAnalysisProfile(
    AnalysisDepth Depth,
    string ProviderKey,
    string Model,
    string ReasoningEffort,
    int MaxOutputTokens,
    int InputTokenTarget,
    int TimeoutSeconds,
    int MaxWebSearchCalls);

/// <summary>
/// The one owner of the technical budgets per depth (FR-2). It has no access to prices, and prices have no access to
/// it (AC-3, AC-18).
/// </summary>
public sealed class GroundedAnalysisProfiles(GroundedAnalysisOptions options)
{
    public GroundedAnalysisProfile For(AnalysisDepth depth)
    {
        var profile = depth == AnalysisDepth.Simple ? options.Profiles.Simple : options.Profiles.Deep;
        return new GroundedAnalysisProfile(
            depth,
            options.ProviderKey.Trim(),
            options.Model.Trim(),
            profile.ReasoningEffort,
            profile.MaxOutputTokens,
            profile.InputTokenTarget,
            profile.TimeoutSeconds,
            profile.MaxWebSearchCalls);
    }
}
