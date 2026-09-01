using System.Xml.Linq;
using FinancialCopilot.Application.FinancialData.Providers;

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

    [Fact]
    public void NoavaranArchiveSource_IsNotDrivenByARecurringHostedWorker()
    {
        // Spec 051 AC #4/#5 and spec 052 AC #4: the Noavaran archive (CodalDB SQL) is a one-time
        // import source. No recurring hosted worker may drive it or its import coordinator; ordinary
        // recurring refresh belongs to the current API source. The archive sync/import is reachable
        // only through the explicit DataAdmin endpoints.
        var root = FindSolutionRoot();
        var workerProgram = File.ReadAllText(Path.Combine(root, "FinancialCopilot.Worker", "Program.cs"));

        var hostedServiceLines = workerProgram
            .Split('\n')
            .Where(line => line.Contains("AddHostedService", StringComparison.Ordinal))
            .ToArray();

        var failures = hostedServiceLines
            .Where(line =>
                line.Contains("CodalDb", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("NoavaranArchive", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("ArchiveScheduledSync", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("ArchiveImport", StringComparison.OrdinalIgnoreCase))
            .Select(line => $"Worker registers a recurring hosted service for the archive source: {line.Trim()}")
            .ToArray();

        Assert.Empty(failures);
    }

    [Fact]
    public void IndustryRelativeValuationRuntime_ConsumesPersistedCyclicalWavesSnapshots()
    {
        var root = FindSolutionRoot();
        var workerProgram = File.ReadAllText(Path.Combine(root, "FinancialCopilot.Worker", "Program.cs"));
        var registrations = File.ReadAllText(Path.Combine(
            root,
            "FinancialCopilot.Infrastructure",
            "ServiceCollectionExtensions.cs"));
        var acquisitionWorker = File.ReadAllText(Path.Combine(
            root,
            "FinancialCopilot.Worker",
            "CyclicalWavesDataAcquisitionWorker.cs"));
        var calculationWorker = File.ReadAllText(Path.Combine(
            root,
            "FinancialCopilot.Worker",
            "IndustryRelativeValuationCalculationWorker.cs"));

        Assert.Contains("AddHostedService<CyclicalWavesDataAcquisitionWorker>", workerProgram, StringComparison.Ordinal);
        Assert.Contains("AddHostedService<IndustryRelativeValuationCalculationWorker>", workerProgram, StringComparison.Ordinal);
        Assert.DoesNotContain("AddHostedService<CyclicalWavesRelativeValuationWorker>", workerProgram, StringComparison.Ordinal);
        Assert.Contains("ICyclicalWavesMetricSnapshotReader, CyclicalWavesMetricSnapshotReader", registrations, StringComparison.Ordinal);
        Assert.DoesNotContain("AddScoped<ICyclicalWavesRelativeValuationProviderClient>", registrations, StringComparison.Ordinal);
        Assert.DoesNotContain("AddScoped<IFeature126RelativeValuationPipeline>", registrations, StringComparison.Ordinal);
        Assert.DoesNotContain("AddScoped<IFeature125HandoffSubmissionBoundary>", registrations, StringComparison.Ordinal);
        Assert.DoesNotContain("AddScoped<IIndustryRelativeValuationSourceIngestionService>", registrations, StringComparison.Ordinal);
        Assert.DoesNotContain("IIndustryRelativeValuationOrchestrationService", acquisitionWorker, StringComparison.Ordinal);
        Assert.Contains("IIndustryRelativeValuationOrchestrationService", calculationWorker, StringComparison.Ordinal);
        Assert.DoesNotContain("CyclicalWavesTokenCache", calculationWorker, StringComparison.Ordinal);
        Assert.DoesNotContain("ICyclicalWavesDataAcquisitionService", calculationWorker, StringComparison.Ordinal);
        Assert.DoesNotContain("ICyclicalWavesRelativeValuationProviderClient", calculationWorker, StringComparison.Ordinal);
    }

    [Fact]
    public void StockMarketDb_IsModeledAsMigrationBridge()
    {
        // Spec 054 AC #2/#7: StockMarketDB must remain classified as MigrationBridge, not as
        // an archive or current-incremental source. The ProviderSources catalog is the single owner
        // of this classification; this test proves it has not drifted.
        var descriptor = ProviderSources.StockMarketDb;

        Assert.Equal(SourceMode.MigrationBridge, descriptor.DefaultMode);
        Assert.Equal(LogicalVendor.Tsetmc, descriptor.Vendor);
    }

    [Fact]
    public void ScannerCode_DoesNotReferenceStockMarketDbPhysicalSource()
    {
        // Spec 054 AC #10: the scanner must read canonical market projections (LatestMarketQuotes)
        // and must not be coupled to the StockMarketDB physical source name. If the scanner
        // contains a literal "StockMarketDb" it bypasses the abstraction layer.
        var root = FindSolutionRoot();
        var scannerRoot = Path.Combine(root, "FinancialCopilot.Application", "Scanner");
        var failures = Directory
            .EnumerateFiles(scannerRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains("StockMarketDb", StringComparison.OrdinalIgnoreCase))
            .Select(path => $"{Path.GetFileName(path)} must not reference the StockMarketDb physical source name directly.")
            .ToArray();

        Assert.Empty(failures);
    }

    [Fact]
    public void MigratedSemanticExecutors_DoNotReintroduceLegacySymbolExtraction()
    {
        var source = File.ReadAllText(Path.Combine(
            FindSolutionRoot(), "FinancialCopilot.Application", "AI", "Orchestration", "SemanticCapabilityExecutors.cs"));
        var trend = Slice(source, "public sealed class MonthlyActivityTrendCapabilityExecutor", "public sealed class SymbolMetricLookupCapabilityExecutor");
        var lookup = Slice(source, "public sealed class SymbolMetricLookupCapabilityExecutor", "public sealed class ProductRevenueMixCapabilityExecutor");

        Assert.DoesNotContain("MonthlyActivityTrendIntentRules", trend, StringComparison.Ordinal);
        Assert.DoesNotContain("ExtractCompanySymbol", trend, StringComparison.Ordinal);
        Assert.DoesNotContain("ISymbolLookupParser", lookup, StringComparison.Ordinal);
        Assert.DoesNotContain("IComprehensiveAnalysisQueryParser", source, StringComparison.Ordinal);
        Assert.Contains("QuerySlotType.CompanyOrSymbol", trend, StringComparison.Ordinal);
        Assert.Contains("QuerySlotType.CompanyOrSymbol", lookup, StringComparison.Ordinal);
        Assert.Contains("QuerySlotType.Metric", lookup, StringComparison.Ordinal);

        foreach (var legacyParser in new[]
                 {
                     "FinancialStatementTableIntentRules",
                     "FinancialStatementAnalysisIntentRules",
                     "DisclosureListingIntentRules",
                     "MonthlySalesQualityRankingIntentRules"
                 })
        {
            Assert.DoesNotContain(legacyParser, source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EveryProductionOrchestrator_CutsSemanticFramesOverBeforeLegacyRouting()
    {
        var root = FindSolutionRoot();
        var files = new[]
        {
            Path.Combine(root, "FinancialCopilot.Application", "AI", "Orchestration", "AiQueryOrchestrationService.cs"),
            Path.Combine(root, "FinancialCopilot.Infrastructure", "AI", "OrchestrationV2", "FinancialCopilotAgentWorkflowRunner.cs"),
            Path.Combine(root, "FinancialCopilot.Infrastructure", "AI", "OrchestrationV2", "Workflow", "FinancialCopilotWorkflowDefinition.cs")
        };

        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            var semanticBranch = source.IndexOf("SemanticFrame is", StringComparison.Ordinal);
            var semanticExecution = source.IndexOf("semanticExecutionCoordinator.ExecuteAsync", StringComparison.Ordinal);
            Assert.True(semanticBranch >= 0 && semanticExecution > semanticBranch,
                $"{Path.GetFileName(file)} must execute validated semantic frames through the coordinator.");
        }

        foreach (var file in files.Skip(1))
        {
            var source = File.ReadAllText(file);
            var semanticExecution = source.IndexOf("semanticExecutionCoordinator.ExecuteAsync", StringComparison.Ordinal);
            var modelResolution = source.IndexOf("ResolveModelClient(request)", StringComparison.Ordinal);
            Assert.True(modelResolution > semanticExecution,
                $"{Path.GetFileName(file)} must not require an AI model before deterministic semantic execution.");
        }
    }

    [Fact]
    public void Feature125SemanticPayload_IsHandledByBothV2ExecutionPaths()
    {
        var root = FindSolutionRoot();
        var workflow = File.ReadAllText(Path.Combine(root, "FinancialCopilot.Infrastructure", "AI", "OrchestrationV2", "Workflow", "FinancialCopilotWorkflowDefinition.cs"));
        var fallback = File.ReadAllText(Path.Combine(root, "FinancialCopilot.Infrastructure", "AI", "OrchestrationV2", "FinancialCopilotAgentWorkflowRunner.cs"));
        var capabilityCodes = new[]
        {
            "symbol_vs_industry_relative_valuation",
            "industry_relative_valuation_ranking",
            "industry_relative_valuation_summary",
            "symbol_pair_within_industry"
        };

        Assert.Contains("IndustryRelativeValuationPayload relative => relative.PresentationText", workflow, StringComparison.Ordinal);
        Assert.Contains("IndustryRelativeValuationPayload relative => relative.PresentationText", fallback, StringComparison.Ordinal);
        foreach (var capabilityCode in capabilityCodes)
        {
            Assert.Contains($"\"{capabilityCode}\"", workflow, StringComparison.Ordinal);
            Assert.Contains($"\"{capabilityCode}\"", fallback, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SemanticFeatureDocumentation_TracksVerifiedImplementationState()
    {
        var repositoryRoot = Path.GetFullPath(Path.Combine(FindSolutionRoot(), "..", ".."));
        var specsRoot = Path.Combine(repositoryRoot, "specs");
        var completedFeatures = new[]
        {
            "117-ai-dialogue-outcome-safety-and-localization",
            "118-conversational-capability-registry-and-query-frame",
            "119-canonical-query-entity-and-slot-resolution",
            "120-conversational-task-state-and-clarification-orchestration",
            "121-capability-guidance-and-suggested-actions"
        };

        foreach (var feature in completedFeatures)
        {
            var story = File.ReadAllText(Path.Combine(specsRoot, feature, "user-story.md"));
            var tasks = File.ReadAllText(Path.Combine(specsRoot, feature, "tasks.md"));
            Assert.Contains("`[x]`", story, StringComparison.Ordinal);
            Assert.DoesNotContain("## [ ] Task", tasks, StringComparison.Ordinal);
            Assert.DoesNotContain("## [~] Task", tasks, StringComparison.Ordinal);
        }

        var migrationTasks = File.ReadAllText(Path.Combine(
            specsRoot, "122-semantic-route-migration-and-legacy-retirement", "tasks.md"));
        var governanceTasks = File.ReadAllText(Path.Combine(
            specsRoot, "123-semantic-dialogue-evaluation-and-learning-governance", "tasks.md"));
        var evidence = File.ReadAllText(Path.Combine(
            specsRoot, "123-semantic-dialogue-evaluation-and-learning-governance", "implementation-evidence.md"));

        Assert.DoesNotContain("## [ ] Task", migrationTasks, StringComparison.Ordinal);
        Assert.Contains("## [~] Task 10", migrationTasks, StringComparison.Ordinal);
        Assert.DoesNotContain("## [ ] Task", governanceTasks, StringComparison.Ordinal);
        Assert.DoesNotContain("## [~] Task", governanceTasks, StringComparison.Ordinal);
        Assert.Contains("At least 24 hours", evidence, StringComparison.Ordinal);
        Assert.Contains("No synthetic production observation", evidence, StringComparison.Ordinal);
    }

    private static string Slice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, $"Could not locate semantic executor slice '{startMarker}'.");
        return source[start..end];
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
