using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using FinancialCopilot.Application.Administration;
using FinancialCopilot.Application.AI.ModelProviders;
using FinancialCopilot.Application.AI.Orchestration;
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

            var languageHint = context.Request.Headers.AcceptLanguage.ToString();
            var outcome = AiDialogueOutcomePolicy.FromException(languageHint, exception);
            problemDetails.Extensions["outcome"] = outcome.Outcome.ToString();
            problemDetails.Extensions["outcomeReasonCode"] = outcome.ReasonCode;
            problemDetails.Extensions["replyLanguage"] = outcome.ReplyLanguage;
            Activity.Current?.SetTag("workflow.outcome", outcome.Outcome.ToString());
            Activity.Current?.SetTag("workflow.outcome_reason", outcome.ReasonCode);
            Activity.Current?.SetTag("workflow.reply_language", outcome.ReplyLanguage);

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
            AiModelProviderException { Status: AiExecutionStatus.TimedOut } => new ProblemDetails
            {
                Type = "https://financialcopilot/errors/provider-timeout",
                Title = "temporarily-unavailable",
                Status = StatusCodes.Status503ServiceUnavailable,
                Detail = "The AI capability is temporarily unavailable. Please try again shortly."
            },
            AiModelProviderException { Status: AiExecutionStatus.CapabilityUnavailable or AiExecutionStatus.RuntimeUnavailable } => new ProblemDetails
            {
                Type = "https://financialcopilot/errors/provider-unavailable",
                Title = "temporarily-unavailable",
                Status = StatusCodes.Status503ServiceUnavailable,
                Detail = "The AI capability is temporarily unavailable. Please try again shortly."
            },
            AiModelProviderException { Status: AiExecutionStatus.InvalidStructuredOutput } => new ProblemDetails
            {
                Type = "https://financialcopilot/errors/response-validation-failed",
                Title = "response-validation-failed",
                Status = StatusCodes.Status502BadGateway,
                Detail = "The AI response could not be validated. Please try again."
            },
            AiModelProviderException => new ProblemDetails
            {
                Type = "https://financialcopilot/errors/provider-failure",
                Title = "provider-failure",
                Status = StatusCodes.Status502BadGateway,
                Detail = "The AI capability could not complete the request. Please try again."
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
