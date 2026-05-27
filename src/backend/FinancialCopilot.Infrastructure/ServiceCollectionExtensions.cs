using FinancialCopilot.Billing.Contracts;
using FinancialCopilot.Billing.Pricing;
using FinancialCopilot.Billing.Services;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Conversations;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Metrics;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.Metrics;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using FinancialCopilot.Application.AI.Observability;
using FinancialCopilot.Infrastructure.AI.ModelProviders;
using FinancialCopilot.Infrastructure.AI.Observability;
using FinancialCopilot.Infrastructure.Conversations.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers;
using FinancialCopilot.Infrastructure.Financial.Providers.Persistence;
using FinancialCopilot.Infrastructure.Financial.Semantics.Persistence;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Messaging;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Scanner;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
        services.AddDbContext<SemanticCatalogDbContext>(options => options.UseNpgsql(connectionString));
        services.AddDbContext<FinancialProviderDbContext>(options => options.UseNpgsql(connectionString));
        services.AddDbContext<FinancialIngestionDbContext>(options => options.UseNpgsql(connectionString));
        services.AddDbContext<ConversationDbContext>(options => options.UseNpgsql(connectionString));

        services.AddScoped<ICustomerAccountRepository, CustomerAccountRepository>();
        services.AddScoped<IWalletProjectionRepository, WalletProjectionRepository>();
        services.AddScoped<IWalletService>(provider =>
            provider.GetRequiredService<IWalletProjectionRepository>());
        services.AddScoped<IUsageReservationRepository, UsageReservationRepository>();
        services.AddScoped<IUsageLedgerRepository, UsageLedgerRepository>();
        services.AddScoped<IFinancialTransactionRepository, UsageLedgerRepository>();
        services.AddScoped<IInvoiceAccountRepository, InvoiceAccountRepository>();
        services.AddScoped<ISubscriptionPlanRepository, SubscriptionPlanRepository>();

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
                "v1"));
        services.AddScoped<IPartnerAccountService, PartnerAccountService>();
        services.AddScoped<IBillingAdministrationService, BillingAdministrationService>();
        services.AddScoped<IInvoiceService, InvoiceService>();
        services.AddScoped<IApiUsageReportService, ApiUsageReportService>();
        services.AddScoped<ISubscriptionService, SubscriptionService>();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new PricingPolicy(
            "v1",
            new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["AiQuery.Scanner"] = 1m,
                ["AiQuery.CachedResponse"] = 0.2m,
                ["AiQuery.FinancialComparison"] = 3m,
                ["AiQuery.DeepResearch"] = 15m,
                ["AiQuery.CodalAnalysis"] = 8m,
                ["AiQuery.Summarization"] = 4m,
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
        services.AddSingleton<IAiModelClient>(_ => new ContractPendingAiModelClient(
            new AiModelProviderDescriptor(
                "Abravran",
                "contract-pending",
                AiProviderHostingMode.ContractPending,
                AiModelCapability.None,
                Enabled: false,
                Priority: int.MaxValue)));
        services.AddSingleton<CapabilityBasedAiModelProviderResolver>();
        services.AddSingleton<IAiModelProviderResolver>(provider =>
            provider.GetRequiredService<CapabilityBasedAiModelProviderResolver>());
        services.AddSingleton<IAiProviderCapabilityRegistry>(provider =>
            provider.GetRequiredService<CapabilityBasedAiModelProviderResolver>());
        services.AddSingleton<IAiModelExecutionService, AiModelExecutionService>();

        services.AddScoped<IConversationRepository, ConversationRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<IAiIntentDetector, LlmAiIntentDetector>();
        services.AddScoped<IScannerQueryParser, LlmScannerQueryParser>();
        services.AddScoped<IScannerQueryPlanValidator, ScannerQueryPlanValidator>();
        services.AddScoped<IScannerResultColumnPolicy, ScannerResultColumnPolicy>();
        services.AddScoped<IScannerResultRanker, ScannerResultRanker>();
        services.AddScoped<IMarketQuoteResolver, ProviderMarketQuoteResolver>();
        services.AddScoped<IScannerExecutionService, EfCoreScannerExecutionService>();
        services.AddScoped<IBillingFacadeHook, NoOpBillingFacadeHook>();
        services.AddScoped<IAiQueryOrchestrationService, AiQueryOrchestrationService>();

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
        services.AddSingleton<IFinancialMetricCalculator>(_ => new TrailingTwelveMonthSumMetricCalculator(
            new MetricCode("TTM_EARNINGS"),
            new MetricCode("NET_PROFIT"),
            requiredObservationCount: 4));
        services.AddSingleton<IFinancialMetricCalculator>(_ => new EarningsPerShareMetricCalculator(
            new MetricCode("TTM_EPS"),
            new MetricCode("TTM_EARNINGS"),
            new MetricCode("SHARES_OUTSTANDING")));
        services.AddSingleton<IFinancialMetricCalculator>(_ => new ValuationRatioMetricCalculator(
            new MetricCode("PE_TTM"),
            new MetricCode("LATEST_PRICE"),
            new MetricCode("TTM_EPS")));
        services.AddSingleton<IFinancialMetricCalculator>(_ => new ValuationRatioMetricCalculator(
            new MetricCode("PS_TTM"),
            new MetricCode("MARKET_CAP"),
            new MetricCode("TTM_SALES")));
        services.AddSingleton<FinancialMetricRegistry>(provider =>
            new FinancialMetricRegistry(
                PhaseOneFinancialSemanticCatalog.Definitions,
                provider.GetServices<IFinancialMetricCalculator>()));
        services.AddSingleton<IFinancialMetricRegistry>(provider =>
            provider.GetRequiredService<FinancialMetricRegistry>());
        services.AddSingleton<IMetricDependencyResolver>(provider =>
            provider.GetRequiredService<FinancialMetricRegistry>());
        services.AddSingleton<IMetricAliasResolver, MetricAliasResolver>();
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
        services.AddScoped<MockFinancialDataProvider>();
        services.AddScoped<ISymbolDataProvider>(provider =>
            provider.GetRequiredService<MockFinancialDataProvider>());
        services.AddScoped<IFinancialStatementProvider>(provider =>
            provider.GetRequiredService<MockFinancialDataProvider>());
        services.AddScoped<IMonthlyProductionSalesProvider>(provider =>
            provider.GetRequiredService<MockFinancialDataProvider>());
        services.AddScoped<IMarketDataProvider>(provider =>
            provider.GetRequiredService<MockFinancialDataProvider>());
        services.AddScoped<IFinancialDataProviderHealthService>(provider =>
            provider.GetRequiredService<MockFinancialDataProvider>());

        services.AddScoped<IFinancialPayloadNormalizer, SymbolPayloadNormalizer>();
        services.AddScoped<IFinancialPayloadNormalizer, FinancialStatementPayloadNormalizer>();
        services.AddScoped<IFinancialPayloadNormalizer, MonthlyReportPayloadNormalizer>();
        services.AddScoped<IDerivedMetricRecalculationPublisher, StoredDerivedMetricRecalculationPublisher>();
        services.AddScoped<INormalizedMetricInputSource, NetProfitMetricInputSource>();
        services.AddScoped<INormalizedMetricInputSource, MonthlySalesMetricInputSource>();
        services.AddScoped<INormalizedMetricInputReader, NormalizedMetricInputReader>();
        services.AddScoped<IDerivedMetricResultStore, PersistedDerivedMetricResultStore>();
        services.AddScoped<IDerivedMetricCalculationService, DerivedMetricCalculationService>();
        services.AddScoped<IDerivedMetricRecalculationCommand, DerivedMetricRecalculationCommand>();
        services.AddScoped<FinancialDataSyncProcessor>();
        services.AddScoped<IFinancialDataSyncProcessor>(provider =>
            provider.GetRequiredService<FinancialDataSyncProcessor>());
        services.AddScoped<IDataSyncRunReader>(provider =>
            provider.GetRequiredService<FinancialDataSyncProcessor>());
        services.AddOptions<RabbitMqDataSyncOptions>()
            .BindConfiguration(RabbitMqDataSyncOptions.SectionName);
        services.AddSingleton<RabbitMqDataSyncRequestBus>();
        services.AddSingleton<IDataSyncRequestPublisher>(provider =>
            provider.GetRequiredService<RabbitMqDataSyncRequestBus>());
        services.AddSingleton<IDataSyncRequestConsumer>(provider =>
            provider.GetRequiredService<RabbitMqDataSyncRequestBus>());

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
}
