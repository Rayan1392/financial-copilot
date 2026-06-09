using System.Net.Http.Headers;
using FinancialCopilot.Billing.Contracts;
using FinancialCopilot.Billing.Pricing;
using FinancialCopilot.Billing.Services;
using FinancialCopilot.Application.AI.Evaluation;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Conversations;
using FinancialCopilot.Application.Memory;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.MarketViews;
using FinancialCopilot.Application.FinancialData.Metrics;
using FinancialCopilot.Application.FinancialData.Features;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.Metrics;
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
using FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWaves;
using FinancialCopilot.Infrastructure.Financial.Providers.Persistence;
using FinancialCopilot.Infrastructure.Financial.Semantics.Persistence;
using FinancialCopilot.Infrastructure.Financial.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Messaging;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Ingestion.StockMarketDb;
using FinancialCopilot.Infrastructure.Financial.MarketViews;
using FinancialCopilot.Infrastructure.Financial.Features;
using FinancialCopilot.Infrastructure.Financial.Features.Messaging;
using FinancialCopilot.Infrastructure.Financial.Scanner;
using FinancialCopilot.Infrastructure.Financial.Metadata;
using FinancialCopilot.Application.FinancialData.Metadata;
using FinancialCopilot.Application.Administration;
using FinancialCopilot.Infrastructure.Administration;
using FinancialCopilot.Infrastructure.AI.Consistency;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Adapters;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Config;
using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Functions;
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
        services.AddScoped<OwnedIdentityBillingProvisioner>();
        services.AddScoped<IAdminManagementService, EfCoreAdminManagementService>();

        services.AddOptions<ScannerCacheOptions>()
            .Bind(configuration.GetSection(ScannerCacheOptions.SectionName));
        var scannerCacheSettings = configuration
            .GetSection(ScannerCacheOptions.SectionName)
            .Get<ScannerCacheOptions>() ?? new ScannerCacheOptions();
        if (scannerCacheSettings.UseRedis)
        {
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = scannerCacheSettings.RedisConfiguration;
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
                    string.Equals(options.DefaultProvider, "DeepSeek", StringComparison.OrdinalIgnoreCase),
                "AiProvider:DefaultProvider must be OpenAI or DeepSeek when configured.")
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
            //****AI AGENT*** ATTENTION PLEASE: DO NOT CHANGE THIS CODE
            var apiKey = registration?.CredentialSecretReference
            is not null
                ? Environment.GetEnvironmentVariable(registration.CredentialSecretReference)
                : null;
            //*****************************************************
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            }
            else if (!string.IsNullOrWhiteSpace(providerSettings.OpenAI.ApiKey))
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", providerSettings.OpenAI.ApiKey);
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
        services.AddSingleton<IAiModelProviderDiagnostics>(provider =>
            provider.GetRequiredService<CapabilityBasedAiModelProviderResolver>());
        services.AddSingleton<IAiModelExecutionService, AiModelExecutionService>();

        services.AddScoped<IConversationRepository, ConversationRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<EfCoreMemoryRecordRepository>();
        services.AddScoped<IMemoryConsentService, EfCoreMemoryConsentService>();
        services.AddScoped<IMemoryAuditService, EfCoreMemoryAuditService>();
        services.AddScoped<IMemoryControlService, EfCoreMemoryControlService>();
        services.AddScoped<IMemoryContextProvider, EfCoreMemoryContextProvider>();
        services.AddSingleton<IMemoryProtectionPolicy, ConsentAwareMemoryProtectionPolicy>();
        services.AddScoped<IAiIntentDetector, LlmAiIntentDetector>();
        services.AddScoped<ISymbolLookupParser, LlmSymbolLookupParser>();
        services.AddScoped<ISymbolNameResolver, EfCoreSymbolNameResolver>();
        services.AddScoped<ISymbolMetricLookupService, EfCoreSymbolMetricLookupService>();
        services.AddScoped<IScannerQueryParser, LlmScannerQueryParser>();
        services.AddScoped<IScannerQueryPlanValidator, ScannerQueryPlanValidator>();
        services.AddScoped<IScannerResultColumnPolicy, ScannerResultColumnPolicy>();
        services.AddScoped<IScannerResultRanker, ScannerResultRanker>();
        services.AddScoped<IMarketQuoteResolver, ProviderMarketQuoteResolver>();
        services.AddScoped<IScannerExecutionService, EfCoreScannerExecutionService>();
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
            services.AddScoped<ExplainableAnswerAdapter>();
            services.AddScoped<MemoryContextAdapter>();
            services.AddScoped<BillingFunctions>();
            services.AddScoped<MessagePersistenceFunction>();
            services.AddScoped<MissingAnswerFeedbackFunction>();
            services.AddSingleton<FinancialCopilotAgentFactory>(sp =>
                new FinancialCopilotAgentFactory(sp.GetService<ILoggerFactory>()));
            services.AddScoped<FinancialCopilotAgentWorkflowRunner>();
            services.AddScoped<IAiQueryOrchestrationService,
                MicrosoftAgentFrameworkAiQueryOrchestrationService>();
        }
        else
        {
            services.AddScoped<IAiQueryOrchestrationService, AiQueryOrchestrationService>();
        }

        // Evaluation framework — internal quality infrastructure, no public API surface.
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

        // Growth calculators — existing NET_PROFIT and MONTHLY_SALES.
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
        // PE_TTM / PS_TTM: use vendor-supplied CyclicalWaves ratio snapshot until LATEST_PRICE and
        // SHARES_OUTSTANDING become available from market data, at which point replace with
        // ValuationRatioMetricCalculator(PE_TTM, LATEST_PRICE, TTM_EPS).
        services.AddSingleton<IFinancialMetricCalculator>(_ => new SourceLineItemPassthroughMetricCalculator(
            new MetricCode("PE_TTM"),
            new MetricCode("PE_RATIO")));
        services.AddSingleton<IFinancialMetricCalculator>(_ => new SourceLineItemPassthroughMetricCalculator(
            new MetricCode("PS_TTM"),
            new MetricCode("PS_RATIO")));

        // CodalDB-derived YoY growth calculators (use cumulative ThreeMonths input, shifted −12 months).
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

        // CyclicalWaves data provider
        services
            .AddOptions<CyclicalWavesProviderOptions>()
            .BindConfiguration(CyclicalWavesProviderOptions.SectionName);
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
        services.AddScoped<MockFinancialDataProvider>();
        services.AddScoped<ISymbolDataProvider>(provider =>
            provider.GetRequiredService<CyclicalWavesDataProviderClient>());
        services.AddScoped<IFinancialStatementProvider>(provider =>
            provider.GetRequiredService<CyclicalWavesDataProviderClient>());
        services.AddScoped<IMonthlyProductionSalesProvider>(provider =>
            provider.GetRequiredService<CyclicalWavesDataProviderClient>());
        services.AddScoped<IFinancialDataProviderHealthService>(provider =>
            provider.GetRequiredService<CyclicalWavesDataProviderClient>());

        // NADPCO HTTP API provider foundation. Registered by name for coexisting ingestion routes;
        // it does not replace the default CyclicalWaves provider and is not a market-data source.
        services
            .AddOptions<NadpcoApiProviderOptions>()
            .BindConfiguration(NadpcoApiProviderOptions.SectionName);
        // Spec 053 — per-run Shamsi start boundary override for current-API backfill (scoped; the
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

        // Spec 051 — logical-vendor/physical-source model support: dataset source-priority policy
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
        services.AddScoped<IFinancialPayloadNormalizer, CyclicalWavesSymbolNormalizer>();
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
        services.AddScoped<IDerivedMetricRecalculationPublisher, StoredDerivedMetricRecalculationPublisher>();
        // LineItemMetricInputSource — one per source metric backed by NormalizedFinancialStatementLineItems.
        // NET_PROFIT subsumes the legacy NetProfitMetricInputSource; MonthlyProductionSales uses its own table.
        foreach (var code in new[] { "NET_PROFIT", "REVENUE", "GROSS_PROFIT", "OPERATING_PROFIT",
                                     "EPS", "TOTAL_EQUITY", "FINANCE_COSTS", "INCOME_TAX",
                                     "OPERATING_CASH_FLOW",
                                     // Vendor ratio snapshots — triggers PE_TTM / PS_TTM passthrough.
                                     "PE_RATIO", "PS_RATIO" })
        {
            var captured = new MetricCode(code);
            services.AddScoped<INormalizedMetricInputSource>(sp =>
                new LineItemMetricInputSource(
                    sp.GetRequiredService<FinancialIngestionDbContext>(), captured));
        }
        services.AddScoped<INormalizedMetricInputSource, MonthlySalesMetricInputSource>();
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
        services.AddScoped<IFeatureRecalculationScheduler, FeatureRecalculationScheduler>();
        services.AddScoped<IFeatureComputationProcessor, FeatureComputationProcessor>();
        services.AddScoped<ICyclicalWavesFullSyncService, CyclicalWavesFullSyncService>();
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
        services
            .AddOptions<NadpcoScheduledSyncOptions>()
            .BindConfiguration(NadpcoScheduledSyncOptions.SectionName);
        services.AddScoped<EfCoreNadpcoScheduledSyncRunRepository>();
        services.AddScoped<INadpcoScheduledSyncRunReader>(provider =>
            provider.GetRequiredService<EfCoreNadpcoScheduledSyncRunRepository>());
        services.AddScoped<INadpcoScheduledSyncCoordinator, NadpcoScheduledSyncCoordinator>();
        services.AddSingleton<INadpcoScheduledSyncAlertSink, LoggingNadpcoScheduledSyncAlertSink>();

        // Spec 052 — one-time Noavaran archive import (DataAdmin-triggered; no recurring worker).
        services.AddScoped<EfCoreArchiveImportRunRepository>();
        services.AddScoped<IArchiveImportRunReader>(provider =>
            provider.GetRequiredService<EfCoreArchiveImportRunRepository>());
        services.AddScoped<IArchiveFreezeStateStore, EfCoreArchiveFreezeStateStore>();
        services.AddScoped<IArchiveCoverageReader, EfCoreArchiveCoverageReader>();
        services.AddScoped<IArchiveImportCoordinator, ArchiveImportCoordinator>();

        // Spec 053 — Noavaran current-API ingestion: archive-vs-current gap report + DataAdmin
        // backfill (one-off Shamsi boundary override) + separate current-API health. Reuses the
        // existing current-API scheduled-sync orchestration (single ingestion path).
        services.AddScoped<ICurrentApiGapReader, EfCoreCurrentApiGapReader>();
        services.AddScoped<ICurrentApiBackfillCoordinator, CurrentApiBackfillCoordinator>();
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
        services
            .AddOptions<MarketViewOptions>()
            .BindConfiguration(MarketViewOptions.SectionName);
        services.AddSingleton<IMarketViewCache, MemoryMarketViewCache>();
        services.AddScoped<IWatchlistService, WatchlistService>();
        services.AddScoped<IMarketSummaryService, MarketSummaryService>();
        services.AddScoped<PersistedMarketDataProvider>();
        services.AddScoped<IMarketDataProvider>(provider =>
            provider.GetRequiredService<IOptions<StockMarketDbProviderOptions>>().Value.UsePersistedMarketQuotes
                ? provider.GetRequiredService<PersistedMarketDataProvider>()
                : provider.GetRequiredService<MockFinancialDataProvider>());

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
            .BindConfiguration(RabbitMqDataSyncOptions.SectionName);
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
