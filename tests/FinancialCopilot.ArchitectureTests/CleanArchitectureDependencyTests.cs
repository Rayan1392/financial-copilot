using System.Xml.Linq;

namespace FinancialCopilot.ArchitectureTests;

public sealed class CleanArchitectureDependencyTests
{
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> AllowedReferences =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["FinancialCopilot.API"] = new HashSet<string>(["FinancialCopilot.Application", "FinancialCopilot.Infrastructure"]),
            ["FinancialCopilot.Application"] = new HashSet<string>(["FinancialCopilot.Domain"]),
            ["FinancialCopilot.Domain"] = new HashSet<string>(),
            ["FinancialCopilot.Infrastructure"] = new HashSet<string>(["FinancialCopilot.Application", "FinancialCopilot.Domain"]),
            ["FinancialCopilot.Worker"] = new HashSet<string>(["FinancialCopilot.Application", "FinancialCopilot.Infrastructure"])
        };

    [Fact]
    public void ProductionProjectReferences_RespectCleanArchitectureBoundaries()
    {
        var root = FindSolutionRoot();
        var failures = new List<string>();

        foreach (var (projectName, allowedDependencies) in AllowedReferences)
        {
            var projectPath = Path.Combine(root, projectName, $"{projectName}.csproj");
            var projectDocument = XDocument.Load(projectPath);
            var references = projectDocument
                .Descendants("ProjectReference")
                .Select(reference => Path.GetFileNameWithoutExtension(reference.Attribute("Include")!.Value))
                .Where(AllowedReferences.ContainsKey);

            foreach (var reference in references)
            {
                if (!allowedDependencies.Contains(reference))
                {
                    failures.Add($"{projectName} must not reference {reference}.");
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static string FindSolutionRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var solutionRoot = Path.Combine(directory.FullName, "src", "backend");

            if (File.Exists(Path.Combine(solutionRoot, "FinancialCopilot.sln")))
            {
                return solutionRoot;
            }
        }

        throw new DirectoryNotFoundException("Could not find src/backend/FinancialCopilot.sln from the test output directory.");
    }
}
