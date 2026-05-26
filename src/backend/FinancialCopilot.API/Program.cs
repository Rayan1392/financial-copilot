using FinancialCopilot.API.Middleware;
using FinancialCopilot.API.Security;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
    options.UseUtcTimestamp = true;
});

builder.Services.AddControllers();
builder.Services.AddHealthChecks();
builder.Services.AddOpenApi();
builder.Services.AddFinancialCopilotSecurity(builder.Configuration);
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

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapControllers();

app.Run();

public partial class Program;
