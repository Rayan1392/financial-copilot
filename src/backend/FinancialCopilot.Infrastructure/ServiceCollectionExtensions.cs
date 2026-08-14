using System.Net.Http.Headers;
using FinancialCopilot.Billing.Contracts;
using FinancialCopilot.Billing.Pricing;
using FinancialCopilot.Billing.Services;
using FinancialCopilot.Application.AI.Evaluation;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Conversations;
using FinancialCopilot.Application.Memory;
using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.FinancialData.CodalAlerts;
using FinancialCopilot.Application.FinancialData.ConditionalTrackers;
using FinancialCopilot.Application.FinancialData.FollowedSymbols;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Insights;
using FinancialCopilot.Application.FinancialData.Radar;
using FinancialCopilot.Application.FinancialData.ProfessionalScanners;
using FinancialCopilot.Application.FinancialData.MarketViews;
using FinancialCopilot.Application.FinancialData.MarketReports;
using FinancialCopilot.Application.FinancialData.Metrics;
using FinancialCopilot.Application.FinancialData.Features;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Domain.Financial.Insights;
using FinancialCopilot.Domain.Financial.Insights.Microstructure;
using FinancialCopilot.Domain.Financial.RelativeValuation;
using FinancialCopilot.Domain.Financial.Services;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using FinancialCopilot.Infrastructure.Billing;
using FinancialCopilot.Application.AI.Observability;
using FinancialCopilot.Infrastructure.AI.ModelProviders;
using FinancialCopilot.Infrastructure.AI.Observability;
using FinancialCopilot.Infrastructure.Authentication;
using FinancialCopilot.Infrastructure.Authentication.Persistence;
using FinancialCopilot.Infrastructure.Conversations.Persistence;
using FinancialCopilot.Infrastructure.Memory;
using FinancialCopilot.Infrastructure.Memory.Persistence;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CodalDb;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;
using FinancialCopilot.Infrastructure.Financial.Providers;
using FinancialCopilot.Infrastructure.Financial.Providers.CodalDb;
using FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;
using FinancialCopilot.Infrastructure.Financial.Providers.StockMarketDb;
using FinancialCopilot.Infrastructure.Financial.Providers.Tsetmc;
using FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWaves;
using FinancialCopilot.Infrastructure.Financial.Providers.Persistence;
using FinancialCopilot.Application.FinancialData.FundPortfolio;
using FinancialCopilot.Infrastructure.Financial.FundPortfolio;
using FinancialCopilot.Infrastructure.Financial.Semantics.Persistence;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Insights;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Messaging;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Ingestion.StockMarketDb;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Tsetmc;
using FinancialCopilot.Infrastructure.Financial.CodalAlerts;
using FinancialCopilot.Infrastructure.Financial.ConditionalTrackers;
using FinancialCopilot.Infrastructure.Financial.FollowedSymbols;
using FinancialCopilot.Infrastructure.Financial.MarketViews;
using FinancialCopilot.Infrastructure.Financial.MarketReports;
using FinancialCopilot.Infrastructure.Financial.Radar;
using FinancialCopilot.Infrastructure.Financial.ProfessionalScanners;
using FinancialCopilot.Infrastructure.Financial.Features;
using FinancialCopilot.Infrastructure.Financial.Features.Messaging;
using FinancialCopilot.Infrastructure.Financial.Scanner;
using FinancialCopilot.Infrastructure.Financial.Semantics;
using FinancialCopilot.Infrastructure.Financial.Metadata;
using FinancialCopilot.Application.FinancialData.Metadata;
using FinancialCopilot.Application.Administration;
using FinancialCopilot.Application.Telegram;
using FinancialCopilot.Application.Notifications;
using FinancialCopilot.Infrastructure.Administration;
using FinancialCopilot.Infrastructure.Notifications;
using FinancialCopilot.Infrastructure.AI.Consistency;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Adapters;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Config;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Functions;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFinancialCopilotInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("FinancialCopilot") ??
            throw new InvalidOperationException("Connection string 'FinancialCopilot' is required.");

        services.AddDbContext<BillingDbContext>(options => options.UseNpgsql(connectionString));
        services.AddDbContext<AuthDbContext>(options => options.UseNpgsql(connectionString));
        services.AddDbContext<SemanticCatalogDbContext>(options => options.UseNpgsql(connectionString));
        services.AddDbContext<FinancialProviderDbContext>(options => options.UseNpgsql(connectionString));
        services.AddSingleton<IFundPortfolioValueNormalizer, FundPortfolioValueNormalizer>();
        services.AddScoped<IFundPortfolioWorkbookParser, XlsxFundPortfolioWorkbookParser>();
        services.AddOptions<FundPortfolioRawStorageOptions>()
            .BindConfiguration(FundPortfolioRawStorageOptions.SectionName)
            .Validate(options => options.MaximumFileBytes > 0 && !string.IsNullOrWhiteSpace(options.RootPath),
                "Fund portfolio raw storage options must define a positive file limit and root path.")
            .ValidateOnStart();
        services.AddScoped<IFundPortfolioRawWorkbookStore, FileSystemFundPortfolioRawWorkbookStore>();
        services.AddScoped<IInvestmentFundRepository, EfCoreInvestmentFundRepository>();
        services.AddScoped<IFundPortfolioReportRepository, EfCoreFundPortfolioReportRepository>();
        services.AddSingleton<IFundPortfolioIngestionTelemetrySink, LoggingFundPortfolioIngestionTelemetry>();
        services.AddSingleton<IFundPortfolioOperationalTelemetry, FundPortfolioOperationalTelemetry>();
        services.AddScoped<ICreateOrResolveInvestmentFundUseCase, CreateOrResolveInvestmentFundUseCase>();
        services.AddScoped<IIngestFundPortfolioWorkbookUseCase, IngestFundPortfolioWorkbookUseCase>();
        services.AddScoped<IGetFundPortfolioReportStatusUseCase, GetFundPortfolioReportStatusUseCase>();
        services.AddScoped<IGetFundPortfolioReportIssuesUseCase, GetFundPortfolioReportIssuesUseCase>();
        services.AddScoped<IFundPortfolioReportQueryRepository, EfCoreFundPortfolioReportQueryRepository>();
        services.AddScoped<IQueryFundPortfolioReportsUseCase, QueryFundPortfolioReportsUseCase>();
        services.AddScoped<IFundPortfolioRawWorkbookReader, FileSystemFundPortfolioRawWorkbookReader>();
        services.AddScoped<IFundPortfolioReportReprocessRepository, EfCoreFundPortfolioReportReprocessRepository>();
        services.AddScoped<IReprocessFundPortfolioReportUseCase, ReprocessFundPortfolioReportUseCase>();
        services.AddScoped<IFundPortfolioEquitySectionNormalizer, FundEquitySectionNormalizer>();
        services.AddScoped<IFundPortfolioSectionNormalizer>(provider => provider.GetRequiredService<IFundPortfolioEquitySectionNormalizer>());
        services.AddSingleton<IFundEquityNormalizationTelemetry, FundEquityNormalizationTelemetry>();
        services.AddSingleton<IFundEquityCorporateActionAdjustmentProvider, NoKnownFundEquityCorporateActionAdjustmentProvider>();
        services.AddScoped<IFundEquityPositionRepository, EfCoreFundEquityPositionRepository>();
        services.AddScoped<IFundPortfolioAnalyticsRepository, EfCoreFundPortfolioAnalyticsRepository>();
        services.AddScoped<IFundComparablePeriodReportReader, EfCoreFundComparablePeriodReportReader>();
        services.AddScoped<IFundComparablePeriodSelector, FundComparablePeriodSelector>();
        services.AddSingleton<IFundHoldingsActivityAnalyticsCalculator, FundHoldingsActivityAnalyticsCalculator>();
        services.AddScoped<IFundHoldingsActivityFactReader, EfCoreFundHoldingsActivityFactReader>();
        services.AddOptions<FundPortfolioMaterialityOptions>()
            .BindConfiguration(FundPortfolioMaterialityOptions.SectionName)
            .Validate(options => options.AbsoluteAmount >= 0m &&
                options.AssetWeightChangePercentagePoints >= 0m &&
                options.Percentile is >= 0m and <= 1m &&
                !string.IsNullOrWhiteSpace(options.Version),
                "Fund portfolio materiality thresholds must be non-negative, percentile must be between 0 and 1, and version is required.")
            .ValidateOnStart();
        services.AddScoped<FundHoldingsActivityAnalyticsService>();
        services.AddSingleton<IFundSectorStrategyAnalyticsCalculator, FundSectorStrategyAnalyticsCalculator>();
        services.AddScoped<IFundSectorStrategyFactReader, EfCoreFundSectorStrategyFactReader>();
        services.AddOptions<FundSectorStrategyOptions>()
            .BindConfiguration(FundSectorStrategyOptions.SectionName)
            .Validate(options => options.MinimumMeaningfulChangePercentagePoints >= 0m && !string.IsNullOrWhiteSpace(options.Version),
                "Fund strategy posture threshold and version are required.")
            .ValidateOnStart();
        services.AddScoped<FundSectorStrategyAnalyticsService>();
        services.AddSingleton<IFundTurnoverLiquidityAnalyticsCalculator, FundTurnoverLiquidityAnalyticsCalculator>();
        services.AddScoped<IFundTurnoverLiquidityFactReader, EfCoreFundTurnoverLiquidityFactReader>();
        services.AddOptions<FundTurnoverLiquidityOptions>()
            .BindConfiguration(FundTurnoverLiquidityOptions.SectionName)
            .Validate(options => options.ParticipationRate is > 0m and <= 1m &&
                options.VolumeLookbackDays > 0 && !string.IsNullOrWhiteSpace(options.Version),
                "Fund turnover/liquidity settings must have a valid participation rate, lookback, and version.")
            .ValidateOnStart();
        services.AddScoped<FundTurnoverLiquidityAnalyticsService>();
        services.AddSingleton<IFundDerivativeIncomeValuationAnalyticsCalculator, FundDerivativeIncomeValuationAnalyticsCalculator>();
        services.AddScoped<IFundDerivativeIncomeValuationFactReader, EfCoreFundDerivativeIncomeValuationFactReader>();
        services.AddOptions<FundDerivativeIncomeValuationOptions>()
            .BindConfiguration(FundDerivativeIncomeValuationOptions.SectionName)
            .Validate(options => !string.IsNullOrWhiteSpace(options.Version),
                "Fund derivative/income/valuation calculation version is required.")
            .ValidateOnStart();
        services.AddScoped<FundDerivativeIncomeValuationAnalyticsService>();
        services.AddSingleton<IFundPortfolioSignalGenerator, FundPortfolioSignalGenerator>();
        services.AddOptions<FundPortfolioSignalGenerationOptions>()
            .BindConfiguration(FundPortfolioSignalGenerationOptions.SectionName)
            .Validate(options => options.MinimumActivityAmount >= 0m &&
                options.MinimumWeightChangePercentagePoints >= 0m &&
                options.MinimumSectorChangePercentagePoints >= 0m &&
                options.MinimumConcentrationIncreasePercentagePoints >= 0m &&
                options.MinimumHedgeCoverageChangePercentagePoints >= 0m &&
                options.MinimumUnrealizedIncomeConcentrationPercentage >= 0m &&
                options.MinimumValuationAdjustmentExposurePercentage >= 0m &&
                !string.IsNullOrWhiteSpace(options.Version),
                "Fund portfolio signal thresholds must be non-negative and versioned.")
            .ValidateOnStart();
        services.AddScoped<FundPortfolioSignalGenerationService>();
        services.AddScoped<IFundPortfolioIntelligenceReadUseCase, GetFundPortfolioIntelligenceUseCase>();
        services.AddScoped<IFundPortfolioIntelligenceDetailRepository, EfCoreFundPortfolioIntelligenceDetailRepository>();
        services.AddScoped<IFundPortfolioAnalyticsCalculator, DeterministicFundPortfolioAnalyticsCalculator>();
        services.AddScoped<IFundPortfolioAnalyticsCalculationRegistry, FundPortfolioAnalyticsCalculationRegistry>();
        services.AddScoped<IFundPortfolioAnalyticsRecalculationCoordinator, FundPortfolioAnalyticsRecalculationCoordinator>();
        services.AddScoped<IGetFundEquityPositionsUseCase, GetFundEquityPositionsUseCase>();
        services.AddScoped<IGetFundEquityActivityUseCase, GetFundEquityActivityUseCase>();
        services.AddScoped<IGetCompanyFundHoldingsUseCase, GetCompanyFundHoldingsUseCase>();
        services.AddOptions<FundNonEquityNormalizationOptions>()
            .BindConfiguration(FundNonEquityNormalizationOptions.SectionName)
            .Validate(options => options.QuantityTolerance >= 0 && options.AbsoluteValueTolerance >= 0 && options.PercentagePointTolerance >= 0,
                "Fund non-equity normalization tolerances must be non-negative.")
            .ValidateOnStart();
        services.AddScoped<IFundPortfolioNonEquitySectionNormalizer, FundNonEquitySectionNormalizer>();
        services.AddScoped<IFundPortfolioSectionNormalizer>(provider => provider.GetRequiredService<IFundPortfolioNonEquitySectionNormalizer>());
        services.AddSingleton<IFundNonEquityNormalizationTelemetry, FundNonEquityNormalizationTelemetry>();
        services.AddScoped<IFundIncomeQualitySectionNormalizer, FundIncomeQualitySectionNormalizer>();
        services.AddScoped<IFundIncomeQualityRepository, EfCoreFundIncomeQualityRepository>();
        services.AddScoped<IGetFundIncomeQualityOverviewUseCase, GetFundIncomeQualityOverviewUseCase>();
        services.AddScoped<IFundNonEquityAssetRepository, EfCoreFundNonEquityAssetRepository>();
        services.AddOptions<FundPortfolioLocalSourceOptions>()
            .BindConfiguration("FundPortfolio:LocalSource")
            .Validate(options => options.MaximumItemsPerPage is > 0 and <= 500, "Fund portfolio source page size must be between 1 and 500.")
            .ValidateOnStart();
        services.AddSingleton<IFundPortfolioReportSource, ConfiguredLocalFundPortfolioReportSource>();
        services.AddSingleton<ManualUploadFundPortfolioReportSource>(_ => new ManualUploadFundPortfolioReportSource([]));
        services.AddSingleton<IFundPortfolioReportSource>(provider => provider.GetRequiredService<ManualUploadFundPortfolioReportSource>());
        services.AddScoped<IFundPortfolioReportSourceRegistry, FundPortfolioReportSourceRegistry>();
        services.AddScoped<IFundPortfolioImportRunRepository, EfCoreFundPortfolioImportRunRepository>();
        services.AddScoped<IStartFundPortfolioImportRunUseCase, StartFundPortfolioImportRunUseCase>();
        services.AddScoped<IImportFundPortfolioItemUseCase, ImportFundPortfolioItemUseCase>();
        services.AddScoped<IFinalizeFundPortfolioImportRunUseCase, FinalizeFundPortfolioImportRunUseCase>();
        services.AddScoped<IFundPortfolioMappingReviewRepository, EfCoreFundPortfolioMappingReviewRepository>();
        services.AddScoped<IResolveFundPortfolioMappingReviewUseCase, ResolveFundPortfolioMappingReviewUseCase>();
        services.AddScoped<IFundPortfolioAuditSink, EfCoreFundPortfolioAuditSink>();
        services.AddScoped<EfCoreFundPortfolioSourceWatermarkStore>();
        services.AddScoped<IFundPortfolioRetentionStore, EfCoreFundPortfolioRetentionStore>();
        services.AddOptions<FundPortfolioRetentionOptions>().BindConfiguration(FundPortfolioRetentionOptions.SectionName).Validate(options => options.RunMetadataDays > 0 && options.FailedItemDays > 0 && options.MappingDecisionDays > 0 && options.RawFileDays > 0 && options.CadenceHours > 0, "Fund portfolio retention periods must be positive.").ValidateOnStart();
        services.AddHostedService<FundPortfolioRetentionWorker>();
        services.AddOptions<FundPortfolioScheduledDiscoveryOptions>()
            .BindConfiguration(FundPortfolioScheduledDiscoveryOptions.SectionName)
            .Validate(options => options.CadenceSeconds > 0 && options.LookbackDays > 0 && options.BatchSize is > 0 and <= 500 && options.Concurrency is > 0 and <= 32 && options.LeaseDurationSeconds is >= 30 and <= 3600,
                "Fund portfolio scheduled discovery options must be positive and bounded.")
            .ValidateOnStart();
        services.AddHostedService<FundPortfolioScheduledDiscoveryWorker>();
        services.AddOptions<FundPortfolioImportProcessingOptions>()
            .BindConfiguration(FundPortfolioImportProcessingOptions.SectionName)
            .Validate(options => options.Concurrency is > 0 and <= 32 && options.PollSeconds > 0 && options.MaximumAttempts is > 0 and <= 10,
                "Fund portfolio import processing options must be positive and bounded.")
            .ValidateOnStart();
        services.AddHostedService<FundPortfolioImportProcessingWorker>();
        services.AddDbContext<FinancialIngestionDbContext>(options => options.UseNpgsql(connectionString));
        services.AddDbContext<ConversationDbContext>(options => options.UseNpgsql(connectionString));
        services.AddDbContext<MemoryDbContext>(options => options.UseNpgsql(connectionString));

        services
            .AddIdentityCore<FinancialCopilotUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<FinancialCopilotRole>()
            .AddEntityFrameworkStores<AuthDbContext>();
        services
            .AddOptions<OwnedIdentityOptions>()
            .Bind(configuration.GetSection(OwnedIdentityOptions.SectionName))
            .Validate(options =>
                Guid.TryParse(options.DefaultTenantId, out _) &&
                options.AccessTokenMinutes > 0 &&
                options.RefreshTokenDays > 0,
                "Owned Identity tenant and token lifetime settings must be valid.")
            .ValidateOnStart();
        services.AddScoped<IOwnedIdentityService, OwnedIdentityService>();
        services
            .AddOptions<TelegramLinkOptions>()
            .Bind(configuration.GetSection(TelegramLinkOptions.SectionName))
            .Validate(options =>
                !string.IsNullOrWhiteSpace(options.BotUsername) &&
                Uri.TryCreate(options.WebConfirmationBaseUrl, UriKind.Absolute, out var confirmationUri) &&
                confirmationUri.Scheme is "https" or "http" &&
                options.TokenLifetimeMinutes is >= 1 and <= 60,
                "Telegram account-linking options must contain a bot username, confirmation URL, and a 1-60 minute token lifetime.")
            .ValidateOnStart();
        services.AddScoped<ITelegramLinkService, TelegramLinkService>();
        services.AddScoped<ITelegramIdentityLinkReader>(provider => provider.GetRequiredService<ITelegramLinkService>());
        services
            .AddOptions<TelegramMembershipOptions>()
            .Bind(configuration.GetSection(TelegramMembershipOptions.SectionName))
            .Validate(options =>
                options.DailyFreeCredits > 0 &&
                options.VerificationCacheMinutes > 0 &&
                options.ProviderFailureCacheMinutes > 0 &&
                !string.IsNullOrWhiteSpace(options.PolicyVersion),
                "Telegram membership options must define a positive allowance, cache lifetime, and policy version.")
            .ValidateOnStart();
        services.AddOptions<TelegramGatewayOptions>()
            .BindConfiguration(TelegramGatewayOptions.SectionName)
            .Validate(options =>
                !options.Enabled ||
                Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) &&
                uri.Scheme == Uri.UriSchemeHttps &&
                !string.IsNullOrWhiteSpace(options.ServiceId) &&
                !string.IsNullOrWhiteSpace(options.ServiceSecret) &&
                options.RequestTimeoutSeconds is > 0 and <= 120,
                "Telegram Gateway settings must define an HTTPS URL, service identity, secret, and bounded timeout when enabled.")
            .ValidateOnStart();
        services.AddHttpClient<ITelegramGatewayClient, TelegramGatewayClient>();
        services.AddScoped<ITelegramChannelMembershipProvider, TelegramBotMembershipProvider>();
        services.AddScoped<TelegramMembershipService>();
        services.AddScoped<ITelegramMembershipService>(provider => provider.GetRequiredService<TelegramMembershipService>());
        services.AddScoped<IDailyFreeAllowanceService>(provider => provider.GetRequiredService<TelegramMembershipService>());
        services.AddScoped<TelegramMembershipRevalidationProcessor>();
        services.AddSingleton<ITelegramMonthlyTrendChartRenderer, TelegramMonthlyTrendChartRenderer>();
        services.AddSingleton<ITelegramAssistantResponseRenderer, TelegramAssistantResponseRenderer>();
        services.AddSingleton<ITelegramDisclosurePaginationStateStore, TelegramDisclosurePaginationStateStore>();
        services.AddScoped<ITelegramAiAssistantAdapter, TelegramAiAssistantAdapter>();
        services.AddScoped<OwnedIdentityBillingProvisioner>();
        services.AddScoped<IAdminManagementService, EfCoreAdminManagementService>();

        services.AddOptions<SalesGrowthScannerOptions>()
            .BindConfiguration(SalesGrowthScannerOptions.SectionName)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<SalesGrowthScannerOptions>, SalesGrowthScannerOptionsValidator>();
        services.AddSingleton<ISalesGrowthCommonEvaluationPeriodSelector>(provider =>
            new SalesGrowthCommonEvaluationPeriodSelector(
                provider.GetRequiredService<IOptions<SalesGrowthScannerOptions>>().Value));
        services.AddSingleton<ISalesGrowthComparisonCalculator, SalesGrowthComparisonCalculator>();

        services.AddOptions<ScannerCacheOptions>()
            .Bind(configuration.GetSection(ScannerCacheOptions.SectionName));
        var scannerCacheSettings = configuration
            .GetSection(ScannerCacheOptions.SectionName)
            .Get<ScannerCacheOptions>() ?? new ScannerCacheOptions();
        var cyclicalWavesAcquisitionSettings = configuration
            .GetSection(CyclicalWavesDataAcquisitionOptions.SectionName)
            .Get<CyclicalWavesDataAcquisitionOptions>() ?? new CyclicalWavesDataAcquisitionOptions();
        services
            .AddOptions<CyclicalWavesDataAcquisitionOptions>()
            .BindConfiguration(CyclicalWavesDataAcquisitionOptions.SectionName)
            .Validate(
                options =>
                    !options.Enabled ||
                    (CyclicalWavesUtcCronSchedule.IsValid(options.Schedule) &&
                     options.RequestDelayMilliseconds is >= 0 and <= 60_000 &&
                     options.TimeoutSeconds is > 0 and <= 300 &&
                     options.RetryCount is >= 0 and <= 5),
                "Enabled CyclicalWaves acquisition settings must define a valid five-field UTC " +
                "schedule and bounded pacing, timeout, and retry values.")
            .ValidateOnStart();

        if (scannerCacheSettings.UseRedis || cyclicalWavesAcquisitionSettings.Enabled)
        {
            var redisConfiguration = configuration.GetConnectionString("Redis");
            if (string.IsNullOrWhiteSpace(redisConfiguration))
            {
                redisConfiguration = scannerCacheSettings.RedisConfiguration;
            }

            if (cyclicalWavesAcquisitionSettings.Enabled && string.IsNullOrWhiteSpace(redisConfiguration))
            {
                throw new InvalidOperationException(
                    "Redis configuration is required when CyclicalWaves data acquisition is enabled.");
            }

            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = redisConfiguration;
                options.InstanceName = scannerCacheSettings.InstanceName;
            });
        }
        else
        {
        services.AddDistributedMemoryCache();
        }
        services.AddSingleton<IScannerCache, DistributedScannerCache>();
        services.AddMemoryCache();

        services.AddScoped<ICustomerAccountRepository, CustomerAccountRepository>();
        services.AddScoped<IWalletProjectionRepository, WalletProjectionRepository>();
        services.AddScoped<IWalletService>(provider =>
            provider.GetRequiredService<IWalletProjectionRepository>());
        services.AddScoped<IUsageReservationRepository, UsageReservationRepository>();
        services.AddScoped<IUsageLedgerRepository, UsageLedgerRepository>();
        services.AddScoped<IFinancialTransactionRepository, UsageLedgerRepository>();
        services.AddScoped<IInvoiceAccountRepository, InvoiceAccountRepository>();
        services.AddScoped<ISubscriptionPlanRepository, SubscriptionPlanRepository>();
        services.AddScoped<IPlanCapabilityService, PlanCapabilityService>();

        services.AddScoped<IBillableAccountResolver, BillableAccountResolver>();
        services.AddScoped<ICreditLinePolicyService, CreditLinePolicyService>();
        services.AddScoped<ICreditReservationService, UsageReservationAuthorizationService>();
        services.AddScoped<IUsageChargeCalculator, OperationUsageChargeCalculator>();
        services.AddScoped<IUsageAccountingService, UsageAccountingService>();
        services.AddScoped<IFinancialAccountingService, FinancialTransactionAccountingService>();
        services.AddScoped<ICreditAdjustmentService, CreditAdjustmentService>();
        services.AddScoped<IUsageRefundService, UsageRefundService>();
        services.AddScoped<IUsageFinalizationService, UsageFinalizationService>();
        services.AddScoped<IBillingOutboxProcessor>(provider =>
            new BillingOutboxProcessor(
                provider.GetRequiredService<BillingDbContext>(),
                provider.GetRequiredService<IBillingOutboxDispatcher>(),
                provider.GetRequiredService<TimeProvider>()));
        services.AddScoped<IBillingMaintenanceService, BillingMaintenanceService>();
        services.AddScoped<IWalletProjectionBuilder, WalletProjectionBuilder>();
        services.AddScoped<IEntitlementService>(provider =>
            new WalletEntitlementService(
                provider.GetRequiredService<IWalletService>(),
                provider.GetRequiredService<IPricingPolicyProvider>(),
                provider.GetRequiredService<ICreditLinePolicyService>(),
                "v1",
                provider.GetRequiredService<IPlanCapabilityService>()));
        services.AddScoped<IPartnerAccountService, PartnerAccountService>();
        services.AddScoped<IBillingAdministrationService, BillingAdministrationService>();
        services.AddScoped<IInvoiceService, InvoiceService>();
        services.AddScoped<IApiUsageReportService, ApiUsageReportService>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();
        services.AddScoped<IBillingPurchaseUseCases, BillingPurchaseUseCases>();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new PricingPolicy(
            "v1",
            new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["AiQuery.Scanner"] = 1m,
                ["AiQuery.StockAnalysis"] = 3m,
                ["AiQuery.CachedResponse"] = 0.2m,
                ["AiQuery.FinancialComparison"] = 3m,
                ["AiQuery.DeepResearch"] = 15m,
                ["AiQuery.PortfolioAnalysis"] = 6m,
                ["AiQuery.CodalAnalysis"] = 8m,
                ["AiQuery.Summarization"] = 4m,
                ["AiQuery.PersonalDigest"] = 4m,
                ["AiQuery.Embeddings"] = 0.5m,
                ["AiQuery.RagSearch"] = 2m,
                ["AiQuery.BackgroundJob"] = 6m
            },
            CachedMultiplier: 0.2m,
            ZeroChargeStatuses: new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "ValidationFailed",
                "ClarificationRequired",
                "ProviderFailed",
                "CancelledBeforeExecution",
                "TimedOutBeforeExecution"
            },
            CompletionMultipliers: new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["PartiallyCompleted"] = 0.5m
            }));
        services.AddSingleton<IPricingPolicyProvider, ConfiguredPricingPolicyProvider>();

        services
            .AddOptions<AiProviderOptions>()
            .Bind(configuration.GetSection(AiProviderOptions.SectionName))
            .Validate(
                options => string.IsNullOrWhiteSpace(options.DefaultProvider) ||
                    string.Equals(options.DefaultProvider, "OpenAI", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(options.DefaultProvider, "DeepSeek", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(options.DefaultProvider, "Abravran", StringComparison.OrdinalIgnoreCase),
                "AiProvider:DefaultProvider must be OpenAI, DeepSeek, or Abravran when configured.")
            .Validate(
                options => options.Abravran.MaxTokens > 0 &&
                    options.Abravran.Temperature is >= 0 and <= 2,
                "AiProvider:Abravran MaxTokens must be positive and Temperature must be between 0 and 2.")
            .ValidateOnStart();
        services.AddSingleton<IAiModelProviderRoutingPolicy, ConfiguredAiProviderRoutingPolicy>();
        services.AddSingleton<IAiExecutionUsageAccumulator, InMemoryAiExecutionUsageAccumulator>();

        services
            .AddOptions<AiModelProviderOptions>()
            .Bind(configuration.GetSection(AiModelProviderOptions.SectionName))
            .Validate(
                options => options.Providers.All(provider =>
                    !provider.Enabled ||
                    (!string.IsNullOrWhiteSpace(provider.ProviderKey) &&
                        !string.IsNullOrWhiteSpace(provider.ModelKey))),
                "Enabled AI model providers must specify provider and model keys.")
            .ValidateOnStart();
        services.AddSingleton<IAiExecutionTelemetrySink, LoggingAiExecutionTelemetrySink>();
        services.AddSingleton<IAiWorkflowTelemetrySink, LoggingAiWorkflowTelemetrySink>();
        services.AddSingleton<IAiStructuredOutputValidator, JsonStructuredOutputValidator>();
        services.AddSingleton<IAiModelClient>(provider =>
        {
            var settings = provider.GetRequiredService<
                Microsoft.Extensions.Options.IOptions<AiModelProviderOptions>>().Value;
            var registration = settings.Providers.FirstOrDefault(item =>
                string.Equals(item.Adapter, "Fake", StringComparison.OrdinalIgnoreCase)) ??
                new AiModelProviderRegistration
                {
                    ProviderKey = "DeterministicFake",
                    ModelKey = "fake-model",
                    HostingMode = AiProviderHostingMode.Fake,
                    Capabilities = AiModelCapability.ChatCompletion |
                        AiModelCapability.StructuredOutput |
                        AiModelCapability.ToolCalling |
                        AiModelCapability.Streaming |
                        AiModelCapability.Embeddings |
                        AiModelCapability.UsageReporting |
                        AiModelCapability.HealthCheck,
                    Enabled = false,
                    Priority = 1000
                };

            return new DeterministicFakeAiModelClient(
                ToDescriptor(registration),
                provider.GetRequiredService<TimeProvider>());
        });
        services.AddHttpClient("OllamaAiModelClient", (provider, client) =>
        {
            var settings = provider.GetRequiredService<
                Microsoft.Extensions.Options.IOptions<AiModelProviderOptions>>().Value;
            var registration = settings.Providers.FirstOrDefault(item =>
                string.Equals(item.Adapter, "Ollama", StringComparison.OrdinalIgnoreCase));

            if (registration?.Endpoint is not null)
            {
                client.BaseAddress = new Uri(registration.Endpoint, UriKind.Absolute);
                client.Timeout = TimeSpan.FromSeconds(registration.TimeoutSeconds);
            }
        });
        services.AddSingleton<IAiModelClient>(provider =>
        {
            var settings = provider.GetRequiredService<
                Microsoft.Extensions.Options.IOptions<AiModelProviderOptions>>().Value;
            var registration = settings.Providers.FirstOrDefault(item =>
                string.Equals(item.Adapter, "Ollama", StringComparison.OrdinalIgnoreCase)) ??
                new AiModelProviderRegistration
                {
                    ProviderKey = "Ollama",
                    ModelKey = "unconfigured",
                    HostingMode = AiProviderHostingMode.Local,
                    Enabled = false
                };

            return new OllamaAiModelClient(
                provider.GetRequiredService<IHttpClientFactory>().CreateClient("OllamaAiModelClient"),
                ToDescriptor(registration),
                provider.GetRequiredService<TimeProvider>());
        });
        services.AddHttpClient("OpenAiHostedAiModelTransport", (provider, client) =>
        {
            var settings = provider.GetRequiredService<
                Microsoft.Extensions.Options.IOptions<AiModelProviderOptions>>().Value;
            var providerSettings = provider.GetRequiredService<IOptions<AiProviderOptions>>().Value;
            var registration = GetRegistration(settings, "OpenAI") ??
                CreateOpenAiRegistration(providerSettings);

            if (registration?.Endpoint is not null)
            {
                client.BaseAddress = new Uri(registration.Endpoint, UriKind.Absolute);
                client.Timeout = TimeSpan.FromSeconds(registration.TimeoutSeconds);
            }

            var apiKey = !string.IsNullOrWhiteSpace(registration?.CredentialSecretReference)
                ? Environment.GetEnvironmentVariable(registration.CredentialSecretReference)
                : null;

            // Support the development-only key on the provider registration, then
            // fall back to the legacy AiProvider section.
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                apiKey = registration?.ApiKey;
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                apiKey = providerSettings.OpenAI.ApiKey;
            }

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", apiKey);
            }
        });
        services.AddSingleton<IAiModelClient>(provider =>
        {
            var settings = provider.GetRequiredService<
                Microsoft.Extensions.Options.IOptions<AiModelProviderOptions>>().Value;
            var providerSettings = provider.GetRequiredService<IOptions<AiProviderOptions>>().Value;
            var registration = GetRegistration(settings, "OpenAI") ??
                CreateOpenAiRegistration(providerSettings) ??
                new AiModelProviderRegistration
                {
                    ProviderKey = "OpenAI",
                    ModelKey = "unconfigured",
                    HostingMode = AiProviderHostingMode.Hosted,
                    Enabled = false
                };

            return new ConfiguredHostedAiModelClient(
                ToDescriptor(registration),
                new OpenAiHostedAiModelTransport(
                    provider.GetRequiredService<IHttpClientFactory>().CreateClient("OpenAiHostedAiModelTransport")),
                provider.GetRequiredService<TimeProvider>());
        });
        services.AddHttpClient("DeepSeekHostedAiModelTransport", (provider, client) =>
        {
            var settings = provider.GetRequiredService<
                Microsoft.Extensions.Options.IOptions<AiModelProviderOptions>>().Value;
            var providerSettings = provider.GetRequiredService<IOptions<AiProviderOptions>>().Value;
            var registration = GetRegistration(settings, "DeepSeek") ??
                CreateDeepSeekRegistration(providerSettings);

            if (registration?.Endpoint is not null)
            {
                client.BaseAddress = new Uri(registration.Endpoint, UriKind.Absolute);
                client.Timeout = TimeSpan.FromSeconds(registration.TimeoutSeconds);
            }

            var apiKey = registration?.CredentialSecretReference is not null
                ? Environment.GetEnvironmentVariable(registration.CredentialSecretReference)
                : null;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                apiKey = providerSettings.DeepSeek.ApiKey;
            }

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }
        });
        services.AddSingleton<IAiModelClient>(provider =>
        {
            var settings = provider.GetRequiredService<
                Microsoft.Extensions.Options.IOptions<AiModelProviderOptions>>().Value;
            var providerSettings = provider.GetRequiredService<IOptions<AiProviderOptions>>().Value;
            var registration = GetRegistration(settings, "DeepSeek") ??
                CreateDeepSeekRegistration(providerSettings) ??
                new AiModelProviderRegistration
                {
                    ProviderKey = "DeepSeek",
                    ModelKey = "unconfigured",
                    HostingMode = AiProviderHostingMode.Hosted,
                    Enabled = false
                };

            return new ConfiguredHostedAiModelClient(
                ToDescriptor(registration),
                new DeepSeekHostedAiModelTransport(
                    provider.GetRequiredService<IHttpClientFactory>().CreateClient("DeepSeekHostedAiModelTransport"),
                    provider.GetRequiredService<IOptions<AiProviderOptions>>()),
                provider.GetRequiredService<TimeProvider>());
        });
        services.AddHttpClient("AbravranHostedAiModelTransport", (provider, client) =>
        {
            var settings = provider.GetRequiredService<
                Microsoft.Extensions.Options.IOptions<AiModelProviderOptions>>().Value;
            var registration = GetRegistration(settings, "Abravran");

            if (registration?.Endpoint is not null)
            {
                client.BaseAddress = new Uri(registration.Endpoint, UriKind.Absolute);
                client.Timeout = TimeSpan.FromSeconds(registration.TimeoutSeconds);
            }

            var apiKey = registration?.CredentialSecretReference is not null
                ? Environment.GetEnvironmentVariable(registration.CredentialSecretReference)
                : null;
            apiKey ??= registration?.ApiKey;
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", $"apikey {apiKey}");
            }
        });
        services.AddSingleton<IAiModelClient>(provider =>
        {
            var settings = provider.GetRequiredService<
                Microsoft.Extensions.Options.IOptions<AiModelProviderOptions>>().Value;
            var registration = GetRegistration(settings, "Abravran") ??
                new AiModelProviderRegistration
                {
                    ProviderKey = "Abravran",
                    ModelKey = "unconfigured",
                    HostingMode = AiProviderHostingMode.Hosted,
                    Enabled = false
                };

            return new ConfiguredHostedAiModelClient(
                ToDescriptor(registration),
                new AbravranHostedAiModelTransport(
                    provider.GetRequiredService<IHttpClientFactory>().CreateClient("AbravranHostedAiModelTransport"),
                    provider.GetRequiredService<IOptions<AiProviderOptions>>()),
                provider.GetRequiredService<TimeProvider>());
        });
        services.AddSingleton<CapabilityBasedAiModelProviderResolver>();
        services.AddSingleton<IAiModelProviderResolver>(provider =>
            provider.GetRequiredService<CapabilityBasedAiModelProviderResolver>());
        services.AddSingleton<IAiProviderCapabilityRegistry>(provider =>
            provider.GetRequiredService<CapabilityBasedAiModelProviderResolver>());
        services.AddSingleton<IAiModelProviderDiagnostics>(provider =>
            provider.GetRequiredService<CapabilityBasedAiModelProviderResolver>());
        services.AddSingleton<IConversationalCapabilityRegistry>(_ =>
            new ConversationalCapabilityRegistry(InitialConversationalCapabilityCatalog.Create()));
        services.AddSingleton<QueryInterpretationValidator>();
        services.AddSingleton<IQueryInterpretationTelemetrySink, ActivityQueryInterpretationTelemetrySink>();
        services.AddSingleton<ICapabilityInterpreter, DeterministicCapabilityInterpreter>();
        services.AddSingleton<ICapabilityGuidanceService, CapabilityGuidanceService>();
        services.Configure<SemanticRoutingOptions>(configuration.GetSection(SemanticRoutingOptions.SectionName));
        services.AddScoped<SemanticRoutingOptions>(provider => provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<SemanticRoutingOptions>>().Value);
        services.AddSingleton<ISemanticRoutingTelemetrySink, SemanticRoutingEventTelemetrySink>();
        services.AddScoped<ISemanticRoutingRolloutCoordinator, SemanticRoutingRolloutCoordinator>();
        services.AddScoped<ISemanticCapabilityDispatcher, SemanticCapabilityDispatcher>();
        services.AddScoped<ISemanticExecutionCoordinator, SemanticExecutionCoordinator>();
        services.AddScoped<ISemanticOutcomeFeedbackCollector, SemanticOutcomeFeedbackCollector>();
        services.AddSingleton<ISemanticDialogueEventSink, BoundedSemanticDialogueEventSink>();
        services.AddSingleton<ISemanticDialogueMetricsQuery, SemanticDialogueMetricsQuery>();
        services.AddSingleton<ISemanticDialogueOutcomeTelemetry, SemanticDialogueOutcomeTelemetry>();
        services.AddScoped<ISemanticOfflineRegressionRunner, SemanticOfflineRegressionRunner>();
        services.AddSingleton<ISemanticPhraseCandidatePolicy, SemanticPhraseCandidatePolicy>();
        services.AddScoped<IConversationalCapabilityExecutor, MonthlyActivityTrendCapabilityExecutor>();
        services.AddScoped<IConversationalCapabilityExecutor, SymbolMetricLookupCapabilityExecutor>();
        services.AddScoped<IConversationalCapabilityExecutor, ProductRevenueMixCapabilityExecutor>();
        services.AddScoped<IConversationalCapabilityExecutor, FinancialStatementTableCapabilityExecutor>();
        services.AddScoped<IConversationalCapabilityExecutor, FinancialStatementAnalysisCapabilityExecutor>();
        services.AddScoped<IConversationalCapabilityExecutor, DisclosureListingCapabilityExecutor>();
        services.AddScoped<IConversationalCapabilityExecutor, MonthlySalesQualityCapabilityExecutor>();
        services.AddScoped<IConversationalCapabilityExecutor, PsGaugeCapabilityExecutor>();
        services.AddScoped<IConversationalCapabilityExecutor, ComprehensiveAnalysisCapabilityExecutor>();
        services.AddScoped<IConversationalCapabilityExecutor, StockScreeningCapabilityExecutor>();
        services.AddScoped<IConversationalCapabilityExecutor, PersonalizedInsightExplanationCapabilityExecutor>();
        services.AddOptions<IndustryRelativeValuationOptions>()
            .BindConfiguration(IndustryRelativeValuationOptions.SectionName)
            .Validate(options => options.IsValid(out _), "IndustryRelativeValuation options are invalid.")
            .ValidateOnStart();
        services.AddScoped<IIndustryRelativeValuationReadRepository, IndustryRelativeValuationReadRepository>();
        services.AddOptions<IndustryRelativeValuationReadOptions>()
            .BindConfiguration(IndustryRelativeValuationReadOptions.SectionName)
            .Validate(options =>
                options.DefaultResultLimit is >= 1 and <= 100 &&
                options.MaximumResultLimit is >= 1 and <= 1000 &&
                options.DefaultResultLimit <= options.MaximumResultLimit,
                "Industry relative valuation read limits are invalid.")
            .ValidateOnStart();
        services.AddScoped<IndustryRelativeValuationReadOptions>(provider =>
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<IndustryRelativeValuationReadOptions>>().Value);
        services.AddScoped<IIndustryRelativeValuationSemanticResolver, IndustryRelativeValuationSemanticAdapter>();
        services.AddScoped<IConversationalCapabilityExecutor>(provider => new IndustryRelativeValuationCapabilityExecutor(provider.GetRequiredService<IIndustryRelativeValuationReadRepository>(), "symbol_vs_industry_relative_valuation"));
        services.AddScoped<IConversationalCapabilityExecutor>(provider => new IndustryRelativeValuationCapabilityExecutor(provider.GetRequiredService<IIndustryRelativeValuationReadRepository>(), "industry_relative_valuation_ranking"));
        services.AddScoped<IConversationalCapabilityExecutor>(provider => new IndustryRelativeValuationCapabilityExecutor(provider.GetRequiredService<IIndustryRelativeValuationReadRepository>(), "industry_relative_valuation_summary"));
        services.AddScoped<IConversationalCapabilityExecutor>(provider => new IndustryRelativeValuationCapabilityExecutor(provider.GetRequiredService<IIndustryRelativeValuationReadRepository>(), "symbol_pair_within_industry"));
        services.AddSingleton<IQueryInterpretationProposalProvider, NoOpQueryInterpretationProposalProvider>();
        services.AddSingleton<HybridCapabilityInterpreter>();
        services.AddSingleton<CapabilityRegistryProjection>();
        services.AddOptions<CanonicalEntityResolutionOptions>();
        services.AddScoped<CanonicalEntityResolutionOptions>(provider =>
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<CanonicalEntityResolutionOptions>>().Value);
        services.Configure<ConversationTaskStateOptions>(configuration.GetSection(ConversationTaskStateOptions.SectionName));
        services.AddSingleton<IConversationTaskStateTelemetrySink, LoggingConversationTaskStateTelemetrySink>();
        services.AddScoped<ICanonicalQueryEntityResolver, CanonicalQueryEntityResolver>();
        services.AddScoped<ICanonicalQueryIndustryResolver>(provider =>
            (ICanonicalQueryIndustryResolver)provider.GetRequiredService<ICanonicalQueryEntityResolver>());
        services.AddScoped<ICapabilitySlotValidator, CapabilitySlotValidator>();
        services.AddSingleton<ISemanticQueryFrameEnricher, SemanticQueryFrameEnricher>();
        services.AddScoped<ICanonicalCompanyRouteAdapter, CanonicalCompanyRouteAdapter>();
        services.AddSingleton<IAiModelExecutionService, AiModelExecutionService>();

        services.AddScoped<IConversationRepository, ConversationRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<IConversationTaskStateRepository, ConversationTaskStateRepository>();
        services.AddScoped<IConversationTaskStateService>(provider => new ConversationTaskStateService(
            provider.GetRequiredService<IConversationTaskStateRepository>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<ConversationTaskStateOptions>>().Value,
            provider.GetRequiredService<IConversationTaskStateTelemetrySink>()));
        services.AddScoped<IConversationDialogueGate, ConversationDialogueGate>();
        services.AddScoped<EfCoreMemoryRecordRepository>();
        services.AddScoped<IMemoryConsentService, EfCoreMemoryConsentService>();
        services.AddScoped<IMemoryAuditService, EfCoreMemoryAuditService>();
        services.AddScoped<IMemoryControlService, EfCoreMemoryControlService>();
        services.AddScoped<IMemoryContextProvider, EfCoreMemoryContextProvider>();
        services.AddSingleton<IMemoryProtectionPolicy, ConsentAwareMemoryProtectionPolicy>();
        services.AddScoped<IAiIntentDetector, LlmAiIntentDetector>();
        services.AddScoped<IComprehensiveAnalysisQueryParser, LlmComprehensiveAnalysisQueryParser>();
        services.AddScoped<ISymbolLookupParser, LlmSymbolLookupParser>();
        services
            .AddOptions<MonthlyActivityLookupOptions>()
            .BindConfiguration(MonthlyActivityLookupOptions.SectionName);
        services.AddScoped<EfCoreSymbolMetricLookupService>();
        services.AddScoped<ILegacySymbolMetricLookupService>(provider =>
            provider.GetRequiredService<EfCoreSymbolMetricLookupService>());
        services.AddScoped<SnapshotMonthlyActivitySymbolMetricLookupService>();
        services.AddScoped<ISnapshotMonthlyActivitySymbolMetricLookupService>(provider =>
            provider.GetRequiredService<SnapshotMonthlyActivitySymbolMetricLookupService>());
        services.AddScoped<ISymbolMetricLookupService, SwitchableMonthlyActivitySymbolMetricLookupService>();
        services.AddScoped<IScannerQueryParser, LlmScannerQueryParser>();
        services.AddScoped<IScannerQueryPlanValidator, ScannerQueryPlanValidator>();
        services.AddScoped<IScannerResultColumnPolicy, ScannerResultColumnPolicy>();
        services.AddScoped<IScannerResultRanker, ScannerResultRanker>();
        services.AddScoped<IMarketQuoteResolver, ProviderMarketQuoteResolver>();
        services.AddScoped<IScannerExecutionService, EfCoreScannerExecutionService>();
        services.AddSingleton<ISalesGrowthScannerTelemetrySink, LoggingSalesGrowthScannerTelemetrySink>();
        services.AddScoped<IConfidenceScoreCalculator, ConfidenceScoreCalculator>();
        services.AddScoped<IConfidenceScoringService, ConfidenceScoringService>();
        services.AddSingleton<IConfidenceScoringAuditSink, LoggingConfidenceScoringAuditSink>();
        services.AddScoped<IScannerExplanationGenerator, LlmScannerExplanationGenerator>();
        services.AddScoped<IExplainableAnswerBuilder, ExplainableAnswerBuilder>();
        services.AddScoped<IBillingFacadeHook, AiFacadeBillingHook>();

        // Deterministic prose + numeric consistency protection. Shared by V1 and V2 so the LLM can
        // never report a metric value that disagrees with the deterministic structured table.
        services.AddSingleton<MetricDisplayNameResolver>();
        services.AddSingleton<ISymbolLookupProseBuilder, SymbolLookupProseBuilder>();
        services.AddSingleton<IAnswerConsistencyWarningSink, LoggingAnswerConsistencyWarningSink>();
        services.AddSingleton<IAnswerConsistencyValidator, AnswerConsistencyValidator>();

        services.Configure<AiOrchestrationOptions>(
            configuration.GetSection(AiOrchestrationOptions.SectionName));

        var orchestrationMode = configuration
            .GetSection(AiOrchestrationOptions.SectionName)
            .GetValue<AiOrchestrationMode>("Mode", AiOrchestrationMode.V1);

        if (orchestrationMode == AiOrchestrationMode.MicrosoftAgentFrameworkV2)
        {
            services.AddScoped<ScannerToolAdapter>();
            services.AddScoped<SymbolLookupToolAdapter>();
            services.AddScoped<ComprehensiveAnalysisToolAdapter>();
            services.AddScoped<ExplainableAnswerAdapter>();
            services.AddScoped<MemoryContextAdapter>();
            services.AddScoped<BillingFunctions>();
            services.AddScoped<MessagePersistenceFunction>();
            services.AddScoped<MissingAnswerFeedbackFunction>();
            services.AddSingleton<FinancialCopilotAgentFactory>(sp =>
                new FinancialCopilotAgentFactory(sp.GetService<ILoggerFactory>()));
            services.AddScoped<FinancialCopilotAgentWorkflowRunner>();
            services.AddScoped<FinancialCopilotWorkflowDefinition>();
            services.AddScoped<IAiQueryOrchestrationService,
                MicrosoftAgentFrameworkAiQueryOrchestrationService>();
        }
        else
        {
            services.AddScoped<IAiQueryOrchestrationService, AiQueryOrchestrationService>();
        }

        // Evaluation framework â€” internal quality infrastructure, no public API surface.
        services.AddSingleton<IEvaluationDatasetRepository, SeedEvaluationDatasetRepository>();
        services.AddSingleton<IEvaluationRunRepository, InMemoryEvaluationRunRepository>();
        services.AddSingleton<IPromptVersionRegistry, NoOpPromptVersionRegistry>();
        services.AddSingleton<IInterpretationScorer, InterpretationScorer>();
        services.AddSingleton<IClarificationScorer, ClarificationScorer>();
        services.AddSingleton<IEvidenceCompletenessScorer, EvidenceCompletenessScorer>();
        services.AddSingleton<IConfidenceProtectionScorer, ConfidenceProtectionScorer>();
        services.AddSingleton<IRegressionReporter, RegressionReporter>();
        services.AddScoped<IAiEvaluationRunner, AiEvaluationRunner>();

        // EBIT = NET_PROFIT + FINANCE_COSTS + INCOME_TAX (additive composite).
        services.AddSingleton<IFinancialMetricCalculator>(_ => new AdditiveCompositeMetricCalculator(
            new MetricCode("EBIT"),
            [new MetricCode("NET_PROFIT"), new MetricCode("FINANCE_COSTS"), new MetricCode("INCOME_TAX")]));

        // Growth calculators â€” existing NET_PROFIT and MONTHLY_SALES.
        services.AddSingleton<IFinancialMetricCalculator>(_ => new PercentageGrowthMetricCalculator(
            new MetricCode("NET_PROFIT_GROWTH_YOY"),
            new MetricCode("NET_PROFIT")));
        services.AddSingleton<IFinancialMetricCalculator>(_ => new PercentageGrowthMetricCalculator(
            new MetricCode("NET_PROFIT_GROWTH_QOQ"),
            new MetricCode("NET_PROFIT")));
        services.AddSingleton<IFinancialMetricCalculator>(_ => new PercentageGrowthMetricCalculator(
            new MetricCode("MONTHLY_SALES_GROWTH_YOY"),
            new MetricCode("MONTHLY_SALES")));
        services.AddSingleton<IFinancialMetricCalculator>(_ => new PercentageGrowthMetricCalculator(
            new MetricCode("MONTHLY_SALES_GROWTH_MOM"),
            new MetricCode("MONTHLY_SALES")));
        services.AddSingleton<IFinancialMetricCalculator>(_ => new TrailingTwelveMonthSumMetricCalculator(
            new MetricCode("TTM_SALES"),
            new MetricCode("MONTHLY_SALES"),
            requiredObservationCount: 12));
        // Spec 057: identity persistence of monthly-activity aggregates â€” a single-component
        // additive composite selects the source observation matching each monthly period, so one
        // DerivedMetrics row exists per company-month and the symbol lookup can answer
        // Â«Ø¢Ø®Ø±ÛŒÙ† ÙØ±ÙˆØ´ / Ù…Ù‚Ø¯Ø§Ø± ÙØ±ÙˆØ´ / Ù†Ø±Ø® ÙØ±ÙˆØ´ / Ù…Ù‚Ø¯Ø§Ø± ØªÙˆÙ„ÛŒØ¯Â» from the latest month.
        foreach (var monthlyCode in new[]
        {
            "MONTHLY_SALES", "MONTHLY_SALES_YTD", "MONTHLY_SALES_YTD_PREVIOUS_MONTH",
            "MONTHLY_SALES_QUANTITY", "MONTHLY_PRODUCTION_QUANTITY", "MONTHLY_SALES_RATE"
        })
        {
            var captured = new MetricCode(monthlyCode);
            services.AddSingleton<IFinancialMetricCalculator>(_ =>
                new AdditiveCompositeMetricCalculator(captured, [captured]));
        }
        services.AddSingleton<IFinancialMetricCalculator>(_ => new TrailingTwelveMonthSumMetricCalculator(
            new MetricCode("TTM_EARNINGS"),
            new MetricCode("NET_PROFIT"),
            requiredObservationCount: 4));
        services.AddSingleton<IFinancialMetricCalculator>(_ => new EarningsPerShareMetricCalculator(
            new MetricCode("TTM_EPS"),
            new MetricCode("TTM_EARNINGS"),
            new MetricCode("SHARES_OUTSTANDING")));
        foreach (var cyclicalWavesBaseCode in new[]
        {
            "REVENUE", "NET_PROFIT", "GROSS_PROFIT", "OPERATING_PROFIT"
        })
        {
            var captured = new MetricCode(cyclicalWavesBaseCode);
            services.AddSingleton<IFinancialMetricCalculator>(_ =>
                new SourceLineItemPassthroughMetricCalculator(captured, captured));
        }
        // PE_TTM / PS_TTM: use vendor-supplied CyclicalWaves ratio snapshot until LATEST_PRICE and
        // SHARES_OUTSTANDING become available from market data, at which point replace with
        // ValuationRatioMetricCalculator(PE_TTM, LATEST_PRICE, TTM_EPS).
        services.AddSingleton<IFinancialMetricCalculator>(_ => new SourceLineItemPassthroughMetricCalculator(
            new MetricCode("PE_TTM"),
            new MetricCode("PE_RATIO")));
        services.AddSingleton<IFinancialMetricCalculator>(_ => new SourceLineItemPassthroughMetricCalculator(
            new MetricCode("PS_TTM"),
            new MetricCode("PS_RATIO")));

        // CyclicalWaves pre-computed average metrics â€” passthrough from line items â†’ DerivedMetrics.
        services.AddSingleton<IFinancialMetricCalculator>(_ => new SourceLineItemPassthroughMetricCalculator(
            new MetricCode("AVG_4Q_REVENUE"),
            new MetricCode("AVG_4Q_REVENUE")));
        services.AddSingleton<IFinancialMetricCalculator>(_ => new SourceLineItemPassthroughMetricCalculator(
            new MetricCode("AVG_12M_MONTHLY_SALES"),
            new MetricCode("AVG_12M_MONTHLY_SALES")));

        // CyclicalWaves margin snapshots â€” passthrough from FinancialStatementLineItems â†’ DerivedMetrics.
        services.AddSingleton<IFinancialMetricCalculator>(_ => new SourceLineItemPassthroughMetricCalculator(
            new MetricCode("NET_PROFIT_MARGIN"),
            new MetricCode("NET_PROFIT_MARGIN")));
        services.AddSingleton<IFinancialMetricCalculator>(_ => new SourceLineItemPassthroughMetricCalculator(
            new MetricCode("GROSS_PROFIT_MARGIN"),
            new MetricCode("GROSS_PROFIT_MARGIN")));
        services.AddSingleton<IFinancialMetricCalculator>(_ => new SourceLineItemPassthroughMetricCalculator(
            new MetricCode("OPERATING_PROFIT_MARGIN"),
            new MetricCode("OPERATING_PROFIT_MARGIN")));

        // CodalDB-derived YoY growth calculators (use cumulative ThreeMonths input, shifted âˆ’12 months).
        services.AddSingleton<IFinancialMetricCalculator>(_ => new PercentageGrowthMetricCalculator(new MetricCode("REVENUE_GROWTH_YOY"),          new MetricCode("REVENUE")));
        services.AddSingleton<IFinancialMetricCalculator>(_ => new PercentageGrowthMetricCalculator(new MetricCode("GROSS_PROFIT_GROWTH_YOY"),     new MetricCode("GROSS_PROFIT")));
        services.AddSingleton<IFinancialMetricCalculator>(_ => new PercentageGrowthMetricCalculator(new MetricCode("OPERATING_PROFIT_GROWTH_YOY"), new MetricCode("OPERATING_PROFIT")));
        services.AddSingleton<IFinancialMetricCalculator>(_ => new PercentageGrowthMetricCalculator(new MetricCode("EPS_GROWTH_YOY"),              new MetricCode("EPS")));
        services.AddSingleton<IFinancialMetricCalculator>(_ => new PercentageGrowthMetricCalculator(new MetricCode("EBIT_GROWTH_YOY"),             new MetricCode("EBIT")));
        services.AddSingleton<IFinancialMetricCalculator>(_ => new PercentageGrowthMetricCalculator(new MetricCode("EQUITY_GROWTH_YOY"),           new MetricCode("TOTAL_EQUITY")));

        // CodalDB-derived QoQ growth calculators (input must be discrete ThreeMonths via CodalDiscreteQuarterDeriver).
        services.AddSingleton<IFinancialMetricCalculator>(_ => new PercentageGrowthMetricCalculator(new MetricCode("REVENUE_GROWTH_QOQ"),          new MetricCode("REVENUE")));
        services.AddSingleton<IFinancialMetricCalculator>(_ => new PercentageGrowthMetricCalculator(new MetricCode("GROSS_PROFIT_GROWTH_QOQ"),     new MetricCode("GROSS_PROFIT")));
        services.AddSingleton<IFinancialMetricCalculator>(_ => new PercentageGrowthMetricCalculator(new MetricCode("OPERATING_PROFIT_GROWTH_QOQ"), new MetricCode("OPERATING_PROFIT")));
        services.AddSingleton<IFinancialMetricCalculator>(_ => new PercentageGrowthMetricCalculator(new MetricCode("EPS_GROWTH_QOQ"),              new MetricCode("EPS")));
        services.AddSingleton<IFinancialMetricCalculator>(_ => new PercentageGrowthMetricCalculator(new MetricCode("EBIT_GROWTH_QOQ"),             new MetricCode("EBIT")));
        services.AddSingleton<IFinancialMetricCalculator>(_ => new PercentageGrowthMetricCalculator(new MetricCode("EQUITY_GROWTH_QOQ"),           new MetricCode("TOTAL_EQUITY")));

        services.AddSingleton<FinancialMetricRegistry>(provider =>
            new FinancialMetricRegistry(
                PhaseOneFinancialSemanticCatalog.Definitions,
                provider.GetServices<IFinancialMetricCalculator>()));
        services.AddSingleton<IFinancialMetricRegistry>(provider =>
            provider.GetRequiredService<FinancialMetricRegistry>());
        services.AddSingleton<IMetricDependencyResolver>(provider =>
            provider.GetRequiredService<FinancialMetricRegistry>());
        services.AddSingleton<MetricAliasResolver>();
        services.AddSingleton<IMetricAliasExpressionNormalizer, DefaultMetricAliasExpressionNormalizer>();
        services.AddScoped<IDynamicMetricAliasRepository, EfCoreDynamicMetricAliasRepository>();
        services.AddScoped<IMetricAliasCandidateRepository, EfCoreMetricAliasCandidateRepository>();
        services.AddSingleton<CompositeMetricAliasResolver>(provider =>
            new CompositeMetricAliasResolver(
                provider.GetRequiredService<IServiceScopeFactory>(),
                provider.GetRequiredService<MetricAliasResolver>(),
                provider.GetRequiredService<IMetricAliasExpressionNormalizer>(),
                provider.GetRequiredService<ILogger<CompositeMetricAliasResolver>>()));
        services.AddSingleton<IMetricAliasResolver>(provider =>
            provider.GetRequiredService<CompositeMetricAliasResolver>());
        services.AddSingleton<IMetricAliasCacheInvalidator>(provider =>
            provider.GetRequiredService<CompositeMetricAliasResolver>());
        services.AddSingleton<EfCoreMetricPeriodAliasResolver>();
        services.AddSingleton<IMetricPeriodAliasResolver>(provider =>
            provider.GetRequiredService<EfCoreMetricPeriodAliasResolver>());
        services.AddSingleton<EfCoreMetricDefinitionCapabilityReader>();
        services.AddSingleton<IMetricDefinitionCapabilityReader>(provider =>
            provider.GetRequiredService<EfCoreMetricDefinitionCapabilityReader>());
        services.AddSingleton<IDirectMetricRoutingRegistry, DirectMetricRoutingRegistry>();

        services
            .AddOptions<MetricAliasLearningOptions>()
            .BindConfiguration(MetricAliasLearningOptions.SectionName);
        services.AddSingleton<MetricAliasLearningPolicy>(provider =>
            new MetricAliasLearningPolicy(
                provider.GetRequiredService<IOptions<MetricAliasLearningOptions>>().Value));
        services.AddSingleton<NoOpMetricAliasLearningSignalCollector>();
        services.AddSingleton<AsyncFireAndForgetMetricAliasLearningSignalCollector>();
        services.AddSingleton<IMetricAliasLearningSignalCollector>(provider =>
        {
            var settings = provider.GetRequiredService<IOptions<MetricAliasLearningOptions>>().Value;
            return settings.Enabled
                ? provider.GetRequiredService<AsyncFireAndForgetMetricAliasLearningSignalCollector>()
                : provider.GetRequiredService<NoOpMetricAliasLearningSignalCollector>();
        });
        services.AddSingleton<IMetricCalculationPolicyProvider>(_ =>
            new MetricCalculationPolicyProvider(PhaseOneFinancialSemanticCatalog.Policies));

        services
            .AddOptions<FinancialProviderOptions>()
            .BindConfiguration(FinancialProviderOptions.SectionName);
        services.AddTransient<FinancialProviderResilienceHandler>();
        services
            .AddHttpClient<ConfiguredFinancialDataProviderClient>((provider, client) =>
            {
                var settings = provider.GetRequiredService<
                    Microsoft.Extensions.Options.IOptions<FinancialProviderOptions>>().Value;
                client.BaseAddress = new Uri(settings.BaseAddress, UriKind.Absolute);

                if (!string.IsNullOrWhiteSpace(settings.ApiKey))
                {
                    client.DefaultRequestHeaders.Add("X-Api-Key", settings.ApiKey);
                }
            })
            .AddHttpMessageHandler<FinancialProviderResilienceHandler>();
        services.AddScoped<IProviderRawPayloadStore, ProviderRawPayloadStore>();

        // CyclicalWaves data provider
        services
            .AddOptions<CyclicalWavesProviderOptions>()
            .BindConfiguration(CyclicalWavesProviderOptions.SectionName)
            .Validate(
                options =>
                    options.TokenExpirationSafetyMarginSeconds is > 0 and <= 3_600 &&
                    (!cyclicalWavesAcquisitionSettings.Enabled ||
                     (Uri.TryCreate(options.BaseAddress, UriKind.Absolute, out var baseAddress) &&
                      baseAddress.Scheme == Uri.UriSchemeHttps &&
                      !string.IsNullOrWhiteSpace(options.UserName) &&
                      !string.IsNullOrWhiteSpace(options.Password))),
                "CyclicalWaves settings must define a bounded token safety margin and, when data " +
                "acquisition is enabled, an HTTPS base address plus secret-backed credentials.")
            .ValidateOnStart();
        services.AddSingleton<CyclicalWavesTokenCache>();
        services.AddTransient<CyclicalWavesAuthHandler>();
        services
            .AddHttpClient<CyclicalWavesDataProviderClient>((provider, client) =>
            {
                var settings = provider.GetRequiredService<
                    Microsoft.Extensions.Options.IOptions<CyclicalWavesProviderOptions>>().Value;
                client.BaseAddress = new Uri(settings.BaseAddress, UriKind.Absolute);
            })
            .AddHttpMessageHandler<CyclicalWavesAuthHandler>()
            .AddHttpMessageHandler<FinancialProviderResilienceHandler>();
        services.AddTransient<CyclicalWavesDataAcquisitionResilienceHandler>();
        services
            .AddHttpClient<CyclicalWavesDataAcquisitionClient>((provider, client) =>
            {
                var settings = provider.GetRequiredService<
                    Microsoft.Extensions.Options.IOptions<CyclicalWavesProviderOptions>>().Value;
                client.BaseAddress = new Uri(settings.BaseAddress, UriKind.Absolute);
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .AddHttpMessageHandler<CyclicalWavesAuthHandler>()
            .AddHttpMessageHandler<CyclicalWavesDataAcquisitionResilienceHandler>();
        services.AddScoped<ICyclicalWavesDataAcquisitionClient>(provider =>
            provider.GetRequiredService<CyclicalWavesDataAcquisitionClient>());
        services.AddSingleton<ICanonicalJsonHasher, CanonicalJsonHasher>();
        services.AddScoped<ICyclicalWavesAcquisitionCompanySource, CyclicalWavesAcquisitionCompanySource>();
        services.AddScoped<ICyclicalWavesDataAcquisitionRepository, CyclicalWavesDataAcquisitionRepository>();
        services.AddScoped<ICyclicalWavesDataAcquisitionService, CyclicalWavesDataAcquisitionService>();
        services.AddScoped<MockFinancialDataProvider>();
        services.AddScoped<ISymbolDataProvider>(provider =>
            provider.GetRequiredService<CyclicalWavesDataProviderClient>());
        services.AddScoped<IFinancialStatementProvider>(provider =>
            provider.GetRequiredService<CyclicalWavesDataProviderClient>());
        services.AddScoped<IMonthlyProductionSalesProvider>(provider =>
            provider.GetRequiredService<CyclicalWavesDataProviderClient>());
        services.AddScoped<IFinancialDataProviderHealthService>(provider =>
            provider.GetRequiredService<CyclicalWavesDataProviderClient>());
        services.AddScoped<ICyclicalWavesPsProviderClient>(provider =>
            provider.GetRequiredService<CyclicalWavesDataProviderClient>());
        services.AddScoped<ICyclicalWavesPsAcceptedOperation, CyclicalWavesPsOperation>();
        services.AddScoped<ICyclicalWavesRelativeValuationProviderClient>(provider =>
            provider.GetRequiredService<CyclicalWavesDataProviderClient>());
        services
            .AddOptions<CyclicalWavesPsSyncOptions>()
            .BindConfiguration(CyclicalWavesPsSyncOptions.SectionName)
            .Validate(options =>
                options.SnapshotCadenceMinutes > 0 && options.HistoryCadenceHours > 0 &&
                options.MaxConcurrency is > 0 and <= 16 && options.MaxCompaniesPerRun is > 0 &&
                options.MaxRunDurationMinutes > 0 && options.MaxResponseBytes is > 0 &&
                options.MaxHistoryPointsPerCompany > 0 && options.LeaseDurationMinutes > 0 &&
                options.LeaseRenewalMinutes is > 0 && options.LeaseRenewalMinutes < options.LeaseDurationMinutes &&
                options.MaximumAbsoluteRatio > 0m,
                "CyclicalWaves P/S synchronization options are invalid.")
            .ValidateOnStart();

        // CyclicalWaves blog â€” ComprehensiveAnalysis sync (spec 065).
        // Reuses CyclicalWavesAuthHandler + CyclicalWavesTokenCache from above.
        services
            .AddOptions<ComprehensiveAnalysisBlogOptions>()
            .BindConfiguration(ComprehensiveAnalysisBlogOptions.SectionName);
        services
            .AddHttpClient<ComprehensiveAnalysisBlogClient>((provider, client) =>
            {
                var settings = provider.GetRequiredService<
                    Microsoft.Extensions.Options.IOptions<CyclicalWavesProviderOptions>>().Value;
                client.BaseAddress = new Uri(settings.BaseAddress, UriKind.Absolute);
            })
            .AddHttpMessageHandler<CyclicalWavesAuthHandler>()
            .AddHttpMessageHandler<FinancialProviderResilienceHandler>();
        services.AddSingleton<IHtmlTextStripper, HtmlAgilityPackTextStripper>();
        services.AddScoped<ComprehensiveAnalysisRepository>();
        services.AddScoped<IComprehensiveAnalysisQueryRepository>(provider =>
            provider.GetRequiredService<ComprehensiveAnalysisRepository>());
        services.AddScoped<IComprehensiveAnalysisSyncRunReader>(provider =>
            provider.GetRequiredService<ComprehensiveAnalysisRepository>());
        services.AddScoped<IComprehensiveAnalysisFullSyncService, ComprehensiveAnalysisFullSyncService>();
        services.AddScoped<IComprehensiveAnalysisDailySyncService, ComprehensiveAnalysisDailySyncService>();
        services.AddScoped<IComprehensiveAnalysisPlainTextBackfillService, ComprehensiveAnalysisPlainTextBackfillService>();
        services.AddScoped<IComprehensiveAnalysisSearchRepository, EfCoreComprehensiveAnalysisSearchRepository>();
        services.AddScoped<IComprehensiveAnalysisQueryUseCase, ComprehensiveAnalysisQueryUseCase>();
        services.AddScoped<IQueryComprehensiveAnalysisUseCase, QueryComprehensiveAnalysisUseCase>();

        // NADPCO HTTP API provider foundation. Registered by name for coexisting ingestion routes;
        // it does not replace the default CyclicalWaves provider and is not a market-data source.
        services
            .AddOptions<NadpcoApiProviderOptions>()
            .BindConfiguration(NadpcoApiProviderOptions.SectionName);
        // Spec 053 â€” per-run Shamsi start boundary override for current-API backfill (scoped; the
        // provider client and the backfill coordinator share the same scope instance).
        services.AddScoped<NoavaranCurrentApiBoundaryOverride>();
        services.AddScoped<INoavaranCurrentApiBoundaryOverride>(provider =>
            provider.GetRequiredService<NoavaranCurrentApiBoundaryOverride>());
        services.AddSingleton<NadpcoApiTokenCache>();
        services.AddTransient<NadpcoApiAuthHandler>();
        services.AddTransient<NadpcoApiResilienceHandler>();
        services
            .AddHttpClient<INadpcoApiTokenProvider, NadpcoApiTokenProvider>((provider, client) =>
            {
                var settings = provider.GetRequiredService<IOptions<NadpcoApiProviderOptions>>().Value;
                client.BaseAddress = new Uri(settings.BaseAddress, UriKind.Absolute);
            })
            .AddHttpMessageHandler<NadpcoApiResilienceHandler>();
        services
            .AddHttpClient<NadpcoApiDataProviderClient>((provider, client) =>
            {
                var settings = provider.GetRequiredService<IOptions<NadpcoApiProviderOptions>>().Value;
                client.BaseAddress = new Uri(settings.BaseAddress, UriKind.Absolute);
            })
            .AddHttpMessageHandler<NadpcoApiAuthHandler>()
            .AddHttpMessageHandler<NadpcoApiResilienceHandler>();

        // CodalDb data provider (read-only SQL Server; coexists with CyclicalWaves). It is NOT
        // registered as the default ISymbolDataProvider/etc. nor as IMarketDataProvider; it is
        // selected for ingestion by name through IFinancialDataProviderRouter.
        services
            .AddOptions<CodalDbProviderOptions>()
            .BindConfiguration(CodalDbProviderOptions.SectionName);
        services.AddSingleton<CodalDbConnectionFactory>();
        services.AddSingleton<CodalDbSqlResilience>();
        services.AddScoped<ICodalDbQueryExecutor, SqlCodalDbQueryExecutor>();
        services.AddScoped<CodalDbDataProviderClient>();
        services.AddScoped<IFinancialRatioProvider>(sp => sp.GetRequiredService<CodalDbDataProviderClient>());
        services.AddScoped<IFinancialDataProviderRouter>(provider =>
        {
            var cyclicalWaves = provider.GetRequiredService<CyclicalWavesDataProviderClient>();
            var codalDb = provider.GetRequiredService<CodalDbDataProviderClient>();
            var nadpcoApi = provider.GetRequiredService<NadpcoApiDataProviderClient>();
            var cyclicalWavesName = provider
                .GetRequiredService<IOptions<CyclicalWavesProviderOptions>>().Value.ProviderName;
            var codalDbName = provider
                .GetRequiredService<IOptions<CodalDbProviderOptions>>().Value.ProviderName;
            var nadpcoApiName = provider
                .GetRequiredService<IOptions<NadpcoApiProviderOptions>>().Value.ProviderName;

            // Legacy aliases (spec 051): keep pre-rename names ("CodalDb"/"NadpcoApi") resolvable so
            // in-flight DataSyncRequest messages enqueued before the rename still route correctly.
            var symbolProviders = new Dictionary<string, ISymbolDataProvider>
            {
                [cyclicalWavesName] = cyclicalWaves,
                [codalDbName] = codalDb,
                [nadpcoApiName] = nadpcoApi
            };
            var statementProviders = new Dictionary<string, IFinancialStatementProvider>
            {
                [cyclicalWavesName] = cyclicalWaves,
                [codalDbName] = codalDb,
                [nadpcoApiName] = nadpcoApi
            };
            var monthlyProviders = new Dictionary<string, IMonthlyProductionSalesProvider>
            {
                [cyclicalWavesName] = cyclicalWaves,
                [codalDbName] = codalDb,
                [nadpcoApiName] = nadpcoApi
            };
            var ratioProviders = new Dictionary<string, IFinancialRatioProvider>
            {
                [codalDbName] = codalDb,
                [nadpcoApiName] = nadpcoApi
            };

            // CodalDb -> NoavaranArchiveSql, NadpcoApi -> NoavaranCurrentApi (provider already
            // registered under the current name; add the legacy key pointing at the same instance).
            static void AddLegacyAliases<T>(Dictionary<string, T> registry)
            {
                foreach (var (legacyName, currentName) in ProviderSources.LegacyNameAliases)
                {
                    if (!registry.ContainsKey(legacyName) && registry.TryGetValue(currentName, out var provider))
                    {
                        registry[legacyName] = provider;
                    }
                }
            }

            AddLegacyAliases(symbolProviders);
            AddLegacyAliases(statementProviders);
            AddLegacyAliases(monthlyProviders);
            AddLegacyAliases(ratioProviders);

            return new FinancialDataProviderRouter(
                symbolProviders, statementProviders, monthlyProviders, ratioProviders);
        });

        // Spec 051 â€” logical-vendor/physical-source model support: dataset source-priority policy
        // (pure config) and cross-source identity-conflict logging.
        services
            .AddOptions<SourcePriorityOptions>()
            .BindConfiguration(SourcePriorityOptions.SectionName);
        services.AddScoped<ISourcePriorityResolver, SourcePriorityResolver>();
        services.AddSingleton<IIdentityConflictLog, LoggingIdentityConflictLog>();
        services.AddScoped<ISourceFreshnessReader, SourceFreshnessReader>();

        services.AddScoped<IFinancialPayloadNormalizer, SymbolPayloadNormalizer>();
        services.AddScoped<IFinancialPayloadNormalizer, FinancialStatementPayloadNormalizer>();
        services.AddScoped<IFinancialPayloadNormalizer, MonthlyReportPayloadNormalizer>();
        services.AddScoped<IFinancialPayloadNormalizer, CyclicalWavesFinancialStatementNormalizer>();
        services.AddScoped<IFinancialPayloadNormalizer, CyclicalWavesMonthlyReportNormalizer>();
        services.AddSingleton<CanonicalSymbolLinkageResolver>();
        services.AddScoped<IFinancialPayloadNormalizer, CodalDbSymbolNormalizer>();
        services.AddScoped<IFinancialPayloadNormalizer, CodalDbFinancialStatementNormalizer>();
        services.AddScoped<IFinancialPayloadNormalizer, CodalDbMonthlyReportNormalizer>();
        services.AddScoped<IFinancialPayloadNormalizer, CodalDbRatioNormalizer>();
        services.AddScoped<IFinancialPayloadNormalizer, NadpcoApiCompanyNormalizer>();
        services.AddScoped<IFinancialPayloadNormalizer, NadpcoApiFinancialStatementNormalizer>();
        services.AddScoped<IFinancialPayloadNormalizer, NadpcoApiFundamentalIndexNormalizer>();
        services.AddScoped<IFinancialPayloadNormalizer, NadpcoApiMonthlyActivityNormalizer>();
        services.AddScoped<IFinancialStatementAnalysisRepository, EfCoreFinancialStatementAnalysisRepository>();
        services.AddScoped<IFinancialStatementSelectionService, FinancialStatementSelectionService>();
        services.AddScoped<IFinancialStatementAnalysisRenderer, FinancialStatementAnalysisRenderer>();
        services.AddScoped<IFinancialStatementAnalysisUseCase, FinancialStatementAnalysisUseCase>();
        services.AddScoped<IFinancialStatementTableRepository, EfCoreFinancialStatementTableRepository>();
        services.AddScoped<IFinancialStatementTableRenderer, FinancialStatementTableRenderer>();
        services.AddScoped<IFinancialStatementTableQueryUseCase, FinancialStatementTableQueryUseCase>();
        // Spec 075 â€” product revenue mix: calculator + repository wired after monthly-activity ingestion.
        services.AddScoped<ICompanyProductRevenueMixRepository, EfCoreProductRevenueMixRepository>();
        services.AddScoped<ICompanyProductRevenueMixCalculator, CompanyProductRevenueMixCalculator>();
        services.AddScoped<IProductRevenueMixQueryUseCase, ProductRevenueMixQueryUseCase>();
        services.AddScoped<IProductRevenueMixBackfillService, ProductRevenueMixBackfillService>();
        // Spec 076 â€” company-month trend snapshot: calculated during ingestion and queryable for AI/chart.
        services.AddOptions<TrendSnapshotBackfillOptions>()
            .BindConfiguration(TrendSnapshotBackfillOptions.SectionName);
        services.AddScoped<ICompanyMonthlyActivityTrendSnapshotRepository, EfCoreCompanyMonthlyActivityTrendSnapshotRepository>();
        services.AddScoped<ICompanyMonthlyActivityTrendSnapshotCalculator, CompanyMonthlyActivityTrendSnapshotCalculator>();
        services.AddScoped<ICompanyMonthlyActivityTrendSnapshotBackfillService, CompanyMonthlyActivityTrendSnapshotBackfillService>();
        services.AddScoped<IMonthlyActivityTrendQueryUseCase, MonthlyActivityTrendQueryUseCase>();
        // Spec 112 — provider-neutral feed over persisted monthly reports and financial statements.
        services.AddScoped<ICompanyDisclosureFeedRepository, CompanyDisclosureFeedRepository>();
        services.AddScoped<IDisclosureListingUseCase, DisclosureListingUseCase>();
        // Spec 080 — deterministic monthly production/sales quality ranking snapshots.
        services.AddSingleton<IMonthlySalesQualityScoreCalculator, MonthlySalesQualityScoreCalculator>();
        services.AddScoped<IMonthlySalesQualityRankingRepository, MonthlySalesQualityRankingRepository>();
        services.AddScoped<IRecalculateMonthlySalesQualityRankingUseCase, RecalculateMonthlySalesQualityRankingUseCase>();
        services.AddScoped<IMonthlySalesQualityRankingQueryUseCase, MonthlySalesQualityRankingQueryUseCase>();
        // Spec 084 — proactive market event intelligence.
        services.AddSingleton<IInsightScoringService, DeterministicInsightScoringService>();
        services.AddSingleton<IInsightDeduplicationPolicy, InsightDeduplicationPolicy>();
        services.AddScoped<IInsightEventRepository, InsightEventRepository>();
        services.AddScoped<IFollowedSymbolInsightFeedRepository, InsightEventRepository>();
        services.AddScoped<IUserInsightStateRepository, UserInsightStateRepository>();
        services.AddScoped<IInsightDetector, MonthlyReportPublishedDetector>();
        services.AddScoped<IInsightDetector, MonthlySalesAnomalyDetector>();
        services.AddScoped<IInsightDetector, MonthlyQualityRankingChangeDetector>();
        services.AddScoped<IInsightDetector, PriceMovementDetector>();
        services.AddScoped<IInsightDetector, ComprehensiveAnalysisPublishedDetector>();
        services.AddScoped<IInsightDetector, FinancialStatementPublishedDetector>();
        services.AddScoped<IInsightDetector, SubscribedCodalAnnouncementDetector>();
        services.AddScoped<IInsightDetector, DataFreshnessDetector>();
        // Spec 092 — governed, deterministic market-microstructure detector policies.
        services.AddOptions<MarketMicrostructureOptions>()
            .BindConfiguration(MarketMicrostructureOptions.SectionName)
            .Validate(options =>
                    options.BatchSize is > 0 and <= 2_000 &&
                    options.BaselineLookback is > 1 and <= 120 &&
                    options.MinimumBaselineObservations is > 1 and <= 120 &&
                    options.MaximumSourceAgeMinutes > 0 &&
                    options.BuyerSellerPowerRatio > 1m &&
                    options.AnomalyRatio > 1m,
                "Market-microstructure detector settings must be positive and bounded.")
            .ValidateOnStart();
        services.AddSingleton<IMicrostructureSignalDetector, LargeTradeSignalDetector>();
        services.AddSingleton<IMicrostructureSignalDetector, BuyerSellerPowerSignalDetector>();
        services.AddSingleton<IMicrostructureSignalDetector, RealMoneyFlowSignalDetector>();
        services.AddSingleton<IMicrostructureSignalDetector, OrderQueueSignalDetector>();
        services.AddSingleton<IMicrostructureSignalDetector, VolumeAnomalySignalDetector>();
        services.AddSingleton<IMicrostructureSignalDetector, TradingValueAnomalySignalDetector>();
        services.AddScoped<MarketMicrostructureInsightDetector>();
        services.AddScoped<IInsightDetector>(provider => provider.GetRequiredService<MarketMicrostructureInsightDetector>());
        services.AddScoped<IGenerateMarketMicrostructureInsightsUseCase, GenerateMarketMicrostructureInsightsUseCase>();
        services.AddScoped<IGenerateMarketInsightsUseCase, GenerateMarketInsightsUseCase>();
        services.AddScoped<IGetMarketInsightFeedUseCase, GetMarketInsightFeedUseCase>();
        services.AddScoped<IGetMyFollowedSymbolInsightsUseCase, GetMyFollowedSymbolInsightsUseCase>();
        services.AddScoped<IMarkUserInsightSeenUseCase, MarkUserInsightSeenUseCase>();
        services.AddScoped<IDismissUserInsightUseCase, DismissUserInsightUseCase>();
        services.AddScoped<IExplainInsightUseCase, ExplainInsightUseCase>();
        // Spec 050 â€” all-index coverage: provider fetch (empty companyIndexIds) + non-scannable
        // staging normalizer (does not touch DerivedMetrics or the curated 041 path).
        services.AddScoped<IFundamentalIndexCoverageProvider>(sp =>
            sp.GetRequiredService<NadpcoApiDataProviderClient>());
        services.AddScoped<IFinancialPayloadNormalizer, NadpcoApiFundamentalIndexCoverageNormalizer>();
        services.AddScoped<ICompanyResolverService, CompanyResolverService>();
        services.AddScoped<ICyclicalWavesCompanyMappingService, CyclicalWavesCompanyMappingService>();
        services.AddScoped<IBackfillCyclicalWavesCompanyIdService, BackfillCyclicalWavesCompanyIdService>();
        services.AddScoped<IDerivedMetricRecalculationPublisher, StoredDerivedMetricRecalculationPublisher>();
        // LineItemMetricInputSource â€” one per source metric backed by NormalizedFinancialStatementLineItems.
        // NET_PROFIT subsumes the legacy NetProfitMetricInputSource; MonthlyProductionSales uses its own table.
        foreach (var code in new[] { "NET_PROFIT", "REVENUE", "GROSS_PROFIT", "OPERATING_PROFIT",
                                     "EPS", "TOTAL_EQUITY", "FINANCE_COSTS", "INCOME_TAX",
                                     "OPERATING_CASH_FLOW",
                                     // Vendor ratio snapshots â€” triggers PE_TTM / PS_TTM passthrough.
                                     "PE_RATIO", "PS_RATIO",
                                     // CyclicalWaves margin snapshots â€” triggers passthrough calculators below.
                                     "NET_PROFIT_MARGIN", "GROSS_PROFIT_MARGIN", "OPERATING_PROFIT_MARGIN",
                                     // CyclicalWaves pre-computed 4-quarter average revenue snapshot (Q0 only).
                                     "AVG_4Q_REVENUE" })
        {
            var captured = new MetricCode(code);
            services.AddScoped<INormalizedMetricInputSource>(sp =>
                new LineItemMetricInputSource(
                    sp.GetRequiredService<FinancialIngestionDbContext>(),
                    captured,
                    sp.GetRequiredService<ILogger<LineItemMetricInputSource>>()));
        }
        services.AddScoped<INormalizedMetricInputSource, MonthlyAvgSaleMetricInputSource>();
        services.AddScoped<INormalizedMetricInputSource, MonthlySalesMetricInputSource>();
        services.AddScoped<INormalizedMetricInputSource, MonthlySalesYtdMetricInputSource>();
        services.AddScoped<INormalizedMetricInputSource, MonthlySalesYtdPreviousMonthMetricInputSource>();
        // Spec 057: monthly-activity aggregates (sales quantity, production quantity,
        // quantity-weighted sales rate) backed by MonthlyReportLineItems.
        services.AddScoped<INormalizedMetricInputSource, MonthlySalesQuantityMetricInputSource>();
        services.AddScoped<INormalizedMetricInputSource, MonthlyProductionQuantityMetricInputSource>();
        services.AddScoped<INormalizedMetricInputSource, MonthlySalesRateMetricInputSource>();
        services.AddSingleton<IMonthlyActivityOutputTypeResolver, DefaultMonthlyActivityOutputTypeResolver>();
        services.AddScoped<INormalizedMetricInputReader, NormalizedMetricInputReader>();
        services.AddScoped<IDerivedMetricResultStore, PersistedDerivedMetricResultStore>();
        services.AddScoped<IDerivedMetricCalculationService, DerivedMetricCalculationService>();
        services.AddScoped<IDerivedMetricRecalculationCommand, DerivedMetricRecalculationCommand>();
        services.AddScoped<IFeatureDefinitionRegistry, PersistedFeatureDefinitionRegistry>();
        services.AddScoped<IFeatureSnapshotRepository, PersistedFeatureSnapshotRepository>();
        services.AddScoped<IFeatureQueryService>(provider =>
            provider.GetRequiredService<IFeatureSnapshotRepository>() as IFeatureQueryService ??
            throw new InvalidOperationException("Feature snapshot repository does not provide query services."));
        services.AddScoped<IFeatureComputationJobRepository, PersistedFeatureComputationJobRepository>();
        services.AddScoped<IFeatureInputReader, NoOpFeatureInputReader>();
        services.AddScoped<IDerivedFeatureCalculationService, DerivedFeatureCalculationService>();
        services.AddSingleton<IDerivedFeatureCalculator, FundPortfolioAnalyticsFeatureCalculator>();
        services.AddScoped<IFeatureRecalculationScheduler, FeatureRecalculationScheduler>();
        services.AddScoped<IFeatureComputationProcessor, FeatureComputationProcessor>();
        services.AddScoped<ICyclicalWavesFullSyncService, CyclicalWavesFullSyncService>();
        services.AddScoped<NoavaranEligibleCompanyPsScopeReader>();
        services.AddScoped<IPsEligibleCompanyScopeReader>(provider =>
            provider.GetRequiredService<NoavaranEligibleCompanyPsScopeReader>());
        services.AddScoped<CyclicalWavesPsVisualizationSyncService>();
        services.AddScoped<ICyclicalWavesPsVisualizationSyncService>(provider =>
            provider.GetRequiredService<CyclicalWavesPsVisualizationSyncService>());
        services.AddScoped<IFeature114AcceptedPsVisualizationPersistence>(provider =>
            provider.GetRequiredService<CyclicalWavesPsVisualizationSyncService>());
        services
            .AddOptions<IndustryRelativeValuationSourceOptions>()
            .BindConfiguration(IndustryRelativeValuationSourceOptions.SectionName)
            .Validate(options => options.MaximumCompaniesPerRun > 0 &&
                                 options.MaximumConcurrency is > 0 and <= 32 &&
                                 options.LeaseMinutes > 0,
                "Industry relative valuation source-ingestion options are invalid.")
            .ValidateOnStart();
        services.AddScoped<IIndustryRelativeValuationSourceIngestionService,
            IndustryRelativeValuationSourceIngestionService>();
        services.AddScoped<IEligibleUniverseReader, NoavaranEligibleCompanyUniverseReader>();
        services.AddScoped<IFeature126SourceFactStore, IndustryRelativeValuationSourceFactStore>();
        services.AddScoped<IFeature126LeaseStore, IndustryRelativeValuationLeaseStore>();
        services.AddScoped<IFeature126LeaseReadinessProbe>(provider =>
            (IndustryRelativeValuationLeaseStore)provider.GetRequiredService<IFeature126LeaseStore>());
        services.AddScoped<IFeature125HandoffConsumer, Feature125HandoffConsumer>();
        services.AddOptions<Feature126Options>()
            .BindConfiguration(Feature126Options.SectionName);
        services.AddOptions<RelativeValuationIngestionOptions>()
            .BindConfiguration(RelativeValuationIngestionOptions.SectionName)
            .Validate(options => options.DailyCadenceMinutes > 0 &&
                                 options.PageSize > 0 &&
                                 options.MaximumConcurrency is > 0 and <= 32 &&
                                 options.RetryCount is >= 0 and <= 10 &&
                                 options.CompanyTimeoutSeconds > 0 &&
                                 options.LeaseMinutes > 0,
                "Feature 126 ingestion options are invalid.")
            .ValidateOnStart();
        services.AddScoped<IFeature126RelativeValuationPipeline, RelativeValuationPipeline>();
        services.AddSingleton<IFeature126OperationalSummarySink, Feature126OperationalSummaryRegistry>();
        services.AddOptions<Feature126TelemetryOptions>()
            .BindConfiguration(Feature126TelemetryOptions.SectionName);
        services.AddHttpClient(nameof(SeqFeature126EventSink), (provider, client) =>
        {
            var settings = provider.GetRequiredService<IOptions<Feature126TelemetryOptions>>().Value;
            client.Timeout = TimeSpan.FromSeconds(Math.Max(1, settings.RequestTimeoutSeconds));
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseProxy = false });
        services.AddSingleton<SeqFeature126EventSink>(provider =>
            new SeqFeature126EventSink(
                provider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(SeqFeature126EventSink)),
                provider.GetRequiredService<IOptions<Feature126TelemetryOptions>>().Value,
                provider.GetRequiredService<TimeProvider>(),
                (exception, endpoint) => provider.GetRequiredService<ILogger<SeqFeature126EventSink>>()
                    .LogWarning(exception, "Feature 126 telemetry probe failed for Seq endpoint {SeqEndpoint}.", endpoint),
                (statusCode, responseBody, endpoint, eventType) => provider.GetRequiredService<ILogger<SeqFeature126EventSink>>()
                    .LogInformation("Feature 126 telemetry export: endpoint {SeqEndpoint}, event type {EventType}, HTTP {StatusCode}, response body {ResponseBody}.", endpoint, eventType, statusCode, responseBody),
                (exception, endpoint, eventType) => provider.GetRequiredService<ILogger<SeqFeature126EventSink>>()
                    .LogError(exception, "Feature 126 telemetry export failed: endpoint {SeqEndpoint}, event type {EventType}, exception type {ExceptionType}, message {ExceptionMessage}.", endpoint, eventType, exception.GetType().FullName, exception.Message)));
        services.AddScoped<IFeature126DurableEventSink, Feature126PostgresEventSink>();
        services.AddScoped<IFeature126EventAppender, Feature126EventAppender>();
        services.AddScoped<RuntimeActivationGate, Feature126RuntimeActivationGate>();
        services.AddScoped<IIndustryRelativeValuationOrchestrationService,
            IndustryRelativeValuationOrchestrationService>();
        services.AddScoped<IFeature125HandoffSubmissionBoundary>(provider =>
            provider.GetRequiredService<IIndustryRelativeValuationOrchestrationService>()
                as IFeature125HandoffSubmissionBoundary ??
            throw new InvalidOperationException("Feature 125 orchestration does not implement the handoff boundary."));
        services.AddScoped<IndustryRelativeValuationCalculationInputBuilder>();
        services.AddScoped<IndustryRelativeValuationCalculationSnapshotWriter>();
        services.AddOptions<IndustryWatchOptions>()
            .BindConfiguration("IndustryRelativeValuation")
            .Validate(options => options.EntryConsecutiveSnapshots is >= 1 and <= 30 &&
                                 options.ExitConsecutiveSnapshots is >= 1 and <= 30,
                "Industry watch thresholds must be between 1 and 30.")
            .ValidateOnStart();
        services.AddScoped(provider =>
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<IndustryWatchOptions>>().Value);
        services.AddScoped<IndustryWatchEvaluationService>();
        services.AddScoped<ICompanyPsVisualizationReader>(provider =>
            provider.GetRequiredService<CyclicalWavesPsVisualizationSyncService>());
        services.AddOptions<CyclicalWavesPsVisualizationOptions>()
            .BindConfiguration(CyclicalWavesPsVisualizationOptions.SectionName)
            .Validate(options => options.MaxSyncAgeHours > 0 && options.MaxObservationLagTradingDays >= 0 &&
                                 options.MaxHistoryPoints is > 0 and <= 10_000 &&
                                 options.DisplayPercentageDecimals is >= 0 and <= 6,
                "CyclicalWaves P/S visualization options are invalid.")
            .ValidateOnStart();
        services.AddScoped<IPsVisualizationExperienceUseCase, PsVisualizationExperienceUseCase>();
        services.AddScoped<IMetricRecalculationProcessor, MetricRecalculationProcessor>();
        services.AddScoped<IAssistedQueryMetadataService, EfCoreAssistedQueryMetadataService>();
        services.AddScoped<ICodalDbSyncStateStore, EfCoreCodalDbSyncStateStore>();
        services.AddScoped<ICodalDbScheduledSyncService, CodalDbScheduledSyncService>();
        services.AddScoped<INadpcoApiSyncStateStore, EfCoreNadpcoApiSyncStateStore>();
        services.AddScoped<INadpcoCompanyCatalogCleanSlateService, NadpcoCompanyCatalogCleanSlateService>();
        services.AddScoped<NadpcoApiScheduledSyncService>();
        services.AddScoped<INadpcoApiScheduledSyncService>(provider =>
            provider.GetRequiredService<NadpcoApiScheduledSyncService>());
        services.AddScoped<INadpcoApiSyncStateReader>(provider =>
            provider.GetRequiredService<NadpcoApiScheduledSyncService>());
        // Spec 057 â€” manual reverse-chronological monthly-activity backfill (DataAdmin-only) and
        // the backfill-complete marker that gates the steady-state previous-month refresh.
        services.AddScoped<MonthlyActivityBackfillCoordinator>();
        services.AddScoped<IMonthlyActivityBackfillCoordinator>(provider =>
            provider.GetRequiredService<MonthlyActivityBackfillCoordinator>());
        services.AddScoped<IMonthlyActivityBackfillStateReader>(provider =>
            provider.GetRequiredService<MonthlyActivityBackfillCoordinator>());
        services.AddScoped<ISingleCompanyMonthlyIngestionService, SingleCompanyMonthlyIngestionService>();
        services
            .AddOptions<NadpcoScheduledSyncOptions>()
            .BindConfiguration(NadpcoScheduledSyncOptions.SectionName);
        services.AddScoped<EfCoreNadpcoScheduledSyncRunRepository>();
        services.AddScoped<INadpcoScheduledSyncRunReader>(provider =>
            provider.GetRequiredService<EfCoreNadpcoScheduledSyncRunRepository>());
        services.AddScoped<INadpcoScheduledSyncCoordinator, NadpcoScheduledSyncCoordinator>();
        services.AddSingleton<INadpcoScheduledSyncAlertSink, LoggingNadpcoScheduledSyncAlertSink>();

        // Spec 052 â€” one-time Noavaran archive import (DataAdmin-triggered; no recurring worker).
        services.AddScoped<EfCoreArchiveImportRunRepository>();
        services.AddScoped<IArchiveImportRunReader>(provider =>
            provider.GetRequiredService<EfCoreArchiveImportRunRepository>());
        services.AddScoped<IArchiveFreezeStateStore, EfCoreArchiveFreezeStateStore>();
        services.AddScoped<IArchiveCoverageReader, EfCoreArchiveCoverageReader>();
        services.AddScoped<IArchiveImportCoordinator, ArchiveImportCoordinator>();

        // Spec 053 â€” Noavaran current-API ingestion: archive-vs-current gap report + DataAdmin
        // backfill (one-off Shamsi boundary override) + separate current-API health. Reuses the
        // existing current-API scheduled-sync orchestration (single ingestion path).
        services.AddScoped<ICurrentApiGapReader, EfCoreCurrentApiGapReader>();
        services.AddScoped<ICurrentApiBackfillCoordinator, CurrentApiBackfillCoordinator>();
        services.AddScoped<INoavaranEligibleCompanyReferenceReader, NoavaranEligibleCompanyViewReader>();
        services.AddScoped<IEligibleFundamentalIndexBulkSyncService, EligibleFundamentalIndexBulkSyncService>();

        // Spec 050 â€” all-index fundamental-index catch-up coverage (DataAdmin-only; no recurring worker).
        services.AddScoped<EfCoreFundamentalIndexCatchUpRunRepository>();
        services.AddScoped<IFundamentalIndexCatchUpRunReader>(provider =>
            provider.GetRequiredService<EfCoreFundamentalIndexCatchUpRunRepository>());
        services.AddScoped<IFundamentalIndexCatchUpCoordinator, FundamentalIndexCatchUpCoordinator>();
        services
            .AddOptions<StockMarketDbProviderOptions>()
            .BindConfiguration(StockMarketDbProviderOptions.SectionName);
        services.AddSingleton<StockMarketDbConnectionFactory>();
        services.AddSingleton<StockMarketDbSqlResilience>();
        services.AddScoped<IStockMarketDbQueryExecutor, SqlStockMarketDbQueryExecutor>();
        services.AddScoped<StockMarketDbSyncService>();
        services.AddScoped<IStockMarketDbSyncService>(provider =>
            provider.GetRequiredService<StockMarketDbSyncService>());
        services.AddScoped<IStockMarketDbSyncStateReader>(provider =>
            provider.GetRequiredService<StockMarketDbSyncService>());
        services.AddScoped<IStockMarketHistoryRetentionService, StockMarketHistoryRetentionService>();
        // Spec 054 Phase 2 â€” direct TSETMC web-service feed. When TsetmcWebService:Enabled=false
        // (or credentials absent), NullTsetmcDirectFeedSyncService is wired and all sync operations
        // are no-ops. Set Enabled=true + UserName/Password to activate the real adapter.
        services
            .AddOptions<TsetmcWebServiceOptions>()
            .BindConfiguration(TsetmcWebServiceOptions.SectionName);
        services.AddHttpClient<ITsetmcWebServiceClient, TsetmcWebServiceClient>((provider, client) =>
        {
            var opts = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<TsetmcWebServiceOptions>>().Value;
            client.BaseAddress = new Uri(opts.ServiceUrl);
            client.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds);
        });
        services.AddScoped<TsetmcDirectFeedSyncService>();
        services.AddScoped<ITsetmcDirectFeedSyncService>(provider =>
        {
            var opts = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<TsetmcWebServiceOptions>>().Value;
            return opts.Enabled && !string.IsNullOrWhiteSpace(opts.UserName)
                ? provider.GetRequiredService<TsetmcDirectFeedSyncService>()
                : new NullTsetmcDirectFeedSyncService();
        });
        services.AddScoped<ITsetmcSyncStateReader>(provider =>
        {
            var opts = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<TsetmcWebServiceOptions>>().Value;
            return opts.Enabled && !string.IsNullOrWhiteSpace(opts.UserName)
                ? provider.GetRequiredService<TsetmcDirectFeedSyncService>()
                : new NullTsetmcDirectFeedSyncService();
        });
        services.AddScoped<ITsetmcValidationService, TsetmcValidationService>();
        services.AddScoped<IMarketQuoteMismatchReader, MarketQuoteMismatchReader>();
        services
            .AddOptions<MarketQuoteSourcePriorityOptions>()
            .BindConfiguration(MarketQuoteSourcePriorityOptions.SectionName);
        services.AddSingleton<IMarketQuoteSourcePriority, ConfiguredMarketQuoteSourcePriority>();
        services
            .AddOptions<MarketViewOptions>()
            .BindConfiguration(MarketViewOptions.SectionName)
            .Validate(options =>
                    options.StaleAfterMinutes > 0 &&
                    options.PulseCadenceMinutes > 0 &&
                    options.PulseIndustryDriverCount > 0 &&
                    options.PulseHistoryPageSize > 0 &&
                    options.PulseSegments.Length > 0,
                "Market view and market-pulse settings must be positive and include at least one segment.")
            .ValidateOnStart();
        services.AddSingleton<IMarketViewCache, MemoryMarketViewCache>();
        services.AddScoped<IWatchlistService, WatchlistService>();
        services.AddScoped<IMarketSummaryService, MarketSummaryService>();
        services.AddScoped<MarketPulseService>();
        services.AddScoped<IMarketPulseService>(provider => provider.GetRequiredService<MarketPulseService>());
        services.AddScoped<IMarketPulseSnapshotGenerator>(provider => provider.GetRequiredService<MarketPulseService>());
        services
            .AddOptions<MarketReportOptions>()
            .BindConfiguration(MarketReportOptions.SectionName)
            .Validate(options =>
                    options.ScheduleCadenceMinutes > 0 &&
                    options.MaximumPublicInsights > 0 &&
                    options.MaximumPersonalInsights > 0 &&
                    options.PersonalDailyGenerationLimit > 0 &&
                    options.LeaseMinutes > 0 &&
                    options.MaximumAttempts > 0 &&
                    options.Segments.Length > 0,
                "Market report limits, lease, attempts, and segments must be configured with positive values.")
            .ValidateOnStart();
        services.AddScoped<MarketReportEvidenceAssembler>();
        services.AddScoped<MarketReportNarrativePolicy>();
        services.AddScoped<MarketReportService>();
        services.AddScoped<IMarketReportService>(provider => provider.GetRequiredService<MarketReportService>());
        services.AddScoped<IMarketReportScheduler>(provider => provider.GetRequiredService<MarketReportService>());
        services.AddScoped<IFollowedSymbolRepository, EfCoreFollowedSymbolRepository>();
        services.AddScoped<IFollowedCompanyResolver, EfCoreFollowedCompanyResolver>();
        services.AddScoped<IGetMyFollowedSymbolsUseCase, GetMyFollowedSymbolsUseCase>();
        services.AddScoped<IFollowSymbolUseCase, FollowSymbolUseCase>();
        services.AddScoped<IUnfollowSymbolUseCase, UnfollowSymbolUseCase>();
        services.AddScoped<IReplaceMyFollowedSymbolsUseCase, ReplaceMyFollowedSymbolsUseCase>();
        services.AddScoped<ICodalAlertSubscriptionRepository, EfCoreCodalAlertSubscriptionRepository>();
        services.AddScoped<IGetMyCodalAlertSubscriptionsUseCase, GetMyCodalAlertSubscriptionsUseCase>();
        services.AddScoped<ICreateCodalAlertSubscriptionUseCase, CreateCodalAlertSubscriptionUseCase>();
        services.AddScoped<IUpdateCodalAlertSubscriptionUseCase, UpdateCodalAlertSubscriptionUseCase>();
        services.AddScoped<IDeleteCodalAlertSubscriptionUseCase, DeleteCodalAlertSubscriptionUseCase>();
        services.AddScoped<IGenerateCodalAlertSummaryUseCase, GenerateCodalAlertSummaryUseCase>();
        services.AddScoped<INotificationIntentPublisher, EfCoreNotificationIntentPublisher>();
        services.AddOptions<NotificationDispatcherOptions>()
            .BindConfiguration(NotificationDispatcherOptions.SectionName)
            .Validate(options => options.IntervalSeconds > 0 && options.BatchSize is > 0 and <= 1000 &&
                                 options.LeaseSeconds is >= 30 and <= 600 &&
                                 options.MaximumAttempts is > 0 and <= 20 &&
                                 options.InitialBackoffSeconds > 0 &&
                                 options.MaximumBackoffSeconds >= options.InitialBackoffSeconds &&
                                 options.DigestMaximumItems is > 0 and <= 100 &&
                                 options.MessagePartLength is >= 500 and <= 4000 &&
                                 options.TransportErrorRetentionDays is > 0 and <= 365 &&
                                 options.DeliveryAuditRetentionDays >= options.TransportErrorRetentionDays,
                "Notification dispatcher settings must be positive and bounded.")
            .ValidateOnStart();
        services.AddOptions<AlertHistoryOptions>()
            .BindConfiguration(AlertHistoryOptions.SectionName)
            .Validate(options => options.IntervalSeconds is > 0 and <= 3600 &&
                                 options.HandoffBatchSize is > 0 and <= 1000 &&
                                 options.EvidenceRetentionDays is >= 30 and <= 3650 &&
                                 options.FeedbackRetentionDays is >= 30 and <= 3650 &&
                                 options.MaximumPageSize is > 0 and <= 500 &&
                                 options.MaximumQueryRangeDays is > 0 and <= 3650,
                "Alert history settings must be positive and bounded.")
            .ValidateOnStart();
        services.AddScoped<INotificationEntitlementPolicy, NotificationEntitlementPolicy>();
        services.AddScoped<INotificationRecipientResolver, NotificationRecipientResolver>();
        services.AddScoped<INotificationUseCases, NotificationUseCases>();
        services.AddScoped<INotificationDispatcher, NotificationDispatcher>();
        services.AddScoped<INotificationOperations, NotificationOperations>();
        services.AddScoped<AlertHistoryUseCases>();
        services.AddScoped<IAlertHistoryUseCases>(provider => provider.GetRequiredService<AlertHistoryUseCases>());
        services.AddScoped<IAlertOutcomeHandoffProcessor>(provider => provider.GetRequiredService<AlertHistoryUseCases>());
        services.AddScoped<ITelegramNotificationTransport, TelegramNotificationTransport>();
        services.AddScoped<IAlertRuleRepository, EfCoreAlertRuleRepository>();
        services.AddScoped<IGovernedAlertRuleParser, GovernedAlertRuleParser>();
        services.AddScoped<IConditionalTrackerEntitlementPolicy, ConditionalTrackerEntitlementPolicy>();
        services.AddScoped<IConditionalTrackerUseCases, ConditionalTrackerUseCases>();
        services.AddScoped<IConditionalTrackerEvaluationProcessor, ConditionalTrackerEvaluationProcessor>();
        services.AddOptions<RadarOptions>()
            .BindConfiguration(RadarOptions.SectionName);
        services.AddScoped<IRadarRepository, RadarRepository>();
        services.AddScoped<IRadarEntitlementPolicy, RadarEntitlementPolicy>();
        services.AddScoped<IRadarNotificationPolicyGate, AllowRadarNotificationPolicyGate>();
        services.AddScoped<IRadarUseCases, RadarUseCases>();
        services.AddScoped<IRadarEvaluationProcessor, RadarEvaluationProcessor>();
        services.AddSingleton<IProfessionalFilterCatalog, GovernedProfessionalFilterCatalog>();
        services.AddScoped<ISavedFilterRepository, SavedFilterRepository>();
        services.AddScoped<IProfessionalScannerEntitlementPolicy, ProfessionalScannerEntitlementPolicy>();
        services.AddScoped<IProfessionalScannerUseCases, ProfessionalScannerUseCases>();
        services.AddScoped<PersistedMarketDataProvider>();
        services.AddScoped<IMarketDataProvider>(provider =>
            provider.GetRequiredService<PersistedMarketDataProvider>());

        // Missing-answer feedback (spec 028). Phase 1 default: real repository, no-op collector
        // (so production has zero collection overhead until MissingAnswerFeedback:Enabled=true).
        services
            .AddOptions<MissingAnswerFeedbackOptions>()
            .BindConfiguration(MissingAnswerFeedbackOptions.SectionName);
        services.AddScoped<IMissingAnswerFeedbackRepository, EfCoreMissingAnswerFeedbackRepository>();
        services.AddSingleton<NoOpMissingAnswerFeedbackCollector>();
        services.AddSingleton<AsyncFireAndForgetMissingAnswerFeedbackCollector>();
        services.AddSingleton<IMissingAnswerFeedbackCollector>(provider =>
        {
            var settings = provider.GetRequiredService<IOptions<MissingAnswerFeedbackOptions>>().Value;
            return settings.Enabled
                ? provider.GetRequiredService<AsyncFireAndForgetMissingAnswerFeedbackCollector>()
                : provider.GetRequiredService<NoOpMissingAnswerFeedbackCollector>();
        });
        services.AddScoped<FinancialDataSyncProcessor>();
        services.AddScoped<IFinancialDataSyncProcessor>(provider =>
            provider.GetRequiredService<FinancialDataSyncProcessor>());
        services.AddScoped<IDataSyncRunReader>(provider =>
            provider.GetRequiredService<FinancialDataSyncProcessor>());
        services.AddOptions<RabbitMqDataSyncOptions>()
            .BindConfiguration(RabbitMqDataSyncOptions.SectionName)
            .Validate(
                options => options.ConsumerCount is > 0 and <= 32,
                "DataSyncMessaging:ConsumerCount must be between 1 and 32.")
            .ValidateOnStart();
        services.AddSingleton<RabbitMqDataSyncRequestBus>();
        services.AddSingleton<IDataSyncRequestPublisher>(provider =>
            provider.GetRequiredService<RabbitMqDataSyncRequestBus>());
        services.AddSingleton<IDataSyncRequestConsumer>(provider =>
            provider.GetRequiredService<RabbitMqDataSyncRequestBus>());
        services.AddOptions<RabbitMqFeatureOptions>()
            .BindConfiguration(RabbitMqFeatureOptions.SectionName);
        services.AddSingleton<RabbitMqFeatureBus>();
        services.AddSingleton<IFeatureRecalculationPublisher>(provider =>
            provider.GetRequiredService<RabbitMqFeatureBus>());
        services.AddSingleton<IFeatureRecalculationConsumer>(provider =>
            provider.GetRequiredService<RabbitMqFeatureBus>());

        // Spec 058 â€” live data sync monitor: scoped reader aggregates all provider run states;
        // singleton polling monitor fan-outs SSE events to connected admin clients.
        services.AddScoped<IDataSyncActivityReader, EfCoreDataSyncActivityReader>();
        services
            .AddOptions<DataSyncMonitorOptions>()
            .BindConfiguration(DataSyncMonitorOptions.SectionName);
        services.AddSingleton<PollingDataSyncActivityMonitor>();
        services.AddSingleton<IDataSyncActivityMonitor>(provider =>
            provider.GetRequiredService<PollingDataSyncActivityMonitor>());
        services.AddHostedService(provider =>
            provider.GetRequiredService<PollingDataSyncActivityMonitor>());

        return services;
    }

    private static AiModelProviderDescriptor ToDescriptor(AiModelProviderRegistration registration) =>
        new(
            registration.ProviderKey,
            registration.ModelKey,
            registration.HostingMode,
            registration.Capabilities,
            registration.Enabled,
            registration.Priority,
            registration.AllowedTenantIds.Count == 0
                ? null
                : registration.AllowedTenantIds.ToHashSet(),
            registration.DataResidency,
            registration.AllowSensitivePrompts);

    private static AiModelProviderRegistration? GetRegistration(
        AiModelProviderOptions options,
        string adapter) =>
        options.Providers.FirstOrDefault(item =>
            string.Equals(item.Adapter, adapter, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.ProviderKey, adapter, StringComparison.OrdinalIgnoreCase));

    private static AiModelProviderRegistration? CreateOpenAiRegistration(AiProviderOptions options) =>
        HasProviderConfiguration(options, "OpenAI")
            ? new AiModelProviderRegistration
            {
                ProviderKey = "OpenAI",
                ModelKey = options.OpenAI.Model,
                HostingMode = AiProviderHostingMode.Hosted,
                Adapter = "OpenAI",
                Endpoint = "https://api.openai.com/v1/",
                Enabled = true,
                Priority = 10,
                Capabilities = AiModelCapability.ChatCompletion |
                    AiModelCapability.StructuredOutput |
                    AiModelCapability.ToolCalling |
                    AiModelCapability.Streaming |
                    AiModelCapability.UsageReporting |
                    AiModelCapability.HealthCheck,
                TimeoutSeconds = 120
            }
            : null;

    private static AiModelProviderRegistration? CreateDeepSeekRegistration(AiProviderOptions options) =>
        HasProviderConfiguration(options, "DeepSeek")
            ? new AiModelProviderRegistration
            {
                ProviderKey = "DeepSeek",
                ModelKey = options.DeepSeek.Model,
                HostingMode = AiProviderHostingMode.Hosted,
                Adapter = "DeepSeek",
                Endpoint = options.DeepSeek.BaseUrl,
                Enabled = true,
                Priority = 10,
                Capabilities = AiModelCapability.ChatCompletion |
                    AiModelCapability.StructuredOutput |
                    AiModelCapability.ToolCalling |
                    AiModelCapability.Streaming |
                    AiModelCapability.UsageReporting |
                    AiModelCapability.HealthCheck,
                TimeoutSeconds = 120
            }
            : null;

    private static bool HasProviderConfiguration(AiProviderOptions options, string providerKey) =>
        string.Equals(options.DefaultProvider, providerKey, StringComparison.OrdinalIgnoreCase);
}
