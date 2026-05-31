using FinancialCopilot.Infrastructure;
using FinancialCopilot.Worker;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog((_, lc) => lc.ReadFrom.Configuration(builder.Configuration));

builder.Services.AddFinancialCopilotInfrastructure(builder.Configuration);
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
builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<DataSyncConsumerWorker>();
builder.Services.AddHostedService<FeatureComputationConsumerWorker>();
builder.Services.AddHostedService<DerivedMetricRecalculationWorker>();

var host = builder.Build();
host.Run();
