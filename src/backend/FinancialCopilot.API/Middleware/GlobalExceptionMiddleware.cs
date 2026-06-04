using Microsoft.AspNetCore.Mvc;
using FinancialCopilot.Application.Administration;
using FinancialCopilot.Billing;

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

            var problemDetails = CreateProblemDetails(exception);

            problemDetails.Extensions["traceId"] = context.TraceIdentifier;
            problemDetails.Extensions["correlationId"] = context.TraceIdentifier;

            context.Response.StatusCode = problemDetails.Status ?? StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(problemDetails);
        }
    }

    private static ProblemDetails CreateProblemDetails(Exception exception) =>
        exception switch
        {
            AdminManagementException admin => new ProblemDetails
            {
                Type = $"https://financialcopilot/errors/{admin.ErrorCode}",
                Title = admin.ErrorCode,
                Status = admin.StatusCode,
                Detail = admin.Message
            },
            InsufficientCreditException => new ProblemDetails
            {
                Type = "https://financialcopilot/errors/insufficient-credit",
                Title = "insufficient-credit",
                Status = StatusCodes.Status402PaymentRequired,
                Detail = "Available spending capacity is insufficient."
            },
            Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException => new ProblemDetails
            {
                Type = "https://financialcopilot/errors/concurrency-conflict",
                Title = "concurrency-conflict",
                Status = StatusCodes.Status409Conflict,
                Detail = "The resource changed while the request was being processed."
            },
            _ => new ProblemDetails
            {
                Type = "https://financialcopilot/errors/internal-server-error",
                Title = "An unexpected error occurred.",
                Status = StatusCodes.Status500InternalServerError,
                Detail = "The request could not be completed."
            }
        };
}
