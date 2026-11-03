using FinancialCopilot.Application.Authentication.Profile;
using FinancialCopilot.Application.Authentication.TahlilApp;
using FinancialCopilot.Infrastructure.Authentication.Profile;
using FinancialCopilot.Infrastructure.Billing.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Authentication.TahlilApp;

/// <summary>Composition root for TahlilApp launch identity (Design §22.3). Concrete wiring lives only here.</summary>
public static class TahlilAppServiceCollectionExtensions
{
    public static IServiceCollection AddTahlilAppIdentity(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<TahlilAppOptions>()
            .Bind(configuration.GetSection(TahlilAppOptions.SectionName))
            .PostConfigure(options => options.DataProtectionKeyRingPath = configuration["DataProtection:KeyRingPath"] ?? string.Empty)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<TahlilAppOptions>, TahlilAppOptionsValidator>();

        // Persistent, fixed-name key ring (Design §17.1). Keys survive restarts and redeploys when a path is configured.
        var dataProtection = services.AddDataProtection().SetApplicationName("FinancialCopilot");
        var keyRingPath = configuration["DataProtection:KeyRingPath"];
        if (!string.IsNullOrWhiteSpace(keyRingPath))
        {
            dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
        }

        services.AddSingleton<TahlilAppProfileProtector>();
        services.AddScoped<ITahlilAppLaunchAttemptStore, TahlilAppLaunchAttemptStore>();
        services.AddScoped<ITahlilAppAccountProvisioner, TahlilAppAccountProvisioner>();
        services.AddScoped<ITahlilAppSessionIssuer, TahlilAppSessionIssuer>();
        services.AddScoped<ITahlilAppSubscriptionSynchronizer, ExternalSubscriptionSynchronizer>();
        services.AddScoped<IOwnProfileStore, OwnProfileStore>();
        services.AddScoped<OwnProfileUseCase>();
        services.AddScoped<TahlilAppSyncStateStore>();
        services.AddScoped<ITahlilAppEntitlementStateStore>(provider => provider.GetRequiredService<TahlilAppSyncStateStore>());
        services.AddScoped<ITahlilAppSyncStateRecorder>(provider => provider.GetRequiredService<TahlilAppSyncStateStore>());
        services.AddScoped<ITahlilAppEntitlementRefresher, TahlilAppEntitlementRefresher>();

        // Revalidation runs only when the feature and the revalidation switch are both on (Design §11, default off).
        if (configuration.GetValue<bool>("Authentication:TahlilApp:Enabled") &&
            configuration.GetValue<bool>("Authentication:TahlilApp:Revalidation:Enabled"))
        {
            services.AddHostedService<TahlilAppEntitlementRevalidationWorker>();
        }
        services.AddScoped<IOwnProfileStore, OwnProfileStore>();
        services.AddScoped<OwnProfileUseCase>();

        // Dedicated client: no resilience handler (no retry) and no redirects, because the exchange is not idempotent.
        services
            .AddHttpClient<ITahlilAppIdentityGateway, TahlilAppIdentityGateway>()
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });

        services.AddScoped<TahlilAppLaunchUseCase>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<TahlilAppOptions>>().Value;
            return new TahlilAppLaunchUseCase(
                provider.GetRequiredService<ITahlilAppIdentityGateway>(),
                provider.GetRequiredService<ITahlilAppLaunchAttemptStore>(),
                provider.GetRequiredService<ITahlilAppAccountProvisioner>(),
                provider.GetRequiredService<ITahlilAppSubscriptionSynchronizer>(),
                provider.GetRequiredService<ITahlilAppSyncStateRecorder>(),
                provider.GetRequiredService<ITahlilAppSessionIssuer>(),
                new TahlilAppLaunchPolicy(
                    options.Enabled,
                    new Dictionary<string, string>(options.PlanMap, StringComparer.Ordinal),
                    TimeSpan.FromMinutes(options.Revalidation.StatusTtlMinutes)),
                provider.GetRequiredService<TimeProvider>());
        });

        return services;
    }
}
