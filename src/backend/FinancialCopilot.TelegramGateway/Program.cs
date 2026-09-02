using FinancialCopilot.TelegramGateway;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddHttpClient();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("telegram-gateway", context => RateLimitPartition.GetFixedWindowLimiter(
        $"{(context.Request.Headers.TryGetValue("X-Gateway-Id", out var gatewayId) ? gatewayId.ToString() : "anonymous")}:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = Math.Max(1, context.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<TelegramGatewaySettings>>().Value.RateLimitPermitLimit),
            Window = TimeSpan.FromSeconds(Math.Max(1, context.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<TelegramGatewaySettings>>().Value.RateLimitWindowSeconds)),
            QueueLimit = Math.Max(0, context.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<TelegramGatewaySettings>>().Value.RateLimitQueueLimit),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        }));
});
builder.Services.AddOptions<TelegramGatewaySettings>()
    .BindConfiguration(TelegramGatewaySettings.SectionName)
    .Validate(settings => settings.IsValidForStartup(),
        "Enabled polling requires a bot token, HTTPS primary API URL, API key, valid polling/timeouts, and absolute durable state paths. Inbound service identity and secret must be configured together.")
    .ValidateOnStart();
builder.Services.AddSingleton<GatewayRequestAuthenticator>();
builder.Services.AddSingleton<GatewayReplayNonceStore>();
builder.Services.AddSingleton<GatewayIdempotencyStore>();
builder.Services.AddSingleton<TelegramApiClient>();
builder.Services.AddSingleton<PrimaryApiClient>();
builder.Services.AddHealthChecks()
    .AddCheck<PrimaryApiClient>("primary-api-authentication");
builder.Services.AddHostedService<TelegramGatewayPollingWorker>();

var app = builder.Build();
var gatewaySettings = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<TelegramGatewaySettings>>().Value;
app.Use(async (context, next) =>
{
    if (gatewaySettings.RequireHttps && context.Request.Path.StartsWithSegments("/v1", StringComparison.OrdinalIgnoreCase) && !context.Request.IsHttps)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("HTTPS is required.");
        return;
    }
    await next();
});
app.UseRateLimiter();
app.MapHealthChecks("/health");
if (gatewaySettings.HasInboundApiCredentials)
{
    app.MapControllers().RequireRateLimiting("telegram-gateway");
}
app.Run();
