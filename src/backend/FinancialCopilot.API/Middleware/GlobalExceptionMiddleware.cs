using Microsoft.AspNetCore.Mvc;

namespace FinancialCopilot.API.Middleware;

public sealed class GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled exception processing request.");

            var problemDetails = new ProblemDetails
            {
                Type = "https://financialcopilot/errors/internal-server-error",
                Title = "An unexpected error occurred.",
                Status = StatusCodes.Status500InternalServerError,
                Detail = "The request could not be completed."
            };

            problemDetails.Extensions["traceId"] = context.TraceIdentifier;
            problemDetails.Extensions["correlationId"] = context.TraceIdentifier;

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(problemDetails);
        }
    }
}
