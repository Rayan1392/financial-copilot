using System.Globalization;
using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.Application.AI.Orchestration;

public sealed class IndustryRelativeValuationReadOptions
{
    public const string SectionName = "IndustryRelativeValuation";
    public int DefaultResultLimit { get; init; } = 10;
    public int MaximumResultLimit { get; init; } = 100;

    public void Validate()
    {
        if (DefaultResultLimit is < 1 or > 100)
            throw new InvalidOperationException("IndustryRelativeValuation:DefaultResultLimit must be between 1 and 100.");
        if (MaximumResultLimit is < 1 or > 1000)
            throw new InvalidOperationException("IndustryRelativeValuation:MaximumResultLimit must be between 1 and 1000.");
        if (DefaultResultLimit > MaximumResultLimit)
            throw new InvalidOperationException("IndustryRelativeValuation default result limit cannot exceed maximum result limit.");
    }
}

public enum IndustryRelativeValuationResolutionStatus
{
    Resolved, Ambiguous, Missing, NotFound, InvalidIndustryMembership, DifferentIndustries
}

public sealed record IndustryRelativeValuationResolution(
    IndustryRelativeValuationResolutionStatus Status,
    Guid? GroupId = null,
    string? GroupTitle = null,
    Guid? IndustryId = null,
    string? IndustryName = null,
    IReadOnlyList<Guid>? CompanyIds = null,
    IReadOnlyList<string>? Symbols = null,
    string? Detail = null,
    IReadOnlyList<string>? Candidates = null,
    IReadOnlyList<Guid>? CandidateIds = null);

public interface IIndustryRelativeValuationSemanticResolver
{
    Task<IndustryRelativeValuationResolution> ResolveAsync(
        string capabilityCode,
        QueryInterpretation interpretation,
        CancellationToken cancellationToken = default);
}

public sealed record IndustryRelativeValuationReadRequest(
    Guid? GroupId,
    IReadOnlyList<Guid> CompanyIds,
    string CapabilityCode,
    int Limit = 3);

public sealed record RelativeValuationSourceEvidence(
    string MetricKind,
    string ObservationId,
    DateTimeOffset? ObservedAtUtc,
    DateTimeOffset? PersistedAtUtc,
    string SourceVersion,
    string SourceWatermark);

public sealed record RelativeValuationMetricReadModel(
    decimal? Percent,
    decimal? BenchmarkValue,
    string Classification,
    bool IsOutlier,
    string Reason,
    string DataQualityStatus,
    string MetricKind = "",
    string BenchmarkQuality = "",
    int BenchmarkCleanCount = 0,
    int BenchmarkOutlierCount = 0,
    string InsufficientBenchmarkReason = "");

public sealed record RelativeValuationMemberReadModel(
    Guid CompanyId,
    string Symbol,
    string CompanyName,
    int? Rank,
    int TotalMembers,
    RelativeValuationMetricReadModel PE,
    RelativeValuationMetricReadModel PS,
    RelativeValuationMetricReadModel Equilibrium,
    IReadOnlyList<RelativeValuationSourceEvidence>? SourceEvidence = null);

public sealed record IndustryRelativeValuationReadModel(
    string CapabilityCode,
    Guid GroupId,
    string GroupExternalId,
    string GroupTitle,
    DateOnly CalculationDate,
    Guid CalculationId,
    int CalculationVersion,
    string PublicationStatus,
    DateTimeOffset? PublishedAtUtc,
    string AlgorithmVersion,
    string RankVersion,
    int TotalMembers,
    int TotalRankedMembers,
    IReadOnlyList<RelativeValuationMemberReadModel> Members,
    IReadOnlyList<RelativeValuationMetricReadModel> Benchmarks,
    string DataQualityStatus,
    DateTimeOffset CalculatedAtUtc = default,
    IReadOnlyList<RelativeValuationSourceEvidence>? SourceFreshnessEvidence = null,
    string BarrierStatus = "",
    string ReadinessStatus = "",
    string InsufficientBenchmarkReason = "",
    Guid? IndustryId = null,
    string? IndustryExternalId = null,
    string? IndustryTitle = null);

