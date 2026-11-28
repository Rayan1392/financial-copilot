using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.CyclicalWavesV2;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.ValuationGauges;
using FinancialCopilot.Application.FinancialData.Insights;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWavesV2;
using FinancialCopilot.Infrastructure.Financial.Insights;
using FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWaves;
using FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWavesV2;
using FinancialCopilot.Infrastructure.Financial.Scanner;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure;

/// <summary>
/// Feature 148 composition: the CyclicalWaves v2 provider, the legacy retirement guard, and the Chat
/// provider selection. Called last by <see cref="ServiceCollectionExtensions.AddFinancialCopilotInfrastructure"/>
/// so the CyclicalWaves-mode registrations replace the NoavaranAmin defaults.
/// </summary>
public static class CyclicalWavesV2ServiceCollectionExtensions
{
    /// <summary>Typed-client names of every legacy CyclicalWaves (back1.cyclicalwaves.com) HTTP client.</summary>
    public static readonly IReadOnlyList<string> LegacyCyclicalWavesClientNames =
    [
        nameof(CyclicalWavesDataProviderClient),
        nameof(CyclicalWavesDataAcquisitionClient),
        nameof(ComprehensiveAnalysisBlogClient)
    ];

    public static IServiceCollection AddCyclicalWavesV2AndProviderSelection(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var financialData = configuration.GetSection(FinancialDataOptions.SectionName).Get<FinancialDataOptions>()
                            ?? new FinancialDataOptions();
        services.AddOptions<FinancialDataOptions>()
            .BindConfiguration(FinancialDataOptions.SectionName)
            .Validate(options => Enum.IsDefined(options.ChatProvider), "FinancialData:ChatProvider must be NoavaranAmin or CyclicalWaves.")
            .ValidateOnStart();
        // Feature 159: an invalid capability source map fails composition, so the host never starts with it.
        var capabilitySources = ChatCapabilitySourceMap.Create(financialData);
        var relativeValuationEnabled = configuration.GetValue<bool>(
            $"{IndustryRelativeValuationOptions.SectionName}:CyclicalWavesV2Enabled");
        // Feature 159 change «standalone-valuation-gauge-cards»: off by default; read-only over Feature 148 snapshots.
        services.AddOptions<ValuationGaugeCardsOptions>()
            .BindConfiguration(ValuationGaugeCardsOptions.SectionName)
            .Validate(options => options.IsValid(out _), "ValuationGaugeCards settings are invalid: MaxFetchAgeHours 1-720, ReadTimeoutMilliseconds and QuoteTimeoutMilliseconds 100-30000, PercentageDecimals 0-3; ForwardEstimate: 0 < MinGrowthFactor < MaxGrowthFactor <= 20, MaxMonthlyReportAgeDays 1-365, ReadTimeoutMilliseconds 100-30000, no empty ExcludedIndustryExternalIds.")
            .ValidateOnStart();
        var valuationGaugeCardsEnabled = configuration.GetValue<bool>($"{ValuationGaugeCardsOptions.SectionName}:Enabled");
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<ValuationGaugeCardsOptions>>().Value);
        services.AddScoped<IValuationGaugeSnapshotReader, ValuationGaugeSnapshotReader>();
        services.AddScoped<IValuationGaugeQuoteReader, ValuationGaugeQuoteReader>();
        services.AddSingleton<IValuationGaugeObserver, ValuationGaugeTelemetry>();
        // Change «valuation-gauge-forward-ratio»: the estimator input reader exists only when the estimate is enabled;
        // the use case takes it as an optional dependency, so the flag-off path is today's.
        if (configuration.GetValue<bool>($"{ValuationGaugeCardsOptions.SectionName}:ForwardEstimate:Enabled"))
            services.AddScoped<IForwardRatioInputsReader, ForwardRatioInputsReader>();
        services.AddScoped<IValuationGaugeCardsUseCase, ValuationGaugeCardsUseCase>();
        services.AddScoped<IConversationalCapabilityExecutor, ValuationGaugeCapabilityExecutor>();
        // Feature 168: option comparison answers; off by default, read-only over Feature 167 data.
        services.AddOptions<FinancialCopilot.Application.FinancialData.Options.OptionAnswersOptions>()
            .BindConfiguration(FinancialCopilot.Application.FinancialData.Options.OptionAnswersOptions.SectionName)
            .Validate(options => options.IsValid(out _), "OptionAnswers settings are invalid: fee rates 0-0.05, AnnualizationFloorDays 0-365, MaxExpiries 1-12, RiskFreeAnnualRate 0-5, VolatilityWindowDays 20-500, TradingDaysPerYear 200-366.")
            .ValidateOnStart();
        var optionAnswersEnabled = configuration.GetValue<bool>(
            $"{FinancialCopilot.Application.FinancialData.Options.OptionAnswersOptions.SectionName}:Enabled");
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<FinancialCopilot.Application.FinancialData.Options.OptionAnswersOptions>>().Value);
        services.AddScoped<FinancialCopilot.Application.FinancialData.Options.IOptionMarketSnapshotReader>(provider =>
            (FinancialCopilot.Infrastructure.Financial.Options.EfCoreOptionChainReader)provider
                .GetRequiredService<FinancialCopilot.Application.FinancialData.Options.IOptionChainReader>());
        services.AddScoped<FinancialCopilot.Application.FinancialData.Options.IOptionComparisonUseCase,
            FinancialCopilot.Application.FinancialData.Options.OptionComparisonUseCase>();
        services.AddScoped<IConversationalCapabilityExecutor, OptionComparisonCapabilityExecutor>();
        services.AddSingleton<IChatCapabilityAvailability>(new ChatCapabilityAvailability(
            financialData.ChatProvider, capabilitySources, relativeValuationEnabled, valuationGaugeCardsEnabled,
            optionAnswersEnabled));
        services.AddScoped<CyclicalWavesV2RelativeValuationInputReader>();
        services.AddOptions<ComprehensiveAnalysisPresentationOptions>()
            .BindConfiguration(ComprehensiveAnalysisPresentationOptions.SectionName)
            .Validate(options => options.IsValid(out _), "ComprehensiveAnalysis settings are invalid: TahlilAppAnalysesUrl must be absolute https, TeaserPercent 1-50, TeaserMaxCharacters 20-1000, TeaserMinimumSourceCharacters 0-2000, MaxAgeDays 1-365.")
            .ValidateOnStart();

        AddLegacyRetirement(services);
        AddProvider(services);
        AddIngestion(services);

        // Chat provider selection: NoavaranAmin registrations stay untouched except C2.
        if (financialData.ChatProvider == FinancialDataChatProvider.CyclicalWaves)
        {
            services.AddScoped<CyclicalWavesV2GaugeQueryService>();
            services.AddScoped<CyclicalWavesV2CanonicalEntityResolver>();
            services.Replace(ServiceDescriptor.Scoped<ICanonicalQueryEntityResolver>(provider =>
                provider.GetRequiredService<CyclicalWavesV2CanonicalEntityResolver>()));
            services.Replace(ServiceDescriptor.Scoped<ISymbolMetricLookupService, CyclicalWavesV2SymbolMetricLookupService>());
            services.Replace(ServiceDescriptor.Scoped<IQuarterlyFinancialReportsQueryUseCase, CyclicalWavesV2QuarterlyIncomeStatementUseCase>());
            services.Replace(ServiceDescriptor.Scoped<IFinancialStatementTableQueryUseCase, CyclicalWavesV2FinancialStatementTableQueryUseCase>());
            services.Replace(ServiceDescriptor.Scoped<IComprehensiveAnalysisQueryUseCase, CyclicalWavesV2ComprehensiveAnalysisQuery>());
            services.Replace(ServiceDescriptor.Scoped<IScannerExecutionService, CyclicalWavesV2ScannerExecutionService>());
            services.Replace(ServiceDescriptor.Scoped<IPsVisualizationExperienceUseCase, CyclicalWavesV2PsGaugeVisualizationUseCase>());
        }
        else if (capabilitySources.TryGetSource(CapabilityExecutionRoutes.ComprehensiveAnalysis, out var analysisSource) &&
                 analysisSource == FinancialDataChatProvider.CyclicalWaves)
        {
            // Feature 159: comprehensive analysis alone reads the v2 analysis tables; every other NoavaranAmin
            // registration stays untouched.
            services.Replace(ServiceDescriptor.Scoped<IComprehensiveAnalysisQueryUseCase, CyclicalWavesV2ComprehensiveAnalysisQuery>());
        }
        else
        {
            // C2: no comprehensive-analysis table (legacy or v2) is readable from Chat in NoavaranAmin mode.
            services.Replace(ServiceDescriptor.Scoped<IComprehensiveAnalysisQueryUseCase, UnavailableComprehensiveAnalysisQueryUseCase>());
        }

        // The legacy published-analysis detector reads only retired tables; historical insight events stay.
        RemoveImplementation<IInsightDetector, ComprehensiveAnalysisPublishedDetector>(services);
        DecorateWithRecencyWindow(services);
        return services;
    }

    /// <summary>
    /// Change «analysis-recency-window»: whichever analysis use case the provider selection chose, every caller (semantic
    /// executor, agent tools, Telegram, V1) reads it through the recent-window rule.
    /// </summary>
    private static void DecorateWithRecencyWindow(IServiceCollection services)
    {
        var selected = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(IComprehensiveAnalysisQueryUseCase));
        if (selected?.ImplementationType is not { } innerType)
            throw new InvalidOperationException("The comprehensive analysis use case must be registered by type before the recency window is applied.");
        // The selected implementation stays registered as itself, so composition stays inspectable.
        services.TryAdd(ServiceDescriptor.Scoped(innerType, innerType));
        services.Replace(ServiceDescriptor.Scoped<IComprehensiveAnalysisQueryUseCase>(provider =>
            new RecentComprehensiveAnalysisQueryUseCase(
                (IComprehensiveAnalysisQueryUseCase)provider.GetRequiredService(innerType),
                provider.GetRequiredService<IOptions<ComprehensiveAnalysisPresentationOptions>>().Value,
                provider.GetService<TimeProvider>() ?? TimeProvider.System)));
    }

    private static void AddLegacyRetirement(IServiceCollection services)
    {
        services.AddOptions<CyclicalWavesLegacyOptions>().BindConfiguration(CyclicalWavesLegacyOptions.SectionName);
        services.TryAddSingleton(TimeProvider.System);
        foreach (var clientName in LegacyCyclicalWavesClientNames)
        {
            // Index 0: the guard runs before the legacy auth handler, so not even the login leaves the process.
            services.AddHttpClient(clientName).ConfigureAdditionalHttpMessageHandlers((handlers, provider) =>
                handlers.Insert(0, new LegacyCyclicalWavesCallBlockingHandler(
                    clientName,
                    provider.GetRequiredService<IOptions<CyclicalWavesLegacyOptions>>(),
                    provider.GetRequiredService<TimeProvider>(),
                    provider.GetRequiredService<ILogger<LegacyCyclicalWavesCallBlockingHandler>>())));
        }
    }

    private static void AddProvider(IServiceCollection services)
    {
        services.AddOptions<CyclicalWavesV2Options>()
            .BindConfiguration(CyclicalWavesV2Options.SectionName)
            .Validate(options => options.IsValid(out _), "CyclicalWavesV2 options are invalid; see CyclicalWavesV2Options for the allowed ranges.")
            .ValidateOnStart();
        services.AddSingleton<CyclicalWavesV2TokenService>();
        services.AddTransient<CyclicalWavesV2AuthHandler>();
        services.AddTransient<CyclicalWavesV2ResilienceHandler>();

        services.AddHttpClient(CyclicalWavesV2TokenService.AuthHttpClientName, (provider, client) =>
            {
                client.BaseAddress = new Uri(provider.GetRequiredService<IOptions<CyclicalWavesV2Options>>().Value.AuthBaseAddress);
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
            .AddHttpMessageHandler<CyclicalWavesV2ResilienceHandler>();

        services.AddHttpClient<ICyclicalWavesV2Client, CyclicalWavesV2DataClient>((provider, client) =>
            {
                client.BaseAddress = new Uri(provider.GetRequiredService<IOptions<CyclicalWavesV2Options>>().Value.DataBaseAddress);
                client.Timeout = Timeout.InfiniteTimeSpan;
                client.MaxResponseContentBufferSize = 16 * 1024 * 1024;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
            .AddHttpMessageHandler<CyclicalWavesV2AuthHandler>()
            .AddHttpMessageHandler<CyclicalWavesV2ResilienceHandler>();

        services.AddHttpClient<ICyclicalWavesV2GaugeClient, CyclicalWavesV2GaugeDataClient>((provider, client) =>
            {
                client.BaseAddress = new Uri(provider.GetRequiredService<IOptions<CyclicalWavesV2Options>>().Value.GaugeBaseUrl);
                client.Timeout = Timeout.InfiniteTimeSpan;
                client.MaxResponseContentBufferSize = 4 * 1024 * 1024;
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
            .AddHttpMessageHandler<CyclicalWavesV2AuthHandler>()
            .AddHttpMessageHandler<CyclicalWavesV2ResilienceHandler>();
    }

    private static void AddIngestion(IServiceCollection services)
    {
        services.AddScoped<ICyclicalWavesV2Store, EfCoreCyclicalWavesV2Store>();
        services.AddScoped<ICyclicalWavesV2AnalysisStore, EfCoreCyclicalWavesV2AnalysisStore>();
        services.AddScoped<ICyclicalWavesV2GaugeStore, EfCoreCyclicalWavesV2GaugeStore>();
        services.AddScoped<ICyclicalWavesV2SyncRunStore, EfCoreCyclicalWavesV2SyncRunStore>();
        services.AddScoped(provider =>
        {
            var options = provider.GetRequiredService<IOptions<CyclicalWavesV2Options>>().Value;
            return new CyclicalWavesV2SyncSettings(
                options.IncomeStatementLimit,
                options.AnalysisPageSize,
                options.AnalysisIncrementalOverlapDays,
                TimeSpan.FromMilliseconds(options.RequestDelayMilliseconds),
                TimeSpan.FromMinutes(options.LeaseMinutes));
        });
        services.AddScoped<ICyclicalWavesV2SyncCoordinator, CyclicalWavesV2SyncCoordinator>();
        // Change «intraday-gauge-sync-and-codal-trigger»: CodalMirror posts queue a gauge refresh of their symbol.
        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<CyclicalWavesV2Options>>().Value;
            return new CodalAnnouncementGaugeRefreshSettings(
                options.IngestionEnabled && options.CodalGaugeRefreshEnabled,
                TimeSpan.FromMinutes(options.CodalGaugeRecheckMinutes));
        });
        services.AddScoped<CodalAnnouncementGaugeRefresh>();
        services.AddSingleton<ICodalAnnouncementGaugeRefreshTrigger, ScopedCodalAnnouncementGaugeRefreshTrigger>();
        services.AddScoped<CyclicalWavesV2ReadModel>();
    }

    private static void RemoveImplementation<TService, TImplementation>(IServiceCollection services)
    {
        foreach (var descriptor in services
                     .Where(descriptor => descriptor.ServiceType == typeof(TService) &&
                                          descriptor.ImplementationType == typeof(TImplementation))
                     .ToList())
        {
            services.Remove(descriptor);
        }
    }
}
