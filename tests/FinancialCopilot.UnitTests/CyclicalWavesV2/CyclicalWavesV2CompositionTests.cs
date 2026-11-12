using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Insights;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Infrastructure;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWavesV2;
using FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWaves;
using FinancialCopilot.Infrastructure.Financial.Scanner;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.UnitTests.CyclicalWavesV2;

public sealed class CyclicalWavesV2CompositionTests
{
    // The 11 legacy endpoints inventoried in impact-analysis §1 (login included).
    public static TheoryData<string, string> LegacyEndpoints => new()
    {
        { nameof(CyclicalWavesDataProviderClient), "auth/login" },
        { nameof(CyclicalWavesDataProviderClient), "symbol/getAllSymbols" },
        { nameof(CyclicalWavesDataProviderClient), "symbol/getFinancialStatements" },
        { nameof(CyclicalWavesDataProviderClient), "symbol/getMonthlyReports" },
        { nameof(CyclicalWavesDataProviderClient), "symbol/getPS" },
        { nameof(CyclicalWavesDataProviderClient), "symbol/getLastPS" },
        { nameof(CyclicalWavesDataAcquisitionClient), "symbol/getPE" },
        { nameof(CyclicalWavesDataAcquisitionClient), "symbol/getLastPE" },
        { nameof(CyclicalWavesDataAcquisitionClient), "symbol/getEquilibrium" },
        { nameof(ComprehensiveAnalysisBlogClient), "blog/getComprehensiveAnalysis" },
        { nameof(CyclicalWavesDataProviderClient), "health" }
    };

    [Theory]
    [MemberData(nameof(LegacyEndpoints))]
    public async Task LegacyClients_AreBlockedBeforeAnyTransmission(string clientName, string path)
    {
        var network = new StubHttpHandler((_, _) => new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        using var provider = Build(ChatProvider: "NoavaranAmin", configure: services =>
        {
            foreach (var name in CyclicalWavesV2ServiceCollectionExtensions.LegacyCyclicalWavesClientNames)
                services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() => network);
        });

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(clientName);
        var exception = await Assert.ThrowsAsync<FinancialProviderException>(() =>
            client.GetAsync(new Uri(new Uri("https://back1.cyclicalwaves.com/api/"), path)));

        Assert.Equal(FinancialProviderErrorCode.ProviderRetired, exception.Code);
        Assert.Equal(0, network.Count);
    }

    [Fact]
    public async Task LegacyGuard_PassesThroughOnlyWhenExplicitlyAllowed()
    {
        var network = new StubHttpHandler((_, _) => new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        var guard = new LegacyCyclicalWavesCallBlockingHandler(
            "test",
            Options.Create(new CyclicalWavesLegacyOptions { AllowCalls = true }),
            TimeProvider.System,
            NullLogger<LegacyCyclicalWavesCallBlockingHandler>.Instance) { InnerHandler = network };

        using var response = await new HttpClient(guard).GetAsync("https://back1.cyclicalwaves.com/api/health");

        Assert.Equal(1, network.Count);
    }

    [Fact]
    public void NoavaranAminMode_KeepsExistingChatPortsAndDisablesComprehensiveAnalysis()
    {
        var services = Compose("NoavaranAmin");

        Assert.Equal(typeof(SwitchableMonthlyActivitySymbolMetricLookupService), Last<ISymbolMetricLookupService>(services).ImplementationType);
        Assert.Equal(typeof(EfCoreScannerExecutionService), Last<IScannerExecutionService>(services).ImplementationType);
        Assert.Equal(typeof(UnavailableComprehensiveAnalysisQueryUseCase), Last<IComprehensiveAnalysisQueryUseCase>(services).ImplementationType);
        Assert.NotEqual(typeof(CyclicalWavesV2QuarterlyIncomeStatementUseCase), Last<IQuarterlyFinancialReportsQueryUseCase>(services).ImplementationType);
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(CyclicalWavesV2GaugeQueryService));
        Assert.Equal(typeof(FinancialCopilot.Infrastructure.AI.OrchestrationV2.Adapters.CanonicalQueryEntityResolver), Last<ICanonicalQueryEntityResolver>(services).ImplementationType);
    }