public interface IIndustryRelativeValuationReadRepository
{
    Task<IndustryRelativeValuationReadModel?> ReadAsync(
        IndustryRelativeValuationReadRequest request,
        CancellationToken cancellationToken = default);
}

public static class IndustryRelativeValuationPresentation
{
    public static string Explain(IndustryRelativeValuationReadModel model, string language = "fa") =>
        language.Equals("en", StringComparison.OrdinalIgnoreCase)
            ? ExplainEnglish(model)
            : ExplainPersian(model);

    private static string ExplainEnglish(IndustryRelativeValuationReadModel model)
    {
        var title = model.CapabilityCode switch
        {
            "symbol_vs_industry_relative_valuation" => "Symbol versus industry comparison",
            "industry_relative_valuation_ranking" => "Industry relative valuation ranking",
            "industry_relative_valuation_summary" => "Industry relative valuation summary",
            "symbol_pair_within_industry" => "Symbol pair comparison",
            _ => "Industry relative valuation"
        };
        var lines = new List<string>
        {
            $"{title}: {model.GroupTitle}",
            $"Published snapshot: {model.PublicationStatus}; calculation date: {model.CalculationDate:yyyy-MM-dd}; calculated: {model.CalculatedAtUtc:O}; published: {model.PublishedAtUtc:O}",
            $"Members: {model.TotalMembers}; ranked members: {model.TotalRankedMembers}; barrier/readiness: {model.BarrierStatus}/{model.ReadinessStatus}"
        };
        foreach (var member in model.Members)
            lines.Add($"{member.Symbol}: rank {member.Rank?.ToString() ?? "unranked"}/{model.TotalMembers}; PE {MetricEnglish(member.PE)}; PS {MetricEnglish(member.PS)}; equilibrium {MetricEnglish(member.Equilibrium)}");
        if (!string.IsNullOrWhiteSpace(model.InsufficientBenchmarkReason))
            lines.Add($"Benchmark unavailable: {model.InsufficientBenchmarkReason}");
        lines.Add("This is informational persisted snapshot data, not investment advice.");
        return string.Join(Environment.NewLine, lines);
    }

