using FinancialCopilot.API.Middleware;
using FinancialCopilot.API.Security;
using FinancialCopilot.API;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Infrastructure;
using FinancialCopilot.Infrastructure.Authentication.TahlilApp;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using Serilog;
using System.Text;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

//var apiKey = "dev-telegram-worker-key";

//var hash = Convert.ToHexString(
//    System.Security.Cryptography.SHA256.HashData(
//        System.Text.Encoding.UTF8.GetBytes(apiKey)
//        )
//  );

//Environment.SetEnvironmentVariable(
//     "Authentication__ApiKeys__Clients__0__ClientId",
//    "22222222-2222-2222-2222-222222222222", EnvironmentVariableTarget.User
//);

//Environment.SetEnvironmentVariable(
//    "Authentication__ApiKeys__Clients__0__TenantId",
//    "11111111-1111-1111-1111-111111111111",
//    EnvironmentVariableTarget.User
//);

//Environment.SetEnvironmentVariable(
//     "Authentication__ApiKeys__Clients__0__Name",
//    "Telegram Dev Poller",
//    EnvironmentVariableTarget.User
//);

//Environment.SetEnvironmentVariable(
//    "Authentication__ApiKeys__Clients__0__KeySha256",
//    hash,
//    EnvironmentVariableTarget.User
//);

//Environment.SetEnvironmentVariable(
//  "Authentication__ApiKeys__Clients__0__IsActive",
//    "true",
//    EnvironmentVariableTarget.User
//);



var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    // Preserve the tracked appsettings.Development.json while allowing local secrets.
    // Insert before environment variables and command line so standard ASP.NET Core
    // precedence remains: local JSON < environment variables < command line.
    var environmentVariablesIndex = builder.Configuration.Sources
        .ToList()
        .FindIndex(source =>
            source is Microsoft.Extensions.Configuration.EnvironmentVariables.EnvironmentVariablesConfigurationSource environmentSource &&
            string.IsNullOrEmpty(environmentSource.Prefix));
    builder.Configuration.Sources.Insert(
        environmentVariablesIndex >= 0 ? environmentVariablesIndex : builder.Configuration.Sources.Count,
        new Microsoft.Extensions.Configuration.Json.JsonConfigurationSource
        {
            FileProvider = builder.Environment.ContentRootFileProvider,
            Path = "appsettings.Development.Local.json",
            Optional = true,
            ReloadOnChange = true
        });
}

builder.Host.UseSerilog((ctx, lc) => lc.ReadFrom.Configuration(ctx.Configuration));

builder.Services.AddControllers();
builder.Services.AddHealthChecks();
builder.Services.AddCors(options =>
{
    var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
        ?? ["http://localhost:5173"];
    if (builder.Environment.IsDevelopment())
    {
        // Vite moves the app to 5174 when another project already occupies 5173.
        allowedOrigins = allowedOrigins
            .Append("http://localhost:5174")
            .Append("http://127.0.0.1:5174")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    options.AddPolicy("Frontend", policy =>
        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        var publicServerUrl = builder.Configuration["OpenApi:ServerUrl"];
        if (!string.IsNullOrWhiteSpace(publicServerUrl))
        {
            document.Servers = [new OpenApiServer { Url = publicServerUrl }];
        }

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Enter your JWT (without the 'Bearer ' prefix)"
        };
        return Task.CompletedTask;
    });
    options.AddOperationTransformer((operation, context, _) =>
    {
        var hasAuthorize = context.Description.ActionDescriptor.EndpointMetadata
            .OfType<Microsoft.AspNetCore.Authorization.IAuthorizeData>().Any();
        if (!hasAuthorize) return Task.CompletedTask;
        var requirement = new OpenApiSecurityRequirement();
        requirement[new OpenApiSecuritySchemeReference("Bearer", context.Document)] = [];
        operation.Security = [requirement];
        return Task.CompletedTask;
    });
});
builder.Services.AddFinancialCopilotSecurity(builder.Configuration);
builder.Services.AddFinancialCopilotInfrastructure(builder.Configuration);
builder.Services.AddHostedService<TelegramFinancialStatementReconciliationWorker>();
builder.Services.AddTahlilAppIdentity(builder.Configuration);
builder.Services
    .AddOptions<ForwardedHeadersSecurityOptions>()
    .BindConfiguration(ForwardedHeadersSecurityOptions.SectionName)
    .Validate(options => options.IsValid(out _), "Forwarded header trust configuration is invalid.")
    .ValidateOnStart();
var forwardedHeadersSecurity = builder.Configuration
    .GetSection(ForwardedHeadersSecurityOptions.SectionName)
    .Get<ForwardedHeadersSecurityOptions>() ?? new ForwardedHeadersSecurityOptions();
if (!forwardedHeadersSecurity.IsValid(out var forwardedHeadersError))
{
    throw new InvalidOperationException(forwardedHeadersError);
}
builder.Services.Configure<ForwardedHeadersOptions>(forwardedHeadersSecurity.ApplyTo);
builder.Services
    .AddOptions<PublicAuthenticationRateLimitOptions>()
    .BindConfiguration(PublicAuthenticationRateLimitOptions.SectionName)
    .Validate(options => options.PermitLimit is > 0 and <= 1000 && options.WindowSeconds is > 0 and <= 3600,
        "Public authentication rate limits must be positive and bounded.")
    .ValidateOnStart();
builder.Services
    .AddOptions<AuthenticatedActorRateLimitOptions>()
    .BindConfiguration(AuthenticatedActorRateLimitOptions.SectionName)
    .Validate(
        options => options.PermitLimit > 0 && options.WindowSeconds > 0,
        "Authenticated actor rate limit settings must be positive.")
    .ValidateOnStart();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(
        RateLimitPolicies.AuthenticatedActor,
        context => RateLimitPolicies.Partition(
            context,
            context.RequestServices.GetRequiredService<IOptionsSnapshot<AuthenticatedActorRateLimitOptions>>().Value));
    options.AddPolicy(
        RateLimitPolicies.PublicAuthentication,
        context => RateLimitPolicies.PublicIp(
            context,
            context.RequestServices.GetRequiredService<IOptionsSnapshot<PublicAuthenticationRateLimitOptions>>().Value));
});

var app = builder.Build();

var activeAiProvider = app.Services
    .GetRequiredService<IAiModelProviderDiagnostics>()
    .GetActiveProvider(Guid.Empty);
app.Logger.LogInformation(
    "AI model provider initialized. ConfiguredProvider: {ConfiguredProvider}; ActiveProvider: {ActiveProvider}; Model: {Model}; Available: {Available}",
    activeAiProvider.ConfiguredProviderKey ?? "auto",
    activeAiProvider.ProviderKey ?? "none",
    activeAiProvider.ModelKey ?? "none",
    activeAiProvider.Available);

if (app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
{
    await app.ApplyPendingDatabaseMigrationsAsync();
}

if (forwardedHeadersSecurity.Enabled)
{
    app.UseForwardedHeaders();
}
app.UseSerilogRequestLogging();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("OpenApi:Enabled"))
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseAuthentication();
app.UseCors("Frontend");
app.UseRateLimiter();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapGet("/api/ui/v1/config", (IConfiguration configuration) =>
    Results.Ok(new
    {
        quarterlyFinancialTable = new
        {
            showChart = configuration.GetValue("QuarterlyFinancialTable:ShowChart", true)
        }
    }));
app.MapControllers();

app.Run();

public partial class Program;
