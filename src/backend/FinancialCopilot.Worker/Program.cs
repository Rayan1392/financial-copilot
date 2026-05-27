using FinancialCopilot.Infrastructure;
using FinancialCopilot.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddFinancialCopilotInfrastructure(builder.Configuration);
builder.Services
    .AddOptions<BillingMaintenanceOptions>()
    .BindConfiguration(BillingMaintenanceOptions.SectionName)
    .Validate(
        options => options.IntervalSeconds > 0 && options.BatchSize > 0,
        "Billing maintenance settings must be positive.")
    .ValidateOnStart();
builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<DataSyncConsumerWorker>();
builder.Services.AddHostedService<FeatureComputationConsumerWorker>();

var host = builder.Build();
host.Run();
