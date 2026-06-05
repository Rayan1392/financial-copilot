using System.Xml.Linq;

namespace FinancialCopilot.ArchitectureTests;

public sealed class CleanArchitectureDependencyTests
{
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> AllowedReferences =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["FinancialCopilot.API"] = new HashSet<string>(["FinancialCopilot.Application", "FinancialCopilot.Billing", "FinancialCopilot.Infrastructure"]),
            ["FinancialCopilot.Application"] = new HashSet<string>(["FinancialCopilot.Domain"]),
            ["FinancialCopilot.Domain"] = new HashSet<string>(),
            ["FinancialCopilot.Billing"] = new HashSet<string>(["FinancialCopilot.Domain"]),
            ["FinancialCopilot.Infrastructure"] = new HashSet<string>(["FinancialCopilot.Application", "FinancialCopilot.Billing", "FinancialCopilot.Domain"]),
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

    [Fact]
    public void ScannerAndOrchestratorCode_DoesNotEmbedMetricFormulaRoutingCodes()
    {
        var applicationRoot = Path.Combine(FindSolutionRoot(), "FinancialCopilot.Application");
        var governedFormulaCodes = new[] { "NET_PROFIT_GROWTH_YOY", "NET_PROFIT_GROWTH_QOQ", "PE_TTM" };
        var failures = Directory
            .EnumerateFiles(applicationRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path =>
                Path.GetFileName(path).Contains("Scanner", StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(path).Contains("Orchestrator", StringComparison.OrdinalIgnoreCase))
            .SelectMany(path => governedFormulaCodes
                .Where(code => File.ReadAllText(path).Contains(code, StringComparison.Ordinal))
                .Select(code => $"{Path.GetFileName(path)} must resolve '{code}' through semantic contracts."))
            .ToArray();

        Assert.Empty(failures);
    }

    [Fact]
    public void MicrosoftAgentFrameworkPackages_OnlyReferencedFromInfrastructure()
    {
        // MAF packages must not appear as NuGet PackageReferences in Domain, Application, Billing, or API.
        // Their presence outside Infrastructure would couple business policy to a volatile vendor framework.
        var root = FindSolutionRoot();
        var mafPackagePrefixes = new[] { "Microsoft.Agents.", "Microsoft.Agents.AI" };
        var protectedProjects = new[]
        {
            "FinancialCopilot.Domain",
            "FinancialCopilot.Application",
            "FinancialCopilot.Billing",
            "FinancialCopilot.API"
        };

        var failures = protectedProjects
            .Select(name => Path.Combine(root, name, $"{name}.csproj"))
            .Where(File.Exists)
            .SelectMany(path =>
            {
                var doc = XDocument.Load(path);
                return doc.Descendants("PackageReference")
                    .Select(r => r.Attribute("Include")?.Value)
                    .Where(pkg => pkg is not null &&
                        mafPackagePrefixes.Any(prefix =>
                            pkg!.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                    .Select(pkg => $"{Path.GetFileNameWithoutExtension(path)} must not directly reference MAF package '{pkg}'.");
            })
            .ToArray();

        Assert.Empty(failures);
    }

    [Fact]
    public void BusinessAndPublicContractAssemblies_DoNotReferenceVendorModelProviders()
    {
        var root = FindSolutionRoot();
        var protectedRoots = new[]
        {
            Path.Combine(root, "FinancialCopilot.Domain"),
            Path.Combine(root, "FinancialCopilot.Billing"),
            Path.Combine(root, "FinancialCopilot.API", "Contracts")
        };
        var vendorTerms = new[] { "OpenAI", "Anthropic", "Claude", "Abravran", "Ollama" };
        var failures = protectedRoots
            .Where(Directory.Exists)
            .SelectMany(path => Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories))
            .SelectMany(path => vendorTerms
                .Where(term => File.ReadAllText(path).Contains(term, StringComparison.OrdinalIgnoreCase))
                .Select(term => $"{Path.GetFileName(path)} must not reference AI vendor '{term}'."))
            .ToArray();

        Assert.Empty(failures);
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
