namespace FinancialCopilot.ArchitectureTests;

/// <summary>
/// Feature 163 (T15, AC-17): the Symbol-Grounded Fallback policy stays free of delivery and persistence details, and
/// the frozen V1 orchestration path never references it (specs/POLICY-V1-FREEZE.md).
/// </summary>
public sealed class SymbolGroundedFallback163ArchitectureTests
{
    private static readonly string[] ForbiddenInPolicy =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "FinancialCopilot.Infrastructure",
        "System.Net.Http",
        "Microsoft.Agents"
    ];

    [Fact]
    public void FallbackPolicy_HasNoInfrastructureOrDeliveryDependencies()
    {
        var folder = Path.Combine(FindSolutionRoot(), "FinancialCopilot.Application", "AI", "SymbolGroundedFallback");
        var files = Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories).ToArray();
        Assert.NotEmpty(files);

        var failures = files
            .SelectMany(path => ForbiddenInPolicy
                .Where(token => File.ReadAllText(path).Contains(token, StringComparison.Ordinal))
                .Select(token => $"{Path.GetFileName(path)} must not depend on {token}."))
            .ToArray();

        Assert.Empty(failures);
    }

    [Fact]
    public void FrozenV1Orchestration_DoesNotReferenceTheFallback()
    {
        var v1 = Path.Combine(FindSolutionRoot(), "FinancialCopilot.Application", "AI", "Orchestration", "AiQueryOrchestrationService.cs");
        Assert.True(File.Exists(v1), v1);
        Assert.DoesNotContain("SymbolGroundedFallback", File.ReadAllText(v1), StringComparison.Ordinal);
    }

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
