using FinancialCopilot.Infrastructure;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Authentication;
using FinancialCopilot.Infrastructure.Authentication.TahlilApp;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;
using FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;
using FinancialCopilot.Worker;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog((_, lc) => lc.ReadFrom.Configuration(builder.Configuration));

var bankingIndustryGroupId = builder.Configuration.GetValue<Guid>(BankingFinancialReportsConfiguration.ConfigurationKey);
if (bankingIndustryGroupId == Guid.Empty)
{
    throw new InvalidOperationException($"{BankingFinancialReportsConfiguration.ConfigurationKey} must be configured with a non-empty GUID.");
}

builder.Services.AddSingleton(new BankingFinancialReportsConfiguration(bankingIndustryGroupId));
builder.Services.AddFinancialCopilotInfrastructure(builder.Configuration);
builder.Services.AddTahlilAppIdentity(builder.Configuration);
builder.Services
    .AddOptions<BillingMaintenanceOptions>()
    .BindConfiguration(BillingMaintenanceOptions.SectionName)
    .Validate(
        options => options.IntervalSeconds > 0 && options.BatchSize > 0,
        "Billing maintenance settings must be positive.")
    .ValidateOnStart();
builder.Services
    .AddOptions<DerivedMetricRecalculationOptions>()
    .BindConfiguration(DerivedMetricRecalculationOptions.SectionName)
    .Validate(
        options => options.IntervalSeconds > 0 && options.BatchSize > 0,
        "Derived-metric recalculation settings must be positive.")
    .ValidateOnStart();
builder.Services
    .AddOptions<ConditionalTrackerEvaluationOptions>()
    .BindConfiguration(ConditionalTrackerEvaluationOptions.SectionName)
    .Validate(
        options => options.IntervalSeconds > 0 && options.BatchSize is > 0 and <= 1000,
        "Conditional tracker evaluation settings must be positive and bounded.")
    .ValidateOnStart();
builder.Services
    .AddOptions<FinancialCopilot.Infrastructure.Financial.Radar.RadarOptions>()
    .BindConfiguration(FinancialCopilot.Infrastructure.Financial.Radar.RadarOptions.SectionName)
    .Validate(
        options => options.EvaluationCadenceSeconds is >= 10 and <= 3600 &&
                   options.BatchSize is > 0 and <= 1000 &&
                   options.MaximumEventsPerProfile is > 0 and <= 2000 &&
                   options.InitialLookbackHours is > 0 and <= 168 &&
                   options.LeaseSeconds is >= 30 and <= 600 &&
                   options.RetryCount is > 0 and <= 10,
        "Radar evaluation settings must be positive and bounded.")
    .ValidateOnStart();
builder.Services
    .AddOptions<MarketMicrostructureDetectionWorkerOptions>()
    .BindConfiguration(MarketMicrostructureDetectionWorkerOptions.SectionName)
    .Validate(
        options => options.IntervalSeconds > 0 && options.LookbackDays is > 0 and <= 90 && options.RetryCount is > 0 and <= 10,
        "Market microstructure detection worker settings must be positive and bounded.")
    .ValidateOnStart();
builder.Services
    .AddOptions<MarketInsightGenerationWorkerOptions>()
    .BindConfiguration(MarketInsightGenerationWorkerOptions.SectionName)
    .Validate(
        options => options.IntervalSeconds > 0 && options.LookbackDays is > 0 and <= 90 && options.RetryCount is > 0 and <= 10,
        "Market insight generation worker settings must be positive and bounded.")
    .ValidateOnStart();
builder.Services
    .AddOptions<TelegramMembershipRevalidationOptions>()
    .BindConfiguration(TelegramMembershipRevalidationOptions.SectionName)
    .Validate(
        options =>
            options.CadenceSeconds > 0 &&
            options.BatchSize > 0 &&
            options.MaxConcurrency > 0 &&
            options.LeaseSeconds > 0 &&
            options.RetryCount > 0 &&
            options.InitialBackoffSeconds > 0 &&
            options.MaxBackoffSeconds >= options.InitialBackoffSeconds,
        "Telegram membership revalidation settings must be positive.")
    .ValidateOnStart();
builder.Services
    .AddOptions<StockMarketDbPollingOptions>()
    .BindConfiguration(StockMarketDbPollingOptions.SectionName);
builder.Services.AddHttpClient();
builder.Services.AddHostedService<TelegramMembershipRevalidationWorker>();
builder.Services.AddHostedService<NotificationDispatchWorker>();
builder.Services.AddHostedService<AlertHistoryHandoffWorker>();
builder.Services
    .AddOptions<NadpcoScheduledSyncOptions>()
    .BindConfiguration(NadpcoScheduledSyncOptions.SectionName)
    .Validate(
        options =>
            options.CadenceSeconds > 0 &&
            options.BatchSize > 0 &&
            options.MaxConcurrency > 0 &&
            options.RetryCount >= 0 &&
            options.RetryDelaySeconds >= 0 &&
            options.MaxRunDurationSeconds > 0 &&
            options.LockLeaseSeconds > 0,
        "NADPCO scheduled sync settings must be positive.")
    .ValidateOnStart();
builder.Services
    .AddOptions<MetricAliasLearningWorkerOptions>()
    .BindConfiguration(MetricAliasLearningWorkerOptions.SectionName)
    .Validate(
        options => options.IntervalSeconds > 0 && options.BatchSize > 0,
        "MetricAliasLearning worker settings must be positive.")
    .ValidateOnStart();
builder.Services
    .AddOptions<TsetmcPollingOptions>()
    .BindConfiguration(TsetmcPollingOptions.SectionName)
    .Validate(options => options.HasValidIntervals,
        "TsetmcPolling intervals must be positive; option intervals must be at least 10 seconds.")
    .ValidateOnStart();
builder.Services
    .AddOptions<ComprehensiveAnalysisBlogOptions>()
    .BindConfiguration(ComprehensiveAnalysisBlogOptions.SectionName);
builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<DataSyncConsumerWorker>();
builder.Services.AddHostedService<MonthlyActivityBackfillOutboxWorker>();
builder.Services.AddHostedService<FeatureComputationConsumerWorker>();
builder.Services.AddHostedService<DerivedMetricRecalculationWorker>();
builder.Services.AddHostedService<ConditionalTrackerEvaluationWorker>();
builder.Services.AddHostedService<RadarEvaluationWorker>();
builder.Services.AddHostedService<MarketMicrostructureDetectionWorker>();
builder.Services.AddHostedService<MarketInsightGenerationWorker>();
builder.Services.AddHostedService<MarketPulseWorker>();
builder.Services.AddHostedService<MarketReportWorker>();
builder.Services.AddHostedService<StockMarketDbPollingWorker>();
builder.Services.AddHostedService<NadpcoScheduledSyncWorker>();
builder.Services.AddHostedService<MetricAliasLearningWorker>();
builder.Services.AddHostedService<TsetmcPollingWorker>();
// Feature 148 retired the legacy CyclicalWaves integration: ComprehensiveAnalysisDailySyncWorker,
// CyclicalWavesDataAcquisitionWorker remains unhosted. This calculation worker
// is restored only when configured to consume the independent v2 data.
builder.Services.AddHostedService<CyclicalWavesV2SyncWorker>();
if (builder.Configuration.GetValue<bool>("IndustryRelativeValuation:CyclicalWavesV2Enabled"))
    builder.Services.AddHostedService<IndustryRelativeValuationCalculationWorker>();

var host = builder.Build();
host.Run();