    private static string ExplainPersian(IndustryRelativeValuationReadModel model)
    {
        var title = model.CapabilityCode switch
        {
            "symbol_vs_industry_relative_valuation" => "\u0645\u0642\u0627\u06cc\u0633\u0647 \u0646\u0645\u0627\u062f \u0628\u0627 \u0635\u0646\u0639\u062a",
            "industry_relative_valuation_ranking" => "\u0631\u062a\u0628\u0647\u200c\u0628\u0646\u062f\u06cc \u0627\u0631\u0632\u0634\u200c\u06af\u0630\u0627\u0631\u06cc \u0646\u0633\u0628\u06cc \u0635\u0646\u0639\u062a",
            "industry_relative_valuation_summary" => "\u062e\u0644\u0627\u0635\u0647 \u0627\u0631\u0632\u0634\u200c\u06af\u0630\u0627\u0631\u06cc \u0646\u0633\u0628\u06cc \u0635\u0646\u0639\u062a",
            "symbol_pair_within_industry" => "\u0645\u0642\u0627\u06cc\u0633\u0647 \u062f\u0648 \u0646\u0645\u0627\u062f \u062f\u0631 \u0635\u0646\u0639\u062a",
            _ => "\u0627\u0631\u0632\u0634\u200c\u06af\u0630\u0627\u0631\u06cc \u0646\u0633\u0628\u06cc \u0635\u0646\u0639\u062a"
        };
        var lines = new List<string>
        {
            $"### {title}",
            $"**\u06af\u0631\u0648\u0647 \u0635\u0646\u0639\u062a\u06cc:** {model.GroupTitle}",
            $"**\u0627\u0646\u062f\u0627\u0632\u0647 \u06af\u0631\u0648\u0647:** {FormatPersianInteger(model.TotalMembers)} \u0646\u0645\u0627\u062f",
            string.Empty
        };
        lines.Add("| \u0646\u0645\u0627\u062f | P/E | P/S | \u0642\u06cc\u0645\u062a \u0628\u0647 \u062a\u0639\u0627\u062f\u0644\u06cc |");
        lines.Add("|---|---:|---:|---:|");
        foreach (var member in model.Members)
            lines.Add($"| {member.Symbol} | {FormatPercent(member.PE.Percent)} | {FormatPercent(member.PS.Percent)} | {FormatPercent(member.Equilibrium.Percent)} |");
        var peBenchmark = FormatPercent(Benchmark(model, "PE"));
        var psBenchmark = FormatPercent(Benchmark(model, "PS"));
        var equilibriumBenchmark = FormatPercent(Benchmark(model, "Equilibrium"));
        lines.Add($"| \u0645\u06cc\u0627\u0646\u06af\u06cc\u0646 \u0635\u0646\u0639\u062a | {peBenchmark} | {psBenchmark} | {equilibriumBenchmark} |");
        if (!string.IsNullOrWhiteSpace(model.InsufficientBenchmarkReason))
            lines.Add("**\u062a\u0648\u062c\u0647:** \u0628\u0631\u0627\u06cc \u0628\u062e\u0634\u06cc \u0627\u0632 \u062f\u0627\u062f\u0647\u200c\u0647\u0627 \u0645\u0639\u06cc\u0627\u0631 \u0642\u0627\u0628\u0644 \u0627\u062a\u06a9\u0627 \u062f\u0631 \u062f\u0633\u062a\u0631\u0633 \u0646\u06cc\u0633\u062a\u061b \u0628\u0646\u0627\u0628\u0631\u0627\u06cc\u0646 \u0622\u0646 \u0628\u062e\u0634 \u0642\u0627\u0628\u0644 \u0645\u0642\u0627\u06cc\u0633\u0647 \u0646\u06cc\u0633\u062a.");
        lines.Add("\u0627\u06cc\u0646 \u0627\u0637\u0644\u0627\u0639\u0627\u062a \u062a\u0648\u0635\u06cc\u0647 \u0628\u0647 \u0633\u0631\u0645\u0627\u06cc\u0647 \u06af\u0630\u0627\u0631\u06cc \u0646\u06cc\u0633\u062a");
        return string.Join(Environment.NewLine, lines);
    }

    private static decimal? Benchmark(IndustryRelativeValuationReadModel model, string metricKind) =>
        model.Benchmarks.FirstOrDefault(metric => metric.MetricKind.Equals(metricKind, StringComparison.OrdinalIgnoreCase))?.Percent;

    private static string ClassificationPersian(RelativeValuationMetricReadModel metric)
    {
        if (metric.Percent is null || metric.BenchmarkValue is null)
            return "\u0642\u0627\u0628\u0644 \u0645\u0642\u0627\u06cc\u0633\u0647 \u0646\u06cc\u0633\u062a";
        if (metric.IsOutlier)
            return "\u062f\u0627\u062f\u0647 \u067e\u0631\u062a\u061b \u062f\u0631 \u0645\u0639\u06cc\u0627\u0631 \u06af\u0631\u0648\u0647 \u0644\u062d\u0627\u0638 \u0646\u0634\u062f\u0647";
        return metric.Classification switch
        {
            "Green" => "\u0645\u0637\u0644\u0648\u0628\u200c\u062a\u0631 \u0627\u0632 \u0645\u0639\u06cc\u0627\u0631 \u06af\u0631\u0648\u0647",
            "Red" => "\u0628\u0627\u0644\u0627\u062a\u0631 \u0627\u0632 \u0645\u0639\u06cc\u0627\u0631 \u06af\u0631\u0648\u0647",
            _ => "\u0642\u0627\u0628\u0644 \u0645\u0642\u0627\u06cc\u0633\u0647 \u0646\u06cc\u0633\u062a"
        };
    }

