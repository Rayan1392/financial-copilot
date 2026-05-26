namespace FinancialCopilot.API.Middleware;

public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-Id";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = GetCorrelationId(context);

        context.TraceIdentifier = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await next(context);
        }
    }

    private static string GetCorrelationId(HttpContext context)
    {
        var incomingCorrelationId = context.Request.Headers[HeaderName].FirstOrDefault();

        return string.IsNullOrWhiteSpace(incomingCorrelationId)
            ? Guid.NewGuid().ToString()
            : incomingCorrelationId;
    }
}
