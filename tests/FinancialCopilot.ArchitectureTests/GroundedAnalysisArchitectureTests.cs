namespace FinancialCopilot.ArchitectureTests;

/// <summary>
/// Change «sol-deep-analysis-web-search» (T11, AC-17, AC-18): the actual API cost is monitoring only and never reaches a
/// charge; prices and technical profiles have separate owners; the analysis policy stays free of delivery and
/// persistence details; cost telemetry never touches report values, the question or the answer.
/// </summary>
public sealed class GroundedAnalysisArchitectureTests
{
    private const string Telemetry = "GroundedAnalysisTelemetry";

    [Fact] // T11: no billing code reads the cost estimate.
    public void NoBillingPath_ReadsTheCostEstimate()
    {
        var root = FindSolutionRoot();
        var billingFiles = Directory.EnumerateFiles(Path.Combine(root, "FinancialCopilot.Billing"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "FinancialCopilot.Infrastructure", "Billing"), "*.cs", SearchOption.AllDirectories))
            .Where(path => !IsGenerated(path))
            .ToArray();
        Assert.NotEmpty(billingFiles);

        var failures = billingFiles
            .SelectMany(path => new[] { Telemetry, "EstimateUsd", "CostAlertUsd", "WebSearchCallUsd" }
                .Where(token => Code(path).Contains(token, StringComparison.Ordinal))
                .Select(token => $"{Path.GetFileName(path)} must not read {token}."))
            .ToArray();

        Assert.Empty(failures);
    }

    [Fact] // T11: recording the cost returns nothing, so no caller can turn it into a charge.
    public void CostTelemetry_ReturnsNothing_AndLogsNoText()
    {
        var code = Code(Path.Combine(FindSolutionRoot(), "FinancialCopilot.Infrastructure", "AI", "GroundedAnalysis", $"{Telemetry}.cs"));

        Assert.Contains("public void Record(", code, StringComparison.Ordinal);
        // AC-17: only identifiers, counts and amounts; never the question, the answer or report values.
        foreach (var token in new[] { "Question", ".Answer", ".Text", "Facts.Data", "Prompt" })
            Assert.DoesNotContain(token, code, StringComparison.Ordinal);
    }

    [Fact] // AC-18: prices and technical profiles do not read each other.
    public void PricesAndProfiles_HaveSeparateOwners()
    {
        var folder = Path.Combine(FindSolutionRoot(), "FinancialCopilot.Application", "AI", "GroundedAnalysis");
        var pricing = Code(Path.Combine(folder, "GroundedAnalysisPricing.cs"));
        var profiles = Code(Path.Combine(folder, "GroundedAnalysisOptions.cs"));

        foreach (var token in new[] { "GroundedAnalysisOptions", "GroundedAnalysisProfile", "MaxOutputTokens", "ReasoningEffort", "WebSearchCalls" })
            Assert.DoesNotContain(token, pricing, StringComparison.Ordinal);
        foreach (var token in new[] { "GroundedAnalysisPricing", "Credits" })
            Assert.DoesNotContain(token, profiles, StringComparison.Ordinal);
    }

    [Fact] // AC-18: the analysis policy has no infrastructure or delivery dependency.
    public void AnalysisPolicy_HasNoInfrastructureOrDeliveryDependencies()
    {
        var folder = Path.Combine(FindSolutionRoot(), "FinancialCopilot.Application", "AI", "GroundedAnalysis");
        var failures = Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .SelectMany(path => new[] { "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "FinancialCopilot.Infrastructure", "FinancialCopilot.Billing", "System.Net.Http" }
                .Where(token => Code(path).Contains(token, StringComparison.Ordinal))
                .Select(token => $"{Path.GetFileName(path)} must not depend on {token}."))
            .ToArray();

        Assert.Empty(failures);
    }

    [Fact] // Change «local-company-data-context» T23 (refinement R1): the data pack loader reads only through Application ports.
    public void DataPackLoader_DependsOnlyOnApplicationTypes()
    {
        var loader = typeof(FinancialCopilot.Application.AI.GroundedAnalysis.CompanyAnalysisDataPackLoader);
        var application = loader.Assembly;

        var constructor = Assert.Single(loader.GetConstructors());
        var failures = constructor.GetParameters()
            .Where(parameter => parameter.ParameterType != typeof(bool))
            .Where(parameter => parameter.ParameterType.Assembly != application ||
                                !(parameter.ParameterType.IsInterface || parameter.ParameterType.Name.EndsWith("Options", StringComparison.Ordinal)))
            .Select(parameter => $"{parameter.Name}: {parameter.ParameterType.FullName} is not an Application port or options type.")
            .ToArray();

        Assert.Empty(failures);
    }

    /// <summary>The file's code without comment lines, so rationale comments may name the other owner.</summary>
    private static string Code(string path) =>
        string.Join('\n', File.ReadAllLines(path).Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static bool IsGenerated(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string FindSolutionRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var solutionRoot = Path.Combine(directory.FullName, "src", "backend");
            if (File.Exists(Path.Combine(solutionRoot, "FinancialCopilot.sln"))) return solutionRoot;
        }

        throw new DirectoryNotFoundException("Could not find src/backend/FinancialCopilot.sln from the test output directory.");
    }
}