    private static string FormatPercent(decimal? value) =>
        value is null ? "\u2014" : $"{FormatPersianNumber(value.Value)}\u066a";

    private static string FormatPersianNumber(decimal value) =>
        ToPersianDigits(value.ToString("0.##", CultureInfo.InvariantCulture)).Replace('.', '\u066b');

    private static string FormatPersianInteger(int value) =>
        ToPersianDigits(value.ToString(CultureInfo.InvariantCulture));

    private static string FormatRank(int? rank, int total) =>
        rank is null ? "\u0628\u062f\u0648\u0646 \u0631\u062a\u0628\u0647" : $"{FormatPersianInteger(rank.Value)} \u0627\u0632 {FormatPersianInteger(total)}";

    private static string FormatJalaliDate(DateTimeOffset value) =>
        ToPersianDigits(ShamsiMonthCalculator.FormatJalaliDate(value));

    private static string PublicationStatusPersian(string status) => status switch
    {
        "Published" => "\u0645\u062d\u0627\u0633\u0628\u0647 \u0645\u0646\u062a\u0634\u0631\u0634\u062f\u0647 \u0648 \u0642\u0627\u0628\u0644 \u0627\u0633\u062a\u0641\u0627\u062f\u0647",
        "Inconclusive" => "\u0645\u062d\u0627\u0633\u0628\u0647 \u0642\u0637\u0639\u06cc \u0646\u0634\u062f\u0647",
        _ => "\u0648\u0636\u0639\u06cc\u062a \u062f\u0627\u062f\u0647 \u0646\u0627\u0645\u0634\u062e\u0635"
    };

    private static string ToPersianDigits(string value) =>
        string.Concat(value.Select(character => character is >= '0' and <= '9'
            ? "\u06f0\u06f1\u06f2\u06f3\u06f4\u06f5\u06f6\u06f7\u06f8\u06f9"[character - '0']
            : character));

    private static string MetricEnglish(RelativeValuationMetricReadModel metric)
    {
        if (metric.Percent is null)
            return $"unavailable ({metric.Reason})";
        var benchmark = metric.BenchmarkValue is null
            ? $"benchmark unavailable ({metric.InsufficientBenchmarkReason})"
            : $"benchmark {metric.BenchmarkValue:0.##}";
        var outlier = metric.IsOutlier ? $"; outlier: {metric.Reason}" : string.Empty;
        return $"{metric.Percent:0.##}% vs {benchmark}; status {metric.Classification}{outlier}";
    }

    private static string MetricPersian(RelativeValuationMetricReadModel metric)
    {
        if (metric.Percent is null)
            return $"\u062f\u0631 \u062f\u0633\u062a\u0631\u0633 \u0646\u06cc\u0633\u062a ({metric.Reason})";
        var benchmark = metric.BenchmarkValue is null
            ? $"\u0628\u0646\u0686\u0645\u0627\u0631\u06a9 \u0646\u0627\u0645\u0634\u062e\u0635 ({metric.InsufficientBenchmarkReason})"
            : $"\u0628\u0646\u0686\u0645\u0627\u0631\u06a9 {metric.BenchmarkValue:0.##}";
        var outlier = metric.IsOutlier ? $"; \u067e\u0631\u062a: {metric.Reason}" : string.Empty;
        return $"{metric.Percent:0.##}% \u062f\u0631 \u0645\u0642\u0627\u0628\u0644 {benchmark} | \u0648\u0636\u0639\u06cc\u062a {metric.Classification}{outlier}";
    }
}