    [Fact]
    public void CyclicalWavesMode_SwapsOnlyTheChatPorts()
    {
        var services = Compose("CyclicalWaves");

        Assert.Equal(typeof(CyclicalWavesV2SymbolMetricLookupService), Last<ISymbolMetricLookupService>(services).ImplementationType);
        Assert.Equal(typeof(CyclicalWavesV2ScannerExecutionService), Last<IScannerExecutionService>(services).ImplementationType);
        Assert.Equal(typeof(CyclicalWavesV2ComprehensiveAnalysisQuery), Last<IComprehensiveAnalysisQueryUseCase>(services).ImplementationType);
        Assert.Equal(typeof(CyclicalWavesV2QuarterlyIncomeStatementUseCase), Last<IQuarterlyFinancialReportsQueryUseCase>(services).ImplementationType);
        Assert.Equal(typeof(CyclicalWavesV2FinancialStatementTableQueryUseCase), Last<IFinancialStatementTableQueryUseCase>(services).ImplementationType);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(CyclicalWavesV2GaugeQueryService));
        Assert.Equal(typeof(CyclicalWavesV2PsGaugeVisualizationUseCase), Last<IPsVisualizationExperienceUseCase>(services).ImplementationType);
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(CyclicalWavesV2CanonicalEntityResolver));
        // The company resolver used by ingestion is never swapped.
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(ICompanyResolverService) &&
                                                      descriptor.ImplementationType?.Namespace?.Contains("CyclicalWavesV2") == true);
    }

    [Theory]
    [InlineData("NoavaranAmin")]
    [InlineData("CyclicalWaves")]
    public void LegacyAnalysisDetectorIsUnregisteredInBothModes(string mode) =>
        Assert.DoesNotContain(Compose(mode), descriptor =>
            descriptor.ServiceType == typeof(IInsightDetector) &&
            descriptor.ImplementationType?.Name == "ComprehensiveAnalysisPublishedDetector");

    [Theory]
    [InlineData("NoavaranAmin", FinancialDataChatProvider.NoavaranAmin)]
    [InlineData("CyclicalWaves", FinancialDataChatProvider.CyclicalWaves)]
    public void AvailabilityPolicyFollowsConfiguration(string mode, FinancialDataChatProvider expected)
    {
        using var provider = Build(mode);

        Assert.Equal(expected, provider.GetRequiredService<IChatCapabilityAvailability>().Provider);
    }

    [Fact]
    public void Feature159_NoavaranAminWithAnalysisMapped_SwapsOnlyComprehensiveAnalysis()
    {
        var unmapped = Compose("NoavaranAmin");
        var mapped = Compose("NoavaranAmin", ("FinancialData:CapabilitySources:comprehensive_analysis", "CyclicalWaves"));

        Assert.Equal(typeof(CyclicalWavesV2ComprehensiveAnalysisQuery), Last<IComprehensiveAnalysisQueryUseCase>(mapped).ImplementationType);
        Assert.Equal(typeof(SwitchableMonthlyActivitySymbolMetricLookupService), Last<ISymbolMetricLookupService>(mapped).ImplementationType);
        Assert.Equal(typeof(EfCoreScannerExecutionService), Last<IScannerExecutionService>(mapped).ImplementationType);
        Assert.DoesNotContain(mapped, descriptor => descriptor.ServiceType == typeof(CyclicalWavesV2GaugeQueryService));
        Assert.Equal(typeof(FinancialCopilot.Infrastructure.AI.OrchestrationV2.Adapters.CanonicalQueryEntityResolver), Last<ICanonicalQueryEntityResolver>(mapped).ImplementationType);
        // Every other Chat port is registered exactly as without the map.
        foreach (var serviceType in new[]
                 {
                     typeof(IQuarterlyFinancialReportsQueryUseCase), typeof(IFinancialStatementTableQueryUseCase),
                     typeof(IPsVisualizationExperienceUseCase), typeof(ISymbolMetricLookupService), typeof(IScannerExecutionService),
                     typeof(ICanonicalQueryEntityResolver)
                 })
        {
            Assert.Equal(
                unmapped.Last(descriptor => descriptor.ServiceType == serviceType).ImplementationType,
                mapped.Last(descriptor => descriptor.ServiceType == serviceType).ImplementationType);
        }
    }

    [Fact]
    public void Feature159_MappedAvailabilityAnswersAnalysisFromCyclicalWavesAndKeepsTheBaseMode()
    {
        using var provider = Build("NoavaranAmin", configure: null,
            ("FinancialData:CapabilitySources:comprehensive_analysis", "CyclicalWaves"));
        var availability = provider.GetRequiredService<IChatCapabilityAvailability>();

        Assert.Equal(FinancialDataChatProvider.NoavaranAmin, availability.Provider);
        Assert.Equal(FinancialDataChatProvider.CyclicalWaves, availability.SourceFor(CapabilityExecutionRoutes.ComprehensiveAnalysis));
        Assert.True(availability.Evaluate(CapabilityExecutionRoutes.ComprehensiveAnalysis).IsAvailable);
        Assert.Equal(FinancialDataChatProvider.NoavaranAmin, availability.SourceFor(CapabilityExecutionRoutes.SymbolLookup));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Feature159_EmptyMapValueMeansUnmapped(string value)
    {
        var services = Compose("NoavaranAmin", ("FinancialData:CapabilitySources:comprehensive_analysis", value));

        Assert.Equal(typeof(UnavailableComprehensiveAnalysisQueryUseCase), Last<IComprehensiveAnalysisQueryUseCase>(services).ImplementationType);
    }

    [Theory]
    [InlineData("NoavaranAmin", "symbol_lookup", "CyclicalWaves", "cannot be overridden")]
    [InlineData("NoavaranAmin", "comprehensive_analysis", "Codal", "unknown provider")]
    [InlineData("NoavaranAmin", "comprehensive_analysis", "1", "unknown provider")]
    [InlineData("CyclicalWaves", "comprehensive_analysis", "CyclicalWaves", "only while FinancialData:ChatProvider is NoavaranAmin")]
    public void Feature159_InvalidMapFailsComposition(string mode, string route, string source, string message)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            Compose(mode, ($"FinancialData:CapabilitySources:{route}", source)));

        Assert.Contains(message, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://tahlilapp.com/app/analysis")]
    [InlineData("app/analysis")]
    public void Feature159_AnalysesLinkMustBeAbsoluteHttps(string url)
    {
        using var provider = Build("NoavaranAmin", configure: null, ("ComprehensiveAnalysis:TahlilAppAnalysesUrl", url));

        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<ComprehensiveAnalysisPresentationOptions>>().Value);
    }

    [Fact]
    public void Feature159_AnalysesLinkDefaultsToTheTahlilAppAnalysisPage()
    {
        using var provider = Build("NoavaranAmin");

        Assert.Equal("https://tahlilapp.com/app/analysis",
            provider.GetRequiredService<IOptions<ComprehensiveAnalysisPresentationOptions>>().Value.TahlilAppAnalysesUrl);
    }

    private static ServiceDescriptor Last<T>(IServiceCollection services) => services.Last(descriptor => descriptor.ServiceType == typeof(T));

    private static IServiceCollection Compose(string chatProvider, params (string Key, string? Value)[] extra)
    {
        var configuration = Configuration(chatProvider, extra);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.AddFinancialCopilotInfrastructure(configuration);
        return services;
    }

    private static ServiceProvider Build(string ChatProvider, Action<IServiceCollection>? configure = null, params (string Key, string? Value)[] extra)
    {
        var services = Compose(ChatProvider, extra);
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private static IConfiguration Configuration(string chatProvider, (string Key, string? Value)[] extra)
    {
        var values = new Dictionary<string, string?>
        {
            ["ConnectionStrings:FinancialCopilot"] = "Host=localhost;Database=fake",
            ["FinancialData:ChatProvider"] = chatProvider
        };
        foreach (var (key, value) in extra)
        {
            values[key] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
