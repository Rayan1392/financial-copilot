using FinancialCopilot.API.Middleware;
using FinancialCopilot.API.Security;
using FinancialCopilot.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, lc) => lc.ReadFrom.Configuration(ctx.Configuration));

builder.Services.AddControllers();
builder.Services.AddHealthChecks();
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
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
});

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapControllers();

app.Run();

public partial class Program;
